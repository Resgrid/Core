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
	/// Real-database proof for Protocols P4 on both engines: M0399 (CallProtocols case columns and the shared trigger engine's criteria on
	/// DispatchProtocolTriggers and RunCardTriggers) and M0400 (ProtocolAiSuggestions) over the P0/P1/P3 schema; Up again is a no-op, Down
	/// removes them and Up rebuilds them. Then the repositories and engines on rows read back from the database: the department-scoped case
	/// write, AI suggestions (department scope, confirmed once), trigger criteria evaluated by the one engine for protocols and run cards,
	/// and the department cleanup taking AI suggestions.
	/// Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class ProtocolDecisionEngineDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "protocol_p4_";
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

		private IMigration[] P4() => IsPostgres
			? new IMigration[] { new Resgrid.Providers.MigrationsPg.Migrations.M0399_AddProtocolDecisionEnginePg(), new Resgrid.Providers.MigrationsPg.Migrations.M0400_AddProtocolAiSuggestionsPg() }
			: new IMigration[] { new Resgrid.Providers.Migrations.Migrations.M0399_AddProtocolDecisionEngine(), new Resgrid.Providers.Migrations.Migrations.M0400_AddProtocolAiSuggestions() };

		private IMigration[] P3() => IsPostgres
			? new IMigration[]
			{
				new Resgrid.Providers.MigrationsPg.Migrations.M0379_AddCallProtocolStepLogPg(), new Resgrid.Providers.MigrationsPg.Migrations.M0380_AddProtocolVersionTranslationsPg(),
				new Resgrid.Providers.MigrationsPg.Migrations.M0381_AddProtocolAttachmentSha256Pg()
			}
			: new IMigration[]
			{
				new Resgrid.Providers.Migrations.Migrations.M0379_AddCallProtocolStepLog(), new Resgrid.Providers.Migrations.Migrations.M0380_AddProtocolVersionTranslations(),
				new Resgrid.Providers.Migrations.Migrations.M0381_AddProtocolAttachmentSha256()
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
CREATE TABLE runcards (runcardid serial PRIMARY KEY, departmentid integer NOT NULL, name citext);
CREATE TABLE runcardtriggers (runcardtriggerid serial PRIMARY KEY, runcardid integer NOT NULL REFERENCES runcards (runcardid), triggertype integer NOT NULL, priority integer, calltypeid integer, startson timestamp, endson timestamp);
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
CREATE TABLE RunCards (RunCardId int IDENTITY(1,1) PRIMARY KEY, DepartmentId int NOT NULL, Name nvarchar(max));
CREATE TABLE RunCardTriggers (RunCardTriggerId int IDENTITY(1,1) PRIMARY KEY, RunCardId int NOT NULL REFERENCES RunCards (RunCardId), TriggerType int NOT NULL, Priority int, CallTypeId int, StartsOn datetime2, EndsOn datetime2);
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
			Migrate(P4(), r => r.MigrateUp());
		}


		private async Task<int> CountSchemaAsync()
		{
			await using var database = Connect(_connection);
			return await database.ExecuteScalarAsync<int>(IsPostgres
				? "SELECT (SELECT COUNT(*) FROM information_schema.tables WHERE table_name = 'protocolaisuggestions') + " +
				  "(SELECT COUNT(*) FROM information_schema.columns WHERE (table_name = 'callprotocols' AND column_name IN ('determinant', 'overridereason', 'casejson')) " +
				  "OR (table_name IN ('dispatchprotocoltriggers', 'runcardtriggers') AND column_name IN ('geofencegroupid', 'daysofweek', 'startminute', 'endminute', 'keywords', 'poitypeid', 'callsources')))"
				: "SELECT (SELECT COUNT(*) FROM sys.tables WHERE name = 'ProtocolAiSuggestions') + " +
				  "(SELECT COUNT(*) FROM sys.columns WHERE (object_id = OBJECT_ID('dbo.CallProtocols') AND name IN ('Determinant', 'OverrideReason', 'CaseJson')) " +
				  "OR (object_id IN (OBJECT_ID('dbo.DispatchProtocolTriggers'), OBJECT_ID('dbo.RunCardTriggers')) AND name IN ('GeofenceGroupId', 'DaysOfWeek', 'StartMinute', 'EndMinute', 'Keywords', 'PoiTypeId', 'CallSources')))");
		}

		// 1 table + 3 call protocol columns + 2 x 7 trigger columns.
		private const int Expected = 18;

		[Test, Order(1)]
		public async Task Up_adds_the_columns_and_table_and_up_again_changes_nothing()
		{
			(await CountSchemaAsync()).Should().Be(Expected);
			await using (var database = Connect(_connection))
				await database.ExecuteAsync(IsPostgres ? "DELETE FROM \"VersionInfo\" WHERE \"Version\" IN (399, 400)" : "DELETE FROM [VersionInfo] WHERE [Version] IN (399, 400)");

			Migrate(P4(), r => r.MigrateUp());
			(await CountSchemaAsync()).Should().Be(Expected);
		}

		[Test, Order(2)]
		public async Task Down_removes_them_and_up_again_rebuilds_them()
		{
			Migrate(Base().Concat(P3()).Concat(P4()).ToArray(), r => r.MigrateDown(381));
			(await CountSchemaAsync()).Should().Be(0);

			Migrate(P4(), r => r.MigrateUp());
			(await CountSchemaAsync()).Should().Be(Expected);
		}

		private CallProtocolsRepository CallProtocols() => Repository((c, s, u, q) => new CallProtocolsRepository(c, s, u, q));
		private ProtocolAiSuggestionsRepository Suggestions() => Repository((c, s, u, q) => new ProtocolAiSuggestionsRepository(c, s, u, q));

		private async Task<int> AddCallProtocolAsync(int callId, int protocolId)
		{
			await using var database = Connect(_connection);
			return await database.ExecuteScalarAsync<int>(IsPostgres
				? "INSERT INTO callprotocols (callid, dispatchprotocolid, score, trigger, data) VALUES (@C, @P, 0, 0, 'MD') RETURNING callprotocolid"
				: "INSERT INTO [CallProtocols] ([CallId], [DispatchProtocolId], [Score], [Trigger], [Data]) OUTPUT INSERTED.[CallProtocolId] VALUES (@C, @P, 0, 0, 'MD')",
				new { C = callId, P = protocolId });
		}

		[Test, Order(3)]
		public async Task The_case_is_written_alone_and_only_on_the_departments_own_call()
		{
			var ours = await AddCallProtocolAsync(1, 1);
			var record = new CallProtocolCase { ProtocolVersionId = 5, Status = (int)ProtocolCaseStatuses.Complete, ComputedDeterminant = "10-D-1", Determinant = "10-E-1", Overridden = true, OverrideReason = "Caller now not breathing" };

			(await CallProtocols().TryUpdateCaseAsync(1, ours, "10-E-1", "Caller now not breathing", record.ToJson())).Should().BeTrue();
			(await CallProtocols().TryUpdateCaseAsync(2, ours, "99-X", "wrong department", "{}")).Should().BeFalse("another department's id never writes a call protocol");

			await using var database = Connect(_connection);
			var row = await database.QueryFirstAsync<CallProtocol>($"SELECT * FROM {T("CallProtocols")} WHERE {T("CallProtocolId")} = @Id", new { Id = ours });
			row.Determinant.Should().Be("10-E-1");
			row.OverrideReason.Should().Be("Caller now not breathing");
			row.Data.Should().Be("MD", "the rest of the row is never written");
			CallProtocolCase.Parse(row.CaseJson).ComputedDeterminant.Should().Be("10-D-1");

			// Determinant reports compare like SQL Server's collation on both engines (citext on PostgreSQL).
			(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("CallProtocols")} WHERE {T("Determinant")} = " + (IsPostgres ? "@D::citext" : "@D"), new { D = "10-e-1" })).Should().Be(1);
		}

		[Test, Order(4)]
		public async Task Ai_suggestions_are_department_scoped_and_confirmed_once()
		{
			var saved = await Suggestions().SaveOrUpdateAsync(new ProtocolAiSuggestion
			{
				DepartmentId = 1, CallId = 1, DispatchProtocolId = 1, ProtocolVersionId = 7, AnswersJson = "{\"breathing\":\"no\"}", Confidence = 0.8765m,
				Rationale = "Caller says not breathing", InputKind = (int)ProtocolAiSuggestionInputs.IntakeThread, ModelRevision = "rev", CreatedOn = DateTime.UtcNow, RequestedByUserId = "dispatcher"
			}, CancellationToken.None, true);
			saved.ProtocolAiSuggestionId.Should().BeGreaterThan(0);

			(await Suggestions().GetByIdForDepartmentAsync(2, saved.ProtocolAiSuggestionId)).Should().BeNull();
			var read = await Suggestions().GetByIdForDepartmentAsync(1, saved.ProtocolAiSuggestionId);
			read.Answers["breathing"].Should().Be("no");
			read.Confidence.Should().Be(0.8765m);
			(await Suggestions().GetForCallAsync(1, 1)).Should().ContainSingle();
			(await Suggestions().GetForCallAsync(2, 1)).Should().BeEmpty();

			(await Suggestions().TryConfirmAsync(2, saved.ProtocolAiSuggestionId, "other", DateTime.UtcNow)).Should().BeFalse();
			(await Suggestions().TryConfirmAsync(1, saved.ProtocolAiSuggestionId, "dispatcher", DateTime.UtcNow)).Should().BeTrue();
			(await Suggestions().TryConfirmAsync(1, saved.ProtocolAiSuggestionId, "second", DateTime.UtcNow)).Should().BeFalse("a suggestion is confirmed once");
			(await Suggestions().GetByIdForDepartmentAsync(1, saved.ProtocolAiSuggestionId)).ConfirmedByUserId.Should().Be("dispatcher");
		}

		[Test, Order(5)]
		public async Task Trigger_criteria_read_back_from_both_tables_run_through_the_one_engine()
		{
			await using var database = Connect(_connection);
			await database.ExecuteAsync($@"
INSERT INTO {T("DispatchProtocolTriggers")} ({T("DispatchProtocolId")}, {T("Type")}, {T("GeofenceGroupId")}, {T("DaysOfWeek")}, {T("StartMinute")}, {T("EndMinute")}, {T("Keywords")}, {T("PoiTypeId")}, {T("CallSources")})
VALUES (1, 3, 40, 62, 1320, 360, 'chest pain, not breathing', 9, '1,6');
INSERT INTO {T("RunCards")} ({T("DepartmentId")}, {T("Name")}) VALUES (1, 'Medical');
INSERT INTO {T("RunCardTriggers")} ({T("RunCardId")}, {T("TriggerType")}, {T("Priority")}, {T("Keywords")}, {T("GeofenceGroupId")}) VALUES (1, 0, 3, 'NOT BREATHING', 40);");

			var protocolTrigger = (await database.QueryAsync<DispatchProtocolTrigger>($"SELECT * FROM {T("DispatchProtocolTriggers")} WHERE {T("Type")} = 3")).Single();
			var runCardTrigger = (await database.QueryAsync<RunCardTrigger>($"SELECT * FROM {T("RunCardTriggers")}")).Single();
			protocolTrigger.Keywords.Should().Be("chest pain, not breathing");
			runCardTrigger.GeofenceGroupId.Should().Be(40);

			// Saturday 02:00 department time is inside Friday's 22:00–06:00 window (days Monday–Friday).
			var call = new ProtocolCallContext
			{
				Priority = 3, AtUtc = DateTime.UtcNow, LocalTime = new DateTime(2026, 11, 28, 2, 0, 0), Nature = "<p>Man down, <b>not breathing</b></p>",
				GeofenceGroupIds = new System.Collections.Generic.HashSet<int> { 40 }, LocationPoiTypeId = 9, CallSource = 0
			};
			ProtocolTriggerMatcher.Match(new DispatchProtocol { Triggers = new System.Collections.Generic.List<DispatchProtocolTrigger> { protocolTrigger } }, call).Should().ContainSingle();
			Resgrid.Services.RunCardsService.GetTriggerMatchSpecificity(new RunCard { Triggers = new System.Collections.Generic.List<RunCardTrigger> { runCardTrigger } }, call)
				.Should().Be(1 + 3 * 2, "priority plus two conditions");

			call.GeofenceGroupIds = new System.Collections.Generic.HashSet<int> { 41 };
			ProtocolTriggerMatcher.Match(new DispatchProtocol { Triggers = new System.Collections.Generic.List<DispatchProtocolTrigger> { protocolTrigger } }, call).Should().BeEmpty();
			Resgrid.Services.RunCardsService.GetTriggerMatchSpecificity(new RunCard { Triggers = new System.Collections.Generic.List<RunCardTrigger> { runCardTrigger } }, call).Should().BeNull();
		}

		[Test, Order(6)]
		public async Task The_department_cleanup_takes_the_departments_ai_suggestions()
		{
			await Suggestions().SaveOrUpdateAsync(new ProtocolAiSuggestion { DepartmentId = 2, CallId = 2, DispatchProtocolId = 2, CreatedOn = DateTime.UtcNow }, CancellationToken.None, true);

			await using var database = Connect(_connection);
			await database.OpenAsync();
			await using (var transaction = await database.BeginTransactionAsync())
			{
				await ProtocolDepartmentCleanup.DeleteWithinTransactionAsync(database, transaction, 1, type);
				await transaction.CommitAsync();
			}

			(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("ProtocolAiSuggestions")} WHERE {T("DepartmentId")} = 1")).Should().Be(0);
			(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("ProtocolAiSuggestions")} WHERE {T("DepartmentId")} = 2")).Should().Be(1);
		}
	}
}
