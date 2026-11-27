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
	/// Real-database proof for the Workforce Pro add-on migrations (M0422, and M0423's add-on state for the dunning grace) on both engines:
	/// they re-run as a no-op, roll back and apply again (the PlanAddons row for type 8 seeded once with empty price ids, the Workforce.Pro flag
	/// seeded off) and never remove a catalog row something was bought on; seats are unique per department and member whatever the id's case;
	/// the add-on state is one row per department, written over on each save; department cleanup removes the seats and the state for that
	/// department only. Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level
	/// connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class WorkforceProSeatsDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "wp_seats_";
		private const string UserA = "7a1c8b52-6f3e-4d5b-9c1a-1b2c3d4e5f60";
		private const string UserB = "8b2d9c63-7e4f-4e6c-8d2b-2c3d4e5f6a71";
		private const int Department = 4201;
		private const int OtherDepartment = 4202;

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

		private string Tables(params string[] names) => IsPostgres
			? "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name IN (" + string.Join(", ", names.Select(n => "'" + n.ToLowerInvariant() + "'")) + ")"
			: "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME IN (" + string.Join(", ", names.Select(n => "'" + n + "'")) + ")";

		[Test, Order(1)]
		public async Task The_migration_reruns_as_a_no_op_rolls_back_and_applies_again()
		{
			var version = IsPostgres ? "\"VersionInfo\"" : "[VersionInfo]";
			var versionColumn = IsPostgres ? "\"Version\"" : "[Version]";
			await using (var database = Connect(_connection))
				await database.ExecuteAsync($"DELETE FROM {version} WHERE {versionColumn} >= 422");

			using (var runner = Runner())
			using (var scope = runner.CreateScope())
			{
				var migrations = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
				migrations.MigrateUp();
				(await ScalarAsync($"SELECT COUNT(*) FROM {T("PlanAddons")} WHERE {T("AddonType")} = 8")).Should().Be(1, "the catalog row is seeded once");
				(await ScalarAsync(Tables("WorkforceProSeats", "WorkforceProAddonStates"))).Should().Be(2);

				migrations.MigrateDown(422);
				(await ScalarAsync(Tables("WorkforceProAddonStates"))).Should().Be(0, "M0423 rolls back on its own");
				(await ScalarAsync(Tables("WorkforceProSeats"))).Should().Be(1);

				migrations.MigrateDown(421);
				(await ScalarAsync(Tables("WorkforceProSeats", "WorkforceProAddonStates"))).Should().Be(0);
				(await ScalarAsync($"SELECT COUNT(*) FROM {T("PlanAddons")} WHERE {T("AddonType")} = 8")).Should().Be(0, "nothing was bought on the row, so Down removes it");
				(await ScalarAsync($"SELECT COUNT(*) FROM {T("FeatureFlags")} WHERE {T("FlagKey")} = 'Workforce.Pro'")).Should().Be(0);

				migrations.MigrateUp();
			}

			(await ScalarAsync($"SELECT COUNT(*) FROM {T("PlanAddons")} WHERE {T("PlanAddonId")} = @Id AND {T("AddonType")} = 8 AND {T("Cost")} = 119 " +
				$"AND {T("ExternalId")} = '' AND {T("TestExternalId")} = ''", new { Id = WorkforceProAddonConfig.PlanAddonId }))
				.Should().Be(1, "the owner sets the Stripe price ids later; until then checkout fails closed");
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("FeatureFlags")} WHERE {T("FlagKey")} = 'Workforce.Pro' AND {T("IsEnabledGlobally")} = {(IsPostgres ? "false" : "0")}"))
				.Should().Be(1, "general availability is the operator switching the flag on, not a migration");
		}

		[Test, Order(2)]
		public async Task Seats_are_unique_per_department_and_member_whatever_the_ids_case()
		{
			var seats = Repository((c, s, u, q) => new WorkforceProSeatsRepository(c, s, u, q));
			var now = DateTime.UtcNow;

			(await seats.AddAsync(new WorkforceProSeat { DepartmentId = Department, UserId = UserA, EnabledOn = now.AddMinutes(-5), EnabledByUserId = UserB })).Should().BeTrue();
			(await seats.AddAsync(new WorkforceProSeat { DepartmentId = Department, UserId = UserA.ToUpperInvariant(), EnabledOn = now })).Should().BeFalse("a repeat is not an error, whatever the case");
			(await seats.AddAsync(new WorkforceProSeat { DepartmentId = Department, UserId = UserB, EnabledOn = now })).Should().BeTrue();
			(await seats.AddAsync(new WorkforceProSeat { DepartmentId = OtherDepartment, UserId = UserA, EnabledOn = now })).Should().BeTrue("seats are per department");
			(await seats.AddAsync(new WorkforceProSeat { DepartmentId = 0, UserId = UserA, EnabledOn = now })).Should().BeFalse();

			var rows = await seats.GetForDepartmentAsync(Department);
			rows.Select(r => r.UserId).Should().Equal(UserA, UserB);
			rows[0].EnabledByUserId.Should().Be(UserB);
			rows[0].EnabledOn.Should().BeCloseTo(now.AddMinutes(-5), TimeSpan.FromSeconds(1));

			(await seats.RemoveAsync(Department, new[] { UserB.ToUpperInvariant() })).Should().Be(1, "member ids compare case-insensitively");
			(await seats.RemoveAsync(Department, Array.Empty<string>())).Should().Be(0);
			(await seats.GetForDepartmentAsync(Department)).Select(r => r.UserId).Should().Equal(UserA);
			(await seats.GetForDepartmentAsync(OtherDepartment)).Should().ContainSingle();
		}

		[Test, Order(3)]
		public async Task The_add_on_state_is_one_row_per_department_and_a_save_writes_over_it()
		{
			var states = Repository((c, s, u, q) => new WorkforceProAddonStatesRepository(c, s, u, q));
			var paidThrough = new DateTime(2026, 12, 1, 8, 30, 0, DateTimeKind.Utc);

			(await states.GetForDepartmentAsync(Department)).Should().BeNull();
			await states.SaveForDepartmentAsync(new WorkforceProAddonState
			{
				DepartmentId = Department, Packs = 3, PaidThroughUtc = paidThrough, LastActiveOn = paidThrough.AddDays(-2), UpdatedOn = paidThrough.AddDays(-2)
			});
			await states.SaveForDepartmentAsync(new WorkforceProAddonState { DepartmentId = OtherDepartment, Packs = 1, UpdatedOn = paidThrough });

			var row = await states.GetForDepartmentAsync(Department);
			row.Packs.Should().Be(3);
			row.PaidThroughUtc.Should().BeCloseTo(paidThrough, TimeSpan.FromSeconds(1));
			row.RenewalCancelled.Should().BeFalse();
			row.LapsedSinceUtc.Should().BeNull();

			// The gate records the end of a failed renewal's grace over the same row.
			row.LapsedSinceUtc = paidThrough.AddDays(7);
			row.RenewalCancelled = true;
			row.UpdatedOn = paidThrough.AddDays(7);
			await states.SaveForDepartmentAsync(row);

			(await ScalarAsync($"SELECT COUNT(*) FROM {T("WorkforceProAddonStates")} WHERE {T("DepartmentId")} = @D", new { D = Department })).Should().Be(1);
			var ended = await states.GetForDepartmentAsync(Department);
			ended.LapsedSinceUtc.Should().BeCloseTo(paidThrough.AddDays(7), TimeSpan.FromSeconds(1));
			ended.RenewalCancelled.Should().BeTrue();
			(await states.GetForDepartmentAsync(OtherDepartment)).Packs.Should().Be(1);
		}

		[Test, Order(4)]
		public async Task Department_cleanup_removes_the_seats_for_that_department_only()
		{
			await using (var database = Connect(_connection))
			{
				await database.OpenAsync();
				await using var transaction = await database.BeginTransactionAsync();
				await UnlinkedDepartmentCleanup.DeleteWithinTransactionAsync(database, transaction, Department, type);
				await transaction.CommitAsync();
			}

			(await ScalarAsync($"SELECT COUNT(*) FROM {T("WorkforceProSeats")} WHERE {T("DepartmentId")} = @D", new { D = Department })).Should().Be(0);
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("WorkforceProSeats")} WHERE {T("DepartmentId")} = @D", new { D = OtherDepartment })).Should().Be(1);
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("WorkforceProAddonStates")} WHERE {T("DepartmentId")} = @D", new { D = Department })).Should().Be(0);
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("WorkforceProAddonStates")} WHERE {T("DepartmentId")} = @D", new { D = OtherDepartment })).Should().Be(1);
		}
	}
}
