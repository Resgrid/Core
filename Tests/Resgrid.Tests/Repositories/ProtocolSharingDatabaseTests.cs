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
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries;
using Resgrid.Repositories.DataRepository.Queries.Common;
using Resgrid.Repositories.DataRepository.Servers.PostgreSql;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;

namespace Resgrid.Tests.Repositories
{
	/// <summary>
	/// Real-database proof for Protocols P5 on both engines: M0388 (ProtocolPublicLinks, ContentAttributions), M0389 (the community exchange)
	/// and M0390 (ProtocolSearchMisses); Up again is a no-op, Down removes them and Up rebuilds them. Then the repositories on rows read back:
	/// attribution upsert, case-exact public tokens and revoke-once, search miss counting, the exchange browse / status guard / rating refresh /
	/// comment hide, the analytics call read, and the department cleanup on both engines.
	/// Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class ProtocolSharingDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "protocol_p5_";
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

		private IMigration[] P5() => IsPostgres
			? new IMigration[]
			{
				new Resgrid.Providers.MigrationsPg.Migrations.M0388_AddProtocolPublicPagesAndAttributionsPg(), new Resgrid.Providers.MigrationsPg.Migrations.M0389_AddCommunityExchangePg(),
				new Resgrid.Providers.MigrationsPg.Migrations.M0390_AddProtocolSearchMissesPg()
			}
			: new IMigration[]
			{
				new Resgrid.Providers.Migrations.Migrations.M0388_AddProtocolPublicPagesAndAttributions(), new Resgrid.Providers.Migrations.Migrations.M0389_AddCommunityExchange(),
				new Resgrid.Providers.Migrations.Migrations.M0390_AddProtocolSearchMisses()
			};

