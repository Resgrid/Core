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
	/// Real-database proof for the M0323 intake tables on both engines: an intake message is claimed once per channel and provider
	/// id (without the collision ever raising), a failed claim is taken over by one retry only, counts and call lookups read the
	/// rows back, channel addresses match regardless of case and one live channel holds an address, voice sessions are unique per
	/// CallSid, and purges remove only old rows. Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION
	/// (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class IntakeRepositoriesDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "intake_repositories_";
		private static readonly DateTime Base = new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);

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

		private IntakeMessagesRepository Messages() { var (c, s, u, q) = Parts(); return new IntakeMessagesRepository(c, s, u, q); }
		private IntakeChannelsRepository Channels() { var (c, s, u, q) = Parts(); return new IntakeChannelsRepository(c, s, u, q); }
		private VoiceIntakeSessionsRepository Sessions() { var (c, s, u, q) = Parts(); return new VoiceIntakeSessionsRepository(c, s, u, q); }

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
				// M0323 seeds a PlanAddons row and feature flags; those tables belong to earlier migrations.
				if (IsPostgres)
				{
					await setup.ExecuteAsync("CREATE EXTENSION IF NOT EXISTS citext;");
					await ((NpgsqlConnection)setup).ReloadTypesAsync();
					await setup.ExecuteAsync("CREATE TABLE planaddons (planaddonid varchar(36) PRIMARY KEY, addontype int NOT NULL, cost numeric NOT NULL, externalid citext, testexternalid citext);" +
						"CREATE TABLE featureflags (featureflagid serial PRIMARY KEY, flagkey citext NOT NULL, name citext, description citext, category citext, isenabledglobally boolean NOT NULL);" +
						"CREATE TABLE featureflagprerequisites (featureflagid int NOT NULL, requiredfeatureflagid int NOT NULL, requiredvalue citext);" +
						"INSERT INTO featureflags (flagkey, name, isenabledglobally) VALUES ('Ai.Enhanced', 'Enhanced AI', false);" +
						// The Calls columns the abandoned-claim lookup reads (types as M0001 creates them).
						"CREATE TABLE calls (callid int PRIMARY KEY, departmentid int NOT NULL, sourceidentifier citext, callsource integer NOT NULL DEFAULT 0, " +
						"loggedon timestamp without time zone NOT NULL, isdeleted boolean NOT NULL);");
				}
				else
				{
					await setup.ExecuteAsync("CREATE TABLE [PlanAddons] ([PlanAddonId] nvarchar(36) PRIMARY KEY, [AddonType] int NOT NULL, [Cost] decimal(18,2) NOT NULL, [ExternalId] nvarchar(200), [TestExternalId] nvarchar(200));" +
						"CREATE TABLE [FeatureFlags] ([FeatureFlagId] int IDENTITY PRIMARY KEY, [FlagKey] nvarchar(200) NOT NULL, [Name] nvarchar(200), [Description] nvarchar(max), [Category] nvarchar(100), [IsEnabledGlobally] bit NOT NULL);" +
						"CREATE TABLE [FeatureFlagPrerequisites] ([FeatureFlagId] int NOT NULL, [RequiredFeatureFlagId] int NOT NULL, [RequiredValue] nvarchar(200));" +
						"INSERT INTO [FeatureFlags] ([FlagKey], [Name], [IsEnabledGlobally]) VALUES ('Ai.Enhanced', 'Enhanced AI', 0);" +
						"CREATE TABLE [Calls] ([CallId] int PRIMARY KEY, [DepartmentId] int NOT NULL, [SourceIdentifier] nvarchar(max) NULL, [CallSource] int NOT NULL DEFAULT 0, " +
						"[LoggedOn] datetime2 NOT NULL, [IsDeleted] bit NOT NULL);");
				}
			}

			var source = new Mock<IMigrationSource>();
			source.Setup(s => s.GetMigrations()).Returns(IsPostgres
				? new IMigration[] { new M0323_AddIntakeChannelsAndDedicatedNumbersPg(), new M0326_AddIntakeSpamScreeningPg() }
				: new IMigration[] { new M0323_AddIntakeChannelsAndDedicatedNumbers(), new M0326_AddIntakeSpamScreening() });
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
			foreach (var table in new[] { "IntakeMessages", "IntakeChannels", "VoiceIntakeSessions", "Calls", "IntakeBlockedSenders" })
				await database.ExecuteAsync("DELETE FROM " + (IsPostgres ? table.ToLowerInvariant() : "[" + table + "]"));
		}

		private static IntakeMessage Message(string channelId, string providerId, DateTime receivedOn, int state = (int)IntakeMessageStates.Received, int? callId = null,
			int channelType = (int)IntakeChannelTypes.Sms) => new IntakeMessage
		{
			IntakeMessageId = Guid.NewGuid().ToString("D"), DepartmentId = 55, IntakeChannelId = channelId, ChannelType = channelType, ProviderMessageId = providerId,
			Sender = "+15305550199", State = state, CallId = callId, ReceivedOn = receivedOn
		};

		[Test]
		public async Task a_provider_message_is_claimed_once_per_channel_even_concurrently()
		{
			var repository = Messages();

			var claims = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Messages().TryClaimAsync(Message("ch-1", "SM-1", Base))));

			claims.Count(c => c).Should().Be(1);
			(await repository.TryClaimAsync(Message("ch-2", "SM-1", Base))).Should().BeTrue("the same id on another channel is another message");
			(await repository.GetByProviderMessageIdAsync("ch-1", "sm-1")).Should().NotBeNull("provider ids match regardless of case");
		}

		private IntakeBlockedSendersRepository Blocked() { var (c, s, u, q) = Parts(); return new IntakeBlockedSendersRepository(c, s, u, q); }

		private static IntakeBlockedSender Block(string sender, DateTime blockedOn, DateTime? expiresOn, int departmentId = 55) => new IntakeBlockedSender
		{
			IntakeBlockedSenderId = Guid.NewGuid().ToString("D"), DepartmentId = departmentId, Sender = sender, ChannelType = (int)IntakeChannelTypes.Sms,
			Reason = IntakeBlockReasons.Spam, CallId = 900, BlockedByUserId = "dispatcher-1", BlockedOn = blockedOn, ExpiresOn = expiresOn
		};

		[Test]
		public async Task the_per_sender_lookup_reads_only_the_senders_recent_calls_newest_first()
		{
			var repository = Messages();
			var old = Message("ch-1", "SM-old", Base.AddHours(-2), (int)IntakeMessageStates.CallCreated, 800);
			var first = Message("ch-1", "SM-a", Base.AddMinutes(-40), (int)IntakeMessageStates.CallCreated, 801);
			var second = Message("ch-2", "SM-b", Base.AddMinutes(-10), (int)IntakeMessageStates.CallCreated, 802);
			var joined = Message("ch-1", "SM-c", Base.AddMinutes(-5), (int)IntakeMessageStates.AddedToCall, 802);
			var other = Message("ch-1", "SM-d", Base.AddMinutes(-5), (int)IntakeMessageStates.CallCreated, 803);
			other.Sender = "+19165550100";
			var email = Message("ch-3", "EM-1", Base.AddMinutes(-3), (int)IntakeMessageStates.CallCreated, 804, (int)IntakeChannelTypes.Email);
			email.Sender = "Dispatch@Mercy.org";
			foreach (var message in new[] { old, first, second, joined, other, email })
				(await repository.TryClaimAsync(message)).Should().BeTrue();

			var rows = await repository.GetCallsCreatedBySenderSinceAsync(55, "+15305550199", Base.AddHours(-1));
			rows.Select(r => r.CallId).Should().Equal(802, 801);

			(await repository.GetCallsCreatedBySenderSinceAsync(55, "dispatch@mercy.org", Base.AddHours(-1))).Should().ContainSingle("senders match regardless of case");
			(await repository.GetCallsCreatedBySenderSinceAsync(56, "+15305550199", Base.AddHours(-1))).Should().BeEmpty();
		}

		[Test]
		public async Task screenings_replace_only_what_they_are_allowed_to()
		{
			var repository = Messages();
			var created = Message("ch-1", "SM-s", Base, (int)IntakeMessageStates.CallCreated, 900);
			var joined = Message("ch-1", "SM-j", Base, (int)IntakeMessageStates.AddedToConversation, 900);
			(await repository.TryClaimAsync(created)).Should().BeTrue();
			(await repository.TryClaimAsync(joined)).Should().BeTrue();

			(await repository.SetScreeningAsync(55, 900, (int)IntakeScreenings.LikelyNotIncident, new[] { 0, 1 }, null, Base)).Should().BeTrue("unscreened (NULL) reads as None");
			(await repository.SetScreeningAsync(55, 900, (int)IntakeScreenings.LikelyNotIncident, new[] { 0, 1 }, null, Base)).Should().BeFalse("no change");
			(await repository.SetScreeningAsync(55, 900, (int)IntakeScreenings.DismissedAsSpam, new[] { 0, 1, 2 }, "dispatcher-1", Base.AddMinutes(1))).Should().BeTrue();
			(await repository.SetScreeningAsync(55, 900, (int)IntakeScreenings.None, new[] { 1 }, null, Base.AddMinutes(2))).Should().BeFalse("the AI never clears a dismissal");
			(await repository.SetScreeningAsync(56, 900, (int)IntakeScreenings.RepeatSender, new[] { 0, 1, 2, 3 }, null, Base)).Should().BeFalse("another department's call");

			var rows = await repository.GetByCallIdsAsync(55, new[] { 900 });
			rows.Should().ContainSingle("only the row that created the call carries the screening");
			rows[0].Screening.Should().Be((int)IntakeScreenings.DismissedAsSpam);
			rows[0].ScreenedByUserId.Should().Be("dispatcher-1");
			rows[0].ScreenedOn.Should().Be(Base.AddMinutes(1));
		}

		[Test]
		public async Task one_standing_block_per_sender_even_concurrently_and_expiry_and_lifting_apply()
		{
			var repository = Blocked();
			var inserts = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Blocked().TryInsertAsync(Block("+15305550199", Base, Base.AddDays(30)))));
			inserts.Count(i => i).Should().Be(1);
			(await repository.TryInsertAsync(Block("+15305550199", Base, null, departmentId: 56))).Should().BeTrue("another department blocks on its own");

			(await repository.GetInForceAsync(55, "+15305550199", Base.AddDays(29))).Should().NotBeNull();
			(await repository.GetInForceAsync(55, "+15305550199", Base.AddDays(31))).Should().BeNull("expired");
			(await repository.GetCurrentAsync(55, "+15305550199")).Should().NotBeNull("an expired row stands until lifted or purged");

			var current = await repository.GetCurrentAsync(55, "+15305550199");
			current.ExpiresOn = Base.AddDays(60);
			current.BlockedByUserId = "dispatcher-2";
			(await repository.RenewAsync(current)).Should().BeTrue();
			(await repository.GetInForceAsync(55, "+15305550199", Base.AddDays(45))).BlockedByUserId.Should().Be("dispatcher-2");

			(await repository.RemoveAsync(56, current.IntakeBlockedSenderId, "admin-1", Base.AddDays(1))).Should().BeFalse("not that department's block");
			(await repository.RemoveAsync(55, current.IntakeBlockedSenderId, "admin-1", Base.AddDays(1))).Should().BeTrue();
			(await repository.RemoveAsync(55, current.IntakeBlockedSenderId, "admin-1", Base.AddDays(1))).Should().BeFalse("already lifted");
			(await repository.GetInForceAsync(55, "+15305550199", Base.AddDays(2))).Should().BeNull();
			(await repository.GetCurrentForDepartmentAsync(55)).Should().BeEmpty();
			(await repository.TryInsertAsync(Block("+15305550199", Base.AddDays(3), null))).Should().BeTrue("a lifted block makes room for a new one");
		}

		[Test]
		public async Task email_blocks_match_regardless_of_case()
		{
			var repository = Blocked();
			(await repository.TryInsertAsync(Block("dispatch@mercy.org", Base, null))).Should().BeTrue();
			(await repository.TryInsertAsync(Block("Dispatch@Mercy.ORG", Base, null))).Should().BeFalse("the same sender");
			(await repository.GetInForceAsync(55, "DISPATCH@mercy.org", Base)).Should().NotBeNull();
		}

		[Test]
		public async Task the_purge_removes_only_blocks_that_ended_before_the_cutoff()
		{
			var repository = Blocked();
			var lifted = Block("+15305550101", Base.AddDays(-200), null);
			var expired = Block("+15305550102", Base.AddDays(-200), Base.AddDays(-100));
			var standing = Block("+15305550103", Base.AddDays(-200), null);
			var recent = Block("+15305550104", Base.AddDays(-20), Base.AddDays(-10));
			foreach (var block in new[] { lifted, expired, standing, recent })
				(await repository.TryInsertAsync(block)).Should().BeTrue();
			(await repository.RemoveAsync(55, lifted.IntakeBlockedSenderId, "admin-1", Base.AddDays(-150))).Should().BeTrue();

			(await repository.PurgeEndedBeforeAsync(Base.AddDays(-90))).Should().Be(2);

			(await repository.GetCurrentForDepartmentAsync(55)).Select(b => b.Sender).Should().BeEquivalentTo("+15305550103", "+15305550104");
		}

		[Test]
		public async Task rows_without_a_provider_id_always_insert()
		{
			var repository = Messages();

			(await repository.TryClaimAsync(Message("ch-1", null, Base))).Should().BeTrue();
			(await repository.TryClaimAsync(Message("ch-1", null, Base))).Should().BeTrue();
		}

		[Test]
		public async Task a_failed_claim_is_taken_over_by_one_retry_and_completion_is_recorded()
		{
			var repository = Messages();
			var message = Message("ch-1", "SM-2", Base, (int)IntakeMessageStates.Failed);
			(await repository.TryClaimAsync(message)).Should().BeTrue();

			var takers = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
				Messages().TryReclaimAsync(message.IntakeMessageId, (int)IntakeMessageStates.Failed, Base.AddMinutes(1), Base.AddMinutes(-9))));
			takers.Count(t => t).Should().Be(1);

			(await repository.CompleteAsync(message.IntakeMessageId, (int)IntakeMessageStates.CallCreated, 900, "ok", Base.AddSeconds(2))).Should().BeTrue();
			var stored = await repository.GetByProviderMessageIdAsync("ch-1", "SM-2");
			stored.State.Should().Be((int)IntakeMessageStates.CallCreated);
			stored.CallId.Should().Be(900);
			(await repository.TryReclaimAsync(message.IntakeMessageId, (int)IntakeMessageStates.CallCreated, Base.AddMinutes(2), Base.AddMinutes(-8)))
				.Should().BeFalse("a message with a call is never taken over");
		}

		[Test]
		public async Task an_abandoned_claim_is_taken_over_by_one_retry_and_the_new_owner_gets_a_fresh_window()
		{
			var repository = Messages();
			var message = Message("ch-1", "SM-3", Base);
			(await repository.TryClaimAsync(message)).Should().BeTrue();

			var now = Base.AddMinutes(15);
			(await repository.TryReclaimAsync(message.IntakeMessageId, (int)IntakeMessageStates.Received, Base.AddMinutes(5), Base.AddMinutes(-5)))
				.Should().BeFalse("the claim is not old enough yet");

			// Two retries read the same stale row at once: the state stays Received, so only the restamped ReceivedOn stops the second.
			var takers = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
				Messages().TryReclaimAsync(message.IntakeMessageId, (int)IntakeMessageStates.Received, now, now.AddMinutes(-10))));
			takers.Count(t => t).Should().Be(1);

			var stored = await repository.GetByProviderMessageIdAsync("ch-1", "SM-3");
			stored.State.Should().Be((int)IntakeMessageStates.Received);
			stored.ReceivedOn.Should().BeCloseTo(now, TimeSpan.FromSeconds(1));
			(await repository.TryReclaimAsync(message.IntakeMessageId, (int)IntakeMessageStates.Received, now.AddMinutes(1), now.AddMinutes(-9)))
				.Should().BeFalse("the new owner's claim is fresh again");
		}

		[Test]
		public async Task the_call_an_abandoned_claim_saved_is_found_by_its_provider_id_unless_a_message_names_it()
		{
			await using (var database = Connect(_connection))
			{
				await database.ExecuteAsync(IsPostgres
					? "INSERT INTO calls (callid, departmentid, sourceidentifier, callsource, loggedon, isdeleted) VALUES " +
					  "(700, 55, 'SM-4', 6, @Logged, false), (701, 55, 'SM-4', 6, @Old, false), (702, 56, 'SM-4', 6, @Logged, false), " +
					  "(703, 55, 'SM-4', 1, @Logged, false), (704, 55, 'SM-4', 6, @Logged, true), (705, 55, 'SM-5', 7, @Logged, false), (706, 55, 'SM-5', 7, @Logged, false)"
					: "INSERT INTO [Calls] ([CallId], [DepartmentId], [SourceIdentifier], [CallSource], [LoggedOn], [IsDeleted]) VALUES " +
					  "(700, 55, 'SM-4', 6, @Logged, 0), (701, 55, 'SM-4', 6, @Old, 0), (702, 56, 'SM-4', 6, @Logged, 0), " +
					  "(703, 55, 'SM-4', 1, @Logged, 0), (704, 55, 'SM-4', 6, @Logged, 1), (705, 55, 'SM-5', 7, @Logged, 0), (706, 55, 'SM-5', 7, @Logged, 0)",
					new { Logged = Base.AddMinutes(1), Old = Base.AddDays(-2) });
			}

			var repository = Messages();
			var sources = new[] { (int)CallSources.Intake, (int)CallSources.VoiceIntake };
			(await repository.GetUnclaimedIntakeCallIdAsync(55, "sm-4", sources, Base)).Should().Be(700,
				"same department, intake source, logged after the claim, not deleted (and provider ids match regardless of case)");

			// 706 already belongs to another message (it made it); 705 is the orphan.
			await repository.TryClaimAsync(Message("ch-2", "other", Base, (int)IntakeMessageStates.CallCreated, 706));
			(await repository.GetUnclaimedIntakeCallIdAsync(55, "SM-5", sources, Base)).Should().Be(705);
			(await repository.GetUnclaimedIntakeCallIdAsync(55, "SM-9", sources, Base)).Should().BeNull();
		}

		[Test]
		public async Task counts_and_call_lookups()
		{
			var repository = Messages();
			await repository.TryClaimAsync(Message("ch-1", "a", Base, (int)IntakeMessageStates.CallCreated, 900));
			await repository.TryClaimAsync(Message("ch-1", "b", Base.AddDays(1), (int)IntakeMessageStates.SenderNotAllowed));
			await repository.TryClaimAsync(Message("ch-2", "c", Base, (int)IntakeMessageStates.CallCreated, 901, (int)IntakeChannelTypes.Email));
			await repository.TryClaimAsync(Message("ch-1", "d", Base.AddMonths(-1), (int)IntakeMessageStates.CallCreated, 800));

			var from = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
			var before = from.AddMonths(1);
			(await repository.CountAsync(55, (int)IntakeChannelTypes.Sms, from, before, new[] { (int)IntakeMessageStates.Duplicate })).Should().Be(2);
			(await repository.CountAsync(55, (int)IntakeChannelTypes.Sms, from, before, new[] { (int)IntakeMessageStates.SenderNotAllowed })).Should().Be(1);
			(await repository.CountAsync(55, (int)IntakeChannelTypes.Sms, from, before, Array.Empty<int>())).Should().Be(2);
			var byType = await repository.CountByTypeAsync(55, from, before, Array.Empty<int>());
			byType[(int)IntakeChannelTypes.Sms].Should().Be(2);
			byType[(int)IntakeChannelTypes.Email].Should().Be(1);
			(await repository.CountByChannelAsync(55, from, before, Array.Empty<int>()))["ch-1"].Should().Be(2);

			var calls = await repository.GetByCallIdsAsync(55, new[] { 900, 901, 902 });
			calls.Select(c => c.CallId).Should().BeEquivalentTo(new int?[] { 900, 901 });
			(await repository.GetByCallIdsAsync(99, new[] { 900 })).Should().BeEmpty();

			(await repository.PurgeReceivedBeforeAsync(from)).Should().Be(1);
		}

		[Test]
		public async Task one_live_channel_per_address_matched_regardless_of_case()
		{
			var repository = Channels();
			var channel = new IntakeChannel
			{
				DepartmentId = 55, Name = "Intake", ChannelType = (int)IntakeChannelTypes.Email, Address = "hospital", ProcessingMode = 1, IsEnabled = true, CreatedOn = Base
			};
			await repository.SaveOrUpdateAsync(channel, CancellationToken.None);

			(await repository.GetLiveByAddressAsync((int)IntakeChannelTypes.Email, "HOSPITAL"))?.IntakeChannelId.Should().Be(channel.IntakeChannelId);
			(await repository.GetLiveByAddressAsync((int)IntakeChannelTypes.Slack, "hospital")).Should().BeNull();
			(await repository.GetLiveForDepartmentAsync(55)).Should().ContainSingle();

			Func<Task> twin = () => Channels().SaveOrUpdateAsync(new IntakeChannel
			{
				DepartmentId = 99, Name = "Twin", ChannelType = (int)IntakeChannelTypes.Email, Address = "hospital", ProcessingMode = 1, IsEnabled = true, CreatedOn = Base
			}, CancellationToken.None);
			await twin.Should().ThrowAsync<Exception>("UX_IntakeChannels_Address holds one live channel per address");

			channel.IsDeleted = true;
			await repository.SaveOrUpdateAsync(channel, CancellationToken.None);
			(await repository.GetLiveByAddressAsync((int)IntakeChannelTypes.Email, "hospital")).Should().BeNull();
			(await repository.GetLiveForDepartmentAsync(55)).Should().BeEmpty();
		}

		[Test]
		public async Task voice_sessions_are_unique_per_call_sid_and_purged_by_age()
		{
			var repository = Sessions();
			VoiceIntakeSession Session(string sid, DateTime started) => new VoiceIntakeSession
			{
				VoiceIntakeSessionId = Guid.NewGuid().ToString("D"), DepartmentId = 55, ProviderCallId = sid, FromNumber = "+15305550199", ToNumber = "+17755550100",
				Step = (int)VoiceIntakeSteps.Nature, AnswersJson = "{}", StartedOn = started
			};

			var first = Session("CA1", Base);
			(await repository.TryInsertAsync(first)).Should().BeTrue();
			(await repository.TryInsertAsync(Session("CA1", Base))).Should().BeFalse();

			// An overlapping callback's snapshot (still at Nature, retries 0) moved it on first: a write from a stale read applies nothing.
			var newer = await repository.GetByProviderCallIdAsync("CA1");
			newer.Step = (int)VoiceIntakeSteps.Location;
			newer.AnswersJson = "{\"Nature\":\"Fire\"}";
			(await repository.UpdateProgressAsync(newer, (int)VoiceIntakeSteps.Nature, 0)).Should().BeTrue();

			first.Retries = 1;
			(await repository.UpdateProgressAsync(first, (int)VoiceIntakeSteps.Nature, 0)).Should().BeFalse("the stored session is no longer at the step this callback read");
			(await repository.GetByProviderCallIdAsync("CA1")).Step.Should().Be((int)VoiceIntakeSteps.Location);

			first.Step = (int)VoiceIntakeSteps.Completed;
			first.Retries = 0;
			first.CallId = 900;
			first.AnswersJson = null;
			first.CompletedOn = Base.AddMinutes(2);
			(await repository.UpdateProgressAsync(first, (int)VoiceIntakeSteps.Location, 0)).Should().BeTrue();
			var stored = await repository.GetByProviderCallIdAsync("ca1");
			stored.Step.Should().Be((int)VoiceIntakeSteps.Completed);
			stored.CallId.Should().Be(900);
			stored.AnswersJson.Should().BeNull();

			// A completed session is never rewritten, even by a write that read the same step.
			first.Step = (int)VoiceIntakeSteps.Location;
			first.CallId = null;
			first.CompletedOn = null;
			(await repository.UpdateProgressAsync(first, (int)VoiceIntakeSteps.Completed, 0)).Should().BeFalse();
			(await repository.GetByProviderCallIdAsync("CA1")).CallId.Should().Be(900);

			await repository.TryInsertAsync(Session("CA2", Base.AddDays(-3)));
			(await repository.CountStartedAsync(55, Base.AddHours(-1), Base.AddHours(1))).Should().Be(1);
			(await repository.PurgeStartedBeforeAsync(Base.AddDays(-2))).Should().Be(1);
			(await repository.GetByProviderCallIdAsync("CA2")).Should().BeNull();
		}
	}
}
