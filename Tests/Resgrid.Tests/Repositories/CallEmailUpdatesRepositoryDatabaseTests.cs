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
	/// Real-database proof for M0330 (CAD email call updates) on both engines: the ledger reads back by call, open/pending and
	/// recently closed calls are found by external identifier, incident number (trimmed, case ignored) or a ledger row, and the
	/// per-ID lock excludes a second holder until released. Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION /
	/// RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class CallEmailUpdatesRepositoryDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "cad_email_updates_";
		private const int Dept = 55;
		private DatabaseTypes _previous;
		private bool _configured;
		private string _master, _connection, _database;
		private ServiceProvider _runner;

		private bool IsPostgres => type == DatabaseTypes.Postgres;

		private DbConnection Connect(string connection) => IsPostgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);

		private CallEmailUpdatesRepository Repository()
		{
			var connections = new Mock<IConnectionProvider>();
			connections.Setup(c => c.Create()).Returns(() => Connect(_connection));
			SqlConfiguration configuration = IsPostgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration();
			var queries = new ConcurrentDictionary<Type, IQuery>();
			queries[typeof(InsertQuery)] = new InsertQuery(configuration);
			queries[typeof(UpdateQuery)] = new UpdateQuery(configuration);
			queries[typeof(SelectByIdQuery)] = new SelectByIdQuery(configuration);
			queries[typeof(SelectByDepartmentIdQuery)] = new SelectByDepartmentIdQuery(configuration);
			var list = new Mock<IQueryList>();
			list.Setup(l => l.RetrieveQueryList()).Returns(queries);
			return new CallEmailUpdatesRepository(connections.Object, configuration, Mock.Of<IUnitOfWork>(), new QueryFactory(list.Object));
		}

		[OneTimeSetUp]
		public async Task Create_isolated_database()
		{
			_master = Environment.GetEnvironmentVariable(IsPostgres ? "RESGRID_ADP_POSTGRES_TEST_CONNECTION" : "RESGRID_ADP_SQLSERVER_TEST_CONNECTION");
			if (string.IsNullOrWhiteSpace(_master)) Assert.Ignore("Set a test connection to run real database checks.");

			// As the API host runs (Startup): the seed's UTC DateTimes go into timestamp columns as written, not through the
			// test server's session time zone.
			if (IsPostgres) AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

			_previous = DataConfig.DatabaseType;
			DataConfig.DatabaseType = type;
			_configured = true;
			var database = Prefix + Guid.NewGuid().ToString("N");
			await using (var master = Connect(_master))
				await master.ExecuteAsync("CREATE DATABASE " + database);
			_database = database;
			_connection = IsPostgres
				? new NpgsqlConnectionStringBuilder(_master) { Database = _database }.ConnectionString
				: new SqlConnectionStringBuilder(_master) { InitialCatalog = _database }.ConnectionString;

			await using (var setup = Connect(_connection))
			{
				await setup.OpenAsync();
				// The Calls columns the lookups read (types as M0001 creates them) and the intake table M0330 alters.
				if (IsPostgres)
				{
					await setup.ExecuteAsync("CREATE EXTENSION IF NOT EXISTS citext;");
					await ((NpgsqlConnection)setup).ReloadTypesAsync();
					await setup.ExecuteAsync("CREATE TABLE calls (callid int PRIMARY KEY, departmentid int NOT NULL, name citext, incidentnumber citext, externalidentifier citext, " +
						"state int NOT NULL, closedon timestamp without time zone NULL, loggedon timestamp without time zone NOT NULL, isdeleted boolean NOT NULL);" +
						"CREATE TABLE intakechannels (intakechannelid citext PRIMARY KEY, departmentid int NOT NULL);");
				}
				else
				{
					await setup.ExecuteAsync("CREATE TABLE [Calls] ([CallId] int PRIMARY KEY, [DepartmentId] int NOT NULL, [Name] nvarchar(max) NULL, [IncidentNumber] nvarchar(max) NULL, " +
						"[ExternalIdentifier] nvarchar(max) NULL, [State] int NOT NULL, [ClosedOn] datetime2 NULL, [LoggedOn] datetime2 NOT NULL, [IsDeleted] bit NOT NULL);" +
						"CREATE TABLE [IntakeChannels] ([IntakeChannelId] nvarchar(128) PRIMARY KEY, [DepartmentId] int NOT NULL);");
				}
			}

			var source = new Mock<IMigrationSource>();
			source.Setup(s => s.GetMigrations()).Returns(IsPostgres ? new IMigration[] { new M0330_AddCallEmailUpdatesPg() } : new IMigration[] { new M0330_AddCallEmailUpdates() });
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
			_runner?.Dispose();
			if (_configured) DataConfig.DatabaseType = _previous;
			if (_database == null) return;
			if (!_database.StartsWith(Prefix, StringComparison.Ordinal) || !Guid.TryParseExact(_database.Substring(Prefix.Length), "N", out _))
				throw new InvalidOperationException("Unexpected test database name.");
			if (IsPostgres) NpgsqlConnection.ClearAllPools(); else SqlConnection.ClearAllPools();
			await using var master = Connect(_master);
			await master.ExecuteAsync(IsPostgres ? "DROP DATABASE " + _database + " WITH (FORCE)"
				: "ALTER DATABASE " + _database + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + _database);
		}

		[SetUp]
		public async Task Empty_the_tables()
		{
			await using var database = Connect(_connection);
			foreach (var table in new[] { "CallEmailUpdates", "Calls", "IntakeChannels" })
				await database.ExecuteAsync("DELETE FROM " + (IsPostgres ? table.ToLowerInvariant() : "[" + table + "]"));
		}

		private async Task AddCallAsync(int callId, int state, string incidentNumber = null, string externalIdentifier = null, int departmentId = Dept,
			DateTime? closedOn = null, bool deleted = false)
		{
			await using var database = Connect(_connection);
			var sql = IsPostgres
				? "INSERT INTO calls (callid, departmentid, name, incidentnumber, externalidentifier, state, closedon, loggedon, isdeleted) VALUES (@CallId, @DepartmentId, 'Fire', @IncidentNumber, @ExternalIdentifier, @State, @ClosedOn, @LoggedOn, @IsDeleted)"
				: "INSERT INTO [Calls] ([CallId], [DepartmentId], [Name], [IncidentNumber], [ExternalIdentifier], [State], [ClosedOn], [LoggedOn], [IsDeleted]) VALUES (@CallId, @DepartmentId, 'Fire', @IncidentNumber, @ExternalIdentifier, @State, @ClosedOn, @LoggedOn, @IsDeleted)";
			await database.ExecuteAsync(sql, new
			{
				CallId = callId, DepartmentId = departmentId, IncidentNumber = incidentNumber, ExternalIdentifier = externalIdentifier, State = state,
				ClosedOn = closedOn, LoggedOn = DateTime.UtcNow.AddHours(-1), IsDeleted = deleted
			});
		}

		private static CallEmailUpdate Row(int? callId, string cadCallId, DateTime receivedOn, CallEmailUpdateOutcomes outcome = CallEmailUpdateOutcomes.Created) => new CallEmailUpdate
		{
			CallEmailUpdateId = Guid.NewGuid().ToString(), DepartmentId = Dept, CallId = callId, CadCallId = cadCallId, Source = (int)CallEmailUpdateSources.DispatchAddress,
			MessageId = "m-" + Guid.NewGuid().ToString("N"), SentOn = receivedOn.AddSeconds(-5), ReceivedOn = receivedOn, Outcome = (int)outcome,
			ChangedFields = "Location", FieldHashes = "{\"Location\":\"abc\"}"
		};

		[Test]
		public async Task The_ledger_reads_back_a_calls_rows_oldest_first()
		{
			var repository = Repository();
			var now = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
			await repository.InsertAsync(Row(42, "26-1", now.AddMinutes(5), CallEmailUpdateOutcomes.Updated), CancellationToken.None);
			await repository.InsertAsync(Row(42, "26-1", now), CancellationToken.None);
			await repository.InsertAsync(Row(43, "26-2", now), CancellationToken.None);
			await repository.InsertAsync(Row(null, "26-9", now, CallEmailUpdateOutcomes.CloseUnmatched), CancellationToken.None);

			var rows = (await repository.GetByCallAsync(Dept, 42)).ToList();

			rows.Select(r => r.Outcome).Should().Equal((int)CallEmailUpdateOutcomes.Created, (int)CallEmailUpdateOutcomes.Updated);
			rows[0].FieldHashes.Should().Be("{\"Location\":\"abc\"}");
			rows[0].SentOn.Should().NotBeNull();
		}

		[Test]
		public async Task A_row_is_found_by_its_message_and_moved_on_only_from_the_outcome_it_was_written_with()
		{
			var repository = Repository();
			var row = Row(42, "26-1", DateTime.UtcNow, CallEmailUpdateOutcomes.Applying);
			row.MessageId = "PM-Abc-1";
			await repository.InsertAsync(row, CancellationToken.None);
			await repository.InsertAsync(Row(42, "26-1", DateTime.UtcNow), CancellationToken.None);

			(await repository.GetByMessageAsync(Dept, " 26-1 ", "pm-abc-1")).Select(r => r.CallEmailUpdateId).Should().Equal(new[] { row.CallEmailUpdateId });
			(await repository.GetByMessageAsync(Dept, "26-2", "PM-Abc-1")).Should().BeEmpty("another CAD call ID");
			(await repository.GetByMessageAsync(Dept + 1, "26-1", "PM-Abc-1")).Should().BeEmpty("another department");

			(await repository.SetOutcomeAsync(row.CallEmailUpdateId, (int)CallEmailUpdateOutcomes.Applying, (int)CallEmailUpdateOutcomes.Updated)).Should().BeTrue();
			(await repository.SetOutcomeAsync(row.CallEmailUpdateId, (int)CallEmailUpdateOutcomes.Applying, (int)CallEmailUpdateOutcomes.Closed)).Should().BeFalse("it already moved on");
			(await repository.GetByCallAsync(Dept, 42)).Single(r => r.CallEmailUpdateId == row.CallEmailUpdateId).Outcome.Should().Be((int)CallEmailUpdateOutcomes.Updated);
		}

		[Test]
		public async Task Open_and_pending_calls_are_found_by_either_identifier_or_a_ledger_row()
		{
			var repository = Repository();
			await AddCallAsync(1, (int)CallStates.Active, externalIdentifier: " 26-1 ");
			await AddCallAsync(2, (int)CallStates.Pending, incidentNumber: "26-1");
			await AddCallAsync(3, (int)CallStates.Active, incidentNumber: "AB-7");
			await AddCallAsync(4, (int)CallStates.Active);
			await AddCallAsync(5, (int)CallStates.Closed, incidentNumber: "26-1", closedOn: DateTime.UtcNow);
			await AddCallAsync(6, (int)CallStates.Active, incidentNumber: "26-1", deleted: true);
			await AddCallAsync(7, (int)CallStates.Active, incidentNumber: "26-1", departmentId: Dept + 1);
			await repository.InsertAsync(Row(4, "Parkland|20180514", DateTime.UtcNow), CancellationToken.None);

			(await repository.GetOpenCallsForCadCallIdAsync(Dept, "26-1")).Select(c => c.CallId).Should().BeEquivalentTo(new[] { 1, 2 });
			(await repository.GetOpenCallsForCadCallIdAsync(Dept, "ab-7")).Select(c => c.CallId).Should().Equal(3);
			(await repository.GetOpenCallsForCadCallIdAsync(Dept, "Parkland|20180514")).Select(c => c.CallId).Should().Equal(4);
			(await repository.GetOpenCallsForCadCallIdAsync(Dept, "26-404")).Should().BeEmpty();
		}

		[Test]
		public async Task Closed_calls_are_found_only_since_the_given_time()
		{
			var repository = Repository();
			await AddCallAsync(1, (int)CallStates.Closed, incidentNumber: "26-1", closedOn: DateTime.UtcNow.AddMinutes(-10));
			await AddCallAsync(2, (int)CallStates.Cancelled, incidentNumber: "26-1", closedOn: DateTime.UtcNow.AddHours(-5));
			await AddCallAsync(3, (int)CallStates.Active, incidentNumber: "26-1");

			(await repository.GetClosedCallsForCadCallIdAsync(Dept, "26-1", DateTime.UtcNow.AddHours(-1))).Select(c => c.CallId).Should().Equal(1);
			(await repository.GetClosedCallsForCadCallIdAsync(Dept, "26-1", DateTime.UtcNow.AddDays(-1))).Select(c => c.CallId).Should().BeEquivalentTo(new[] { 1, 2 });
		}

		[Test]
		public async Task The_lock_keeps_a_second_holder_out_until_it_is_released()
		{
			var repository = Repository();

			var first = await repository.AcquireCadCallLockAsync(Dept, "26-1", TimeSpan.FromSeconds(2));
			first.Should().NotBeNull();

			(await repository.AcquireCadCallLockAsync(Dept, "26-1", TimeSpan.FromMilliseconds(300))).Should().BeNull("another email holds the same CAD call ID");

			var other = await repository.AcquireCadCallLockAsync(Dept, "26-2", TimeSpan.FromSeconds(2));
			other.Should().NotBeNull("a different ID is not blocked");
			await other.DisposeAsync();

			await first.DisposeAsync();
			var again = await repository.AcquireCadCallLockAsync(Dept, "26-1", TimeSpan.FromSeconds(2));
			again.Should().NotBeNull("released when the first holder disposed it");
			await again.DisposeAsync();
		}

		[Test]
		public async Task Intake_channels_get_the_cad_updates_flag_off_by_default()
		{
			await using var database = Connect(_connection);
			await database.ExecuteAsync(IsPostgres
				? "INSERT INTO intakechannels (intakechannelid, departmentid) VALUES ('c1', 55)"
				: "INSERT INTO [IntakeChannels] ([IntakeChannelId], [DepartmentId]) VALUES ('c1', 55)");

			(await database.ExecuteScalarAsync<bool>(IsPostgres
				? "SELECT cadupdates FROM intakechannels WHERE intakechannelid = 'c1'"
				: "SELECT [CadUpdates] FROM [IntakeChannels] WHERE [IntakeChannelId] = 'c1'")).Should().BeFalse();
		}
	}
}
