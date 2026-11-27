using System;
using System.Collections.Concurrent;
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
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Queries;
using Resgrid.Repositories.DataRepository.Queries.Common;
using Resgrid.Repositories.DataRepository.Servers.PostgreSql;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;

namespace Resgrid.Tests.Repositories
{
	/// <summary>
	/// Real-database proof for Protocols P1 and the signing layer (S0) on both engines: M0341-M0343 over P0 data (every protocol
	/// gets version 1.0 from its text, older dispatched text becomes legacy versions, call rows cite the version they were
	/// dispatched with, rows without a hash cite 1.0), re-running Up on the migrated schema changes nothing, Down removes it all
	/// and Up again rebuilds the same mapping. Then the repository queries worker 84 and the lifecycle rely on: the conditional
	/// row-version update (a stale save changes nothing), the due-work queries (only due rows; no department sweep), the review
	/// reminder claim, the purge candidates (never a version a call cites, never under legal hold) and signature records.
	/// Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION (server-level connections) to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class ProtocolLibraryDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "protocol_p1_";
		private const string SizeUp = "<p>Size up</p>";
		private const string Cpr = "<p>CPR</p>";
		private const string OldCpr = "<p>Old CPR</p>";
		private DatabaseTypes _previous;
		private string _master, _connection, _database;

		private bool IsPostgres => type == DatabaseTypes.Postgres;

