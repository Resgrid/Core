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
using Resgrid.Services.AiDestination;

namespace Resgrid.Tests.Repositories
{
	/// <summary>
	/// Real-database proof for the M0323 Enhanced AI destination tables on both engines. Reviews: concurrent triggers for one call fold
	/// into its one waiting row (UX_AiDestinationReviews_Open), a waiting row keeps its due time while a claimed one moves it, the due scan
	/// (TOP / LIMIT) takes rows oldest first once debounced, a claim holds its lease and is taken over only after it lapses, completion and
	/// release act only under their own claim, and processed rows are purged by age. Advice: the call's newest Open advice, superseding
	/// leaves one open, a state transition is won once (and rolled back), every read and write is department-scoped. Set
	/// RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class AiDestinationRepositoriesDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "ai_destination_repositories_";
		private const int DepartmentId = 55, OtherDepartmentId = 99;
		private static readonly DateTime Base = new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);
		private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

		private DatabaseTypes _previous;
		private bool _configured;
		private string _master, _connection, _database;
		private ServiceProvider _runner;

		private bool IsPostgres => type == DatabaseTypes.Postgres;

		private DbConnection Connect(string connection) => IsPostgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);

		private string Table(string name) => IsPostgres ? name.ToLowerInvariant() : "[" + name + "]";

		private string Column(string name) => IsPostgres ? name.ToLowerInvariant() : "[" + name + "]";

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

		private AiDestinationReviewsRepository Reviews() { var (c, s, u, q) = Parts(); return new AiDestinationReviewsRepository(c, s, u, q); }
		private AiDestinationAdvicesRepository Advices() { var (c, s, u, q) = Parts(); return new AiDestinationAdvicesRepository(c, s, u, q); }

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
						"INSERT INTO featureflags (flagkey, name, isenabledglobally) VALUES ('Ai.Enhanced', 'Enhanced AI', false);");
				}
				else
				{
					await setup.ExecuteAsync("CREATE TABLE [PlanAddons] ([PlanAddonId] nvarchar(36) PRIMARY KEY, [AddonType] int NOT NULL, [Cost] decimal(18,2) NOT NULL, [ExternalId] nvarchar(200), [TestExternalId] nvarchar(200));" +
						"CREATE TABLE [FeatureFlags] ([FeatureFlagId] int IDENTITY PRIMARY KEY, [FlagKey] nvarchar(200) NOT NULL, [Name] nvarchar(200), [Description] nvarchar(max), [Category] nvarchar(100), [IsEnabledGlobally] bit NOT NULL);" +
						"CREATE TABLE [FeatureFlagPrerequisites] ([FeatureFlagId] int NOT NULL, [RequiredFeatureFlagId] int NOT NULL, [RequiredValue] nvarchar(200));" +
						"INSERT INTO [FeatureFlags] ([FlagKey], [Name], [IsEnabledGlobally]) VALUES ('Ai.Enhanced', 'Enhanced AI', 0);");
				}
			}

			var source = new Mock<IMigrationSource>();
			source.Setup(s => s.GetMigrations()).Returns(IsPostgres
				? new IMigration[] { new M0323_AddIntakeChannelsAndDedicatedNumbersPg() }
				: new IMigration[] { new M0323_AddIntakeChannelsAndDedicatedNumbers() });
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
			foreach (var table in new[] { "AiDestinationReviews", "AiDestinationAdvices" })
				await database.ExecuteAsync("DELETE FROM " + Table(table));
		}

		/// <summary>Every row for the call, read straight from the table (zone-less values, as stored).</summary>
		private async Task<AiDestinationReview[]> RowsForCallAsync(int callId)
		{
			await using var database = Connect(_connection);
			return (await database.QueryAsync<AiDestinationReview>(
				"SELECT * FROM " + Table("AiDestinationReviews") + " WHERE " + Column("CallId") + " = @CallId", new { CallId = callId })).ToArray();
		}

		private async Task<AiDestinationReview> WaitingRowAsync(int callId) => (await RowsForCallAsync(callId)).Single(r => r.ProcessedOn == null);

		private static DateTime Claim(DateTime utc) => AiDestinationAdvisorService.ClaimStamp(utc);

		/// <summary>A review queued, claimed and completed as worker 81 does, processed at <paramref name="processedOn"/>.</summary>
		private async Task<AiDestinationReview> ProcessedAsync(int callId, DateTime processedOn)
		{
			var reviews = Reviews();
			await reviews.QueueAsync(DepartmentId, callId, AiDestinationReviewTriggers.CallCreated, processedOn.AddMinutes(-2));
			var row = await WaitingRowAsync(callId);
			var claimedOn = Claim(processedOn.AddMinutes(-1));
			(await reviews.TryClaimAsync(row.AiDestinationReviewId, claimedOn, claimedOn - Lease)).Should().BeTrue();
			(await reviews.CompleteAsync(row.AiDestinationReviewId, claimedOn, AiDestinationOutcomes.Advised, processedOn)).Should().BeTrue();
			return row;
		}

		[Test]
		public async Task Concurrent_triggers_for_one_call_fold_into_its_one_waiting_review()
		{
			var triggers = new[]
			{
				AiDestinationReviewTriggers.CallCreated, AiDestinationReviewTriggers.CallUpdated, AiDestinationReviewTriggers.NoteAdded, AiDestinationReviewTriggers.NoteAdded,
				AiDestinationReviewTriggers.CallUpdated, AiDestinationReviewTriggers.AvailabilityChanged, AiDestinationReviewTriggers.NoteAdded, AiDestinationReviewTriggers.CallUpdated
			};

			await Task.WhenAll(triggers.Select(t => Reviews().QueueAsync(DepartmentId, 900, t, Base)));

			var rows = await RowsForCallAsync(900);
			rows.Should().ContainSingle("UX_AiDestinationReviews_Open holds one waiting review per call; the losers fold into the winner's row");
			rows[0].DepartmentId.Should().Be(DepartmentId);
			rows[0].RequestedOn.Should().Be(Base);
			rows[0].ClaimedOn.Should().BeNull();

			// A later trigger while the row waits: the latest trigger is kept, the due time is not (a stream of notes cannot postpone it).
			await Reviews().QueueAsync(DepartmentId, 900, AiDestinationReviewTriggers.OnDemand, Base.AddSeconds(30));
			var waiting = await WaitingRowAsync(900);
			(await RowsForCallAsync(900)).Should().ContainSingle();
			waiting.ReviewTrigger.Should().Be((int)AiDestinationReviewTriggers.OnDemand);
			waiting.RequestedOn.Should().Be(Base, "a waiting row keeps its due time");

			// Another call has its own row; another department cannot fold into this one, nor add a second waiting row for the call.
			await Reviews().QueueAsync(DepartmentId, 901, AiDestinationReviewTriggers.CallCreated, Base);
			(await RowsForCallAsync(901)).Should().ContainSingle();
			await Reviews().QueueAsync(OtherDepartmentId, 900, AiDestinationReviewTriggers.NoteAdded, Base.AddMinutes(1));
			waiting = (await RowsForCallAsync(900)).Single();
			waiting.DepartmentId.Should().Be(DepartmentId);
			waiting.ReviewTrigger.Should().Be((int)AiDestinationReviewTriggers.OnDemand, "another department never changes the row");

			// Ids that cannot be a call are ignored.
			await Reviews().QueueAsync(0, 902, AiDestinationReviewTriggers.CallCreated, Base);
			await Reviews().QueueAsync(DepartmentId, 0, AiDestinationReviewTriggers.CallCreated, Base);
			(await RowsForCallAsync(902)).Should().BeEmpty();
			(await RowsForCallAsync(0)).Should().BeEmpty();
		}

		[Test]
		public async Task The_due_scan_takes_debounced_waiting_rows_oldest_first()
		{
			var reviews = Reviews();
			await reviews.QueueAsync(DepartmentId, 902, AiDestinationReviewTriggers.CallCreated, Base.AddSeconds(20));
			await reviews.QueueAsync(DepartmentId, 900, AiDestinationReviewTriggers.CallCreated, Base);
			await reviews.QueueAsync(OtherDepartmentId, 901, AiDestinationReviewTriggers.NoteAdded, Base.AddSeconds(10));

			// The worker asks for rows requested at or before now - debounce.
			var due = await reviews.GetDueAsync(Base.AddSeconds(10), Base.AddSeconds(10) - Lease, 10);
			due.Select(r => r.CallId).Should().Equal(new[] { 900, 901 }, "oldest first, across departments, the 20-second row still debouncing");
			due[0].RequestedOn.Should().Be(Base);
			due[0].RequestedOn.Kind.Should().Be(DateTimeKind.Utc);
			due[1].DepartmentId.Should().Be(OtherDepartmentId);
			due[1].ReviewTrigger.Should().Be((int)AiDestinationReviewTriggers.NoteAdded);

			(await reviews.GetDueAsync(Base.AddMinutes(1), Base, 1)).Select(r => r.CallId).Should().Equal(new[] { 900 }, "TOP / LIMIT takes the oldest");
			(await reviews.GetDueAsync(Base.AddMinutes(1), Base, 2)).Select(r => r.CallId).Should().Equal(new[] { 900, 901 });
			(await reviews.GetDueAsync(Base.AddSeconds(-1), Base, 10)).Should().BeEmpty();
		}

		[Test]
		public async Task A_claim_holds_its_lease_and_is_taken_over_only_after_it_lapses()
		{
			var reviews = Reviews();
			await reviews.QueueAsync(DepartmentId, 900, AiDestinationReviewTriggers.CallCreated, Base);
			var id = (await WaitingRowAsync(900)).AiDestinationReviewId;

			var first = Claim(Base.AddMinutes(1).AddTicks(1_234_567));
			(await reviews.TryClaimAsync(id, first, first - Lease)).Should().BeTrue();
			(await WaitingRowAsync(900)).ClaimedOn.Should().Be(first, "the millisecond claim stamp reads back exactly");

			var second = Claim(Base.AddMinutes(2));
			var takers = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Reviews().TryClaimAsync(id, second, second - Lease)));
			takers.Should().OnlyContain(t => !t, "the first worker's lease is running");
			(await reviews.GetDueAsync(second, second - Lease, 10)).Should().BeEmpty("a claimed row inside its lease is not due");

			// The first worker stopped; once its lease lapsed one worker takes the row over.
			var third = Claim(Base.AddMinutes(7));
			(await reviews.GetDueAsync(third, third - Lease, 10)).Select(r => r.AiDestinationReviewId).Should().Equal(new[] { id });
			takers = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Reviews().TryClaimAsync(id, third, third - Lease)));
			takers.Count(t => t).Should().Be(1, "the lapsed row is taken over once");

			// The stopped worker finishing late neither completes the row nor hands back the new claim.
			(await reviews.CompleteAsync(id, first, AiDestinationOutcomes.Advised, Base.AddMinutes(8))).Should().BeFalse();
			var row = await WaitingRowAsync(900);
			row.ClaimedOn.Should().Be(third);
			row.ProcessedOn.Should().BeNull();

			(await reviews.CompleteAsync(id, third, AiDestinationOutcomes.Advised, Base.AddMinutes(8))).Should().BeTrue();
			row = (await RowsForCallAsync(900)).Single();
			row.ProcessedOn.Should().Be(Base.AddMinutes(8));
			row.Outcome.Should().Be(AiDestinationOutcomes.Advised);

			(await reviews.TryClaimAsync(id, Claim(Base.AddHours(1)), Base.AddHours(1))).Should().BeFalse("a processed row is never claimed again");
			(await reviews.GetDueAsync(Base.AddHours(1), Base.AddHours(1), 10)).Should().BeEmpty();

			// The open-row index covers waiting rows only: the next trigger starts a new review next to the processed one.
			await reviews.QueueAsync(DepartmentId, 900, AiDestinationReviewTriggers.NoteAdded, Base.AddMinutes(9));
			var all = await RowsForCallAsync(900);
			all.Should().HaveCount(2);
			all.Single(r => r.ProcessedOn == null).RequestedOn.Should().Be(Base.AddMinutes(9));
		}

		[Test]
		public async Task A_trigger_during_processing_moves_the_due_time_and_completion_hands_the_row_back()
		{
			var reviews = Reviews();
			await reviews.QueueAsync(DepartmentId, 900, AiDestinationReviewTriggers.CallCreated, Base);
			var id = (await WaitingRowAsync(900)).AiDestinationReviewId;
			var claimedOn = Claim(Base.AddMinutes(1));
			(await reviews.TryClaimAsync(id, claimedOn, claimedOn - Lease)).Should().BeTrue();

			// A note arrives while the model is thinking.
			await reviews.QueueAsync(DepartmentId, 900, AiDestinationReviewTriggers.NoteAdded, Base.AddSeconds(90));
			var row = await WaitingRowAsync(900);
			row.RequestedOn.Should().Be(Base.AddSeconds(90), "a claimed row's due time moves to the newer request");
			row.ReviewTrigger.Should().Be((int)AiDestinationReviewTriggers.NoteAdded);
			row.ClaimedOn.Should().Be(claimedOn, "the pass in flight keeps its claim");

			(await reviews.CompleteAsync(id, claimedOn, AiDestinationOutcomes.Advised, Base.AddMinutes(2))).Should().BeFalse("a newer request arrived since the claim");
			row = (await RowsForCallAsync(900)).Single();
			row.ProcessedOn.Should().BeNull();
			row.Outcome.Should().BeNull();
			row.ClaimedOn.Should().BeNull("the row was released for another run");

			// Due again once the newer request's debounce passed, and claimable at once (no lease to wait out).
			(await reviews.GetDueAsync(Base.AddSeconds(89), Base, 10)).Should().BeEmpty();
			(await reviews.GetDueAsync(Base.AddSeconds(90), Base, 10)).Select(r => r.AiDestinationReviewId).Should().Equal(new[] { id });
			var again = Claim(Base.AddMinutes(3));
			(await reviews.TryClaimAsync(id, again, again - Lease)).Should().BeTrue();
			(await reviews.CompleteAsync(id, again, AiDestinationOutcomes.Unchanged, Base.AddMinutes(4))).Should().BeTrue();
		}

		[Test]
		public async Task A_claimed_row_is_given_back_only_under_its_own_claim()
		{
			var reviews = Reviews();
			await reviews.QueueAsync(DepartmentId, 900, AiDestinationReviewTriggers.CallCreated, Base);
			var id = (await WaitingRowAsync(900)).AiDestinationReviewId;
			var claimedOn = Claim(Base.AddMinutes(1));
			(await reviews.TryClaimAsync(id, claimedOn, claimedOn - Lease)).Should().BeTrue();

			await reviews.ReleaseAsync(id, claimedOn.AddMilliseconds(1));
			(await WaitingRowAsync(900)).ClaimedOn.Should().Be(claimedOn, "not that claim");

			await reviews.ReleaseAsync(id, claimedOn);
			var row = await WaitingRowAsync(900);
			row.ClaimedOn.Should().BeNull();
			row.RequestedOn.Should().Be(Base, "a busy pass gives the row back as it was");

			var next = Claim(Base.AddMinutes(1).AddSeconds(5));
			(await reviews.TryClaimAsync(id, next, next - Lease)).Should().BeTrue("a released row is claimable inside the old lease");
			(await reviews.TryClaimAsync(null, next, next - Lease)).Should().BeFalse();
			(await reviews.TryClaimAsync(Guid.NewGuid().ToString("D"), next, next)).Should().BeFalse();
		}

		[Test]
		public async Task Only_processed_reviews_older_than_the_cutoff_are_purged()
		{
			var old = await ProcessedAsync(900, Base.AddDays(-40));
			var recent = await ProcessedAsync(901, Base.AddDays(-10));
			await Reviews().QueueAsync(DepartmentId, 902, AiDestinationReviewTriggers.CallCreated, Base.AddDays(-60));

			(await Reviews().DeleteProcessedBeforeAsync(Base.AddDays(-30))).Should().Be(1);

			(await RowsForCallAsync(900)).Should().BeEmpty();
			(await RowsForCallAsync(901)).Single().AiDestinationReviewId.Should().Be(recent.AiDestinationReviewId);
			(await RowsForCallAsync(902)).Should().ContainSingle("a waiting row is never purged, however old");
			old.AiDestinationReviewId.Should().NotBe(recent.AiDestinationReviewId);
			(await Reviews().DeleteProcessedBeforeAsync(Base.AddDays(-30))).Should().Be(0);
		}

		private static AiDestinationAdvice Advice(int callId, DateTime createdOn, int departmentId = DepartmentId) => new AiDestinationAdvice
		{
			AiDestinationAdviceId = Guid.NewGuid().ToString("D"),
			DepartmentId = departmentId,
			CallId = callId,
			CurrentPoiId = 10,
			RecommendedPoiId = 20,
			RequiredCapabilityIds = "3,7",
			Rationale = "Stroke symptoms in the note: the comprehensive stroke centre.",
			Confidence = 0.8125,
			IsChange = true,
			ReviewTrigger = (int)AiDestinationReviewTriggers.NoteAdded,
			State = (int)AiDestinationAdviceStates.Open,
			InputDigest = new string('a', 64),
			PromptVersion = "destination-v1",
			ModelRevision = "synthetic-revision",
			CreatedOn = createdOn
		};

		[Test]
		public async Task The_newest_open_advice_is_read_per_call_and_superseding_leaves_it_the_only_open_one()
		{
			var advices = Advices();
			var older = Advice(900, Base);
			var newer = Advice(900, Base.AddMinutes(1));
			newer.RecommendedPoiId = null;
			newer.IsChange = false;
			newer.RequiredCapabilityIds = null;
			var otherCall = Advice(901, Base);
			foreach (var advice in new[] { older, newer, otherCall })
				await advices.InsertAsync(advice, CancellationToken.None);

			(await advices.GetOpenForCallAsync(DepartmentId, 900)).AiDestinationAdviceId.Should().Be(newer.AiDestinationAdviceId, "newest first (TOP 1 / LIMIT 1)");

			// The service inserts the new advice, then supersedes every other Open row of the call.
			(await advices.SupersedeOpenAsync(DepartmentId, 900, newer.AiDestinationAdviceId, Base.AddMinutes(1))).Should().Be(1);
			(await advices.SupersedeOpenAsync(DepartmentId, 900, newer.AiDestinationAdviceId, Base.AddMinutes(2))).Should().Be(0, "already superseded");

			var superseded = await advices.GetForDepartmentAsync(DepartmentId, older.AiDestinationAdviceId);
			superseded.State.Should().Be((int)AiDestinationAdviceStates.Superseded);
			superseded.ResolvedOn.Should().Be(Base.AddMinutes(1));
			superseded.ResolvedOn.Value.Kind.Should().Be(DateTimeKind.Utc);
			superseded.ResolvedByUserId.Should().BeNull();

			var stored = await advices.GetOpenForCallAsync(DepartmentId, 900);
			stored.AiDestinationAdviceId.Should().Be(newer.AiDestinationAdviceId);
			stored.CreatedOn.Should().Be(Base.AddMinutes(1));
			stored.CreatedOn.Kind.Should().Be(DateTimeKind.Utc);
			stored.RecommendedPoiId.Should().BeNull();
			stored.CurrentPoiId.Should().Be(10);
			stored.IsChange.Should().BeFalse();
			stored.RequiredCapabilityIds.Should().BeNull();
			stored.Confidence.Should().Be(0.8125);
			stored.Rationale.Should().Be(newer.Rationale);
			stored.InputDigest.Should().Be(newer.InputDigest);
			stored.PromptVersion.Should().Be("destination-v1");
			stored.ReviewTrigger.Should().Be((int)AiDestinationReviewTriggers.NoteAdded);
			(await advices.GetLatestForCallAsync(DepartmentId, 900)).AiDestinationAdviceId.Should().Be(newer.AiDestinationAdviceId);

			(await advices.GetOpenForCallAsync(DepartmentId, 901)).AiDestinationAdviceId.Should().Be(otherCall.AiDestinationAdviceId, "another call is untouched");
			var full = await advices.GetForDepartmentAsync(DepartmentId, otherCall.AiDestinationAdviceId);
			full.RecommendedPoiId.Should().Be(20);
			full.RequiredCapabilityIds.Should().Be("3,7");
			full.IsChange.Should().BeTrue();

			// No exception id: every Open row of the call is superseded.
			(await advices.SupersedeOpenAsync(DepartmentId, 901, null, Base.AddMinutes(3))).Should().Be(1);
			(await advices.GetOpenForCallAsync(DepartmentId, 901)).Should().BeNull();
			(await advices.GetLatestForCallAsync(DepartmentId, 901)).State.Should().Be((int)AiDestinationAdviceStates.Superseded, "the latest advice in any state");
			(await advices.GetLatestForCallAsync(DepartmentId, 902)).Should().BeNull();
		}

		[Test]
		public async Task Two_publishers_racing_on_one_call_leave_the_newest_advice_open_whatever_the_order()
		{
			var advices = Advices();

			// Both inserted before either supersedes: the older one's supersede must not close the newer one, and vice versa.
			var first = Advice(910, Base);
			var second = Advice(910, Base.AddSeconds(2));
			await advices.InsertAsync(first, CancellationToken.None);
			await advices.InsertAsync(second, CancellationToken.None);
			(await advices.SupersedeOpenAsync(DepartmentId, 910, second.AiDestinationAdviceId, Base.AddSeconds(3))).Should().Be(1, "the second supersedes the first");
			(await advices.SupersedeOpenAsync(DepartmentId, 910, first.AiDestinationAdviceId, Base.AddSeconds(3))).Should().Be(0, "the first must not supersede the newer second");
			(await advices.GetOpenForCallAsync(DepartmentId, 910)).AiDestinationAdviceId.Should().Be(second.AiDestinationAdviceId);

			// The other order ends the same way.
			var third = Advice(911, Base);
			var fourth = Advice(911, Base.AddSeconds(2));
			await advices.InsertAsync(third, CancellationToken.None);
			await advices.InsertAsync(fourth, CancellationToken.None);
			(await advices.SupersedeOpenAsync(DepartmentId, 911, third.AiDestinationAdviceId, Base.AddSeconds(3))).Should().Be(0);
			(await advices.SupersedeOpenAsync(DepartmentId, 911, fourth.AiDestinationAdviceId, Base.AddSeconds(3))).Should().Be(1);
			(await advices.GetOpenForCallAsync(DepartmentId, 911)).AiDestinationAdviceId.Should().Be(fourth.AiDestinationAdviceId);

			// The same instant: the id decides, so exactly one stays open.
			var tieA = Advice(912, Base);
			var tieB = Advice(912, Base);
			await advices.InsertAsync(tieA, CancellationToken.None);
			await advices.InsertAsync(tieB, CancellationToken.None);
			var changed = await advices.SupersedeOpenAsync(DepartmentId, 912, tieA.AiDestinationAdviceId, Base.AddSeconds(1))
				+ await advices.SupersedeOpenAsync(DepartmentId, 912, tieB.AiDestinationAdviceId, Base.AddSeconds(1));
			changed.Should().Be(1);
			(await advices.GetOpenForCallAsync(DepartmentId, 912)).Should().NotBeNull();
		}

		[Test]
		public async Task Every_advice_read_and_write_starts_at_the_department()
		{
			var advices = Advices();
			var advice = Advice(900, Base);
			await advices.InsertAsync(advice, CancellationToken.None);

			(await advices.GetForDepartmentAsync(OtherDepartmentId, advice.AiDestinationAdviceId)).Should().BeNull();
			(await advices.GetOpenForCallAsync(OtherDepartmentId, 900)).Should().BeNull();
			(await advices.GetLatestForCallAsync(OtherDepartmentId, 900)).Should().BeNull();
			(await advices.SupersedeOpenAsync(OtherDepartmentId, 900, null, Base.AddMinutes(1))).Should().Be(0);
			(await advices.TryTransitionAsync(OtherDepartmentId, advice.AiDestinationAdviceId, AiDestinationAdviceStates.Open, AiDestinationAdviceStates.Applied,
				"intruder", Base.AddMinutes(1))).Should().BeFalse();

			var stored = await advices.GetForDepartmentAsync(DepartmentId, advice.AiDestinationAdviceId);
			stored.Should().NotBeNull();
			stored.State.Should().Be((int)AiDestinationAdviceStates.Open);
			stored.ResolvedByUserId.Should().BeNull();

			(await advices.GetForDepartmentAsync(DepartmentId, null)).Should().BeNull();
			(await advices.GetForDepartmentAsync(DepartmentId, new string('a', 37))).Should().BeNull();
		}

		[Test]
		public async Task A_state_transition_is_won_once_and_an_apply_can_be_rolled_back()
		{
			var advices = Advices();
			var advice = Advice(900, Base);
			await advices.InsertAsync(advice, CancellationToken.None);

			// Two dispatchers pressing Apply at once: one wins.
			var winners = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => Advices().TryTransitionAsync(DepartmentId, advice.AiDestinationAdviceId,
				AiDestinationAdviceStates.Open, AiDestinationAdviceStates.Applied, "dispatcher-" + i, Base.AddMinutes(2))));
			winners.Count(w => w).Should().Be(1);

			var applied = await advices.GetForDepartmentAsync(DepartmentId, advice.AiDestinationAdviceId);
			applied.State.Should().Be((int)AiDestinationAdviceStates.Applied);
			applied.ResolvedByUserId.Should().StartWith("dispatcher-");
			applied.ResolvedOn.Should().Be(Base.AddMinutes(2));
			(await advices.GetOpenForCallAsync(DepartmentId, 900)).Should().BeNull();
			(await advices.TryTransitionAsync(DepartmentId, advice.AiDestinationAdviceId, AiDestinationAdviceStates.Open, AiDestinationAdviceStates.Dismissed,
				"dispatcher-9", Base.AddMinutes(3))).Should().BeFalse("not open any more");

			// The call save failed after the apply: the service puts the advice back to Open, clearing who and when.
			(await advices.TryTransitionAsync(DepartmentId, advice.AiDestinationAdviceId, AiDestinationAdviceStates.Applied, AiDestinationAdviceStates.Open, null, null))
				.Should().BeTrue();
			var reopened = await advices.GetOpenForCallAsync(DepartmentId, 900);
			reopened.Should().NotBeNull();
			reopened.AiDestinationAdviceId.Should().Be(advice.AiDestinationAdviceId);
			reopened.ResolvedByUserId.Should().BeNull();
			reopened.ResolvedOn.Should().BeNull();

			(await advices.TryTransitionAsync(DepartmentId, advice.AiDestinationAdviceId, AiDestinationAdviceStates.Open, AiDestinationAdviceStates.Dismissed,
				"dispatcher-9", Base.AddMinutes(4))).Should().BeTrue();
			(await advices.GetOpenForCallAsync(DepartmentId, 900)).Should().BeNull();
			var dismissed = await advices.GetLatestForCallAsync(DepartmentId, 900);
			dismissed.State.Should().Be((int)AiDestinationAdviceStates.Dismissed);
			dismissed.ResolvedByUserId.Should().Be("dispatcher-9");
			dismissed.ResolvedOn.Should().Be(Base.AddMinutes(4));

			(await advices.TryTransitionAsync(DepartmentId, " ", AiDestinationAdviceStates.Open, AiDestinationAdviceStates.Applied, "x", Base)).Should().BeFalse();
		}
	}
}
