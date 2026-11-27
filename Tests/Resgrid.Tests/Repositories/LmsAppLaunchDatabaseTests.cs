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
	/// Real-database proof for round 10 A8 (M0421, LmsRuntimeSessions.AppSessionId) on both engines: the migration re-runs as a no-op,
	/// rolls back to exactly the table before it and applies again; the in-app launch link is consumed once; the newest session an app session
	/// minted is found for its department only; the column round-trips; department cleanup removes the new kinds of row too. Set
	/// RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class LmsAppLaunchDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "lms_app_launch_";
		private const string UserA = "5d2e3b44-2a4c-4e3d-8b7f-1c2d3e4f5a62";
		private const int Department = 4701;
		private const int OtherDepartment = 4702;
		private const long Version = 421;

		private DatabaseTypes _previousType;
		private string _previousConnection, _master, _connection, _database;

		private bool IsPostgres => type == DatabaseTypes.Postgres;

		private DbConnection Connect(string connection) => IsPostgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);

		private string T(string name) => IsPostgres ? name.ToLowerInvariant() : "[" + name + "]";

		private string C(string name) => IsPostgres ? name.ToLowerInvariant() : "[" + name + "]";

		private LmsRuntimeSessionsRepository Sessions()
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
			return new LmsRuntimeSessionsRepository(connections.Object, configuration, Mock.Of<IUnitOfWork>(), new QueryFactory(list.Object));
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

		private string ColumnExists() => IsPostgres
			? "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'lmsruntimesessions' AND column_name = 'appsessionid'"
			: "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'LmsRuntimeSessions' AND COLUMN_NAME = 'AppSessionId'";

		private string IndexExists() => IsPostgres
			? "SELECT COUNT(*) FROM pg_indexes WHERE schemaname = 'public' AND indexname = 'ix_lmsruntimesessions_appsession'"
			: "SELECT COUNT(*) FROM sys.indexes WHERE name = 'IX_LmsRuntimeSessions_AppSession' AND object_id = OBJECT_ID('dbo.LmsRuntimeSessions')";

		private string Columns() => IsPostgres
			? "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'lmsruntimesessions'"
			: "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'LmsRuntimeSessions'";

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
		public async Task The_migration_reruns_as_a_no_op_rolls_back_to_the_table_before_it_and_applies_again()
		{
			(await ScalarAsync(ColumnExists())).Should().Be(1);
			(await ScalarAsync(IndexExists())).Should().Be(1);
			var columns = await ScalarAsync(Columns());
			var version = IsPostgres ? "\"VersionInfo\"" : "[VersionInfo]";
			var versionColumn = IsPostgres ? "\"Version\"" : "[Version]";
			await ExecuteAsync($"DELETE FROM {version} WHERE {versionColumn} >= {Version}");

			using (var runner = Runner())
			using (var scope = runner.CreateScope())
			{
				var migrations = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
				migrations.MigrateUp();
				(await ScalarAsync(ColumnExists())).Should().Be(1, "a second Up over the existing column is a no-op");
				(await ScalarAsync(IndexExists())).Should().Be(1);

				migrations.MigrateDown(Version - 1);
				(await ScalarAsync(ColumnExists())).Should().Be(0);
				(await ScalarAsync(IndexExists())).Should().Be(0);
				(await ScalarAsync(Columns())).Should().Be(columns - 1, "Down removes exactly what Up added");

				migrations.MigrateUp();
			}

			(await ScalarAsync(ColumnExists())).Should().Be(1);
			(await ScalarAsync(IndexExists())).Should().Be(1);
		}

		[Test, Order(2)]
		public async Task A_launch_link_is_consumed_once_and_an_app_sessions_newest_session_is_found_for_its_department_only()
		{
			var sessions = Sessions();
			var now = DateTime.UtcNow;
			LmsRuntimeSession Row(LmsRuntimeKinds kind, string token, int? appSessionId = null, int department = Department) => new LmsRuntimeSession
			{
				DepartmentId = department, LmsRuntimeRegistrationId = 0, LmsEnrollmentId = 70, LmsLessonId = 80, UserId = UserA, Kind = (int)kind,
				TokenHash = LmsLearnerIdentity.HashToken(token), CreatedOn = now, ExpiresOn = now.AddMinutes(5), AppSessionId = appSessionId
			};

			var launch = await sessions.InsertAsync(Row(LmsRuntimeKinds.AppLaunch, "launch-1"), default, true);
			(await sessions.GetByTokenHashAsync(LmsLearnerIdentity.HashToken("launch-1"))).Kind.Should().Be((int)LmsRuntimeKinds.AppLaunch);
			(await sessions.TryMarkUsedAsync(launch.LmsRuntimeSessionId, now)).Should().BeTrue();
			(await sessions.TryMarkUsedAsync(launch.LmsRuntimeSessionId, now)).Should().BeFalse("single use");

			var app = await sessions.InsertAsync(Row(LmsRuntimeKinds.AppSession, "page-1"), default, true);
			(await sessions.GetLatestForAppSessionAsync(Department, app.LmsRuntimeSessionId)).Should().BeNull("nothing minted yet");
			var first = await sessions.InsertAsync(Row(LmsRuntimeKinds.Scorm12, "content-1", app.LmsRuntimeSessionId), default, true);
			var second = await sessions.InsertAsync(Row(LmsRuntimeKinds.Scorm12, "content-2", app.LmsRuntimeSessionId), default, true);
			await sessions.InsertAsync(Row(LmsRuntimeKinds.Scorm12, "web-1"), default, true);
			await sessions.InsertAsync(Row(LmsRuntimeKinds.Scorm12, "other-1", app.LmsRuntimeSessionId, OtherDepartment), default, true);

			(await sessions.GetLatestForAppSessionAsync(Department, app.LmsRuntimeSessionId)).LmsRuntimeSessionId.Should().Be(second.LmsRuntimeSessionId);
			(await sessions.GetLatestForAppSessionAsync(OtherDepartment + 1, app.LmsRuntimeSessionId)).Should().BeNull();
			(await sessions.GetLatestForAppSessionAsync(Department, 0)).Should().BeNull();
			(await sessions.GetByTokenHashAsync(LmsLearnerIdentity.HashToken("content-1"))).AppSessionId.Should().Be(app.LmsRuntimeSessionId);
			(await sessions.GetByTokenHashAsync(LmsLearnerIdentity.HashToken("web-1"))).AppSessionId.Should().BeNull();
			first.LmsRuntimeSessionId.Should().BeLessThan(second.LmsRuntimeSessionId);
		}

		[Test, Order(3)]
		public async Task Department_cleanup_removes_the_app_rows_of_that_department_only()
		{
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("LmsRuntimeSessions")} WHERE {C("DepartmentId")} = @d AND {C("Kind")} IN (4, 5)", new { d = Department })).Should().Be(2);
			await using (var database = Connect(_connection))
			{
				await database.OpenAsync();
				await using var transaction = await database.BeginTransactionAsync();
				await UnlinkedDepartmentCleanup.DeleteWithinTransactionAsync(database, transaction, Department, type);
				await transaction.CommitAsync();
			}
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("LmsRuntimeSessions")} WHERE {C("DepartmentId")} = @d", new { d = Department })).Should().Be(0);
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("LmsRuntimeSessions")} WHERE {C("DepartmentId")} = @d", new { d = OtherDepartment })).Should().Be(1);
		}
	}
}