		private DbConnection Connect(string connection) => IsPostgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);

		private string T(string name) => IsPostgres ? name.ToLowerInvariant() : "[" + name + "]";

		private T2 Repository<T2>(Func<IConnectionProvider, SqlConfiguration, IUnitOfWork, IQueryFactory, T2> build)
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

		private IMigration[] P0() => IsPostgres
			? new IMigration[] { new Resgrid.Providers.MigrationsPg.Migrations.M0336_ProtocolFoundationPg(), new Resgrid.Providers.MigrationsPg.Migrations.M0337_ProtocolSnapshotsAndUniqueCallProtocolsPg() }
			: new IMigration[] { new Resgrid.Providers.Migrations.Migrations.M0336_ProtocolFoundation(), new Resgrid.Providers.Migrations.Migrations.M0337_ProtocolSnapshotsAndUniqueCallProtocols() };

		private IMigration[] P1() => IsPostgres
			? new IMigration[]
			{
				new Resgrid.Providers.MigrationsPg.Migrations.M0341_AddSignatureRecordsPg(), new Resgrid.Providers.MigrationsPg.Migrations.M0342_AddProtocolLibraryAndVersionsPg(),
				new Resgrid.Providers.MigrationsPg.Migrations.M0343_BackfillProtocolVersionsPg(),
				// e-Signature S2 (M0358): SignatureRecords gained the external-signer columns the entity now writes.
				new Resgrid.Providers.MigrationsPg.Migrations.M0358_AddExternalSignatureColumnsPg(),
				// Round 3 A: the version repository reads and writes ContentKind and QuizTrainingId (M0347, M0348).
				new Resgrid.Providers.MigrationsPg.Migrations.M0347_AddProtocolDocumentsAndFileTextPg(), new Resgrid.Providers.MigrationsPg.Migrations.M0348_AddProtocolAcknowledgmentsPg()
			}
			: new IMigration[]
			{
				new Resgrid.Providers.Migrations.Migrations.M0341_AddSignatureRecords(), new Resgrid.Providers.Migrations.Migrations.M0342_AddProtocolLibraryAndVersions(),
				new Resgrid.Providers.Migrations.Migrations.M0343_BackfillProtocolVersions(),
				// e-Signature S2 (M0358): SignatureRecords gained the external-signer columns the entity now writes.
				new Resgrid.Providers.Migrations.Migrations.M0358_AddExternalSignatureColumns(),
				new Resgrid.Providers.Migrations.Migrations.M0347_AddProtocolDocumentsAndFileText(), new Resgrid.Providers.Migrations.Migrations.M0348_AddProtocolAcknowledgments()
			};

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

			// The M0001 shape of the tables P0 and P1 change, with P0-era rows written before the migrations.
			await using (var database = Connect(_connection))
			{
				await database.OpenAsync();
				await database.ExecuteAsync(IsPostgres ? @"
CREATE EXTENSION IF NOT EXISTS citext;
CREATE TABLE departments (departmentid serial PRIMARY KEY, name citext, timezone citext);
CREATE TABLE departmentsettings (departmentsettingid serial PRIMARY KEY, departmentid integer NOT NULL, settingtype integer NOT NULL, setting citext);
CREATE TABLE calltypes (calltypeid serial PRIMARY KEY, departmentid integer NOT NULL, type citext);
CREATE TABLE calls (callid serial PRIMARY KEY, departmentid integer NOT NULL, loggedon timestamp NOT NULL, number citext, name citext);
CREATE TABLE dispatchprotocols (dispatchprotocolid serial PRIMARY KEY, departmentid integer NOT NULL REFERENCES departments (departmentid), name citext NOT NULL, code citext NOT NULL,
	isdisabled boolean NOT NULL DEFAULT false, isdeleted boolean NOT NULL DEFAULT false, deletedon timestamp NULL, description citext, protocoltext citext,
	createdon timestamp NOT NULL DEFAULT now(), createdbyuserid citext NOT NULL DEFAULT 'u', updatedon timestamp, minimumweight integer NOT NULL DEFAULT 0, updatedbyuserid citext NOT NULL DEFAULT 'u');
CREATE TABLE dispatchprotocoltriggers (dispatchprotocoltriggerid serial PRIMARY KEY, dispatchprotocolid integer NOT NULL REFERENCES dispatchprotocols (dispatchprotocolid), type integer NOT NULL,
	startson timestamp, endson timestamp, priority integer, calltype citext, geofence citext);
CREATE TABLE dispatchprotocolquestions (dispatchprotocolquestionid serial PRIMARY KEY, dispatchprotocolid integer NOT NULL REFERENCES dispatchprotocols (dispatchprotocolid), question citext);
CREATE TABLE dispatchprotocolquestionanswers (dispatchprotocolquestionanswerid serial PRIMARY KEY, dispatchprotocolquestionid integer NOT NULL REFERENCES dispatchprotocolquestions (dispatchprotocolquestionid), answer citext, weight integer NOT NULL);
CREATE TABLE dispatchprotocolattachments (dispatchprotocolattachmentid serial PRIMARY KEY, dispatchprotocolid integer NOT NULL REFERENCES dispatchprotocols (dispatchprotocolid), filename citext, filetype citext, data bytea);
CREATE TABLE callprotocols (callprotocolid serial PRIMARY KEY, callid integer NOT NULL, dispatchprotocolid integer NOT NULL REFERENCES dispatchprotocols (dispatchprotocolid), score integer NOT NULL, trigger integer NOT NULL, data citext);
INSERT INTO departments (name, timezone) VALUES ('One', 'America/New_York'), ('Two', 'Eastern Standard Time');
" : @"
CREATE TABLE Departments (DepartmentId int IDENTITY(1,1) PRIMARY KEY, Name nvarchar(100), TimeZone nvarchar(100));
CREATE TABLE DepartmentSettings (DepartmentSettingId int IDENTITY(1,1) PRIMARY KEY, DepartmentId int NOT NULL, SettingType int NOT NULL, Setting nvarchar(max));
CREATE TABLE CallTypes (CallTypeId int IDENTITY(1,1) PRIMARY KEY, DepartmentId int NOT NULL, Type nvarchar(max));
CREATE TABLE Calls (CallId int IDENTITY(1,1) PRIMARY KEY, DepartmentId int NOT NULL, LoggedOn datetime NOT NULL, Number nvarchar(50), Name nvarchar(max));
CREATE TABLE DispatchProtocols (DispatchProtocolId int IDENTITY(1,1) PRIMARY KEY, DepartmentId int NOT NULL REFERENCES Departments (DepartmentId), Name nvarchar(100) NOT NULL, Code nvarchar(4) NOT NULL,
	IsDisabled bit NOT NULL DEFAULT 0, IsDeleted bit NOT NULL DEFAULT 0, DeletedOn datetime2 NULL, Description nvarchar(500), ProtocolText nvarchar(max),
	CreatedOn datetime NOT NULL DEFAULT GETUTCDATE(), CreatedByUserId nvarchar(max) NOT NULL DEFAULT 'u', UpdatedOn datetime, MinimumWeight int NOT NULL DEFAULT 0, UpdatedByUserId nvarchar(max) NOT NULL DEFAULT 'u');
CREATE TABLE DispatchProtocolTriggers (DispatchProtocolTriggerId int IDENTITY(1,1) PRIMARY KEY, DispatchProtocolId int NOT NULL REFERENCES DispatchProtocols (DispatchProtocolId), Type int NOT NULL,
	StartsOn datetime, EndsOn datetime, Priority int, CallType nvarchar(max), Geofence nvarchar(max));
CREATE TABLE DispatchProtocolQuestions (DispatchProtocolQuestionId int IDENTITY(1,1) PRIMARY KEY, DispatchProtocolId int NOT NULL REFERENCES DispatchProtocols (DispatchProtocolId), Question nvarchar(max));
CREATE TABLE DispatchProtocolQuestionAnswers (DispatchProtocolQuestionAnswerId int IDENTITY(1,1) PRIMARY KEY, DispatchProtocolQuestionId int NOT NULL REFERENCES DispatchProtocolQuestions (DispatchProtocolQuestionId), Answer nvarchar(max), Weight int NOT NULL);
CREATE TABLE DispatchProtocolAttachments (DispatchProtocolAttachmentId int IDENTITY(1,1) PRIMARY KEY, DispatchProtocolId int NOT NULL REFERENCES DispatchProtocols (DispatchProtocolId), FileName nvarchar(max), FileType nvarchar(max), Data varbinary(max));
CREATE TABLE CallProtocols (CallProtocolId int IDENTITY(1,1) PRIMARY KEY, CallId int NOT NULL, DispatchProtocolId int NOT NULL REFERENCES DispatchProtocols (DispatchProtocolId), Score int NOT NULL, [Trigger] int NOT NULL, Data nvarchar(max));
INSERT INTO Departments (Name, TimeZone) VALUES ('One', 'Eastern Standard Time'), ('Two', 'Eastern Standard Time');
");
				if (database is NpgsqlConnection postgres)
					await postgres.ReloadTypesAsync();

				await database.ExecuteAsync($@"
INSERT INTO {T("Calls")} ({T("DepartmentId")}, {T("LoggedOn")}) VALUES (1, @Recent), (1, @Recent), (1, @Recent), (1, @Recent), (2, @Recent);
INSERT INTO {T("DispatchProtocols")} ({T("DepartmentId")}, {T("Name")}, {T("Code")}, {T("ProtocolText")}) VALUES (1, 'Fire', 'SF', @SizeUp), (1, 'Cardiac', 'CA', @Cpr), (2, 'Theirs', 'TH', '<p>x</p>');",
					new { Recent = DateTime.UtcNow.AddDays(-2), SizeUp, Cpr });
			}

			Migrate(P0(), r => r.MigrateUp());

			// P0-era rows: call 1 dispatched Fire with today's text, call 2 before content hashes existed, call 3 Cardiac with an
			// older text (its snapshot), call 4 Cardiac with today's text, call 5 another department's protocol.
			await using (var database = Connect(_connection))
			{
				await database.ExecuteAsync($@"
INSERT INTO {T("DispatchProtocolSnapshots")} ({T("DepartmentId")}, {T("DispatchProtocolId")}, {T("ContentHash")}, {T("ProtocolText")}, {T("FirstUsedOn")}) VALUES
	(1, 1, @SizeUpHash, @SizeUp, @Earlier), (1, 2, @OldHash, @OldCpr, @Earlier), (1, 2, @CprHash, @Cpr, @Recent);
INSERT INTO {T("CallProtocols")} ({T("CallId")}, {T("DispatchProtocolId")}, {T("Score")}, {T("Trigger")}, {T("Data")}, {T("ContentHash")}) VALUES
	(1, 1, 0, 0, 'A', @SizeUpHash), (2, 1, 0, 0, 'B', NULL), (3, 2, 0, 0, 'C', @OldHash), (4, 2, 0, 0, 'D', @CprHash), (5, 3, 0, 0, 'E', NULL);",
					new
					{
						SizeUp, Cpr, OldCpr, Earlier = DateTime.UtcNow.AddDays(-60), Recent = DateTime.UtcNow.AddDays(-2),
						SizeUpHash = ProtocolContentHash.Compute(SizeUp), CprHash = ProtocolContentHash.Compute(Cpr), OldHash = ProtocolContentHash.Compute(OldCpr)
					});
			}

			Migrate(P1(), r => r.MigrateUp());
		}

		[OneTimeTearDown]
		public async Task Remove_only_this_fixture_database()
		{
			if (_database == null) return;
			DataConfig.DatabaseType = _previous;
			if (IsPostgres) NpgsqlConnection.ClearAllPools(); else SqlConnection.ClearAllPools();
			await using var master = Connect(_master);
			await master.ExecuteAsync(IsPostgres ? $"DROP DATABASE IF EXISTS {_database} WITH (FORCE)" : $"ALTER DATABASE {_database} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {_database}");
		}

		private sealed class VersionRow
		{
			public int ProtocolVersionId { get; set; }
			public int DispatchProtocolId { get; set; }
			public int Major { get; set; }
			public int Minor { get; set; }
			public int Status { get; set; }
			public string ContentHash { get; set; }
			public string ContentHtml { get; set; }
		}

		private sealed class Mapping
		{
			public string Data { get; set; }
			public int? ProtocolVersionId { get; set; }
		}

		private async Task<(List<VersionRow> Versions, Dictionary<string, int?> Calls, List<(int Id, int Status, int? Current)> Protocols)> ReadBackfillAsync()
		{
			await using var database = Connect(_connection);
			var versions = (await database.QueryAsync<VersionRow>(
				$"SELECT {T("ProtocolVersionId")} AS ProtocolVersionId, {T("DispatchProtocolId")} AS DispatchProtocolId, {T("Major")} AS Major, {T("Minor")} AS Minor, " +
				$"{T("Status")} AS Status, {T("ContentHash")} AS ContentHash, {T("ContentHtml")} AS ContentHtml FROM {T("ProtocolVersions")} " +
				$"WHERE {T("ChangeSummary")} IN (@First, @Legacy) ORDER BY {T("ProtocolVersionId")}",
				new
				{
					First = "Version 1.0: the protocol's text when protocol versions were introduced.",
					Legacy = "Text a call was dispatched with before protocol versions were introduced."
				})).ToList();
			var calls = (await database.QueryAsync<Mapping>($"SELECT {T("Data")} AS Data, {T("ProtocolVersionId")} AS ProtocolVersionId FROM {T("CallProtocols")}"))
				.ToDictionary(x => x.Data, x => x.ProtocolVersionId);
			var protocols = (await database.QueryAsync<(int, int, int?)>(
				$"SELECT {T("DispatchProtocolId")}, {T("Status")}, {T("CurrentVersionId")} FROM {T("DispatchProtocols")} ORDER BY {T("DispatchProtocolId")}")).ToList();
			return (versions, calls, protocols);
		}

		private static void ShouldBeTheBackfill((List<VersionRow> Versions, Dictionary<string, int?> Calls, List<(int Id, int Status, int? Current)> Protocols) state)
		{
			var fire = state.Versions.Where(v => v.DispatchProtocolId == 1).ToList();
			var cardiac = state.Versions.Where(v => v.DispatchProtocolId == 2).ToList();

			var fireFirst = fire.Should().ContainSingle("Fire's only dispatched text is its text today").Subject;
			fireFirst.Major.Should().Be(1);
			fireFirst.Minor.Should().Be(0);
			fireFirst.Status.Should().Be((int)ProtocolVersionStatuses.Published);
			fireFirst.ContentHtml.Should().Be(SizeUp, "1.0 is the stored text exactly, so the P0 snapshot hash still matches it");
			fireFirst.ContentHash.Should().Be(ProtocolContentHash.Compute(SizeUp));

			cardiac.Should().HaveCount(2);
			var cardiacFirst = cardiac.Single(v => v.Major == 1);
			var legacy = cardiac.Single(v => v.Major == 0);
			cardiacFirst.Status.Should().Be((int)ProtocolVersionStatuses.Published);
			legacy.Status.Should().Be((int)ProtocolVersionStatuses.Legacy);
			legacy.Minor.Should().Be(1);
			legacy.ContentHtml.Should().Be(OldCpr);
			legacy.ContentHash.Should().Be(ProtocolContentHash.Compute(OldCpr));

			state.Calls["A"].Should().Be(fireFirst.ProtocolVersionId, "the hash it was dispatched with is 1.0's");
			state.Calls["B"].Should().Be(fireFirst.ProtocolVersionId, "a row from before content hashes cites 1.0");
			state.Calls["C"].Should().Be(legacy.ProtocolVersionId, "an older dispatched text cites its legacy version");
			state.Calls["D"].Should().Be(cardiacFirst.ProtocolVersionId);
			state.Calls["E"].Should().NotBeNull("another department's protocol is backfilled the same way");

			state.Protocols.Where(p => p.Id <= 2).Should().OnlyContain(p => p.Status == (int)ProtocolStatuses.Published);
			state.Protocols.Single(p => p.Id == 1).Current.Should().Be(fireFirst.ProtocolVersionId);
			state.Protocols.Single(p => p.Id == 2).Current.Should().Be(cardiacFirst.ProtocolVersionId);
		}

		[Test, Order(1)]
		public async Task The_backfill_gives_every_protocol_1_0_legacy_text_its_own_version_and_calls_the_version_they_were_dispatched_with()
		{
			ShouldBeTheBackfill(await ReadBackfillAsync());
		}

		[Test, Order(2)]
		public async Task Up_again_on_the_migrated_schema_changes_nothing()
		{
			await using (var database = Connect(_connection))
				// FluentMigrator's version table keeps its quoted mixed-case name on PostgreSQL.
				await database.ExecuteAsync(IsPostgres
					? "DELETE FROM \"VersionInfo\" WHERE \"Version\" IN (341, 342, 343)"
					: "DELETE FROM [VersionInfo] WHERE [Version] IN (341, 342, 343)");

			Migrate(P1(), r => r.MigrateUp());

			var state = await ReadBackfillAsync();
			state.Versions.Should().HaveCount(4, "1.0 for each of the three protocols and Cardiac's one legacy text, not twice");
			ShouldBeTheBackfill(state);
		}

		[Test, Order(3)]
		public async Task Down_removes_the_library_and_up_again_rebuilds_the_same_mapping()
		{
			Migrate(P1(), r => r.MigrateDown(340));

			await using (var database = Connect(_connection))
			{
				var exists = IsPostgres
					? "SELECT COUNT(*) FROM information_schema.tables WHERE table_name IN ('signaturerecords', 'protocolversions', 'protocolcategories', 'protocolaudiences', 'protocolcomments', 'protocolversionapprovals')"
					: "SELECT COUNT(*) FROM sys.tables WHERE name IN ('SignatureRecords', 'ProtocolVersions', 'ProtocolCategories', 'ProtocolAudiences', 'ProtocolComments', 'ProtocolVersionApprovals')";
				(await database.ExecuteScalarAsync<int>(exists)).Should().Be(0);
				var column = IsPostgres
					? "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'callprotocols' AND column_name = 'protocolversionid'"
					: "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.CallProtocols') AND name = 'ProtocolVersionId'";
				(await database.ExecuteScalarAsync<int>(column)).Should().Be(0);
				(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("CallProtocols")}")).Should().Be(5, "Down never removes what calls recorded");
				(await database.ExecuteScalarAsync<string>($"SELECT {T("ProtocolText")} FROM {T("DispatchProtocols")} WHERE {T("DispatchProtocolId")} = 1")).Should().Be(SizeUp);
			}

			Migrate(P1(), r => r.MigrateUp());
			ShouldBeTheBackfill(await ReadBackfillAsync());
		}

		private ProtocolVersionsRepository Versions() => Repository((c, s, u, q) => new ProtocolVersionsRepository(c, s, u, q));

		private ProtocolLibraryRepository Library() => Repository((c, s, u, q) => new ProtocolLibraryRepository(c, s, u, q));

		private async Task<ProtocolVersion> AddVersionAsync(int departmentId, int protocolId, ProtocolVersionStatuses status, DateTime? effective = null, DateTime? expires = null,
			DateTime? superseded = null)
		{
			var html = "<p>Version " + Guid.NewGuid().ToString("N") + "</p>";
			return await Versions().InsertAsync(new ProtocolVersion
			{
				DepartmentId = departmentId,
				DispatchProtocolId = protocolId,
				Major = 9,
				Minor = 0,
				Status = (int)status,
				EffectiveOn = effective,
				ExpiresOn = expires,
				SupersededOn = superseded,
				ContentJson = ProtocolContentDocument.FromHtml(html).ToJson(),
				ContentHtml = html,
				ContentHash = ProtocolContentHash.Compute(html),
				CreatedOn = DateTime.UtcNow,
				CreatedByUserId = "author",
				RowVersion = 0
			}, CancellationToken.None, true);
		}

		[Test, Order(10)]
		public async Task A_save_against_an_older_row_version_changes_nothing()
		{
			var draft = await AddVersionAsync(1, 1, ProtocolVersionStatuses.Draft);
			var mine = await Versions().GetByIdForDepartmentAsync(1, draft.ProtocolVersionId);
			var theirs = await Versions().GetByIdForDepartmentAsync(1, draft.ProtocolVersionId);

			mine.ChangeSummary = "Mine";
			(await Versions().TryUpdateAsync(mine, mine.RowVersion)).Should().BeTrue();
			mine.RowVersion.Should().Be(1);

			theirs.ChangeSummary = "Theirs";
			(await Versions().TryUpdateAsync(theirs, theirs.RowVersion)).Should().BeFalse("the copy was loaded before the other save");
			(await Versions().GetByIdForDepartmentAsync(1, draft.ProtocolVersionId)).ChangeSummary.Should().Be("Mine");

			var other = await Versions().GetByIdForDepartmentAsync(1, draft.ProtocolVersionId);
			other.DepartmentId = 2;
			(await Versions().TryUpdateAsync(other, other.RowVersion)).Should().BeFalse("another department's id never updates the row");
			(await Versions().GetByIdForDepartmentAsync(2, draft.ProtocolVersionId)).Should().BeNull();

			(await Library().TryBumpRowVersionAsync(1, 1, 0)).Should().BeTrue();
			(await Library().TryBumpRowVersionAsync(1, 1, 0)).Should().BeFalse("the protocol moved on");
			(await Library().TryBumpRowVersionAsync(2, 1, 1)).Should().BeFalse("another department");
		}

		[Test, Order(11)]
		public async Task Only_due_scheduled_and_expiring_versions_come_back()
		{
			var now = DateTime.UtcNow;
			var due = await AddVersionAsync(1, 2, ProtocolVersionStatuses.Approved, effective: now.AddMinutes(-5));
			var later = await AddVersionAsync(1, 2, ProtocolVersionStatuses.Approved, effective: now.AddDays(3));
			var unscheduled = await AddVersionAsync(2, 3, ProtocolVersionStatuses.Approved);
			var draft = await AddVersionAsync(2, 3, ProtocolVersionStatuses.Draft, effective: now.AddDays(-1));
			var expiring = await AddVersionAsync(2, 3, ProtocolVersionStatuses.Published, expires: now.AddMinutes(-1));
			var notYet = await AddVersionAsync(1, 1, ProtocolVersionStatuses.Published, expires: now.AddDays(10));

			var activations = (await Versions().GetDueActivationsAsync(now, 50)).Select(v => v.ProtocolVersionId).ToList();
			activations.Should().Contain(due.ProtocolVersionId);
			activations.Should().NotContain(new[] { later.ProtocolVersionId, unscheduled.ProtocolVersionId, draft.ProtocolVersionId });

			var expirations = (await Versions().GetDueExpirationsAsync(now, 50)).Select(v => v.ProtocolVersionId).ToList();
			expirations.Should().Contain(expiring.ProtocolVersionId, "due work is found across departments without listing them");
			expirations.Should().NotContain(notYet.ProtocolVersionId);

			(await Versions().GetDueActivationsAsync(now, 1)).Should().HaveCount(1, "the batch size bounds a run");
		}

		[Test, Order(12)]
		public async Task Review_reminders_come_back_once_per_stage_and_only_when_due()
		{
			var now = DateTime.UtcNow;
			var soon = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0).AddDays(5);
			await using (var database = Connect(_connection))
			{
				await database.ExecuteAsync($"UPDATE {T("DispatchProtocols")} SET {T("NextReviewOn")} = @Soon, {T("ReviewReminderStage")} = 0, {T("OwnerUserId")} = 'owner' WHERE {T("DispatchProtocolId")} = 1", new { Soon = soon });
				await database.ExecuteAsync($"UPDATE {T("DispatchProtocols")} SET {T("NextReviewOn")} = @Far WHERE {T("DispatchProtocolId")} = 2", new { Far = now.AddDays(90) });
				await database.ExecuteAsync($"UPDATE {T("DispatchProtocols")} SET {T("NextReviewOn")} = @Past, {T("IsDeleted")} = @Deleted WHERE {T("DispatchProtocolId")} = 3",
					new { Past = now.AddDays(-3), Deleted = true });
			}

			var due = await Library().GetReviewRemindersDueAsync(now, 50);
			var item = due.Should().ContainSingle("a review 90 days out and a deleted protocol owe nothing").Subject;
			item.DispatchProtocolId.Should().Be(1);
			item.OwnerUserId.Should().Be("owner");
			item.NextReviewOn.Should().Be(soon);
			ProtocolReviewReminders.DueStage(item.NextReviewOn, now).Should().Be(ProtocolReviewReminderStages.SevenDays);

			(await Library().MarkReviewReminderAsync(1, 1, item.NextReviewOn, (int)ProtocolReviewReminderStages.SevenDays)).Should().BeTrue();
			(await Library().MarkReviewReminderAsync(1, 1, item.NextReviewOn, (int)ProtocolReviewReminderStages.SevenDays)).Should().BeFalse("a second run never claims it again");
			(await Library().MarkReviewReminderAsync(1, 1, item.NextReviewOn.AddDays(1), (int)ProtocolReviewReminderStages.Overdue)).Should().BeFalse("a review stamp moved the date");
			(await Library().GetReviewRemindersDueAsync(now, 50)).Should().BeEmpty("the stage due now was sent");
		}

		[Test, Order(13)]
		public async Task Purge_candidates_are_old_superseded_versions_no_call_cites_and_never_under_legal_hold()
		{
			var now = DateTime.UtcNow;
			var old = await AddVersionAsync(1, 1, ProtocolVersionStatuses.Superseded, superseded: now.AddDays(-400));
			var recent = await AddVersionAsync(1, 1, ProtocolVersionStatuses.Superseded, superseded: now.AddDays(-10));
			var cited = await AddVersionAsync(1, 1, ProtocolVersionStatuses.Superseded, superseded: now.AddDays(-400));
			var held = await AddVersionAsync(1, 2, ProtocolVersionStatuses.Superseded, superseded: now.AddDays(-400));
			await using (var database = Connect(_connection))
			{
				await database.ExecuteAsync($"UPDATE {T("CallProtocols")} SET {T("ProtocolVersionId")} = @Id WHERE {T("Data")} = 'B'", new { Id = cited.ProtocolVersionId });
				await database.ExecuteAsync($"UPDATE {T("DispatchProtocols")} SET {T("LegalHold")} = @Hold WHERE {T("DispatchProtocolId")} = 2", new { Hold = true });
			}

			var candidates = (await Versions().GetPurgeCandidatesAsync(1, now.AddDays(-365), 50)).Select(v => v.ProtocolVersionId).ToList();
			candidates.Should().Contain(old.ProtocolVersionId);
			candidates.Should().NotContain(new[] { recent.ProtocolVersionId, cited.ProtocolVersionId, held.ProtocolVersionId });
			(await Versions().GetPurgeCandidatesAsync(2, now.AddDays(-365), 50)).Should().NotContain(v => v.ProtocolVersionId == old.ProtocolVersionId, "another department");

			(await Versions().PurgeAsync(1, cited.ProtocolVersionId)).Should().Be(0, "a version a call cites is never deleted, whatever asked");
			(await Versions().PurgeAsync(1, old.ProtocolVersionId)).Should().Be(1);
			(await Versions().GetByIdForDepartmentAsync(1, old.ProtocolVersionId)).Should().BeNull();
		}

		[Test, Order(14)]
		public async Task Retention_work_is_found_through_departments_that_set_retention()
		{
			await using (var database = Connect(_connection))
				await database.ExecuteAsync($"INSERT INTO {T("DepartmentSettings")} ({T("DepartmentId")}, {T("SettingType")}, {T("Setting")}) VALUES (1, @Type, 'x'), (2, @Type, 'x')",
					new { Type = (int)DepartmentSettingTypes.ProtocolLibrarySettings });

			var departments = await Library().GetDepartmentsWithRetentionWorkAsync(50);
			departments.Should().Contain(1, "it has superseded versions");
			departments.Should().NotContain(2, "nothing there has ended");
		}

		[Test, Order(15)]
		public async Task Signature_records_round_trip_and_stay_in_their_department()
		{
			var signatures = Repository((c, s, u, q) => new SignatureRecordsRepository(c, s, u, q));
			var hash = ProtocolContentHash.Compute(SizeUp);
			var saved = await signatures.InsertAsync(new SignatureRecord
			{
				DepartmentId = 1,
				SubjectKind = (int)SignatureSubjectKinds.ProtocolVersion,
				SubjectId = "77",
				RevisionId = "round-1/step-1",
				SignerUserId = "approver",
				SignerNameSnapshot = "A. Approver",
				SignerRoleSnapshot = "Medical Director",
				Intent = (int)SignatureIntents.Approve,
				StatementVersion = "protocol-approval/1",
				StatementText = "I approve this protocol version.",
				Level = (int)SignatureLevels.StepUp,
				Method = (int)SignatureMethods.StepUp,
				ArtifactSha256 = hash,
				SignedOn = DateTime.UtcNow,
				CreatedOn = DateTime.UtcNow
			}, CancellationToken.None, true);

			saved.SignatureRecordId.Should().BePositive();
			var found = (await signatures.GetForSubjectAsync(1, (int)SignatureSubjectKinds.ProtocolVersion, "77")).Should().ContainSingle().Subject;
			found.ArtifactSha256.Should().Be(hash);
			found.Level.Should().Be((int)SignatureLevels.StepUp);
			(await signatures.GetForSubjectAsync(2, (int)SignatureSubjectKinds.ProtocolVersion, "77")).Should().BeEmpty();
			(await signatures.GetByIdForDepartmentAsync(2, saved.SignatureRecordId)).Should().BeNull();
		}

		[Test, Order(99)]
		public async Task The_department_cleanup_clears_the_library_and_leaves_other_departments()
		{
			await using var database = Connect(_connection);
			await database.OpenAsync();
			await using (var transaction = await database.BeginTransactionAsync())
			{
				await ProtocolDepartmentCleanup.DeleteWithinTransactionAsync(database, transaction, 1, type);
				await transaction.CommitAsync();
			}

			(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("ProtocolVersions")} WHERE {T("DepartmentId")} = 1")).Should().Be(0);
			(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("DispatchProtocols")} WHERE {T("DepartmentId")} = 1")).Should().Be(0);
			(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("ProtocolVersions")} WHERE {T("DepartmentId")} = 2")).Should().BeGreaterThan(0);
			(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("CallProtocols")} WHERE {T("CallId")} = 5")).Should().Be(1);
		}
	}
}
