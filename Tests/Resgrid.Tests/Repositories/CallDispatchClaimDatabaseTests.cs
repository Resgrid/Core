using System;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Linq;
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
using Resgrid.Repositories.DataRepository.Queries.Calls;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;

namespace Resgrid.Tests.Repositories
{
	/// <summary>
	/// Real-database proof for the dispatch claim (M0266) on both engines: one claim per waiting call, a claim is given back
	/// or ended only by its own claim time (which has to read back equal through Dapper's DateTime parameters), and a claim
	/// abandoned past its lease can be taken again, also by the scheduled-calls worker's query. Set
	/// RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class CallDispatchClaimDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "call_dispatch_claim_";
		private DatabaseTypes _previous;
		private string _master, _connection, _database;
		private ServiceProvider _runner;
		private int _nextCallId;

		private bool IsPostgres => type == DatabaseTypes.Postgres;

		private DbConnection Connect(string connection) => IsPostgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);

		private CallsRepository Repository()
		{
			var connections = new Mock<IConnectionProvider>();
			connections.Setup(c => c.Create()).Returns(() => Connect(_connection));

			SqlConfiguration configuration = IsPostgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration();
			var queries = new ConcurrentDictionary<Type, IQuery>();
			queries[typeof(SelectNonDispatchedScheduledCallsByDateQuery)] = new SelectNonDispatchedScheduledCallsByDateQuery(configuration);
			var list = new Mock<IQueryList>();
			list.Setup(l => l.RetrieveQueryList()).Returns(queries);

			return new CallsRepository(connections.Object, configuration, Mock.Of<IUnitOfWork>(), new QueryFactory(list.Object));
		}

		[OneTimeSetUp]
		public async Task Create_isolated_database()
		{
			_master = Environment.GetEnvironmentVariable(IsPostgres ? "RESGRID_ADP_POSTGRES_TEST_CONNECTION" : "RESGRID_ADP_SQLSERVER_TEST_CONNECTION");
			if (string.IsNullOrWhiteSpace(_master)) Assert.Ignore("Set a test connection to run real database checks.");

			if (IsPostgres) AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

			_previous = DataConfig.DatabaseType;
			DataConfig.DatabaseType = type;
			_database = Prefix + Guid.NewGuid().ToString("N");
			await using (var master = Connect(_master))
				await master.ExecuteAsync("CREATE DATABASE " + _database);
			_connection = IsPostgres
				? new NpgsqlConnectionStringBuilder(_master) { Database = _database }.ConnectionString
				: new SqlConnectionStringBuilder(_master) { InitialCatalog = _database }.ConnectionString;

			// A minimal stand-in for the M0001 Calls table, without the column M0266 adds.
			await using (var database = Connect(_connection))
			{
				await database.OpenAsync();
				await database.ExecuteAsync(IsPostgres
					? @"CREATE TABLE calls (callid int PRIMARY KEY, departmentid int NOT NULL, state int NOT NULL, dispatchon timestamp NULL,
						hasbeendispatched boolean NULL, isdeleted boolean NOT NULL DEFAULT false);"
					: @"CREATE TABLE Calls (CallId int PRIMARY KEY, DepartmentId int NOT NULL, State int NOT NULL, DispatchOn datetime2 NULL,
						HasBeenDispatched bit NULL, IsDeleted bit NOT NULL DEFAULT 0);");
			}

			var source = new Mock<IMigrationSource>();
			source.Setup(s => s.GetMigrations()).Returns(IsPostgres
				? new IMigration[] { new M0266_AddCallDispatchClaimPg() }
				: new IMigration[] { new M0266_AddCallDispatchClaim() });
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

		private async Task<int> SeedCallAsync(int state, DateTime? dispatchOn, bool? hasBeenDispatched, DateTime? claimedOn = null, bool deleted = false, int departmentId = 7)
		{
			var callId = ++_nextCallId;
			await using var database = Connect(_connection);
			await database.ExecuteAsync(IsPostgres
				? "INSERT INTO calls (callid, departmentid, state, dispatchon, hasbeendispatched, isdeleted, dispatchclaimedon) VALUES (@CallId, @DepartmentId, @State, @DispatchOn, @HasBeenDispatched, @Deleted, @ClaimedOn)"
				: "INSERT INTO Calls (CallId, DepartmentId, State, DispatchOn, HasBeenDispatched, IsDeleted, DispatchClaimedOn) VALUES (@CallId, @DepartmentId, @State, @DispatchOn, @HasBeenDispatched, @Deleted, @ClaimedOn)",
				new { CallId = callId, DepartmentId = departmentId, State = state, DispatchOn = dispatchOn, HasBeenDispatched = hasBeenDispatched, Deleted = deleted, ClaimedOn = claimedOn });
			return callId;
		}

		private async Task<(int State, bool? HasBeenDispatched, DateTime? ClaimedOn)> ReadAsync(int callId)
		{
			await using var database = Connect(_connection);
			var row = await database.QuerySingleAsync(IsPostgres
				? "SELECT state, hasbeendispatched, dispatchclaimedon FROM calls WHERE callid = @CallId"
				: "SELECT State AS state, HasBeenDispatched AS hasbeendispatched, DispatchClaimedOn AS dispatchclaimedon FROM Calls WHERE CallId = @CallId", new { CallId = callId });
			return ((int)row.state, (bool?)row.hasbeendispatched, (DateTime?)row.dispatchclaimedon);
		}

		private static DateTime Now => DateTime.UtcNow;
		private static DateTime StaleBefore => DateTime.UtcNow - CallDispatchClaims.Lease;

		[Test]
		public async Task Only_waiting_calls_are_claimed_and_only_once()
		{
			var repository = Repository();
			var pending = await SeedCallAsync((int)CallStates.Pending, null, null);
			var scheduled = await SeedCallAsync((int)CallStates.Active, Now.AddMinutes(-1), false);
			var immediate = await SeedCallAsync((int)CallStates.Active, null, null);
			var closed = await SeedCallAsync((int)CallStates.Closed, Now.AddMinutes(-1), false);
			var sent = await SeedCallAsync((int)CallStates.Active, Now.AddMinutes(-1), true);
			var deleted = await SeedCallAsync((int)CallStates.Pending, null, null, deleted: true);
			var otherDepartment = await SeedCallAsync((int)CallStates.Pending, null, null, departmentId: 9);

			(await repository.TryClaimCallForDispatchAsync(pending, 7, Now, StaleBefore)).Should().BeTrue();
			(await repository.TryClaimCallForDispatchAsync(pending, 7, Now, StaleBefore)).Should().BeFalse("the first claim is still in flight");
			(await repository.TryClaimCallForDispatchAsync(scheduled, 7, Now, StaleBefore)).Should().BeTrue();
			(await repository.TryClaimCallForDispatchAsync(immediate, 7, Now, StaleBefore)).Should().BeFalse("an ordinary active call was dispatched when it was entered");
			(await repository.TryClaimCallForDispatchAsync(closed, 7, Now, StaleBefore)).Should().BeFalse();
			(await repository.TryClaimCallForDispatchAsync(sent, 7, Now, StaleBefore)).Should().BeFalse();
			(await repository.TryClaimCallForDispatchAsync(deleted, 7, Now, StaleBefore)).Should().BeFalse();
			(await repository.TryClaimCallForDispatchAsync(otherDepartment, 7, Now, StaleBefore)).Should().BeFalse();
		}

		[Test]
		public async Task A_claim_is_released_or_ended_only_by_its_own_claim_time()
		{
			var repository = Repository();
			var callId = await SeedCallAsync((int)CallStates.Pending, null, null);
			var claimedOn = new DateTime(2026, 10, 7, 9, 30, 12, 345, DateTimeKind.Utc).AddTicks(6789);
			(await repository.TryClaimCallForDispatchAsync(callId, 7, claimedOn, StaleBefore)).Should().BeTrue();

			(await repository.ReleaseCallDispatchClaimAsync(callId, 7, claimedOn.AddMilliseconds(-50), (int)CallStates.Pending, null, null))
				.Should().BeFalse("another sender's claim is not this one's to give back");
			(await repository.ReleaseCallDispatchClaimAsync(callId, 7, claimedOn, (int)CallStates.Pending, null, null))
				.Should().BeTrue("the claim time written has to read back equal through the same parameter type");
			(await ReadAsync(callId)).Should().Be(((int)CallStates.Pending, (bool?)null, (DateTime?)null));

			(await repository.TryClaimCallForDispatchAsync(callId, 7, claimedOn, StaleBefore)).Should().BeTrue("a released call is waiting again");
			(await repository.CompleteCallDispatchClaimAsync(callId, 7, claimedOn)).Should().BeTrue();
			(await ReadAsync(callId)).Should().Be(((int)CallStates.Pending, (bool?)true, (DateTime?)null));
			(await repository.TryClaimCallForDispatchAsync(callId, 7, Now, StaleBefore)).Should().BeFalse("a completed claim never expires into a second dispatch");
		}

		[Test]
		public async Task A_closed_call_is_not_put_back_by_a_late_release()
		{
			var repository = Repository();
			var callId = await SeedCallAsync((int)CallStates.Active, Now.AddMinutes(-1), false);
			var claimedOn = Now;
			(await repository.TryClaimCallForDispatchAsync(callId, 7, claimedOn, StaleBefore)).Should().BeTrue();
			await using (var database = Connect(_connection))
				await database.ExecuteAsync(IsPostgres ? "UPDATE calls SET state = 1 WHERE callid = @CallId" : "UPDATE Calls SET State = 1 WHERE CallId = @CallId", new { CallId = callId });

			(await repository.ReleaseCallDispatchClaimAsync(callId, 7, claimedOn, (int)CallStates.Active, Now.AddMinutes(-1), false)).Should().BeFalse();
			(await ReadAsync(callId)).State.Should().Be((int)CallStates.Closed);
		}

		[Test]
		public async Task An_abandoned_claim_can_be_taken_again_and_the_worker_finds_it()
		{
			var repository = Repository();
			var abandonedOn = Now - CallDispatchClaims.Lease - TimeSpan.FromMinutes(1);
			var abandoned = await SeedCallAsync((int)CallStates.Active, Now.AddMinutes(-7), true, claimedOn: abandonedOn);
			var inFlight = await SeedCallAsync((int)CallStates.Active, Now.AddMinutes(-2), true, claimedOn: Now.AddMinutes(-1));
			var sent = await SeedCallAsync((int)CallStates.Active, Now.AddMinutes(-3), true);

			var due = (await repository.GetAllNonDispatchedScheduledCallsWithinDateRange(Now.AddMinutes(-15), Now)).Select(c => c.CallId).ToList();
			due.Should().Contain(abandoned).And.NotContain(inFlight).And.NotContain(sent);

			(await repository.TryClaimCallForDispatchAsync(inFlight, 7, Now, StaleBefore)).Should().BeFalse("its lease has not run out");
			var claimedOn = Now;
			(await repository.TryClaimCallForDispatchAsync(abandoned, 7, claimedOn, StaleBefore)).Should().BeTrue();
			(await repository.ReleaseCallDispatchClaimAsync(abandoned, 7, abandonedOn, (int)CallStates.Active, null, false))
				.Should().BeFalse("the dead process's claim time no longer matches");
		}
	}
}
