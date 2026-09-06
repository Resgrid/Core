using System;
using System.Collections.Generic;
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
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Providers.Migrations.Migrations;
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;
using Resgrid.Repositories.DataRepository.Transactions;

namespace Resgrid.Tests.Rms
{
	/// <summary>Executes the shipped RMS SQL Server migrations and real repository transactions in a disposable database.</summary>
	[TestFixture, NonParallelizable]
	public class RmsRetentionDatabaseTests
	{
		private string _database;
		private string _connection;
		private DatabaseTypes _previousType;
		private const string Master = "Server=(localdb)\\ResgridRmsVerification;Integrated Security=true;Initial Catalog=master;TrustServerCertificate=true";

		[OneTimeSetUp]
		public async Task CreateIsolatedDatabase()
		{
			if (Environment.GetEnvironmentVariable("RESGRID_RMS_DATABASE_TESTS") != "1") Assert.Ignore("Set RESGRID_RMS_DATABASE_TESTS=1 to run isolated LocalDB integration tests.");
			_previousType = DataConfig.DatabaseType;
			DataConfig.DatabaseType = DatabaseTypes.SqlServer;
			_database = "ResgridRmsVerification_" + Guid.NewGuid().ToString("N");
			using var master = new SqlConnection(Master);
			await master.ExecuteAsync($"CREATE DATABASE [{_database}]");
			_connection = new SqlConnectionStringBuilder(Master) { InitialCatalog = _database }.ConnectionString;
			using (var db = new SqlConnection(_connection))
				await db.ExecuteAsync("CREATE TABLE Departments (DepartmentId int NOT NULL PRIMARY KEY); INSERT Departments VALUES (11),(12); CREATE TABLE DepartmentSettings (DepartmentSettingId int IDENTITY PRIMARY KEY, DepartmentId int NOT NULL, SettingType int NOT NULL, Setting nvarchar(max) NOT NULL); CREATE TABLE WorkflowRuns (WorkflowRunId nvarchar(36) NOT NULL PRIMARY KEY, WorkflowId nvarchar(36) NOT NULL, DepartmentId int NOT NULL, Status int NOT NULL, CompletedOn datetime2 NULL, InputPayload nvarchar(max) NULL, ErrorMessage nvarchar(max) NULL); CREATE TABLE WorkflowRunLogs (WorkflowRunLogId nvarchar(36) NOT NULL PRIMARY KEY, WorkflowRunId nvarchar(36) NOT NULL, RenderedOutput nvarchar(max) NULL, ActionResult nvarchar(max) NULL, ErrorMessage nvarchar(max) NULL);");
			var versions = new HashSet<long> { 48, 49, 124, 142, 143, 144, 145, 150, 151, 153, 154, 155, 156, 157, 160, 164, 165, 166, 167, 168, 169, 170, 171, 173 };
			var migrations = typeof(M0150_AddRmsRecordsCore).Assembly.GetTypes().Where(t => typeof(IMigration).IsAssignableFrom(t) && !t.IsAbstract)
				.Where(t => t.GetCustomAttributes(typeof(MigrationAttribute), false).Cast<MigrationAttribute>().Any(a => versions.Contains(a.Version)))
				.Select(t => (IMigration)Activator.CreateInstance(t)).ToList();
			var source = new Mock<IMigrationSource>(); source.Setup(s => s.GetMigrations()).Returns(migrations);
			using var services = new ServiceCollection().AddFluentMigratorCore()
				.ConfigureRunner(r => r.AddSqlServer2016().WithGlobalConnectionString(_connection))
				.AddSingleton(source.Object).BuildServiceProvider();
			services.GetRequiredService<IMigrationRunner>().MigrateUp();
			TestContext.Progress.WriteLine("Migrated isolated database: " + _database);
		}

		[OneTimeTearDown]
		public async Task RemoveOnlyCreatedDatabase()
		{
			if (_database == null) return;
			DataConfig.DatabaseType = _previousType;
			SqlConnection.ClearAllPools();
			if (!_database.StartsWith("ResgridRmsVerification_", StringComparison.Ordinal) || _database.Length != 55) throw new InvalidOperationException("Unexpected test database name.");
			using var master = new SqlConnection(Master);
			await master.ExecuteAsync($"ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}]");
		}

		private IConnectionProvider Connections()
		{
			var provider = new Mock<IConnectionProvider>();
			provider.Setup(p => p.Create()).Returns(() => new SqlConnection(_connection));
			return provider.Object;
		}

		private RmsRetentionRepository Purger(IConnectionProvider connections, UnitOfWork unit)
			=> new RmsRetentionRepository(connections, new SqlServerConfiguration(), unit, Mock.Of<IQueryFactory>());

		private static IQueryFactory WriteQueries()
		{
			var list = new Mock<Resgrid.Model.Repositories.Queries.Contracts.IQueryList>();
			var queries = new System.Collections.Concurrent.ConcurrentDictionary<Type, Resgrid.Model.Repositories.Queries.Contracts.IQuery>();
			queries[typeof(Resgrid.Repositories.DataRepository.Queries.Common.InsertQuery)] = new Resgrid.Repositories.DataRepository.Queries.Common.InsertQuery(new SqlServerConfiguration());
			queries[typeof(Resgrid.Repositories.DataRepository.Queries.Common.UpdateQuery)] = new Resgrid.Repositories.DataRepository.Queries.Common.UpdateQuery(new SqlServerConfiguration());
			list.Setup(l => l.RetrieveQueryList()).Returns(queries);
			return new Resgrid.Repositories.DataRepository.Queries.QueryFactory(list.Object);
		}

