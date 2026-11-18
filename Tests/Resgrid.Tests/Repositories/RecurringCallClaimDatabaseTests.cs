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
using Resgrid.Repositories.DataRepository.Servers.SqlServer;

namespace Resgrid.Tests.Repositories
{
	/// <summary>
	/// Real-database proof for the recurring-call claims (PR #587 round 2) on both engines: a worker claims an occurrence only while
	/// its schedule runs as the revision it read (the schedule row locked for the check: UPDLOCK on SQL Server, FOR SHARE inside the
	/// EXISTS on PostgreSQL); a claim is completed, renewed, returned or dropped only under its own stamp (which has to read back equal
	/// through Dapper's DateTime parameters); and an alert is sent under a notification claim one sender at a time. Set
	/// RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class RecurringCallClaimDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "recurring_call_claim_";
		private const int DepartmentId = 7;
		private DatabaseTypes _previous;
		private string _master, _connection, _database;
		private ServiceProvider _runner;

		private bool IsPostgres => type == DatabaseTypes.Postgres;

		private DbConnection Connect(string connection) => IsPostgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);

		private (IConnectionProvider Connections, SqlConfiguration Configuration, QueryFactory Queries) Parts()
		{
			var connections = new Mock<IConnectionProvider>();
			connections.Setup(c => c.Create()).Returns(() => Connect(_connection));
			var list = new Mock<IQueryList>();
			list.Setup(l => l.RetrieveQueryList()).Returns(new ConcurrentDictionary<Type, IQuery>());
			return (connections.Object, IsPostgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration(), new QueryFactory(list.Object));
		}

		private RecurringCallOccurrencesRepository Occurrences()
		{
			var (connections, configuration, queries) = Parts();
			return new RecurringCallOccurrencesRepository(connections, configuration, Mock.Of<IUnitOfWork>(), queries);
		}

		private RecurringCallAlertsRepository Alerts()
		{
			var (connections, configuration, queries) = Parts();
			return new RecurringCallAlertsRepository(connections, configuration, Mock.Of<IUnitOfWork>(), queries);
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

			// citext for the PostgreSQL tables, and a minimal stand-in for the M0001 Calls table that M0319 extends.
			await using (var database = Connect(_connection))
			{
				await database.OpenAsync();
				await database.ExecuteAsync(IsPostgres
					? "CREATE EXTENSION IF NOT EXISTS citext; CREATE TABLE calls (callid int PRIMARY KEY, departmentid int NOT NULL, dispatchon timestamp NULL);"
					: "CREATE TABLE Calls (CallId int PRIMARY KEY, DepartmentId int NOT NULL, DispatchOn datetime2 NULL);");

				// The data source loaded its types before citext existed; without a reload a citext column cannot be read back.
				if (database is NpgsqlConnection postgres)
					await postgres.ReloadTypesAsync();
			}

			var source = new Mock<IMigrationSource>();
			source.Setup(s => s.GetMigrations()).Returns(IsPostgres
				? new IMigration[] { new M0319_AddRecurringCallsPg(), new M0320_ExtendRecurringCallsPg() }
				: new IMigration[] { new M0319_AddRecurringCalls(), new M0320_ExtendRecurringCalls() });
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

		private static DateTime Stamp(DateTime utc) => RecurringCallOccurrence.ClaimStamp(utc);

		private async Task<int> SeedScheduleAsync(DateTime createdOn, DateTime? updatedOn = null)
		{
			await using var database = Connect(_connection);
			return await database.ExecuteScalarAsync<int>(IsPostgres
				? "INSERT INTO recurringcalls (departmentid, name, frequency, times, startson, leadtimeminutes, createdon, updatedon) " +
				  "VALUES (@DepartmentId, 'Dialysis run', 1, '08:00', @CreatedOn, 30, @CreatedOn, @UpdatedOn) RETURNING recurringcallid"
				: "INSERT INTO RecurringCalls (DepartmentId, Name, Frequency, Times, StartsOn, LeadTimeMinutes, CreatedOn, UpdatedOn) OUTPUT INSERTED.RecurringCallId " +
				  "VALUES (@DepartmentId, 'Dialysis run', 1, '08:00', @CreatedOn, 30, @CreatedOn, @UpdatedOn)",
				new { DepartmentId, CreatedOn = createdOn, UpdatedOn = updatedOn });
		}

		private async Task SetScheduleAsync(int recurringCallId, bool active, bool deleted)
		{
			await using var database = Connect(_connection);
			await database.ExecuteAsync(IsPostgres
				? "UPDATE recurringcalls SET isactive = @Active, isdeleted = @Deleted WHERE recurringcallid = @Id"
				: "UPDATE RecurringCalls SET IsActive = @Active, IsDeleted = @Deleted WHERE RecurringCallId = @Id",
				new { Id = recurringCallId, Active = active, Deleted = deleted });
		}

		private static RecurringCallOccurrence Claim(int recurringCallId, DateTime occurrenceOn, DateTime stamp) => new RecurringCallOccurrence
		{
			RecurringCallId = recurringCallId,
			DepartmentId = DepartmentId,
			OccurrenceOn = occurrenceOn,
			State = (int)RecurringCallOccurrenceStates.Creating,
			UpdatedOn = stamp
		};

		private async Task<(int State, int? CallId, DateTime UpdatedOn)?> ReadOccurrenceAsync(string id)
		{
			await using var database = Connect(_connection);
			var row = await database.QuerySingleOrDefaultAsync(IsPostgres
				? "SELECT state, callid, updatedon FROM recurringcalloccurrences WHERE recurringcalloccurrenceid = @Id::citext"
				: "SELECT State AS state, CallId AS callid, UpdatedOn AS updatedon FROM RecurringCallOccurrences WHERE RecurringCallOccurrenceId = @Id", new { Id = id });
			return row == null ? null : ((int)row.state, (int?)row.callid, (DateTime)row.updatedon);
		}

		[Test]
		public async Task A_worker_claims_an_occurrence_only_while_its_schedule_runs_as_it_read_it()
		{
			var created = Stamp(DateTime.UtcNow.AddHours(-2));
			var scheduleId = await SeedScheduleAsync(created);
			var occurrences = Occurrences();
			var first = new DateTime(2026, 12, 1, 8, 0, 0, DateTimeKind.Utc);

			(await occurrences.TryClaimWhileRunningAsync(Claim(scheduleId, first, Stamp(DateTime.UtcNow)), created)).Should().BeTrue();
			(await occurrences.TryClaimWhileRunningAsync(Claim(scheduleId, first, Stamp(DateTime.UtcNow)), created)).Should().BeFalse("the occurrence is claimed already");
			(await occurrences.TryClaimWhileRunningAsync(Claim(scheduleId, first.AddDays(1), Stamp(DateTime.UtcNow)), created.AddSeconds(5)))
				.Should().BeFalse("the worker read another revision");

			await SetScheduleAsync(scheduleId, active: false, deleted: false);
			(await occurrences.TryClaimWhileRunningAsync(Claim(scheduleId, first.AddDays(2), Stamp(DateTime.UtcNow)), created)).Should().BeFalse("paused");

			await SetScheduleAsync(scheduleId, active: true, deleted: true);
			(await occurrences.TryClaimWhileRunningAsync(Claim(scheduleId, first.AddDays(3), Stamp(DateTime.UtcNow)), created)).Should().BeFalse("deleted");

			await SetScheduleAsync(scheduleId, active: true, deleted: false);
			(await occurrences.TryClaimWhileRunningAsync(Claim(scheduleId, first.AddDays(4), Stamp(DateTime.UtcNow)), created)).Should().BeTrue("running again");

			// An edited schedule's revision is its last change.
			var edited = Stamp(DateTime.UtcNow.AddMinutes(-5));
			var editedId = await SeedScheduleAsync(created, edited);
			(await occurrences.TryClaimWhileRunningAsync(Claim(editedId, first, Stamp(DateTime.UtcNow)), created)).Should().BeFalse("read before the edit");
			(await occurrences.TryClaimWhileRunningAsync(Claim(editedId, first, Stamp(DateTime.UtcNow)), edited)).Should().BeTrue();
		}

		[Test]
		public async Task A_claim_is_renewed_and_completed_only_under_its_own_stamp()
		{
			var created = Stamp(DateTime.UtcNow.AddHours(-2));
			var scheduleId = await SeedScheduleAsync(created);
			var occurrences = Occurrences();
			var claimedOn = Stamp(DateTime.UtcNow.AddSeconds(-30));
			var claim = Claim(scheduleId, new DateTime(2026, 12, 2, 8, 0, 0, DateTimeKind.Utc), claimedOn);
			(await occurrences.TryClaimAsync(claim)).Should().BeTrue();

			// The stamp reads back exactly as written.
			(await ReadOccurrenceAsync(claim.RecurringCallOccurrenceId)).Value.UpdatedOn.Should().Be(claimedOn);

			var renewedOn = Stamp(DateTime.UtcNow);
			(await occurrences.TryRenewClaimAsync(claim.RecurringCallOccurrenceId, claimedOn.AddMilliseconds(-1), renewedOn, created)).Should().BeFalse("not this claim");
			(await occurrences.TryRenewClaimAsync(claim.RecurringCallOccurrenceId, claimedOn, renewedOn, created.AddSeconds(5))).Should().BeFalse("schedule changed");
			(await occurrences.TryRenewClaimAsync(claim.RecurringCallOccurrenceId, claimedOn, renewedOn, created)).Should().BeTrue();
			(await ReadOccurrenceAsync(claim.RecurringCallOccurrenceId)).Value.UpdatedOn.Should().Be(renewedOn);

			(await occurrences.TryCompleteClaimAsync(claim.RecurringCallOccurrenceId, 501, claimedOn, Stamp(DateTime.UtcNow))).Should().BeFalse("the claim was renewed since");
			(await occurrences.TryCompleteClaimAsync(claim.RecurringCallOccurrenceId, 501, renewedOn, Stamp(DateTime.UtcNow))).Should().BeTrue();

			var stored = (await ReadOccurrenceAsync(claim.RecurringCallOccurrenceId)).Value;
			stored.State.Should().Be((int)RecurringCallOccurrenceStates.Created);
			stored.CallId.Should().Be(501);
			(await occurrences.TryRenewClaimAsync(claim.RecurringCallOccurrenceId, renewedOn, Stamp(DateTime.UtcNow), null)).Should().BeFalse("no longer creating");
		}

		[Test]
		public async Task A_renewal_with_the_schedule_paused_is_refused()
		{
			var created = Stamp(DateTime.UtcNow.AddHours(-2));
			var scheduleId = await SeedScheduleAsync(created);
			var occurrences = Occurrences();
			var claimedOn = Stamp(DateTime.UtcNow);
			var claim = Claim(scheduleId, new DateTime(2026, 12, 3, 8, 0, 0, DateTimeKind.Utc), claimedOn);
			(await occurrences.TryClaimAsync(claim)).Should().BeTrue();

			await SetScheduleAsync(scheduleId, active: false, deleted: false);

			(await occurrences.TryRenewClaimAsync(claim.RecurringCallOccurrenceId, claimedOn, Stamp(DateTime.UtcNow.AddSeconds(1)), created)).Should().BeFalse();
			(await occurrences.TryRenewClaimAsync(claim.RecurringCallOccurrenceId, claimedOn, Stamp(DateTime.UtcNow.AddSeconds(1)), null))
				.Should().BeTrue("without a revision only the claim itself is checked");
		}

		[Test]
		public async Task A_claim_is_returned_or_dropped_only_under_its_own_stamp()
		{
			var scheduleId = await SeedScheduleAsync(Stamp(DateTime.UtcNow.AddHours(-2)));
			var occurrences = Occurrences();

			var returnedOn = Stamp(DateTime.UtcNow.AddSeconds(-10));
			var returned = Claim(scheduleId, new DateTime(2026, 12, 4, 8, 0, 0, DateTimeKind.Utc), returnedOn);
			(await occurrences.TryClaimAsync(returned)).Should().BeTrue();
			(await occurrences.TryReturnClaimAsync(returned.RecurringCallOccurrenceId, returnedOn.AddMilliseconds(1), (int)RecurringCallOccurrenceStates.AwaitingDispatcher,
				"dispatcher", Stamp(DateTime.UtcNow))).Should().BeFalse();
			(await occurrences.TryReturnClaimAsync(returned.RecurringCallOccurrenceId, returnedOn, (int)RecurringCallOccurrenceStates.AwaitingDispatcher,
				"dispatcher", Stamp(DateTime.UtcNow))).Should().BeTrue();
			(await ReadOccurrenceAsync(returned.RecurringCallOccurrenceId)).Value.State.Should().Be((int)RecurringCallOccurrenceStates.AwaitingDispatcher);

			var droppedOn = Stamp(DateTime.UtcNow.AddSeconds(-5));
			var dropped = Claim(scheduleId, new DateTime(2026, 12, 5, 8, 0, 0, DateTimeKind.Utc), droppedOn);
			(await occurrences.TryClaimAsync(dropped)).Should().BeTrue();
			(await occurrences.DeleteClaimAsync(dropped.RecurringCallOccurrenceId, droppedOn.AddMilliseconds(-1))).Should().BeFalse();
			(await occurrences.DeleteClaimAsync(dropped.RecurringCallOccurrenceId, droppedOn)).Should().BeTrue();
			(await ReadOccurrenceAsync(dropped.RecurringCallOccurrenceId)).Should().BeNull();
		}

		[Test]
		public async Task An_alert_is_sent_under_one_notification_claim_at_a_time()
		{
			var scheduleId = await SeedScheduleAsync(Stamp(DateTime.UtcNow.AddHours(-2)));
			var alerts = Alerts();
			var occurrenceOn = new DateTime(2026, 12, 6, 8, 0, 0, DateTimeKind.Utc);
			var alert = new RecurringCallAlert
			{
				DepartmentId = DepartmentId,
				RecurringCallId = scheduleId,
				OccurrenceOn = occurrenceOn,
				Kind = (int)RecurringCallAlertKinds.Missed,
				CreatedOn = DateTime.UtcNow
			};
			(await alerts.TryRaiseAsync(alert)).Should().BeTrue();

			var first = Stamp(DateTime.UtcNow);
			(await alerts.TryClaimNotificationAsync(alert.RecurringCallAlertId, first)).Should().BeTrue();
			(await alerts.TryClaimNotificationAsync(alert.RecurringCallAlertId, Stamp(DateTime.UtcNow.AddSeconds(1)))).Should().BeFalse("someone is sending it");
			(await alerts.ReleaseNotificationClaimAsync(alert.RecurringCallAlertId, first.AddMilliseconds(1))).Should().BeFalse("not that claim");
			(await alerts.ReleaseNotificationClaimAsync(alert.RecurringCallAlertId, first)).Should().BeTrue();

			(await alerts.TryResolveAsync(alert.RecurringCallAlertId, 1, "dispatcher", null, DateTime.UtcNow)).Should().BeTrue();
			(await alerts.TryClaimNotificationAsync(alert.RecurringCallAlertId, Stamp(DateTime.UtcNow))).Should().BeFalse("closed meanwhile");

			// Closed, the occurrence can be alerted again (the unique index covers open alerts only).
			var again = new RecurringCallAlert
			{
				DepartmentId = DepartmentId,
				RecurringCallId = scheduleId,
				OccurrenceOn = occurrenceOn,
				Kind = (int)RecurringCallAlertKinds.Missed,
				CreatedOn = DateTime.UtcNow
			};
			(await alerts.TryRaiseAsync(again)).Should().BeTrue();
			(await alerts.TryRaiseAsync(new RecurringCallAlert
			{
				DepartmentId = DepartmentId,
				RecurringCallId = scheduleId,
				OccurrenceOn = occurrenceOn,
				Kind = (int)RecurringCallAlertKinds.Missed,
				CreatedOn = DateTime.UtcNow
			})).Should().BeFalse("one open alert per occurrence and kind");
		}
	}

	/// <summary>
	/// Real-database proof for the call broadcast outbox (M0321) on both engines: claims (due, ignoring the schedule, a lapsed lease),
	/// ending and handing back only under the claim's token, superseding only a pending row, the TOP / LIMIT due scan and the purge.
	/// Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class CallBroadcastOutboxDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "call_broadcast_outbox_";
		private DatabaseTypes _previous;
		private string _master, _connection, _database;
		private ServiceProvider _runner;
		private int _nextCallId = 100;

		private bool IsPostgres => type == DatabaseTypes.Postgres;

		private DbConnection Connect(string connection) => IsPostgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);

		private CallBroadcastOutboxRepository Repository()
		{
			var connections = new Mock<IConnectionProvider>();
			connections.Setup(c => c.Create()).Returns(() => Connect(_connection));
			var list = new Mock<IQueryList>();
			list.Setup(l => l.RetrieveQueryList()).Returns(new ConcurrentDictionary<Type, IQuery>());
			return new CallBroadcastOutboxRepository(connections.Object, IsPostgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration(),
				Mock.Of<IUnitOfWork>(), new QueryFactory(list.Object));
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

			if (IsPostgres)
			{
				await using var database = Connect(_connection);
				await database.OpenAsync();
				await database.ExecuteAsync("CREATE EXTENSION IF NOT EXISTS citext;");

				// The data source loaded its types before citext existed; without a reload a citext column cannot be read back.
				await ((NpgsqlConnection)database).ReloadTypesAsync();
			}

			var source = new Mock<IMigrationSource>();
			source.Setup(s => s.GetMigrations()).Returns(IsPostgres
				? new IMigration[] { new M0321_AddCallBroadcastOutboxPg() }
				: new IMigration[] { new M0321_AddCallBroadcastOutbox() });
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

		private async Task<CallBroadcastOutboxEntry> InsertAsync(DateTime nextAttemptOn, int? callId = null)
		{
			var entry = new CallBroadcastOutboxEntry
			{
				DepartmentId = 7,
				CallId = callId ?? ++_nextCallId,
				State = (int)CallBroadcastOutboxStates.Pending,
				NextAttemptOn = nextAttemptOn,
				CreatedByUserId = "dispatcher",
				CreatedOn = DateTime.UtcNow
			};
			entry.SetRecipients(new[] { "Medic-User" }, new[] { 3 }, new[] { 11, 12 }, null);
			await Repository().InsertEntryAsync(entry);
			return entry;
		}

		[Test]
		public async Task A_row_round_trips_and_is_claimed_by_one_sender()
		{
			var repository = Repository();
			var entry = await InsertAsync(DateTime.UtcNow.AddMinutes(2));

			var stored = await repository.GetEntryAsync(entry.CallBroadcastOutboxId.ToUpperInvariant());
			stored.Should().NotBeNull("ids compare case-insensitively");
			stored.GetUserIds().Should().Equal(new[] { "Medic-User" });
			stored.GetUnitIds().Should().Equal(new[] { 11, 12 });
			stored.RoleIds.Should().BeNull();
			(await repository.GetOpenIdsForCallAsync(entry.CallId, DateTime.UtcNow)).Should().ContainSingle();

			var now = DateTime.UtcNow;
			(await repository.TryClaimAsync(entry.CallBroadcastOutboxId, "worker", now, now.AddMinutes(2), false)).Should().BeFalse("not due yet");
			(await repository.TryClaimAsync(entry.CallBroadcastOutboxId, "edit", now, now.AddMinutes(2), true)).Should().BeTrue("the edit sends its own row at once");
			(await repository.TryClaimAsync(entry.CallBroadcastOutboxId, "other", now, now.AddMinutes(2), true)).Should().BeFalse("claimed, lease running");
			(await repository.GetOpenIdsForCallAsync(entry.CallId, DateTime.UtcNow)).Should().BeEmpty();

			(await repository.CompleteAsync(entry.CallBroadcastOutboxId, "other", CallBroadcastOutboxStates.Sent, DateTime.UtcNow, null)).Should().BeFalse();
			(await repository.CompleteAsync(entry.CallBroadcastOutboxId, "edit", CallBroadcastOutboxStates.Sent, DateTime.UtcNow, null)).Should().BeTrue();

			stored = await repository.GetEntryAsync(entry.CallBroadcastOutboxId);
			stored.State.Should().Be((int)CallBroadcastOutboxStates.Sent);
			stored.Attempts.Should().Be(1);
			stored.CompletedOn.Should().NotBeNull();
		}

		[Test]
		public async Task A_handed_back_row_is_due_again_and_the_due_scan_takes_the_oldest_first()
		{
			var repository = Repository();
			var refused = await InsertAsync(DateTime.UtcNow.AddMinutes(2));
			var now = DateTime.UtcNow;
			(await repository.TryClaimAsync(refused.CallBroadcastOutboxId, "edit", now, now.AddMinutes(2), true)).Should().BeTrue();
			(await repository.RetryLaterAsync(refused.CallBroadcastOutboxId, "other", now.AddMinutes(-2), "refused")).Should().BeFalse("not that claim");
			(await repository.RetryLaterAsync(refused.CallBroadcastOutboxId, "edit", now.AddMinutes(-2), "refused")).Should().BeTrue();

			var stored = await repository.GetEntryAsync(refused.CallBroadcastOutboxId);
			stored.State.Should().Be((int)CallBroadcastOutboxStates.Pending);
			stored.ClaimToken.Should().BeNull();
			stored.LastError.Should().Be("refused");

			var later = await InsertAsync(DateTime.UtcNow.AddMinutes(-1));
			(await repository.GetDueIdsAsync(DateTime.UtcNow, 1)).Should().Equal(new[] { refused.CallBroadcastOutboxId }, "oldest due first, one row asked for");
			(await repository.GetDueIdsAsync(DateTime.UtcNow, 10)).Should().Contain(new[] { refused.CallBroadcastOutboxId, later.CallBroadcastOutboxId });
			(await repository.TryClaimAsync(later.CallBroadcastOutboxId, "worker", DateTime.UtcNow, DateTime.UtcNow.AddMinutes(2), false)).Should().BeTrue("due");
		}

		[Test]
		public async Task A_claim_whose_lease_lapsed_is_taken_over()
		{
			var repository = Repository();
			var entry = await InsertAsync(DateTime.UtcNow.AddMinutes(2));
			var now = DateTime.UtcNow;
			(await repository.TryClaimAsync(entry.CallBroadcastOutboxId, "stopped", now.AddMinutes(-5), now.AddSeconds(-1), true)).Should().BeTrue();

			(await repository.GetDueIdsAsync(DateTime.UtcNow, 100)).Should().Contain(entry.CallBroadcastOutboxId);
			(await repository.TryClaimAsync(entry.CallBroadcastOutboxId, "worker", DateTime.UtcNow, DateTime.UtcNow.AddMinutes(2), false)).Should().BeTrue();
			(await repository.CompleteAsync(entry.CallBroadcastOutboxId, "stopped", CallBroadcastOutboxStates.Sent, DateTime.UtcNow, null))
				.Should().BeFalse("the stopped sender lost its claim");
			(await repository.GetEntryAsync(entry.CallBroadcastOutboxId)).Attempts.Should().Be(2);
		}

		[Test]
		public async Task Only_a_pending_row_is_superseded_and_finished_rows_are_purged()
		{
			var repository = Repository();
			var pending = await InsertAsync(DateTime.UtcNow.AddMinutes(2));
			var sending = await InsertAsync(DateTime.UtcNow.AddMinutes(2));
			var now = DateTime.UtcNow;
			(await repository.TryClaimAsync(sending.CallBroadcastOutboxId, "edit", now, now.AddMinutes(2), true)).Should().BeTrue();

			(await repository.TrySupersedeAsync(pending.CallBroadcastOutboxId, DateTime.UtcNow)).Should().BeTrue();
			(await repository.TrySupersedeAsync(pending.CallBroadcastOutboxId, DateTime.UtcNow)).Should().BeFalse("already ended");
			(await repository.TrySupersedeAsync(sending.CallBroadcastOutboxId, DateTime.UtcNow)).Should().BeFalse("being sent");

			(await repository.PurgeCompletedBeforeAsync(DateTime.UtcNow.AddMinutes(-1))).Should().Be(0, "it ended just now");
			(await repository.PurgeCompletedBeforeAsync(DateTime.UtcNow.AddMinutes(1))).Should().BeGreaterThanOrEqualTo(1);
			(await repository.GetEntryAsync(pending.CallBroadcastOutboxId)).Should().BeNull();
			(await repository.GetEntryAsync(sending.CallBroadcastOutboxId)).Should().NotBeNull("still being sent");
		}
	}
}
