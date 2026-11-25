using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using FluentMigrator.Runner;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Npgsql;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Providers.Migrations.Migrations;
using Resgrid.Providers.MigrationsPg.Migrations;
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries;
using Resgrid.Repositories.DataRepository.Servers.PostgreSql;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;

namespace Resgrid.Tests.Repositories
{
	/// <summary>
	/// Real-database proof for Training T2 (courses and assessments) on both engines: the whole migration chain, M0359/M0360 re-run as
	/// no-ops and rolled back and forward again, and the LMS repositories' own SQL: department-or-system reads (the Academy is
	/// DepartmentId 0), the one-open-enrollment key, row-version claims, the reminder claim with and without a previous value (the
	/// PostgreSQL untyped-null trap), the progress and attendee unique races, the entitlement stamp upsert, the media queue claim
	/// (SKIP LOCKED / READPAST), approval decisions and the department cleanup. Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION /
	/// RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class TrainingT2RepositoryDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "training_t2_";
		private const string UserA = "7b2e0c74-8f5a-4f7d-9e3c-3d4e5f6a7b82";
		private const string UserB = "8c3f1d85-9a6b-4a8e-8f4d-4e5f6a7b8c93";

		private DatabaseTypes _previousType;
		private string _previousConnection, _master, _connection, _database;
		private int _department, _otherDepartment;
		private LmsCourse _course, _system;
		private LmsCourseVersion _version, _systemVersion;
		private LmsLesson _lesson, _quiz;
		private LmsQuestionBank _bank;

		private bool IsPostgres => type == DatabaseTypes.Postgres;