		private void Migrate(IMigration[] migrations, Action<IMigrationRunner> run)
		{
			var source = new Mock<IMigrationSource>();
			source.Setup(s => s.GetMigrations()).Returns(migrations);
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
				// The call tables the analytics read, as P0-P4 left them (only the columns the read uses).
				await database.ExecuteAsync(IsPostgres ? @"
CREATE EXTENSION IF NOT EXISTS citext;
CREATE TABLE calls (callid serial PRIMARY KEY, departmentid integer NOT NULL, loggedon timestamp NOT NULL);
CREATE TABLE dispatchprotocols (dispatchprotocolid serial PRIMARY KEY, departmentid integer NOT NULL, name citext NOT NULL);
CREATE TABLE callprotocols (callprotocolid serial PRIMARY KEY, callid integer NOT NULL, dispatchprotocolid integer NOT NULL, score integer NOT NULL DEFAULT 0,
	trigger integer NOT NULL DEFAULT 0, source integer NULL, protocolversionid integer NULL, attachedon timestamp NULL, removedon timestamp NULL,
	steplogjson text NULL, determinant citext NULL, overridereason citext NULL);" : @"
CREATE TABLE Calls (CallId int IDENTITY(1,1) PRIMARY KEY, DepartmentId int NOT NULL, LoggedOn datetime NOT NULL);
CREATE TABLE DispatchProtocols (DispatchProtocolId int IDENTITY(1,1) PRIMARY KEY, DepartmentId int NOT NULL, Name nvarchar(100) NOT NULL);
CREATE TABLE CallProtocols (CallProtocolId int IDENTITY(1,1) PRIMARY KEY, CallId int NOT NULL, DispatchProtocolId int NOT NULL, Score int NOT NULL DEFAULT 0,
	[Trigger] int NOT NULL DEFAULT 0, Source int NULL, ProtocolVersionId int NULL, AttachedOn datetime2 NULL, RemovedOn datetime2 NULL,
	StepLogJson nvarchar(max) NULL, Determinant nvarchar(50) NULL, OverrideReason nvarchar(500) NULL);");
				if (database is NpgsqlConnection postgres)
					await postgres.ReloadTypesAsync();
			}

			Migrate(P5(), r => r.MigrateUp());
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

		private static readonly string[] Tables = { "ProtocolPublicLinks", "ContentAttributions", "ExchangeListings", "ExchangeRatings", "ExchangeComments", "ExchangeImports", "ProtocolSearchMisses" };

		private async Task<int> CountSchemaAsync()
		{
			await using var database = Connect(_connection);
			return await database.ExecuteScalarAsync<int>(IsPostgres
				? "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = ANY(@Names)"
				: "SELECT COUNT(*) FROM sys.tables WHERE name IN @Names", new { Names = IsPostgres ? Tables.Select(t => t.ToLowerInvariant()).ToArray() : Tables });
		}

		[Test, Order(1)]
		public async Task Up_adds_the_tables_and_up_again_changes_nothing()
		{
			(await CountSchemaAsync()).Should().Be(Tables.Length);
			await using (var database = Connect(_connection))
				await database.ExecuteAsync(IsPostgres ? "DELETE FROM \"VersionInfo\" WHERE \"Version\" IN (388, 389, 390)" : "DELETE FROM [VersionInfo] WHERE [Version] IN (388, 389, 390)");

			Migrate(P5(), r => r.MigrateUp());
			(await CountSchemaAsync()).Should().Be(Tables.Length);
		}

		[Test, Order(2)]
		public async Task Down_removes_them_and_up_again_rebuilds_them()
		{
			Migrate(P5(), r => r.MigrateDown(387));
			(await CountSchemaAsync()).Should().Be(0);

			Migrate(P5(), r => r.MigrateUp());
			(await CountSchemaAsync()).Should().Be(Tables.Length);
		}

		[Test, Order(3)]
		public async Task Attribution_is_one_row_per_subject_and_a_second_save_replaces_it()
		{
			var repository = Repository((c, s, u, q) => new ContentAttributionsRepository(c, s, u, q));
			ContentAttribution Row(int department, int subject, string license) => new ContentAttribution
			{
				DepartmentId = department, SubjectKind = (int)ContentAttributionSubjects.ProtocolVersion, SubjectId = subject, SourceKind = (int)ContentAttributionSources.TemplateLibrary,
				SourceRef = "fire-sog-starter", SourceName = "Resgrid template library: Fire", Author = "Resgrid", License = license, LicenseUrl = ContentLicenses.UrlFor(license),
				SourceVersion = "1.0", CreatedOn = DateTime.UtcNow, CreatedByUserId = "u1"
			};

			var first = await repository.SaveForSubjectAsync(Row(1, 500, ContentLicenses.CcBy));
			var second = await repository.SaveForSubjectAsync(Row(1, 500, ContentLicenses.Cc0));
			await repository.SaveForSubjectAsync(Row(2, 500, ContentLicenses.CcBy));
			await repository.SaveForSubjectAsync(Row(1, 501, ContentLicenses.CcBySa));

			second.ContentAttributionId.Should().Be(first.ContentAttributionId);
			(await repository.GetForSubjectAsync(1, ContentAttributionSubjects.ProtocolVersion, 500)).License.Should().Be(ContentLicenses.Cc0);
			(await repository.GetForSubjectAsync(1, ContentAttributionSubjects.Training, 500)).Should().BeNull();
			(await repository.GetForSubjectsAsync(1, ContentAttributionSubjects.ProtocolVersion, new[] { 500, 501, 502 })).Select(a => a.SubjectId).Should().BeEquivalentTo(new[] { 500, 501 });
			(await repository.GetForSubjectsAsync(1, ContentAttributionSubjects.ProtocolVersion, Array.Empty<int>())).Should().BeEmpty();
		}

		[Test, Order(4)]
		public async Task Public_tokens_match_exactly_and_a_link_is_revoked_once_by_its_department()
		{
			var repository = Repository((c, s, u, q) => new ProtocolPublicLinksRepository(c, s, u, q));
			var token = "AbCdEfGhIjKlMnOpQrStUvWxYz012345";
			ProtocolPublicPages.IsTokenShape(token).Should().BeTrue();
			var link = await repository.InsertAsync(new ProtocolPublicLink
			{
				DepartmentId = 1, Kind = (int)ProtocolPublicLinkKinds.Protocol, DispatchProtocolId = 3, Token = token, EmbedEnabled = true, EmbedOrigins = "https://fire.example.org",
				CreatedOn = DateTime.UtcNow, CreatedByUserId = "u1"
			}, CancellationToken.None, true);

			(await repository.GetByTokenAsync(token)).ProtocolPublicLinkId.Should().Be(link.ProtocolPublicLinkId);
			(await repository.GetByTokenAsync(token.ToLowerInvariant())).Should().BeNull("tokens are case-sensitive on both engines");
			(await repository.GetByTokenAsync(token.ToUpperInvariant())).Should().BeNull();
			(await repository.GetForDepartmentAsync(1, false)).Should().ContainSingle();

			(await repository.TryRevokeAsync(2, link.ProtocolPublicLinkId, "u2", DateTime.UtcNow)).Should().BeFalse("another department never revokes it");
			(await repository.TryRevokeAsync(1, link.ProtocolPublicLinkId, "u1", DateTime.UtcNow)).Should().BeTrue();
			(await repository.TryRevokeAsync(1, link.ProtocolPublicLinkId, "u1", DateTime.UtcNow)).Should().BeFalse("revoked once");
			(await repository.GetByTokenAsync(token)).IsRevoked.Should().BeTrue();
			(await repository.GetForDepartmentAsync(1, false)).Should().BeEmpty();
			(await repository.GetForDepartmentAsync(1, true)).Should().ContainSingle();

			await repository.InsertAsync(new ProtocolPublicLink { DepartmentId = 2, Kind = 1, ProtocolCategoryId = 4, Token = "zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz2", CreatedOn = DateTime.UtcNow },
				CancellationToken.None, true);
			Func<Task> duplicate = () => repository.InsertAsync(new ProtocolPublicLink { DepartmentId = 2, Kind = 1, ProtocolCategoryId = 5, Token = "zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz2", CreatedOn = DateTime.UtcNow },
				CancellationToken.None, true);
			await duplicate.Should().ThrowAsync<Exception>("the token is unique");
		}

		[Test, Order(5)]
		public async Task Search_misses_are_counted_per_query_and_day_and_summed_over_the_period()
		{
			var repository = Repository((c, s, u, q) => new ProtocolSearchMissesRepository(c, s, u, q));
			var day = new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc);
			await repository.RecordAsync(1, ProtocolSearchSurfaces.Library, "trench rescue", day);
			await repository.RecordAsync(1, ProtocolSearchSurfaces.Library, "trench rescue", day.AddHours(3));
			await repository.RecordAsync(1, ProtocolSearchSurfaces.Library, "trench rescue", day.AddDays(1));
			await repository.RecordAsync(1, ProtocolSearchSurfaces.Library, "ice rescue", day);
			await repository.RecordAsync(2, ProtocolSearchSurfaces.Library, "trench rescue", day);

			var top = await repository.GetTopAsync(1, day.Date, day.Date.AddDays(7), 10);
			top.Select(m => (m.Query, m.Count)).Should().Equal(("trench rescue", 3), ("ice rescue", 1));
			top[0].LastSeenOn.Should().BeCloseTo(day.AddDays(1), TimeSpan.FromSeconds(1));
			(await repository.GetTopAsync(1, day.Date.AddDays(1), day.Date.AddDays(2), 10)).Single().Count.Should().Be(1, "the period is by day");
		}

