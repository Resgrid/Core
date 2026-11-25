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
	/// Real-database proof for LMS general availability (M0366, M0367) on both engines: the migrations re-run as no-ops, roll back and
	/// apply again (the PlanAddons row seeded once, the flag still off); seats are unique per department and member whatever the id's
	/// case; the add-on state is one row per department; at most one lapse is open per department; department cleanup removes the
	/// new tables for that department only. Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION
	/// (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class LmsSeatsDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "lms_seats_";
		private const string UserA = "7a1c8b52-6f3e-4d5b-9c1a-1b2c3d4e5f60";
		private const string UserB = "8b2d9c63-7e4f-4e6c-8d2b-2c3d4e5f6a71";
		private const int Department = 4101;
		private const int OtherDepartment = 4102;

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
		public async Task The_migrations_rerun_as_no_ops_roll_back_and_apply_again()
		{
			var version = IsPostgres ? "\"VersionInfo\"" : "[VersionInfo]";
			var versionColumn = IsPostgres ? "\"Version\"" : "[Version]";
			await using (var database = Connect(_connection))
				await database.ExecuteAsync($"DELETE FROM {version} WHERE {versionColumn} >= 366");

			using (var runner = Runner())
			using (var scope = runner.CreateScope())
			{
				var migrations = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
				migrations.MigrateUp();
				(await ScalarAsync($"SELECT COUNT(*) FROM {T("PlanAddons")} WHERE {T("AddonType")} = 9")).Should().Be(1, "the catalog row is seeded once");
				(await ScalarAsync(Tables("LmsMemberSeats", "LmsAddonStates", "LmsAddonLapses"))).Should().Be(3);

				migrations.MigrateDown(365);
				(await ScalarAsync(Tables("LmsMemberSeats", "LmsAddonStates", "LmsAddonLapses"))).Should().Be(0);
				(await ScalarAsync($"SELECT COUNT(*) FROM {T("PlanAddons")} WHERE {T("AddonType")} = 9")).Should().Be(0, "nothing was bought on the row, so Down removes it");

				migrations.MigrateUp();
			}

			(await ScalarAsync($"SELECT COUNT(*) FROM {T("PlanAddons")} WHERE {T("PlanAddonId")} = @Id AND {T("AddonType")} = 9", new { Id = LmsAddonConfig.PlanAddonId })).Should().Be(1);
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("FeatureFlags")} WHERE {T("FlagKey")} = 'Training.Lms' AND {T("IsEnabledGlobally")} = {(IsPostgres ? "false" : "0")}"))
				.Should().Be(1, "general availability is the operator switching the flag on, not a migration");
		}

		[Test, Order(2)]
		public async Task Seats_are_unique_per_department_and_member_whatever_the_ids_case()
		{
			var seats = Repository((c, s, u, q) => new LmsMemberSeatsRepository(c, s, u, q));
			var now = DateTime.UtcNow;

			(await seats.AddAsync(new LmsMemberSeat { DepartmentId = Department, UserId = UserA, EnabledOn = now.AddMinutes(-5), EnabledByUserId = UserB })).Should().BeTrue();
			(await seats.AddAsync(new LmsMemberSeat { DepartmentId = Department, UserId = UserA, EnabledOn = now })).Should().BeFalse("a repeat is not an error");
			(await seats.AddAsync(new LmsMemberSeat { DepartmentId = Department, UserId = UserB, EnabledOn = now })).Should().BeTrue();
			(await seats.AddAsync(new LmsMemberSeat { DepartmentId = OtherDepartment, UserId = UserA, EnabledOn = now })).Should().BeTrue("seats are per department");

			var rows = await seats.GetForDepartmentAsync(Department);
			rows.Select(r => r.UserId).Should().Equal(UserA, UserB);
			rows[0].EnabledByUserId.Should().Be(UserB);

			(await seats.RemoveAsync(Department, new[] { UserB.ToUpperInvariant() })).Should().Be(1, "member ids compare case-insensitively");
			(await seats.GetForDepartmentAsync(Department)).Select(r => r.UserId).Should().Equal(UserA);
			(await seats.GetForDepartmentAsync(OtherDepartment)).Should().ContainSingle();
		}

		[Test, Order(3)]
		public async Task The_add_on_state_is_one_row_per_department()
		{
			var states = Repository((c, s, u, q) => new LmsAddonStatesRepository(c, s, u, q));
			var paidThrough = new DateTime(2027, 1, 31, 12, 0, 0, DateTimeKind.Utc);

			(await states.GetForDepartmentAsync(Department)).Should().BeNull();
			await states.SaveForDepartmentAsync(new LmsAddonState { DepartmentId = Department, Packs = 3, PaidThroughUtc = paidThrough, LastActiveOn = paidThrough.AddDays(-2), UpdatedOn = DateTime.UtcNow });
			await states.SaveForDepartmentAsync(new LmsAddonState { DepartmentId = Department, Packs = 4, PaidThroughUtc = paidThrough, RenewalCancelled = true, LapsedSinceUtc = null, UpdatedOn = DateTime.UtcNow });

			(await ScalarAsync($"SELECT COUNT(*) FROM {T("LmsAddonStates")} WHERE {T("DepartmentId")} = @D", new { D = Department })).Should().Be(1);
			var state = await states.GetForDepartmentAsync(Department);
			state.Packs.Should().Be(4);
			state.RenewalCancelled.Should().BeTrue();
			state.PaidThroughUtc.Should().Be(paidThrough);
			state.LastActiveOn.Should().BeNull("the save writes the whole row");
		}

		[Test, Order(4)]
		public async Task At_most_one_lapse_is_open_per_department_and_closing_records_the_end()
		{
			var lapses = Repository((c, s, u, q) => new LmsAddonLapsesRepository(c, s, u, q));
			var started = new DateTime(2027, 2, 7, 12, 0, 0, DateTimeKind.Utc);

			(await lapses.OpenAsync(new LmsAddonLapse { DepartmentId = Department, StartedOn = started, PaidThroughUtc = started.AddDays(-7), Reason = (int)LmsLapseReasons.PaymentFailed, Packs = 3, DetectedOn = DateTime.UtcNow })).Should().BeTrue();
			(await lapses.OpenAsync(new LmsAddonLapse { DepartmentId = Department, StartedOn = started.AddDays(1), DetectedOn = DateTime.UtcNow })).Should().BeFalse("one open lapse per department");
			(await lapses.OpenAsync(new LmsAddonLapse { DepartmentId = OtherDepartment, StartedOn = started, DetectedOn = DateTime.UtcNow })).Should().BeTrue();

			(await lapses.GetOpenAsync(Department)).StartedOn.Should().Be(started);
			var closed = await lapses.CloseAsync(Department, started.AddDays(31));
			closed.EndedOn.Should().Be(started.AddDays(31));
			(await lapses.GetOpenAsync(Department)).Should().BeNull();
			(await lapses.CloseAsync(Department, started.AddDays(40))).Should().BeNull("nothing open");

			(await lapses.OpenAsync(new LmsAddonLapse { DepartmentId = Department, StartedOn = started.AddDays(60), DetectedOn = DateTime.UtcNow })).Should().BeTrue("a new lapse after reactivation");
			var all = await lapses.GetForDepartmentAsync(Department);
			all.Should().HaveCount(2);
			all[0].Reason.Should().Be((int)LmsLapseReasons.PaymentFailed);
			all[0].Packs.Should().Be(3);
			(await lapses.CloseAsync(Department, started.AddDays(10))).EndedOn.Should().Be(started.AddDays(60), "a lapse never ends before it began");
		}

		[Test, Order(5)]
		public async Task Department_cleanup_removes_the_lms_tables_for_that_department_only()
		{
			await using (var database = Connect(_connection))
			{
				await database.OpenAsync();
				await using var transaction = await database.BeginTransactionAsync();
				await UnlinkedDepartmentCleanup.DeleteWithinTransactionAsync(database, transaction, Department, type);
				await transaction.CommitAsync();
			}

			foreach (var table in new[] { "LmsMemberSeats", "LmsAddonStates", "LmsAddonLapses" })
				(await ScalarAsync($"SELECT COUNT(*) FROM {T(table)} WHERE {T("DepartmentId")} = @D", new { D = Department })).Should().Be(0, table);
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("LmsMemberSeats")} WHERE {T("DepartmentId")} = @D", new { D = OtherDepartment })).Should().Be(1);
			(await ScalarAsync($"SELECT COUNT(*) FROM {T("LmsAddonLapses")} WHERE {T("DepartmentId")} = @D", new { D = OtherDepartment })).Should().Be(1);
		}
	}
}
