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
	/// Real-database proof for Protocols P5 QA (dispatch case review) on both engines: M0391 over the P0–P4 protocol schema (Up, Up again is a
	/// no-op, Down, Up), then the repository on rows read back from the database: the candidate query (department, dates, removed and deleted
	/// calls, case recorded, protocol, determinant prefix compared case-insensitively, dispatcher of record, already reviewed), the unique
	/// review per call protocol, the versioned score write, acknowledgment and reply once, the queue, the dispatcher's list and the report
	/// window, and both department cleanups. Set RESGRID_ADP_SQLSERVER_TEST_CONNECTION / RESGRID_ADP_POSTGRES_TEST_CONNECTION to run.
	/// </summary>
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class DispatchCaseReviewDatabaseTests(DatabaseTypes type)
	{
		private const string Prefix = "dispatch_case_review_";
		private DatabaseTypes _previous;
		private string _master, _connection, _database;
		private int _versionId;
		private readonly DateTime _now = new DateTime(2026, 11, 20, 15, 0, 0, DateTimeKind.Utc);

		private bool IsPostgres => type == DatabaseTypes.Postgres;

		private DbConnection Connect(string connection) => IsPostgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);

		private string T(string name) => IsPostgres ? name.ToLowerInvariant() : "[" + name + "]";

		private DispatchCaseReviewsRepository Reviews()
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
			return new DispatchCaseReviewsRepository(connections.Object, configuration, Mock.Of<IUnitOfWork>(), new QueryFactory(list.Object));
		}

		private IMigration[] Protocols() => IsPostgres
			? new IMigration[]
			{
				new Resgrid.Providers.MigrationsPg.Migrations.M0336_ProtocolFoundationPg(), new Resgrid.Providers.MigrationsPg.Migrations.M0337_ProtocolSnapshotsAndUniqueCallProtocolsPg(),
				new Resgrid.Providers.MigrationsPg.Migrations.M0341_AddSignatureRecordsPg(), new Resgrid.Providers.MigrationsPg.Migrations.M0342_AddProtocolLibraryAndVersionsPg(),
				new Resgrid.Providers.MigrationsPg.Migrations.M0343_BackfillProtocolVersionsPg(), new Resgrid.Providers.MigrationsPg.Migrations.M0347_AddProtocolDocumentsAndFileTextPg(),
				new Resgrid.Providers.MigrationsPg.Migrations.M0348_AddProtocolAcknowledgmentsPg(), new Resgrid.Providers.MigrationsPg.Migrations.M0361_AddCallProtocolStepLogPg(),
				new Resgrid.Providers.MigrationsPg.Migrations.M0362_AddProtocolVersionTranslationsPg(), new Resgrid.Providers.MigrationsPg.Migrations.M0363_AddProtocolAttachmentSha256Pg(),
				new Resgrid.Providers.MigrationsPg.Migrations.M0376_AddProtocolDecisionEnginePg(), new Resgrid.Providers.MigrationsPg.Migrations.M0377_AddProtocolAiSuggestionsPg()
			}
			: new IMigration[]
			{
				new Resgrid.Providers.Migrations.Migrations.M0336_ProtocolFoundation(), new Resgrid.Providers.Migrations.Migrations.M0337_ProtocolSnapshotsAndUniqueCallProtocols(),
				new Resgrid.Providers.Migrations.Migrations.M0341_AddSignatureRecords(), new Resgrid.Providers.Migrations.Migrations.M0342_AddProtocolLibraryAndVersions(),
				new Resgrid.Providers.Migrations.Migrations.M0343_BackfillProtocolVersions(), new Resgrid.Providers.Migrations.Migrations.M0347_AddProtocolDocumentsAndFileText(),
				new Resgrid.Providers.Migrations.Migrations.M0348_AddProtocolAcknowledgments(), new Resgrid.Providers.Migrations.Migrations.M0361_AddCallProtocolStepLog(),
				new Resgrid.Providers.Migrations.Migrations.M0362_AddProtocolVersionTranslations(), new Resgrid.Providers.Migrations.Migrations.M0363_AddProtocolAttachmentSha256(),
				new Resgrid.Providers.Migrations.Migrations.M0376_AddProtocolDecisionEngine(), new Resgrid.Providers.Migrations.Migrations.M0377_AddProtocolAiSuggestions()
			};

		private IMigration[] Qa() => IsPostgres
			? new IMigration[] { new Resgrid.Providers.MigrationsPg.Migrations.M0391_AddDispatchCaseReviewsPg() }
			: new IMigration[] { new Resgrid.Providers.Migrations.Migrations.M0391_AddDispatchCaseReviews() };

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
CREATE TABLE calls (callid serial PRIMARY KEY, departmentid integer NOT NULL, loggedon timestamp NOT NULL, number citext, name citext, reportinguserid citext, isdeleted boolean NOT NULL DEFAULT false);
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
CREATE TABLE Calls (CallId int IDENTITY(1,1) PRIMARY KEY, DepartmentId int NOT NULL, LoggedOn datetime NOT NULL, Number nvarchar(50), Name nvarchar(max), ReportingUserId nvarchar(128), IsDeleted bit NOT NULL DEFAULT 0);
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

				await database.ExecuteAsync($"INSERT INTO {T("DispatchProtocols")} ({T("DepartmentId")}, {T("Name")}, {T("Code")}, {T("ProtocolText")}) VALUES (1, 'Cardiac', '09', '<p>x</p>'), (1, 'Falls', '17', '<p>x</p>'), (2, 'Theirs', 'TH', '<p>x</p>');");
			}

			Migrate(Protocols(), r => r.MigrateUp());
			Migrate(Qa(), r => r.MigrateUp());

			// M0343 gave every existing protocol its version 1.0; call rows cite it (CallProtocols.ProtocolVersionId has a foreign key).
			await using (var database = Connect(_connection))
				_versionId = await database.ExecuteScalarAsync<int>($"SELECT MIN({T("ProtocolVersionId")}) FROM {T("ProtocolVersions")} WHERE {T("DispatchProtocolId")} = 1");
			_versionId.Should().BeGreaterThan(0);
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

		private async Task<int> CountSchemaAsync()
		{
			await using var database = Connect(_connection);
			return await database.ExecuteScalarAsync<int>(IsPostgres
				? "SELECT (SELECT COUNT(*) FROM information_schema.tables WHERE table_name = 'dispatchcasereviews') + " +
				  "(SELECT COUNT(*) FROM pg_indexes WHERE tablename = 'dispatchcasereviews' AND indexname IN ('ux_dispatchcasereviews_callprotocol', 'ix_dispatchcasereviews_status', 'ix_dispatchcasereviews_dispatcher', 'ix_dispatchcasereviews_callloggedon'))"
				: "SELECT (SELECT COUNT(*) FROM sys.tables WHERE name = 'DispatchCaseReviews') + " +
				  "(SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.DispatchCaseReviews') AND name IN ('UX_DispatchCaseReviews_CallProtocol', 'IX_DispatchCaseReviews_Status', 'IX_DispatchCaseReviews_Dispatcher', 'IX_DispatchCaseReviews_CallLoggedOn'))");
		}

		[Test, Order(1)]
		public async Task Up_creates_the_table_and_its_indexes_and_up_again_changes_nothing()
		{
			(await CountSchemaAsync()).Should().Be(5);
			await using (var database = Connect(_connection))
				await database.ExecuteAsync(IsPostgres ? "DELETE FROM \"VersionInfo\" WHERE \"Version\" = 391" : "DELETE FROM [VersionInfo] WHERE [Version] = 391");

			Migrate(Qa(), r => r.MigrateUp());
			(await CountSchemaAsync()).Should().Be(5);
		}

		[Test, Order(2)]
		public async Task Down_removes_the_table_and_up_again_rebuilds_it()
		{
			Migrate(Protocols().Concat(Qa()).ToArray(), r => r.MigrateDown(377));
			(await CountSchemaAsync()).Should().Be(0);

			Migrate(Qa(), r => r.MigrateUp());
			(await CountSchemaAsync()).Should().Be(5);
		}

		private async Task<int> AddCallAsync(DbConnection database, int departmentId, DateTime loggedOn, string number, string reportingUserId, bool deleted = false) =>
			await database.ExecuteScalarAsync<int>(IsPostgres
				? "INSERT INTO calls (departmentid, loggedon, number, name, reportinguserid, isdeleted) VALUES (@D, @L, @N, 'Call ' || @N, @R, @X) RETURNING callid"
				: "INSERT INTO [Calls] ([DepartmentId], [LoggedOn], [Number], [Name], [ReportingUserId], [IsDeleted]) OUTPUT INSERTED.[CallId] VALUES (@D, @L, @N, 'Call ' + @N, @R, @X)",
				new { D = departmentId, L = IsPostgres ? DateTime.SpecifyKind(loggedOn, DateTimeKind.Unspecified) : loggedOn, N = number, R = reportingUserId, X = deleted });

		private async Task<int> AddCallProtocolAsync(DbConnection database, int callId, int protocolId, string determinant, string attachedBy, bool withCase = true, bool removed = false,
			string overrideReason = null) =>
			await database.ExecuteScalarAsync<int>(IsPostgres
				? "INSERT INTO callprotocols (callid, dispatchprotocolid, score, trigger, data, determinant, attachedbyuserid, casejson, removedon, overridereason, protocolversionid) " +
				  "VALUES (@C, @P, 0, 0, 'x', @Det, @By, @Case, @Removed, @Why, @V) RETURNING callprotocolid"
				: "INSERT INTO [CallProtocols] ([CallId], [DispatchProtocolId], [Score], [Trigger], [Data], [Determinant], [AttachedByUserId], [CaseJson], [RemovedOn], [OverrideReason], [ProtocolVersionId]) " +
				  "OUTPUT INSERTED.[CallProtocolId] VALUES (@C, @P, 0, 0, 'x', @Det, @By, @Case, @Removed, @Why, @V)",
				new
				{
					C = callId, P = protocolId, Det = determinant, By = attachedBy, Case = withCase ? "{\"versionId\":70,\"code\":\"09\",\"steps\":[]}" : null,
					Removed = removed ? (DateTime?)DateTime.SpecifyKind(_now, IsPostgres ? DateTimeKind.Unspecified : DateTimeKind.Utc) : null, Why = overrideReason,
					V = protocolId == 1 ? (int?)_versionId : null
				});

		private DispatchCaseReview NewReview(int departmentId, int callId, int callProtocolId, string dispatcher, DateTime loggedOn) => new DispatchCaseReview
		{
			DepartmentId = departmentId, CallId = callId, CallProtocolId = callProtocolId, DispatchProtocolId = 1, ProtocolVersionId = 70, Determinant = "09-E-01", DispatcherUserId = dispatcher,
			CallLoggedOn = loggedOn, CallNumber = "N" + callId, ProtocolCode = "09", ProtocolName = "Cardiac", Status = (int)DispatchCaseReviewStatuses.Queued, RmsQualityRubricId = "r1",
			CriteriaJson = "[]", ItemsJson = "[]", SampledByUserId = "officer", SampledOn = _now, CreatedOn = _now, ModifiedOn = _now, RowVersion = 1
		};

		[Test, Order(3)]
		public async Task Candidates_are_the_departments_recorded_cases_filtered_as_asked_and_flagged_once_reviewed()
		{
			int ours, ours2, falls, reportedOnly, removed, noCase, deletedCall, oldCall, theirs;
			await using (var database = Connect(_connection))
			{
				var c1 = await AddCallAsync(database, 1, _now.AddDays(-3), "26-1", "Reporter");
				var c2 = await AddCallAsync(database, 1, _now.AddDays(-2), "26-2", "reporter");
				var c3 = await AddCallAsync(database, 1, _now.AddDays(-1), "26-3", "Dispatcher-B");
				var c4 = await AddCallAsync(database, 1, _now.AddDays(-1), "26-4", "x", deleted: true);
				var c5 = await AddCallAsync(database, 1, _now.AddDays(-90), "25-9", "x");
				var c6 = await AddCallAsync(database, 2, _now.AddDays(-1), "T-1", "x");
				ours = await AddCallProtocolAsync(database, c1, 1, "09-E-01", "Dispatcher-A", overrideReason: "caller not breathing");
				ours2 = await AddCallProtocolAsync(database, c2, 1, "09-D-02", "dispatcher-a");
				falls = await AddCallProtocolAsync(database, c2, 2, "17-A-01", "Dispatcher-A");
				reportedOnly = await AddCallProtocolAsync(database, c3, 1, "09_x", null);
				removed = await AddCallProtocolAsync(database, c3, 2, "17-B-01", "Dispatcher-A", removed: true);
				noCase = await AddCallProtocolAsync(database, c1, 2, null, "Dispatcher-A", withCase: false);
				deletedCall = await AddCallProtocolAsync(database, c4, 1, "09-E-01", "Dispatcher-A");
				oldCall = await AddCallProtocolAsync(database, c5, 1, "09-E-01", "Dispatcher-A");
				theirs = await AddCallProtocolAsync(database, c6, 3, "09-E-01", "Dispatcher-A");
			}

			var window = new DispatchCaseCandidateFilter { FromUtc = _now.AddDays(-30), ToUtc = _now };
			var all = await Reviews().GetCandidatesAsync(1, window, 100);
			all.Select(c => c.CallProtocolId).Should().BeEquivalentTo(new[] { ours, ours2, falls, reportedOnly },
				"removed rows, rows without a case, deleted calls, calls outside the window and other departments are never candidates");
			all.First().CallProtocolId.Should().Be(reportedOnly, "newest call first");
			var first = all.Single(c => c.CallProtocolId == ours);
			first.Overridden.Should().BeTrue();
			first.CallNumber.Should().Be("26-1");
			first.ProtocolVersionId.Should().Be(_versionId);
			first.LoggedOn.Should().BeCloseTo(_now.AddDays(-3), TimeSpan.FromSeconds(1));
			all.Single(c => c.CallProtocolId == reportedOnly).DispatcherUserId.Should().Be("Dispatcher-B");

			(await Reviews().GetCandidatesAsync(1, new DispatchCaseCandidateFilter { FromUtc = window.FromUtc, ToUtc = window.ToUtc, DispatchProtocolId = 1 }, 100))
				.Select(c => c.CallProtocolId).Should().BeEquivalentTo(new[] { ours, ours2, reportedOnly });
			(await Reviews().GetCandidatesAsync(1, new DispatchCaseCandidateFilter { FromUtc = window.FromUtc, ToUtc = window.ToUtc, Determinant = "09-e" }, 100))
				.Select(c => c.CallProtocolId).Should().BeEquivalentTo(new[] { ours }, "a determinant prefix, compared case-insensitively");
			(await Reviews().GetCandidatesAsync(1, new DispatchCaseCandidateFilter { FromUtc = window.FromUtc, ToUtc = window.ToUtc, Determinant = "09_" }, 100))
				.Select(c => c.CallProtocolId).Should().BeEquivalentTo(new[] { reportedOnly }, "an underscore in a determinant is a character, not a wildcard");
			(await Reviews().GetCandidatesAsync(1, new DispatchCaseCandidateFilter { FromUtc = window.FromUtc, ToUtc = window.ToUtc, DispatcherUserId = "DISPATCHER-A" }, 100))
				.Select(c => c.CallProtocolId).Should().BeEquivalentTo(new[] { ours, ours2, falls });
			(await Reviews().GetCandidatesAsync(1, new DispatchCaseCandidateFilter { FromUtc = window.FromUtc, ToUtc = window.ToUtc, DispatcherUserId = "dispatcher-b" }, 100))
				.Select(c => c.CallProtocolId).Should().BeEquivalentTo(new[] { reportedOnly }, "the reporting member is the dispatcher when nobody attached the protocol");
			(await Reviews().GetCandidatesAsync(1, window, 2)).Should().HaveCount(2);

			var cases = await Reviews().GetCaseJsonAsync(1, new[] { ours, theirs });
			cases.Keys.Should().BeEquivalentTo(new[] { ours }, "another department's case is never read");
			CallProtocolCase.Parse(cases[ours]).ProtocolVersionId.Should().Be(70);

			(await Reviews().TryInsertAsync(NewReview(1, 1, ours, "Dispatcher-A", _now.AddDays(-3)))).Should().BeTrue();
			(await Reviews().TryInsertAsync(NewReview(1, 1, ours, "Dispatcher-A", _now.AddDays(-3)))).Should().BeFalse("one review per call protocol");
			(await Reviews().GetCandidatesAsync(1, window, 100)).Single(c => c.CallProtocolId == ours).AlreadyReviewed.Should().BeTrue();
			(await Reviews().GetCandidatesAsync(1, window, 100)).Where(c => c.CallProtocolId != ours).Should().OnlyContain(c => !c.AlreadyReviewed);
			_ = (removed, noCase, deletedCall, oldCall);
		}

		[Test, Order(4)]
		public async Task Scores_are_versioned_and_a_review_is_acknowledged_and_replied_to_once_by_its_dispatcher()
		{
			var review = NewReview(1, 2, 9001, "Dispatcher-A", _now.AddDays(-2));
			(await Reviews().TryInsertAsync(review)).Should().BeTrue();
			review.DispatchCaseReviewId.Should().BeGreaterThan(0);
			(await Reviews().GetByIdForDepartmentAsync(2, review.DispatchCaseReviewId)).Should().BeNull("every read is scoped to the department");

			(await Reviews().TryAssignAsync(1, review.DispatchCaseReviewId, "officer", 1, _now)).Should().BeTrue();
			(await Reviews().TryAssignAsync(1, review.DispatchCaseReviewId, "other", 1, _now)).Should().BeFalse("the version moved on");

			var loaded = await Reviews().GetByIdForDepartmentAsync(1, review.DispatchCaseReviewId);
			loaded.AssignedToUserId.Should().Be("officer");
			loaded.RowVersion.Should().Be(2);
			loaded.SampledOn.Kind.Should().Be(DateTimeKind.Utc);

			var stale = loaded.RowVersion - 1;
			loaded.Status = (int)DispatchCaseReviewStatuses.InReview;
			loaded.ItemsJson = "[{\"key\":\"q1\",\"kind\":1,\"compliance\":1}]";
			loaded.Score = null;
			(await Reviews().TrySaveScoresAsync(loaded, stale)).Should().BeFalse();
			(await Reviews().TrySaveScoresAsync(loaded, loaded.RowVersion)).Should().BeTrue("a draft without a score saves a null score");

			loaded.Status = (int)DispatchCaseReviewStatuses.Sent;
			loaded.Score = 88;
			loaded.QuestionCount = 4;
			loaded.QuestionPoints = 350;
			loaded.ReviewerUserId = "officer";
			loaded.ReviewerComments = "Good";
			loaded.SentOn = _now;
			(await Reviews().TrySaveScoresAsync(loaded, loaded.RowVersion)).Should().BeTrue();
			(await Reviews().TrySaveScoresAsync(loaded, loaded.RowVersion)).Should().BeFalse("a sent review never changes");
			(await Reviews().TryDeleteOpenAsync(1, loaded.DispatchCaseReviewId)).Should().BeFalse("a sent review never leaves");

			var sent = await Reviews().GetByIdForDepartmentAsync(1, loaded.DispatchCaseReviewId);
			(sent.Score, sent.QuestionPoints, sent.Status).Should().Be((88, 350, (int)DispatchCaseReviewStatuses.Sent));
			sent.SentOn.Should().BeCloseTo(_now, TimeSpan.FromSeconds(1));

			(await Reviews().TryCommentAsync(1, sent.DispatchCaseReviewId, "officer", "not mine", _now)).Should().BeFalse("only the dispatcher replies");
			(await Reviews().TryAcknowledgeAsync(1, sent.DispatchCaseReviewId, "someone-else", null, _now)).Should().BeFalse();
			(await Reviews().TryAcknowledgeAsync(1, sent.DispatchCaseReviewId, "DISPATCHER-A", "Understood", _now)).Should().BeTrue();
			(await Reviews().TryAcknowledgeAsync(1, sent.DispatchCaseReviewId, "dispatcher-a", "Again", _now)).Should().BeFalse("acknowledged once");
			(await Reviews().TryCommentAsync(1, sent.DispatchCaseReviewId, "dispatcher-a", "Second reply", _now)).Should().BeFalse("one reply, given with the acknowledgment");

			var done = await Reviews().GetByIdForDepartmentAsync(1, sent.DispatchCaseReviewId);
			done.Status.Should().Be((int)DispatchCaseReviewStatuses.Acknowledged);
			done.DispatcherComment.Should().Be("Understood");
			done.AcknowledgedOn.Should().NotBeNull();

			// A review acknowledged without a comment still takes its one reply.
			var other = NewReview(1, 3, 9002, "Dispatcher-A", _now.AddDays(-1));
			other.Status = (int)DispatchCaseReviewStatuses.Sent;
			other.SentOn = _now;
			(await Reviews().TryInsertAsync(other)).Should().BeTrue();
			(await Reviews().TryAcknowledgeAsync(1, other.DispatchCaseReviewId, "dispatcher-a", null, _now)).Should().BeTrue();
			(await Reviews().TryCommentAsync(1, other.DispatchCaseReviewId, "Dispatcher-A", "Late reply", _now)).Should().BeTrue();
			(await Reviews().TryCommentAsync(1, other.DispatchCaseReviewId, "Dispatcher-A", "Another", _now)).Should().BeFalse();
			(await Reviews().GetByIdForDepartmentAsync(1, other.DispatchCaseReviewId)).DispatcherComment.Should().Be("Late reply");
		}

		[Test, Order(5)]
		public async Task The_queue_the_dispatchers_list_the_report_window_and_the_counts_read_back()
		{
			var open = NewReview(1, 4, 9101, "Dispatcher-C", _now.AddDays(-5));
			(await Reviews().TryInsertAsync(open)).Should().BeTrue();
			var foreign = NewReview(2, 5, 9102, "Dispatcher-C", _now.AddDays(-5));
			foreign.Status = (int)DispatchCaseReviewStatuses.Sent;
			foreign.SentOn = _now;
			(await Reviews().TryInsertAsync(foreign)).Should().BeTrue();

			(await Reviews().GetOpenAsync(1, 100)).Select(r => r.DispatchCaseReviewId).Should().Contain(open.DispatchCaseReviewId).And.NotContain(foreign.DispatchCaseReviewId);
			(await Reviews().GetOpenAsync(1, 100)).Should().OnlyContain(r => r.Status == 0 || r.Status == 1);

			var mine = await Reviews().GetSentForDispatcherAsync(1, "DISPATCHER-a", 50);
			mine.Should().HaveCount(2).And.OnlyContain(r => r.DepartmentId == 1 && r.IsSent);
			(await Reviews().GetSentForDispatcherAsync(1, "Dispatcher-C", 50)).Should().BeEmpty("queued reviews are not the dispatcher's yet, and department 2's are not department 1's");

			(await Reviews().GetSentForReportAsync(1, _now.AddDays(-30), _now, null, null, null, 100)).Should().HaveCount(2);
			(await Reviews().GetSentForReportAsync(1, _now.AddDays(-30), _now, 1, 70, "dispatcher-A", 100)).Should().HaveCount(2);
			(await Reviews().GetSentForReportAsync(1, _now.AddDays(-30), _now, 2, null, null, 100)).Should().BeEmpty();
			(await Reviews().GetSentForReportAsync(1, _now.AddDays(-1.5), _now, null, null, null, 100)).Should().HaveCount(1, "the window is on the call's date");

			var counts = await Reviews().GetStatusCountsAsync(1);
			counts[(int)DispatchCaseReviewStatuses.Queued].Should().BeGreaterThanOrEqualTo(2);
			counts[(int)DispatchCaseReviewStatuses.Acknowledged].Should().Be(2);

			(await Reviews().TryDeleteOpenAsync(2, open.DispatchCaseReviewId)).Should().BeFalse("another department never removes one");
			(await Reviews().TryDeleteOpenAsync(1, open.DispatchCaseReviewId)).Should().BeTrue();
			(await Reviews().GetByIdForDepartmentAsync(1, open.DispatchCaseReviewId)).Should().BeNull();
		}

		[Test, Order(6)]
		public async Task Both_department_cleanups_take_the_departments_reviews_and_leave_the_others()
		{
			await using var database = Connect(_connection);
			await database.OpenAsync();
			(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("DispatchCaseReviews")} WHERE {T("DepartmentId")} = 1")).Should().BeGreaterThan(0);

			await using (var transaction = await database.BeginTransactionAsync())
			{
				await UnlinkedDepartmentCleanup.DeleteWithinTransactionAsync(database, transaction, 1, type);
				await transaction.CommitAsync();
			}
			(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("DispatchCaseReviews")} WHERE {T("DepartmentId")} = 1")).Should().Be(0);

			(await Reviews().TryInsertAsync(NewReview(1, 6, 9201, "Dispatcher-A", _now))).Should().BeTrue();
			await using (var transaction = await database.BeginTransactionAsync())
			{
				await ProtocolDepartmentCleanup.DeleteWithinTransactionAsync(database, transaction, 1, type);
				await transaction.CommitAsync();
			}

			(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("DispatchCaseReviews")} WHERE {T("DepartmentId")} = 1")).Should().Be(0);
			(await database.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {T("DispatchCaseReviews")} WHERE {T("DepartmentId")} = 2")).Should().Be(1);
		}
	}
}