		private ExchangeListing Listing(int department, int status, string title, string payload = "{\"format\":\"resgrid-protocols\"}") => new ExchangeListing
		{
			DepartmentId = department, ItemKind = (int)ExchangeItemKinds.Protocol, SourceRef = "3", Title = title, Summary = "Summary of " + title, Tags = "rit",
			VersionLabel = "1.0", PayloadJson = payload, PayloadHash = ExchangeRules.Hash(payload), License = ContentLicenses.CcBy, AttributionName = "Dept " + department,
			Status = status, SubmittedOn = DateTime.UtcNow, SubmittedByUserId = "u" + department, AttestationVersion = ExchangeRules.AttestationVersion,
			ListedOn = status == (int)ExchangeListingStatuses.Listed ? DateTime.UtcNow : (DateTime?)null
		};

		[Test, Order(6)]
		public async Task The_exchange_lists_only_reviewed_items_guards_status_moves_and_keeps_its_counters()
		{
			var listings = Repository((c, s, u, q) => new ExchangeListingsRepository(c, s, u, q));
			var ratings = Repository((c, s, u, q) => new ExchangeRatingsRepository(c, s, u, q));
			var comments = Repository((c, s, u, q) => new ExchangeCommentsRepository(c, s, u, q));
			var imports = Repository((c, s, u, q) => new ExchangeImportsRepository(c, s, u, q));

			var pending = await listings.InsertAsync(Listing(1, (int)ExchangeListingStatuses.PendingReview, "Mayday"), CancellationToken.None, true);
			var listed = await listings.InsertAsync(Listing(1, (int)ExchangeListingStatuses.Listed, "Rapid intervention 100% ready"), CancellationToken.None, true);
			var theirs = await listings.InsertAsync(Listing(2, (int)ExchangeListingStatuses.Listed, "Ice rescue"), CancellationToken.None, true);

			var (items, total) = await listings.BrowseAsync(new ExchangeBrowseQuery());
			total.Should().Be(2);
			items.Select(i => i.ExchangeListingId).Should().BeEquivalentTo(new[] { listed.ExchangeListingId, theirs.ExchangeListingId });
			items.Should().OnlyContain(i => i.PayloadJson == null, "lists never read the payload");
			(await listings.BrowseAsync(new ExchangeBrowseQuery { Search = "100%" })).Items.Should().ContainSingle(i => i.ExchangeListingId == listed.ExchangeListingId);
			(await listings.BrowseAsync(new ExchangeBrowseQuery { Search = "ICE" })).Items.Should().ContainSingle(i => i.ExchangeListingId == theirs.ExchangeListingId);
			(await listings.BrowseAsync(new ExchangeBrowseQuery { Search = "%" })).Total.Should().Be(1, "a % in the search is a literal");
			(await listings.GetPendingReviewAsync(10)).Select(l => l.ExchangeListingId).Should().Equal(pending.ExchangeListingId);
			(await listings.GetHeaderAsync(pending.ExchangeListingId)).PayloadJson.Should().BeNull();
			(await listings.GetWithPayloadAsync(pending.ExchangeListingId)).PayloadJson.Should().NotBeNull();
			(await listings.GetForDepartmentAsync(1)).Should().HaveCount(2);

			var move = await listings.GetHeaderAsync(pending.ExchangeListingId);
			move.Status = (int)ExchangeListingStatuses.Listed;
			move.ReviewedOn = DateTime.UtcNow;
			move.ReviewedByUserId = "reviewer";
			move.ListedOn = DateTime.UtcNow;
			(await listings.TryUpdateStatusAsync(move, (int)ExchangeListingStatuses.Rejected)).Should().BeFalse("only from the status the caller read");
			(await listings.TryUpdateStatusAsync(move, (int)ExchangeListingStatuses.PendingReview)).Should().BeTrue();
			(await listings.TryUpdateStatusAsync(move, (int)ExchangeListingStatuses.PendingReview)).Should().BeFalse("decided once");
			(await listings.GetHeaderAsync(pending.ExchangeListingId)).ReviewedByUserId.Should().Be("reviewer");

			await ratings.InsertAsync(new ExchangeRating { ExchangeListingId = listed.ExchangeListingId, DepartmentId = 2, UserId = "Member-A", Stars = 5, CreatedOn = DateTime.UtcNow }, CancellationToken.None, true);
			await ratings.InsertAsync(new ExchangeRating { ExchangeListingId = listed.ExchangeListingId, DepartmentId = 3, UserId = "member-b", Stars = 2, CreatedOn = DateTime.UtcNow }, CancellationToken.None, true);
			Func<Task> twice = () => ratings.InsertAsync(new ExchangeRating { ExchangeListingId = listed.ExchangeListingId, DepartmentId = 2, UserId = "member-a", Stars = 1, CreatedOn = DateTime.UtcNow },
				CancellationToken.None, true);
			await twice.Should().ThrowAsync<Exception>("one rating per member, ids compared case-insensitively");
			(await ratings.GetForListingAndUserAsync(listed.ExchangeListingId, "MEMBER-A")).Stars.Should().Be(5);
			await listings.RefreshRatingAsync(listed.ExchangeListingId);
			await listings.IncrementImportCountAsync(listed.ExchangeListingId);
			var counted = await listings.GetHeaderAsync(listed.ExchangeListingId);
			counted.RatingCount.Should().Be(2);
			counted.RatingTotal.Should().Be(7);
			counted.ImportCount.Should().Be(1);
			(await listings.BrowseAsync(new ExchangeBrowseQuery { Sort = "rating" })).Items.Should().NotBeEmpty();
			(await listings.BrowseAsync(new ExchangeBrowseQuery { Sort = "imports" })).Items.First().ExchangeListingId.Should().Be(listed.ExchangeListingId);

			var comment = await comments.InsertAsync(new ExchangeComment { ExchangeListingId = listed.ExchangeListingId, DepartmentId = 2, UserId = "member-a", AuthorName = "Dept 2", Body = "Works for us", CreatedOn = DateTime.UtcNow },
				CancellationToken.None, true);
			(await comments.TryHideAsync(comment.ExchangeCommentId, "reviewer", DateTime.UtcNow)).Should().BeTrue();
			(await comments.TryHideAsync(comment.ExchangeCommentId, "reviewer", DateTime.UtcNow)).Should().BeFalse();
			(await comments.GetForListingAsync(listed.ExchangeListingId, false)).Should().BeEmpty();
			(await comments.GetForListingAsync(listed.ExchangeListingId, true)).Should().ContainSingle();
			(await comments.GetCommentAsync(comment.ExchangeCommentId)).HiddenOn.Should().NotBeNull();

			await imports.InsertAsync(new ExchangeImport { ExchangeListingId = listed.ExchangeListingId, DepartmentId = 2, UserId = "member-a", ItemKind = 0, CreatedItemId = 77, ImportedOn = DateTime.UtcNow },
				CancellationToken.None, true);
			(await imports.GetForDepartmentAsync(2)).Single().CreatedItemId.Should().Be(77);
		}

