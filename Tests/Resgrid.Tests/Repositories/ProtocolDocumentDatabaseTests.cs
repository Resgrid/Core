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
	/// Real-database proof for round 3 A on both engines: M0353 (document-backed versions, file text, the Documents redirect columns) and
	/// M0354 (acknowledgments) over the P0/P1 schema, Up again is a no-op, Down removes them and Up rebuilds them. Then the repositories:
	/// file metadata never carries the bytes, the server-side copy, a submitted version's file cannot be deleted, extracted pages and
	/// the search text of the version in force only, the claim of a queued extraction, attachments without text, the unique
	/// (version, member) acknowledgment with case-insensitive members, the conditional status update, superseding, the reminder
	/// due-work query and its claim, the must-read query, the retention purge taking acknowledgments and files with the version, and the
	/// department cleanup. Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class ProtocolDocumentDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "protocol_p2_";
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

		private IMigration[] Base() => IsPostgres
			? new IMigration[]
			{
				new Resgrid.Providers.MigrationsPg.Migrations.M0336_ProtocolFoundationPg(), new Resgrid.Providers.MigrationsPg.Migrations.M0337_ProtocolSnapshotsAndUniqueCallProtocolsPg(),
				new Resgrid.Providers.MigrationsPg.Migrations.M0342_AddSignatureRecordsPg(), new Resgrid.Providers.MigrationsPg.Migrations.M0343_AddProtocolLibraryAndVersionsPg(),
				new Resgrid.Providers.MigrationsPg.Migrations.M0344_BackfillProtocolVersionsPg()
			}
			: new IMigration[]
			{
				new Resgrid.Providers.Migrations.Migrations.M0336_ProtocolFoundation(), new Resgrid.Providers.Migrations.Migrations.M0337_ProtocolSnapshotsAndUniqueCallProtocols(),
				new Resgrid.Providers.Migrations.Migrations.M0342_AddSignatureRecords(), new Resgrid.Providers.Migrations.Migrations.M0343_AddProtocolLibraryAndVersions(),
				new Resgrid.Providers.Migrations.Migrations.M0344_BackfillProtocolVersions()
			};

		private IMigration[] Round3() => IsPostgres
			? new IMigration[] { new Resgrid.Providers.MigrationsPg.Migrations.M0353_AddProtocolDocumentsAndFileTextPg(), new Resgrid.Providers.MigrationsPg.Migrations.M0354_AddProtocolAcknowledgmentsPg() }
			: new IMigration[] { new Resgrid.Providers.Migrations.Migrations.M0353_AddProtocolDocumentsAndFileText(), new Resgrid.Providers.Migrations.Migrations.M0354_AddProtocolAcknowledgments() };

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
CREATE TABLE documents (documentid serial PRIMARY KEY, departmentid integer NOT NULL REFERENCES departments (departmentid), name citext NOT NULL, data bytea NOT NULL);
INSERT INTO departments (name, timezone) VALUES ('One', 'America/New_York'), ('Two', 'America/New_York');
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
CREATE TABLE Documents (DocumentId int IDENTITY(1,1) PRIMARY KEY, DepartmentId int NOT NULL REFERENCES Departments (DepartmentId), Name nvarchar(max) NOT NULL, Data varbinary(max) NOT NULL);
INSERT INTO Departments (Name, TimeZone) VALUES ('One', 'Eastern Standard Time'), ('Two', 'Eastern Standard Time');
");
				if (database is NpgsqlConnection postgres)
					await postgres.ReloadTypesAsync();

				await database.ExecuteAsync($@"
INSERT INTO {T("DispatchProtocols")} ({T("DepartmentId")}, {T("Name")}, {T("Code")}, {T("ProtocolText")}) VALUES (1, 'Mayday', 'MD', '<p>Mayday</p>'), (1, 'Bulletin', 'BU', '<p>b</p>'), (2, 'Theirs', 'TH', '<p>x</p>');
INSERT INTO {T("DispatchProtocolAttachments")} ({T("DispatchProtocolId")}, {T("FileName")}, {T("FileType")}, {T("Data")}) VALUES
	(1, 'Annex.PDF', 'application/pdf', @Bytes), (1, 'photo.jpg', 'image/jpeg', @Bytes), (3, 'theirs.docx', 'application/x', @Bytes);
INSERT INTO {T("Documents")} ({T("DepartmentId")}, {T("Name")}, {T("Data")}) VALUES (1, 'Old SOG', @Bytes);", new { Bytes = new byte[] { 1, 2, 3 } });
			}

			Migrate(Base(), r => r.MigrateUp());
			Migrate(Round3(), r => r.MigrateUp());
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

		private async Task<int> CountTablesAsync()
		{
			await using var database = Connect(_connection);
			return await database.ExecuteScalarAsync<int>(IsPostgres
				? "SELECT COUNT(*) FROM information_schema.tables WHERE table_name IN ('protocolversionfiles', 'protocolfiletexts', 'protocolfiletextpages', 'protocolacknowledgments')"
				: "SELECT COUNT(*) FROM sys.tables WHERE name IN ('ProtocolVersionFiles', 'ProtocolFileTexts', 'ProtocolFileTextPages', 'ProtocolAcknowledgments')");
		}

		private async Task<int> CountColumnsAsync()
		{
			await using var database = Connect(_connection);
			return await database.ExecuteScalarAsync<int>(IsPostgres
				? "SELECT COUNT(*) FROM information_schema.columns WHERE (table_name = 'protocolversions' AND column_name IN ('contentkind', 'quiztrainingid')) " +
				  "OR (table_name = 'documents' AND column_name IN ('movedtoprotocolid', 'movedon')) OR (table_name = 'dispatchprotocols' AND column_name IN ('ackduedays', 'ackbundleorder', 'acktrainingid', 'standardstags'))"
				: "SELECT COUNT(*) FROM sys.columns WHERE (object_id = OBJECT_ID('dbo.ProtocolVersions') AND name IN ('ContentKind', 'QuizTrainingId')) " +
				  "OR (object_id = OBJECT_ID('dbo.Documents') AND name IN ('MovedToProtocolId', 'MovedOn')) OR (object_id = OBJECT_ID('dbo.DispatchProtocols') AND name IN ('AckDueDays', 'AckBundleOrder', 'AckTrainingId', 'StandardsTags'))");
		}

		[Test, Order(1)]
		public async Task Up_adds_the_tables_and_columns_and_up_again_changes_nothing()
		{
			(await CountTablesAsync()).Should().Be(4);
			(await CountColumnsAsync()).Should().Be(8);

			await using (var database = Connect(_connection))
				await database.ExecuteAsync(IsPostgres ? "DELETE FROM \"VersionInfo\" WHERE \"Version\" IN (353, 354)" : "DELETE FROM [VersionInfo] WHERE [Version] IN (353, 354)");
			Migrate(Round3(), r => r.MigrateUp());

			(await CountTablesAsync()).Should().Be(4);
			(await CountColumnsAsync()).Should().Be(8);
			await using var check = Connect(_connection);
			(await check.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("ProtocolVersions")} WHERE {T("ContentKind")} <> 0")).Should().Be(0, "every existing version is authored HTML");
		}

		[Test, Order(2)]
		public async Task Down_removes_them_and_up_again_rebuilds_them()
		{
			Migrate(Base().Concat(Round3()).ToArray(), r => r.MigrateDown(344));
			(await CountTablesAsync()).Should().Be(0);
			(await CountColumnsAsync()).Should().Be(0);
			await using (var database = Connect(_connection))
				(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("Documents")}")).Should().Be(1, "Down never removes documents");

			Migrate(Round3(), r => r.MigrateUp());
			(await CountTablesAsync()).Should().Be(4);
			(await CountColumnsAsync()).Should().Be(8);
		}

		private ProtocolVersionsRepository Versions() => Repository((c, s, u, q) => new ProtocolVersionsRepository(c, s, u, q));
		private ProtocolVersionFilesRepository Files() => Repository((c, s, u, q) => new ProtocolVersionFilesRepository(c, s, u, q));
		private ProtocolFileTextsRepository Texts() => Repository((c, s, u, q) => new ProtocolFileTextsRepository(c, s, u, q));
		private ProtocolAcknowledgmentsRepository Acks() => Repository((c, s, u, q) => new ProtocolAcknowledgmentsRepository(c, s, u, q));

		private async Task<ProtocolVersion> AddVersionAsync(int departmentId, int protocolId, ProtocolVersionStatuses status, ProtocolVersionContentKinds kind = ProtocolVersionContentKinds.Html,
			int major = 1, int minor = 0)
		{
			var html = "<p>v" + Guid.NewGuid().ToString("N") + "</p>";
			return await Versions().InsertAsync(new ProtocolVersion
			{
				DepartmentId = departmentId, DispatchProtocolId = protocolId, Major = major, Minor = minor, Status = (int)status, ContentKind = (int)kind,
				ContentHtml = html, ContentJson = ProtocolContentDocument.FromHtml(html).ToJson(), ContentHash = ProtocolContentHash.Compute(html),
				CreatedOn = DateTime.UtcNow, CreatedByUserId = "author"
			}, CancellationToken.None, true);
		}

		private async Task<ProtocolVersionFile> AddFileAsync(ProtocolVersion version, byte[] bytes)
		{
			return await Files().InsertAsync(new ProtocolVersionFile
			{
				DepartmentId = version.DepartmentId, DispatchProtocolId = version.DispatchProtocolId, ProtocolVersionId = version.ProtocolVersionId, FileName = "SOG-1 Mayday.pdf",
				ContentType = ProtocolDocumentRules.PdfContentType, Size = bytes.Length, Sha256 = ProtocolDocumentRules.HashFile(bytes), Data = bytes, CreatedOn = DateTime.UtcNow, CreatedByUserId = "author"
			}, CancellationToken.None, true);
		}

		[Test, Order(10)]
		public async Task A_version_keeps_its_content_kind_and_quiz_and_its_file_metadata_never_carries_the_bytes()
		{
			var version = await AddVersionAsync(1, 1, ProtocolVersionStatuses.Draft, ProtocolVersionContentKinds.Document);
			version.QuizTrainingId = 77;
			(await Versions().TryUpdateAsync(version, version.RowVersion)).Should().BeTrue();
			var headers = await Versions().GetForProtocolAsync(1, 1);
			headers.Single(v => v.ProtocolVersionId == version.ProtocolVersionId).ContentKind.Should().Be((int)ProtocolVersionContentKinds.Document);
			headers.Single(v => v.ProtocolVersionId == version.ProtocolVersionId).QuizTrainingId.Should().Be(77);

			var bytes = Enumerable.Range(0, 5000).Select(i => (byte)(i % 251)).ToArray();
			await AddFileAsync(version, bytes);

			var metadata = await Files().GetMetadataForVersionAsync(1, version.ProtocolVersionId);
			metadata.Data.Should().BeNull();
			metadata.Size.Should().Be(5000);
			metadata.Sha256.Should().Be(ProtocolDocumentRules.HashFile(bytes));
			(await Files().GetWithDataForVersionAsync(1, version.ProtocolVersionId)).Data.Should().Equal(bytes);
			(await Files().GetWithDataForVersionAsync(2, version.ProtocolVersionId)).Should().BeNull("another department never reads it");
			(await Files().GetMetadataForVersionsAsync(1, new[] { version.ProtocolVersionId, 999999 })).Should().ContainSingle();

			// A new draft copies the file server-side; the copy hashes the same.
			var draft = await AddVersionAsync(1, 1, ProtocolVersionStatuses.Draft, ProtocolVersionContentKinds.Document, 0, 0);
			var copy = await Files().CopyToVersionAsync(1, version.ProtocolVersionId, draft.ProtocolVersionId, 1, "author", DateTime.UtcNow);
			copy.Sha256.Should().Be(metadata.Sha256);
			(await Files().GetWithDataForVersionAsync(1, draft.ProtocolVersionId)).Data.Should().Equal(bytes);

			// Only a draft's file can be replaced.
			version.Status = (int)ProtocolVersionStatuses.InReview;
			(await Versions().TryUpdateAsync(version, version.RowVersion)).Should().BeTrue();
			(await Files().DeleteForVersionAsync(1, version.ProtocolVersionId)).Should().Be(0);
			(await Files().DeleteForVersionAsync(1, draft.ProtocolVersionId)).Should().Be(1);
		}

		[Test, Order(11)]
		public async Task Extracted_pages_feed_the_search_text_of_the_version_in_force_only()
		{
			var current = await AddVersionAsync(1, 1, ProtocolVersionStatuses.Published, ProtocolVersionContentKinds.Document, 2, 0);
			var older = await AddVersionAsync(1, 1, ProtocolVersionStatuses.Superseded, ProtocolVersionContentKinds.Document, 1, 5);
			var currentFile = await AddFileAsync(current, new byte[] { 1 });
			var olderFile = await AddFileAsync(older, new byte[] { 2 });

			async Task<ProtocolFileText> TextAsync(int kind, int sourceId, int? versionId, IReadOnlyList<string> pages, ProtocolFileTextStatuses status = ProtocolFileTextStatuses.Extracted)
			{
				var row = await Texts().InsertAsync(new ProtocolFileText
				{
					DepartmentId = 1, DispatchProtocolId = 1, SourceKind = kind, SourceId = sourceId, ProtocolVersionId = versionId, Status = (int)ProtocolFileTextStatuses.Pending, QueuedOn = DateTime.UtcNow
				}, CancellationToken.None, true);
				row.Status = (int)status;
				row.PageCount = pages.Count;
				row.ExtractedOn = DateTime.UtcNow;
				await Texts().SaveResultAsync(row, pages);
				return row;
			}

			var currentText = await TextAsync((int)ProtocolFileSourceKinds.VersionFile, currentFile.ProtocolVersionFileId, current.ProtocolVersionId, new[] { "Current page one LUNAR", "Current page two" });
			await TextAsync((int)ProtocolFileSourceKinds.VersionFile, olderFile.ProtocolVersionFileId, older.ProtocolVersionId, new[] { "Superseded OBSOLETE text" });
			await TextAsync((int)ProtocolFileSourceKinds.Attachment, 1, null, new[] { "Annex page ANNEX" });

			(await Texts().GetPagesAsync(1, currentText.ProtocolFileTextId)).Select(p => p.PageNumber).Should().Equal(1, 2);
			(await Texts().GetForSourceAsync(1, (int)ProtocolFileSourceKinds.VersionFile, currentFile.ProtocolVersionFileId)).PageCount.Should().Be(2);

			var search = await Texts().GetSearchTextAsync(1, 1, current.ProtocolVersionId, 32000);
			search.Should().Contain("LUNAR").And.Contain("ANNEX").And.NotContain("OBSOLETE", "a superseded version's file is not what members read now");
			(await Texts().GetSearchTextAsync(1, 1, current.ProtocolVersionId, 10)).Length.Should().BeLessThanOrEqualTo(10);
			(await Texts().GetSearchTextAsync(2, 1, current.ProtocolVersionId, 32000)).Should().BeEmpty();

			// A new draft copies the text with the file; a replaced draft file takes its text with it.
			var draft = await AddVersionAsync(1, 1, ProtocolVersionStatuses.Draft, ProtocolVersionContentKinds.Document, 0, 0);
			await Texts().CopyAsync(currentText, 4242, draft.ProtocolVersionId, DateTime.UtcNow);
			var copied = await Texts().GetForSourceAsync(1, (int)ProtocolFileSourceKinds.VersionFile, 4242);
			(await Texts().GetPagesAsync(1, copied.ProtocolFileTextId)).Should().HaveCount(2);
			(await Texts().DeleteForSourceAsync(1, (int)ProtocolFileSourceKinds.VersionFile, 4242)).Should().Be(1);
			(await Texts().GetForSourceAsync(1, (int)ProtocolFileSourceKinds.VersionFile, 4242)).Should().BeNull();
		}

		[Test, Order(12)]
		public async Task Queued_extractions_are_claimed_once_and_attachments_without_text_are_found()
		{
			var candidates = await Texts().GetAttachmentsWithoutTextAsync(100);
			// Attachment 1 has text from the previous test; the jpg is not a protocol file; department 2's docx is found too (due work across departments).
			candidates.Select(c => c.FileName).Should().BeEquivalentTo("theirs.docx");
			candidates.Single().DepartmentId.Should().Be(2);

			var queued = await Texts().InsertAsync(new ProtocolFileText
			{
				DepartmentId = 2, DispatchProtocolId = 3, SourceKind = (int)ProtocolFileSourceKinds.Attachment, SourceId = candidates.Single().DispatchProtocolAttachmentId,
				Status = (int)ProtocolFileTextStatuses.Pending, QueuedOn = DateTime.UtcNow
			}, CancellationToken.None, true);

			(await Texts().GetPendingAsync(10)).Select(t => t.ProtocolFileTextId).Should().Contain(queued.ProtocolFileTextId);
			(await Texts().GetAttachmentsWithoutTextAsync(100)).Should().BeEmpty();
			(await Texts().TryClaimAsync(queued.ProtocolFileTextId, 0)).Should().BeTrue();
			(await Texts().TryClaimAsync(queued.ProtocolFileTextId, 0)).Should().BeFalse("a second worker loses the claim");

			Func<Task> duplicate = () => Texts().InsertAsync(new ProtocolFileText
			{
				DepartmentId = 2, DispatchProtocolId = 3, SourceKind = (int)ProtocolFileSourceKinds.Attachment, SourceId = queued.SourceId, Status = 0, QueuedOn = DateTime.UtcNow
			}, CancellationToken.None, true);
			await duplicate.Should().ThrowAsync<Exception>("one extraction per file");
		}

		private ProtocolAcknowledgment Ack(ProtocolVersion version, string userId, ProtocolAcknowledgmentStatuses status = ProtocolAcknowledgmentStatuses.Open, DateTime? due = null) => new ProtocolAcknowledgment
		{
			DepartmentId = version.DepartmentId, DispatchProtocolId = version.DispatchProtocolId, ProtocolVersionId = version.ProtocolVersionId, UserId = userId, Status = (int)status,
			Source = (int)ProtocolAcknowledgmentSources.Publish, AssignedOn = DateTime.UtcNow.AddDays(-20), DueOn = due
		};

		[Test, Order(20)]
		public async Task Acknowledgments_are_unique_per_member_and_version_and_change_status_only_from_the_expected_one()
		{
			var version = await AddVersionAsync(1, 2, ProtocolVersionStatuses.Published, major: 3);
			var row = await Acks().InsertAsync(Ack(version, "Member-A"), CancellationToken.None, true);

			Func<Task> duplicate = () => Acks().InsertAsync(Ack(version, "member-a"), CancellationToken.None, true);
			await duplicate.Should().ThrowAsync<Exception>("one acknowledgment per member per version, whatever the id's case");

			(await Acks().GetForVersionAndUserAsync(1, version.ProtocolVersionId, "MEMBER-A")).ProtocolAcknowledgmentId.Should().Be(row.ProtocolAcknowledgmentId);
			(await Acks().GetForUserAsync(1, "member-a")).Should().ContainSingle();
			(await Acks().GetForVersionAndUserAsync(2, version.ProtocolVersionId, "member-a")).Should().BeNull();

			var mine = await Acks().GetByIdForDepartmentAsync(1, row.ProtocolAcknowledgmentId);
			var theirs = await Acks().GetByIdForDepartmentAsync(1, row.ProtocolAcknowledgmentId);
			mine.Status = (int)ProtocolAcknowledgmentStatuses.Acknowledged;
			mine.SignedOn = DateTime.UtcNow;
			mine.CompletedOn = DateTime.UtcNow;
			mine.SignatureRecordId = 5;
			(await Acks().TryUpdateStatusAsync(mine, (int)ProtocolAcknowledgmentStatuses.Open)).Should().BeTrue();
			theirs.Status = (int)ProtocolAcknowledgmentStatuses.Withdrawn;
			(await Acks().TryUpdateStatusAsync(theirs, (int)ProtocolAcknowledgmentStatuses.Open)).Should().BeFalse("the signature got there first");
			var stored = await Acks().GetByIdForDepartmentAsync(1, row.ProtocolAcknowledgmentId);
			stored.Status.Should().Be((int)ProtocolAcknowledgmentStatuses.Acknowledged);
			stored.SignatureRecordId.Should().Be(5);
		}

		[Test, Order(21)]
		public async Task Superseding_and_withdrawing_touch_only_open_rows_of_the_protocol()
		{
			var old = await AddVersionAsync(1, 2, ProtocolVersionStatuses.Superseded, major: 4);
			var current = await AddVersionAsync(1, 2, ProtocolVersionStatuses.Published, major: 5);
			await Acks().InsertAsync(Ack(old, "open-1"), CancellationToken.None, true);
			await Acks().InsertAsync(Ack(old, "signed-1", ProtocolAcknowledgmentStatuses.Acknowledged), CancellationToken.None, true);
			await Acks().InsertAsync(Ack(current, "open-1"), CancellationToken.None, true);

			(await Acks().SupersedeOpenAsync(1, 2, current.ProtocolVersionId, DateTime.UtcNow)).Should().BeGreaterThanOrEqualTo(1);
			(await Acks().GetForVersionAndUserAsync(1, old.ProtocolVersionId, "open-1")).Status.Should().Be((int)ProtocolAcknowledgmentStatuses.Superseded);
			(await Acks().GetForVersionAndUserAsync(1, old.ProtocolVersionId, "signed-1")).Status.Should().Be((int)ProtocolAcknowledgmentStatuses.Acknowledged);
			(await Acks().GetForVersionAndUserAsync(1, current.ProtocolVersionId, "open-1")).Status.Should().Be((int)ProtocolAcknowledgmentStatuses.Open);

			(await Acks().WithdrawOpenForProtocolAsync(1, 2, DateTime.UtcNow)).Should().BeGreaterThanOrEqualTo(1);
			(await Acks().GetForVersionAndUserAsync(1, current.ProtocolVersionId, "open-1")).Status.Should().Be((int)ProtocolAcknowledgmentStatuses.Withdrawn);
			(await Acks().GetForVersionAndUserAsync(1, old.ProtocolVersionId, "signed-1")).Status.Should().Be((int)ProtocolAcknowledgmentStatuses.Acknowledged);
		}

		[Test, Order(22)]
		public async Task Reminders_come_back_only_when_owed_and_are_claimed_once_per_stage()
		{
			await using (var database = Connect(_connection))
				await database.ExecuteAsync($"UPDATE {T("DispatchProtocols")} SET {T("Status")} = 1 WHERE {T("DispatchProtocolId")} IN (1, 2)");
			var version = await AddVersionAsync(1, 1, ProtocolVersionStatuses.Published, major: 9);
			var now = DateTime.UtcNow;
			var due = await Acks().InsertAsync(Ack(version, "due-today", due: now.AddHours(-1)), CancellationToken.None, true);
			var late = await Acks().InsertAsync(Ack(version, "very-late", due: now.AddDays(-10)), CancellationToken.None, true);
			await Acks().InsertAsync(Ack(version, "not-yet", due: now.AddDays(2)), CancellationToken.None, true);
			await Acks().InsertAsync(Ack(version, "signed", ProtocolAcknowledgmentStatuses.Acknowledged, now.AddDays(-10)), CancellationToken.None, true);

			var owed = await Acks().GetRemindersDueAsync(now, 100);
			owed.Select(o => o.UserId).Should().BeEquivalentTo("due-today", "very-late");

			(await Acks().MarkReminderAsync(1, late.ProtocolAcknowledgmentId, (int)ProtocolAcknowledgmentReminderStages.Escalated, now)).Should().BeTrue();
			(await Acks().MarkReminderAsync(1, late.ProtocolAcknowledgmentId, (int)ProtocolAcknowledgmentReminderStages.Escalated, now)).Should().BeFalse("claimed once");
			(await Acks().GetByIdForDepartmentAsync(1, late.ProtocolAcknowledgmentId)).EscalatedOn.Should().NotBeNull();
			(await Acks().MarkReminderAsync(1, due.ProtocolAcknowledgmentId, (int)ProtocolAcknowledgmentReminderStages.Due, now)).Should().BeTrue();
			(await Acks().GetByIdForDepartmentAsync(1, due.ProtocolAcknowledgmentId)).EscalatedOn.Should().BeNull();

			(await Acks().GetRemindersDueAsync(now, 100)).Should().BeEmpty("every owed stage was claimed");
		}

		[Test, Order(23)]
		public async Task Must_read_rows_come_from_a_bulletin_in_force_and_awaiting_quiz_rows_are_found()
		{
			var bulletin = await AddVersionAsync(1, 2, ProtocolVersionStatuses.Published, major: 12);
			await using (var database = Connect(_connection))
				await database.ExecuteAsync($"UPDATE {T("DispatchProtocols")} SET {T("Kind")} = 6, {T("Status")} = 1, {T("CurrentVersionId")} = @V WHERE {T("DispatchProtocolId")} = 2",
					new { V = bulletin.ProtocolVersionId });
			await Acks().InsertAsync(Ack(bulletin, "Reader-1"), CancellationToken.None, true);
			var awaiting = await Acks().InsertAsync(Ack(bulletin, "quiz-taker", ProtocolAcknowledgmentStatuses.AwaitingQuiz), CancellationToken.None, true);

			(await Acks().GetOpenBulletinsForUserAsync(1, "reader-1")).Should().ContainSingle();
			(await Acks().GetOpenBulletinsForUserAsync(2, "reader-1")).Should().BeEmpty();
			(await Acks().GetAwaitingQuizAsync(10)).Select(a => a.ProtocolAcknowledgmentId).Should().Contain(awaiting.ProtocolAcknowledgmentId);

			// Retired (expired): it leaves the card.
			await using (var database = Connect(_connection))
				await database.ExecuteAsync($"UPDATE {T("DispatchProtocols")} SET {T("Status")} = 2 WHERE {T("DispatchProtocolId")} = 2");
			(await Acks().GetOpenBulletinsForUserAsync(1, "reader-1")).Should().BeEmpty();
		}

		[Test, Order(30)]
		public async Task The_retention_purge_takes_acknowledgments_and_the_file_with_the_version_and_never_a_cited_one()
		{
			var ended = await AddVersionAsync(1, 1, ProtocolVersionStatuses.Superseded, ProtocolVersionContentKinds.Document, 0, 7);
			var file = await AddFileAsync(ended, new byte[] { 9 });
			var text = await Texts().InsertAsync(new ProtocolFileText
			{
				DepartmentId = 1, DispatchProtocolId = 1, SourceKind = (int)ProtocolFileSourceKinds.VersionFile, SourceId = file.ProtocolVersionFileId, ProtocolVersionId = ended.ProtocolVersionId,
				Status = (int)ProtocolFileTextStatuses.Extracted, QueuedOn = DateTime.UtcNow
			}, CancellationToken.None, true);
			await Texts().SaveResultAsync(text, new[] { "page" });
			await Acks().InsertAsync(Ack(ended, "kept-until-purge", ProtocolAcknowledgmentStatuses.Acknowledged), CancellationToken.None, true);

			(await Versions().PurgeAsync(1, ended.ProtocolVersionId)).Should().Be(1);

			await using var database = Connect(_connection);
			(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("ProtocolAcknowledgments")} WHERE {T("ProtocolVersionId")} = @V", new { V = ended.ProtocolVersionId })).Should().Be(0);
			(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("ProtocolVersionFiles")} WHERE {T("ProtocolVersionId")} = @V", new { V = ended.ProtocolVersionId })).Should().Be(0);
			(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("ProtocolFileTextPages")} WHERE {T("ProtocolFileTextId")} = @V", new { V = text.ProtocolFileTextId })).Should().Be(0);

			// A version a call cites is never purged, nor what hangs off it.
			var cited = await AddVersionAsync(1, 1, ProtocolVersionStatuses.Superseded, ProtocolVersionContentKinds.Document, 0, 8);
			await AddFileAsync(cited, new byte[] { 8 });
			await Acks().InsertAsync(Ack(cited, "cited-member", ProtocolAcknowledgmentStatuses.Acknowledged), CancellationToken.None, true);
			await database.ExecuteAsync($"INSERT INTO {T("CallProtocols")} ({T("CallId")}, {T("DispatchProtocolId")}, {T("Score")}, {T("Trigger")}, {T("Data")}, {T("ProtocolVersionId")}) VALUES (77, 1, 0, 0, 'Z', @V)",
				new { V = cited.ProtocolVersionId });
			(await Versions().PurgeAsync(1, cited.ProtocolVersionId)).Should().Be(0);
			(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("ProtocolAcknowledgments")} WHERE {T("ProtocolVersionId")} = @V", new { V = cited.ProtocolVersionId })).Should().Be(1);
			(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("ProtocolVersionFiles")} WHERE {T("ProtocolVersionId")} = @V", new { V = cited.ProtocolVersionId })).Should().Be(1);
		}

		[Test, Order(99)]
		public async Task The_department_cleanup_clears_files_text_and_acknowledgments_and_leaves_other_departments()
		{
			var theirs = await AddVersionAsync(2, 3, ProtocolVersionStatuses.Published, ProtocolVersionContentKinds.Document);
			await AddFileAsync(theirs, new byte[] { 3 });
			await Acks().InsertAsync(Ack(theirs, "their-member"), CancellationToken.None, true);

			await using var database = Connect(_connection);
			await database.OpenAsync();
			await database.ExecuteAsync($"DELETE FROM {T("CallProtocols")} WHERE {T("CallId")} = 77");
			await using (var transaction = await database.BeginTransactionAsync())
			{
				await ProtocolDepartmentCleanup.DeleteWithinTransactionAsync(database, transaction, 1, type);
				await transaction.CommitAsync();
			}

			foreach (var table in new[] { "ProtocolAcknowledgments", "ProtocolVersionFiles", "ProtocolFileTexts", "ProtocolFileTextPages", "ProtocolVersions" })
			{
				(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T(table)} WHERE {T("DepartmentId")} = 1")).Should().Be(0, table);
			}
			(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("ProtocolAcknowledgments")} WHERE {T("DepartmentId")} = 2")).Should().Be(1);
			(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("ProtocolVersionFiles")} WHERE {T("DepartmentId")} = 2")).Should().Be(1);
		}
	}
}
