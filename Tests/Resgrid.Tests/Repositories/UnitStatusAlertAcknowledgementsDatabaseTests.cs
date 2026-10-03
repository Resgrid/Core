using System;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using FluentMigrator;
using FluentMigrator.Runner;
using FluentMigrator.Runner.Initialization;
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
using Resgrid.Repositories.DataRepository.Queries.Common;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;

namespace Resgrid.Tests.Repositories
{
	/// <summary>
	/// Real-database proof for M0258 on both engines: an acknowledgement round-trips through the repository, the IN / ANY
	/// list query finds it, only one live acknowledgement per status episode is allowed, the unit must belong to the
	/// department, and deleting the status record takes its acknowledgement with it. Set
	/// RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class UnitStatusAlertAcknowledgementsDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "unit_alert_ack_";
		private const int DepartmentId = 42;
		private DatabaseTypes _previous;
		private string _master, _connection, _database;
		private ServiceProvider _runner;

		// Whole seconds: SQL Server rounds sub-second values, and the assertions compare stored instants.
		private static DateTime Now
		{
			get
			{
				var now = DateTime.UtcNow;
				return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
			}
		}

		private bool IsPostgres => type == DatabaseTypes.Postgres;

		private DbConnection Connect(string connection) => IsPostgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);

		private SqlConfiguration Configuration() => IsPostgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration();

		private UnitStatusAlertAcknowledgementsRepository Repository()
		{
			var connections = new Mock<IConnectionProvider>();
			connections.Setup(c => c.Create()).Returns(() => Connect(_connection));

			var configuration = Configuration();
			var queries = new ConcurrentDictionary<Type, IQuery>();
			queries[typeof(InsertQuery)] = new InsertQuery(configuration);
			queries[typeof(UpdateQuery)] = new UpdateQuery(configuration);
			var list = new Mock<IQueryList>();
			list.Setup(l => l.RetrieveQueryList()).Returns(queries);

			return new UnitStatusAlertAcknowledgementsRepository(connections.Object, configuration, Mock.Of<IUnitOfWork>(), new QueryFactory(list.Object));
		}

		[OneTimeSetUp]
		public async Task Create_isolated_database()
		{
			_master = Environment.GetEnvironmentVariable(IsPostgres ? "RESGRID_ADP_POSTGRES_TEST_CONNECTION" : "RESGRID_ADP_SQLSERVER_TEST_CONNECTION");
			if (string.IsNullOrWhiteSpace(_master)) Assert.Ignore("Set a test connection to run real database checks.");

			// The API turns this on for PostgreSQL (Web.Services Startup); repositories write UTC DateTimes.
			if (IsPostgres) AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

			_previous = DataConfig.DatabaseType;
			DataConfig.DatabaseType = type;
			_database = Prefix + Guid.NewGuid().ToString("N");
			await using (var master = Connect(_master))
				await master.ExecuteAsync("CREATE DATABASE " + _database);
			_connection = IsPostgres
				? new NpgsqlConnectionStringBuilder(_master) { Database = _database }.ConnectionString
				: new SqlConnectionStringBuilder(_master) { InitialCatalog = _database }.ConnectionString;

			// Minimal stand-ins for M0001's Units / UnitStates, with the (DepartmentId, UnitId) key M0101 adds.
			await using (var database = Connect(_connection))
			{
				await database.OpenAsync();
				await database.ExecuteAsync(IsPostgres
					? @"CREATE EXTENSION IF NOT EXISTS citext;
						CREATE TABLE units (unitid serial PRIMARY KEY, departmentid int NOT NULL, CONSTRAINT uq_units_departmentid_unitid UNIQUE (departmentid, unitid));
						CREATE TABLE unitstates (unitstateid serial PRIMARY KEY, unitid int NOT NULL);"
					: @"CREATE TABLE Units (UnitId int IDENTITY PRIMARY KEY, DepartmentId int NOT NULL, CONSTRAINT UQ_Units_DepartmentId_UnitId UNIQUE (DepartmentId, UnitId));
						CREATE TABLE UnitStates (UnitStateId int IDENTITY PRIMARY KEY, UnitId int NOT NULL);");

				// Npgsql loaded this database's types on the first open, before citext existed; a real database has
				// the extension before the application ever connects.
				if (database is NpgsqlConnection postgres)
					postgres.ReloadTypes();
			}

			var source = new Mock<IMigrationSource>();
			source.Setup(s => s.GetMigrations()).Returns(IsPostgres
				? new IMigration[] { new M0258_AddUnitStatusAlertAcknowledgementsPg() }
				: new IMigration[] { new M0258_AddUnitStatusAlertAcknowledgements() });
			_runner = new ServiceCollection().AddFluentMigratorCore().ConfigureRunner(r =>
			{
				if (IsPostgres) r.AddPostgres(); else r.AddSqlServer();
				r.WithGlobalConnectionString(_connection);
			}).AddSingleton(source.Object).BuildServiceProvider();
			_runner.GetRequiredService<IMigrationRunner>().MigrateUp();
		}

		[OneTimeTearDown]
		public async Task Remove_only_this_fixture_database()
		{
			if (_database == null) return;
			_runner?.Dispose();
			DataConfig.DatabaseType = _previous;
			if (!_database.StartsWith(Prefix, StringComparison.Ordinal) || !Guid.TryParseExact(_database.Substring(Prefix.Length), "N", out _))
				throw new InvalidOperationException("Unexpected test database name.");
			if (IsPostgres) NpgsqlConnection.ClearAllPools(); else SqlConnection.ClearAllPools();
			await using var master = Connect(_master);
			await master.ExecuteAsync(IsPostgres ? "DROP DATABASE " + _database + " WITH (FORCE)"
				: "ALTER DATABASE " + _database + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + _database);
		}

		/// <summary>Adds a unit in the department and one status record for it; returns (unitId, unitStateId).</summary>
		private async Task<(int UnitId, int UnitStateId)> SeedUnitAndStateAsync(int departmentId = DepartmentId)
		{
			await using var database = Connect(_connection);
			var unitId = await database.ExecuteScalarAsync<int>(IsPostgres
				? "INSERT INTO units (departmentid) VALUES (@DepartmentId) RETURNING unitid"
				: "INSERT INTO Units (DepartmentId) OUTPUT INSERTED.UnitId VALUES (@DepartmentId)", new { DepartmentId = departmentId });
			var unitStateId = await database.ExecuteScalarAsync<int>(IsPostgres
				? "INSERT INTO unitstates (unitid) VALUES (@UnitId) RETURNING unitstateid"
				: "INSERT INTO UnitStates (UnitId) OUTPUT INSERTED.UnitStateId VALUES (@UnitId)", new { UnitId = unitId });
			return (unitId, unitStateId);
		}

		private static UnitStatusAlertAcknowledgement NewAcknowledgement(int unitId, int unitStateId, int departmentId = DepartmentId) => new()
		{
			UnitStatusAlertAcknowledgementId = Guid.NewGuid().ToString(),
			DepartmentId = departmentId,
			UnitId = unitId,
			UnitStateId = unitStateId,
			Level = (int)UnitStatusAlertLevels.Warn,
			Mode = (int)UnitStatusAlertAcknowledgementModes.Muted,
			MutedUntil = Now.AddMinutes(15),
			Note = "MUG not departed, technical malfunction reported.",
			AcknowledgedByUserId = "dispatcher-1",
			AcknowledgedOn = Now
		};

		[Test]
		public async Task An_acknowledgement_round_trips_and_is_found_by_its_status_record()
		{
			var (unitId, unitStateId) = await SeedUnitAndStateAsync();
			var (_, otherStateId) = await SeedUnitAndStateAsync();
			var acknowledgement = NewAcknowledgement(unitId, unitStateId);

			await Repository().InsertAsync(acknowledgement, CancellationToken.None);

			var byList = (await Repository().GetActiveForUnitStatesAsync(DepartmentId, new[] { unitStateId, otherStateId })).ToList();
			byList.Should().ContainSingle();
			var stored = byList.Single();
			stored.UnitStatusAlertAcknowledgementId.Should().Be(acknowledgement.UnitStatusAlertAcknowledgementId);
			stored.UnitId.Should().Be(unitId);
			stored.Level.Should().Be((int)UnitStatusAlertLevels.Warn);
			stored.Mode.Should().Be((int)UnitStatusAlertAcknowledgementModes.Muted);
			stored.MutedUntil.Should().Be(acknowledgement.MutedUntil);
			stored.AcknowledgedOn.Should().Be(acknowledgement.AcknowledgedOn);
			stored.Note.Should().Be(acknowledgement.Note);
			stored.AcknowledgedByUserId.Should().Be("dispatcher-1");
			stored.ClearedOn.Should().BeNull();

			(await Repository().GetActiveForUnitStateAsync(DepartmentId, unitId, unitStateId)).Should().ContainSingle();
			(await Repository().GetActiveForUnitStatesAsync(DepartmentId + 1, new[] { unitStateId })).Should().BeEmpty("another department never reads it");
			(await Repository().GetActiveForUnitStatesAsync(DepartmentId, Array.Empty<int>())).Should().BeEmpty();
		}

		[Test]
		public async Task A_cleared_acknowledgement_is_no_longer_active()
		{
			var (unitId, unitStateId) = await SeedUnitAndStateAsync();
			var acknowledgement = NewAcknowledgement(unitId, unitStateId);
			await Repository().InsertAsync(acknowledgement, CancellationToken.None);

			acknowledgement.ClearedOn = Now;
			acknowledgement.ClearedByUserId = "dispatcher-2";
			await Repository().UpdateAsync(acknowledgement, CancellationToken.None);

			(await Repository().GetActiveForUnitStateAsync(DepartmentId, unitId, unitStateId)).Should().BeEmpty();
			(await Repository().GetActiveForUnitStatesAsync(DepartmentId, new[] { unitStateId })).Should().BeEmpty();
		}

		[Test]
		public async Task Only_one_live_acknowledgement_per_status_episode()
		{
			var (unitId, unitStateId) = await SeedUnitAndStateAsync();
			var first = NewAcknowledgement(unitId, unitStateId);
			await Repository().InsertAsync(first, CancellationToken.None);

			Func<Task> second = () => Repository().InsertAsync(NewAcknowledgement(unitId, unitStateId), CancellationToken.None);
			await second.Should().ThrowAsync<DbException>("the filtered unique index admits one uncleared row per UnitStateId");

			// Once the first is cleared, a replacement is accepted: the history row stays.
			first.ClearedOn = Now;
			first.ClearedByUserId = "dispatcher-1";
			await Repository().UpdateAsync(first, CancellationToken.None);
			await Repository().InsertAsync(NewAcknowledgement(unitId, unitStateId), CancellationToken.None);

			(await Repository().GetActiveForUnitStateAsync(DepartmentId, unitId, unitStateId)).Should().ContainSingle();
		}

		[Test]
		public async Task The_unit_must_belong_to_the_department()
		{
			var (unitId, unitStateId) = await SeedUnitAndStateAsync();

			Func<Task> act = () => Repository().InsertAsync(NewAcknowledgement(unitId, unitStateId, DepartmentId + 1), CancellationToken.None);

			await act.Should().ThrowAsync<DbException>("the (DepartmentId, UnitId) foreign key rejects a unit from another department");
		}

		[Test]
		public async Task Deleting_the_status_record_removes_its_acknowledgements()
		{
			var (unitId, unitStateId) = await SeedUnitAndStateAsync();
			await Repository().InsertAsync(NewAcknowledgement(unitId, unitStateId), CancellationToken.None);

			await using (var database = Connect(_connection))
				await database.ExecuteAsync(IsPostgres ? "DELETE FROM unitstates WHERE unitstateid = @Id" : "DELETE FROM UnitStates WHERE UnitStateId = @Id", new { Id = unitStateId });

			await using (var database = Connect(_connection))
				(await database.ExecuteScalarAsync<int>(IsPostgres
					? "SELECT COUNT(*) FROM unitstatusalertacknowledgements WHERE unitstateid = @Id"
					: "SELECT COUNT(*) FROM UnitStatusAlertAcknowledgements WHERE UnitStateId = @Id", new { Id = unitStateId })).Should().Be(0);
		}
	}
}