		[Test, Order(7)]
		public async Task The_analytics_read_takes_only_the_departments_protocols_on_calls_logged_in_the_period()
		{
			var inRange = new DateTime(2026, 11, 10, 12, 0, 0);
			await using (var database = Connect(_connection))
			{
				await database.ExecuteAsync($@"
INSERT INTO {T("DispatchProtocols")} ({T("DepartmentId")}, {T("Name")}) VALUES (1, 'Breathing'), (2, 'Theirs');
INSERT INTO {T("Calls")} ({T("DepartmentId")}, {T("LoggedOn")}) VALUES (1, @In), (1, @Out), (2, @In);", new { In = inRange, Out = inRange.AddDays(-60) });
				var log = new ProtocolStepLog { StartedOn = inRange, CompletedOn = inRange.AddMinutes(7) }.ToJson();
				await database.ExecuteAsync($@"
INSERT INTO {T("CallProtocols")} ({T("CallId")}, {T("DispatchProtocolId")}, {T("Source")}, {T("ProtocolVersionId")}, {T("Determinant")}, {T("OverrideReason")}, {T("StepLogJson")})
VALUES (1, 1, 1, 11, '06-D-1', 'Caller unreliable', @Log), (2, 1, 0, 11, NULL, NULL, NULL), (3, 2, 0, NULL, NULL, NULL, NULL), (3, 1, 0, NULL, NULL, NULL, NULL);", new { Log = log });
			}

			var repository = Repository((c, s, u, q) => new ProtocolAnalyticsRepository(c, s, u, q));
			var rows = await repository.GetCallUsageAsync(1, inRange.AddDays(-1), inRange.AddDays(1), 100);

			rows.Should().ContainSingle("the call out of range, the other department's call and its protocol are left out");
			var row = rows.Single();
			row.Determinant.Should().Be("06-D-1");
			row.OverrideReason.Should().Be("Caller unreliable");
			row.Source.Should().Be(1);
			row.ProtocolVersionId.Should().Be(11);
			ProtocolAnalytics.MinutesOnProtocol(row.StepLogJson).Should().BeApproximately(7, 0.001);
		}

		[Test, Order(8)]
		public async Task The_department_cleanup_removes_the_departments_own_rows_and_leaves_other_departments_rows()
		{
			var listings = Repository((c, s, u, q) => new ExchangeListingsRepository(c, s, u, q));
			var ratings = Repository((c, s, u, q) => new ExchangeRatingsRepository(c, s, u, q));
			var mine = await listings.InsertAsync(Listing(5, (int)ExchangeListingStatuses.Listed, "Five's"), CancellationToken.None, true);
			var other = await listings.InsertAsync(Listing(6, (int)ExchangeListingStatuses.Listed, "Six's"), CancellationToken.None, true);
			await ratings.InsertAsync(new ExchangeRating { ExchangeListingId = mine.ExchangeListingId, DepartmentId = 6, UserId = "six", Stars = 4, CreatedOn = DateTime.UtcNow }, CancellationToken.None, true);
			await ratings.InsertAsync(new ExchangeRating { ExchangeListingId = other.ExchangeListingId, DepartmentId = 5, UserId = "five", Stars = 4, CreatedOn = DateTime.UtcNow }, CancellationToken.None, true);
			await ratings.InsertAsync(new ExchangeRating { ExchangeListingId = other.ExchangeListingId, DepartmentId = 7, UserId = "seven", Stars = 3, CreatedOn = DateTime.UtcNow }, CancellationToken.None, true);
			await Repository((c, s, u, q) => new ContentAttributionsRepository(c, s, u, q)).SaveForSubjectAsync(new ContentAttribution
			{
				DepartmentId = 5, SubjectKind = 1, SubjectId = 9, SourceName = "x", CreatedOn = DateTime.UtcNow
			});
			await Repository((c, s, u, q) => new ProtocolSearchMissesRepository(c, s, u, q)).RecordAsync(5, ProtocolSearchSurfaces.Library, "hazmat", DateTime.UtcNow);
			await Repository((c, s, u, q) => new ProtocolPublicLinksRepository(c, s, u, q)).InsertAsync(new ProtocolPublicLink
			{
				DepartmentId = 5, Kind = 0, DispatchProtocolId = 1, Token = "five5five5five5five5five5five5fi", CreatedOn = DateTime.UtcNow
			}, CancellationToken.None, true);

			await using var database = Connect(_connection);
			await database.OpenAsync();
			await using (var transaction = await database.BeginTransactionAsync())
			{
				await UnlinkedDepartmentCleanup.DeleteWithinTransactionAsync(database, transaction, 5, type);
				await transaction.CommitAsync();
			}

			foreach (var table in new[] { "ProtocolPublicLinks", "ContentAttributions", "ProtocolSearchMisses", "ExchangeListings", "ExchangeRatings" })
				(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T(table)} WHERE {T("DepartmentId")} = 5")).Should().Be(0, table);
			(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("ExchangeRatings")} WHERE {T("ExchangeListingId")} = @Id", new { Id = mine.ExchangeListingId }))
				.Should().Be(1, "another department's rating stays its own row (nothing shows it once the listing is gone)");
			(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("ExchangeListings")} WHERE {T("DepartmentId")} = 6")).Should().Be(1);
			(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("ExchangeRatings")} WHERE {T("DepartmentId")} = 7")).Should().Be(1);
		}
	}
}
