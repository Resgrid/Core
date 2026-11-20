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
	/// Real-database proof for the M0325 intake conversation tables on both engines: one open thread per department, transport and
	/// participant even when texts race (no unique-violation raised), the reply lookup across departments within the window, every state
	/// change conditional (connect/take-over, release, AI turn claim, handler change, close exactly once), activity never moving back,
	/// the idle and ended-call sweeps, and the delivery log (an inbound provider id once per conversation). Set
	/// RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class IntakeConversationRepositoriesDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "intake_conversations_";
		private const int DepartmentId = 55;
		private const string Texter = "+15305550199";
		private static readonly DateTime Base = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

		private DatabaseTypes _previous;
		private bool _configured;
		private string _master, _connection, _database;
		private ServiceProvider _runner;

		private bool IsPostgres => type == DatabaseTypes.Postgres;

		private DbConnection Connect(string connection) => IsPostgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);

		private (IConnectionProvider, SqlConfiguration, IUnitOfWork, IQueryFactory) Parts()
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
			return (connections.Object, configuration, Mock.Of<IUnitOfWork>(), new QueryFactory(list.Object));
		}

		private IntakeConversationsRepository Conversations() { var (c, s, u, q) = Parts(); return new IntakeConversationsRepository(c, s, u, q); }
		private IntakeConversationMessagesRepository Messages() { var (c, s, u, q) = Parts(); return new IntakeConversationMessagesRepository(c, s, u, q); }

		[OneTimeSetUp]
		public async Task Create_isolated_database()
		{
			_master = Environment.GetEnvironmentVariable(IsPostgres ? "RESGRID_ADP_POSTGRES_TEST_CONNECTION" : "RESGRID_ADP_SQLSERVER_TEST_CONNECTION");
			if (string.IsNullOrWhiteSpace(_master)) Assert.Ignore("Set a test connection to run real database checks.");

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
				// M0323 seeds a PlanAddons row and feature flags (earlier migrations' tables); the ended-call sweep joins Calls.
				if (IsPostgres)
				{
					await setup.ExecuteAsync("CREATE EXTENSION IF NOT EXISTS citext;");
					await ((NpgsqlConnection)setup).ReloadTypesAsync();
					await setup.ExecuteAsync("CREATE TABLE planaddons (planaddonid varchar(36) PRIMARY KEY, addontype int NOT NULL, cost numeric NOT NULL, externalid citext, testexternalid citext);" +
						"CREATE TABLE featureflags (featureflagid serial PRIMARY KEY, flagkey citext NOT NULL, name citext, description citext, category citext, isenabledglobally boolean NOT NULL);" +
						"CREATE TABLE featureflagprerequisites (featureflagid int NOT NULL, requiredfeatureflagid int NOT NULL, requiredvalue citext);" +
						"INSERT INTO featureflags (flagkey, name, isenabledglobally) VALUES ('Ai.Enhanced', 'Enhanced AI', false);" +
						"CREATE TABLE calls (callid int PRIMARY KEY, departmentid int NOT NULL, state int NOT NULL, isdeleted boolean NOT NULL);");
				}
				else
				{
					await setup.ExecuteAsync("CREATE TABLE [PlanAddons] ([PlanAddonId] nvarchar(36) PRIMARY KEY, [AddonType] int NOT NULL, [Cost] decimal(18,2) NOT NULL, [ExternalId] nvarchar(200), [TestExternalId] nvarchar(200));" +
						"CREATE TABLE [FeatureFlags] ([FeatureFlagId] int IDENTITY PRIMARY KEY, [FlagKey] nvarchar(200) NOT NULL, [Name] nvarchar(200), [Description] nvarchar(max), [Category] nvarchar(100), [IsEnabledGlobally] bit NOT NULL);" +
						"CREATE TABLE [FeatureFlagPrerequisites] ([FeatureFlagId] int NOT NULL, [RequiredFeatureFlagId] int NOT NULL, [RequiredValue] nvarchar(200));" +
						"INSERT INTO [FeatureFlags] ([FlagKey], [Name], [IsEnabledGlobally]) VALUES ('Ai.Enhanced', 'Enhanced AI', 0);" +
						"CREATE TABLE [Calls] ([CallId] int PRIMARY KEY, [DepartmentId] int NOT NULL, [State] int NOT NULL, [IsDeleted] bit NOT NULL);");
				}
			}

			var source = new Mock<IMigrationSource>();
			source.Setup(s => s.GetMigrations()).Returns(IsPostgres
				? new IMigration[] { new M0323_AddIntakeChannelsAndDedicatedNumbersPg(), new M0325_AddIntakeConversationsPg() }
				: new IMigration[] { new M0323_AddIntakeChannelsAndDedicatedNumbers(), new M0325_AddIntakeConversations() });
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
			foreach (var table in new[] { "IntakeConversationMessages", "IntakeConversations", "Calls" })
				await database.ExecuteAsync("DELETE FROM " + (IsPostgres ? table.ToLowerInvariant() : "[" + table + "]"));
		}

		private static IntakeConversation Thread(int departmentId = DepartmentId, string participant = Texter, DateTime? activity = null, int? callId = 900,
			int handler = (int)IntakeConversationHandlers.Unassisted) => new IntakeConversation
		{
			IntakeConversationId = Guid.NewGuid().ToString("D"), DepartmentId = departmentId, IntakeChannelId = "ch-sms", CallId = callId,
			ChannelType = (int)IntakeChannelTypes.Sms, ParticipantAddress = participant, InboundAddress = "+17755550100", ReplyFromAddress = "+18885550123",
			State = (int)IntakeConversationStates.Open, Handler = handler, StartedOn = activity ?? Base, LastInboundOn = activity ?? Base, LastActivityOn = activity ?? Base
		};

		private static IntakeConversationMessage Message(string conversationId, string providerId, int direction = (int)IntakeConversationMessageDirections.Inbound,
			DateTime? createdOn = null) => new IntakeConversationMessage
		{
			IntakeConversationMessageId = Guid.NewGuid().ToString("D"), IntakeConversationId = conversationId, DepartmentId = DepartmentId, Direction = direction,
			Author = direction == (int)IntakeConversationMessageDirections.Inbound ? (int)IntakeConversationAuthors.Participant : (int)IntakeConversationAuthors.System,
			ProviderMessageId = providerId, Status = direction == (int)IntakeConversationMessageDirections.Inbound ? (int)IntakeConversationMessageStatuses.Received
				: (int)IntakeConversationMessageStatuses.Queued, CreatedOn = createdOn ?? Base
		};

		private async Task AddCall(int callId, int state, bool deleted = false)
		{
			await using var database = Connect(_connection);
			await database.ExecuteAsync(IsPostgres
				? "INSERT INTO calls (callid, departmentid, state, isdeleted) VALUES (@Id, @Department, @State, @Deleted)"
				: "INSERT INTO [Calls] ([CallId], [DepartmentId], [State], [IsDeleted]) VALUES (@Id, @Department, @State, @Deleted)",
				new { Id = callId, Department = DepartmentId, State = state, Deleted = deleted });
		}

		[Test]
		public async Task one_open_thread_per_participant_and_department_even_when_texts_race()
		{
			var inserts = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Conversations().TryInsertOpenAsync(Thread())));

			inserts.Count(i => i).Should().Be(1, "the partial unique index arbitrates without raising");
			var repository = Conversations();
			(await repository.TryInsertOpenAsync(Thread(departmentId: 56))).Should().BeTrue("another department is another thread");

			var open = await repository.GetOpenForParticipantAsync(DepartmentId, (int)IntakeChannelTypes.Sms, Texter);
			open.Should().NotBeNull();
			(await repository.TryCloseAsync(open.IntakeConversationId, DepartmentId, "Idle", false, Base.AddHours(1))).Should().BeTrue();
			(await repository.TryInsertOpenAsync(Thread())).Should().BeTrue("a closed thread does not block a new one");
		}

		[Test]
		public async Task replies_reach_the_most_recently_active_open_thread_within_the_window()
		{
			var repository = Conversations();
			var older = Thread(departmentId: 56, activity: Base);
			var newer = Thread(activity: Base.AddMinutes(30));
			await repository.TryInsertOpenAsync(older);
			await repository.TryInsertOpenAsync(newer);

			(await repository.GetOpenForReplyAsync((int)IntakeChannelTypes.Sms, Texter, Base.AddHours(-24))).IntakeConversationId.Should().Be(newer.IntakeConversationId);
			(await repository.GetOpenForReplyAsync((int)IntakeChannelTypes.Sms, Texter, Base.AddHours(1))).Should().BeNull("outside the reply window");
			(await repository.GetOpenForReplyAsync((int)IntakeChannelTypes.Sms, "+15305550100", Base.AddHours(-24))).Should().BeNull();

			await repository.TryCloseAsync(newer.IntakeConversationId, DepartmentId, "Idle", false, Base.AddHours(1));
			(await repository.GetOpenForReplyAsync((int)IntakeChannelTypes.Sms, Texter, Base.AddHours(-24))).IntakeConversationId.Should().Be(older.IntakeConversationId);
			(await repository.GetLatestForCallAsync(DepartmentId, 900)).IntakeConversationId.Should().Be(newer.IntakeConversationId, "closed threads still belong to their call");
		}

		[Test]
		public async Task connect_take_over_release_and_close_are_conditional()
		{
			var repository = Conversations();
			var thread = Thread();
			await repository.TryInsertOpenAsync(thread);
			var id = thread.IntakeConversationId;

			var connects = await Task.WhenAll(new[] { "disp-1", "disp-2", "disp-3" }.Select(u => Conversations().TryConnectAsync(id, DepartmentId, u, null, Base.AddMinutes(1))));
			connects.Count(c => c).Should().Be(1, "two dispatchers never both take a thread");
			var holder = (await repository.GetByIdAsync(id)).AssignedUserId;

			(await repository.TryConnectAsync(id, DepartmentId, "disp-9", null, Base.AddMinutes(2))).Should().BeFalse();
			(await repository.TryConnectAsync(id, DepartmentId, "disp-9", holder.ToUpperInvariant(), Base.AddMinutes(2))).Should().BeTrue("take-over names the holder");
			(await repository.TryReleaseAsync(id, DepartmentId, holder, (int)IntakeConversationHandlers.Unassisted)).Should().BeFalse("only the holder releases");
			(await repository.TryReleaseAsync(id, DepartmentId, "DISP-9", (int)IntakeConversationHandlers.Assistant)).Should().BeTrue();

			var released = await repository.GetByIdAsync(id);
			released.AssignedUserId.Should().BeNull();
			released.Handler.Should().Be((int)IntakeConversationHandlers.Assistant);

			var claims = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Conversations().TryClaimAssistantTurnAsync(id, DepartmentId, 0)));
			claims.Count(c => c).Should().Be(1);
			(await repository.TrySetHandlerAsync(id, DepartmentId, (int)IntakeConversationHandlers.Assistant, (int)IntakeConversationHandlers.Unassisted, true, 8)).Should().BeTrue();
			(await repository.TrySetHandlerAsync(id, DepartmentId, (int)IntakeConversationHandlers.Assistant, (int)IntakeConversationHandlers.Unassisted, true, 8)).Should().BeFalse();
			(await repository.GetByIdAsync(id)).AssistantTurns.Should().Be(8);

			var closes = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Conversations().TryCloseAsync(id, DepartmentId, "OptedOut", true, Base.AddMinutes(5))));
			closes.Count(c => c).Should().Be(1, "routers hear about a close exactly once");
			var closed = await repository.GetByIdAsync(id);
			closed.IsOpen.Should().BeFalse();
			closed.OptedOut.Should().BeTrue();
			closed.CloseReason.Should().Be("OptedOut");
			(await repository.TryConnectAsync(id, DepartmentId, "disp-1", null, Base.AddMinutes(6))).Should().BeFalse();
			(await repository.TryConnectAsync(id, DepartmentId + 1, "disp-1", null, Base.AddMinutes(6))).Should().BeFalse();
		}

		[Test]
		public async Task activity_never_moves_back_and_the_desk_item_is_kept()
		{
			var repository = Conversations();
			var thread = Thread();
			await repository.TryInsertOpenAsync(thread);

			await repository.TouchAsync(thread.IntakeConversationId, DepartmentId, Base.AddMinutes(10), Base.AddMinutes(10));
			await repository.TouchAsync(thread.IntakeConversationId, DepartmentId, Base.AddMinutes(5), null);
			(await repository.SetDeskItemAsync(thread.IntakeConversationId, DepartmentId, "desk-1")).Should().BeTrue();
			await repository.TryConnectAsync(thread.IntakeConversationId, DepartmentId, "disp-1", null, Base.AddMinutes(11));

			var stored = await repository.GetByIdAsync(thread.IntakeConversationId);
			stored.LastActivityOn.Should().Be(Base.AddMinutes(11));
			stored.LastInboundOn.Should().Be(Base.AddMinutes(10));
			stored.DeskItemId.Should().Be("desk-1");
		}

		[Test]
		public async Task the_sweeps_find_idle_threads_and_threads_whose_call_ended()
		{
			var repository = Conversations();
			var idle = Thread(participant: "+15305550101", activity: Base, callId: 901);
			var active = Thread(participant: "+15305550102", activity: Base.AddHours(5), callId: 902);
			var closedCall = Thread(participant: "+15305550103", activity: Base.AddHours(5), callId: 903);
			var deletedCall = Thread(participant: "+15305550104", activity: Base.AddHours(5), callId: 904);
			var missingCall = Thread(participant: "+15305550105", activity: Base.AddHours(5), callId: 905);
			foreach (var thread in new[] { idle, active, closedCall, deletedCall, missingCall })
				await repository.TryInsertOpenAsync(thread);
			await AddCall(901, (int)CallStates.Pending);
			await AddCall(902, (int)CallStates.Active);
			await AddCall(903, (int)CallStates.Closed);
			await AddCall(904, (int)CallStates.Pending, deleted: true);

			(await repository.GetOpenIdleAsync(Base.AddHours(4), 100)).Select(c => c.IntakeConversationId).Should().Equal(idle.IntakeConversationId);
			(await repository.GetOpenWithEndedCallAsync(new[] { (int)CallStates.Active, (int)CallStates.Pending }, 100)).Select(c => c.IntakeConversationId)
				.Should().BeEquivalentTo(new[] { closedCall.IntakeConversationId, deletedCall.IntakeConversationId, missingCall.IntakeConversationId });
		}

		[Test]
		public async Task an_inbound_provider_id_is_one_message_per_conversation_and_deliveries_are_recorded()
		{
			var conversations = Conversations();
			var thread = Thread();
			await conversations.TryInsertOpenAsync(thread);
			var other = Thread(departmentId: 56);
			await conversations.TryInsertOpenAsync(other);

			var inserts = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Messages().TryInsertInboundAsync(Message(thread.IntakeConversationId, "SM1"))));
			inserts.Count(i => i).Should().Be(1);
			var repository = Messages();
			(await repository.TryInsertInboundAsync(Message(other.IntakeConversationId, "SM1"))).Should().BeTrue("another conversation");
			(await repository.TryInsertInboundAsync(Message(thread.IntakeConversationId, null, createdOn: Base.AddSeconds(1)))).Should().BeTrue();
			(await repository.TryInsertInboundAsync(Message(thread.IntakeConversationId, null, createdOn: Base.AddSeconds(2)))).Should().BeTrue();

			var outbound = Message(thread.IntakeConversationId, null, (int)IntakeConversationMessageDirections.Outbound, Base.AddSeconds(3));
			await repository.InsertMessageAsync(outbound);
			(await repository.UpdateDeliveryAsync(outbound.IntakeConversationMessageId, DepartmentId, (int)IntakeConversationMessageStatuses.Sent, "SMout1", null, Base.AddSeconds(4))).Should().BeTrue();
			(await repository.SetCallNoteAsync(outbound.IntakeConversationMessageId, DepartmentId, 4321)).Should().BeTrue();
			(await repository.UpdateDeliveryAsync(outbound.IntakeConversationMessageId, DepartmentId + 1, (int)IntakeConversationMessageStatuses.Failed, null, "x", null)).Should().BeFalse();

			var rows = await repository.GetForConversationAsync(DepartmentId, thread.IntakeConversationId);
			rows.Should().HaveCount(4);
			rows.Last().IntakeConversationMessageId.Should().Be(outbound.IntakeConversationMessageId);
			rows.Last().Status.Should().Be((int)IntakeConversationMessageStatuses.Sent);
			rows.Last().ProviderMessageId.Should().Be("SMout1");
			rows.Last().CallNoteId.Should().Be(4321);
			rows.Last().SentOn.Should().Be(Base.AddSeconds(4));
		}
	}
}
