using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
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
	/// Real-database proof for round 7B (M0400–M0402) on both engines: the migrations re-run as no-ops, roll back exactly what they added and
	/// apply again; recorded completions round trip (case-insensitive member lookups); the ledger stores the recorded-by metadata and the
	/// requirement facts read it; a learner e-mail is claimed once; the unsubscribe key is matched exactly; a moved class clears its reminder
	/// claims; department cleanup removes the new tables for that department only. Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION /
	/// RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class RecordedCompletionsDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "r7b_rc_";
		private const string Member = "5c2e7a11-1f3b-4d2c-9a6e-0b1c2d3e4f77";
		private const string Officer = "5c2e7a11-1f3b-4d2c-9a6e-0b1c2d3e4f78";
		private const int Department = 4701;
		private const int OtherDepartment = 4702;

		private static readonly string[] Tables = { "TrainingCompletionRecordings", "LmsLearnerNotices" };

		private static readonly (string Table, string Column)[] Columns =
		{
			("LearningRecords", "CompletionMethod"), ("LearningRecords", "RecordedByUserId"), ("LearningRecords", "RecordedOn"), ("LearningRecords", "RecordedCompletionId"),
			("TrainingUsers", "CompletionMethod"), ("TrainingUsers", "RecordedCompletionId"), ("LmsEnrollments", "CompletionMethod"), ("LmsEnrollments", "RecordedCompletionId"),
			("LmsLessonProgress", "RecordedCompletionId"), ("LmsExternalLearners", "Language"), ("LmsExternalLearners", "RemindersOptOut"), ("LmsExternalLearners", "UnsubscribeKey")
		};

		private DatabaseTypes _previousType;
		private string _previousConnection, _master, _connection, _database;

		private bool IsPostgres => type == DatabaseTypes.Postgres;

		private DbConnection Connect(string connection) => IsPostgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);

		private string T(string name) => IsPostgres ? name.ToLowerInvariant() : "[" + name + "]";

		private TRepository Repository<TRepository>(Func<IConnectionProvider, SqlConfiguration, IUnitOfWork, IQueryFactory, TRepository> build)
		{
			var connections = new Mock<IConnectionProvider>();
			connections.Setup(c => c.Create()).Returns(() => Connect(_connection));
			SqlConfiguration configuration = IsPostgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration();
			var queries = new ConcurrentDictionary<Type, IQuery>();
			foreach (var query in new[] { typeof(Resgrid.Repositories.DataRepository.Queries.Common.InsertQuery), typeof(Resgrid.Repositories.DataRepository.Queries.Common.UpdateQuery),
				typeof(Resgrid.Repositories.DataRepository.Queries.Common.DeleteQuery), typeof(Resgrid.Repositories.DataRepository.Queries.Common.SelectAllQuery),
				typeof(Resgrid.Repositories.DataRepository.Queries.Common.SelectByIdQuery), typeof(Resgrid.Repositories.DataRepository.Queries.Common.SelectByDepartmentIdQuery) })
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

		private async Task<int> ScalarAsync(string sql, object parameters = null)
		{
			await using var database = Connect(_connection);
			return await database.ExecuteScalarAsync<int>(sql, parameters);
		}

		private async Task ExecuteAsync(string sql, object parameters = null)
		{
			await using var database = Connect(_connection);
			await database.ExecuteAsync(sql, parameters);
		}

		private string TablesExist(params string[] names) => IsPostgres
			? "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name IN (" + string.Join(", ", names.Select(n => "'" + n.ToLowerInvariant() + "'")) + ")"
			: "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME IN (" + string.Join(", ", names.Select(n => "'" + n + "'")) + ")";

		private string ColumnsExist() => IsPostgres
			? "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = 'public' AND (" +
			  string.Join(" OR ", Columns.Select(c => $"(table_name = '{c.Table.ToLowerInvariant()}' AND column_name = '{c.Column.ToLowerInvariant()}')")) + ")"
			: "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE " + string.Join(" OR ", Columns.Select(c => $"(TABLE_NAME = '{c.Table}' AND COLUMN_NAME = '{c.Column}')"));

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

		[Test, Order(1)]
		public async Task The_migrations_rerun_as_no_ops_roll_back_exactly_what_they_added_and_apply_again()
		{
			var version = IsPostgres ? "\"VersionInfo\"" : "[VersionInfo]";
			var versionColumn = IsPostgres ? "\"Version\"" : "[Version]";
			await ExecuteAsync($"DELETE FROM {version} WHERE {versionColumn} >= 400 AND {versionColumn} <= 402");

			using (var runner = Runner())
			using (var scope = runner.CreateScope())
			{
				var migrations = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
				migrations.MigrateUp();
				(await ScalarAsync(TablesExist(Tables))).Should().Be(Tables.Length, "a second Up over existing tables is a no-op");
				(await ScalarAsync(ColumnsExist())).Should().Be(Columns.Length);

				migrations.MigrateDown(399);
				(await ScalarAsync(TablesExist(Tables))).Should().Be(0);
				(await ScalarAsync(ColumnsExist())).Should().Be(0, "Down removes exactly the columns Up added");
				(await ScalarAsync(TablesExist("LearningRecords", "TrainingUsers", "LmsEnrollments", "LmsExternalLearners"))).Should().Be(4, "and nothing else");

				migrations.MigrateUp();
			}

			(await ScalarAsync(TablesExist(Tables))).Should().Be(Tables.Length);
			(await ScalarAsync(ColumnsExist())).Should().Be(Columns.Length);
		}

		[Test, Order(2)]
		public async Task Recordings_round_trip_with_case_insensitive_member_lookups()
		{
			var recordings = Repository((c, s, u, q) => new TrainingCompletionRecordingsRepository(c, s, u, q));
			var stored = await recordings.InsertAsync(new TrainingCompletionRecording
			{
				DepartmentId = Department, UserId = Member, TargetKind = (int)TrainingCompletionRecordingTargets.Course, TargetId = 31, TargetVersionId = 4, TargetName = "CPR",
				SourceKind = (int)TrainingCompletionRecordingSources.ClassSession, SourceRefId = "57", BatchKey = Guid.NewGuid().ToString("N"), Note = "Group viewing",
				AttestationVersion = TrainingCompletionAttestation.CurrentVersion, QuizWaived = true, CompletedOn = DateTime.UtcNow.AddHours(-1), RecordedByUserId = Officer, RecordedOn = DateTime.UtcNow
			}, default, true);
			stored.TrainingCompletionRecordingId.Should().BePositive();
			await recordings.InsertAsync(new TrainingCompletionRecording
			{
				DepartmentId = OtherDepartment, UserId = Member, TargetKind = 0, TargetId = 9, SourceKind = 0, AttestationVersion = TrainingCompletionAttestation.CurrentVersion,
				CompletedOn = DateTime.UtcNow, RecordedByUserId = Officer, RecordedOn = DateTime.UtcNow
			}, default, true);

			(await recordings.GetForUserAsync(Department, Member.ToUpperInvariant())).Should().ContainSingle().Which.QuizWaived.Should().BeTrue();
			(await recordings.GetForSourceAsync(Department, (int)TrainingCompletionRecordingSources.ClassSession, "57")).Should().ContainSingle();
			(await recordings.GetForTargetAsync(Department, (int)TrainingCompletionRecordingTargets.Course, 31)).Should().ContainSingle();
			(await recordings.GetByIdsAsync(Department, new[] { stored.TrainingCompletionRecordingId })).Single().Note.Should().Be("Group viewing");

			stored.VoidedOn = DateTime.UtcNow;
			stored.VoidedByUserId = Officer;
			stored.VoidReason = "wrong class";
			await recordings.SaveOrUpdateAsync(stored, default, true);
			(await recordings.GetByIdForDepartmentAsync(Department, stored.TrainingCompletionRecordingId)).VoidReason.Should().Be("wrong class");
			(await recordings.GetByIdForDepartmentAsync(OtherDepartment, stored.TrainingCompletionRecordingId)).Should().BeNull("every read starts at the department");
		}

		[Test, Order(3)]
		public async Task The_ledger_stores_the_recorded_by_metadata_and_the_requirement_facts_read_it()
		{
			var records = Repository((c, s, u, q) => new LearningRecordsRepository(c, s, u, q));
			var recordedOn = DateTime.UtcNow.AddMinutes(-5);
			var (record, inserted) = await records.InsertIfAbsentAsync(new LearningRecord
			{
				DepartmentId = Department, UserId = Member, SourceKind = (int)LearningRecordSourceKinds.CourseCompletion, SourceRefId = "training:77:0", Title = "CPR",
				CompletedOn = DateTime.UtcNow.AddHours(-1), Hours = 2m, Status = (int)LearningRecordStatuses.Verified, CompletionMethod = (int)LearningCompletionMethods.RecordedByOther,
				RecordedByUserId = Officer, RecordedOn = recordedOn, RecordedCompletionId = 61
			});
			inserted.Should().BeTrue();
			var read = await records.GetByIdForDepartmentAsync(Department, record.LearningRecordId);
			read.IsRecordedByOther.Should().BeTrue();
			read.RecordedByUserId.Should().Be(Officer);
			read.RecordedCompletionId.Should().Be(61);
			read.RecordedOn.Should().BeCloseTo(recordedOn, TimeSpan.FromSeconds(1));

			var facts = Repository((c, s, u, q) => new LmsRequirementFactsRepository(c, s, u, q));
			(await facts.GetTrainingCompletionsAsync(Department, new[] { 77 })).Should().ContainSingle().Which.CompletionMethod.Should().Be((int)LearningCompletionMethods.RecordedByOther);
		}

		[Test, Order(4)]
		public async Task A_learner_email_is_claimed_once_and_the_unsubscribe_key_is_matched_exactly()
		{
			var notices = Repository((c, s, u, q) => new LmsLearnerNoticesRepository(c, s, u, q));
			var notice = new LmsLearnerNotice { DepartmentId = Department, LmsExternalLearnerId = 12, LmsClassSessionId = 57, Kind = (int)LmsLearnerNoticeKinds.Reminder, NoticeKey = "r:638700000000000000", ClaimedOn = DateTime.UtcNow };
			(await notices.TryClaimAsync(notice)).Should().BeTrue();
			(await notices.TryClaimAsync(new LmsLearnerNotice { DepartmentId = Department, LmsExternalLearnerId = 12, LmsClassSessionId = 57, Kind = (int)LmsLearnerNoticeKinds.Reminder, NoticeKey = "r:638700000000000000", ClaimedOn = DateTime.UtcNow }))
				.Should().BeFalse("the same notice is claimed once");
			(await notices.TryClaimAsync(new LmsLearnerNotice { DepartmentId = Department, LmsExternalLearnerId = 12, LmsClassSessionId = 57, Kind = (int)LmsLearnerNoticeKinds.Reminder, NoticeKey = "r:638700000000000001", ClaimedOn = DateTime.UtcNow }))
				.Should().BeTrue("a moved class is reminded again");
			(await notices.MarkAsync(Department, notice.LmsLearnerNoticeId, DateTime.UtcNow, null)).Should().BeTrue();
			(await notices.GetForLearnerAsync(Department, 12)).Should().HaveCount(2).And.Contain(n => n.SentOn != null);
			(await notices.DeleteForLearnerAsync(Department, 12)).Should().Be(2);

			var learners = Repository((c, s, u, q) => new LmsExternalLearnersRepository(c, s, u, q));
			var learner = await learners.InsertAsync(new LmsExternalLearner
			{
				DepartmentId = Department, LearnerUserId = LmsExternalLearnerIds.New(), Email = "rc-learner@example.org", FirstName = "Lee", LastName = "Learner",
				Status = (int)LmsExternalLearnerStatuses.Active, SecurityStamp = LmsExternalLearnerRules.NewToken(), CreatedOn = DateTime.UtcNow, Language = "de", RemindersOptOut = true,
				UnsubscribeKey = "AbCdEf-key_1"
			}, default, true);
			var found = await learners.GetByUnsubscribeKeyAsync(Department, "AbCdEf-key_1");
			found.LmsExternalLearnerId.Should().Be(learner.LmsExternalLearnerId);
			found.Language.Should().Be("de");
			found.RemindersOptOut.Should().BeTrue();
			(await learners.GetByUnsubscribeKeyAsync(Department, "abcdef-key_1")).Should().BeNull("a token is compared exactly");
			(await learners.GetByUnsubscribeKeyAsync(OtherDepartment, "AbCdEf-key_1")).Should().BeNull();
		}

		[Test, Order(5)]
		public async Task A_moved_class_clears_the_reminder_claims_of_its_roster_only()
		{
			var enrollments = Repository((c, s, u, q) => new LmsClassEnrollmentsRepository(c, s, u, q));
			var mine = await enrollments.InsertAsync(new LmsClassEnrollment { DepartmentId = Department, LmsClassSessionId = 70, UserId = Member, Status = (int)LmsClassEnrollmentStatuses.Enrolled, EnrolledOn = DateTime.UtcNow }, default, true);
			var other = await enrollments.InsertAsync(new LmsClassEnrollment { DepartmentId = Department, LmsClassSessionId = 71, UserId = Member, Status = (int)LmsClassEnrollmentStatuses.Enrolled, EnrolledOn = DateTime.UtcNow }, default, true);
			(await enrollments.ClaimReminderAsync(Department, mine.LmsClassEnrollmentId, DateTime.UtcNow)).Should().BeTrue();
			(await enrollments.ClaimReminderAsync(Department, other.LmsClassEnrollmentId, DateTime.UtcNow)).Should().BeTrue();

			(await enrollments.ResetRemindersAsync(Department, 70)).Should().Be(1);
			(await enrollments.GetByIdForDepartmentAsync(Department, mine.LmsClassEnrollmentId)).ReminderSentOn.Should().BeNull();
			(await enrollments.GetByIdForDepartmentAsync(Department, other.LmsClassEnrollmentId)).ReminderSentOn.Should().NotBeNull();
			(await enrollments.ClaimReminderAsync(Department, mine.LmsClassEnrollmentId, DateTime.UtcNow)).Should().BeTrue("the new time is reminded once");
		}

		[Test, Order(6)]
		public async Task Department_cleanup_removes_the_new_tables_for_that_department_only()
		{
			var notices = Repository((c, s, u, q) => new LmsLearnerNoticesRepository(c, s, u, q));
			await notices.TryClaimAsync(new LmsLearnerNotice { DepartmentId = Department, LmsExternalLearnerId = 13, LmsClassSessionId = 57, Kind = 0, NoticeKey = "e:1:0", ClaimedOn = DateTime.UtcNow });
			await notices.TryClaimAsync(new LmsLearnerNotice { DepartmentId = OtherDepartment, LmsExternalLearnerId = 14, LmsClassSessionId = 58, Kind = 0, NoticeKey = "e:2:0", ClaimedOn = DateTime.UtcNow });

			await using (var database = Connect(_connection))
			{
				await database.OpenAsync();
				await using var transaction = await database.BeginTransactionAsync();
				await UnlinkedDepartmentCleanup.DeleteWithinTransactionAsync(database, transaction, Department, type);
				await transaction.CommitAsync();
			}

			foreach (var table in Tables)
				(await ScalarAsync($"SELECT COUNT(*) FROM {T(table)} WHERE {T("DepartmentId")} = @D", new { D = Department })).Should().Be(0, table);
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("TrainingCompletionRecordings")} WHERE {T("DepartmentId")} = @D", new { D = OtherDepartment })).Should().Be(1);
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("LmsLearnerNotices")} WHERE {T("DepartmentId")} = @D", new { D = OtherDepartment })).Should().Be(1);
		}
	}
}
