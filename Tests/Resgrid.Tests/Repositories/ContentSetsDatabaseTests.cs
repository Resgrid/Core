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
using Resgrid.Model.Approvals;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Repositories.Queries.Contracts;
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries;
using Resgrid.Repositories.DataRepository.Queries.Common;
using Resgrid.Repositories.DataRepository.Servers.PostgreSql;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;

namespace Resgrid.Tests.Repositories
{
	/// <summary>
	/// Real-database proof for round 10 A3 on both engines: M0414 (the Protocols and Training share kinds on DepartmentLinks), M0415 (content
	/// sets, items, subscriptions, copies, the update inbox and proposals), M0416 (LMS sponsorships) and M0417 (mutual-aid countersignature
	/// columns); up again is a no-op, down removes exactly them and up rebuilds them. Then the repositories on rows read back (one open
	/// subscription per set and department, the due/claim/compare-and-set inbox, the auto-accept and deadline join, detaching copies, one active
	/// sponsorship per pair and ending it once, the countersignature reads) and the department cleanup for both parties.
	/// Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class ContentSetsDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "content_sets_";
		private DatabaseTypes _previous;
		private string _master, _connection, _database;

		private bool IsPostgres => type == DatabaseTypes.Postgres;

		private DbConnection Connect(string connection) => IsPostgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);

		private string T(string name) => IsPostgres ? name.ToLowerInvariant() : "[" + name + "]";

		private TRepository Repository<TRepository>(Func<IConnectionProvider, SqlConfiguration, IUnitOfWork, IQueryFactory, TRepository> build)
		{
			var connections = new Mock<IConnectionProvider>();
			connections.Setup(c => c.Create()).Returns(() => Connect(_connection));
			SqlConfiguration configuration = IsPostgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration();
			var queries = new ConcurrentDictionary<Type, IQuery>();
			queries[typeof(InsertQuery)] = new InsertQuery(configuration);
			queries[typeof(UpdateQuery)] = new UpdateQuery(configuration);
			queries[typeof(SelectByIdQuery)] = new SelectByIdQuery(configuration);
			var list = new Mock<IQueryList>();
			list.Setup(l => l.RetrieveQueryList()).Returns(queries);
			return build(connections.Object, configuration, Mock.Of<IUnitOfWork>(), new QueryFactory(list.Object));
		}

		private IMigration[] Migrations() => IsPostgres
			? new IMigration[]
			{
				new Resgrid.Providers.MigrationsPg.Migrations.M0386_AddMutualAidAgreementsPg(),
				new Resgrid.Providers.MigrationsPg.Migrations.M0414_AddDepartmentLinkContentSharesPg(), new Resgrid.Providers.MigrationsPg.Migrations.M0415_AddContentSetsPg(),
				new Resgrid.Providers.MigrationsPg.Migrations.M0416_AddLmsSponsorshipsPg(), new Resgrid.Providers.MigrationsPg.Migrations.M0417_AddMutualAidCountersignaturePg()
			}
			: new IMigration[]
			{
				new Resgrid.Providers.Migrations.Migrations.M0386_AddMutualAidAgreements(),
				new Resgrid.Providers.Migrations.Migrations.M0414_AddDepartmentLinkContentShares(), new Resgrid.Providers.Migrations.Migrations.M0415_AddContentSets(),
				new Resgrid.Providers.Migrations.Migrations.M0416_AddLmsSponsorships(), new Resgrid.Providers.Migrations.Migrations.M0417_AddMutualAidCountersignature()
			};

		private void Migrate(Action<IMigrationRunner> run)
		{
			var source = new Mock<IMigrationSource>();
			source.Setup(s => s.GetMigrations()).Returns(Migrations());
			using var provider = new ServiceCollection().AddFluentMigratorCore().ConfigureRunner(r =>
			{
				if (IsPostgres) r.AddPostgres(); else r.AddSqlServer();
				r.WithGlobalConnectionString(_connection);
			}).AddSingleton(source.Object).BuildServiceProvider();
			run(provider.GetRequiredService<IMigrationRunner>());
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

			await using (var database = Connect(_connection))
			{
				await database.OpenAsync();
				// DepartmentLinks as the baseline schema left it (M0414 adds its four columns).
				await database.ExecuteAsync(IsPostgres ? @"
CREATE EXTENSION IF NOT EXISTS citext;
CREATE TABLE departmentlinks (departmentlinkid serial PRIMARY KEY, departmentid integer NOT NULL, departmentcolor text NULL, departmentsharecalls boolean NOT NULL DEFAULT false,
	departmentshareunits boolean NOT NULL DEFAULT false, departmentsharepersonnel boolean NOT NULL DEFAULT false, departmentshareorders boolean NOT NULL DEFAULT false,
	linkeddepartmentid integer NOT NULL, linkenabled boolean NOT NULL DEFAULT false, linkeddepartmentcolor text NULL, linkeddepartmentsharecalls boolean NOT NULL DEFAULT false,
	linkeddepartmentshareunits boolean NOT NULL DEFAULT false, linkeddepartmentsharepersonnel boolean NOT NULL DEFAULT false, linkeddepartmentshareorders boolean NOT NULL DEFAULT false,
	linkcreated timestamp NOT NULL, linkaccepted timestamp NULL);" : @"
CREATE TABLE DepartmentLinks (DepartmentLinkId int IDENTITY(1,1) PRIMARY KEY, DepartmentId int NOT NULL, DepartmentColor nvarchar(50) NULL, DepartmentShareCalls bit NOT NULL DEFAULT 0,
	DepartmentShareUnits bit NOT NULL DEFAULT 0, DepartmentSharePersonnel bit NOT NULL DEFAULT 0, DepartmentShareOrders bit NOT NULL DEFAULT 0, LinkedDepartmentId int NOT NULL,
	LinkEnabled bit NOT NULL DEFAULT 0, LinkedDepartmentColor nvarchar(50) NULL, LinkedDepartmentShareCalls bit NOT NULL DEFAULT 0, LinkedDepartmentShareUnits bit NOT NULL DEFAULT 0,
	LinkedDepartmentSharePersonnel bit NOT NULL DEFAULT 0, LinkedDepartmentShareOrders bit NOT NULL DEFAULT 0, LinkCreated datetime2 NOT NULL, LinkAccepted datetime2 NULL);");
				if (database is NpgsqlConnection postgres)
					await postgres.ReloadTypesAsync();
			}

			Migrate(r => r.MigrateUp());
		}

		[OneTimeTearDown]
		public async Task Remove_only_this_fixture_database()
		{
			if (_database == null) return;
			DataConfig.DatabaseType = _previous;
			if (!_database.StartsWith(Prefix, StringComparison.Ordinal) || !Guid.TryParseExact(_database.Substring(Prefix.Length), "N", out _))
				throw new InvalidOperationException("Unexpected test database name.");
			if (IsPostgres) NpgsqlConnection.ClearAllPools(); else SqlConnection.ClearAllPools();
			await using var master = Connect(_master);
			await master.ExecuteAsync(IsPostgres ? $"DROP DATABASE IF EXISTS {_database} WITH (FORCE)" : $"ALTER DATABASE {_database} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {_database}");
		}

		private static readonly string[] Tables = { "ContentSets", "ContentSetItems", "ContentSetSubscriptions", "ContentSetCopies", "ContentSetUpdates", "ContentSetProposals", "LmsSponsorships" };

		private static readonly string[] LinkColumns = { "DepartmentShareProtocols", "DepartmentShareTraining", "LinkedDepartmentShareProtocols", "LinkedDepartmentShareTraining" };

		private static readonly string[] AgreementColumns =
		{
			"RequiresCountersignature", "CountersignStatus", "CountersignRequestId", "CountersignRequestedOn", "CountersignedOn", "CountersignedByUserId", "CountersignNote"
		};

		private async Task<(int Tables, int LinkColumns, int AgreementColumns)> SchemaAsync()
		{
			await using var database = Connect(_connection);
			var tables = await database.ExecuteScalarAsync<int>(IsPostgres
				? "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = ANY(@Names)"
				: "SELECT COUNT(*) FROM sys.tables WHERE name IN @Names", new { Names = IsPostgres ? Tables.Select(t => t.ToLowerInvariant()).ToArray() : Tables });
			async Task<int> Columns(string table, string[] names) => await database.ExecuteScalarAsync<int>(IsPostgres
				? "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = @Table AND column_name = ANY(@Names)"
				: "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(@Table) AND name IN @Names",
				new { Table = IsPostgres ? table.ToLowerInvariant() : "dbo." + table, Names = IsPostgres ? names.Select(n => n.ToLowerInvariant()).ToArray() : names });
			return (tables, await Columns("DepartmentLinks", LinkColumns), await Columns("MutualAidAgreements", AgreementColumns));
		}

		[Test, Order(1)]
		public async Task Up_adds_the_tables_and_columns_and_up_again_changes_nothing()
		{
			(await SchemaAsync()).Should().Be((Tables.Length, LinkColumns.Length, AgreementColumns.Length));
			await using (var database = Connect(_connection))
				await database.ExecuteAsync(IsPostgres ? "DELETE FROM \"VersionInfo\" WHERE \"Version\" IN (414, 415, 416, 417)" : "DELETE FROM [VersionInfo] WHERE [Version] IN (414, 415, 416, 417)");

			Migrate(r => r.MigrateUp());
			(await SchemaAsync()).Should().Be((Tables.Length, LinkColumns.Length, AgreementColumns.Length));
		}

		[Test, Order(2)]
		public async Task Down_removes_exactly_them_and_up_rebuilds_them()
		{
			Migrate(r => r.MigrateDown(413));
			(await SchemaAsync()).Should().Be((0, 0, 0));

			Migrate(r => r.MigrateUp());
			(await SchemaAsync()).Should().Be((Tables.Length, LinkColumns.Length, AgreementColumns.Length));
		}

		[Test, Order(3)]
		public async Task The_new_share_kinds_round_trip_through_the_link_repository()
		{
			var links = Repository((c, s, u, q) => new DepartmentLinksRepository(c, s, u, q));
			var saved = await links.InsertAsync(new DepartmentLink
			{
				DepartmentId = 10, LinkedDepartmentId = 20, LinkEnabled = true, DepartmentShareProtocols = true, LinkedDepartmentShareTraining = true, LinkCreated = DateTime.UtcNow
			}, CancellationToken.None, true);
			var read = await links.GetByIdAsync(saved.DepartmentLinkId);
			read.DepartmentShareProtocols.Should().BeTrue();
			read.DepartmentShareTraining.Should().BeFalse();
			read.LinkedDepartmentShareTraining.Should().BeTrue();
			read.LinkedDepartmentShareProtocols.Should().BeFalse();
		}

		[Test, Order(4)]
		public async Task Sets_subscriptions_and_copies_read_back_and_one_open_subscription_per_set_and_department()
		{
			var sets = Repository((c, s, u, q) => new ContentSetsRepository(c, s, u, q));
			var items = Repository((c, s, u, q) => new ContentSetItemsRepository(c, s, u, q));
			var subscriptions = Repository((c, s, u, q) => new ContentSetSubscriptionsRepository(c, s, u, q));
			var copies = Repository((c, s, u, q) => new ContentSetCopiesRepository(c, s, u, q));
			var now = DateTime.UtcNow;

			var set = await sets.InsertAsync(new ContentSet { DepartmentId = 40, Kind = (int)ContentSetKinds.Protocols, Name = "County EMS", LockedDeferDays = 30, CreatedOn = now }, CancellationToken.None, true);
			await sets.InsertAsync(new ContentSet { DepartmentId = 40, Kind = (int)ContentSetKinds.Training, Name = "Academy", CreatedOn = now }, CancellationToken.None, true);
			await sets.InsertAsync(new ContentSet { DepartmentId = 40, Kind = (int)ContentSetKinds.Protocols, Name = "Old", IsArchived = true, CreatedOn = now }, CancellationToken.None, true);
			(await sets.GetLiveForPublishersAsync(new[] { 40, 41 }, ContentSetKinds.Protocols)).Select(s => s.Name).Should().Equal("County EMS");
			(await sets.GetForDepartmentAsync(40, ContentSetKinds.Protocols, true)).Should().HaveCount(2);
			(await sets.GetByIdForDepartmentAsync(41, set.ContentSetId)).Should().BeNull();

			var item = await items.InsertAsync(new ContentSetItem
			{
				DepartmentId = 40, ContentSetId = set.ContentSetId, ItemKind = (int)ContentSetItemKinds.Protocol, ItemId = 7, LockMode = (int)ContentSetLockModes.CustomizableBlocks,
				CustomizableBlockKeys = "b2,b3", SortOrder = 1, AddedOn = now
			}, CancellationToken.None, true);
			(await items.GetLiveForItemAsync(40, ContentSetItemKinds.Protocol, 7)).Single().CustomizableKeys.Should().Equal("b2", "b3");
			(await items.GetLiveByKindAsync(ContentSetItemKinds.Protocol, 10)).Should().ContainSingle();

			var subscription = await subscriptions.InsertAsync(new ContentSetSubscription
			{
				DepartmentId = 1, ContentSetId = set.ContentSetId, PublisherDepartmentId = 40, DepartmentLinkId = 3, Status = (int)ContentSetSubscriptionStatuses.Active, AutoAccept = true,
				SubscribedOn = now
			}, CancellationToken.None, true);
			Func<Task> second = () => subscriptions.InsertAsync(new ContentSetSubscription
			{
				DepartmentId = 1, ContentSetId = set.ContentSetId, PublisherDepartmentId = 40, DepartmentLinkId = 3, Status = (int)ContentSetSubscriptionStatuses.Suspended, SubscribedOn = now
			}, CancellationToken.None, true);
			await second.Should().ThrowAsync<Exception>("one open subscription per set and department");
			(await subscriptions.GetOpenAsync(set.ContentSetId, 1)).ContentSetSubscriptionId.Should().Be(subscription.ContentSetSubscriptionId);
			(await subscriptions.GetOpenForLinkAsync(3)).Should().ContainSingle();
			(await subscriptions.GetAllOpenAsync(10)).Should().ContainSingle();
			(await subscriptions.GetOpenBetweenAsync(40, 1)).Should().ContainSingle();

			// An ended subscription frees the slot.
			subscription.Status = (int)ContentSetSubscriptionStatuses.Ended;
			await subscriptions.UpdateAsync(subscription, CancellationToken.None, true);
			await second.Should().NotThrowAsync();
			(await subscriptions.GetForDepartmentAsync(1, false)).Should().ContainSingle(s => s.Status == (int)ContentSetSubscriptionStatuses.Suspended);
			(await subscriptions.GetForDepartmentAsync(1, true)).Should().HaveCount(2);

			var copy = await copies.InsertAsync(new ContentSetCopy
			{
				DepartmentId = 1, ContentSetSubscriptionId = subscription.ContentSetSubscriptionId, ContentSetItemId = item.ContentSetItemId, PublisherDepartmentId = 40,
				ItemKind = (int)ContentSetItemKinds.Protocol, SourceItemId = 7, LocalItemId = 700, SourceVersionId = 70, SourceVersionLabel = "2.1",
				LocalCustomizationJson = "{\"overrides\":{\"b2\":\"<p>x</p>\"},\"additions\":[]}", CreatedOn = now
			}, CancellationToken.None, true);
			(await copies.GetManagedForLocalItemAsync(1, ContentSetItemKinds.Protocol, 700)).ContentSetCopyId.Should().Be(copy.ContentSetCopyId);
			(await copies.GetManagedForLocalItemAsync(2, ContentSetItemKinds.Protocol, 700)).Should().BeNull();
			(await copies.GetManagedForDepartmentAsync(1, ContentSetItemKinds.Protocol)).Should().ContainSingle();
			(await copies.DetachAsync(subscription.ContentSetSubscriptionId, item.ContentSetItemId, now)).Should().Be(1);
			(await copies.DetachAsync(subscription.ContentSetSubscriptionId, null, now)).Should().Be(0, "already detached");
			(await copies.GetManagedForLocalItemAsync(1, ContentSetItemKinds.Protocol, 700)).Should().BeNull();
		}

		[Test, Order(5)]
		public async Task The_inbox_claims_once_moves_only_from_the_expected_status_and_finds_what_no_one_has_to_decide()
		{
			var subscriptions = Repository((c, s, u, q) => new ContentSetSubscriptionsRepository(c, s, u, q));
			var updates = Repository((c, s, u, q) => new ContentSetUpdatesRepository(c, s, u, q));
			var now = DateTime.UtcNow;
			var auto = await subscriptions.InsertAsync(new ContentSetSubscription
			{
				DepartmentId = 2, ContentSetId = 500, PublisherDepartmentId = 40, DepartmentLinkId = 4, Status = (int)ContentSetSubscriptionStatuses.Active, AutoAccept = true, SubscribedOn = now
			}, CancellationToken.None, true);
			var manual = await subscriptions.InsertAsync(new ContentSetSubscription
			{
				DepartmentId = 2, ContentSetId = 501, PublisherDepartmentId = 40, DepartmentLinkId = 4, Status = (int)ContentSetSubscriptionStatuses.Active, SubscribedOn = now
			}, CancellationToken.None, true);

			ContentSetUpdate Row(int subscriptionId, ContentSetUpdateStatuses status, DateTime? deadline = null, DateTime? applyOn = null, DateTime? deferredUntil = null) => new ContentSetUpdate
			{
				DepartmentId = 2, ContentSetSubscriptionId = subscriptionId, ContentSetItemId = 9, PublisherDepartmentId = 40, ItemKind = 0, SourceItemId = 7, SourceVersionId = 71,
				SourceVersionLabel = "2.2", Title = "Stroke", SourceEffectiveOn = now, DeadlineOn = deadline, Status = (int)status, ApplyOn = applyOn, DeferredUntil = deferredUntil, CreatedOn = now
			};
			var autoPending = await updates.InsertAsync(Row(auto.ContentSetSubscriptionId, ContentSetUpdateStatuses.Pending), CancellationToken.None, true);
			var manualPending = await updates.InsertAsync(Row(manual.ContentSetSubscriptionId, ContentSetUpdateStatuses.Pending), CancellationToken.None, true);
			var pastDeadline = await updates.InsertAsync(Row(manual.ContentSetSubscriptionId, ContentSetUpdateStatuses.Deferred, now.AddMinutes(-1), deferredUntil: now.AddDays(1)),
				CancellationToken.None, true);
			var deferralOver = await updates.InsertAsync(Row(manual.ContentSetSubscriptionId, ContentSetUpdateStatuses.Deferred, null, deferredUntil: now.AddMinutes(-1)), CancellationToken.None, true);
			var due = await updates.InsertAsync(Row(manual.ContentSetSubscriptionId, ContentSetUpdateStatuses.Scheduled, applyOn: now.AddMinutes(-1)), CancellationToken.None, true);
			await updates.InsertAsync(Row(manual.ContentSetSubscriptionId, ContentSetUpdateStatuses.Scheduled, applyOn: now.AddDays(1)), CancellationToken.None, true);

			(await updates.GetUndecidedDueAsync(now, 50)).Select(u => u.ContentSetUpdateId).Should()
				.BeEquivalentTo(new[] { autoPending.ContentSetUpdateId, pastDeadline.ContentSetUpdateId, deferralOver.ContentSetUpdateId });
			(await updates.GetUndecidedDueAsync(now, 50)).Should().NotContain(u => u.ContentSetUpdateId == manualPending.ContentSetUpdateId);

			(await updates.GetDueAsync(now, TimeSpan.FromMinutes(15), 50)).Select(u => u.ContentSetUpdateId).Should().Equal(due.ContentSetUpdateId);
			(await updates.TryClaimAsync(due.ContentSetUpdateId, now, TimeSpan.FromMinutes(15))).Should().BeTrue();
			(await updates.TryClaimAsync(due.ContentSetUpdateId, now, TimeSpan.FromMinutes(15))).Should().BeFalse("another host holds a fresh claim");
			(await updates.GetDueAsync(now, TimeSpan.FromMinutes(15), 50)).Should().BeEmpty();

			due.Status = (int)ContentSetUpdateStatuses.Applied;
			due.AppliedOn = now;
			due.LocalVersionId = 7001;
			(await updates.TryUpdateFromStatusAsync(due, ContentSetUpdateStatuses.Pending)).Should().BeFalse();
			(await updates.TryUpdateFromStatusAsync(due, ContentSetUpdateStatuses.Scheduled)).Should().BeTrue();
			(await updates.GetByIdForDepartmentAsync(2, due.ContentSetUpdateId)).LocalVersionId.Should().Be(7001);
			(await updates.GetByIdForDepartmentAsync(3, due.ContentSetUpdateId)).Should().BeNull();

			(await updates.GetOpenForItemAsync(manual.ContentSetSubscriptionId, 9)).Should().HaveCount(4);
			(await updates.CloseOpenForSubscriptionAsync(manual.ContentSetSubscriptionId, ContentSetUpdateStatuses.Superseded, now)).Should().Be(4);
			(await updates.GetForDepartmentAsync(2, true, 50)).Select(u => u.ContentSetUpdateId).Should().Equal(autoPending.ContentSetUpdateId);
			(await updates.GetForDepartmentAsync(2, false, 50)).Should().HaveCount(6);
		}

		[Test, Order(6)]
		public async Task Proposals_and_sponsorships_read_back_and_a_sponsorship_ends_once()
		{
			var proposals = Repository((c, s, u, q) => new ContentSetProposalsRepository(c, s, u, q));
			var proposal = await proposals.InsertAsync(new ContentSetProposal
			{
				DepartmentId = 1, PublisherDepartmentId = 40, ContentSetId = 1, ContentSetItemId = 1, ItemKind = 0, SourceItemId = 7, Body = "Add the dose table.", AuthorUserId = "u1",
				AuthorName = "Pat", SubscriberName = "Dept 1", Status = (int)ContentSetProposalStatuses.Open, CreatedOn = DateTime.UtcNow
			}, CancellationToken.None, true);
			(await proposals.GetForPublisherAsync(40, true, 10)).Should().ContainSingle();
			(await proposals.GetForDepartmentAsync(1, 10)).Should().ContainSingle();
			(await proposals.GetForPublisherByIdAsync(41, proposal.ContentSetProposalId)).Should().BeNull();

			var sponsorships = Repository((c, s, u, q) => new LmsSponsorshipsRepository(c, s, u, q));
			var active = await sponsorships.InsertAsync(new LmsSponsorship
			{
				DepartmentId = 1, SponsorDepartmentId = 40, DepartmentLinkId = 3, Status = (int)LmsSponsorshipStatuses.Active, SeatLimit = 25, StartedOn = DateTime.UtcNow
			}, CancellationToken.None, true);
			Func<Task> again = () => sponsorships.InsertAsync(new LmsSponsorship
			{
				DepartmentId = 1, SponsorDepartmentId = 40, DepartmentLinkId = 3, Status = (int)LmsSponsorshipStatuses.Active, StartedOn = DateTime.UtcNow
			}, CancellationToken.None, true);
			await again.Should().ThrowAsync<Exception>("one active sponsorship per sponsor and department");
			(await sponsorships.GetActiveForDepartmentAsync(1)).Single().SeatLimit.Should().Be(25);
			(await sponsorships.GetActiveForSponsorAsync(40)).Should().ContainSingle();
			(await sponsorships.GetActiveForLinkAsync(3)).Should().ContainSingle();
			(await sponsorships.GetAllActiveAsync(10)).Should().ContainSingle();
			(await sponsorships.GetActiveAsync(40, 1)).LmsSponsorshipId.Should().Be(active.LmsSponsorshipId);

			(await sponsorships.TryEndAsync(active.LmsSponsorshipId, LmsSponsorshipEndReasons.LinkRevoked, null, DateTime.UtcNow)).Should().BeTrue();
			(await sponsorships.TryEndAsync(active.LmsSponsorshipId, LmsSponsorshipEndReasons.EndedBySponsor, "u", DateTime.UtcNow)).Should().BeFalse();
			(await sponsorships.GetActiveForDepartmentAsync(1)).Should().BeEmpty();
			(await sponsorships.GetForPartyAsync(40, 10)).Single().EndReason.Should().Be((int)LmsSponsorshipEndReasons.LinkRevoked);
			await again.Should().NotThrowAsync();
		}

		[Test, Order(7)]
		public async Task Countersignature_columns_read_back_for_the_partner_and_the_link()
		{
			var agreements = Repository((c, s, u, q) => new MutualAidAgreementsRepository(c, s, u, q));
			await agreements.InsertAsync(new MutualAidAgreement
			{
				DepartmentId = 1, Title = "North county", DepartmentLinkId = 3, PartnerDepartmentId = 40, Status = (int)MutualAidAgreementStatuses.Approved, RequiresCountersignature = true,
				CountersignStatus = (int)MutualAidCountersignStatuses.Requested, CountersignRequestId = 77, CountersignRequestedOn = DateTime.UtcNow, CreatedOn = DateTime.UtcNow
			}, CancellationToken.None, true);
			await agreements.InsertAsync(new MutualAidAgreement
			{
				DepartmentId = 2, Title = "Signed", DepartmentLinkId = 3, PartnerDepartmentId = 40, Status = (int)MutualAidAgreementStatuses.Approved, RequiresCountersignature = true,
				CountersignStatus = (int)MutualAidCountersignStatuses.Countersigned, CountersignedOn = DateTime.UtcNow, CountersignedByUserId = "partner", CreatedOn = DateTime.UtcNow
			}, CancellationToken.None, true);

			var awaiting = await agreements.GetAwaitingCountersignatureAsync(40);
			awaiting.Should().ContainSingle(a => a.Title == "North county" && a.CountersignRequestId == 77 && a.RequiresCountersignature);
			(await agreements.GetAwaitingCountersignatureAsync(1)).Should().BeEmpty();
			(await agreements.GetCountersignRequestedForLinkAsync(3)).Should().ContainSingle();
			(await agreements.GetAllCountersignRequestedAsync(10)).Should().ContainSingle();
			(await agreements.GetAllForDepartmentAsync(2)).Single().CountersignedByUserId.Should().Be("partner");
		}

		[Test, Order(8)]
		public async Task Department_cleanup_removes_its_rows_as_publisher_subscriber_sponsor_and_sponsored()
		{
			var sets = Repository((c, s, u, q) => new ContentSetsRepository(c, s, u, q));
			var subscriptions = Repository((c, s, u, q) => new ContentSetSubscriptionsRepository(c, s, u, q));
			var copies = Repository((c, s, u, q) => new ContentSetCopiesRepository(c, s, u, q));
			var updates = Repository((c, s, u, q) => new ContentSetUpdatesRepository(c, s, u, q));
			var sponsorships = Repository((c, s, u, q) => new LmsSponsorshipsRepository(c, s, u, q));
			var now = DateTime.UtcNow;

			// Department 60 publishes to 61; department 62 publishes to 60; 63 and 64 are unrelated.
			await sets.InsertAsync(new ContentSet { DepartmentId = 60, Kind = 0, Name = "Mine", CreatedOn = now }, CancellationToken.None, true);
			await sets.InsertAsync(new ContentSet { DepartmentId = 63, Kind = 0, Name = "Theirs", CreatedOn = now }, CancellationToken.None, true);
			await subscriptions.InsertAsync(new ContentSetSubscription { DepartmentId = 61, ContentSetId = 9001, PublisherDepartmentId = 60, DepartmentLinkId = 1, SubscribedOn = now }, CancellationToken.None, true);
			await subscriptions.InsertAsync(new ContentSetSubscription { DepartmentId = 60, ContentSetId = 9002, PublisherDepartmentId = 62, DepartmentLinkId = 1, SubscribedOn = now }, CancellationToken.None, true);
			await subscriptions.InsertAsync(new ContentSetSubscription { DepartmentId = 64, ContentSetId = 9003, PublisherDepartmentId = 63, DepartmentLinkId = 1, SubscribedOn = now }, CancellationToken.None, true);
			await copies.InsertAsync(new ContentSetCopy { DepartmentId = 61, ContentSetSubscriptionId = 1, ContentSetItemId = 1, PublisherDepartmentId = 60, LocalItemId = 1, CreatedOn = now }, CancellationToken.None, true);
			await updates.InsertAsync(new ContentSetUpdate { DepartmentId = 60, ContentSetSubscriptionId = 2, ContentSetItemId = 1, PublisherDepartmentId = 62, SourceEffectiveOn = now, CreatedOn = now },
				CancellationToken.None, true);
			await sponsorships.InsertAsync(new LmsSponsorship { DepartmentId = 61, SponsorDepartmentId = 60, DepartmentLinkId = 1, StartedOn = now }, CancellationToken.None, true);
			await sponsorships.InsertAsync(new LmsSponsorship { DepartmentId = 60, SponsorDepartmentId = 62, DepartmentLinkId = 1, StartedOn = now }, CancellationToken.None, true);
			await sponsorships.InsertAsync(new LmsSponsorship { DepartmentId = 64, SponsorDepartmentId = 63, DepartmentLinkId = 1, StartedOn = now }, CancellationToken.None, true);

			await using var database = Connect(_connection);
			await database.OpenAsync();
			await using (var transaction = await database.BeginTransactionAsync())
			{
				await UnlinkedDepartmentCleanup.DeleteWithinTransactionAsync(database, transaction, 60, type);
				await transaction.CommitAsync();
			}

			foreach (var table in Tables)
				(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T(table)} WHERE {T("DepartmentId")} = 60")).Should().Be(0, table);
			(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("ContentSetSubscriptions")} WHERE {T("PublisherDepartmentId")} = 60")).Should().Be(0);
			(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("ContentSetCopies")} WHERE {T("PublisherDepartmentId")} = 60")).Should().Be(0);
			(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("LmsSponsorships")} WHERE {T("SponsorDepartmentId")} = 60")).Should().Be(0);
			(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("ContentSets")} WHERE {T("DepartmentId")} = 63")).Should().Be(1);
			(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("ContentSetSubscriptions")} WHERE {T("DepartmentId")} = 64")).Should().Be(1);
			(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("LmsSponsorships")} WHERE {T("DepartmentId")} = 64")).Should().Be(1);
		}
	}
}
