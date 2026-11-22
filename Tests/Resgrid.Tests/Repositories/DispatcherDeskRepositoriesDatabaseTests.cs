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
		public async Task One_open_item_per_call_and_reference_while_closed_items_never_block_new_work()
		{
			const int dept = 91;
			var first = NewItem(departmentId: dept, callId: 5150);
			(await Items().TryInsertAsync(first)).Should().BeTrue();
			(await Items().TryInsertAsync(NewItem(departmentId: dept, callId: 5150))).Should().BeFalse("a second open item for the call loses");
			(await Items().TryInsertAsync(NewItem(departmentId: dept + 1, callId: 5150))).Should().BeTrue("the key is per department");

			var voice = NewItem(departmentId: dept, reference: "CA-Unique");
			(await Items().TryInsertAsync(voice)).Should().BeTrue();
			(await Items().TryInsertAsync(NewItem(departmentId: dept, reference: "ca-unique"))).Should().BeFalse("a provider retry of the same call");

			// Linking a call another open item has loses like a stale version (no exception).
			var other = NewItem(departmentId: dept);
			(await Items().TryInsertAsync(other)).Should().BeTrue();
			other.CallId = 5150;
			(await Items().TryUpdateAsync(other, 0)).Should().BeFalse();

			// Once the first is closed, the call may come back to the desk.
			first.State = (int)DispatcherQueueItemStates.Completed;
			(await Items().TryUpdateAsync(first, 0)).Should().BeTrue();
			(await Items().TryUpdateAsync(other, 0)).Should().BeTrue();
			(await Items().TryInsertAsync(NewItem(departmentId: dept, callId: 5150, state: DispatcherQueueItemStates.Completed))).Should().BeTrue();

			(await Items().GetCallIdsOnDeskAsync(dept, new[] { 5150, 5151, 5150 })).Should().Equal(5150);
			(await Items().GetCallIdsOnDeskAsync(dept, new int[0])).Should().BeEmpty();

			// The insert race's winner lookup is scoped like the index: this department's open row, whatever another department or a
			// closed row holds under the same reference (newer ones included).
			var elsewhere = NewItem(departmentId: dept + 1, reference: "CA-Unique", created: Now.AddMinutes(1));
			(await Items().TryInsertAsync(elsewhere)).Should().BeTrue();
			(await Items().GetOpenByExternalReferenceAsync(dept, "ca-UNIQUE"))?.DispatcherQueueItemId.Should().Be(voice.DispatcherQueueItemId);
			voice.State = (int)DispatcherQueueItemStates.Completed;
			(await Items().TryUpdateAsync(voice, 0)).Should().BeTrue();
			(await Items().GetOpenByExternalReferenceAsync(dept, "CA-Unique")).Should().BeNull("its only row is closed now");
		}

		[Test]
		public async Task The_offer_stamp_changes_only_the_last_offered_time()
		{
			var presence = new DispatcherPresence
			{
				DispatcherPresenceId = Guid.NewGuid().ToString(), DepartmentId = 92, UserId = "offer-stamp", State = (int)DispatcherPresenceStates.OnBreak,
				StateChangedOn = Now, SignedInOn = Now
			};
			(await Presences().TryInsertAsync(presence)).Should().BeTrue();

			(await Presences().SetLastOfferedOnAsync(presence.DispatcherPresenceId.ToUpperInvariant(), Now.AddMinutes(3))).Should().BeTrue();

			var read = await Presences().GetForUserAsync(92, "offer-stamp");
			read.LastOfferedOn.Should().Be(Now.AddMinutes(3));
			read.State.Should().Be((int)DispatcherPresenceStates.OnBreak);
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
		public async Task A_provider_message_is_stored_once_and_messages_without_one_are_not_limited()
		{
			var item = NewItem();
			await Items().InsertAsync(item, CancellationToken.None, true);

			DispatcherConversationMessage Message(string providerId, DispatcherMessageDirections direction = DispatcherMessageDirections.Inbound) => new DispatcherConversationMessage
			{
				DispatcherConversationMessageId = Guid.NewGuid().ToString(), DepartmentId = Dept, DispatcherQueueItemId = item.DispatcherQueueItemId,
				Direction = (int)direction, Body = "hello", CreatedOn = Now, ProviderMessageId = providerId
			};

			(await Messages().TryInsertAsync(Message("SM-dup"))).Should().BeTrue();
			(await Messages().TryInsertAsync(Message("sm-DUP"))).Should().BeFalse("the provider id is unique regardless of case (UX_DispatcherConversationMessages_Provider)");
			(await Messages().TryInsertAsync(Message(null, DispatcherMessageDirections.Note))).Should().BeTrue();
			(await Messages().TryInsertAsync(Message(null, DispatcherMessageDirections.Outbound))).Should().BeTrue("replies and notes carry no provider id");

			(await Messages().GetForItemAsync(item.DispatcherQueueItemId)).Should().HaveCount(3);
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
		public async Task Presence_writes_are_conditional_on_the_signed_in_session()
		{
			// PR 589: the heartbeat, the duty carry-on and the sign-off each act only on the session they read.
			var signedIn = Now.AddHours(-2);
			var presence = new DispatcherPresence
			{
				DispatcherPresenceId = Guid.NewGuid().ToString(), DepartmentId = Dept, UserId = "Session-Ann", State = (int)DispatcherPresenceStates.Available,
				StateChangedOn = signedIn, SignedInOn = signedIn, LastSeenOn = signedIn, AwayReason = "x", MissedOffers = 2
			};
			(await Presences().TryInsertAsync(presence)).Should().BeTrue();
			var stored = await Presences().GetForUserAsync(Dept, "session-ann");

			(await Presences().TouchAsync(presence.DispatcherPresenceId.ToUpperInvariant(), Now)).Should().BeTrue();
			var otherSession = await Presences().GetForUserAsync(Dept, "session-ann");
			otherSession.SignedInOn = stored.SignedInOn.Value.AddSeconds(1);
			(await Presences().TrySetDutyAsync(otherSession, (int)DispatcherDutyModes.Scheduled, 3, 100, Now.AddHours(8))).Should().BeFalse("another session");
			(await Presences().TrySetDutyAsync(stored, (int)DispatcherDutyModes.Unscheduled, 3, 100, null)).Should().BeTrue();
			(await Presences().TrySignOffAsync(presence.DispatcherPresenceId, stored.SignedInOn.Value.AddSeconds(1), Now)).Should().BeFalse("another session");

			var touched = await Presences().GetForUserAsync(Dept, "Session-Ann");
			touched.LastSeenOn.Should().BeCloseTo(Now, TimeSpan.FromMilliseconds(1));
			touched.State.Should().Be((int)DispatcherPresenceStates.Available, "the heartbeat writes nothing else");
			touched.DutyMode.Should().Be((int)DispatcherDutyModes.Unscheduled);
			touched.DutyEndsOn.Should().BeNull();

			(await Presences().TrySignOffAsync(presence.DispatcherPresenceId, touched.SignedInOn, Now)).Should().BeTrue();
			(await Presences().TrySignOffAsync(presence.DispatcherPresenceId, touched.SignedInOn, Now)).Should().BeFalse("already signed off");
			(await Presences().TouchAsync(presence.DispatcherPresenceId, Now.AddMinutes(1))).Should().BeFalse("a heartbeat cannot bring it back");
			(await Presences().TrySetDutyAsync(touched, (int)DispatcherDutyModes.Scheduled, 3, 100, Now)).Should().BeFalse();

			var off = await Presences().GetForUserAsync(Dept, "Session-Ann");
			off.State.Should().Be((int)DispatcherPresenceStates.Offline);
			off.AwayReason.Should().BeNull();
			off.MissedOffers.Should().Be(0);
			off.LastSeenOn.Should().BeCloseTo(Now, TimeSpan.FromMilliseconds(1));
		}

		[Test]
		public async Task The_duty_carry_on_and_the_return_from_break_write_only_what_the_caller_read()
		{
			// PR 589 round 2: a duty another worker moved on, and a sign-off or later break, win over a stale write.
			var signedIn = Now.AddHours(-3);
			var presence = new DispatcherPresence
			{
				DispatcherPresenceId = Guid.NewGuid().ToString(), DepartmentId = Dept, UserId = "Duty-Bob", State = (int)DispatcherPresenceStates.OnBreak,
				StateChangedOn = signedIn, SignedInOn = signedIn, LastSeenOn = signedIn, DutyMode = (int)DispatcherDutyModes.Scheduled, DutyShiftId = 3,
				DutyShiftDayId = 100, DutyEndsOn = Now.AddMinutes(-1), ActiveBreakId = "Break-One", MissedOffers = 2
			};
			(await Presences().TryInsertAsync(presence)).Should().BeTrue();
			var evaluated = await Presences().GetForUserAsync(Dept, "duty-bob");

			// Another worker carries the session on to the next shift day first.
			(await Presences().TrySetDutyAsync(evaluated, (int)DispatcherDutyModes.Scheduled, 3, 101, Now.AddHours(8))).Should().BeTrue();
			(await Presences().TrySetDutyAsync(evaluated, (int)DispatcherDutyModes.Unscheduled, 3, 100, null)).Should().BeFalse("the duty changed since it was read");

			var carried = await Presences().GetForUserAsync(Dept, "Duty-Bob");
			carried.DutyShiftDayId.Should().Be(101);
			carried.DutyEndsOn.Should().BeCloseTo(Now.AddHours(8), TimeSpan.FromMilliseconds(1));

			// Back from a break other than the one on the row: nothing written.
			var otherBreak = await Presences().GetForUserAsync(Dept, "Duty-Bob");
			otherBreak.ActiveBreakId = "Break-Two";
			(await Presences().TryReturnFromBreakAsync(otherBreak, Now, Now)).Should().BeFalse("a later break is not ended by an older one's return");

			(await Presences().TryReturnFromBreakAsync(carried, Now, null)).Should().BeTrue();
			var back = await Presences().GetForUserAsync(Dept, "Duty-Bob");
			back.State.Should().Be((int)DispatcherPresenceStates.Available);
			back.ActiveBreakId.Should().BeNull();
			back.MissedOffers.Should().Be(0);
			back.LastSeenOn.Should().BeCloseTo(signedIn, TimeSpan.FromMilliseconds(1), "LastSeenOn moves only for the dispatcher's own return");

			// A sign-off committed after the read keeps the row offline.
			(await Presences().TrySignOffAsync(back.DispatcherPresenceId, back.SignedInOn, Now)).Should().BeTrue();
			(await Presences().TryReturnFromBreakAsync(back, Now, Now)).Should().BeFalse("signed off since the read");
			(await Presences().GetForUserAsync(Dept, "Duty-Bob")).State.Should().Be((int)DispatcherPresenceStates.Offline);
		}

		[Test]
		public async Task The_sign_in_write_needs_the_state_session_and_break_it_read()
		{
			// PR 589 round 3: a sign-in, a break or a sign-off committed after the sign-in read the row wins over its write.
			var signedOff = Now.AddHours(-1);
			var presence = new DispatcherPresence
			{
				DispatcherPresenceId = Guid.NewGuid().ToString(), DepartmentId = Dept, UserId = "SignIn-Cat", State = (int)DispatcherPresenceStates.Offline,
				StateChangedOn = signedOff, LastOfferedOn = signedOff, CallbackNumber = "+15555550100"
			};
			(await Presences().TryInsertAsync(presence)).Should().BeTrue();
			var read = await Presences().GetForUserAsync(Dept, "signin-cat");

			var signIn = await Presences().GetForUserAsync(Dept, "signin-cat");
			signIn.State = (int)DispatcherPresenceStates.Available;
			signIn.StateChangedOn = Now;
			signIn.SignedInOn = Now;
			signIn.LastSeenOn = Now;
			signIn.DutyMode = (int)DispatcherDutyModes.Scheduled;
			signIn.DutyShiftId = 3;
			signIn.DutyShiftDayId = 100;
			signIn.DutyEndsOn = Now.AddHours(8);
			signIn.DeviceKind = "web";
			signIn.CallbackNumber = "+15555550199";

			var stale = new DispatcherPresence { DispatcherPresenceId = read.DispatcherPresenceId, State = read.State, SignedInOn = Now.AddMinutes(-5) };
			(await Presences().TrySignInAsync(signIn, stale, cancellationToken: CancellationToken.None)).Should().BeFalse("another session was read");

			(await Presences().TrySignInAsync(signIn, read)).Should().BeTrue("the row is still the signed-off one read");
			var stored = await Presences().GetForUserAsync(Dept, "SignIn-Cat");
			stored.State.Should().Be((int)DispatcherPresenceStates.Available);
			stored.SignedInOn.Should().BeCloseTo(Now, TimeSpan.FromMilliseconds(1));
			stored.DutyShiftDayId.Should().Be(100);
			stored.DutyEndsOn.Should().BeCloseTo(Now.AddHours(8), TimeSpan.FromMilliseconds(1));
			stored.DeviceKind.Should().Be("web");
			stored.CallbackNumber.Should().Be("+15555550199");
			stored.LastOfferedOn.Should().BeCloseTo(signedOff, TimeSpan.FromMilliseconds(1), "the sign-in writes only its own columns");

			(await Presences().TrySignInAsync(signIn, read)).Should().BeFalse("the row read was signed off; it is signed in now");

			// On break: the refresh is conditional on the break read (case-insensitive id).
			stored.State = (int)DispatcherPresenceStates.OnBreak;
			stored.ActiveBreakId = "Break-Cat";
			(await Presences().TrySignInAsync(stored, await Presences().GetForUserAsync(Dept, "signin-cat"))).Should().BeTrue();
			var onBreak = await Presences().GetForUserAsync(Dept, "signin-cat");
			var otherBreak = new DispatcherPresence { DispatcherPresenceId = onBreak.DispatcherPresenceId, State = onBreak.State, SignedInOn = onBreak.SignedInOn, ActiveBreakId = "Break-Other" };
			(await Presences().TrySignInAsync(onBreak, otherBreak)).Should().BeFalse("another break was read");
			var sameBreak = new DispatcherPresence { DispatcherPresenceId = onBreak.DispatcherPresenceId, State = onBreak.State, SignedInOn = onBreak.SignedInOn, ActiveBreakId = "break-cat" };
			(await Presences().TrySignInAsync(onBreak, sameBreak)).Should().BeTrue();
		}

		[Test]
		public async Task A_withdrawal_write_needs_the_source_it_checked()
		{
			var row = new ShiftDispatcher
			{
				// A shift of its own: the fixture's database is shared by the tests, and shift 3's roster is counted elsewhere.
				ShiftDispatcherId = Guid.NewGuid().ToString(), DepartmentId = Dept, ShiftId = 77, ShiftDayId = 7700, UserId = "Signup-Cat",
				Source = (int)ShiftDispatcherSources.Assigned, Status = (int)ShiftDispatcherStatuses.Active, AddedOn = Now
			};
			await Dispatchers().InsertAsync(row, CancellationToken.None, true);

			row.Status = (int)ShiftDispatcherStatuses.Denied;
			row.ReviewedByUserId = "Signup-Cat";
			row.ReviewedOn = Now;
			row.Note = "Withdrawn";
			(await Dispatchers().TryUpdateReviewWithSourceAsync(row, (int)ShiftDispatcherSources.Signup)).Should().BeFalse("a manager's assignment now");
			(await Dispatchers().GetByShiftIdAsync(77)).Single(x => x.ShiftDispatcherId == row.ShiftDispatcherId).Status
				.Should().Be((int)ShiftDispatcherStatuses.Active);

			(await Dispatchers().TryUpdateReviewWithSourceAsync(row, (int)ShiftDispatcherSources.Assigned)).Should().BeTrue();
			var stored = (await Dispatchers().GetByShiftIdAsync(77)).Single(x => x.ShiftDispatcherId == row.ShiftDispatcherId);
			stored.Status.Should().Be((int)ShiftDispatcherStatuses.Denied);
			stored.Note.Should().Be("Withdrawn");
			stored.ReviewedOn.Should().BeCloseTo(Now, TimeSpan.FromMilliseconds(1));
		}

		[Test]
		public async Task One_standing_row_per_shift_and_dispatcher_whatever_the_case_but_day_rows_are_free()
		{
			// PR 589: two editors adding the same dispatcher at once cannot both insert a standing row.
			ShiftDispatcher Row(string userId, int? shiftDayId) => new ShiftDispatcher
			{
				ShiftDispatcherId = Guid.NewGuid().ToString(), DepartmentId = Dept, ShiftId = 4242, ShiftDayId = shiftDayId, UserId = userId,
				Source = (int)ShiftDispatcherSources.Assigned, Status = (int)ShiftDispatcherStatuses.Active, AddedOn = Now
			};

			await Dispatchers().InsertAsync(Row("Standing-Ann", null), CancellationToken.None, true);

			Func<Task> second = () => Dispatchers().InsertAsync(Row("standing-ann", null), CancellationToken.None, true);
			await second.Should().ThrowAsync<Exception>("UX_ShiftDispatchers_Standing");

			await Dispatchers().InsertAsync(Row("standing-ann", 900), CancellationToken.None, true);
			await Dispatchers().InsertAsync(Row("standing-ann", 900), CancellationToken.None, true);
			(await Dispatchers().GetByShiftIdAsync(4242)).Should().HaveCount(3);
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

			var moved = new DispatcherBreak { DispatcherBreakId = planned.DispatcherBreakId, State = planned.State, DurationMinutes = 60, ScheduledStartOn = Now.AddMinutes(25) };
			(await Breaks().TryMarkAlertAsync(moved, "ReminderSentOn", Now)).Should().BeFalse("the timer read another plan than the one stored");
			var started = new DispatcherBreak { DispatcherBreakId = planned.DispatcherBreakId, State = (int)DispatcherBreakStates.Active, DurationMinutes = 60, ScheduledStartOn = planned.ScheduledStartOn };
			(await Breaks().TryMarkAlertAsync(started, "ReminderSentOn", Now)).Should().BeFalse("the stored break is not in the state the timer read");

			// PR 589 round 2: a claim whose push was not accepted is given back, only by the claim that set it (sub-millisecond ticks
			// included: PostgreSQL keeps microseconds).
			var claim = Now.AddTicks(1234);
			(await Breaks().TryMarkAlertAsync(planned, "DueAlertSentOn", claim)).Should().BeTrue();
			(await Breaks().TryClearAlertAsync(planned.DispatcherBreakId, "DueAlertSentOn", claim.AddMinutes(1))).Should().BeFalse("another claim's stamp");
			(await Breaks().TryClearAlertAsync(planned.DispatcherBreakId.ToUpperInvariant(), "DueAlertSentOn", claim)).Should().BeTrue();
			(await Breaks().TryMarkAlertAsync(planned, "DueAlertSentOn", Now)).Should().BeTrue("the next pass claims it again");
			Func<Task> injectedClear = () => Breaks().TryClearAlertAsync(planned.DispatcherBreakId, "Note; DROP TABLE x", Now);
			await injectedClear.Should().ThrowAsync<ArgumentOutOfRangeException>();

			(await Breaks().TryMarkAlertAsync(planned, "ReminderSentOn", Now)).Should().BeTrue();
			(await Breaks().TryMarkAlertAsync(planned, "ReminderSentOn", Now.AddMinutes(1))).Should().BeFalse();
			Func<Task> injected = () => Breaks().TryMarkAlertAsync(planned, "Note; DROP TABLE x", Now);
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
		public async Task Observed_break_updates_need_the_read_plan_and_reset_reminders_in_the_same_write()
		{
			var planned = new DispatcherBreak
			{
				DispatcherBreakId = Guid.NewGuid().ToString(), DepartmentId = Dept, UserId = "Obs-Break", BreakType = (int)DispatcherBreakTypes.Break,
				State = (int)DispatcherBreakStates.Scheduled, ScheduledStartOn = Now.AddMinutes(40).AddTicks(1234560), DurationMinutes = 15, CreatedOn = Now,
				ReminderSentOn = Now, DueAlertSentOn = Now, ReturnAlertSentOn = Now
			};
			await Breaks().InsertAsync(planned, CancellationToken.None, true);
			var read = await Breaks().GetByIdAsync(planned.DispatcherBreakId);
			var observed = new DispatcherBreak { DispatcherBreakId = read.DispatcherBreakId, State = read.State, DurationMinutes = read.DurationMinutes, ScheduledStartOn = read.ScheduledStartOn };

			var stale = new DispatcherBreak { DispatcherBreakId = read.DispatcherBreakId, State = read.State, DurationMinutes = 30, ScheduledStartOn = read.ScheduledStartOn };
			read.ScheduledStartOn = Now.AddHours(2);
			(await Breaks().TryUpdateObservedAsync(read, stale, true)).Should().BeFalse("the plan read was a 30-minute break");

			(await Breaks().TryUpdateObservedAsync(read, observed, true)).Should().BeTrue("the stored plan is the one read back, to the database's precision");
			var replanned = await Breaks().GetByIdAsync(planned.DispatcherBreakId);
			replanned.ScheduledStartOn.Should().Be(Now.AddHours(2));
			replanned.ReminderSentOn.Should().BeNull();
			replanned.DueAlertSentOn.Should().BeNull();
			replanned.ReturnAlertSentOn.Should().Be(Now, "only the plan reminders reset");

			(await Breaks().TryUpdateObservedAsync(read, observed, true)).Should().BeFalse("the plan moved with the first write");

			var ancient = new DispatcherBreak
			{
				DispatcherBreakId = Guid.NewGuid().ToString(), DepartmentId = Dept, UserId = "Obs-Break", State = (int)DispatcherBreakStates.Scheduled,
				ScheduledStartOn = Now.AddDays(-3), DurationMinutes = 15, CreatedOn = Now.AddDays(-3)
			};
			await Breaks().InsertAsync(ancient, CancellationToken.None, true);
			(await Breaks().GetForTimersAsync(Now.AddHours(2), Now.AddHours(-25))).Should().NotContain(x => x.DispatcherBreakId == ancient.DispatcherBreakId);
			(await Breaks().GetOverdueScheduledAsync(Now.AddHours(-25), 50)).Should().Contain(x => x.DispatcherBreakId == ancient.DispatcherBreakId,
				"a break the timer window passed by is still found for the catch-up");
			(await Breaks().GetOverdueScheduledAsync(Now.AddHours(-25), 1)).Should().HaveCount(1);
		}

		[Test]
		public async Task A_break_start_writes_only_the_start_columns_and_needs_the_read_plan()
		{
			// PR 589 round 3: a note or shift-day edit saved after the start read the plan stands; a moved plan refuses the start.
			var planned = new DispatcherBreak
			{
				DispatcherBreakId = Guid.NewGuid().ToString(), DepartmentId = Dept, UserId = "Start-Break", BreakType = (int)DispatcherBreakTypes.Meal,
				State = (int)DispatcherBreakStates.Scheduled, ScheduledStartOn = Now.AddMinutes(5).AddTicks(1234560), DurationMinutes = 15, Note = "Old note",
				ShiftId = 3, ShiftDayId = 100, CreatedOn = Now, ReminderSentOn = Now
			};
			await Breaks().InsertAsync(planned, CancellationToken.None, true);
			var read = await Breaks().GetByIdAsync(planned.DispatcherBreakId);
			var observed = new DispatcherBreak { DispatcherBreakId = read.DispatcherBreakId, State = read.State, DurationMinutes = read.DurationMinutes, ScheduledStartOn = read.ScheduledStartOn };

			// A supervisor's edit lands after the read (plan start and length unchanged).
			var edited = await Breaks().GetByIdAsync(planned.DispatcherBreakId);
			edited.Note = "New note";
			edited.ShiftDayId = 101;
			(await Breaks().TryUpdateObservedAsync(edited, observed, false)).Should().BeTrue();

			read.State = (int)DispatcherBreakStates.Active;
			read.DurationMinutes = 20;
			read.StartedOn = Now;
			read.ExpectedReturnOn = Now.AddMinutes(20);
			read.UpdatedByUserId = "start-break";
			read.UpdatedOn = Now;
			var moved = new DispatcherBreak { DispatcherBreakId = read.DispatcherBreakId, State = observed.State, DurationMinutes = 30, ScheduledStartOn = observed.ScheduledStartOn };
			(await Breaks().TryStartObservedAsync(read, moved)).Should().BeFalse("the plan read was a 30-minute break");

			(await Breaks().TryStartObservedAsync(read, observed)).Should().BeTrue();
			var started = await Breaks().GetByIdAsync(planned.DispatcherBreakId);
			started.State.Should().Be((int)DispatcherBreakStates.Active);
			started.DurationMinutes.Should().Be(20);
			started.StartedOn.Should().BeCloseTo(Now, TimeSpan.FromMilliseconds(1));
			started.ExpectedReturnOn.Should().BeCloseTo(Now.AddMinutes(20), TimeSpan.FromMilliseconds(1));
			started.Note.Should().Be("New note", "the edit saved after the read stands");
			started.ShiftDayId.Should().Be(101);
			started.BreakType.Should().Be((int)DispatcherBreakTypes.Meal);
			started.ReminderSentOn.Should().BeCloseTo(Now, TimeSpan.FromMilliseconds(1), "alert stamps are untouched");

			(await Breaks().TryStartObservedAsync(read, observed)).Should().BeFalse("already started");
		}

		[Test]
		public async Task Break_start_lock_needs_a_transaction_and_is_taken_inside_one()
		{
			var (connections, configuration, queries) = Parts();
			Func<Task> outside = () => Breaks().LockDepartmentForBreakStartAsync(Dept);
			await outside.Should().ThrowAsync<InvalidOperationException>();

			using var unitOfWork = new Resgrid.Repositories.DataRepository.Transactions.UnitOfWork(connections);
			var repository = new DispatcherBreaksRepository(connections, configuration, unitOfWork, queries);
			await unitOfWork.CreateOrGetConnectionAsync();
			await repository.LockDepartmentForBreakStartAsync(Dept);
			await repository.LockDepartmentForBreakStartAsync(Dept);
			unitOfWork.CommitChanges();
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
