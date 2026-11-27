using System;
using System.Collections.Concurrent;
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
	/// Real-database proof for Training T5 standards (M0392–M0394) on both engines: the migrations re-run as no-ops, roll back and apply again;
	/// the package claim and move; one registration per enrollment and lesson with the optimistic save; launch tokens looked up by hash, the
	/// cmi5 fetch and the LTI hint consumed once; statement ids and assertion jtis unique; platform key retirement; the xAPI outbox claim,
	/// finish, counts, requeue and purge; the new LmsLessons columns; department cleanup for that department only. Set
	/// RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class LmsStandardsDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "lms_standards_";
		private const string UserA = "4c1f2a33-1f3b-4d2c-9a6e-0b1c2d3e4f71";
		private const int Department = 4601;
		private const int OtherDepartment = 4602;

		private static readonly string[] Tables =
		{
			"LmsContentPackages", "LmsRuntimeRegistrations", "LmsRuntimeSessions", "LmsCmi5Statements", "LmsCmi5StateDocuments", "LmsLtiTools", "LmsLtiPlatformKeys",
			"LmsLtiAccessTokens", "LmsXapiOutbox"
		};

		private DatabaseTypes _previousType;
		private string _previousConnection, _master, _connection, _database;

		private bool IsPostgres => type == DatabaseTypes.Postgres;

		private DbConnection Connect(string connection) => IsPostgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);

		private string T(string name) => IsPostgres ? name.ToLowerInvariant() : "[" + name + "]";

		private string C(string name) => IsPostgres ? name.ToLowerInvariant() : "[" + name + "]";

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

		private string LessonColumns() => IsPostgres
			? "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'lmslessons' AND column_name IN ('lmscontentpackageid', 'lmsltitoolid', 'ltitargetlinkuri', 'lticustomparameters')"
			: "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'LmsLessons' AND COLUMN_NAME IN ('LmsContentPackageId', 'LmsLtiToolId', 'LtiTargetLinkUri', 'LtiCustomParameters')";

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
			(await ScalarAsync(LessonColumns())).Should().Be(4);
			var version = IsPostgres ? "\"VersionInfo\"" : "[VersionInfo]";
			var versionColumn = IsPostgres ? "\"Version\"" : "[Version]";
			await ExecuteAsync($"DELETE FROM {version} WHERE {versionColumn} >= 392");

			using (var runner = Runner())
			using (var scope = runner.CreateScope())
			{
				var migrations = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
				migrations.MigrateUp();
				(await ScalarAsync(Exists(Tables))).Should().Be(Tables.Length, "a second Up over existing tables is a no-op");
				(await ScalarAsync(LessonColumns())).Should().Be(4);

				migrations.MigrateDown(391);
				(await ScalarAsync(Exists(Tables))).Should().Be(0);
				(await ScalarAsync(LessonColumns())).Should().Be(0);

				migrations.MigrateUp();
			}

			(await ScalarAsync(Exists(Tables))).Should().Be(Tables.Length);
			(await ScalarAsync(LessonColumns())).Should().Be(4);
		}

		[Test, Order(2)]
		public async Task Packages_are_claimed_once_moved_by_status_and_listed_without_removed_ones()
		{
			var packages = Repository((c, s, u, q) => new LmsContentPackagesRepository(c, s, u, q));
			var now = DateTime.UtcNow;
			var package = await packages.InsertAsync(new LmsContentPackage
			{
				DepartmentId = Department, Standard = (int)LmsContentStandards.Scorm12, Title = "SCBA", Status = (int)LmsContentPackageStatuses.Queued, SourceKey = "lms/x/source.zip",
				ContentPrefix = "lms/x/content/", ManifestJson = new LmsPackageManifest { Title = "SCBA" }.ToJson(), CreatedOn = now.AddMinutes(-1), CreatedByUserId = UserA
			}, default, true);
			await packages.InsertAsync(new LmsContentPackage { DepartmentId = Department, Title = "Removed", IsDeleted = true, Status = (int)LmsContentPackageStatuses.Queued, CreatedOn = now }, default, true);
			await packages.InsertAsync(new LmsContentPackage { DepartmentId = OtherDepartment, Title = "Theirs", Status = (int)LmsContentPackageStatuses.Ready, CreatedOn = now }, default, true);

			(await packages.GetForDepartmentAsync(Department)).Select(p => p.Title).Should().Equal("SCBA");
			(await packages.GetForDepartmentAsync(Department, true)).Should().HaveCount(2);
			(await packages.GetByIdForDepartmentAsync(OtherDepartment, package.LmsContentPackageId)).Should().BeNull();

			var claimed = await packages.ClaimNextAsync(now, now.AddMinutes(-30));
			claimed.LmsContentPackageId.Should().Be(package.LmsContentPackageId);
			claimed.Status.Should().Be((int)LmsContentPackageStatuses.Processing);
			claimed.Attempts.Should().Be(1);
			(await packages.ClaimNextAsync(now, now.AddMinutes(-30))).Should().BeNull("the removed package is never claimed and the claimed one is fresh");
			(await packages.ClaimNextAsync(now.AddHours(1), now.AddMinutes(10))).Should().NotBeNull("a stale claim goes back to the queue");

			claimed.Status = (int)LmsContentPackageStatuses.Ready;
			claimed.FileCount = 12;
			claimed.ContentBytes = 4096;
			claimed.ReadyOn = now;
			(await packages.TryMoveAsync(claimed, LmsContentPackageStatuses.Processing)).Should().BeTrue();
			(await packages.TryMoveAsync(claimed, LmsContentPackageStatuses.Processing)).Should().BeFalse("it is Ready now");
			var stored = await packages.GetByIdForDepartmentAsync(Department, package.LmsContentPackageId);
			stored.FileCount.Should().Be(12);
			LmsPackageManifest.Parse(stored.ManifestJson).Title.Should().Be("SCBA");
		}

		[Test, Order(3)]
		public async Task One_registration_per_enrollment_and_lesson_and_sessions_consumed_once()
		{
			var registrations = Repository((c, s, u, q) => new LmsRuntimeRegistrationsRepository(c, s, u, q));
			var sessions = Repository((c, s, u, q) => new LmsRuntimeSessionsRepository(c, s, u, q));
			var now = DateTime.UtcNow;
			LmsRuntimeRegistration New() => new LmsRuntimeRegistration
			{
				DepartmentId = Department, LmsEnrollmentId = 70, LmsLessonId = 80, UserId = UserA, Kind = (int)LmsRuntimeKinds.Lti, LmsLtiToolId = 5, RegistrationKey = "subject-1",
				StateJson = new LmsLtiScoreState { ScoreGiven = 4, ScoreMaximum = 5 }.ToJson(), CreatedOn = now
			};

			var (first, inserted) = await registrations.InsertOrGetAsync(New());
			inserted.Should().BeTrue();
			var (second, again) = await registrations.InsertOrGetAsync(New());
			again.Should().BeFalse();
			second.LmsRuntimeRegistrationId.Should().Be(first.LmsRuntimeRegistrationId);
			(await registrations.GetForLessonSubjectAsync(Department, 80, "subject-1")).LmsRuntimeRegistrationId.Should().Be(first.LmsRuntimeRegistrationId);
			(await registrations.GetForLessonSubjectAsync(OtherDepartment, 80, "subject-1")).Should().BeNull();

			first.Satisfied = true;
			first.ScorePercent = 80.5m;
			(await registrations.TryUpdateAsync(first, 0)).Should().BeTrue();
			(await registrations.TryUpdateAsync(first, 0)).Should().BeFalse("stale row version");
			var stored = await registrations.GetAsync(Department, 70, 80);
			stored.Satisfied.Should().BeTrue();
			stored.ScorePercent.Should().Be(80.5m);
			LmsLtiScoreState.Parse(stored.StateJson).ScoreGiven.Should().Be(4);

			var session = await sessions.InsertAsync(new LmsRuntimeSession
			{
				DepartmentId = Department, LmsRuntimeRegistrationId = first.LmsRuntimeRegistrationId, LmsEnrollmentId = 70, LmsLessonId = 80, UserId = UserA, Kind = (int)LmsRuntimeKinds.Cmi5,
				ItemIdentifier = "https://example.test/au/1", SessionKey = Guid.NewGuid().ToString(), TokenHash = LmsLearnerIdentity.HashToken("t1"),
				FetchTokenHash = LmsLearnerIdentity.HashToken("f1"), CreatedOn = now, ExpiresOn = now.AddHours(8)
			}, default, true);
			(await sessions.GetByTokenHashAsync(LmsLearnerIdentity.HashToken("t1"))).LmsRuntimeSessionId.Should().Be(session.LmsRuntimeSessionId);
			(await sessions.GetByTokenHashAsync(LmsLearnerIdentity.HashToken("T1"))).Should().BeNull("token hashes are exact");
			(await sessions.GetByFetchTokenHashAsync(LmsLearnerIdentity.HashToken("f1"))).Should().NotBeNull();
			(await sessions.TryMarkFetchedAsync(session.LmsRuntimeSessionId, LmsLearnerIdentity.HashToken("a1"), now)).Should().BeTrue();
			(await sessions.TryMarkFetchedAsync(session.LmsRuntimeSessionId, LmsLearnerIdentity.HashToken("a2"), now)).Should().BeFalse("the fetch URL works once");
			(await sessions.GetByAuthTokenHashAsync(LmsLearnerIdentity.HashToken("a1"))).Should().NotBeNull();
			(await sessions.GetByAuthTokenHashAsync(LmsLearnerIdentity.HashToken("a2"))).Should().BeNull();
			(await sessions.TryMarkUsedAsync(session.LmsRuntimeSessionId, now)).Should().BeTrue();
			(await sessions.TryMarkUsedAsync(session.LmsRuntimeSessionId, now)).Should().BeFalse();
			await sessions.MarkEndedAsync(session.LmsRuntimeSessionId, now);
			(await sessions.GetByTokenHashAsync(LmsLearnerIdentity.HashToken("t1"))).EndedOn.Should().NotBeNull();

			await sessions.InsertAsync(new LmsRuntimeSession
			{
				DepartmentId = Department, LmsRuntimeRegistrationId = first.LmsRuntimeRegistrationId, UserId = UserA, Kind = 0, TokenHash = LmsLearnerIdentity.HashToken("old"),
				CreatedOn = now.AddDays(-9), ExpiresOn = now.AddDays(-8)
			}, default, true);
			(await sessions.DeleteExpiredAsync(now.AddDays(-7))).Should().Be(1);
		}

		[Test, Order(4)]
		public async Task Statement_ids_and_state_documents_are_unique_per_registration()
		{
			var statements = Repository((c, s, u, q) => new LmsCmi5StatementsRepository(c, s, u, q));
			var documents = Repository((c, s, u, q) => new LmsCmi5StateDocumentsRepository(c, s, u, q));
			var id = Guid.NewGuid().ToString();
			LmsCmi5Statement Statement(int registration) => new LmsCmi5Statement
			{
				DepartmentId = Department, LmsRuntimeRegistrationId = registration, LmsRuntimeSessionId = 1, StatementId = id, VerbId = LmsCmi5Rules.Completed,
				StatementJson = "{\"verb\":{\"id\":\"" + LmsCmi5Rules.Completed + "\"}}", StoredOn = DateTime.UtcNow
			};

			(await statements.TryInsertAsync(Statement(1))).Should().BeTrue();
			(await statements.TryInsertAsync(Statement(1))).Should().BeFalse("the same statement id twice in one registration");
			(await statements.TryInsertAsync(Statement(2))).Should().BeTrue("another registration may reuse it");
			(await statements.CountForRegistrationAsync(Department, 1)).Should().Be(1);
			(await statements.GetByStatementIdAsync(Department, 1, id)).VerbId.Should().Be(LmsCmi5Rules.Completed);
			(await statements.GetForRegistrationAsync(Department, 2, 10)).Should().ContainSingle();

			await documents.InsertAsync(new LmsCmi5StateDocument
			{
				DepartmentId = Department, LmsRuntimeRegistrationId = 1, ActivityId = "https://example.test/au/1", StateId = "bookmark", ContentType = "application/json",
				Content = "{\"page\":2}", UpdatedOn = DateTime.UtcNow
			}, default, true);
			(await documents.GetAsync(Department, 1, "https://example.test/au/1", "bookmark")).Content.Should().Be("{\"page\":2}");
			(await documents.GetAsync(Department, 1, "https://example.test/AU/1", "bookmark")).Should().BeNull("activity ids are exact IRIs");
			(await documents.DeleteAsync(Department, 1, "https://example.test/au/1", null)).Should().Be(1);
			await documents.InsertAsync(new LmsCmi5StateDocument { DepartmentId = Department, LmsRuntimeRegistrationId = 1, ActivityId = "a", StateId = "s", Content = "x", UpdatedOn = DateTime.UtcNow }, default, true);
		}

		[Test, Order(5)]
		public async Task Tools_keys_and_tokens_keep_their_identifiers_exact_and_jtis_unique()
		{
			var tools = Repository((c, s, u, q) => new LmsLtiToolsRepository(c, s, u, q));
			var keys = Repository((c, s, u, q) => new LmsLtiPlatformKeysRepository(c, s, u, q));
			var tokens = Repository((c, s, u, q) => new LmsLtiAccessTokensRepository(c, s, u, q));
			var now = DateTime.UtcNow;
			var clientId = Guid.NewGuid().ToString();

			var tool = await tools.InsertAsync(new LmsLtiTool
			{
				DepartmentId = Department, Name = "CE Provider", ClientId = clientId, DeploymentId = "d1", OidcLoginUrl = "https://tool.test/login", LaunchUrl = "https://tool.test/launch",
				PublicJwksJson = "{\"keys\":[]}", AllowGradePassback = true, IsActive = true, CreatedOn = now
			}, default, true);
			(await tools.GetByClientIdAsync(Department, clientId)).LmsLtiToolId.Should().Be(tool.LmsLtiToolId);
			(await tools.GetByClientIdAsync(OtherDepartment, clientId)).Should().BeNull();
			(await tools.GetForDepartmentAsync(Department)).Should().ContainSingle().Which.AllowGradePassback.Should().BeTrue();

			var oldKey = await keys.InsertAsync(new LmsLtiPlatformKey { DepartmentId = Department, Kid = "k1", PublicJwkJson = "{}", PrivateKeyEncrypted = "enc", Status = 0, CreatedOn = now.AddDays(-1) }, default, true);
			var newKey = await keys.InsertAsync(new LmsLtiPlatformKey { DepartmentId = Department, Kid = "k2", PublicJwkJson = "{}", PrivateKeyEncrypted = "enc", Status = 0, CreatedOn = now }, default, true);
			(await keys.RetireOthersAsync(Department, newKey.LmsLtiPlatformKeyId, now)).Should().Be(1);
			var all = await keys.GetForDepartmentAsync(Department);
			all.Select(k => k.Kid).Should().Equal("k2", "k1");
			all.Single(k => k.LmsLtiPlatformKeyId == oldKey.LmsLtiPlatformKeyId).Status.Should().Be((int)LmsLtiKeyStatuses.Retired);

			LmsLtiAccessToken Token(string hash, string jti) => new LmsLtiAccessToken
			{
				DepartmentId = Department, LmsLtiToolId = tool.LmsLtiToolId, TokenHash = hash, Scopes = LmsLtiRules.ScopeScore, AssertionJti = jti, CreatedOn = now, ExpiresOn = now.AddHours(1)
			};
			(await tokens.TryInsertAsync(Token(LmsLearnerIdentity.HashToken("b1"), "jti-1"))).Should().BeTrue();
			(await tokens.TryInsertAsync(Token(LmsLearnerIdentity.HashToken("b2"), "jti-1"))).Should().BeFalse("a replayed assertion");
			(await tokens.GetByHashAsync(Department, LmsLearnerIdentity.HashToken("b1"))).Scopes.Should().Be(LmsLtiRules.ScopeScore);
			(await tokens.GetByHashAsync(OtherDepartment, LmsLearnerIdentity.HashToken("b1"))).Should().BeNull();
			(await tokens.DeleteExpiredAsync(now.AddHours(2))).Should().Be(1);
			await tokens.TryInsertAsync(Token(LmsLearnerIdentity.HashToken("b3"), "jti-3"));
		}

		[Test, Order(6)]
		public async Task The_outbox_claims_due_statements_finishes_counts_requeues_and_purges()
		{
			var outbox = Repository((c, s, u, q) => new LmsXapiOutboxRepository(c, s, u, q));
			var now = DateTime.UtcNow;
			LmsXapiOutboxItem Item(int department, DateTime next, int status = 0) => new LmsXapiOutboxItem
			{
				DepartmentId = department, StatementId = Guid.NewGuid().ToString(), UserId = UserA, VerbId = LmsXapiVerbs.Completed, StatementJson = "{}", Status = status,
				NextAttemptOn = next, CreatedOn = now.AddMinutes(-5)
			};
			var due = await outbox.InsertAsync(Item(Department, now.AddMinutes(-1)), default, true);
			await outbox.InsertAsync(Item(Department, now.AddHours(1)), default, true);
			await outbox.InsertAsync(Item(OtherDepartment, now.AddMinutes(-1)), default, true);

			var claimed = await outbox.ClaimDueAsync(now, now.AddMinutes(-10), 10);
			claimed.Should().HaveCount(2);
			claimed.Should().OnlyContain(i => i.Status == (int)LmsXapiOutboxStatuses.Sending && i.Attempts == 1);
			(await outbox.ClaimDueAsync(now, now.AddMinutes(-10), 10)).Should().BeEmpty("claimed rows are not claimed again while fresh");

			var mine = claimed.Single(i => i.LmsXapiOutboxItemId == due.LmsXapiOutboxItemId);
			mine.Status = (int)LmsXapiOutboxStatuses.Delivered;
			mine.DeliveredOn = now;
			(await outbox.TryFinishAsync(mine)).Should().BeTrue();
			(await outbox.TryFinishAsync(mine)).Should().BeFalse("no longer Sending");
			var theirs = claimed.Single(i => i.DepartmentId == OtherDepartment);
			theirs.Status = (int)LmsXapiOutboxStatuses.Failed;
			theirs.LastError = "HTTP 400";
			(await outbox.TryFinishAsync(theirs)).Should().BeTrue();

			(await outbox.GetCountsAsync(Department)).Should().Be((1, 0, 1));
			(await outbox.GetCountsAsync(OtherDepartment)).Should().Be((0, 1, 0));
			(await outbox.GetRecentFailedAsync(OtherDepartment, 10)).Should().ContainSingle().Which.LastError.Should().Be("HTTP 400");
			(await outbox.RequeueFailedAsync(OtherDepartment, now)).Should().Be(1);
			(await outbox.GetCountsAsync(OtherDepartment)).Should().Be((1, 0, 0));
			(await outbox.PurgeDeliveredAsync(now.AddMinutes(1))).Should().Be(1);
			(await outbox.GetCountsAsync(Department)).Should().Be((1, 0, 0));
		}

		[Test, Order(7)]
		public async Task A_lesson_keeps_its_package_tool_target_and_custom_parameters()
		{
			var courses = Repository((c, s, u, q) => new LmsCoursesRepository(c, s, u, q));
			var versions = Repository((c, s, u, q) => new LmsCourseVersionsRepository(c, s, u, q));
			var modules = Repository((c, s, u, q) => new LmsCourseModulesRepository(c, s, u, q));
			var lessons = Repository((c, s, u, q) => new LmsLessonsRepository(c, s, u, q));
			var now = DateTime.UtcNow;
			var course = await courses.InsertAsync(new LmsCourse { DepartmentId = Department, Title = "Standards", CreatedOn = now }, default, true);
			var version = await versions.InsertAsync(new LmsCourseVersion { DepartmentId = Department, LmsCourseId = course.LmsCourseId, Title = "Standards", CreatedOn = now }, default, true);
			var module = await modules.InsertAsync(new LmsCourseModule { DepartmentId = Department, LmsCourseVersionId = version.LmsCourseVersionId, SortOrder = 1 }, default, true);

			var lesson = await lessons.InsertAsync(new LmsLesson
			{
				DepartmentId = Department, LmsCourseVersionId = version.LmsCourseVersionId, LmsCourseModuleId = module.LmsCourseModuleId, Kind = (int)LmsLessonKinds.LtiLink, Title = "ACLS",
				LmsLtiToolId = 9, LtiTargetLinkUri = "https://tool.test/acls", LtiCustomParameters = "course=ACLS", PassMark = 70
			}, default, true);
			var stored = (await lessons.GetForVersionAsync(Department, version.LmsCourseVersionId)).Single(l => l.LmsLessonId == lesson.LmsLessonId);
			stored.Kind.Should().Be((int)LmsLessonKinds.LtiLink);
			stored.LmsLtiToolId.Should().Be(9);
			stored.LtiTargetLinkUri.Should().Be("https://tool.test/acls");
			stored.LtiCustomParameters.Should().Be("course=ACLS");
			stored.LmsContentPackageId.Should().BeNull();
		}

		[Test, Order(8)]
		public async Task Department_cleanup_removes_this_departments_rows_only()
		{
			foreach (var table in Tables)
				(await ScalarAsync($"SELECT COUNT(*) FROM {T(table)} WHERE {C("DepartmentId")} = @d", new { d = Department })).Should().BeGreaterThan(0, table + " was seeded for the department");
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("LmsXapiOutbox")} WHERE {C("DepartmentId")} = @d", new { d = OtherDepartment })).Should().BeGreaterThan(0);

			await using (var database = Connect(_connection))
			{
				await database.OpenAsync();
				await using var transaction = await database.BeginTransactionAsync();
				await UnlinkedDepartmentCleanup.DeleteWithinTransactionAsync(database, transaction, Department, type);
				await transaction.CommitAsync();
			}

			foreach (var table in Tables)
				(await ScalarAsync($"SELECT COUNT(*) FROM {T(table)} WHERE {C("DepartmentId")} = @d", new { d = Department })).Should().Be(0, table);
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("LmsXapiOutbox")} WHERE {C("DepartmentId")} = @d", new { d = OtherDepartment })).Should().BeGreaterThan(0);
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("LmsContentPackages")} WHERE {C("DepartmentId")} = @d", new { d = OtherDepartment })).Should().BeGreaterThan(0);
		}
	}
}
