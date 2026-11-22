using System;
using System.Collections.Generic;
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
using Resgrid.Repositories.DataRepository.Servers.PostgreSql;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;

namespace Resgrid.Tests.Repositories
{
	/// <summary>
	/// M0324 dispatcher desk tables on real SQL Server and PostgreSQL: the migration, entity inserts with string keys, the
	/// version-conditional queue update, case-insensitive (citext) user and key lookups, break state transitions and alert stamps,
	/// the unique presence row, the shift day join and the closed-item purge. Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION /
	/// RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level connections) to run; ignored otherwise.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class DispatcherDeskRepositoriesDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "dispatcher_desk_";
		private const int Dept = 7;
		private static readonly DateTime Now = new DateTime(2026, 11, 19, 16, 0, 0, DateTimeKind.Utc);

		private DatabaseTypes _previous;
		private string _master, _connection, _database;
		private ServiceProvider _runner;

		private bool IsPostgres => type == DatabaseTypes.Postgres;

		private DbConnection Connect(string connection) => IsPostgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);

		private (IConnectionProvider Connections, SqlConfiguration Configuration, QueryFactory Queries) Parts()
		{
			var connections = new Mock<IConnectionProvider>();
			connections.Setup(c => c.Create()).Returns(() => Connect(_connection));
			SqlConfiguration configuration = IsPostgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration();

			// The entity insert / update / delete / by-id queries the base repository uses (the real QueryList needs the service locator).
			var queries = new System.Collections.Concurrent.ConcurrentDictionary<Type, IQuery>();
			queries[typeof(InsertQuery)] = new InsertQuery(configuration);
			queries[typeof(UpdateQuery)] = new UpdateQuery(configuration);
			queries[typeof(DeleteQuery)] = new DeleteQuery(configuration);
			queries[typeof(SelectByIdQuery)] = new SelectByIdQuery(configuration);
			var list = new Mock<IQueryList>();
			list.Setup(l => l.RetrieveQueryList()).Returns(queries);

			return (connections.Object, configuration, new QueryFactory(list.Object));
		}

		private T Repo<T>(Func<IConnectionProvider, SqlConfiguration, IUnitOfWork, QueryFactory, T> create)
		{
			var (connections, configuration, queries) = Parts();
			return create(connections, configuration, Mock.Of<IUnitOfWork>(), queries);
		}

		private DispatcherQueueItemsRepository Items() => Repo((c, s, u, q) => new DispatcherQueueItemsRepository(c, s, u, q));
		private DispatcherQueueEventsRepository Events() => Repo((c, s, u, q) => new DispatcherQueueEventsRepository(c, s, u, q));
		private DispatcherConversationMessagesRepository Messages() => Repo((c, s, u, q) => new DispatcherConversationMessagesRepository(c, s, u, q));
		private DispatcherPresencesRepository Presences() => Repo((c, s, u, q) => new DispatcherPresencesRepository(c, s, u, q));
		private DispatcherBreaksRepository Breaks() => Repo((c, s, u, q) => new DispatcherBreaksRepository(c, s, u, q));
		private ShiftDispatchersRepository Dispatchers() => Repo((c, s, u, q) => new ShiftDispatchersRepository(c, s, u, q));
		private ShiftDispatchSettingsRepository Settings() => Repo((c, s, u, q) => new ShiftDispatchSettingsRepository(c, s, u, q));

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

			// citext, and minimal stand-ins for the shift tables the duty query joins (M0001 shapes).
			await using (var database = Connect(_connection))
			{
				await database.OpenAsync();
				await database.ExecuteAsync(IsPostgres
					? "CREATE EXTENSION IF NOT EXISTS citext; " +
					  "CREATE TABLE shifts (shiftid int PRIMARY KEY, departmentid int NOT NULL, name citext, color citext, starttime citext, endtime citext, hours int NULL, " +
					  "assignmenttype int NOT NULL DEFAULT 0, requireapproval boolean NULL); " +
					  "CREATE TABLE shiftdays (shiftdayid int PRIMARY KEY, shiftid int NOT NULL, day timestamp NOT NULL, processed boolean NULL);"
					: "CREATE TABLE Shifts (ShiftId int PRIMARY KEY, DepartmentId int NOT NULL, Name nvarchar(max), Color nvarchar(max), StartTime nvarchar(max), " +
					  "EndTime nvarchar(max), Hours int NULL, AssignmentType int NOT NULL DEFAULT 0, RequireApproval bit NULL); " +
					  "CREATE TABLE ShiftDays (ShiftDayId int PRIMARY KEY, ShiftId int NOT NULL, Day datetime NOT NULL, Processed bit NULL);");

				if (database is NpgsqlConnection postgres)
					await postgres.ReloadTypesAsync();
			}

			var source = new Mock<IMigrationSource>();
			source.Setup(s => s.GetMigrations()).Returns(IsPostgres ? new IMigration[] { new M0324_AddDispatcherDeskPg() } : new IMigration[] { new M0324_AddDispatcherDesk() });
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

		private static DispatcherQueueItem NewItem(int departmentId = Dept, int? callId = null, string key = null, string reference = null,
			DispatcherQueueItemStates state = DispatcherQueueItemStates.Queued, DateTime? created = null) => new DispatcherQueueItem
		{
			DispatcherQueueItemId = Guid.NewGuid().ToString(),
			DepartmentId = departmentId,
			Kind = (int)DispatcherQueueItemKinds.Text,
			State = (int)state,
			CallId = callId,
			Priority = 300,
			SourceAddress = "+15551230001",
			Preview = "Fire in the barn",
			ConversationKey = key,
			ExternalReference = reference,
			CreatedOn = created ?? Now,
			LastActivityOn = created ?? Now
		};

		[Test]
		public async Task Queue_items_insert_with_their_string_key_and_read_back_whatever_the_id_case()
		{
			var item = NewItem();
			await Items().InsertAsync(item, CancellationToken.None, true);

			var read = await Items().GetItemAsync(item.DispatcherQueueItemId.ToUpperInvariant());

			read.Should().NotBeNull();
			read.Preview.Should().Be("Fire in the barn");
			read.CreatedOn.Should().Be(Now);
			read.Version.Should().Be(0);
		}

		[Test]
		public async Task The_version_update_refuses_a_stale_writer_and_bumps_the_version()
		{
			var item = NewItem();
			await Items().InsertAsync(item, CancellationToken.None, true);

			item.State = (int)DispatcherQueueItemStates.Offered;
			item.AssignedUserId = "Ann-User";
			item.OfferExpiresOn = Now.AddSeconds(45);
			item.DeclinedUserIds = "bob";

			(await Items().TryUpdateAsync(item, 5)).Should().BeFalse();
			(await Items().TryUpdateAsync(item, 0)).Should().BeTrue();
			item.Version.Should().Be(1);

			var stale = await Items().GetItemAsync(item.DispatcherQueueItemId);
			stale.Version.Should().Be(1);
			stale.AssignedUserId.Should().Be("Ann-User");
			stale.OfferExpiresOn.Should().Be(Now.AddSeconds(45));
			(await Items().TryUpdateAsync(stale, 0)).Should().BeFalse("a second dispatcher holding version 0 must lose");
		}

		[Test]
		public async Task Open_item_lookups_by_call_conversation_and_reference()
		{
			var forCall = NewItem(callId: 4321);
			var conversation = NewItem(key: "3:+15551230009", created: Now.AddMinutes(-5));
			var closed = NewItem(callId: 4321, state: DispatcherQueueItemStates.Completed);
			var voice = NewItem(reference: "CA-abc123");
			foreach (var item in new[] { forCall, conversation, closed, voice })
				await Items().InsertAsync(item, CancellationToken.None, true);

			(await Items().GetOpenForCallAsync(Dept, 4321)).Select(x => x.DispatcherQueueItemId).Should().Equal(forCall.DispatcherQueueItemId);
			(await Items().AnyForCallAsync(Dept, 4321)).Should().BeTrue();
			(await Items().AnyForCallAsync(Dept, 9999)).Should().BeFalse();

			(await Items().GetOpenByConversationKeyAsync(Dept, "3:+15551230009", Now.AddMinutes(-30)))?.DispatcherQueueItemId.Should().Be(conversation.DispatcherQueueItemId);
			(await Items().GetOpenByConversationKeyAsync(Dept, "3:+15551230009", Now.AddMinutes(-1))).Should().BeNull("its last activity is older than the join window");
			(await Items().GetOpenByConversationKeyAnyDepartmentAsync("3:+15551230009", Now.AddMinutes(-30))).Should().NotBeNull();

			(await Items().GetByExternalReferenceAsync("ca-ABC123"))?.DispatcherQueueItemId.Should().Be(voice.DispatcherQueueItemId);
			(await Items().GetOpenForDepartmentAsync(Dept)).Should().NotContain(x => x.DispatcherQueueItemId == closed.DispatcherQueueItemId);
			(await Items().GetDepartmentsWithOpenItemsAsync()).Should().Contain(Dept);
			(await Items().GetCreatedInRangeAsync(Dept, Now.AddMinutes(-10), Now.AddMinutes(1))).Should().HaveCountGreaterThanOrEqualTo(4);
		}

		[Test]
		public async Task Purging_removes_old_closed_items_with_their_history_and_messages_only()
		{
			var old = NewItem(departmentId: 90, state: DispatcherQueueItemStates.Completed, created: Now.AddDays(-200));
			var recent = NewItem(departmentId: 90, state: DispatcherQueueItemStates.Completed, created: Now.AddDays(-2));
			var openOld = NewItem(departmentId: 90, created: Now.AddDays(-200));
			foreach (var item in new[] { old, recent, openOld })
				await Items().InsertAsync(item, CancellationToken.None, true);

			await Events().InsertAsync(new DispatcherQueueEvent
			{
				DispatcherQueueEventId = Guid.NewGuid().ToString(), DepartmentId = 90, DispatcherQueueItemId = old.DispatcherQueueItemId,
				EventType = (int)DispatcherQueueEventTypes.Completed, OccurredOn = Now.AddDays(-200)
			}, CancellationToken.None, true);
			await Messages().InsertAsync(new DispatcherConversationMessage
			{
				DispatcherConversationMessageId = Guid.NewGuid().ToString(), DepartmentId = 90, DispatcherQueueItemId = old.DispatcherQueueItemId,
				Direction = (int)DispatcherMessageDirections.Inbound, Body = "old", CreatedOn = Now.AddDays(-200), ProviderMessageId = "SM-old"
			}, CancellationToken.None, true);

			(await Items().PurgeClosedBeforeAsync(Now.AddDays(-180))).Should().Be(1);

			(await Items().GetItemAsync(old.DispatcherQueueItemId)).Should().BeNull();
			(await Items().GetItemAsync(recent.DispatcherQueueItemId)).Should().NotBeNull();
			(await Items().GetItemAsync(openOld.DispatcherQueueItemId)).Should().NotBeNull("open work is never purged");
			(await Events().GetForItemAsync(old.DispatcherQueueItemId)).Should().BeEmpty();
			(await Messages().GetByProviderMessageIdAsync("SM-old")).Should().BeNull();
		}

		[Test]
		public async Task Messages_are_found_by_provider_id_and_item()
		{
			var item = NewItem();
			await Items().InsertAsync(item, CancellationToken.None, true);
			await Messages().InsertAsync(new DispatcherConversationMessage
			{
				DispatcherConversationMessageId = Guid.NewGuid().ToString(), DepartmentId = Dept, DispatcherQueueItemId = item.DispatcherQueueItemId,
				Direction = (int)DispatcherMessageDirections.Inbound, Body = new string('x', 3000), CreatedOn = Now, ProviderMessageId = "SM-Abc"
			}, CancellationToken.None, true);

			(await Messages().GetByProviderMessageIdAsync("sm-abc")).Should().NotBeNull();
			(await Messages().GetForItemAsync(item.DispatcherQueueItemId)).Single().Body.Length.Should().Be(3000);
		}

		[Test]
		public async Task One_presence_row_per_member_and_lookups_ignore_user_id_case()
		{
			var presence = new DispatcherPresence
			{
				DispatcherPresenceId = Guid.NewGuid().ToString(), DepartmentId = Dept, UserId = "Mixed-Case-Ann", State = (int)DispatcherPresenceStates.Available,
				StateChangedOn = Now, SignedInOn = Now, CallbackNumber = "+15551230009"
			};
			(await Presences().TryInsertAsync(presence)).Should().BeTrue();

			var duplicate = new DispatcherPresence { DepartmentId = Dept, UserId = "Mixed-Case-Ann", StateChangedOn = Now };
			if (IsPostgres)
				(await Presences().TryInsertAsync(duplicate)).Should().BeFalse("the unique (department, user) index refuses a second row");
			else
				(await Presences().TryInsertAsync(duplicate)).Should().BeFalse();

			var read = await Presences().GetForUserAsync(Dept, "mixed-case-ann");
			read.Should().NotBeNull();
			read.CallbackNumber.Should().Be("+15551230009");
			(await Presences().GetSignedInAsync()).Should().Contain(x => x.DispatcherPresenceId == presence.DispatcherPresenceId);

			read.State = (int)DispatcherPresenceStates.Offline;
			await Presences().SaveOrUpdateAsync(read, CancellationToken.None, true);
			(await Presences().GetSignedInAsync()).Should().NotContain(x => x.DispatcherPresenceId == presence.DispatcherPresenceId);
		}

		[Test]
		public async Task Break_transitions_are_conditional_and_alerts_are_stamped_once()
		{
			var planned = new DispatcherBreak
			{
				DispatcherBreakId = Guid.NewGuid().ToString(), DepartmentId = Dept, UserId = "Ann-Break", BreakType = (int)DispatcherBreakTypes.Meal,
				State = (int)DispatcherBreakStates.Scheduled, ScheduledStartOn = Now.AddMinutes(20), DurationMinutes = 60, CreatedOn = Now, ShiftDayId = 100
			};
			await Breaks().InsertAsync(planned, CancellationToken.None, true);

			(await Breaks().GetOpenForUserAsync(Dept, "ann-break", Now.AddHours(-1), Now.AddHours(2))).Should().ContainSingle();
			(await Breaks().GetForTimersAsync(Now.AddMinutes(30), Now.AddHours(-1))).Should().Contain(x => x.DispatcherBreakId == planned.DispatcherBreakId);
			(await Breaks().GetForShiftDayAsync(100)).Should().ContainSingle();

			(await Breaks().TryMarkAlertAsync(planned.DispatcherBreakId, "ReminderSentOn", Now)).Should().BeTrue();
			(await Breaks().TryMarkAlertAsync(planned.DispatcherBreakId, "ReminderSentOn", Now.AddMinutes(1))).Should().BeFalse();
			Func<Task> injected = () => Breaks().TryMarkAlertAsync(planned.DispatcherBreakId, "Note; DROP TABLE x", Now);
			await injected.Should().ThrowAsync<ArgumentOutOfRangeException>();

			planned.State = (int)DispatcherBreakStates.Active;
			planned.StartedOn = Now.AddMinutes(-10);
			planned.ExpectedReturnOn = Now.AddMinutes(50);
			(await Breaks().TryTransitionAsync(planned, (int)DispatcherBreakStates.Active)).Should().BeFalse("it is still Scheduled");
			(await Breaks().TryTransitionAsync(planned, (int)DispatcherBreakStates.Scheduled)).Should().BeTrue();

			var active = await Breaks().GetActiveForUserAsync(Dept, "ANN-BREAK");
			active.ExpectedReturnOn.Should().Be(Now.AddMinutes(50));
			active.ReminderSentOn.Should().Be(Now, "the transition leaves the alert stamps alone");
			(await Breaks().GetForDepartmentInRangeAsync(Dept, Now.AddDays(5), Now.AddDays(6))).Should().Contain(x => x.DispatcherBreakId == planned.DispatcherBreakId,
				"active breaks are always in range");
		}

		[Test]
		public async Task Duty_rows_join_their_shift_days_and_settings_are_one_per_shift()
		{
			await using (var db = Connect(_connection))
			{
				await db.ExecuteAsync(IsPostgres
					? "INSERT INTO shifts (shiftid, departmentid, name, color, starttime, endtime, assignmenttype, requireapproval) VALUES (3, 7, 'Dispatch A', '#123456', '7:00 AM', '7:00 PM', 1, true); " +
					  "INSERT INTO shiftdays (shiftdayid, shiftid, day) VALUES (100, 3, '2026-11-19'), (101, 3, '2026-11-20'), (102, 3, '2026-12-25');"
					: "INSERT INTO Shifts (ShiftId, DepartmentId, Name, Color, StartTime, EndTime, AssignmentType, RequireApproval) VALUES (3, 7, 'Dispatch A', '#123456', '7:00 AM', '7:00 PM', 1, 1); " +
					  "INSERT INTO ShiftDays (ShiftDayId, ShiftId, Day) VALUES (100, 3, '2026-11-19'), (101, 3, '2026-11-20'), (102, 3, '2026-12-25');");
			}

			var days = (await Dispatchers().GetShiftDayTimesAsync(Dept, new[] { 3 }, new DateTime(2026, 11, 19), new DateTime(2026, 11, 20))).ToList();
			days.Select(x => x.ShiftDayId).Should().BeEquivalentTo(new[] { 100, 101 });
			days.First().ShiftName.Should().Be("Dispatch A");
			days.First().RequireApproval.Should().BeTrue();
			(await Dispatchers().GetShiftDayTimesAsync(Dept, new int[0], new DateTime(2026, 11, 19), new DateTime(2026, 11, 20))).Should().BeEmpty();
			(await Dispatchers().GetShiftDayTimesByIdAsync(102)).Day.Should().Be(new DateTime(2026, 12, 25));

			await Dispatchers().SaveOrUpdateAsync(new ShiftDispatcher { DepartmentId = Dept, ShiftId = 3, UserId = "Ann-Roster", Source = 1, AddedOn = Now }, CancellationToken.None, true);
			await Dispatchers().SaveOrUpdateAsync(new ShiftDispatcher { DepartmentId = Dept, ShiftId = 3, ShiftDayId = 100, UserId = "bob", Source = 2, Status = 1, AddedOn = Now },
				CancellationToken.None, true);
			(await Dispatchers().GetByUserIdAsync(Dept, "ann-roster")).Should().ContainSingle();
			(await Dispatchers().GetByShiftIdAsync(3)).Should().HaveCount(2);

			await Settings().SaveOrUpdateAsync(new ShiftDispatchSetting { DepartmentId = Dept, ShiftId = 3, IsEnabled = true, MinimumOnDesk = 2, UpdatedOn = Now }, CancellationToken.None, true);
			Func<Task> second = () => Settings().InsertAsync(new ShiftDispatchSetting
			{
				ShiftDispatchSettingId = Guid.NewGuid().ToString(), DepartmentId = Dept, ShiftId = 3, UpdatedOn = Now
			}, CancellationToken.None, true);
			await second.Should().ThrowAsync<Exception>("one settings row per shift");
			(await Settings().GetByShiftIdAsync(3)).MinimumOnDesk.Should().Be(2);

			(await Dispatchers().DeleteByShiftIdAsync(3)).Should().Be(2);
			(await Settings().DeleteByShiftIdAsync(3)).Should().Be(1);
		}
	}
}
