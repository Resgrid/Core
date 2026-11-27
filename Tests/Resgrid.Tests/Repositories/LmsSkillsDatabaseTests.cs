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
using Resgrid.Services;

namespace Resgrid.Tests.Repositories
{
	/// <summary>
	/// Real-database proof for Training T4 part 2 (M0381–M0384) on both engines: the migrations re-run as no-ops, roll back and apply again;
	/// sheets and versions with the optimistic save; evaluations with members (the member list, case-insensitive ids), the offline idempotency
	/// key's filtered unique index, evidence lists without bytes; task books and worker 96's expiry query; the notification due query and its
	/// claim; a completed evaluation's learning record counted by 4B's "N passed evaluations" query; department cleanup for that department
	/// only. Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class LmsSkillsDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "lms_skills_";
		private const string UserA = "7e1f2a33-1f3b-4d2c-9a6e-0b1c2d3e4f71";
		private const string UserB = "8f2a3b44-2a4c-4e3d-8b7f-1c2d3e4f5a82";
		private const int Department = 4501;
		private const int OtherDepartment = 4502;

		private static readonly string[] Tables =
		{
			"LmsSkillSheets", "LmsSkillSheetVersions", "LmsSkillEvaluations", "LmsSkillEvaluationMembers", "LmsSkillEvaluationFiles", "LmsTaskBookDefinitions",
			"LmsTaskBooks", "LmsSkillNotifications"
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

		private string Exists(params string[] names) => IsPostgres
			? "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name IN (" + string.Join(", ", names.Select(n => "'" + n.ToLowerInvariant() + "'")) + ")"
			: "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME IN (" + string.Join(", ", names.Select(n => "'" + n + "'")) + ")";

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
		public async Task The_migrations_rerun_as_no_ops_roll_back_and_apply_again()
		{
			(await ScalarAsync(Exists(Tables))).Should().Be(Tables.Length);
			var version = IsPostgres ? "\"VersionInfo\"" : "[VersionInfo]";
			var versionColumn = IsPostgres ? "\"Version\"" : "[Version]";
			await ExecuteAsync($"DELETE FROM {version} WHERE {versionColumn} >= 381");

			using (var runner = Runner())
			using (var scope = runner.CreateScope())
			{
				var migrations = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
				migrations.MigrateUp();
				(await ScalarAsync(Exists(Tables))).Should().Be(Tables.Length, "a second Up over existing tables is a no-op");

				migrations.MigrateDown(380);
				(await ScalarAsync(Exists(Tables))).Should().Be(0);

				migrations.MigrateUp();
			}

			(await ScalarAsync(Exists(Tables))).Should().Be(Tables.Length);
		}

		[Test, Order(2)]
		public async Task Sheets_and_versions_round_trip_with_the_optimistic_save()
		{
			var sheets = Repository((c, s, u, q) => new LmsSkillSheetsRepository(c, s, u, q));
			var versions = Repository((c, s, u, q) => new LmsSkillSheetVersionsRepository(c, s, u, q));

			var sheet = await sheets.InsertAsync(new LmsSkillSheet
			{
				DepartmentId = Department, Title = "SCBA donning", Code = "SKL-SCBA", Kind = 0, AllowCrewEvaluation = true,
				EvaluatorEligibilityJson = new SignerEligibility { CertificationTypeIds = new List<int> { 3 } }.ToJson(), CreatedOn = DateTime.UtcNow, CreatedByUserId = UserA
			}, default, true);
			await sheets.InsertAsync(new LmsSkillSheet { DepartmentId = Department, Title = "Retired", IsDeleted = true, CreatedOn = DateTime.UtcNow }, default, true);
			await sheets.InsertAsync(new LmsSkillSheet { DepartmentId = OtherDepartment, Title = "Theirs", CreatedOn = DateTime.UtcNow }, default, true);

			(await sheets.GetForDepartmentAsync(Department)).Select(s => s.Title).Should().Equal("SCBA donning");
			(await sheets.GetForDepartmentAsync(Department, true)).Should().HaveCount(2);
			(await sheets.GetByIdForDepartmentAsync(OtherDepartment, sheet.LmsSkillSheetId)).Should().BeNull("department scoped");
			(await sheets.GetByIdsAsync(Department, new[] { sheet.LmsSkillSheetId, 999999 })).Should().ContainSingle();
			SignerEligibility.Parse((await sheets.GetByIdForDepartmentAsync(Department, sheet.LmsSkillSheetId)).EvaluatorEligibilityJson).CertificationTypeIds.Should().Equal(3);

			var content = LmsSkillTemplateCatalog.FindSheet("scba-donning").Steps;
			var version = await versions.InsertAsync(new LmsSkillSheetVersion
			{
				DepartmentId = Department, LmsSkillSheetId = sheet.LmsSkillSheetId, Status = 0, ContentJson = new LmsSkillSheetContent { Steps = content }.ToJson(),
				PassThreshold = 87.5m, TimeLimitSeconds = 90, CreatedOn = DateTime.UtcNow
			}, default, true);
			var stored = await versions.GetByIdForDepartmentAsync(Department, version.LmsSkillSheetVersionId);
			stored.PassThreshold.Should().Be(87.5m);
			LmsSkillSheetContent.Parse(stored.ContentJson).Steps.Should().HaveCount(content.Count);

			stored.Status = (int)ProtocolVersionStatuses.Published;
			stored.Major = 1;
			(await versions.TryUpdateAsync(stored, 0)).Should().BeTrue();
			stored.RowVersion.Should().Be(1);
			(await versions.TryUpdateAsync(new LmsSkillSheetVersion { LmsSkillSheetVersionId = stored.LmsSkillSheetVersionId, DepartmentId = Department, LmsSkillSheetId = sheet.LmsSkillSheetId, CreatedOn = DateTime.UtcNow }, 0))
				.Should().BeFalse("a save against an older RowVersion is refused");
			(await versions.GetForSheetAsync(Department, sheet.LmsSkillSheetId)).Single().Major.Should().Be(1);

			sheet.CurrentVersionId = stored.LmsSkillSheetVersionId;
			(await sheets.TryUpdateAsync(sheet, 0)).Should().BeTrue();
			(await sheets.GetByIdForDepartmentAsync(Department, sheet.LmsSkillSheetId)).CurrentVersionId.Should().Be(stored.LmsSkillSheetVersionId);
		}

		[Test, Order(3)]
		public async Task Evaluations_members_and_evidence_query_by_member_evaluator_task_book_and_idempotency_key()
		{
			var evaluations = Repository((c, s, u, q) => new LmsSkillEvaluationsRepository(c, s, u, q));
			var members = Repository((c, s, u, q) => new LmsSkillEvaluationMembersRepository(c, s, u, q));
			var files = Repository((c, s, u, q) => new LmsSkillEvaluationFilesRepository(c, s, u, q));
			var now = DateTime.UtcNow;

			var crew = new LmsSkillEvaluation
			{
				DepartmentId = Department, LmsSkillSheetId = 1, LmsSkillSheetVersionId = 2, Title = "Hoseline", Code = "SKL-HOSE", Mode = 1, Status = 0, EvaluatorUserId = UserA,
				CreatedByUserId = UserA, StartedOn = now.AddMinutes(-30), AnswersJson = "{\"Steps\":{}}", IdempotencyKey = "omni-1", LmsTaskBookId = 77, TaskKey = "t1", CallId = 9001,
				ClassSessionRef = "class-session:42"
			};
			(await evaluations.TryInsertAsync(crew)).Should().BeTrue();
			crew.LmsSkillEvaluationId.Should().BeGreaterThan(0);
			(await evaluations.TryInsertAsync(new LmsSkillEvaluation { DepartmentId = Department, Title = "Replay", StartedOn = now, IdempotencyKey = "omni-1" }))
				.Should().BeFalse("the filtered unique index holds the offline outbox key");
			(await evaluations.TryInsertAsync(new LmsSkillEvaluation { DepartmentId = OtherDepartment, Title = "Other", StartedOn = now, IdempotencyKey = "omni-1" }))
				.Should().BeTrue("keys are unique per department");
			await evaluations.InsertAsync(new LmsSkillEvaluation { DepartmentId = Department, Title = "No key 1", StartedOn = now, EvaluatorUserId = UserB }, default, true);
			await evaluations.InsertAsync(new LmsSkillEvaluation { DepartmentId = Department, Title = "No key 2", StartedOn = now, EvaluatorUserId = UserB }, default, true);

			(await evaluations.GetByIdempotencyKeyAsync(Department, "omni-1")).LmsSkillEvaluationId.Should().Be(crew.LmsSkillEvaluationId);
			(await evaluations.GetByIdempotencyKeyAsync(OtherDepartment, "omni-1")).LmsSkillEvaluationId.Should().NotBe(crew.LmsSkillEvaluationId);

			await members.InsertAsync(new LmsSkillEvaluationMember { LmsSkillEvaluationId = crew.LmsSkillEvaluationId, DepartmentId = Department, UserId = UserA, AttemptNumber = 1, CreatedOn = now }, default, true);
			var b = await members.InsertAsync(new LmsSkillEvaluationMember { LmsSkillEvaluationId = crew.LmsSkillEvaluationId, DepartmentId = Department, UserId = UserB, AttemptNumber = 2, CreatedOn = now }, default, true);
			(await members.GetForEvaluationAsync(Department, crew.LmsSkillEvaluationId)).Should().HaveCount(2);
			(await members.GetForEvaluationsAsync(Department, new[] { crew.LmsSkillEvaluationId, 123456 })).Should().HaveCount(2);

			(await evaluations.GetForMemberAsync(Department, UserB.ToUpperInvariant(), null)).Should().ContainSingle("member ids compare case-insensitively");
			(await evaluations.GetForMemberAsync(Department, UserB, 1)).Should().ContainSingle();
			(await evaluations.GetForMemberAsync(Department, UserB, 2)).Should().BeEmpty();
			b.State = (int)LmsSkillEvaluationMemberStates.SplitOut;
			await members.UpdateAsync(b, default, true);
			(await evaluations.GetForMemberAsync(Department, UserB, null)).Should().BeEmpty("a member split out is no longer on the crew form");

			(await evaluations.GetOpenForEvaluatorAsync(Department, UserA.ToUpperInvariant())).Should().ContainSingle();
			(await evaluations.GetOpenForEvaluatorAsync(Department, UserB)).Should().HaveCount(2);
			(await evaluations.GetForTaskBookAsync(Department, 77)).Should().ContainSingle();
			(await evaluations.GetForClassSessionAsync(Department, "CLASS-SESSION:42")).Should().ContainSingle();
			var listed = await evaluations.GetForDepartmentAsync(Department, now.AddHours(-1), now.AddHours(1), 1, 0, 50);
			listed.Should().ContainSingle();
			listed[0].AnswersJson.Should().BeNull("list queries never load answers");
			(await evaluations.GetForDepartmentAsync(Department, null, null, null, null, 50)).Should().HaveCount(3);

			await files.InsertAsync(new LmsSkillEvaluationFile { LmsSkillEvaluationId = crew.LmsSkillEvaluationId, DepartmentId = Department, StepKey = "seal", FileName = "a.jpg", ContentType = "image/jpeg",
				Size = 3, Sha256 = new string('a', 64), Data = new byte[] { 1, 2, 3 }, CreatedOn = now }, default, true);
			var evidence = await files.GetForEvaluationAsync(Department, crew.LmsSkillEvaluationId);
			evidence.Should().ContainSingle();
			evidence[0].Data.Should().BeNull("list queries never load evidence bytes");
			(await files.GetWithDataAsync(Department, evidence[0].LmsSkillEvaluationFileId)).Data.Should().Equal(1, 2, 3);
			(await files.GetWithDataAsync(OtherDepartment, evidence[0].LmsSkillEvaluationFileId)).Should().BeNull();

			crew.Status = (int)LmsSkillEvaluationStatuses.Completed;
			crew.Result = (int)LmsSkillEvaluationResults.Passed;
			crew.CompletedOn = now;
			crew.SignatureRequestId = 4242;
			crew.Score = 92.5m;
			(await evaluations.TryUpdateAsync(crew, 0)).Should().BeTrue();
			(await evaluations.GetBySignatureRequestAsync(Department, 4242)).Score.Should().Be(92.5m);
			(await evaluations.GetCompletedForReportAsync(Department, now.AddDays(-1), now.AddDays(1), null)).Should().ContainSingle();
			(await evaluations.GetOpenForEvaluatorAsync(Department, UserA)).Should().BeEmpty();
		}

		[Test, Order(4)]
		public async Task A_completed_evaluation_record_is_counted_by_the_passed_evaluations_requirement()
		{
			var records = Repository((c, s, u, q) => new LearningRecordsRepository(c, s, u, q));
			var facts = Repository((c, s, u, q) => new LmsRequirementFactsRepository(c, s, u, q));
			var evaluation = new LmsSkillEvaluation
			{
				LmsSkillEvaluationId = 5100, DepartmentId = Department, Title = "SCBA donning", Code = "SKL-SCBA", EvaluatorUserId = UserB, StartedOn = DateTime.UtcNow.AddMinutes(-10),
				SubmittedOn = DateTime.UtcNow, DurationSeconds = 600, Status = (int)LmsSkillEvaluationStatuses.Completed, Result = (int)LmsSkillEvaluationResults.Passed
			};
			var drafts = LmsSkillEvaluationService.Drafts(evaluation, new LmsSkillSheet { CategoryId = null }, null,
				new[] { new LmsSkillEvaluationMember { LmsSkillEvaluationId = 5100, DepartmentId = Department, UserId = UserA } });
			foreach (var draft in drafts)
				await records.InsertIfAbsentAsync(new LearningRecord
				{
					DepartmentId = Department, UserId = draft.UserId, SourceKind = (int)LearningRecordSourceKinds.SkillEvaluation, SourceRefId = LmsSkillRules.EvaluationSourceRef(5100),
					ComponentKey = draft.ComponentKey, SessionKey = draft.SessionKey, Title = draft.Title, CourseCode = draft.CourseCode, CompletedOn = draft.CompletedOn, Hours = draft.Hours,
					Status = (int)draft.Status
				});
			// The same source written again stays one record (the writer's idempotency key).
			await records.InsertIfAbsentAsync(new LearningRecord
			{
				DepartmentId = Department, UserId = UserA, SourceKind = (int)LearningRecordSourceKinds.SkillEvaluation, SourceRefId = LmsSkillRules.EvaluationSourceRef(5100),
				ComponentKey = string.Empty, Title = "dup", CourseCode = "SKL-SCBA", CompletedOn = DateTime.UtcNow, Status = (int)LearningRecordStatuses.Verified
			});

			var counted = await facts.GetEvaluationsAsync(Department, new[] { "skl-scba" });
			counted.Should().ContainSingle();
			counted[0].UserId.Should().BeEquivalentTo(UserA);
			drafts.Single().Hours.Should().Be(0.17m);
		}

		[Test, Order(5)]
		public async Task Task_books_definitions_and_the_expiry_due_query()
		{
			var definitions = Repository((c, s, u, q) => new LmsTaskBookDefinitionsRepository(c, s, u, q));
			var books = Repository((c, s, u, q) => new LmsTaskBooksRepository(c, s, u, q));
			var now = DateTime.UtcNow;

			var definition = await definitions.InsertAsync(new LmsTaskBookDefinition
			{
				DepartmentId = Department, PositionCode = "FFT2", Name = "Firefighter Type 2", ValidityMonths = 36,
				TasksJson = Newtonsoft.Json.JsonConvert.SerializeObject(LmsSkillTemplateCatalog.FindTaskBook("nwcg-fft2").Tasks), CreatedOn = now
			}, default, true);
			(await definitions.GetForDepartmentAsync(Department)).Should().ContainSingle();
			LmsSkillRules.ParseTasks((await definitions.GetByIdForDepartmentAsync(Department, definition.LmsTaskBookDefinitionId)).TasksJson).Should().HaveCount(10);

			var expired = await books.InsertAsync(new LmsTaskBook { DepartmentId = Department, LmsTaskBookDefinitionId = definition.LmsTaskBookDefinitionId, UserId = UserA, PositionCode = "FFT2",
				Name = "FFT2", InitiatedOn = now.AddYears(-3).AddDays(-1), ExpiresOn = now.AddDays(-1), Status = (int)LmsTaskBookStatuses.Open, SignatureRequestId = 31 }, default, true);
			await books.InsertAsync(new LmsTaskBook { DepartmentId = Department, LmsTaskBookDefinitionId = definition.LmsTaskBookDefinitionId, UserId = UserB, PositionCode = "FFT2",
				Name = "FFT2", InitiatedOn = now, ExpiresOn = now.AddYears(3), Status = (int)LmsTaskBookStatuses.Open }, default, true);
			await books.InsertAsync(new LmsTaskBook { DepartmentId = OtherDepartment, LmsTaskBookDefinitionId = 1, UserId = UserB, PositionCode = "FFT2",
				Name = "FFT2", InitiatedOn = now.AddYears(-4), ExpiresOn = now.AddYears(-1), Status = (int)LmsTaskBookStatuses.Certified }, default, true);

			var due = await books.GetExpiredOpenAsync(now, 50);
			due.Should().ContainSingle().Which.LmsTaskBookId.Should().Be(expired.LmsTaskBookId);
			(await books.GetForMemberAsync(Department, UserA.ToUpperInvariant())).Should().ContainSingle();
			(await books.GetForDepartmentAsync(Department, true)).Should().HaveCount(2);
			(await books.GetBySignatureRequestAsync(Department, 31)).LmsTaskBookId.Should().Be(expired.LmsTaskBookId);

			expired.Status = (int)LmsTaskBookStatuses.Expired;
			(await books.TryUpdateAsync(expired, 0)).Should().BeTrue();
			(await books.GetExpiredOpenAsync(now, 50)).Should().BeEmpty();
		}

		[Test, Order(6)]
		public async Task Notices_are_read_when_due_claimed_once_and_marked_sent()
		{
			var notifications = Repository((c, s, u, q) => new LmsSkillNotificationsRepository(c, s, u, q));
			var now = DateTime.UtcNow;
			var due = await notifications.InsertAsync(new LmsSkillNotification { DepartmentId = Department, UserId = UserA, Kind = 0, Text = "Passed", CreatedOn = now, DueOn = now.AddMinutes(-1) }, default, true);
			await notifications.InsertAsync(new LmsSkillNotification { DepartmentId = Department, UserId = UserA, Kind = 0, Text = "Tomorrow", CreatedOn = now, DueOn = now.AddDays(1) }, default, true);

			var read = await notifications.GetDueAsync(now, now.AddMinutes(-10), 100);
			read.Should().ContainSingle().Which.LmsSkillNotificationId.Should().Be(due.LmsSkillNotificationId);
			(await notifications.ClaimAsync(due.LmsSkillNotificationId, now, now.AddMinutes(-10))).Should().BeTrue();
			(await notifications.ClaimAsync(due.LmsSkillNotificationId, now, now.AddMinutes(-10))).Should().BeFalse("a second worker cannot claim it");
			(await notifications.GetDueAsync(now, now.AddMinutes(-10), 100)).Should().BeEmpty("a fresh claim hides it");
			(await notifications.GetDueAsync(now.AddMinutes(20), now.AddMinutes(10), 100)).Should().ContainSingle("a stale claim is read again");

			await notifications.MarkSentAsync(new[] { due.LmsSkillNotificationId }, now, false);
			(await notifications.GetDueAsync(now.AddDays(2), now.AddDays(2), 100)).Select(n => n.Text).Should().Equal("Tomorrow");
			(await notifications.GetForUserAsync(Department, UserA.ToUpperInvariant(), 10)).Should().HaveCount(2);
		}

		[Test, Order(7)]
		public async Task Department_cleanup_removes_this_departments_rows_only()
		{
			foreach (var table in Tables)
				(await ScalarAsync($"SELECT COUNT(*) FROM {T(table)} WHERE {(IsPostgres ? "departmentid" : "[DepartmentId]")} = @d", new { d = Department })).Should()
					.BeGreaterThan(0, table + " was seeded for the department");

			await using (var database = Connect(_connection))
			{
				await database.OpenAsync();
				await using var transaction = await database.BeginTransactionAsync();
				await UnlinkedDepartmentCleanup.DeleteWithinTransactionAsync(database, transaction, Department, type);
				await transaction.CommitAsync();
			}

			foreach (var table in Tables)
				(await ScalarAsync($"SELECT COUNT(*) FROM {T(table)} WHERE {(IsPostgres ? "departmentid" : "[DepartmentId]")} = @d", new { d = Department })).Should().Be(0, table);
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("LmsSkillEvaluations")} WHERE {(IsPostgres ? "departmentid" : "[DepartmentId]")} = @d", new { d = OtherDepartment })).Should().BeGreaterThan(0);
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("LmsTaskBooks")} WHERE {(IsPostgres ? "departmentid" : "[DepartmentId]")} = @d", new { d = OtherDepartment })).Should().BeGreaterThan(0);
		}
	}
}
