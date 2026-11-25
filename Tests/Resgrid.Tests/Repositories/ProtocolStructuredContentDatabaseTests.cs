using System;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Linq;
using System.Security.Cryptography;
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
	/// Real-database proof for Protocols P3 on both engines: M0379 (CallProtocols step log), M0380 (ProtocolVersionTranslations) and M0381
	/// (DispatchProtocolAttachments.Sha256, backfilled by the database) over the P0/P1/round-3 schema; Up again is a no-op, Down removes
	/// them and Up rebuilds them. Then the repositories: the step log's optimistic revision and its department check, translations
	/// (unique per version and language, approved headers only, conditional update), attachment hashes filled by the database and equal
	/// to the SHA-256 of the bytes, the retention purge and the department cleanup taking translations.
	/// Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class ProtocolStructuredContentDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "protocol_p3_";
		private static readonly byte[] AttachmentBytes = Enumerable.Range(0, 9000).Select(i => (byte)(i % 253)).ToArray();
		private DatabaseTypes _previous;
		private string _master, _connection, _database;

		private bool IsPostgres => type == DatabaseTypes.Postgres;

		private DbConnection Connect(string connection) => IsPostgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);

		private string T(string name) => IsPostgres ? name.ToLowerInvariant() : "[" + name + "]";

		private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

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
				new Resgrid.Providers.MigrationsPg.Migrations.M0344_BackfillProtocolVersionsPg(), new Resgrid.Providers.MigrationsPg.Migrations.M0353_AddProtocolDocumentsAndFileTextPg(),
				new Resgrid.Providers.MigrationsPg.Migrations.M0354_AddProtocolAcknowledgmentsPg()
			}
			: new IMigration[]
			{
				new Resgrid.Providers.Migrations.Migrations.M0336_ProtocolFoundation(), new Resgrid.Providers.Migrations.Migrations.M0337_ProtocolSnapshotsAndUniqueCallProtocols(),
				new Resgrid.Providers.Migrations.Migrations.M0342_AddSignatureRecords(), new Resgrid.Providers.Migrations.Migrations.M0343_AddProtocolLibraryAndVersions(),
				new Resgrid.Providers.Migrations.Migrations.M0344_BackfillProtocolVersions(), new Resgrid.Providers.Migrations.Migrations.M0353_AddProtocolDocumentsAndFileText(),
				new Resgrid.Providers.Migrations.Migrations.M0354_AddProtocolAcknowledgments()
			};

		private IMigration[] P3() => IsPostgres
			? new IMigration[]
			{
				new Resgrid.Providers.MigrationsPg.Migrations.M0379_AddCallProtocolStepLogPg(), new Resgrid.Providers.MigrationsPg.Migrations.M0380_AddProtocolVersionTranslationsPg(),
				new Resgrid.Providers.MigrationsPg.Migrations.M0381_AddProtocolAttachmentSha256Pg(),
				// P4 (M0399): the call protocol case columns the ORM now writes on insert.
				new Resgrid.Providers.MigrationsPg.Migrations.M0399_AddProtocolDecisionEnginePg()
			}
			: new IMigration[]
			{
				new Resgrid.Providers.Migrations.Migrations.M0379_AddCallProtocolStepLog(), new Resgrid.Providers.Migrations.Migrations.M0380_AddProtocolVersionTranslations(),
				new Resgrid.Providers.Migrations.Migrations.M0381_AddProtocolAttachmentSha256(),
				// P4 (M0399): the call protocol case columns the ORM now writes on insert.
				new Resgrid.Providers.Migrations.Migrations.M0399_AddProtocolDecisionEngine()
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
INSERT INTO {T("DispatchProtocols")} ({T("DepartmentId")}, {T("Name")}, {T("Code")}, {T("ProtocolText")}) VALUES (1, 'Mayday', 'MD', '<p>Mayday</p>'), (2, 'Theirs', 'TH', '<p>x</p>');
INSERT INTO {T("DispatchProtocolAttachments")} ({T("DispatchProtocolId")}, {T("FileName")}, {T("FileType")}, {T("Data")}) VALUES (1, 'before.pdf', 'application/pdf', @Bytes), (2, 'theirs.pdf', 'application/pdf', @Bytes);
INSERT INTO {T("Calls")} ({T("DepartmentId")}, {T("LoggedOn")}, {T("Number")}, {T("Name")}) VALUES (1, @Now, '1', 'Ours'), (2, @Now, '2', 'Theirs');", new { Bytes = AttachmentBytes, Now = DateTime.UtcNow });
			}

			Migrate(Base(), r => r.MigrateUp());
			Migrate(P3(), r => r.MigrateUp());
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

		private async Task<int> CountSchemaAsync()
		{
			await using var database = Connect(_connection);
			return await database.ExecuteScalarAsync<int>(IsPostgres
				? "SELECT (SELECT COUNT(*) FROM information_schema.tables WHERE table_name = 'protocolversiontranslations') + " +
				  "(SELECT COUNT(*) FROM information_schema.columns WHERE (table_name = 'callprotocols' AND column_name IN ('steplogjson', 'steplogrevision')) OR (table_name = 'dispatchprotocolattachments' AND column_name = 'sha256'))"
				: "SELECT (SELECT COUNT(*) FROM sys.tables WHERE name = 'ProtocolVersionTranslations') + " +
				  "(SELECT COUNT(*) FROM sys.columns WHERE (object_id = OBJECT_ID('dbo.CallProtocols') AND name IN ('StepLogJson', 'StepLogRevision')) OR (object_id = OBJECT_ID('dbo.DispatchProtocolAttachments') AND name = 'Sha256'))");
		}

		[Test, Order(1)]
		public async Task Up_adds_the_table_and_columns_backfills_hashes_and_up_again_changes_nothing()
		{
			(await CountSchemaAsync()).Should().Be(4);
			await using (var database = Connect(_connection))
			{
				(await database.ExecuteScalarAsync<string>($"SELECT {T("Sha256")} FROM {T("DispatchProtocolAttachments")} WHERE {T("FileName")} = 'before.pdf'"))
					.Should().Be(Sha(AttachmentBytes), "the migration hashes existing attachments in the database");
				await database.ExecuteAsync(IsPostgres ? "DELETE FROM \"VersionInfo\" WHERE \"Version\" IN (379, 380, 381)" : "DELETE FROM [VersionInfo] WHERE [Version] IN (379, 380, 381)");
			}

			Migrate(P3(), r => r.MigrateUp());
			(await CountSchemaAsync()).Should().Be(4);
		}

		[Test, Order(2)]
		public async Task Down_removes_them_and_up_again_rebuilds_them()
		{
			Migrate(Base().Concat(P3()).ToArray(), r => r.MigrateDown(354));
			(await CountSchemaAsync()).Should().Be(0);
			await using (var database = Connect(_connection))
				(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("DispatchProtocolAttachments")}")).Should().Be(2, "Down never removes attachments");

			Migrate(P3(), r => r.MigrateUp());
			(await CountSchemaAsync()).Should().Be(4);
		}

		private CallProtocolsRepository CallProtocols() => Repository((c, s, u, q) => new CallProtocolsRepository(c, s, u, q));
		private ProtocolVersionsRepository Versions() => Repository((c, s, u, q) => new ProtocolVersionsRepository(c, s, u, q));
		private ProtocolVersionTranslationsRepository Translations() => Repository((c, s, u, q) => new ProtocolVersionTranslationsRepository(c, s, u, q));
		private DispatchProtocolAttachmentRepository Attachments() => Repository((c, s, u, q) => new DispatchProtocolAttachmentRepository(c, s, u, q));

		private async Task<ProtocolVersion> AddVersionAsync(int departmentId, int protocolId, ProtocolVersionStatuses status)
		{
			var html = "<p>v" + Guid.NewGuid().ToString("N") + "</p>";
			return await Versions().InsertAsync(new ProtocolVersion
			{
				DepartmentId = departmentId, DispatchProtocolId = protocolId, Major = 1, Status = (int)status, ContentHtml = html,
				ContentJson = ProtocolContentDocument.FromHtml(html).ToJson(), ContentHash = ProtocolContentHash.Compute(html), CreatedOn = DateTime.UtcNow, CreatedByUserId = "author"
			}, CancellationToken.None, true);
		}

		private ProtocolVersionTranslation Translation(ProtocolVersion version, string locale, ProtocolTranslationStatuses status) => new ProtocolVersionTranslation
		{
			DepartmentId = version.DepartmentId, DispatchProtocolId = version.DispatchProtocolId, ProtocolVersionId = version.ProtocolVersionId, Locale = locale, Status = (int)status,
			ContentJson = "{}", ContentHtml = "<p>" + locale + "</p>", ContentHash = ProtocolContentHash.Compute("<p>" + locale + "</p>"), SourceContentHash = version.ContentHash,
			CreatedOn = DateTime.UtcNow, CreatedByUserId = "author"
		};

		[Test, Order(10)]
		public async Task The_step_log_is_written_against_its_revision_and_read_only_through_the_owning_department()
		{
			await using var database = Connect(_connection);
			var callId = await database.ExecuteScalarAsync<int>($"SELECT {T("CallId")} FROM {T("Calls")} WHERE {T("DepartmentId")} = 1");
			var row = await CallProtocols().InsertAsync(new CallProtocol { CallId = callId, DispatchProtocolId = 1, Data = "MD", Source = (int)CallProtocolSources.Manual }, CancellationToken.None, true);

			var fresh = await CallProtocols().GetForDepartmentCallAsync(1, callId, row.CallProtocolId);
			fresh.StepLogRevision.Should().Be(0);
			fresh.StepLogJson.Should().BeNull();

			(await CallProtocols().TryUpdateStepLogAsync(row.CallProtocolId, "{\"entries\":[1]}", 0)).Should().BeTrue();
			(await CallProtocols().TryUpdateStepLogAsync(row.CallProtocolId, "{\"entries\":[2]}", 0)).Should().BeFalse("someone else wrote revision 1");
			var stored = await CallProtocols().GetForDepartmentCallAsync(1, callId, row.CallProtocolId);
			stored.StepLogJson.Should().Be("{\"entries\":[1]}");
			stored.StepLogRevision.Should().Be(1);
			(await CallProtocols().GetForDepartmentCallAsync(2, callId, row.CallProtocolId)).Should().BeNull("another department never reads it");

			// The call's own save path (the ORM) never writes the log back.
			stored.Score = 4;
			await CallProtocols().UpdateAsync(stored, CancellationToken.None, true);
			(await CallProtocols().GetForDepartmentCallAsync(1, callId, row.CallProtocolId)).StepLogJson.Should().Be("{\"entries\":[1]}");
		}

		[Test, Order(11)]
		public async Task Translations_are_unique_per_version_and_language_and_only_approved_headers_are_listed()
		{
			var version = await AddVersionAsync(1, 1, ProtocolVersionStatuses.Published);
			var de = await Translations().InsertAsync(Translation(version, "de", ProtocolTranslationStatuses.Approved), CancellationToken.None, true);
			await Translations().InsertAsync(Translation(version, "fr", ProtocolTranslationStatuses.Draft), CancellationToken.None, true);

			Func<Task> duplicate = () => Translations().InsertAsync(Translation(version, "de", ProtocolTranslationStatuses.Draft), CancellationToken.None, true);
			await duplicate.Should().ThrowAsync<Exception>();

			var headers = await Translations().GetApprovedHeadersForVersionsAsync(1, new[] { version.ProtocolVersionId });
			headers.Select(h => h.Locale).Should().Equal("de");
			headers[0].ContentHtml.Should().BeNull("header reads never select content");
			(await Translations().GetForVersionAsync(1, version.ProtocolVersionId, true)).Should().HaveCount(2);
			(await Translations().GetByIdForDepartmentAsync(2, de.ProtocolVersionTranslationId)).Should().BeNull();

			var loaded = await Translations().GetByIdForDepartmentAsync(1, de.ProtocolVersionTranslationId);
			loaded.Status = (int)ProtocolTranslationStatuses.Withdrawn;
			loaded.WithdrawnOn = DateTime.UtcNow;
			(await Translations().TryUpdateAsync(loaded, loaded.RowVersion)).Should().BeTrue();
			(await Translations().TryUpdateAsync(loaded, 0)).Should().BeFalse("the row version moved on");
			(await Translations().GetApprovedHeadersForVersionsAsync(1, new[] { version.ProtocolVersionId })).Should().BeEmpty();
		}

		[Test, Order(12)]
		public async Task Attachment_hashes_are_filled_by_the_database_without_loading_the_bytes()
		{
			var later = new byte[] { 9, 8, 7, 6 };
			await using (var database = Connect(_connection))
				await database.ExecuteAsync($"INSERT INTO {T("DispatchProtocolAttachments")} ({T("DispatchProtocolId")}, {T("FileName")}, {T("FileType")}, {T("Data")}) VALUES (1, 'later.pdf', 'application/pdf', @Bytes)",
					new { Bytes = later });

			var hashes = (await Attachments().GetAttachmentHashesByDepartmentIdAsync(1)).ToList();

			hashes.Select(h => h.FileName).Should().BeEquivalentTo("before.pdf", "later.pdf");
			hashes.Single(h => h.FileName == "later.pdf").Sha256.Should().Be(Sha(later));
			hashes.Single(h => h.FileName == "later.pdf").Size.Should().Be(4);
			hashes.All(h => h.Data == null).Should().BeTrue();
		}

		[Test, Order(20)]
		public async Task The_retention_purge_and_the_department_cleanup_take_translations_with_their_version()
		{
			var ended = await AddVersionAsync(1, 1, ProtocolVersionStatuses.Superseded);
			await Translations().InsertAsync(Translation(ended, "de", ProtocolTranslationStatuses.Approved), CancellationToken.None, true);
			(await Versions().PurgeAsync(1, ended.ProtocolVersionId)).Should().Be(1);

			var theirs = await AddVersionAsync(2, 2, ProtocolVersionStatuses.Published);
			await Translations().InsertAsync(Translation(theirs, "de", ProtocolTranslationStatuses.Approved), CancellationToken.None, true);

			await using var database = Connect(_connection);
			await database.OpenAsync();
			(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("ProtocolVersionTranslations")} WHERE {T("ProtocolVersionId")} = @V", new { V = ended.ProtocolVersionId })).Should().Be(0);

			await using (var transaction = await database.BeginTransactionAsync())
			{
				await ProtocolDepartmentCleanup.DeleteWithinTransactionAsync(database, transaction, 1, type);
				await transaction.CommitAsync();
			}

			(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("ProtocolVersionTranslations")} WHERE {T("DepartmentId")} = 1")).Should().Be(0);
			(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("ProtocolVersionTranslations")} WHERE {T("DepartmentId")} = 2")).Should().Be(1);
		}
	}
}