		private DbConnection Connect(string connection) => IsPostgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);

		private string T(string name) => IsPostgres ? name.ToLowerInvariant() : "[" + name + "]";

		private TRepo Repository<TRepo>(Func<IConnectionProvider, SqlConfiguration, IUnitOfWork, IQueryFactory, TRepo> build)
		{
			var connections = new Mock<IConnectionProvider>();
			connections.Setup(c => c.Create()).Returns(() => Connect(_connection));
			SqlConfiguration configuration = IsPostgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration();
			var queries = new ConcurrentDictionary<Type, IQuery>();
			foreach (var query in new[] { typeof(Resgrid.Repositories.DataRepository.Queries.Common.InsertQuery), typeof(Resgrid.Repositories.DataRepository.Queries.Common.UpdateQuery),
				typeof(Resgrid.Repositories.DataRepository.Queries.Common.DeleteQuery), typeof(Resgrid.Repositories.DataRepository.Queries.Common.DeleteMultipleQuery),
				typeof(Resgrid.Repositories.DataRepository.Queries.Common.SelectAllQuery), typeof(Resgrid.Repositories.DataRepository.Queries.Common.SelectByIdQuery),
				typeof(Resgrid.Repositories.DataRepository.Queries.Common.SelectByDepartmentIdQuery), typeof(Resgrid.Repositories.DataRepository.Queries.Common.SelectByUserIdQuery) })
				queries[query] = (IQuery)Activator.CreateInstance(query, configuration);
			var list = new Mock<IQueryList>();
			list.Setup(l => l.RetrieveQueryList()).Returns(queries);
			return build(connections.Object, configuration, Mock.Of<IUnitOfWork>(), new QueryFactory(list.Object));
		}

		private ServiceProvider Runner() => new ServiceCollection().AddFluentMigratorCore().ConfigureRunner(r =>
		{
			if (IsPostgres) r.AddPostgres11_0().WithGlobalConnectionString(_connection).ScanIn(typeof(M0001_InitialMigrationPg).Assembly).For.All();
			else r.AddSqlServer().WithGlobalConnectionString(_connection).ScanIn(typeof(M0001_InitialMigration).Assembly).For.All();
		}).BuildServiceProvider(false);

		[OneTimeSetUp]
		public async Task Migrate_an_isolated_database()
		{
			_master = Environment.GetEnvironmentVariable(IsPostgres ? "RESGRID_ADP_POSTGRES_TEST_CONNECTION" : "RESGRID_ADP_SQLSERVER_TEST_CONNECTION");
			if (string.IsNullOrWhiteSpace(_master)) Assert.Ignore("Set a test connection to run real database checks.");

			if (IsPostgres) AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
			_previousType = DataConfig.DatabaseType;
			_previousConnection = DataConfig.CoreConnectionString;
			DataConfig.DatabaseType = type;

			_database = Prefix + Guid.NewGuid().ToString("N");
			await using (var master = Connect(_master))
				await master.ExecuteAsync("CREATE DATABASE " + _database);
			_connection = IsPostgres
				? new NpgsqlConnectionStringBuilder(_master) { Database = _database }.ConnectionString
				: new SqlConnectionStringBuilder(_master) { InitialCatalog = _database }.ConnectionString;
			DataConfig.CoreConnectionString = _connection;

			using (var runner = Runner())
			using (var scope = runner.CreateScope())
				scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();

			await using var database = Connect(_connection);
			await database.OpenAsync();
			if (database is NpgsqlConnection postgres)
				await postgres.ReloadTypesAsync();
			// The LMS tables carry DepartmentId without a key (system content is department 0): any two ids serve.
			_department = 4101;
			_otherDepartment = 4102;
		}

		[OneTimeTearDown]
		public async Task Remove_only_this_fixture_database()
		{
			if (_database == null) return;
			DataConfig.DatabaseType = _previousType;
			DataConfig.CoreConnectionString = _previousConnection;
			if (!_database.StartsWith(Prefix, StringComparison.Ordinal) || !Guid.TryParseExact(_database.Substring(Prefix.Length), "N", out _))
				throw new InvalidOperationException("Unexpected test database name.");
			if (IsPostgres) NpgsqlConnection.ClearAllPools(); else SqlConnection.ClearAllPools();
			await using var master = Connect(_master);
			await master.ExecuteAsync(IsPostgres ? $"DROP DATABASE IF EXISTS {_database} WITH (FORCE)" : $"ALTER DATABASE {_database} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {_database}");
		}

		private async Task<int> ScalarAsync(string sql, object parameters = null)
		{
			await using var database = Connect(_connection);
			return await database.ExecuteScalarAsync<int>(sql, parameters);
		}

		private static readonly string[] LmsTables =
		{
			"LmsCourses", "LmsCourseVersions", "LmsCourseVersionApprovals", "LmsCourseAudiences", "LmsCourseOwners", "LmsCourseModules", "LmsMediaAssets",
			"LmsQuestionBanks", "LmsQuestions", "LmsLessons", "LmsEnrollments", "LmsLessonFiles", "LmsCrewViewings", "LmsLessonProgress", "LmsAssessmentAttempts",
			"LmsSurveyResponses", "LmsCrewViewingAttendees", "LmsPathAssignments", "LmsDepartmentStates"
		};

		private Task<int> CountLmsTablesAsync() => ScalarAsync(IsPostgres
			? "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = ANY(@Names)"
			: "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME IN @Names",
			new { Names = IsPostgres ? LmsTables.Select(t => t.ToLowerInvariant()).ToArray() : LmsTables });

		[Test, Order(1)]
		public async Task T2_migrations_rerun_as_no_ops_roll_back_and_apply_again()
		{
			(await CountLmsTablesAsync()).Should().Be(LmsTables.Length);
			var version = IsPostgres ? "\"VersionInfo\"" : "[VersionInfo]";
			var versionColumn = IsPostgres ? "\"Version\"" : "[Version]";
			await using (var database = Connect(_connection))
				await database.ExecuteAsync($"DELETE FROM {version} WHERE {versionColumn} >= 359");

			using (var runner = Runner())
			using (var scope = runner.CreateScope())
			{
				var migrations = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
				migrations.MigrateUp();
				(await CountLmsTablesAsync()).Should().Be(LmsTables.Length, "Up over the existing schema is a no-op");

				migrations.MigrateDown(358);
				(await CountLmsTablesAsync()).Should().Be(0, "Down removes every T2 table");

				migrations.MigrateUp();
			}

			(await CountLmsTablesAsync()).Should().Be(LmsTables.Length);
		}

		[Test, Order(2)]
		public async Task Courses_read_department_or_system_rows_and_versions_claim_their_row_version()
		{
			var courses = Repository((c, s, u, q) => new LmsCoursesRepository(c, s, u, q));
			var versions = Repository((c, s, u, q) => new LmsCourseVersionsRepository(c, s, u, q));
			var now = DateTime.UtcNow;

			_course = await courses.InsertAsync(new LmsCourse { DepartmentId = _department, Title = "Pump ops", Status = (int)ProtocolStatuses.Published, IsSelfEnrollable = true, CreatedOn = now }, default, true);
			_system = await courses.InsertAsync(new LmsCourse
			{
				DepartmentId = 0, PublisherKind = (int)LmsPublisherKinds.System, SystemKey = "academy:00-start", ManifestVersion = 1, Title = "Start", Status = (int)ProtocolStatuses.Published, CreatedOn = now
			}, default, true);
			await courses.InsertAsync(new LmsCourse { DepartmentId = _otherDepartment, Title = "Theirs", CreatedOn = now }, default, true);

			(await courses.GetForDepartmentAsync(_department)).Select(c => c.Title).Should().Equal("Pump ops");
			(await courses.GetSystemCoursesAsync()).Should().ContainSingle(c => c.LmsCourseId == _system.LmsCourseId);
			(await courses.GetVisibleByIdAsync(_department, _system.LmsCourseId)).Should().NotBeNull("the Academy is visible to every department");
			(await courses.GetVisibleByIdsAsync(_otherDepartment, new[] { _course.LmsCourseId, _system.LmsCourseId })).Select(c => c.LmsCourseId).Should().Equal(_system.LmsCourseId);
			(await courses.GetBySystemKeyAsync("ACADEMY:00-START")).LmsCourseId.Should().Be(_system.LmsCourseId);

			Func<Task> duplicateKey = () => courses.InsertAsync(new LmsCourse { DepartmentId = 0, PublisherKind = 1, SystemKey = "academy:00-start", Title = "Again", CreatedOn = now }, default, true);
			await duplicateKey.Should().ThrowAsync<DbException>("the Academy seed is keyed by SystemKey");

			_version = await versions.InsertAsync(new LmsCourseVersion
			{
				DepartmentId = _department, LmsCourseId = _course.LmsCourseId, Major = 1, Status = (int)ProtocolVersionStatuses.Published, Title = "Pump ops", Hours = 1.5m, CreatedOn = now
			}, default, true);
			_systemVersion = await versions.InsertAsync(new LmsCourseVersion
			{
				DepartmentId = 0, LmsCourseId = _system.LmsCourseId, Major = 1, Status = (int)ProtocolVersionStatuses.Published, Title = "Start", Hours = 0.15m, CreatedOn = now
			}, default, true);

			var copy = await versions.GetVisibleByIdAsync(_department, _version.LmsCourseVersionId);
			copy.Status = (int)ProtocolVersionStatuses.Superseded;
			(await versions.TryUpdateAsync(copy, 0)).Should().BeTrue();
			copy.RowVersion.Should().Be(1);
			var stale = await versions.GetAnyByIdAsync(_version.LmsCourseVersionId);
			stale.RowVersion.Should().Be(1);
			(await versions.TryUpdateAsync(new LmsCourseVersion { LmsCourseVersionId = _version.LmsCourseVersionId, DepartmentId = _department, LmsCourseId = _course.LmsCourseId, Status = 0, CreatedOn = now }, 0))
				.Should().BeFalse("a stale row version never overwrites");
			copy.Status = (int)ProtocolVersionStatuses.Published;
			(await versions.TryUpdateAsync(copy, 1)).Should().BeTrue();

			_course.CurrentVersionId = _version.LmsCourseVersionId;
			(await courses.TryUpdateAsync(_course, 0)).Should().BeTrue();
			(await versions.GetForCourseAsync(_otherDepartment, _course.LmsCourseId)).Should().BeEmpty();
			(await versions.GetVisibleByIdsAsync(_otherDepartment, new[] { _version.LmsCourseVersionId, _systemVersion.LmsCourseVersionId })).Should().ContainSingle();
		}

		[Test, Order(3)]
		public async Task Lessons_banks_questions_owners_and_audiences_round_trip()
		{
			var modules = Repository((c, s, u, q) => new LmsCourseModulesRepository(c, s, u, q));
			var lessons = Repository((c, s, u, q) => new LmsLessonsRepository(c, s, u, q));
			var banks = Repository((c, s, u, q) => new LmsQuestionBanksRepository(c, s, u, q));
			var questions = Repository((c, s, u, q) => new LmsQuestionsRepository(c, s, u, q));
			var owners = Repository((c, s, u, q) => new LmsCourseOwnersRepository(c, s, u, q));
			var audiences = Repository((c, s, u, q) => new LmsCourseAudiencesRepository(c, s, u, q));
			var now = DateTime.UtcNow;

			var module = await modules.InsertAsync(new LmsCourseModule { DepartmentId = _department, LmsCourseVersionId = _version.LmsCourseVersionId, Title = "One", SortOrder = 1 }, default, true);
			_bank = await banks.InsertAsync(new LmsQuestionBank { DepartmentId = _department, Name = "Pumps", CreatedOn = now }, default, true);
			var question = new LmsQuestion { DepartmentId = _department, LmsQuestionBankId = _bank.LmsQuestionBankId, Kind = (int)LmsQuestionKinds.SingleChoice, Text = "PDP?", Points = 1.5m, CreatedOn = now };
			question.Options = new List<LmsQuestionOption> { new LmsQuestionOption { Key = "a", Text = "150", IsCorrect = true }, new LmsQuestionOption { Key = "b", Text = "50" } };
			await questions.InsertAsync(question, default, true);
			(await questions.GetForBankAsync(_department, _bank.LmsQuestionBankId)).Single().Options.Single(o => o.IsCorrect).Key.Should().Be("a");
			(await questions.GetForBankAsync(_otherDepartment, _bank.LmsQuestionBankId)).Should().BeEmpty();

			_lesson = await lessons.InsertAsync(new LmsLesson
			{
				DepartmentId = _department, LmsCourseVersionId = _version.LmsCourseVersionId, LmsCourseModuleId = module.LmsCourseModuleId, Kind = (int)LmsLessonKinds.Blocks, Title = "Read",
				ContentHtml = "<p>x</p>", SortOrder = 1, IsRequired = true
			}, default, true);
			_quiz = await lessons.InsertAsync(new LmsLesson
			{
				DepartmentId = _department, LmsCourseVersionId = _version.LmsCourseVersionId, LmsCourseModuleId = module.LmsCourseModuleId, Kind = (int)LmsLessonKinds.Assessment, Title = "Quiz",
				SortOrder = 2, IsRequired = true, LmsQuestionBankId = _bank.LmsQuestionBankId, PassMark = 80
			}, default, true);
			(await lessons.GetForVersionAsync(_department, _version.LmsCourseVersionId)).Select(l => l.Title).Should().Equal("Read", "Quiz");

			await owners.InsertAsync(new LmsCourseOwner { DepartmentId = _department, LmsCourseId = _course.LmsCourseId, UserId = UserA, CanEdit = true, AddedOn = now }, default, true);
			(await owners.GetForUserAsync(_department, UserA.ToUpperInvariant())).Should().ContainSingle();
			Func<Task> duplicateOwner = () => owners.InsertAsync(new LmsCourseOwner { DepartmentId = _department, LmsCourseId = _course.LmsCourseId, UserId = UserA, AddedOn = now }, default, true);
			await duplicateOwner.Should().ThrowAsync<DbException>("one owner row per member and course");

			await audiences.ReplaceAsync(_department, _course.LmsCourseId, new[] { new ContentAudienceRule(ContentAudienceKinds.Role, "12"), new ContentAudienceRule(ContentAudienceKinds.Group, "4") });
			await audiences.ReplaceAsync(_department, _course.LmsCourseId, new[] { new ContentAudienceRule(ContentAudienceKinds.Role, "13") });
			(await audiences.GetForCourseAsync(_department, _course.LmsCourseId)).Select(a => a.RefId).Should().Equal("13");
		}

		[Test, Order(4)]
		public async Task One_open_enrollment_per_member_and_course_and_the_reminder_claim_runs_with_and_without_a_previous_value()
		{
			var enrollments = Repository((c, s, u, q) => new LmsEnrollmentsRepository(c, s, u, q));
			var now = DateTime.UtcNow;
			LmsEnrollment New(string user) => new LmsEnrollment
			{
				DepartmentId = _department, LmsCourseId = _course.LmsCourseId, LmsCourseVersionId = _version.LmsCourseVersionId, UserId = user, AssignedOn = now,
				Status = (int)LmsEnrollmentStatuses.InProgress, IsOpen = true, LastActivityOn = now.AddDays(-10)
			};

			var (first, created) = await enrollments.InsertOpenAsync(New(UserA));
			created.Should().BeTrue();
			var (same, again) = await enrollments.InsertOpenAsync(New(UserA.ToUpperInvariant()));
			again.Should().BeFalse("one open enrollment per member and course, whatever the id's case");
			same.LmsEnrollmentId.Should().Be(first.LmsEnrollmentId);
			Func<Task> raw = () => enrollments.InsertAsync(New(UserA), default, true);
			await raw.Should().ThrowAsync<DbException>("UX_LmsEnrollments_Open holds under a race too");

			first.Status = (int)LmsEnrollmentStatuses.Completed;
			first.IsOpen = false;
			first.CompletedOn = now;
			(await enrollments.TryUpdateAsync(first, first.RowVersion)).Should().BeTrue();
			(await enrollments.HasCompletedVersionAsync(UserA.ToUpperInvariant(), _version.LmsCourseVersionId)).Should().BeTrue();
			(await enrollments.InsertOpenAsync(New(UserA))).Created.Should().BeTrue("a closed enrollment frees the key");
			(await enrollments.GetOpenForCourseAnyDepartmentAsync(_course.LmsCourseId)).Should().ContainSingle();

			var due = await enrollments.GetReminderDueAsync(now.AddDays(-7), 10);
			var reminder = due.Should().ContainSingle().Subject;
			(await enrollments.ClaimReminderAsync(_department, reminder.LmsEnrollmentId, null, now)).Should().BeTrue("the IS NULL shape");
			(await enrollments.ClaimReminderAsync(_department, reminder.LmsEnrollmentId, null, now)).Should().BeFalse("claimed once");
			var stamped = await enrollments.GetByIdForDepartmentAsync(_department, reminder.LmsEnrollmentId);
			stamped.ReminderSentOn.Should().NotBeNull();
			(await enrollments.ClaimReminderAsync(_department, reminder.LmsEnrollmentId, stamped.ReminderSentOn, now.AddMinutes(1))).Should().BeTrue("the previous-value shape");
			(await enrollments.GetReminderDueAsync(now.AddDays(-7), 10)).Should().BeEmpty("no activity since the reminder");
		}

		[Test, Order(5)]
		public async Task Progress_attempts_attendees_and_surveys_hold_their_keys()
		{
			var enrollments = Repository((c, s, u, q) => new LmsEnrollmentsRepository(c, s, u, q));
			var progress = Repository((c, s, u, q) => new LmsLessonProgressRepository(c, s, u, q));
			var attempts = Repository((c, s, u, q) => new LmsAssessmentAttemptsRepository(c, s, u, q));
			var viewings = Repository((c, s, u, q) => new LmsCrewViewingsRepository(c, s, u, q));
			var attendees = Repository((c, s, u, q) => new LmsCrewViewingAttendeesRepository(c, s, u, q));
			var surveys = Repository((c, s, u, q) => new LmsSurveyResponsesRepository(c, s, u, q));
			var files = Repository((c, s, u, q) => new LmsLessonFilesRepository(c, s, u, q));
			var now = DateTime.UtcNow;

			var enrollment = await enrollments.GetOpenAsync(_department, UserA, _course.LmsCourseId);
			await progress.InsertAsync(new LmsLessonProgress { DepartmentId = _department, LmsEnrollmentId = enrollment.LmsEnrollmentId, LmsLessonId = _lesson.LmsLessonId, UserId = UserA, SeatSeconds = 30 }, default, true);
			var racing = await progress.InsertAsync(new LmsLessonProgress { DepartmentId = _department, LmsEnrollmentId = enrollment.LmsEnrollmentId, LmsLessonId = _lesson.LmsLessonId, UserId = UserA, SeatSeconds = 45 }, default, true);
			var rows = await progress.GetForEnrollmentAsync(_department, enrollment.LmsEnrollmentId);
			rows.Should().ContainSingle("a racing first heartbeat updates the row the other one inserted").Which.SeatSeconds.Should().Be(45);
			racing.LmsLessonProgressId.Should().Be(rows[0].LmsLessonProgressId);

			var file = await files.InsertAsync(new LmsLessonFile { DepartmentId = _department, LmsLessonId = _lesson.LmsLessonId, UserId = UserA, FileName = "a.pdf", ContentType = "application/pdf", SizeBytes = 3, Data = new byte[] { 1, 2, 3 }, UploadedOn = now }, default, true);
			(await files.GetByIdForDepartmentAsync(_department, file.LmsLessonFileId)).Data.Should().Equal(1, 2, 3);
			(await files.GetByIdForDepartmentAsync(_otherDepartment, file.LmsLessonFileId)).Should().BeNull();

			var attempt = await attempts.InsertAsync(new LmsAssessmentAttempt
			{
				DepartmentId = _department, LmsCourseVersionId = _version.LmsCourseVersionId, LmsLessonId = _quiz.LmsLessonId, LmsEnrollmentId = enrollment.LmsEnrollmentId, UserId = UserA,
				AttemptNumber = 1, Status = (int)LmsAttemptStatuses.InProgress, StartedOn = now, PassMark = 80, RevisionHash = new string('a', 64), SnapshotJson = "{}"
			}, default, true);
			attempt.Status = (int)LmsAttemptStatuses.PendingGrading;
			attempt.SubmittedOn = now;
			attempt.Percent = 66.67m;
			(await attempts.TryUpdateAsync(attempt, 0)).Should().BeTrue();
			(await attempts.TryUpdateAsync(attempt, 0)).Should().BeFalse("a second submit loses the claim");
			(await attempts.GetPendingGradingAsync(_department)).Should().ContainSingle().Which.Percent.Should().Be(66.67m);
			(await attempts.GetForUserLessonsAsync(_department, UserA.ToUpperInvariant(), new[] { _quiz.LmsLessonId })).Should().ContainSingle();
			attempt.Status = (int)LmsAttemptStatuses.Graded;
			attempt.Passed = true;
			(await attempts.TryUpdateAsync(attempt, 1)).Should().BeTrue();
			(await attempts.GetFinishedForLessonAsync(_department, _quiz.LmsLessonId)).Should().ContainSingle();

			var viewing = await viewings.InsertAsync(new LmsCrewViewing
			{
				DepartmentId = _department, LmsCourseId = _course.LmsCourseId, LmsCourseVersionId = _version.LmsCourseVersionId, LmsLessonId = _lesson.LmsLessonId, StartedByUserId = UserB,
				StartedOn = now, ExpiresOn = now.AddHours(4), Secret = LmsCrewCodes.NewSecret()
			}, default, true);
			(await attendees.TryInsertAsync(new LmsCrewViewingAttendee { DepartmentId = _department, LmsCrewViewingId = viewing.LmsCrewViewingId, UserId = UserA, MarkedOn = now })).Should().BeTrue();
			(await attendees.TryInsertAsync(new LmsCrewViewingAttendee { DepartmentId = _department, LmsCrewViewingId = viewing.LmsCrewViewingId, UserId = UserA.ToUpperInvariant(), MarkedOn = now })).Should().BeFalse();
			(await attendees.GetForViewingAsync(_department, viewing.LmsCrewViewingId)).Should().ContainSingle();

			await surveys.InsertAsync(new LmsSurveyResponse { DepartmentId = _department, LmsCourseVersionId = _version.LmsCourseVersionId, LmsLessonId = _lesson.LmsLessonId, AnswersJson = "{\"q1\":\"4\"}", SubmittedOn = now.Date }, default, true);
			(await surveys.GetForLessonAsync(_department, _lesson.LmsLessonId)).Should().ContainSingle();
		}

		[Test, Order(6)]
		public async Task The_entitlement_stamp_upserts_and_the_media_queue_is_claimed_once()
		{
			var states = Repository((c, s, u, q) => new LmsDepartmentStatesRepository(c, s, u, q));
			var media = Repository((c, s, u, q) => new LmsMediaAssetsRepository(c, s, u, q));
			var now = new DateTime(2026, 11, 25, 12, 0, 0);

			await states.StampEntitledAsync(_department, now.AddDays(-120));
			await states.StampEntitledAsync(_department, now.AddDays(-100));
			(await states.GetForDepartmentAsync(_department)).LastEntitledOn.Should().Be(now.AddDays(-100));
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("LmsDepartmentStates")} WHERE {T("DepartmentId")} = @D", new { D = _department })).Should().Be(1);

			var asset = await media.InsertAsync(new LmsMediaAsset
			{
				DepartmentId = _department, FileName = "drill.mp4", ContentType = "video/mp4", SourceKey = "lms/x/source.mp4", SourceBytes = 1000, Status = (int)LmsMediaStatuses.Queued, CreatedOn = now
			}, default, true);
			var claimed = await media.ClaimNextAsync(now, now.AddHours(-2));
			claimed.LmsMediaAssetId.Should().Be(asset.LmsMediaAssetId);
			claimed.Status.Should().Be((int)LmsMediaStatuses.Transcoding);
			claimed.Attempts.Should().Be(1);
			(await media.ClaimNextAsync(now, now.AddHours(-2))).Should().BeNull("a fresh claim is not stale");
			(await media.ClaimNextAsync(now.AddHours(3), now.AddHours(1))).Should().NotBeNull("an abandoned claim goes back to the queue");

			claimed.Status = (int)LmsMediaStatuses.Ready;
			claimed.HlsPrefix = "lms/x/hls/";
			claimed.OutputBytes = 500;
			claimed.DurationSeconds = 95;
			claimed.ReadyOn = now;
			(await media.TryMoveAsync(claimed, LmsMediaStatuses.Transcoding)).Should().BeTrue();
			(await media.TryMoveAsync(claimed, LmsMediaStatuses.Transcoding)).Should().BeFalse("only from the expected status");
			(await media.GetStoredBytesAsync(_department)).Should().Be(1500);
			(await media.GetStoredBytesAsync(_otherDepartment)).Should().Be(0);
			(await states.GetLapsedWithReadyMediaAsync(now.AddDays(-90))).Should().Equal(_department);
		}

		[Test, Order(7)]
		public async Task An_approval_step_is_decided_once_and_pending_steps_are_withdrawn()
		{
			var approvals = Repository((c, s, u, q) => new LmsCourseVersionApprovalsRepository(c, s, u, q));
			var now = DateTime.UtcNow;
			var step = await approvals.InsertAsync(new LmsCourseVersionApproval
			{
				DepartmentId = _department, LmsCourseVersionId = _version.LmsCourseVersionId, Round = 1, StepOrder = 1, Decision = (int)ProtocolApprovalDecisions.Pending, CreatedOn = now
			}, default, true);
			var second = await approvals.InsertAsync(new LmsCourseVersionApproval
			{
				DepartmentId = _department, LmsCourseVersionId = _version.LmsCourseVersionId, Round = 1, StepOrder = 2, Decision = (int)ProtocolApprovalDecisions.Pending, CreatedOn = now
			}, default, true);

			step.Decision = (int)ProtocolApprovalDecisions.Approved;
			step.DecidedByUserId = UserB;
			step.DecidedOn = now;
			step.SignatureRecordId = 4242;
			(await approvals.TryDecideAsync(step)).Should().BeTrue();
			(await approvals.TryDecideAsync(step)).Should().BeFalse("a decided step stays decided");
			(await approvals.WithdrawPendingAsync(_department, _version.LmsCourseVersionId, 1, now)).Should().Be(1);
			var stored = await approvals.GetForVersionAsync(_department, _version.LmsCourseVersionId);
			stored.Single(a => a.LmsCourseVersionApprovalId == second.LmsCourseVersionApprovalId).Decision.Should().Be((int)ProtocolApprovalDecisions.Withdrawn);
			stored.Single(a => a.LmsCourseVersionApprovalId == step.LmsCourseVersionApprovalId).SignatureRecordId.Should().Be(4242);
		}

		[Test, Order(8)]
		public async Task Department_cleanup_removes_that_departments_lms_rows_only()
		{
			var enrollments = Repository((c, s, u, q) => new LmsEnrollmentsRepository(c, s, u, q));
			var paths = Repository((c, s, u, q) => new LmsPathAssignmentsRepository(c, s, u, q));
			var now = DateTime.UtcNow;
			// The department's members on the Academy (a system course) and another department's enrollment on it.
			await enrollments.InsertOpenAsync(new LmsEnrollment { DepartmentId = _department, LmsCourseId = _system.LmsCourseId, LmsCourseVersionId = _systemVersion.LmsCourseVersionId, UserId = UserB, AssignedOn = now });
			await enrollments.InsertOpenAsync(new LmsEnrollment { DepartmentId = _otherDepartment, LmsCourseId = _system.LmsCourseId, LmsCourseVersionId = _systemVersion.LmsCourseVersionId, UserId = UserB, AssignedOn = now });
			await paths.InsertAsync(new LmsPathAssignment { DepartmentId = _department, PathKey = "member", UserId = UserB, AssignedOn = now }, default, true);

			await using (var database = Connect(_connection))
			{
				await database.OpenAsync();
				await using var transaction = await database.BeginTransactionAsync();
				await UnlinkedDepartmentCleanup.DeleteWithinTransactionAsync(database, transaction, _department, type);
				await transaction.CommitAsync();
			}

			foreach (var table in LmsTables)
				(await ScalarAsync($"SELECT COUNT(*) FROM {T(table)} WHERE {T("DepartmentId")} = @D", new { D = _department })).Should().Be(0, table);

			(await ScalarAsync($"SELECT COUNT(*) FROM {T("LmsCourses")} WHERE {T("DepartmentId")} = 0")).Should().Be(1, "the Academy is not the department's to delete");
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("LmsCourseVersions")} WHERE {T("DepartmentId")} = 0")).Should().Be(1);
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("LmsCourses")} WHERE {T("DepartmentId")} = @D", new { D = _otherDepartment })).Should().Be(1);
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("LmsEnrollments")} WHERE {T("DepartmentId")} = @D", new { D = _otherDepartment })).Should().Be(1);
		}
	}
}
