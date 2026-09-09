using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
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
using Resgrid.Model.Checklists;
using Resgrid.Model;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Providers.Migrations.Migrations;
using Resgrid.Providers.MigrationsPg.Migrations;
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;
using Resgrid.Repositories.DataRepository.Transactions;
using Resgrid.Services;
using Newtonsoft.Json.Linq;

namespace Resgrid.Tests.Services
{
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public partial class ChecklistDatabaseTests
	{
		private readonly DatabaseTypes _type;
		private DatabaseTypes _previous;
		private string _master, _connection, _database;
		private ServiceProvider _runner;
		private string Q(string name) => _type == DatabaseTypes.Postgres ? name.ToLowerInvariant() : "[" + name + "]";
		private string TextType => _type == DatabaseTypes.Postgres ? "text" : "nvarchar(max)";
		public ChecklistDatabaseTests(DatabaseTypes type) { _type = type; }
		private DbConnection Connect(string connection) => _type == DatabaseTypes.Postgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);
		[OneTimeSetUp]
		public async Task CreateIsolatedDatabaseAndMigrate()
		{
			_master = Environment.GetEnvironmentVariable(_type == DatabaseTypes.Postgres ? "RESGRID_CHECKLIST_POSTGRES_TEST_CONNECTION" : "RESGRID_CHECKLIST_SQLSERVER_TEST_CONNECTION");
			if (string.IsNullOrWhiteSpace(_master)) Assert.Ignore("Set the checklist test connection for " + _type + " to run real database verification.");
			_previous = DataConfig.DatabaseType; DataConfig.DatabaseType = _type;
			_database = "checklist_verification_" + Guid.NewGuid().ToString("N");
			await using var master = Connect(_master); await master.ExecuteAsync("CREATE DATABASE " + _database);
			if (_type == DatabaseTypes.Postgres) { var builder = new NpgsqlConnectionStringBuilder(_master) { Database = _database }; _connection = builder.ConnectionString; }
			else { var builder = new SqlConnectionStringBuilder(_master) { InitialCatalog = _database }; _connection = builder.ConnectionString; }
			await using var db = Connect(_connection);
			await db.ExecuteAsync(_type == DatabaseTypes.Postgres ? "CREATE TABLE departments(departmentid integer PRIMARY KEY); INSERT INTO departments VALUES(77),(88);" : "CREATE TABLE Departments(DepartmentId int PRIMARY KEY); INSERT INTO Departments VALUES(77),(88);");
			await db.ExecuteAsync($"CREATE TABLE {Q("AuditLogs")} ({Q("AuditLogId")} int PRIMARY KEY, {Q("DepartmentId")} int, {Q("LogType")} int, {Q("Data")} {TextType}); CREATE TABLE {Q("DomainEventOutbox")} ({Q("DomainEventOutboxId")} bigint PRIMARY KEY, {Q("DepartmentId")} int, {Q("ProducerSubsystem")} varchar(100), {Q("PayloadJson")} {TextType}, {Q("LastError")} {TextType}, {Q("AggregateId")} varchar(36)); CREATE TABLE {Q("WorkflowRuns")} ({Q("WorkflowRunId")} varchar(36) PRIMARY KEY, {Q("WorkflowId")} varchar(36), {Q("DepartmentId")} int, {Q("TriggerEventType")} int, {Q("Status")} int, {Q("AttemptNumber")} int, {Q("InputPayload")} {TextType}, {Q("ErrorMessage")} varchar(4000)); CREATE TABLE {Q("WorkflowRunLogs")} ({Q("WorkflowRunLogId")} varchar(36) PRIMARY KEY, {Q("WorkflowRunId")} varchar(36), {Q("RenderedOutput")} {TextType}, {Q("ActionResult")} varchar(4000), {Q("ErrorMessage")} varchar(4000));");
			var source = new Mock<IMigrationSource>(); source.Setup(s => s.GetMigrations()).Returns(new IMigration[] { _type == DatabaseTypes.Postgres ? new M0191_AddChecklistWorkflowPg() : new M0191_AddChecklistWorkflow(), _type == DatabaseTypes.Postgres ? new M0192_ProtectChecklistOutcomesPg() : new M0192_ProtectChecklistOutcomes(), _type == DatabaseTypes.Postgres ? new M0193_ReadinessHistoryProtectionPg() : new M0193_ReadinessHistoryProtection(), _type == DatabaseTypes.Postgres ? new M0194_AddChecklistSchedulingPg() : new M0194_AddChecklistScheduling(), _type == DatabaseTypes.Postgres ? new M0195_AddChecklistRemindersPg() : new M0195_AddChecklistReminders(), _type == DatabaseTypes.Postgres ? new M0196_CompleteChecklistSchedulingPg() : new M0196_CompleteChecklistScheduling() });
			_runner = new ServiceCollection().AddFluentMigratorCore().ConfigureRunner(r =>
			{
				if (_type == DatabaseTypes.Postgres) r.AddPostgres(); else r.AddSqlServer();
				r.WithGlobalConnectionString(_connection);
			}).AddSingleton(source.Object).BuildServiceProvider();
			_runner.GetRequiredService<IMigrationRunner>().MigrateUp();
		}
		[OneTimeTearDown]
		public async Task RemoveOnlyThisFixturesDatabase()
		{
			if (_database == null) return;
			_runner?.Dispose(); DataConfig.DatabaseType = _previous;
			if (!_database.StartsWith("checklist_verification_", StringComparison.Ordinal) || !Guid.TryParseExact(_database.Substring(23), "N", out _)) throw new InvalidOperationException("Unexpected test database name.");
			if (_type == DatabaseTypes.Postgres) NpgsqlConnection.ClearAllPools(); else SqlConnection.ClearAllPools();
			await using var master = Connect(_master);
			await master.ExecuteAsync(_type == DatabaseTypes.Postgres ? "DROP DATABASE " + _database + " WITH (FORCE)" : "ALTER DATABASE " + _database + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + _database);
		}
		private IConnectionProvider Connections()
		{ var provider = new Mock<IConnectionProvider>(); provider.Setup(p => p.Create()).Returns(() => Connect(_connection)); return provider.Object; }
		private SqlConfiguration Configuration() => _type == DatabaseTypes.Postgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration();
		private ChecklistRepository Repository(IConnectionProvider provider, IUnitOfWork uow) => new ChecklistRepository(provider, Configuration(), uow, new Mock<IQueryFactory>().Object);
		private static T Row<T>(string parent = null) where T : ChecklistRow, new() => new T { DepartmentId = 77, ParentId = parent, CreatedBy = "author", CreatedOn = DateTime.UtcNow, UpdatedOn = DateTime.UtcNow, Content = "{}" };
		[Test, Order(0)]
		public void Migration_can_roll_back_and_reapply_in_an_empty_isolated_database()
		{
			var runner = _runner.GetRequiredService<IMigrationRunner>();
			runner.MigrateDown(0); runner.MigrateUp(); runner.MigrateUp();
		}
		[Test]
		public async Task Round_trip_versions_occurrences_answers_and_files_and_enforce_tenant_foreign_keys()
		{
			var connections = Connections(); using var uow = new UnitOfWork(connections); var store = Repository(connections, uow);
			await uow.CreateOrGetConnectionAsync(); await store.LockDepartmentAsync(77);
			var definition = Row<ChecklistDefinition>(); await store.WriteAsync(definition, true);
			var version = Row<ChecklistDefinitionVersion>(definition.Id); version.Version = 1; await store.WriteAsync(version, true);
			var completion = Row<ChecklistCompletion>(definition.Id); completion.VersionId = version.Id; completion.TargetId = "77";
			var occurrence = Row<ChecklistOccurrence>(definition.Id); occurrence.VersionId = version.Id; occurrence.CompletionId = completion.Id; occurrence.TargetId = "77"; await store.WriteAsync(occurrence, true);
			completion.OccurrenceId = occurrence.Id; await store.WriteAsync(completion, true);
			var answer = Row<ChecklistCompletionItem>(completion.Id); answer.ItemId = Guid.NewGuid().ToString(); await store.ReplaceAnswersAsync(77, completion.Id, new[] { answer });
			var file = Row<ChecklistCompletionFile>(completion.Id); file.ItemId = answer.ItemId; file.ContentType = "image/png"; file.Data = new byte[] { 1, 2, 3 }; file.Size = 3; file.Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(file.Data)); file.ScanState = 1; await store.WriteAsync(file, true);
			uow.CommitChanges();
			(await store.GetAsync<ChecklistCompletion>(88, completion.Id)).Should().BeNull();
			(await store.GetAsync<ChecklistCompletion>(77, completion.Id)).VersionId.Should().Be(version.Id);
			(await store.ListAsync<ChecklistCompletionItem>(77, completion.Id)).Should().ContainSingle();
			(await store.ListAsync<ChecklistCompletionFile>(77, completion.Id)).Single().Data.Should().BeNull("list reads must never fetch blobs");
			(await store.GetAsync<ChecklistCompletionFile>(77, file.Id)).Data.Should().Equal(1, 2, 3);
			await uow.CreateOrGetConnectionAsync(); var foreign = Row<ChecklistDefinitionVersion>(definition.Id); foreign.DepartmentId = 88; foreign.Version = 2;
			Func<Task> insert = () => store.WriteAsync(foreign, true); await insert.Should().ThrowAsync<DbException>(); uow.DiscardChanges();
		}

		[Test, Order(1)]
		public async Task History_migration_backfills_multiple_batches_without_disclosing_or_replacing_original_payloads()
		{
			var runner = _runner.GetRequiredService<IMigrationRunner>(); runner.MigrateDown(192);
			await using var db = Connect(_connection);
			var completion = Guid.NewGuid().ToString();
			var payload = new JObject { ["CompletionId"] = completion, ["TargetType"] = 3, ["TargetId"] = "SYNTHETIC-PERSON", ["Score"] = 87.25, ["Passed"] = false, ["Note"] = "SYNTHETIC-PHI-CANARY" }.ToString();
			for (var i = 1; i <= 205; i++)
				await db.ExecuteAsync($"INSERT INTO {Q("DomainEventOutbox")} VALUES(@id,77,'Checklists',@payload,NULL,@completion)", new { id = i, payload, completion });
			await db.ExecuteAsync($"INSERT INTO {Q("DomainEventOutbox")} VALUES(206,77,'Records',@payload,NULL,@completion)", new { payload, completion });
			runner.MigrateUp(); runner.MigrateUp();
			var rows = (await db.QueryAsync<DomainEventOutboxEntry>($"SELECT * FROM {Q("DomainEventOutbox")} ORDER BY {Q("DomainEventOutboxId")}")).ToList();
			foreach (var row in rows.Take(205))
			{
				row.PayloadJson.Should().Be(payload);
				row.ReadinessRoutingJson.Should().NotContain("SYNTHETIC").And.NotContain("87.25");
				var routing = JObject.Parse(row.ReadinessRoutingJson); routing["CompletionId"].Value<string>().Should().Be(completion);
				routing["TargetId"].Value<string>().Should().Be("REDACTED"); routing["Passed"].Value<string>().Should().Be("REDACTED");
			}
			rows.Last().ReadinessRoutingJson.Should().BeNull();
			await db.ExecuteAsync($"DELETE FROM {Q("DomainEventOutbox")}");
		}

		[Test, Order(2)]
		public async Task Historical_adp_scans_and_residue_checks_exclude_other_tenants_and_other_features()
		{
			await using var db = Connect(_connection);
			var bulk = new DepartmentDataProtectionBulkRepository(Connections(), Configuration());
			var bindings = AdpTableBindings.ForVersionRange(new ProtectedFieldCatalog(), 15, 16);
			var ids = Enumerable.Range(1, 3).Select(_ => Guid.NewGuid().ToString()).ToArray();
			for (var i = 0; i < 3; i++)
			{
				var parameters = new { numericId = i + 1, id = ids[i], department = i == 1 ? 88 : 77, trigger = i == 2 ? 1 : 67, logType = i == 2 ? (int)AuditLogTypes.GroupAdded : ReadinessHistoryFields.AuditTypes.First(), producer = i == 2 ? "Records" : "Checklists", data = "SYNTHETIC-PHI-CANARY" };
				await db.ExecuteAsync($"INSERT INTO {Q("AuditLogs")} VALUES(@numericId,@department,@logType,@data); INSERT INTO {Q("DomainEventOutbox")} ({Q("DomainEventOutboxId")},{Q("DepartmentId")},{Q("ProducerSubsystem")},{Q("PayloadJson")},{Q("LastError")}) VALUES(@numericId,@department,@producer,@data,@data); INSERT INTO {Q("WorkflowRuns")} ({Q("WorkflowRunId")},{Q("DepartmentId")},{Q("TriggerEventType")},{Q("InputPayload")},{Q("ErrorMessage")}) VALUES(@id,@department,@trigger,@data,@data); INSERT INTO {Q("WorkflowRunLogs")} VALUES(@id,@id,@data,@data,@data);", parameters);
			}
			foreach (var binding in bindings)
			{
				(await bulk.CountRowsAsync(binding, 77)).Should().Be(1, binding.TableName);
				(await bulk.CountRowsAsync(binding, 88)).Should().Be(1, binding.TableName);
				(await bulk.CountRowsAsync(binding, 99)).Should().Be(0, binding.TableName);
				var batch = await bulk.GetBatchAsync(binding, 77, null, 10); batch.Should().ContainSingle();
				batch[0].RowKey.Should().Be(binding.PkIsNumeric ? "1" : ids[0]);
				(await bulk.GetBatchAsync(binding, 77, batch[0].RowKey, 10)).Should().BeEmpty();
				(await bulk.CountTextResidueAsync(binding, 77, false)).Should().Be(1);
				foreach (var column in binding.Columns)
				{
					// A long envelope also proves the widened Workflow error/result columns do not truncate ciphertext.
					await db.ExecuteAsync($"UPDATE {Q(binding.TableName)} SET {Q(column.ColumnName)} = @value WHERE {Q(binding.PkColumn)} = @id", new { value = "rgdp:1:1:" + new string('x', 5000), id = binding.PkIsNumeric ? (object)1 : ids[0] });
				}
				(await bulk.CountTextResidueAsync(binding, 77, false)).Should().Be(0);
				(await bulk.CountTextResidueAsync(binding, 77, true)).Should().Be(1);
				(await bulk.CountTextResidueAsync(binding, 88, false)).Should().Be(1);
				(await bulk.CountSupersededKeyVersionResidueAsync(binding, 77, 2)).Should().Be(1);
			}
			Action rollback = () => _runner.GetRequiredService<IMigrationRunner>().MigrateDown(192);
			rollback.Should().Throw<Exception>();
			_runner.GetRequiredService<IVersionLoader>().LoadVersionInfo(); _runner.GetRequiredService<IMigrationRunner>().MigrateUp();
			await db.ExecuteAsync($"DELETE FROM {Q("WorkflowRunLogs")}; DELETE FROM {Q("WorkflowRuns")}; DELETE FROM {Q("DomainEventOutbox")}; DELETE FROM {Q("AuditLogs")};");
		}
		[Test, Order(3)]
		public async Task Checklist_outbox_initialization_requires_matching_identity_and_rolls_back_with_its_producer()
		{
			await using var db = Connect(_connection);
			await db.ExecuteAsync($"ALTER TABLE {Q("DomainEventOutbox")} ADD {Q("EventId")} varchar(36); ALTER TABLE {Q("DomainEventOutbox")} ADD {Q("State")} int; ALTER TABLE {Q("DomainEventOutbox")} ADD {Q("LeaseOwner")} varchar(100);");
			var connections = Connections(); using var uow = new UnitOfWork(connections);
			var repository = new DomainEventOutboxRepository(connections, Configuration(), uow, new Mock<IQueryFactory>().Object);
			var entry = new DomainEventOutboxEntry { DomainEventOutboxId = 901, DepartmentId = 77, EventId = Guid.NewGuid().ToString(), PayloadJson = "rgdp:1:1:synthetic==" };
			Func<Task> noTransaction = () => repository.InitializeChecklistPayloadAsync(entry);
			await noTransaction.Should().ThrowAsync<InvalidOperationException>();
			var connection = await uow.CreateOrGetConnectionAsync();
			await connection.ExecuteAsync($"INSERT INTO {Q("DomainEventOutbox")} ({Q("DomainEventOutboxId")},{Q("DepartmentId")},{Q("ProducerSubsystem")},{Q("PayloadJson")},{Q("EventId")},{Q("State")}) VALUES(901,77,'Checklists','{{}}',@EventId,@state)", new { entry.EventId, state = (int)DomainEventOutboxState.Pending }, uow.Transaction);
			entry.DepartmentId = 88; (await repository.InitializeChecklistPayloadAsync(entry)).Should().BeFalse(); entry.DepartmentId = 77;
			var eventId = entry.EventId; entry.EventId = Guid.NewGuid().ToString(); (await repository.InitializeChecklistPayloadAsync(entry)).Should().BeFalse(); entry.EventId = eventId;
			(await repository.InitializeChecklistPayloadAsync(entry)).Should().BeTrue();
			(await repository.InitializeChecklistPayloadAsync(entry)).Should().BeFalse("a populated event cannot be reinitialized");
			uow.DiscardChanges();
			(await db.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {Q("DomainEventOutbox")} WHERE {Q("DomainEventOutboxId")}=901")).Should().Be(0);
		}

		[Test]
		public async Task Rollback_removes_partial_data_and_duplicate_version_is_rejected()
		{
			var connections = Connections(); using var uow = new UnitOfWork(connections); var store = Repository(connections, uow);
			await uow.CreateOrGetConnectionAsync(); await store.LockDepartmentAsync(77);
			var row = Row<ChecklistDefinition>(); await store.WriteAsync(row, true); uow.DiscardChanges(); (await store.GetAsync<ChecklistDefinition>(77, row.Id)).Should().BeNull();
			await uow.CreateOrGetConnectionAsync(); await store.WriteAsync(row, true);
			var first = Row<ChecklistDefinitionVersion>(row.Id); first.Version = 1; await store.WriteAsync(first, true); uow.CommitChanges();
			await uow.CreateOrGetConnectionAsync(); var duplicate = Row<ChecklistDefinitionVersion>(row.Id); duplicate.Version = 1;
			Func<Task> insert = () => store.WriteAsync(duplicate, true); await insert.Should().ThrowAsync<DbException>(); uow.DiscardChanges();
		}
		[Test]
		public async Task Department_lock_serializes_two_writers_until_commit()
		{
			var connections = Connections(); using var first = new UnitOfWork(connections); using var second = new UnitOfWork(connections);
			await first.CreateOrGetConnectionAsync(); await second.CreateOrGetConnectionAsync();
			await Repository(connections, first).LockDepartmentAsync(77);
			var waiting = Repository(connections, second).LockDepartmentAsync(77);
			(await Task.WhenAny(waiting, Task.Delay(200))).Should().NotBe(waiting);
			first.CommitChanges(); await waiting.WaitAsync(TimeSpan.FromSeconds(10)); second.CommitChanges();
		}
		[Test, Order(99)]
		public async Task Protected_outcomes_round_trip_as_null_and_prevent_lossy_schema_rollback()
		{
			var connections = Connections(); using var uow = new UnitOfWork(connections); var store = Repository(connections, uow);
			await uow.CreateOrGetConnectionAsync(); await store.LockDepartmentAsync(77);
			var definition = Row<ChecklistDefinition>(); await store.WriteAsync(definition, true);
			var version = Row<ChecklistDefinitionVersion>(definition.Id); version.Version = 1; await store.WriteAsync(version, true);
			var completion = Row<ChecklistCompletion>(definition.Id); completion.VersionId = version.Id; completion.TargetId = "77";
			var occurrence = Row<ChecklistOccurrence>(definition.Id); occurrence.VersionId = version.Id; occurrence.CompletionId = completion.Id; occurrence.TargetId = "77"; await store.WriteAsync(occurrence, true);
			completion.OccurrenceId = occurrence.Id; completion.Score = null; completion.Passed = null; completion.ProtectedScoreEnvelope = "rgdp:1:1:score=="; completion.ProtectedPassedEnvelope = "rgdp:1:1:passed==";
			await store.WriteAsync(completion, true);
			var item = Row<ChecklistCompletionItem>(completion.Id); item.ItemId = Guid.NewGuid().ToString(); item.IsFailure = null; item.ProtectedIsFailureEnvelope = "rgdp:1:1:failure=="; await store.ReplaceAnswersAsync(77, completion.Id, new[] { item }); uow.CommitChanges();
			var stored = await store.GetAsync<ChecklistCompletion>(77, completion.Id);
			stored.Score.Should().BeNull(); stored.Passed.Should().BeNull(); stored.ProtectedPassedEnvelope.Should().Be(completion.ProtectedPassedEnvelope);
			(await store.ListAsync<ChecklistCompletionItem>(77, completion.Id)).Single().IsFailure.Should().BeNull();
			Action rollback = () => _runner.GetRequiredService<IMigrationRunner>().MigrateDown(191);
			rollback.Should().Throw<Exception>();
			_runner.GetRequiredService<IVersionLoader>().LoadVersionInfo(); _runner.GetRequiredService<IMigrationRunner>().MigrateUp();
			(await store.GetAsync<ChecklistCompletion>(77, completion.Id)).ProtectedScoreEnvelope.Should().Be(completion.ProtectedScoreEnvelope);
		}
		[Test]
		public async Task Duplicate_workflow_queue_deliveries_claim_only_one_attempt_per_department()
		{
			string Q(string name) => _type == DatabaseTypes.Postgres ? name.ToLowerInvariant() : "[" + name + "]";
			await using var db = Connect(_connection);
			var id = Guid.NewGuid().ToString(); var workflow = Guid.NewGuid().ToString();
			await db.ExecuteAsync($"INSERT INTO {Q("WorkflowRuns")} ({Q("WorkflowRunId")},{Q("WorkflowId")},{Q("DepartmentId")},{Q("TriggerEventType")},{Q("Status")},{Q("AttemptNumber")},{Q("InputPayload")}) VALUES(@id,@workflow,77,67,@state,1,@payload)", new { id, workflow, state = (int)WorkflowRunStatus.Pending, payload = "SYNTHETIC-PHI-CANARY" });
			var connections = Connections(); using var first = new UnitOfWork(connections); using var second = new UnitOfWork(connections);
			WorkflowRunRepository Repository(IUnitOfWork uow) => new WorkflowRunRepository(connections, Configuration(), uow, new Mock<IQueryFactory>().Object);
			(await Repository(first).TryStartChecklistRunAsync(id, workflow, 88, 1, "{}")).Should().BeFalse();
			var claims = await Task.WhenAll(Repository(first).TryStartChecklistRunAsync(id, workflow, 77, 1, "{}"), Repository(second).TryStartChecklistRunAsync(id, workflow, 77, 1, "{}"));
			claims.Count(c => c).Should().Be(1);
			(await db.QuerySingleAsync<string>($"SELECT {Q("InputPayload")} FROM {Q("WorkflowRuns")} WHERE {Q("WorkflowRunId")}=@id", new { id })).Should().Be("{}");
			(await Repository(first).TryStartChecklistRunAsync(id, workflow, 77, 2, "{}")).Should().BeFalse();
		}
	}
}