		private async Task<string> SeedOperational(string definition = RmsDefinitionKeys.Training)
		{
			var id = Guid.NewGuid().ToString();
			using var db = new SqlConnection(_connection);
			await db.ExecuteAsync(@"INSERT RmsOperationalRecords (RmsOperationalRecordId,DepartmentId,ProtectionId,DefinitionKey,DefinitionVersion,LifecyclePreset,State,AuthorUserId,OwnerUserId,CreatedByUserId,DraftReference,CreatedOn,ModifiedOn,RowVersion,FinalizedOn)
VALUES (@Id,11,@Id,@Definition,1,0,@State,'officer','officer','officer',LEFT(@Id,20),@Old,@Old,1,@Old)", new { Id = id, Definition = definition, State = (int)RmsRecordState.Finalized, Old = DateTime.UtcNow.AddYears(-10) });
			return id;
		}

		[Test]
		[TestCase("expired")]
		[TestCase("revoked")]
		[TestCase("foreign")]
		[TestCase("missing")]
		public async Task Saved_share_anchors_cannot_outlive_the_current_grant(string change)
		{
			var id = await SeedOperational(); await SeedSearchSource(id);
			var incident = Guid.NewGuid().ToString(); var viewer = Guid.NewGuid().ToString();
			var group = 800000 + (int)(uint.Parse(viewer.Substring(0, 6), System.Globalization.NumberStyles.HexNumber) % 100000);
			using var db = new SqlConnection(_connection);
			await db.ExecuteAsync(@"INSERT RmsIncidentReports (RmsIncidentReportId,DepartmentId,ProtectionId,CallId,ReportingEntityId,DefinitionKey,DefinitionVersion,LifecyclePreset,State,AuthorUserId,CreatedOn,ModifiedOn)
VALUES (@Id,11,@Id,7654321,@Viewer,@Definition,1,0,0,'another-officer',SYSUTCDATETIME(),SYSUTCDATETIME())", new { Id = incident, Viewer = viewer, Definition = RmsDefinitionKeys.NerisIncidentReport });
			var connections = Connections(); using var unit = new UnitOfWork(connections);
			var scopes = new RmsRecordGroupScopesRepository(connections, new SqlServerConfiguration(), unit, WriteQueries());
			var shares = new RmsRecordSharesRepository(connections, new SqlServerConfiguration(), unit, WriteQueries());
			foreach (var record in new[] { id, incident })
			{
				await db.ExecuteAsync("INSERT RmsRecordGroupScopes (DepartmentId,RecordId,DepartmentGroupId,AnchorType,CreatedOn) VALUES (11,@Id,@Group,@Anchor,SYSUTCDATETIME())", new { Id = record, Group = group, Anchor = (int)RmsGroupScopeAnchorType.Share });
				var shareId = Guid.NewGuid().ToString();
				await shares.InsertAsync(new RmsRecordShare { RmsRecordShareId = shareId, ProtectionId = shareId, DepartmentId = 11, RecordId = record, DepartmentGroupId = group, GrantedByUserId = "officer", GrantedOn = DateTime.UtcNow, CreatedOn = DateTime.UtcNow, ModifiedOn = DateTime.UtcNow, RowVersion = 1, Reason = "private-grant-reason" }, CancellationToken.None);
			}
			var operational = new RmsOperationalRecordsRepository(connections, new SqlServerConfiguration(), unit, WriteQueries());
			var incidents = new RmsIncidentReportsRepository(connections, new SqlServerConfiguration(), unit, WriteQueries());
			var projections = new RmsRecordSearchProjectionsRepository(connections, new SqlServerConfiguration(), unit, WriteQueries());
			var incidentQuery = new RmsIncidentReportQuery { VisibleGroupIds = new[] { group }, ViewerUserId = viewer };
			var searchQuery = new RmsRecordQuery { VisibleGroupIds = new[] { group }, ViewerUserId = viewer };
			(await operational.CountVisibleAsync(11, new[] { (int)RmsRecordState.Finalized }, new List<int> { group }, viewer)).Should().Be(1);
			(await incidents.CountAsync(11, incidentQuery)).Should().Be(1);
			(await projections.CountAsync(11, searchQuery)).Should().Be(1);
			(await scopes.GetEffectiveSharesAsync(11, new[] { group })).Should().HaveCount(2).And.OnlyContain(s => s.Reason == null);
			var mutation = change switch
			{
				"expired" => "UPDATE RmsRecordShares SET ExpiresOn=DATEADD(second,-1,SYSUTCDATETIME())",
				"revoked" => "UPDATE RmsRecordShares SET RevokedOn=SYSUTCDATETIME()",
				"foreign" => "UPDATE RmsRecordShares SET DepartmentId=12",
				_ => "DELETE FROM RmsRecordShares"
			};
			await db.ExecuteAsync(mutation + " WHERE DepartmentId=11 AND RecordId IN @Ids", new { Ids = new[] { id, incident } });
			(await scopes.GetForRecordAsync(11, id)).Should().BeEmpty();
			(await scopes.GetForRecordsAsync(11, new[] { id, incident })).Should().BeEmpty();
			(await scopes.GetEffectiveSharesAsync(11, new[] { group })).Should().BeEmpty();
			(await scopes.CountRecordsByGroupAsync(11)).Should().NotContainKey(group);
			(await operational.CountVisibleAsync(11, new[] { (int)RmsRecordState.Finalized }, new List<int> { group }, viewer)).Should().Be(0);
			(await incidents.CountAsync(11, incidentQuery)).Should().Be(0);
			(await incidents.QueryAsync(11, incidentQuery)).Should().BeEmpty();
			(await projections.CountAsync(11, searchQuery)).Should().Be(0);
			(await projections.QueryAsync(11, searchQuery)).Should().BeEmpty();
			// An independently valid station anchor still grants the group's access.
			await db.ExecuteAsync("INSERT RmsRecordGroupScopes (DepartmentId,RecordId,DepartmentGroupId,AnchorType,CreatedOn) VALUES (11,@Id,@Group,@Anchor,SYSUTCDATETIME())", new { Id = id, Group = group, Anchor = (int)RmsGroupScopeAnchorType.RecordGroup });
			(await scopes.GetForRecordAsync(11, id)).Should().ContainSingle();
			(await operational.CountVisibleAsync(11, new[] { (int)RmsRecordState.Finalized }, new List<int> { group }, viewer)).Should().Be(1);
		}

		[Test]
		public async Task Dashboard_counts_match_station_owner_participant_and_parent_scope_in_the_real_database()
		{
			var viewer = Guid.NewGuid().ToString(); var groupA = 98761; var groupB = 98762;
			var operational = new[] { await SeedOperational(), await SeedOperational(), await SeedOperational(), await SeedOperational() };
			var reports = new[] { Guid.NewGuid().ToString(), Guid.NewGuid().ToString() }; var analyses = new[] { Guid.NewGuid().ToString(), Guid.NewGuid().ToString() };
			using var db = new SqlConnection(_connection);
			await db.ExecuteAsync("UPDATE RmsOperationalRecords SET State=@State, OwnerUserId=CASE WHEN RmsOperationalRecordId=@Owned THEN @Viewer ELSE 'another-officer' END WHERE RmsOperationalRecordId IN @Ids", new { State = (int)RmsRecordState.Draft, Owned = operational[2], Viewer = viewer, Ids = operational });
			await db.ExecuteAsync("INSERT RmsRecordParticipants (RmsRecordParticipantId,DepartmentId,ProtectionId,RecordId,UserId,CreatedOn,ModifiedOn) VALUES (@Id,11,@Id,@RecordId,@Viewer,@Now,@Now)", new { Id = Guid.NewGuid().ToString(), RecordId = operational[3], Viewer = viewer, Now = DateTime.UtcNow });
			for (var i = 0; i < 2; i++)
			{
				await db.ExecuteAsync(@"INSERT RmsIncidentReports (RmsIncidentReportId,DepartmentId,ProtectionId,CallId,ReportingEntityId,DefinitionKey,DefinitionVersion,LifecyclePreset,State,AuthorUserId,CreatedOn,ModifiedOn)
VALUES (@Id,11,@Id,@Call,@Entity,@Definition,1,0,@State,'another-officer',@Now,@Now);
INSERT RmsIncidentAnalyses (RmsIncidentAnalysisId,DepartmentId,ProtectionId,IncidentReportId,State,AuthorUserId,CreatedOn,ModifiedOn) VALUES (@Analysis,11,@Analysis,@Id,@AnalysisState,'another-officer',@Now,@Now);",
					new { Id = reports[i], Analysis = analyses[i], Call = 91030 + i, Entity = viewer, Definition = RmsDefinitionKeys.NerisIncidentReport, State = (int)RmsRecordState.Rejected, AnalysisState = (int)RmsIncidentAnalysisState.Finalized, Now = DateTime.UtcNow });
				foreach (var id in new[] { operational[i], reports[i] }) await db.ExecuteAsync("INSERT RmsRecordGroupScopes (DepartmentId,RecordId,DepartmentGroupId,AnchorType,CreatedOn) VALUES (11,@Id,@Group,0,@Now)", new { Id = id, Group = i == 0 ? groupA : groupB, Now = DateTime.UtcNow });
			}
			foreach (var pair in operational.Select(id => (id, RmsRecordKind.Operational)).Concat(reports.Select(id => (id, RmsRecordKind.IncidentReport))).Concat(analyses.Select(id => (id, RmsRecordKind.IncidentAnalysis))))
				await db.ExecuteAsync("INSERT RmsRecordDueStates (RmsRecordDueStateId,DepartmentId,RecordId,RecordKind,Obligation,LastEmittedState,CreatedOn,ModifiedOn) VALUES (@Id,11,@RecordId,@Kind,0,@State,@Now,@Now)", new { Id = Guid.NewGuid().ToString(), RecordId = pair.id, Kind = (int)pair.Item2, State = (int)RmsDueState.Overdue, Now = DateTime.UtcNow });
			var connections = Connections(); using var unit = new UnitOfWork(connections);
			var auth = new Mock<Resgrid.Model.Services.IRecordsAuthorizationService>();
			auth.Setup(a => a.IsActiveMemberAsync(viewer, 11)).ReturnsAsync(true); auth.Setup(a => a.GetVisibleGroupIdsAsync(viewer, 11)).ReturnsAsync(new List<int> { groupA });
			var disclosures = new Mock<IRmsDisclosureRequestsRepository>(MockBehavior.Strict);
			var dashboard = new Resgrid.Services.Records.RecordsDashboardService(new RmsOperationalRecordsRepository(connections, new SqlServerConfiguration(), unit, WriteQueries()),
				new RmsIncidentReportsRepository(connections, new SqlServerConfiguration(), unit, WriteQueries()), new RmsIncidentAnalysesRepository(connections, new SqlServerConfiguration(), unit, WriteQueries()),
				new RmsRecordDueStatesRepository(connections, new SqlServerConfiguration(), unit, WriteQueries()), disclosures.Object, Mock.Of<Resgrid.Model.Providers.INerisProfileService>(), Mock.Of<Resgrid.Model.Services.ICallsService>(), auth.Object);
			var first = await dashboard.GetAsync(11, viewer); first.Warnings.Should().BeEmpty(); first.OperationalDrafts.Should().Be(3); first.IncidentRejected.Should().Be(1); first.AnalysesAwaitingFiling.Should().Be(1); first.Overdue.Should().Be(5);
			await db.ExecuteAsync("UPDATE RmsRecordParticipants SET DeletedOn=@Now WHERE RecordId=@Participant; UPDATE RmsIncidentReports SET PurgedOn=@Now WHERE RmsIncidentReportId=@Report", new { Now = DateTime.UtcNow, Participant = operational[3], Report = reports[0] });
			var after = await dashboard.GetAsync(11, viewer); after.Warnings.Should().BeEmpty(); after.OperationalDrafts.Should().Be(2); after.IncidentRejected.Should().Be(0); after.AnalysesAwaitingFiling.Should().Be(0); after.Overdue.Should().Be(2);
			auth.Setup(a => a.GetVisibleGroupIdsAsync(viewer, 11)).ReturnsAsync(new List<int>());
			var ownerOnly = await dashboard.GetAsync(11, viewer); ownerOnly.OperationalDrafts.Should().Be(1); ownerOnly.Overdue.Should().Be(1); disclosures.VerifyNoOtherCalls();
		}

		[Test]
		public async Task Activity_queries_use_official_dates_and_call_while_the_working_amendment_moves_elsewhere()
		{
			var id = await SeedOperational(); var revisionId = Guid.NewGuid().ToString(); var start = new DateTime(2004, 3, 5, 8, 0, 0, DateTimeKind.Utc);
			var record = new RmsOperationalRecord { RmsOperationalRecordId = id, DepartmentId = 11, RecordType = (int)RmsOperationalRecordType.Training,
				DefinitionKey = RmsDefinitionKeys.Training, DefinitionVersion = 1, StartedOn = start, EndedOn = start.AddHours(2), CallId = 91021, AuthorUserId = "officer" };
			var snapshot = Resgrid.Services.Records.RecordSnapshotSerializer.Serialize(Resgrid.Services.Records.RecordSnapshotSerializer.Build(new RecordAggregate { Record = record, Details = new RmsOperationalRecordDetail { RecordId = id, DepartmentId = 11, Course = "Ropes" } }));
			using var db = new SqlConnection(_connection);
			await db.ExecuteAsync(@"INSERT RmsRevisions (RmsRevisionId,DepartmentId,ProtectionId,RecordId,RecordKind,RevisionNumber,Transition,DefinitionKey,DefinitionVersion,SnapshotJson,Checksum,ActorUserId,CreatedOn,OriginClient)
VALUES (@Revision,11,@Revision,@Id,@Kind,1,@Transition,@Definition,1,@Snapshot,@Checksum,'officer',@Old,0);
UPDATE RmsOperationalRecords SET CurrentRevisionId=@Revision,AmendsRevisionId=@Revision,StartedOn=@Draft,EndedOn=@DraftEnd,CallId=91022 WHERE RmsOperationalRecordId=@Id;",
				new { Revision = revisionId, Id = id, Kind = (int)RmsRecordKind.Operational, Transition = (int)RmsRevisionTransition.Finalized, Definition = RmsDefinitionKeys.Training, Snapshot = snapshot, Checksum = Resgrid.Services.Records.RecordSnapshotSerializer.Checksum(snapshot), Old = start.AddDays(1), Draft = start.AddYears(3), DraftEnd = start.AddYears(3).AddHours(20) });
			var connections = Connections(); using var unit = new UnitOfWork(connections);
			var records = new RmsOperationalRecordsRepository(connections, new SqlServerConfiguration(), unit, WriteQueries());
			var revisions = new RmsRevisionsRepository(connections, new SqlServerConfiguration(), unit, WriteQueries());
			var scopes = new RmsRecordGroupScopesRepository(connections, new SqlServerConfiguration(), unit, WriteQueries());
			var auth = new Mock<Resgrid.Model.Services.IRecordsAuthorizationService>(); auth.Setup(a => a.GetVisibleGroupIdsAsync("officer", 11)).ReturnsAsync((List<int>)null);
			var cutover = new Mock<Resgrid.Model.Services.IRecordsCutoverService>(); cutover.Setup(c => c.GetModuleStateAsync(11, It.IsAny<bool>())).ReturnsAsync(new RecordsModuleState { FlagEnabled = true });
			var reporting = new Resgrid.Services.Records.RecordsReportingService(Mock.Of<Resgrid.Model.Services.IWorkLogsService>(), cutover.Object, records, revisions, scopes, auth.Object);
			var activity = (await reporting.GetActivityAsync(11, "officer", RmsOperationalRecordType.Training, start.Date, start.Date.AddDays(1))).Single(e => e.SourceId == id);
			activity.StartedOn.Should().Be(start); activity.EndedOn.Should().Be(start.AddHours(2)); activity.CallId.Should().Be(91021); activity.Course.Should().Be("Ropes");
			(await reporting.GetActivityAsync(11, "officer", RmsOperationalRecordType.Training, start.AddYears(3).Date, start.AddYears(3).Date.AddDays(1))).Should().NotContain(e => e.SourceId == id);
			(await reporting.GetCallActivityAsync(11, "officer", 91021)).Should().Contain(e => e.SourceId == id);
			(await reporting.GetCallActivityAsync(11, "officer", 91022)).Should().NotContain(e => e.SourceId == id);
			(await records.GetByDefinitionAndStartedRangeAsync(12, RmsDefinitionKeys.Training, new[] { (int)RmsRecordState.Finalized }, start.Date, start.Date.AddDays(1))).Should().NotContain(r => r.RmsOperationalRecordId == id);
		}

		[Test]
		public async Task Recovery_decisions_are_tenant_scoped_lease_fenced_and_only_one_concurrent_administrator_can_win()
		{
			var recordId = await SeedOperational(); var submissionId = Guid.NewGuid().ToString(); using var db = new SqlConnection(_connection);
			await db.ExecuteAsync("INSERT RmsSubmissions (RmsSubmissionId,DepartmentId,ProtectionId,RecordId,RecordKind,Destination,DestinationIdentity,IdempotencyKey,State,RequiresReconciliation,CreatePendingReceipt,PayloadJson,ResponseJson,LeaseOwner,LeaseExpiresOn,QueuedOn,CreatedOn,ModifiedOn) VALUES (@Id,11,@Id,@RecordId,1,'NERIS','original-destination',@Id,@State,1,1,'unchanged-payload','unchanged-response','worker',@Expiry,@Now,@Now,@Now)",
				new { Id = submissionId, RecordId = recordId, State = (int)RmsSubmissionState.Failed, Expiry = DateTime.UtcNow.AddMinutes(2), Now = DateTime.UtcNow });
			async Task<bool> Resolve(int department = 11, string destination = "original-destination")
			{
				var connections = Connections(); using var unit = new UnitOfWork(connections); unit.CreateOrGetConnection();
				var repository = new RmsSubmissionsRepository(connections, new SqlServerConfiguration(), unit, WriteQueries());
				var won = await repository.TryConfirmNotCreatedAsync(department, submissionId, 1, destination, DateTime.UtcNow);
				unit.CommitChanges(); return won;
			}
			(await Resolve()).Should().BeFalse("an active worker still owns the outcome");
			await db.ExecuteAsync("UPDATE RmsSubmissions SET LeaseExpiresOn=NULL WHERE RmsSubmissionId=@Id", new { Id = submissionId });
			(await Resolve(12)).Should().BeFalse(); (await Resolve(destination: "other-destination")).Should().BeFalse();
			var results = await Task.WhenAll(Resolve(), Resolve()); results.Count(won => won).Should().Be(1);
			var saved = await db.QuerySingleAsync<RmsSubmission>("SELECT * FROM RmsSubmissions WHERE RmsSubmissionId=@Id", new { Id = submissionId });
			saved.State.Should().Be((int)RmsSubmissionState.Rejected); saved.NextAttemptOn.Should().BeNull(); saved.RequiresReconciliation.Should().BeFalse(); saved.CreatePendingReceipt.Should().BeFalse();
			saved.RowVersion.Should().Be(2); saved.PayloadJson.Should().Be("unchanged-payload"); saved.ResponseJson.Should().Be("unchanged-response");
			await db.ExecuteAsync("UPDATE RmsSubmissions SET RowVersion=1, ExternalId='existing-filing' WHERE RmsSubmissionId=@Id", new { Id = submissionId });
			(await Resolve()).Should().BeFalse("a known receipt cannot be declared absent");
		}

		[Test]
		public async Task Worker_fence_and_officer_supersede_take_department_before_submission_locks()
		{
			var recordId = await SeedOperational(); var submissionId = Guid.NewGuid().ToString(); using var db = new SqlConnection(_connection);
			await db.ExecuteAsync("INSERT RmsSubmissions (RmsSubmissionId,DepartmentId,ProtectionId,RecordId,RecordKind,Destination,IdempotencyKey,State,LeaseOwner,LeaseExpiresOn,QueuedOn,CreatedOn,ModifiedOn) VALUES (@Id,11,@Id,@RecordId,1,'NERIS',@Id,@State,'worker',@Expiry,@Now,@Now,@Now)", new { Id = submissionId, RecordId = recordId, State = (int)RmsSubmissionState.Queued, Expiry = DateTime.UtcNow.AddMinutes(1), Now = DateTime.UtcNow });
			var connections = Connections(); using var officer = new UnitOfWork(connections); using var worker = new UnitOfWork(connections); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
			var officerRecords = new RmsOperationalRecordsRepository(connections, new SqlServerConfiguration(), officer, WriteQueries());
			var workerRecords = new RmsOperationalRecordsRepository(connections, new SqlServerConfiguration(), worker, WriteQueries());
			var officerSubmissions = new RmsSubmissionsRepository(connections, new SqlServerConfiguration(), officer, WriteQueries());
			var workerSubmissions = new RmsSubmissionsRepository(connections, new SqlServerConfiguration(), worker, WriteQueries());
			officer.CreateOrGetConnection(); worker.CreateOrGetConnection();
			var workerSession = await worker.Connection.ExecuteScalarAsync<int>("SELECT @@SPID", transaction: worker.Transaction);
			(await officerRecords.TryBumpRowVersionAsync(11, recordId, 1, timeout.Token)).Should().BeTrue();
			async Task<bool> PersistWorker()
			{
				if (!await workerSubmissions.TryFenceLeaseAsync(11, submissionId, 1, "worker", DateTime.UtcNow, timeout.Token)) { worker.CommitChanges(); return false; }
				var result = await workerRecords.TryBumpRowVersionAsync(11, recordId, 1, timeout.Token); worker.CommitChanges(); return result;
			}
			var persist = PersistWorker();
			try
			{
			// Wait for the competing SQL command to block, not a timing guess about task scheduling.
			for (var i = 0; i < 100; i++)
			{
				if (await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.dm_exec_requests WHERE session_id=@Id AND blocking_session_id > 0", new { Id = workerSession }) > 0) break;
				await Task.Delay(10, timeout.Token);
			}
			(await officerSubmissions.SupersedeOpenForRecordAsync(11, recordId, null, DateTime.UtcNow, timeout.Token)).Should().Be(1); officer.CommitChanges();
			(await persist).Should().BeFalse();
			}
			finally
			{
				officer.DiscardChanges();
				// Drain the competing command before disposing its transaction, including on an assertion failure.
				try { await persist; } catch { }
			}
		}
		[Test]
		public async Task Report_revision_and_scope_batches_exceeding_2100_ids_preserve_every_authorized_row()
		{
			using var db=new SqlConnection(_connection); var batch=Guid.NewGuid().ToString();
			await db.ExecuteAsync(@"INSERT RmsRevisions (RmsRevisionId,DepartmentId,ProtectionId,RecordId,RecordKind,RevisionNumber,Transition,DefinitionKey,DefinitionVersion,SnapshotJson,Checksum,ActorUserId,CreatedOn,OriginClient)
SELECT Id,11,Id,Id,1,1,0,'system.training',1,'{}',@Batch,'officer',GETUTCDATE(),0 FROM (SELECT TOP (2205) CONVERT(nvarchar(36),NEWID()) AS Id FROM sys.all_objects a CROSS JOIN sys.all_objects b) s;
INSERT RmsRecordGroupScopes (DepartmentId,RecordId,DepartmentGroupId,AnchorType,CreatedOn) SELECT 11,RecordId,1,0,GETUTCDATE() FROM RmsRevisions WHERE Checksum=@Batch",new{Batch=batch});
			var ids=(await db.QueryAsync<string>("SELECT RmsRevisionId FROM RmsRevisions WHERE Checksum=@Batch",new{Batch=batch})).ToArray();
			var connections=Connections();using var unit=new UnitOfWork(connections);
			var revisions=new RmsRevisionsRepository(connections,new SqlServerConfiguration(),unit,WriteQueries());
			var scopes=new RmsRecordGroupScopesRepository(connections,new SqlServerConfiguration(),unit,WriteQueries());
			(await revisions.GetByIdsForDepartmentAsync(11,ids.Concat(ids.Take(5)))).Select(r=>r.RmsRevisionId).Should().BeEquivalentTo(ids);
			(await scopes.GetForRecordsAsync(11,ids)).Select(s=>s.RecordId).Should().BeEquivalentTo(ids);
			(await revisions.GetByIdsForDepartmentAsync(12,ids)).Should().BeEmpty(); (await scopes.GetForRecordsAsync(12,ids)).Should().BeEmpty();
		}
		[Test]
		public async Task Purge_deletes_all_record_UDF_versions_and_prevents_a_late_custom_field_writer()
		{
			var id=await SeedOperational(); var definition=Guid.NewGuid().ToString(); using var db=new SqlConnection(_connection);
			await db.ExecuteAsync(@"INSERT UdfDefinitions (UdfDefinitionId,DepartmentId,EntityType,Version,IsActive,CreatedOn,CreatedBy,RecordDefinitionKey,RecordDefinitionVersion) VALUES (@Definition,11,4,1,1,GETUTCDATE(),'officer','system.training',1);
INSERT UdfFieldValues (UdfFieldValueId,UdfFieldId,UdfDefinitionId,EntityId,EntityType,Value,CreatedOn,CreatedBy) VALUES (@Definition,@Definition,@Definition,@Id,4,'sensitive-custom-content',GETUTCDATE(),'officer')",new{Definition=definition,Id=id});
			var connections=Connections();using var unit=new UnitOfWork(connections);
			(await Purger(connections,unit).PurgeAsync(11,id,RmsRecordKind.Operational,1,DateTime.UtcNow)).Purged.Should().BeTrue();
			(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM UdfFieldValues WHERE EntityId=@Id",new{Id=id})).Should().Be(0);
			var udf=new RmsUdfDefinitionsRepository(connections,new SqlServerConfiguration(),unit,WriteQueries());
			unit.CreateOrGetConnection(); Func<Task> late=()=>udf.GuardRecordAsync(11,id,CancellationToken.None); await late.Should().ThrowAsync<InvalidOperationException>(); unit.DiscardChanges();
		}
		private async Task<RecordsSearchDocumentSource> SeedSearchSource(string recordId)
		{
			using var db = new SqlConnection(_connection);
			await db.ExecuteAsync(@"INSERT RmsRecordSearchProjections (RmsRecordSearchProjectionId,DepartmentId,ProtectionId,SourceType,SourceId,DefinitionKey,State,RecordCreatedOn,CreatedOn,ModifiedOn,DisplaySummary,RowVersion)
VALUES (@Id,11,@Id,1,@Id,'system.training',4,@Old,@Old,@Old,'confidentialpump',1)", new { Id = recordId, Old = DateTime.UtcNow.AddYears(-10) });
			return new RecordsSearchDocumentSource { Projection = await db.QuerySingleAsync<RmsRecordSearchProjection>("SELECT * FROM RmsRecordSearchProjections WHERE RmsRecordSearchProjectionId=@Id", new { Id = recordId }), Narrative = "confidential narrative", Generation = "1.0.0" };
		}

		[Test]
		public async Task A_preloaded_Lucene_writer_cannot_resurrect_content_after_the_real_SQL_purge()
		{
			var id = await SeedOperational(); var source = await SeedSearchSource(id);
			var connections = Connections(); using var unit = new UnitOfWork(connections);
			using var host = new Resgrid.Search.LuceneRecordsIndexHost(new Lucene.Net.Store.RAMDirectory(), true);
			var fence = new RmsSearchWriteFence(connections, new SqlServerConfiguration(), unit, Mock.Of<IQueryFactory>());
			var indexer = new Resgrid.Search.LuceneRecordsIndexer(host, fence);
			(await indexer.IndexAsync(new[] { source })).Should().Be(1);
			await indexer.CommitAsync(); (await indexer.CountDocumentsAsync(11)).Should().Be(1);
			var purge = await Purger(connections, unit).PurgeAsync(11, id, RmsRecordKind.Operational, 1, DateTime.UtcNow);
			purge.Purged.Should().BeTrue(); purge.SearchErasurePending.Should().BeTrue();
			var target = (await Purger(connections, unit).GetPendingSearchErasuresAsync(1000)).Single(t => t.RecordId == id);
			target.SourceIds.Should().ContainSingle().Which.Should().Be(id);
			(await indexer.IndexAsync(new[] { source })).Should().Be(0);
			await indexer.CommitAsync(); (await indexer.CountDocumentsAsync(11)).Should().Be(0);
			(await Purger(connections, unit).CompleteSearchErasureAsync(new RmsSearchErasureTarget { DepartmentId = 12, RecordKind = target.RecordKind, RecordId = id, PurgedOn = target.PurgedOn }, DateTime.UtcNow)).Should().BeFalse();
			await indexer.ExpungeDeletesAsync();
			(await Purger(connections, unit).CompleteSearchErasureAsync(target, DateTime.UtcNow)).Should().BeTrue();
			(await Purger(connections, unit).CompleteSearchErasureAsync(target, DateTime.UtcNow)).Should().BeFalse();
			(await Purger(connections, unit).GetPendingSearchErasuresAsync(1000)).Should().NotContain(t => t.RecordId == id);
			source.Projection.DisplaySummary.Should().Be("confidentialpump", "the attack reuses an unchanged pre-purge source");
		}

		[Test]
		[TestCase(true)]
		[TestCase(false)]
		public async Task RMS_activation_and_protection_enrollment_cannot_both_commit(bool rmsFirst)
		{
			var department = rmsFirst ? 1301 : 1302;
			using var db = new SqlConnection(_connection);
			await db.ExecuteAsync("INSERT Departments (DepartmentId) VALUES (@Department)", new { Department = department });
			var connections = Connections(); using var rmsUnit = new UnitOfWork(connections); using var adpUnit = new UnitOfWork(connections);
			var rms = new RmsDepartmentCutoversRepository(connections, new SqlServerConfiguration(), rmsUnit, WriteQueries());
			var adp = new DepartmentDataProtectionPolicyRepository(connections, new SqlServerConfiguration(), adpUnit, WriteQueries());
			var cutover = new RmsDepartmentCutover { DepartmentId = department, ProtectionId = Guid.NewGuid().ToString(), ActivatedByUserId = "admin", ActivatedOn = DateTime.UtcNow, CreatedOn = DateTime.UtcNow, ModifiedOn = DateTime.UtcNow, State = (int)RmsDepartmentCutoverState.Active, Reason = "activation", SourceChecksum = new string('a', 64), PermissionMappingJson = "[]", RowVersion = 1 };
			var policy = new DepartmentDataProtectionPolicy { DepartmentId = department, State = (int)DepartmentDataProtectionState.EnrollmentQueued, CreatedOn = DateTime.UtcNow, CreatedByUserId = "admin" };
			Task competing = null;
			try
			{
				if (rmsFirst)
				{
					rmsUnit.CreateOrGetConnection(); await rms.InsertAsync(cutover, CancellationToken.None, true);
					competing = adp.InsertAsync(policy, CancellationToken.None, true);
				}
				else
				{
					adpUnit.CreateOrGetConnection(); await adp.InsertAsync(policy, CancellationToken.None, true);
					competing = rms.InsertAsync(cutover, CancellationToken.None, true);
				}
				(await Task.WhenAny(competing, Task.Delay(150))).Should().NotBe(competing, "the admission paths must share the department transaction lock");
				if (rmsFirst) rmsUnit.CommitChanges(); else adpUnit.CommitChanges();
				Func<Task> loser = () => competing;
				await loser.Should().ThrowAsync<InvalidOperationException>();
				(await rms.GetByDepartmentIdAsync(department) != null).Should().Be(rmsFirst);
				(await adp.GetByDepartmentIdAsync(department) != null).Should().Be(!rmsFirst);
			}
			finally
			{
				if (rmsFirst) rmsUnit.DiscardChanges(); else adpUnit.DiscardChanges();
				if (competing != null) { try { await competing; } catch { } }
				if (rmsFirst) adpUnit.DiscardChanges(); else rmsUnit.DiscardChanges();
			}
		}

		[Test]
		public async Task Retained_RMS_content_blocks_new_protection_enrollment_even_without_an_active_cutover()
		{
			await SeedOperational();
			var connections = Connections(); using var unit = new UnitOfWork(connections);
			var repository = new DepartmentDataProtectionPolicyRepository(connections, new SqlServerConfiguration(), unit, WriteQueries());
			var policy = new DepartmentDataProtectionPolicy { DepartmentId = 11, State = (int)DepartmentDataProtectionState.EnrollmentQueued, CreatedOn = DateTime.UtcNow };
			Func<Task> insert = () => repository.InsertAsync(policy, CancellationToken.None, true);
			await insert.Should().ThrowAsync<InvalidOperationException>();
			(await repository.GetByDepartmentIdAsync(11)).Should().BeNull();
			policy.State = (int)DepartmentDataProtectionState.Disabled;
			policy = await repository.InsertAsync(policy, CancellationToken.None, true);
			Func<Task> transition = () => repository.TryTransitionStateAsync(11, DepartmentDataProtectionState.Disabled, DepartmentDataProtectionState.EnrollmentQueued, (int)DepartmentDataProtectionMigrationKind.Enrollment, "admin", CancellationToken.None);
			await transition.Should().ThrowAsync<InvalidOperationException>();
			policy.State = (int)DepartmentDataProtectionState.Enabled;
			Func<Task> update = () => repository.UpdateAsync(policy, CancellationToken.None, true);
			await update.Should().ThrowAsync<InvalidOperationException>();
			(await repository.GetByDepartmentIdAsync(11)).State.Should().Be((int)DepartmentDataProtectionState.Disabled);
		}

		[Test]
		public async Task A_command_has_one_durable_reservation_across_nodes_and_uncertain_outcomes_do_not_expire_into_retries()
		{
			var id = await SeedOperational(); var connections = Connections();
			using var firstUnit = new UnitOfWork(connections); using var secondUnit = new UnitOfWork(connections); using var restartedUnit = new UnitOfWork(connections);
			var firstRepository = new RmsCommandReceiptsRepository(connections, new SqlServerConfiguration(), firstUnit, WriteQueries());
			var secondRepository = new RmsCommandReceiptsRepository(connections, new SqlServerConfiguration(), secondUnit, WriteQueries());
			var restartedRepository = new RmsCommandReceiptsRepository(connections, new SqlServerConfiguration(), restartedUnit, WriteQueries());
			var first = new Resgrid.Services.Records.RecordsApiIdempotencyService(new MemoryRecordsApiStateStore(), firstRepository);
			var second = new Resgrid.Services.Records.RecordsApiIdempotencyService(new MemoryRecordsApiStateStore(), secondRepository);
			var restarted = new Resgrid.Services.Records.RecordsApiIdempotencyService(new MemoryRecordsApiStateStore(), restartedRepository);
			var clientKey = Guid.NewGuid().ToString(); var checksum = new string('a', 64);
			var key = Resgrid.Services.Records.RecordsApiIdempotencyService.DurableKey(11, "officer", clientKey, "Reassign");
			var reservations = await Task.WhenAll(first.TryReserveCommandAsync(11, "officer", clientKey, "Reassign", id, checksum), second.TryReserveCommandAsync(11, "officer", clientKey, "Reassign", id, checksum));
			reservations.Count(won => won).Should().Be(1);
			var winner = reservations[0] ? first : second; var loser = reservations[0] ? second : first;
			using var db = new SqlConnection(_connection);
			await db.ExecuteAsync("UPDATE RmsCommandReceipts SET CreatedOn=DATEADD(day,-2,SYSUTCDATETIME()) WHERE DepartmentId=11 AND KeyHash=@Key", new { Key = key });
			var pending = await restarted.TryGetCommandAsync(11, "officer", clientKey, "Reassign");
			pending.RecordId.Should().Be(id); pending.RequestChecksum.Should().Be(checksum); pending.IsPending.Should().BeTrue();
			(await restarted.TryReserveCommandAsync(11, "officer", clientKey, "Reassign", id, checksum)).Should().BeFalse();
			Func<Task> stolenCompletion = () => loser.RememberCommandAsync(11, "officer", clientKey, "Reassign", id, checksum);
			await stolenCompletion.Should().ThrowAsync<RecordIdempotencyException>();
			(await restartedRepository.CompleteAsync(11, key, id, checksum, Guid.NewGuid().ToString())).Should().BeFalse();
			(await restartedRepository.GetAsync(12, key)).Should().BeNull();
			await winner.RememberCommandAsync(11, "officer", clientKey, "Reassign", id, checksum);
			(await restarted.TryGetCommandAsync(11, "officer", clientKey, "Reassign")).IsPending.Should().BeFalse();
			(await restarted.TryReserveCommandAsync(11, "officer", clientKey, "Reassign", id, checksum)).Should().BeFalse();
			(await restarted.TryReserveCommandAsync(11, "other-officer", clientKey, "Reassign", id, checksum)).Should().BeTrue();
			(await restarted.TryReserveCommandAsync(11, "officer", clientKey, "Cancel", id, checksum)).Should().BeTrue();
			(await Purger(connections, restartedUnit).PurgeAsync(11, id, RmsRecordKind.Operational, 1, DateTime.UtcNow)).Purged.Should().BeTrue();
			(await restarted.TryGetCommandAsync(11, "officer", clientKey, "Reassign")).IsPending.Should().BeFalse("metadata-only receipts must not disappear and permit a delayed retry");
			Func<Task> afterPurge = () => restarted.TryReserveCommandAsync(11, "officer", Guid.NewGuid().ToString(), "Reassign", id, checksum);
			await afterPurge.Should().ThrowAsync<InvalidOperationException>();
		}

		[Test]
		public async Task Search_delta_pages_round_trip_precise_database_timestamps_without_repeating_the_last_row()
		{
			var ids = new[] { await SeedOperational(), await SeedOperational(), await SeedOperational() }.OrderBy(id => id, StringComparer.Ordinal).ToArray();
			foreach (var id in ids) await SeedSearchSource(id);
			using var db = new SqlConnection(_connection);
			await db.ExecuteAsync("UPDATE RmsRecordSearchProjections SET ModifiedOn=CONVERT(datetime2,'2050-01-01T12:34:56.1234567') WHERE RmsRecordSearchProjectionId IN @Ids", new { Ids = ids });
			var connections = Connections(); using var unit = new UnitOfWork(connections);
			var repository = new RmsRecordSearchProjectionsRepository(connections, new SqlServerConfiguration(), unit, WriteQueries());
			var first = (await repository.GetModifiedSinceAsync(11, new DateTime(2050, 1, 1), 1)).Single();
			first.RmsRecordSearchProjectionId.Should().Be(ids[0]);
			var second = (await repository.GetModifiedSinceAsync(11, first.ModifiedOn, 1, first.RmsRecordSearchProjectionId)).Single();
			second.RmsRecordSearchProjectionId.Should().Be(ids[1]);
			var third = (await repository.GetModifiedSinceAsync(11, second.ModifiedOn, 1, second.RmsRecordSearchProjectionId)).Single();
			third.RmsRecordSearchProjectionId.Should().Be(ids[2]);
			(await repository.GetModifiedSinceAsync(11, third.ModifiedOn, 1, third.RmsRecordSearchProjectionId)).Should().BeEmpty();
		}

		[Test]
		public async Task Search_erasure_acknowledgement_preserves_database_timestamp_precision()
		{
			var id = await SeedOperational(); using var db = new SqlConnection(_connection);
			// Use a timestamp that cannot round-trip through SQL Server's legacy datetime type.
			await db.ExecuteAsync("UPDATE RmsOperationalRecords SET PurgedOn=CONVERT(datetime2,'2026-09-04T12:34:56.1234567') WHERE RmsOperationalRecordId=@Id", new { Id = id });
			var connections = Connections(); using var unit = new UnitOfWork(connections);
			var repository = Purger(connections, unit);
			var target = (await repository.GetPendingSearchErasuresAsync(1000)).Single(t => t.RecordId == id);
			var wrongTimestamp = new RmsSearchErasureTarget { DepartmentId = target.DepartmentId, RecordKind = target.RecordKind, RecordId = id, PurgedOn = target.PurgedOn.AddTicks(1) };
			(await repository.CompleteSearchErasureAsync(wrongTimestamp, DateTime.UtcNow)).Should().BeFalse();
			(await repository.CompleteSearchErasureAsync(target, DateTime.UtcNow)).Should().BeTrue();
			(await repository.CompleteSearchErasureAsync(target, DateTime.UtcNow)).Should().BeFalse();
		}

		[Test]
		public async Task Retention_waits_until_the_inflight_index_mutation_has_completed()
		{
			var id = await SeedOperational(); var source = await SeedSearchSource(id);
			var connections = Connections(); using var writerUnit = new UnitOfWork(connections); using var purgeUnit = new UnitOfWork(connections);
			var fence = new RmsSearchWriteFence(connections, new SqlServerConfiguration(), writerUnit, Mock.Of<IQueryFactory>());
			Task<RmsPurgeResult> pending = null;
			try
			{
				(await fence.WithLiveSourceAsync(source, current =>
				{
					current.Narrative.Should().Be("confidential narrative");
					pending = Purger(connections, purgeUnit).PurgeAsync(11, id, RmsRecordKind.Operational, 1, DateTime.UtcNow);
					pending.Wait(150).Should().BeFalse("retention cannot pass the department lock until the index mutation returns");
					return 1;
				})).Should().Be(1);
				(await pending).Purged.Should().BeTrue();
			}
			finally { if (pending != null) await pending; }
		}

		[Test]
		public async Task Search_fence_refuses_a_stale_version_and_never_passes_foreign_content_to_the_writer()
		{
			var id = await SeedOperational(); var source = await SeedSearchSource(id);
			var connections = Connections(); using var unit = new UnitOfWork(connections); using var db = new SqlConnection(_connection);
			await db.ExecuteAsync("UPDATE RmsRecordSearchProjections SET RowVersion=2, DisplaySummary='changed' WHERE RmsRecordSearchProjectionId=@Id", new { Id = id });
			var fence = new RmsSearchWriteFence(connections, new SqlServerConfiguration(), unit, Mock.Of<IQueryFactory>());
			var writes = 0;
			Func<Task> stale = () => fence.WithLiveSourceAsync(source, current => ++writes);
			await stale.Should().ThrowAsync<InvalidOperationException>(); writes.Should().Be(0);
			source.Projection.DepartmentId = 12;
			await fence.WithLiveSourceAsync(source, current => { current.Projection.DeletedOn.Should().NotBeNull(); current.Projection.DisplaySummary.Should().BeNull(); current.Narrative.Should().BeNull(); return 0; });
		}

		[Test]
		public async Task A_delayed_audit_cannot_restore_purged_free_text_and_cannot_be_rewritten()
		{
			var id = await SeedOperational(); var connections = Connections(); using var unit = new UnitOfWork(connections);
			(await Purger(connections, unit).PurgeAsync(11, id, RmsRecordKind.Operational, 1, DateTime.UtcNow)).Purged.Should().BeTrue();
			var audit = new RmsAccessAuditsRepository(connections, new SqlServerConfiguration(), unit, WriteQueries());
			var source = new RmsAccessAudit { DepartmentId = 11, RecordId = id, ActorUserId = "officer", Action = (int)RmsAccessAuditAction.Read,
				Purpose = "private attachment name", DetailJson = "private report content", CorrelationId = "private correlation", IpAddress = "192.0.2.1", OccurredOn = DateTime.UtcNow };
			var stored = await audit.InsertAsync(source, CancellationToken.None, true);
			stored.Purpose.Should().Be("Audit for unavailable record"); stored.DetailJson.Should().BeNull(); stored.IpAddress.Should().BeNull(); stored.CorrelationId.Should().BeNull();
			source.DetailJson.Should().Be("private report content", "the caller's delayed object remains the adversarial input");
			using var db = new SqlConnection(_connection);
			(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM RmsAccessAudits WHERE RecordId=@Id AND (Purpose LIKE '%private%' OR DetailJson LIKE '%private%' OR CorrelationId LIKE '%private%')", new { Id = id })).Should().Be(0);
			Func<Task> rewrite = () => audit.UpdateAsync(source, CancellationToken.None, true);
			await rewrite.Should().ThrowAsync<InvalidOperationException>();
		}

		[Test]
		public async Task Incident_and_analysis_purge_removes_copied_content_and_refuses_delayed_workflow_and_outbox_writers()
		{
			var id = Guid.NewGuid().ToString(); var analysis = Guid.NewGuid().ToString(); var run = Guid.NewGuid().ToString();
			var old = DateTime.UtcNow.AddYears(-10); using var db = new SqlConnection(_connection);
			await db.ExecuteAsync(@"INSERT RmsIncidentReports (RmsIncidentReportId,DepartmentId,ProtectionId,CallId,ReportingEntityId,DefinitionKey,DefinitionVersion,LifecyclePreset,State,AuthorUserId,FinalizedOn,CreatedOn,ModifiedOn,RowVersion)
VALUES (@Id,11,@Id,8765432,@Id,@Definition,1,0,@State,'officer',@Old,@Old,@Old,1);
INSERT RmsIncidentAnalyses (RmsIncidentAnalysisId,DepartmentId,ProtectionId,IncidentReportId,State,AuthorUserId,FinalizedOn,CreatedOn,ModifiedOn,GeneralCause,RowVersion)
VALUES (@Analysis,11,@Analysis,@Id,@AnalysisState,'officer',@Old,@Old,@Old,'private cause',1);
INSERT WorkflowRuns (WorkflowRunId,WorkflowId,DepartmentId,Status,CompletedOn,InputPayload,AggregateId) VALUES (@Run,@Run,11,@Completed,@Old,'private workflow',@Analysis);
INSERT WorkflowRunLogs (WorkflowRunLogId,WorkflowRunId,RenderedOutput,ActionResult,ErrorMessage) VALUES (@Run,@Run,'private rendered','private result','private error');",
				new { Id = id, Analysis = analysis, Run = run, Definition = RmsDefinitionKeys.NerisIncidentReport, State = (int)RmsRecordState.Finalized,
					AnalysisState = (int)RmsIncidentAnalysisState.Finalized, Completed = (int)WorkflowRunStatus.Completed, Old = old });
			var connections = Connections(); using var unit = new UnitOfWork(connections);
			var events = new DomainEventOutboxRepository(connections, new SqlServerConfiguration(), unit, WriteQueries());
			var dispatched = await events.InsertAsync(new DomainEventOutboxEntry { DepartmentId = 11, ProducerSubsystem = DomainEventProducers.Records,
				EventId = Guid.NewGuid().ToString(), EventName = "test.analysis", AggregateType = "IncidentAnalysis", AggregateId = analysis, Sequence = 1,
				SchemaVersion = 1, PayloadJson = "private outbox content", OccurredOn = old, CreatedOn = old }, CancellationToken.None, true);
			await events.MarkDispatchedAsync(dispatched.DomainEventOutboxId, old);
			foreach (var recordId in new[] { id, analysis })
			{
				var revision = Guid.NewGuid().ToString(); var narrative = Guid.NewGuid().ToString();
				await db.ExecuteAsync(@"INSERT RmsRevisions (RmsRevisionId,DepartmentId,ProtectionId,RecordId,RecordKind,RevisionNumber,Transition,DefinitionKey,DefinitionVersion,SnapshotJson,Checksum,ActorUserId,CreatedOn,OriginClient)
VALUES (@Revision,11,@Revision,@RecordId,@Kind,1,0,@Definition,1,'private immutable snapshot','original-hash','officer',@Old,0);
INSERT RmsNarratives (RmsNarrativeId,DepartmentId,ProtectionId,RecordId,RevisionId,Narrative,SupplementalJson,CreatedOn,ModifiedOn) VALUES (@Narrative,11,@Narrative,@RecordId,@Revision,'private narrative','private extra fields',@Old,@Old);",
					new { Revision = revision, Narrative = narrative, RecordId = recordId, Kind = recordId == id ? (int)RmsRecordKind.IncidentReport : (int)RmsRecordKind.IncidentAnalysis, Definition = RmsDefinitionKeys.NerisIncidentReport, Old = old });
			}
			var result = await Purger(connections, unit).PurgeAsync(11, id, RmsRecordKind.IncidentReport, 1, DateTime.UtcNow);
			result.Purged.Should().BeTrue(result.Reason); result.SearchErasurePending.Should().BeTrue();
			(await db.QueryAsync<string>("SELECT SnapshotJson FROM RmsRevisions WHERE RecordId IN @Ids", new { Ids = new[] { id, analysis } })).Should().OnlyContain(json => json == "{}");
			(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM RmsNarratives WHERE RecordId IN @Ids", new { Ids = new[] { id, analysis } })).Should().Be(0);
			(await db.ExecuteScalarAsync<string>("SELECT InputPayload FROM WorkflowRuns WHERE WorkflowRunId=@Run", new { Run = run })).Should().Be("{}");
			(await db.ExecuteScalarAsync<string>("SELECT RenderedOutput FROM WorkflowRunLogs WHERE WorkflowRunId=@Run", new { Run = run })).Should().BeNull();
			(await db.ExecuteScalarAsync<string>("SELECT PayloadJson FROM DomainEventOutbox WHERE DomainEventOutboxId=@Id", new { Id = dispatched.DomainEventOutboxId })).Should().Be("{}");
			var target = (await Purger(connections, unit).GetPendingSearchErasuresAsync(1000)).Single(t => t.RecordId == id);
			target.SourceIds.Should().BeEquivalentTo(new[] { id, analysis });
			var workflows = new WorkflowRunRepository(connections, new SqlServerConfiguration(), unit, WriteQueries());
			Func<Task> lateRun = () => workflows.UpdateAsync(new WorkflowRun { WorkflowRunId = run, DepartmentId = 11, AggregateId = analysis, InputPayload = "restored private workflow" }, CancellationToken.None, true);
			await lateRun.Should().ThrowAsync<InvalidOperationException>();
			var logs = new WorkflowRunLogRepository(connections, new SqlServerConfiguration(), unit, WriteQueries());
			Func<Task> lateLog = () => logs.InsertAsync(new WorkflowRunLog { WorkflowRunLogId = Guid.NewGuid().ToString(), WorkflowRunId = run, RenderedOutput = "restored private output" }, CancellationToken.None, true);
			await lateLog.Should().ThrowAsync<InvalidOperationException>();
			dispatched.DomainEventOutboxId = 0; dispatched.EventId = Guid.NewGuid().ToString();
			Func<Task> lateEvent = () => events.InsertAsync(dispatched, CancellationToken.None, true);
			await lateEvent.Should().ThrowAsync<InvalidOperationException>();
			var eventId = await db.ExecuteScalarAsync<long>("SELECT DomainEventOutboxId FROM DomainEventOutbox WHERE AggregateId=@Id", new { Id = analysis });
			(await events.MarkFailedAsync(eventId, "restored private error", DateTime.UtcNow, false)).Should().BeFalse();
			(await db.ExecuteScalarAsync<string>("SELECT LastError FROM DomainEventOutbox WHERE DomainEventOutboxId=@Id", new { Id = eventId })).Should().BeNull();
		}

		[Test]
		public async Task Concurrent_production_release_preserves_the_first_receipt_and_immutable_artifact()
		{
			var id = Guid.NewGuid().ToString(); using var db = new SqlConnection(_connection);
			await db.ExecuteAsync("INSERT RmsDisclosureProductions (RmsDisclosureProductionId,DepartmentId,ProtectionId,DisclosureRequestId,PreparedOn,CreatedOn,ModifiedOn,ArtifactJson,Checksum) VALUES (@Id,11,@Id,@Id,@Now,@Now,@Now,'immutable-packet','original-hash')", new { Id = id, Now = DateTime.UtcNow });
			var connections = Connections(); using var first = new UnitOfWork(connections); using var second = new UnitOfWork(connections);
			var firstRepo = new RmsDisclosureProductionsRepository(connections, new SqlServerConfiguration(), first, Mock.Of<IQueryFactory>());
			var secondRepo = new RmsDisclosureProductionsRepository(connections, new SqlServerConfiguration(), second, Mock.Of<IQueryFactory>());
			first.CreateOrGetConnection(); second.CreateOrGetConnection();
			(await firstRepo.TryReleaseAsync(11, id, 1, "first", DateTime.UtcNow, "Collection", "receipt-1")).Should().BeTrue();
			var competing = secondRepo.TryReleaseAsync(11, id, 1, "second", DateTime.UtcNow, "Email", "receipt-2");
			first.CommitChanges(); (await competing).Should().BeFalse(); second.CommitChanges();
			var saved = await firstRepo.GetByIdForDepartmentAsync(11, id); saved.ReleasedByUserId.Should().Be("first"); saved.DeliveryReference.Should().Be("receipt-1"); saved.ArtifactJson.Should().Be("immutable-packet"); saved.Checksum.Should().Be("original-hash"); saved.RowVersion.Should().Be(2);
		}
		[Test]
		public async Task Evidence_history_pages_include_signed_and_superseded_metadata_without_loading_bodies_or_another_tenant()
		{
			var id = await SeedOperational(); var now = DateTime.UtcNow; using var db = new SqlConnection(_connection);
			var ids = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid().ToString()).ToArray();
			for (var i = 0; i < 4; i++)
				await db.ExecuteAsync(@"INSERT RmsEvidenceArtifacts (RmsEvidenceArtifactId,DepartmentId,ProtectionId,RecordId,RecordKind,Kind,Title,RevisionId,SupersededOn,ManifestJson,ProtectedEnvelope,CapturedOn,CreatedOn,ModifiedOn)
VALUES (@Evidence,@Department,@Evidence,@Record,1,4,@Title,@Revision,@Superseded,'private-message','protected-envelope',@Captured,@Captured,@Captured)",
					new { Evidence = ids[i], Department = i == 3 ? 12 : 11, Record = id, Title = "capture-" + i,
						Revision = i == 0 ? null : Guid.NewGuid().ToString(), Superseded = i == 2 ? (DateTime?)now : null, Captured = now.AddMinutes(-i) });
			var connections = Connections(); using var unit = new UnitOfWork(connections);
			var repo = new RmsEvidenceArtifactsRepository(connections, new SqlServerConfiguration(), unit, Mock.Of<IQueryFactory>());
			var first = (await repo.GetHistoryAsync(11, id, 0, 2)).ToList(); var next = (await repo.GetHistoryAsync(11, id, 2, 2)).ToList();
			first.Select(a => a.Title).Should().Equal("capture-0", "capture-1"); first[1].RevisionId.Should().NotBeNull();
			next.Should().ContainSingle().Which.SupersededOn.Should().NotBeNull();
			first.Concat(next).Should().OnlyContain(a => a.ManifestJson == null && a.ProtectedEnvelope == null && a.DepartmentId == 11);
			(await repo.GetHistoryAsync(12, id, 0, 10)).Should().ContainSingle().Which.Title.Should().Be("capture-3");
		}

		[Test]
		public async Task Purge_removes_draft_revision_attachment_and_evidence_bodies_from_the_database()
		{
			var id = await SeedOperational();
			using var db = new SqlConnection(_connection);
			await db.ExecuteAsync(@"INSERT RmsOperationalRecordDetails (RmsOperationalRecordDetailId,DepartmentId,ProtectionId,RecordId,RevisionId,Narrative,CreatedOn,ModifiedOn,RowVersion) VALUES (@Draft,11,@Draft,@Id,NULL,'sensitive-draft',@Now,@Now,1),(@Detail,11,@Detail,@Id,@Revision,'sensitive-final',@Now,@Now,1);
INSERT RmsRevisions (RmsRevisionId,DepartmentId,ProtectionId,RecordId,RecordKind,RevisionNumber,Transition,DefinitionKey,DefinitionVersion,SnapshotJson,Checksum,ActorUserId,CreatedOn,OriginClient) VALUES (@Revision,11,@Revision,@Id,0,1,0,'system.training',1,'sensitive-snapshot','old-checksum','officer',@Now,0);
INSERT RmsRecordAttachments (RmsRecordAttachmentId,DepartmentId,ProtectionId,RecordId,FileName,ContentType,ByteSize,Checksum,Data,UploadedByUserId,UploadedOn,CreatedOn,ModifiedOn,RowVersion) VALUES (@Attachment,11,@Attachment,@Id,'private.txt','text/plain',3,'hash',0x010203,'officer',@Now,@Now,@Now,1);",
				new { Id = id, Draft = Guid.NewGuid().ToString(), Detail = Guid.NewGuid().ToString(), Revision = Guid.NewGuid().ToString(), Attachment = Guid.NewGuid().ToString(), Now = DateTime.UtcNow });
			await db.ExecuteAsync("INSERT RmsEvidenceArtifacts (RmsEvidenceArtifactId,DepartmentId,ProtectionId,RecordId,RecordKind,Kind,ManifestJson,ProtectedEnvelope,CapturedOn,CreatedOn,ModifiedOn) VALUES (@Evidence,11,@Evidence,@Id,1,1,'sensitive-evidence','sensitive-envelope',@Now,@Now,@Now)", new { Evidence = Guid.NewGuid().ToString(), Id = id, Now = DateTime.UtcNow });
			var connections = Connections(); using var unit = new UnitOfWork(connections);
			var result = await Purger(connections, unit).PurgeAsync(11, id, RmsRecordKind.Operational, 1, DateTime.UtcNow);
			result.Purged.Should().BeTrue(); result.AttachmentsPurged.Should().Be(1);
			(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM RmsOperationalRecordDetails WHERE RecordId=@Id", new { Id = id })).Should().Be(0);
			(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM RmsRecordAttachments WHERE RecordId=@Id", new { Id = id })).Should().Be(0);
			(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM RmsEvidenceArtifacts WHERE RecordId=@Id", new { Id = id })).Should().Be(0);
			(await db.ExecuteScalarAsync<string>("SELECT SnapshotJson FROM RmsRevisions WHERE RecordId=@Id", new { Id = id })).Should().Be("{}");
			(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM RmsAccessAudits WHERE RecordId=@Id AND Purpose='Retention purge'", new { Id = id })).Should().Be(1);
			(await Purger(connections, unit).PurgeAsync(11, id, RmsRecordKind.Operational, 1, DateTime.UtcNow)).Purged.Should().BeFalse();
		}

		[TestCase(false)]
		[TestCase(true)]
		public async Task A_draft_that_matched_a_hold_stays_held_after_its_date_changes_before_first_revision(bool entersAfterPlacement)
		{
			var id = await SeedOperational(); var heldDate = new DateTime(2014, 6, 3); using var db = new SqlConnection(_connection);
			await db.ExecuteAsync("UPDATE RmsOperationalRecords SET StartedOn=@Date,State=@State WHERE RmsOperationalRecordId=@Id", new { Id = id, Date = entersAfterPlacement ? new DateTime(2017, 1, 1) : heldDate, State = (int)RmsRecordState.Draft });
			var connections = Connections(); using var unit = new UnitOfWork(connections);
			var holds = new RmsRecordLegalHoldsRepository(connections, new SqlServerConfiguration(), unit, WriteQueries());
			var records = new RmsOperationalRecordsRepository(connections, new SqlServerConfiguration(), unit, WriteQueries());
			var hold = new RmsRecordLegalHold { DepartmentId = 11, RmsRecordLegalHoldId = Guid.NewGuid().ToString(), PeriodStart = heldDate.Date, PeriodEnd = heldDate.Date.AddDays(1), Reason = "Litigation", PlacedByUserId = "officer", PlacedOn = DateTime.UtcNow, CreatedOn = DateTime.UtcNow, ModifiedOn = DateTime.UtcNow, RowVersion = 1 };
			await holds.InsertAsync(hold, CancellationToken.None, true);
			var record = await records.GetByIdForDepartmentAsync(11, id);
			if (entersAfterPlacement) { record.StartedOn = heldDate; await records.UpdateAsync(record, CancellationToken.None, true); }
			record.StartedOn = new DateTime(2017, 1, 1); record.State = (int)RmsRecordState.Finalized;
			await records.UpdateAsync(record, CancellationToken.None, true);
			var snapshot = Newtonsoft.Json.JsonConvert.SerializeObject(new { StartedOn = record.StartedOn });
			await db.ExecuteAsync("INSERT RmsRevisions (RmsRevisionId,DepartmentId,ProtectionId,RecordId,RecordKind,RevisionNumber,Transition,DefinitionKey,DefinitionVersion,SnapshotJson,Checksum,ActorUserId,CreatedOn,OriginClient) VALUES (@Revision,11,@Revision,@Id,1,1,0,'system.training',1,@Snapshot,@Checksum,'officer',@Now,0)", new { Id = id, Revision = Guid.NewGuid().ToString(), Snapshot = snapshot, Checksum = Resgrid.Services.Records.RecordSnapshotSerializer.Checksum(snapshot), Now = DateTime.UtcNow });
			var result = await Purger(connections, unit).PurgeAsync(11, id, RmsRecordKind.Operational, record.RowVersion, DateTime.UtcNow);
			result.Held.Should().BeTrue(); result.Reason.Should().Contain("previously matched");
			unit.CreateOrGetConnection(); (await holds.TryReleaseAsync(11, hold.RmsRecordLegalHoldId, 1, "officer", "Court released preservation", DateTime.UtcNow)).Should().BeTrue(); unit.CommitChanges();
			(await Purger(connections, unit).PurgeAsync(11, id, RmsRecordKind.Operational, record.RowVersion, DateTime.UtcNow)).Purged.Should().BeTrue();
		}
		[Test]
		public async Task Changing_the_current_date_cannot_remove_a_historical_revision_from_preservation()
		{
			var id = await SeedOperational(); using var db = new SqlConnection(_connection); var heldDate = new DateTime(2014, 6, 3);
			await db.ExecuteAsync("INSERT RmsRevisions (RmsRevisionId,DepartmentId,ProtectionId,RecordId,RecordKind,RevisionNumber,Transition,DefinitionKey,DefinitionVersion,SnapshotJson,Checksum,ActorUserId,CreatedOn,OriginClient) VALUES (@Revision,11,@Revision,@Id,1,1,0,'system.training',1,@Snapshot,@Checksum,'officer',@Now,0); INSERT RmsRecordLegalHolds (RmsRecordLegalHoldId,DepartmentId,PeriodStart,PeriodEnd,Reason,PlacedByUserId,PlacedOn,CreatedOn,ModifiedOn,RowVersion) VALUES (@Hold,11,@Start,@End,'Litigation','officer',@Now,@Now,@Now,1)", new { Id = id, Revision = Guid.NewGuid().ToString(), Hold = Guid.NewGuid().ToString(), Snapshot = Newtonsoft.Json.JsonConvert.SerializeObject(new { StartedOn = heldDate }), Checksum = Resgrid.Services.Records.RecordSnapshotSerializer.Checksum(Newtonsoft.Json.JsonConvert.SerializeObject(new { StartedOn = heldDate })), Start = heldDate.Date, End = heldDate.Date.AddDays(1), Now = DateTime.UtcNow });
			var connections = Connections(); using var unit = new UnitOfWork(connections);
			var result = await Purger(connections, unit).PurgeAsync(11, id, RmsRecordKind.Operational, 1, DateTime.UtcNow); result.Held.Should().BeTrue(); result.Purged.Should().BeFalse(); result.Reason.Should().Contain("covers a historical revision");
			(await db.ExecuteScalarAsync<DateTime?>("SELECT PurgedOn FROM RmsOperationalRecords WHERE RmsOperationalRecordId=@Id", new { Id = id })).Should().BeNull();
			await db.ExecuteAsync("DELETE RmsRecordLegalHolds WHERE PeriodStart=@Start", new { Start = heldDate.Date });
		}
		[Test]
		public async Task A_hold_that_commits_before_the_purge_lock_prevents_deletion()
		{
			var id = await SeedOperational();
			using var holder = new SqlConnection(_connection); await holder.OpenAsync();
			using var transaction = holder.BeginTransaction();
			await holder.ExecuteAsync("SELECT DepartmentId FROM Departments WITH (UPDLOCK,HOLDLOCK) WHERE DepartmentId=11", transaction: transaction);
			var connections = Connections(); using var unit = new UnitOfWork(connections);
			var attempt = Purger(connections, unit).PurgeAsync(11, id, RmsRecordKind.Operational, 1, DateTime.UtcNow);
			await holder.ExecuteAsync("INSERT RmsRecordLegalHolds (RmsRecordLegalHoldId,DepartmentId,RecordId,Reason,PlacedByUserId,PlacedOn,CreatedOn,ModifiedOn,RowVersion) VALUES (@Hold,11,@Id,'Litigation','officer',@Now,@Now,@Now,1)", new { Hold = Guid.NewGuid().ToString(), Id = id, Now = DateTime.UtcNow }, transaction);
			await transaction.CommitAsync();
			(await attempt).Held.Should().BeTrue();
			(await holder.ExecuteScalarAsync<DateTime?>("SELECT PurgedOn FROM RmsOperationalRecords WHERE RmsOperationalRecordId=@Id", new { Id = id })).Should().BeNull();
		}
	}
}
