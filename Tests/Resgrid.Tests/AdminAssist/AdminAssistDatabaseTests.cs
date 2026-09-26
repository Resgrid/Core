using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using FluentMigrator;
using FluentMigrator.Runner;
using FluentMigrator.Runner.Initialization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Npgsql;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Providers.Migrations.Migrations;
using Resgrid.Providers.MigrationsPg.Migrations;
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Configs;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;
using Resgrid.Repositories.DataRepository.Transactions;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture(DatabaseTypes.SqlServer), TestFixture(DatabaseTypes.Postgres), NonParallelizable]
	public class AdminAssistDatabaseTests(DatabaseTypes type)
	{
		private string _master, _connection, _database;
		private DatabaseTypes _previous;
		private bool _configured, _created;
		private ServiceProvider _runner;
		private string Q(string name) => type == DatabaseTypes.Postgres ? name.ToLowerInvariant() : "[" + name + "]";
		private DbConnection Connect(string connection) => type == DatabaseTypes.Postgres ? new NpgsqlConnection(connection) : new SqlConnection(connection);
		private IConnectionProvider Connections()
		{
			var connections = new Mock<IConnectionProvider>(); connections.Setup(c => c.Create()).Returns(() => Connect(_connection)); return connections.Object;
		}
		private AdminAssistRepository Repository(IUnitOfWork unit) => new(Connections(), type == DatabaseTypes.Postgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration(), unit, Mock.Of<IQueryFactory>());
		[OneTimeSetUp]
		public async Task Create_isolated_database()
		{
			var configured = Environment.GetEnvironmentVariable(type == DatabaseTypes.Postgres ? "RESGRID_ADMINASSIST_POSTGRES_TEST_CONNECTION" : "RESGRID_ADMINASSIST_SQLSERVER_TEST_CONNECTION");
			if (string.IsNullOrWhiteSpace(configured)) Assert.Ignore("Configure an Admin Assist test server to execute the real database fixture.");
			if (type == DatabaseTypes.Postgres)
			{
				var builder = new NpgsqlConnectionStringBuilder(configured);
				if (!string.IsNullOrEmpty(builder.Database) && builder.Database != "postgres") throw new InvalidOperationException("Use an administrative test database, not an application database.");
				builder.Database = "postgres"; builder.IncludeErrorDetail = false; _master = builder.ConnectionString;
			}
			else
			{
				var builder = new SqlConnectionStringBuilder(configured);
				if (!string.IsNullOrEmpty(builder.InitialCatalog) && !string.Equals(builder.InitialCatalog, "master", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Use a test server master connection.");
				builder.InitialCatalog = "master"; _master = builder.ConnectionString;
			}
			_previous = DataConfig.DatabaseType; _configured = true; DataConfig.DatabaseType = type;
			_database = "adminassist_verification_" + Guid.NewGuid().ToString("N");
			await using (var master = Connect(_master)) { await master.ExecuteAsync("CREATE DATABASE " + _database); _created = true; }
			_connection = type == DatabaseTypes.Postgres ? new NpgsqlConnectionStringBuilder(_master) { Database = _database }.ConnectionString : new SqlConnectionStringBuilder(_master) { InitialCatalog = _database }.ConnectionString;
			await using (var db = Connect(_connection))
			{
				var text = type == DatabaseTypes.Postgres ? "varchar" : "nvarchar";
				var date = type == DatabaseTypes.Postgres ? "timestamp" : "datetime2";
				var boolean = type == DatabaseTypes.Postgres ? "boolean" : "bit";
				await db.ExecuteAsync($@"CREATE TABLE {Q("Departments")} ({Q("DepartmentId")} int PRIMARY KEY);
CREATE TABLE {Q("FeatureFlags")} ({Q("FeatureFlagId")} {(type == DatabaseTypes.Postgres ? "serial" : "int IDENTITY(1,1)")} PRIMARY KEY,{Q("FlagKey")} {text}(128) UNIQUE,{Q("Name")} {text}(128),{Q("Description")} {text}(512),{Q("Category")} {text}(128),{Q("IsEnabledGlobally")} {boolean});
CREATE TABLE {Q("FeatureFlagPrerequisites")} ({Q("FeatureFlagId")} int,{Q("RequiredFeatureFlagId")} int,{Q("RequiredValue")} {text}(128));
CREATE TABLE {Q("PlanAddons")} ({Q("PlanAddonId")} {text}(36) PRIMARY KEY,{Q("AddonType")} int,{Q("Cost")} decimal(18,2),{Q("ExternalId")} {text}(128),{Q("TestExternalId")} {text}(128));
CREATE TABLE {Q("RmsRecordLegalHolds")} ({Q("DepartmentId")} int,{Q("ReleasedOn")} {date},{Q("RmsRecordLegalHoldId")} {text}(128),{Q("RecordId")} {text}(128),{Q("DefinitionKey")} {text}(128));
CREATE TABLE {Q("DepartmentGroups")} ({Q("DepartmentId")} int,{Q("DepartmentGroupId")} int);
CREATE TABLE {Q("Documents")} ({Q("DepartmentId")} int,{Q("DocumentId")} int,{Q("RemoveOn")} {date},{Q("Name")} {text}(256),{Q("Category")} {text}(256),{Q("IsProtected")} {boolean},{Q("Data")} {(type == DatabaseTypes.Postgres ? "bytea" : "varbinary(max)")});
CREATE TABLE {Q("DepartmentMembers")} ({Q("DepartmentMemberId")} int,{Q("DepartmentId")} int,{Q("UserId")} {text}(128),{Q("IsDeleted")} {boolean},{Q("IsDisabled")} {boolean},{Q("IsHidden")} {boolean},{Q("PasswordLastSetOn")} {date});
CREATE TABLE {Q("AspNetUsers")} ({Q("Id")} {text}(128) PRIMARY KEY,{Q("TwoFactorEnabled")} {boolean},{Q("AuthenticationGeneration")} bigint);
CREATE TABLE {Q("ActionLogs")} ({Q("ActionLogId")} int PRIMARY KEY,{Q("UserId")} {text}(128),{Q("DepartmentId")} int,{Q("ActionTypeId")} int,{Q("Timestamp")} {date},{Q("GeoLocationData")} {text}(128));
INSERT INTO {Q("Departments")} VALUES (7),(8),(9),(10),(11),(12),(13);");
			}
			var source = new Mock<IMigrationSource>(); source.Setup(s => s.GetMigrations()).Returns(new IMigration[] { type == DatabaseTypes.Postgres ? new M0235_AddAdminAssistFoundationPg() : new M0235_AddAdminAssistFoundation(), type == DatabaseTypes.Postgres ? new M0237_AddAdminAssistConversationPg() : new M0237_AddAdminAssistConversation(), type == DatabaseTypes.Postgres ? new M0238_AddEnhancedAiAddonPg() : new M0238_AddEnhancedAiAddon(), type == DatabaseTypes.Postgres ? new M0239_AddAdminAssistDiagnosticsPg() : new M0239_AddAdminAssistDiagnostics(), type == DatabaseTypes.Postgres ? new M0240_AddAiDispatchEnrichmentPg() : new M0240_AddAiDispatchEnrichment(), type == DatabaseTypes.Postgres ? new M0241_AddAiDispatchSettingsPg() : new M0241_AddAiDispatchSettings(), type == DatabaseTypes.Postgres ? new M0242_AddAdminAssistPlansPg() : new M0242_AddAdminAssistPlans() });
			_runner = new ServiceCollection().AddFluentMigratorCore().ConfigureRunner(r => { if (type == DatabaseTypes.Postgres) r.AddPostgres(); else r.AddSqlServer(); r.WithGlobalConnectionString(_connection); }).AddSingleton(source.Object).BuildServiceProvider();
			_runner.GetRequiredService<IMigrationRunner>().MigrateUp();
		}
		[Test]
		public async Task Plan_storage_enforces_private_shared_cas_and_hold_aware_closed_retention()
		{
			await using var db = Connect(_connection); var ct = CancellationToken.None; var now = DateTime.UtcNow;
			await db.ExecuteAsync($"INSERT INTO {Q("Departments")} VALUES (9842)");
			using var unit = new UnitOfWork(Connections()); var repo = Repository(unit);
			var actor = new AdminAssistActor(9842, "plan-owner");
			var row = new AdminAssistPlanRow { Id = Guid.NewGuid().ToString("D"), DepartmentId = 9842, UserId = actor.UserId, CreatedOnUtc = now, UpdatedOnUtc = now, Revision = 1, Status = "Proposed", Content = "enc2:test" };
			await repo.SavePlanAsync(actor, row, 0, "0", 0, ct);
			Assert.That((await repo.ReadPlanAsync(actor, row.Id, ct)).CreatedOnUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
			Assert.That(await repo.ReadPlanAsync(actor with { UserId = "reader" }, row.Id, ct), Is.Null);
			row.Revision = 2; row.Shared = true;
			await repo.SavePlanAsync(actor, row, 1, "0", 0, ct);
			Assert.That(await repo.ReadPlanAsync(actor with { UserId = "reader" }, row.Id, ct), Is.Not.Null);
			Assert.That(await repo.ReadPlanAsync(actor with { DepartmentId = 7 }, row.Id, ct), Is.Null);
			Assert.ThrowsAsync<AdminAssistConcurrencyException>(() => repo.SavePlanAsync(actor, row, 1, "0", 0, ct));
			Assert.ThrowsAsync<ArgumentException>(() => repo.SavePlanAsync(actor with { UserId = "reader" }, row, 1, "0", 0, ct));
			row.Revision = 3; row.ClosedOnUtc = now; row.Status = "Closed";
			Assert.ThrowsAsync<AdminAssistConcurrencyException>(() => repo.SavePlanAsync(actor, row, 2, "999", 0, ct));
			await repo.SavePlanAsync(actor, row, 2, "0", 0, ct);
			await db.ExecuteAsync($"INSERT INTO {Q("RmsRecordLegalHolds")} ({Q("DepartmentId")}) VALUES (9842)");
			Assert.That(await repo.PurgeExpiredMetadataAsync(9842, now.AddDays(91), ct), Is.Zero);
			Assert.That(await repo.ListPlansAsync(actor, now.AddDays(1), ct), Is.Empty);
			await db.ExecuteAsync($"DELETE FROM {Q("RmsRecordLegalHolds")} WHERE {Q("DepartmentId")}=9842");
			await repo.PurgeExpiredMetadataAsync(9842, now.AddDays(91), ct);
			Assert.That(await repo.ReadPlanAsync(actor, row.Id, ct), Is.Null);
		}

		[Test]
		public async Task Diagnostic_storage_enforces_owner_cas_and_hold_aware_retention()
		{
			await using var db = Connect(_connection); var ct = CancellationToken.None;
			await db.ExecuteAsync($"INSERT INTO {Q("Departments")} VALUES (9830)");
			using var unit = new UnitOfWork(Connections()); var repo = Repository(unit);
			var actor = new AdminAssistActor(9830, "diagnostic-owner"); var now = DateTime.UtcNow;
			var row = new AdminAssistDiagnosticRun { Id = Guid.NewGuid().ToString("D"), DepartmentId = 9830, UserId = actor.UserId, Flow = "imports", CreatedOnUtc = now, Revision = 1, Content = "enc2:encrypted-test-data" };
			await repo.SaveDiagnosticAsync(actor, row, ct);
			Assert.That((await repo.ReadDiagnosticAsync(actor, row.Id, ct)).CreatedOnUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
			Assert.That(await repo.ReadDiagnosticAsync(actor with { UserId = "other" }, row.Id, ct), Is.Null);
			Assert.That(await repo.ReadDiagnosticAsync(actor with { DepartmentId = 7 }, row.Id, ct), Is.Null);
			Assert.ThrowsAsync<AdminAssistConcurrencyException>(() => repo.DeleteDiagnosticAsync(actor, new(row.Id, 2), ct));
			await db.ExecuteAsync($"INSERT INTO {Q("RmsRecordLegalHolds")} ({Q("DepartmentId")}) VALUES (9830)");
			await repo.DeleteDiagnosticAsync(actor, new(row.Id), ct);
			Assert.That(await repo.ListDiagnosticsAsync(actor, ct), Is.Empty);
			Assert.That(await repo.ReadDiagnosticAsync(actor, row.Id, ct), Is.Null);
			Assert.That(await repo.PurgeExpiredMetadataAsync(9830, now.AddDays(40), ct), Is.Zero);
			Assert.That(await db.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {Q("AdminAssistDiagnosticRuns")} WHERE {Q("DepartmentId")}=9830"), Is.EqualTo(1));
			await db.ExecuteAsync($"DELETE FROM {Q("RmsRecordLegalHolds")} WHERE {Q("DepartmentId")}=9830");
			await repo.PurgeExpiredMetadataAsync(9830, now.AddDays(40), ct);
			Assert.That(await db.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {Q("AdminAssistDiagnosticRuns")} WHERE {Q("DepartmentId")}=9830"), Is.Zero);
		}
		[Test]
		public async Task Diagnostic_admission_serializes_hosts_and_expires_crashed_owners()
		{
			await using var db = Connect(_connection); await db.ExecuteAsync($"INSERT INTO {Q("Departments")} VALUES (9831)");
			using var firstUnit = new UnitOfWork(Connections()); using var secondUnit = new UnitOfWork(Connections());
			var first = Repository(firstUnit); var second = Repository(secondUnit); var actor = new AdminAssistActor(9831, "a");
			var now = DateTime.UtcNow; var ct = CancellationToken.None;
			var ids = new[] { Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D") };
			var admitted = await Task.WhenAll(first.AcquireDiagnosticLeaseAsync(actor, ids[0], now, ct), second.AcquireDiagnosticLeaseAsync(actor, ids[1], now, ct));
			Assert.That(admitted.Count(x => x), Is.EqualTo(1));
			Assert.That(await second.AcquireDiagnosticLeaseAsync(actor with { UserId = "b" }, Guid.NewGuid().ToString("D"), now, ct), Is.True);
			Assert.That(await first.AcquireDiagnosticLeaseAsync(actor with { UserId = "c" }, Guid.NewGuid().ToString("D"), now, ct), Is.False);
			await first.ReleaseDiagnosticLeaseAsync(actor with { UserId = "other" }, ids[Array.IndexOf(admitted, true)], ct);
			Assert.That(await first.AcquireDiagnosticLeaseAsync(actor, Guid.NewGuid().ToString("D"), now, ct), Is.False);
			Assert.That(await first.AcquireDiagnosticLeaseAsync(actor, Guid.NewGuid().ToString("D"), now.AddSeconds(121), ct), Is.True);
		}

		[Test]
		public async Task Conversation_owner_revision_and_delete_are_enforced_in_storage()
		{
			// Arrange
			await using var db = Connect(_connection);
			await db.ExecuteAsync($"INSERT INTO {Q("Departments")} VALUES (800)");
			using var unit = new UnitOfWork(Connections()); var repository = Repository(unit);
			var actor = new AdminAssistActor(800, "ask-admin");
			var row = new AiGenerationRow { Id = Guid.NewGuid().ToString("D"), ConversationId = Guid.NewGuid().ToString("D"), DepartmentId = actor.DepartmentId,
				UserId = actor.UserId, Content = "enc2:encrypted-test-content", CreatedOnUtc = DateTime.UtcNow, PromptVersion = "test", ModelRevision = "test", RuntimeDigest = "test", RequestDigest = "test", Outcome = "Answered" };
			// Act
			await repository.SaveAsync(actor, row, 0, CancellationToken.None);
			// Assert
			Assert.That(await repository.GetRevisionAsync(actor, row.ConversationId, CancellationToken.None), Is.EqualTo(1));
			Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await repository.ReadAsync(actor with { UserId = "other-admin" }, row.ConversationId, CancellationToken.None));
			Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await repository.ReadAsync(actor with { DepartmentId = 801 }, row.ConversationId, CancellationToken.None));
			Assert.ThrowsAsync<AdminAssistConcurrencyException>(async () => await repository.SaveAsync(actor, row, 0, CancellationToken.None));
			await repository.DeleteAsync(actor, row.ConversationId, 1, CancellationToken.None);
			Assert.That(await repository.ListAsync(actor, CancellationToken.None), Is.Empty);
			Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await repository.ReadAsync(actor, row.ConversationId, CancellationToken.None));
		}

		[Test]
		public async Task Admission_serializes_hosts_and_keeps_uncertain_usage_charged()
		{
			// Arrange
			using var firstUnit = new UnitOfWork(Connections()); using var secondUnit = new UnitOfWork(Connections());
			var first = Repository(firstUnit); var second = Repository(secondUnit);
			var now = DateTime.UtcNow;
			// Act
			var reservations = await Task.WhenAll(first.ReserveAsync(new(801, "admin-a"), now, 8192, 16384, CancellationToken.None), second.ReserveAsync(new(801, "admin-b"), now, 8192, 16384, CancellationToken.None));
			// Assert
			Assert.That(reservations.Count(r => r != null), Is.EqualTo(1));
			Assert.That(await first.RemainingAsync(801, now.AddMinutes(3), 16384, CancellationToken.None), Is.EqualTo(8192));
			await first.CompleteAsync(reservations.Single(r => r != null), 500, "Answered", CancellationToken.None);
			await first.CompleteAsync(reservations.Single(r => r != null), 1, "Answered", CancellationToken.None);
			Assert.That(await first.RemainingAsync(801, now, 16384, CancellationToken.None), Is.EqualTo(15884));
		}

		[Test]
		public async Task Free_admission_counts_only_answered_questions_and_never_takes_the_last_slot()
		{
			// Arrange (enhanced-ai-addon-plan.md §5.4: free turns hold at most one of the two slots; only answers are charged)
			using var firstUnit = new UnitOfWork(Connections()); using var secondUnit = new UnitOfWork(Connections());
			var first = Repository(firstUnit); var second = Repository(secondUnit);
			var now = DateTime.UtcNow; var ct = CancellationToken.None;
			var window = AdminAssistFreeAllowance.Current(null, now, 2, 30, 1);
			// Act / Assert — one free slot, the other stays available to a paying department
			var free = await first.ReserveFreeAsync(new(821, "free-a"), now, 8192, window, 25, ct);
			Assert.That(free.Reason, Is.EqualTo("Reserved"));
			Assert.That((await second.ReserveFreeAsync(new(822, "free-b"), now, 8192, window, 25, ct)).Reason, Is.EqualTo("Busy"));
			var paid = await second.ReserveAsync(new(820, "paid-admin"), now, 8192, 1_000_000, ct);
			Assert.That(paid, Is.Not.Null);
			await first.CompleteAsync(free.Reservation, 100, "Unavailable", ct);
			await second.CompleteAsync(paid, 100, "Answered", ct);
			Assert.That(await first.GetFreeUsageAsync(821, window, now, ct), Is.EqualTo(new AiFreeUsage(0, 1)));
			Assert.That(await first.GetFirstAnsweredAsync(821, ct), Is.Null);
			for (var i = 0; i < 2; i++)
			{
				var answered = await first.ReserveFreeAsync(new(821, "free-a"), now, 8192, window, 25, ct);
				Assert.That(answered.Reason, Is.EqualTo("Reserved"));
				await first.CompleteAsync(answered.Reservation, 100, "Answered", ct);
			}
			Assert.That((await first.ReserveFreeAsync(new(821, "free-a"), now, 8192, window, 25, ct)).Reason, Is.EqualTo("FreeAllowanceExhausted"));
			Assert.That(await first.GetFirstAnsweredAsync(821, ct), Is.Not.Null);
			Assert.That(await first.GetFirstAnsweredAsync(820, ct), Is.Not.Null, "paid answers open the starter window too");
			var other = await second.ReserveFreeAsync(new(823, "free-c"), now, 8192, window, 1, ct);
			Assert.That(other.Reason, Is.EqualTo("Reserved"));
			await second.CompleteAsync(other.Reservation, 0, "Cancelled", ct);
			Assert.That((await second.ReserveFreeAsync(new(823, "free-c"), now, 8192, window, 1, ct)).Reason, Is.EqualTo("FreeAttemptLimit"));
		}

		[Test]
		public async Task Ai_dispatch_claims_a_call_once_and_background_admission_waits_for_idle_slots()
		{
			// Arrange (enhanced-ai-addon-plan.md §4: at-least-once delivery, interactive work first)
			using var firstUnit = new UnitOfWork(Connections()); using var secondUnit = new UnitOfWork(Connections());
			var first = Repository(firstUnit); var second = Repository(secondUnit);
			var audits = new AiDispatchAuditRepository(Connections(), type == DatabaseTypes.Postgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration(), firstUnit, Mock.Of<IQueryFactory>());
			var now = DateTime.UtcNow; var ct = CancellationToken.None;
			await using (var db = Connect(_connection))
			{
				// The viewer joins call numbers; the fixture otherwise has no Calls table.
				await db.ExecuteAsync(type == DatabaseTypes.Postgres
					? "CREATE TABLE IF NOT EXISTS calls (callid int PRIMARY KEY, departmentid int, number varchar(64))"
					: "IF OBJECT_ID('[Calls]', 'U') IS NULL CREATE TABLE [Calls] ([CallId] int PRIMARY KEY, [DepartmentId] int, [Number] nvarchar(64))");
				await db.ExecuteAsync($"INSERT INTO {Q("Calls")} ({Q("CallId")},{Q("DepartmentId")},{Q("Number")}) VALUES (9001,830,'26-9001')");
			}
			Resgrid.Model.AiDispatch.AiDispatchAuditRow Claim() => new() { AiDispatchAuditId = Guid.NewGuid().ToString("D"), DepartmentId = 830, CallId = 9001, Mode = "Enrich",
				Outcome = Resgrid.Model.AiDispatch.AiDispatchOutcomes.InProgress, CreatedOnUtc = now };
			// Act / Assert — the unique (DepartmentId, CallId) index is the claim
			var row = Claim();
			Assert.That(await audits.TryClaimAsync(row, ct), Is.True);
			Assert.That(await audits.TryClaimAsync(Claim(), ct), Is.False);
			row.Outcome = Resgrid.Model.AiDispatch.AiDispatchOutcomes.Applied; row.AppliedFields = "Type,Note"; row.CompletedOnUtc = now;
			await audits.CompleteAsync(row, ct);
			var recent = (await audits.GetRecentAsync(830, 5, ct)).Single();
			Assert.That(recent.Audit.Outcome, Is.EqualTo(Resgrid.Model.AiDispatch.AiDispatchOutcomes.Applied));
			Assert.That(recent.CallNumber, Is.EqualTo("26-9001"));
			Assert.That(await audits.PruneAsync(830, now.AddMinutes(1), ct), Is.EqualTo(1));
			// Settings are compare-and-swap: a stale revision never overwrites another admin's save.
			var settings = new AiDispatchConfigRepository(Connections(), type == DatabaseTypes.Postgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration(), firstUnit, Mock.Of<IQueryFactory>());
			Assert.That(await settings.SaveAsync(new Resgrid.Model.AiDispatch.DepartmentAiDispatchConfig { DepartmentId = 830, MonthlyTokenCap = 20000, FillAddress = false }, 0, ct), Is.True);
			Assert.That(await settings.SaveAsync(new Resgrid.Model.AiDispatch.DepartmentAiDispatchConfig { DepartmentId = 830 }, 0, ct), Is.False);
			Assert.That(await settings.SaveAsync(new Resgrid.Model.AiDispatch.DepartmentAiDispatchConfig { DepartmentId = 830, MonthlyTokenCap = 30000 }, 1, ct), Is.True);
			Assert.That(await settings.SaveAsync(new Resgrid.Model.AiDispatch.DepartmentAiDispatchConfig { DepartmentId = 830 }, 1, ct), Is.False);
			var stored = await settings.GetAsync(830, ct);
			Assert.That((stored.Revision, stored.MonthlyTokenCap, stored.FillAddress), Is.EqualTo((2L, (int?)30000, true)));
			// Background work never starts while any turn is live, and draws on the department budget.
			var interactive = await first.ReserveAsync(new(831, "admin"), now, 8192, 1_000_000, ct);
			Assert.That(await second.ReserveBackgroundAsync(830, "AiDispatch", "EnhancedAi", now, 8192, 1_000_000, ct), Is.Null);
			await first.CompleteAsync(interactive, 100, "Answered", ct);
			var background = await second.ReserveBackgroundAsync(830, "AiDispatch", "EnhancedAi", now, 8192, 1_000_000, ct);
			Assert.That(background, Is.Not.Null);
			Assert.That(await second.ReserveBackgroundAsync(832, "AiDispatch", "EnhancedAi", now, 8192, 1_000_000, ct), Is.Null, "one background turn at a time");
			await second.CompleteAsync(background, 0, "Unavailable", ct);
			Assert.That(await second.ReserveBackgroundAsync(830, "AiDispatch", "EnhancedAi", now, 8192, 4096, ct), Is.Null, "over the monthly budget");
		}

		[Test]
		public async Task Ledger_retention_keeps_an_anonymous_first_answer_without_restarting_starter_allowance()
		{
			await using var db = Connect(_connection);
			await db.ExecuteAsync($"INSERT INTO {Q("Departments")} VALUES (824)");
			using var unit = new UnitOfWork(Connections()); var repository = Repository(unit);
			var actor = new AdminAssistActor(824, "former-admin");
			var now = DateTime.UtcNow;
			var firstDate = now.AddMonths(-15);
			var first = await repository.ReserveAsync(actor, firstDate, 8192, 100000, CancellationToken.None);
			await repository.CompleteAsync(first, 100, "Answered", CancellationToken.None);
			var later = await repository.ReserveAsync(actor, firstDate.AddDays(1), 8192, 100000, CancellationToken.None);
			await repository.CompleteAsync(later, 100, "Answered", CancellationToken.None);
			var recordedFirst = await repository.GetFirstAnsweredAsync(824, CancellationToken.None);

			await repository.PurgeExpiredMetadataAsync(824, now, CancellationToken.None);

			Assert.That(await repository.GetFirstAnsweredAsync(824, CancellationToken.None), Is.EqualTo(recordedFirst));
			Assert.That(await db.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {Q("AiUsageLedger")} WHERE {Q("DepartmentId")}=824"), Is.EqualTo(1));
			Assert.That(await db.ExecuteScalarAsync<string>($"SELECT {Q("UserId")} FROM {Q("AiUsageLedger")} WHERE {Q("DepartmentId")}=824"), Is.Empty);
			Assert.That(AdminAssistFreeAllowance.Current(recordedFirst, now, 20, 30, 4).Allowance, Is.EqualTo(4));
		}

		[Test]
		public async Task Trace_replay_is_idempotent_tenant_bound_and_respects_department_removal()
		{
			await using var db = Connect(_connection);
			await db.ExecuteAsync($"INSERT INTO {Q("Departments")} VALUES (715),(716)");
			using var unit = new UnitOfWork(Connections()); var repository = Repository(unit);
			var row = DispatchTraceQueueTests.Row(); row.DepartmentId = 715;
			row.Content = System.Text.Json.JsonSerializer.Serialize(System.Text.Json.JsonSerializer.Deserialize<DispatchTraceObservation>(row.Content) with { DepartmentId = 715 });
			await repository.SaveTraceAsync(row, CancellationToken.None); await repository.SaveTraceAsync(row, CancellationToken.None);
			Assert.That(await db.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {Q("AdminAssistDispatchTraces")} WHERE {Q("DepartmentId")}=715"), Is.EqualTo(1));
			row.DepartmentId = 716;
			Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await repository.SaveTraceAsync(row, CancellationToken.None));
			Assert.That(await repository.TraceDepartmentExistsAsync(715, CancellationToken.None), Is.True);
			await db.ExecuteAsync($"DELETE FROM {Q("AdminAssistDispatchTraces")} WHERE {Q("DepartmentId")}=715; DELETE FROM {Q("Departments")} WHERE {Q("DepartmentId")}=715");
			Assert.That(await repository.TraceDepartmentExistsAsync(715, CancellationToken.None), Is.False);
		}

		[Test]
		public async Task Bulk_group_membership_change_audit_and_revision_roll_back_together_on_sink_failure()
		{
			await using var db = Connect(_connection);
			var text = type == DatabaseTypes.Postgres ? "varchar" : "nvarchar";
			await db.ExecuteAsync($"CREATE TABLE {Q("DepartmentGroupMembers")} ({Q("DepartmentGroupMemberId")} int,{Q("DepartmentGroupId")} int,{Q("DepartmentId")} int,{Q("UserId")} {text}(128)); CREATE TABLE {Q("AdminAssistAuditProof")} ({Q("Data")} {text}(4000)); INSERT INTO {Q("Departments")} VALUES (711); INSERT INTO {Q("DepartmentGroups")} VALUES (711,7001); INSERT INTO {Q("DepartmentGroupMembers")} VALUES (1,7001,711,'private-user-a'),(2,7001,711,'private-user-b')");
			var configuration = type == DatabaseTypes.Postgres ? (SqlConfiguration)new PostgreSqlConfiguration() : new SqlServerConfiguration();
			var queries = new Mock<IQueryFactory>();
			queries.Setup(q => q.GetDeleteQuery<Resgrid.Repositories.DataRepository.Queries.DepartmentGroups.DeleteGroupMembersByGroupIdDidQuery>()).Returns(new Resgrid.Repositories.DataRepository.Queries.DepartmentGroups.DeleteGroupMembersByGroupIdDidQuery(configuration).GetQuery());
			foreach (var fail in new[] { true, false })
			{
				using var unit = new UnitOfWork(Connections()); var metadata = Repository(unit);
				var audits = new Mock<Resgrid.Model.Repositories.IAuditLogsRepository>();
				audits.Setup(a => a.SaveOrUpdateAsync(It.IsAny<Resgrid.Model.AuditLog>(), It.IsAny<CancellationToken>(), false))
					.Returns<Resgrid.Model.AuditLog, CancellationToken, bool>(async (row, token, firstLevelOnly) => {
						await unit.Connection.ExecuteAsync($"INSERT INTO {Q("AdminAssistAuditProof")} VALUES (@Data)", new { row.Data }, unit.Transaction);
						if (fail) throw new InvalidOperationException("Injected audit sink failure."); return row;
					});
				var journal = new Resgrid.Services.AdminAssist.ConfigurationChangeJournal(unit, metadata, audits.Object, Mock.Of<Resgrid.Model.Services.IProtectedGrantContext>(), TimeProvider.System);
				var groups = new DepartmentGroupMembersRepository(Connections(), configuration, unit, queries.Object, journal);
				Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await groups.DeleteGroupMembersByGroupIdAsync(7001,712));
				if (fail) Assert.ThrowsAsync<InvalidOperationException>(async () => await groups.DeleteGroupMembersByGroupIdAsync(7001,711));
				else Assert.That(await groups.DeleteGroupMembersByGroupIdAsync(7001,711), Is.True);
				Assert.That(await db.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {Q("DepartmentGroupMembers")}"), Is.EqualTo(fail ? 2 : 0));
				Assert.That(await db.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {Q("AdminAssistAuditProof")}"), Is.EqualTo(fail ? 0 : 1));
				Assert.That(await metadata.GetConfigurationRevisionAsync(711, CancellationToken.None), Is.EqualTo(fail ? 0 : 1));
			}
			var data = await db.ExecuteScalarAsync<string>($"SELECT {Q("Data")} FROM {Q("AdminAssistAuditProof")}");
			Assert.That(data, Does.Contain("BulkDelete").And.Not.Contain("private-user"));
		}

		[Test]
		public async Task Security_projection_reads_only_bounded_tenant_policy_member_and_session_metadata()
		{
			await using var db = Connect(_connection);
			var text = type == DatabaseTypes.Postgres ? "varchar" : "nvarchar";
			var date = type == DatabaseTypes.Postgres ? "timestamp" : "datetime2";
			var boolean = type == DatabaseTypes.Postgres ? "boolean" : "bit";
			await db.ExecuteAsync($"CREATE TABLE {Q("DepartmentSecurityPolicies")} ({Q("DepartmentId")} int,{Q("RequireMfa")} {boolean},{Q("RequireSso")} {boolean},{Q("SessionTimeoutMinutes")} int,{Q("MaxConcurrentSessions")} int,{Q("PasswordExpirationDays")} int,{Q("MinPasswordLength")} int)");
			await db.ExecuteAsync($"CREATE TABLE {Q("DepartmentSsoConfigs")} ({Q("DepartmentId")} int,{Q("IsEnabled")} {boolean})");
			await db.ExecuteAsync($"CREATE TABLE {Q("UserSessions")} ({Q("UserSessionId")} {text}(128),{Q("UserId")} {text}(128),{Q("DepartmentId")} int,{Q("State")} int,{Q("CreatedOn")} {date},{Q("LastActiveOn")} {date},{Q("ExpiresOn")} {date},{Q("AuthenticationGeneration")} bigint)");
			var now = new DateTime(2026,9,24,12,0,0);
			var args = new { False = false, True = true, Now = now, Old = now.AddHours(-1), Future = now.AddHours(1) };
			await db.ExecuteAsync($"INSERT INTO {Q("DepartmentMembers")} ({Q("DepartmentMemberId")},{Q("DepartmentId")},{Q("UserId")},{Q("IsDeleted")},{Q("IsDisabled")},{Q("IsHidden")},{Q("PasswordLastSetOn")}) VALUES (1,709,'security-a',@False,@False,@True,@Old),(2,709,'security-missing',@False,@False,@False,NULL),(3,709,'security-disabled',@False,@True,@False,NULL),(4,710,'security-other',@False,@False,@False,NULL)",args);
			await db.ExecuteAsync($"INSERT INTO {Q("AspNetUsers")} ({Q("Id")},{Q("TwoFactorEnabled")},{Q("AuthenticationGeneration")}) VALUES ('security-a',@True,4); INSERT INTO {Q("DepartmentSsoConfigs")} VALUES (709,@True),(709,@False),(710,@True); INSERT INTO {Q("DepartmentSecurityPolicies")} VALUES (709,@True,@False,30,2,90,12)",args);
			await db.ExecuteAsync($"INSERT INTO {Q("UserSessions")} VALUES ('a','security-a',709,0,@Old,@Now,@Future,4),('b','security-a',709,0,@Old,@Now,@Future,3),('expired','security-a',709,0,@Old,@Old,@Old,4),('revoked','security-a',709,1,@Old,@Now,@Future,4),('disabled','security-disabled',709,0,@Old,@Now,@Future,4),('foreign','security-other',710,0,@Old,@Now,@Future,4)",args);
			using var unit = new UnitOfWork(Connections()); var repository = Repository(unit);
			var policy = await repository.ReadSecurityPolicyAsync(709, CancellationToken.None);
			Assert.That(policy.RequireMfa, Is.True); Assert.That(policy.MinPasswordLength, Is.EqualTo(12)); Assert.That(await repository.ReadSecurityPolicyAsync(710, CancellationToken.None), Is.Null);
			var evidence = await repository.ReadSecurityImpactAsync(709, now, 10, CancellationToken.None);
			Assert.That(evidence.Members.Count, Is.EqualTo(2)); Assert.That(evidence.Members[0].TwoFactorEnabled, Is.True); Assert.That(evidence.Members[1].TwoFactorEnabled, Is.Null);
			Assert.That(evidence.Sessions.Select(s => s.Id), Is.EqualTo(new[] { "a", "b" })); Assert.That(evidence.Sessions[1].CurrentGeneration, Is.EqualTo(4)); Assert.That(evidence.EnabledSsoProviders, Is.EqualTo(1));
			Assert.That((await repository.ReadSecurityImpactAsync(709, now, 1, CancellationToken.None)).Members.Count, Is.EqualTo(2));
		}

		[Test]
		public async Task Notification_projection_is_bounded_and_does_not_return_foreign_staffing_or_contact_content()
		{
			await using var db = Connect(_connection);
			var text = type == DatabaseTypes.Postgres ? "varchar" : "nvarchar";
			var boolean = type == DatabaseTypes.Postgres ? "boolean" : "bit";
			await db.ExecuteAsync($"CREATE TABLE {Q("UserProfiles")} ({Q("UserProfileId")} int,{Q("UserId")} {text}(128),{Q("SendNotificationSms")} {boolean},{Q("MobileNumberVerified")} {boolean},{Q("SendNotificationEmail")} {boolean},{Q("EmailVerified")} {boolean},{Q("SendNotificationPush")} {boolean})");
			await db.ExecuteAsync($"CREATE TABLE {Q("UserStates")} ({Q("UserStateId")} int PRIMARY KEY,{Q("UserId")} {text}(128),{Q("DepartmentId")} int,{Q("State")} int)");
			await db.ExecuteAsync($"INSERT INTO {Q("DepartmentMembers")} ({Q("DepartmentMemberId")},{Q("DepartmentId")},{Q("UserId")},{Q("IsDeleted")},{Q("IsDisabled")},{Q("IsHidden")}) VALUES (1,707,'notify-a',@False,@False,@False),(2,707,'notify-b',@False,@False,@True),(3,707,'notify-c',@False,@False,@False),(4,707,'notify-disabled',@False,@True,@False),(5,708,'notify-other',@False,@False,@False)", new { False = false, True = true });
			await db.ExecuteAsync($"INSERT INTO {Q("UserProfiles")} VALUES (1,'notify-a',@True,NULL,@True,@False,@True); INSERT INTO {Q("UserStates")} VALUES (1,'notify-a',707,2),(2,'notify-a',708,42),(3,'notify-b',707,1)", new { False = false, True = true });
			using var unit = new UnitOfWork(Connections());
			var rows = await Repository(unit).ReadNotificationMembersAsync(707, 10, CancellationToken.None);
			Assert.That(rows.Count, Is.EqualTo(3)); Assert.That(rows[0].StaffingKnown, Is.False); Assert.That(rows[0].Staffing, Is.Null);
			Assert.That(rows[0].Sms, Is.True); Assert.That(rows[0].MobileVerified, Is.Null); Assert.That(rows[0].EmailVerified, Is.False);
			Assert.That(rows[1].Staffing, Is.EqualTo(1)); Assert.That(rows[1].ProfileId, Is.Null);
			Assert.That(rows[2].StaffingKnown, Is.True); Assert.That(rows[2].Staffing, Is.Zero); // matches owning getter's default Available
			Assert.That((await Repository(unit).ReadNotificationMembersAsync(707, 1, CancellationToken.None)).Count, Is.EqualTo(2));
		}

		[Test]
		public async Task Retention_preview_is_tenant_bounded_metadata_only_and_excludes_sticky_and_period_holds()
		{
			await using var db = Connect(_connection);
			var text = type == DatabaseTypes.Postgres ? "varchar" : "nvarchar";
			var date = type == DatabaseTypes.Postgres ? "timestamp" : "datetime2";
			foreach (var table in new[] { "RmsOperationalRecords", "RmsIncidentReports" })
			{
				var id = table == "RmsOperationalRecords" ? "RmsOperationalRecordId" : "RmsIncidentReportId";
				await db.ExecuteAsync($"CREATE TABLE {Q(table)} ({Q(id)} {text}(128),{Q("DepartmentId")} int,{Q("DefinitionKey")} {text}(128),{Q("State")} int,{Q("FinalizedOn")} {date},{Q("ModifiedOn")} {date},{Q("AmendsRevisionId")} {text}(128),{Q("RowVersion")} bigint,{Q("PurgedOn")} {date})");
			}
			await db.ExecuteAsync($"CREATE TABLE {Q("RmsRecordLegalHoldMembers")} ({Q("DepartmentId")} int,{Q("HoldId")} {text}(128),{Q("RecordId")} {text}(128))");
			foreach (var table in new[] { "RmsCasualtyRescues", "RmsExposures" }) await db.ExecuteAsync($"CREATE TABLE {Q(table)} ({Q("DepartmentId")} int,{Q("RecordId")} {text}(128))");
			await db.ExecuteAsync($"INSERT INTO {Q("RmsOperationalRecords")} ({Q("RmsOperationalRecordId")},{Q("DepartmentId")},{Q("DefinitionKey")},{Q("State")},{Q("ModifiedOn")},{Q("RowVersion")}) VALUES ('a',705,'training',1,@Now,5),('b',705,'training',1,@Now,6),('c',705,'training',1,@Now,7),('foreign',706,'training',1,@Now,8)", new { Now = new DateTime(2020, 1, 1) });
			await db.ExecuteAsync($"INSERT INTO {Q("RmsRecordLegalHolds")} ({Q("DepartmentId")},{Q("RmsRecordLegalHoldId")},{Q("RecordId")},{Q("DefinitionKey")}) VALUES (705,'sticky','old',NULL),(705,'period',NULL,'training'),(706,'foreign','b',NULL)");
			await db.ExecuteAsync($"INSERT INTO {Q("RmsRecordLegalHoldMembers")} ({Q("DepartmentId")},{Q("HoldId")},{Q("RecordId")}) VALUES (705,'sticky','a')");
			using var unit = new UnitOfWork(Connections());
			var rows = await Repository(unit).ReadRetentionHeadersAsync(705, 1, CancellationToken.None);
			Assert.That(rows.Select(r => r.RecordId), Is.EqualTo(new[] { "a", "b" })); // bound + one detects an incomplete sample
			Assert.That(rows[0].HoldOrPermanentContent, Is.EqualTo(1)); Assert.That(rows[1].HoldOrPermanentContent, Is.Zero);
			Assert.That(rows.All(r => r.HistoricalHoldUncertainty == 1), Is.True);
			Assert.That(await db.ExecuteScalarAsync<long>($"SELECT SUM({Q("RowVersion")}) FROM {Q("RmsOperationalRecords")}"), Is.EqualTo(26));
		}

		[Test]
		public async Task Administrative_references_detect_cross_tenant_missing_and_expired_documents_without_loading_content()
		{
			await using var db = Connect(_connection); var now = new DateTime(2026, 9, 24, 12, 0, 0);
			await db.ExecuteAsync($"INSERT INTO {Q("Documents")} ({Q("DepartmentId")},{Q("DocumentId")},{Q("RemoveOn")}) VALUES (703,9101,@Expired),(703,9102,@Soon),(703,9103,NULL),(704,9104,NULL)", new { Expired = now.AddDays(-1), Soon = now.AddDays(3) });
			await db.ExecuteAsync($"INSERT INTO {Q("DepartmentGroups")} ({Q("DepartmentId")},{Q("DepartmentGroupId")}) VALUES (703,9101),(704,9102)");
			using var unit = new UnitOfWork(Connections());
			var counts = await Repository(unit).ReadAdministrativeReferencesAsync(703, new[] { 9101,9102,9103,9104,9105 }, new[] { 9101,9102 }, now, CancellationToken.None);
			Assert.That(counts.PolicyReferences, Is.EqualTo(5)); Assert.That(counts.UnavailablePolicies, Is.EqualTo(3));
			Assert.That(counts.ExpiringPolicies, Is.EqualTo(1)); Assert.That(counts.UnavailableSites, Is.EqualTo(1));
		}

		[Test]
		public async Task Operating_profile_document_options_are_this_departments_unexpired_documents_without_contents()
		{
			await using var db = Connect(_connection); var now = new DateTime(2026, 9, 24, 12, 0, 0);
			await db.ExecuteAsync($"INSERT INTO {Q("Documents")} ({Q("DepartmentId")},{Q("DocumentId")},{Q("Name")},{Q("Category")},{Q("IsProtected")},{Q("RemoveOn")},{Q("Data")}) " +
				"VALUES (705,9201,'Staffing policy','Policies',@False,NULL,@Data),(705,9202,'Expired policy','Policies',@False,@Expired,@Data),(705,9203,'Protected procedure',NULL,@True,@Later,@Data),(706,9204,'Other department',NULL,@False,NULL,@Data)",
				new { False = false, True = true, Expired = now.AddDays(-1), Later = now.AddDays(3), Data = new byte[] { 1, 2, 3 } });
			using var unit = new UnitOfWork(Connections());
			var options = await Repository(unit).GetOperatingProfileDocumentOptionsAsync(705, now, CancellationToken.None);
			Assert.That(options.Select(d => d.DocumentId).OrderBy(id => id), Is.EqualTo(new[] { 9201, 9203 }));
			var policy = options.Single(d => d.DocumentId == 9201);
			Assert.That((policy.Name, policy.Category, policy.IsProtected), Is.EqualTo(("Staffing policy", "Policies", false)));
			Assert.That(options.Single(d => d.DocumentId == 9203).IsProtected, Is.True);
			Assert.That(options.All(d => d.Data == null), Is.True, "A picker never reads file contents.");
		}

		[Test]
		public async Task Module_preview_counts_are_bounded_tenant_metadata_and_keep_hidden_current_members()
		{
			await using var db = Connect(_connection);
			await db.ExecuteAsync($"INSERT INTO {Q("DepartmentMembers")} ({Q("DepartmentId")},{Q("UserId")},{Q("IsDeleted")},{Q("IsDisabled")},{Q("IsHidden")}) VALUES (701,'current',@False,@False,@False),(701,'hidden',@False,@False,@True),(701,'disabled',@False,@True,@False),(702,'other',@False,@False,@False)", new { False = false, True = true });
			await db.ExecuteAsync($"INSERT INTO {Q("Documents")} ({Q("DepartmentId")},{Q("DocumentId")}) VALUES (701,9001),(702,9002)");
			using var unit = new UnitOfWork(Connections());
			var result = await Repository(unit).ReadModuleImpactCountsAsync(701, "Documents", 20, CancellationToken.None);
			Assert.That(result.Members, Is.EqualTo(2)); Assert.That(result.ContentRows, Is.EqualTo(1));
			Assert.ThrowsAsync<InvalidOperationException>(async () => await Repository(unit).ReadModuleImpactCountsAsync(701, "Documents", 1, CancellationToken.None));
			Assert.That(await db.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {Q("Documents")} WHERE {Q("DepartmentId")}=701"), Is.EqualTo(1));
		}

		[Test]
		public async Task Legacy_workspace_upgrade_preserves_scope_and_review_uses_the_current_configuration_revision()
		{
			await using var db = Connect(_connection);
			await db.ExecuteAsync($"INSERT INTO {Q("Departments")} VALUES (717); INSERT INTO {Q("AdminAssistWorkspaces")} ({Q("DepartmentId")},{Q("Revision")},{Q("Mode")},{Q("AreasJson")},{Q("CatalogVersion")},{Q("ModifiedOn")}) VALUES (717,3,0,@Areas,'previous',@Now)",
				new { Areas = "{\"home\":0,\"security\":0,\"personnel\":1}", Now = DateTime.UtcNow });
			using var unit = new UnitOfWork(Connections()); var repository = Repository(unit);
			var actor = new AdminAssistActor(717, "setup-admin");
			var original = await repository.GetWorkspaceAsync(717, actor.UserId, "current", CancellationToken.None);
			Assert.That(original.Areas["personnel"], Is.EqualTo(SetupAreaChoice.LearnLater));
			var scoped = await repository.UpdateWorkspaceAsync(actor, new(3,"area","personnel","NotApplicable","current","OtherSystem"), CancellationToken.None);
			Assert.That(scoped.AreaReasons["personnel"], Is.EqualTo(SetupAreaReason.OtherSystem));
			var revisited = await repository.UpdateWorkspaceAsync(actor, new(4,"revisit",CatalogVersion:"current",RevisitOnUtc:DateTime.UtcNow.AddDays(10)), CancellationToken.None);
			Assert.That(revisited.RevisitOnUtc?.Kind, Is.EqualTo(DateTimeKind.Utc));
			var review = new SetupReviewEvidence("current", "0", DateTime.UtcNow, 12, 3, 4, 5, scoped.ScopeRevision);
			var reviewed = await repository.UpdateWorkspaceAsync(actor, new(5,"review",CatalogVersion:"current") { ReviewEvidence = review }, CancellationToken.None);
			Assert.That(reviewed.ReviewEvidence, Is.EqualTo(review));
			Assert.That(reviewed.ReviewedOnUtc?.Kind, Is.EqualTo(DateTimeKind.Utc));
			Assert.ThrowsAsync<AdminAssistConcurrencyException>(async () => await repository.UpdateWorkspaceAsync(actor,
				new(6,"review",CatalogVersion:"current") { ReviewEvidence = review with { SnapshotRevision = "1" } }, CancellationToken.None));
			Assert.That((await repository.GetWorkspaceAsync(717, actor.UserId, "current", CancellationToken.None)).Revision, Is.EqualTo(6));
			await repository.UpdateWorkspaceAsync(actor, new(6,"area","personnel","UseNow","current"), CancellationToken.None);
			var updated = await repository.GetWorkspaceAsync(717, actor.UserId, "current", CancellationToken.None);
			Assert.That(updated.AreaReasons.ContainsKey("personnel"), Is.False);
			Assert.That(updated.ReviewEvidence.Unknown, Is.EqualTo(5));
			Assert.That(updated.ScopeRevision, Is.GreaterThan(updated.ReviewEvidence.ScopeRevision));
			Assert.That((await repository.GetHistoryAsync(717, actor.UserId, 0, 20, CancellationToken.None)).Any(h => h.BeforeCode == "NotApplicable:OtherSystem" && h.AfterCode == "UseNow"), Is.True);
			Assert.That((await repository.GetWorkspaceAsync(718, actor.UserId, "current", CancellationToken.None)).ReviewEvidence, Is.Null);
		}

		[Test]
		public async Task Concurrent_first_workspace_save_has_one_winner()
		{
			async Task<bool> Save()
			{
				var unit = new UnitOfWork(Connections());
				try { await Repository(unit).UpdateWorkspaceAsync(new AdminAssistActor(7, "admin"), new SetupProgressCommand(0, "mode", Choice: "Review", CatalogVersion: "test"), CancellationToken.None); return true; }
				catch (AdminAssistConcurrencyException) { return false; }
				finally { unit.DiscardChanges(); }
			}
			var result = await Task.WhenAll(Save(), Save()); Assert.That(result.Count(winner => winner), Is.EqualTo(1));
		}
		[Test]
		public async Task Configuration_revision_and_history_roll_back_together()
		{
			var unit = new UnitOfWork(Connections()); var repository = Repository(unit);
			await unit.CreateOrGetConnectionAsync(); await repository.LockConfigurationAsync(8, CancellationToken.None);
			await repository.AppendConfigurationChangeAsync(8, "admin", "setting.EnableTextToCall", "false", "true", "correlation", CancellationToken.None);
			unit.DiscardChanges();
			Assert.That(await repository.GetConfigurationRevisionAsync(8, CancellationToken.None), Is.Zero);
			Assert.That(await repository.GetHistoryAsync(8, "admin", 0, 10, CancellationToken.None), Is.Empty);

		}
		[Test]
		public async Task Personal_history_is_filtered_before_pagination_and_never_crosses_tenant()
		{
			var repository = Repository(new UnitOfWork(Connections()));
			await repository.UpdateWorkspaceAsync(new AdminAssistActor(9, "other"), new SetupProgressCommand(0, "learn", "feature", "true", "test"), CancellationToken.None);
			await repository.UpdateWorkspaceAsync(new AdminAssistActor(9, "admin"), new SetupProgressCommand(1, "learn", "feature", "true", "test"), CancellationToken.None);
			Assert.That((await repository.GetHistoryAsync(9, "admin", 0, 1, CancellationToken.None)).Single().ActorId, Is.EqualTo("admin"));
			Assert.That(await repository.GetHistoryAsync(9, "admin", 1, 1, CancellationToken.None), Is.Empty);
			Assert.That(await repository.GetHistoryAsync(8, "admin", 0, 10, CancellationToken.None), Is.Empty);
			var dismissed = await repository.UpdateWorkspaceAsync(new AdminAssistActor(9, "other"), new SetupProgressCommand(2, "dismiss", Choice: "true", CatalogVersion: "test"), CancellationToken.None);
			Assert.That(dismissed.SetupPromptDismissed, Is.True);
			Assert.That(dismissed.LearnedCapabilityIds, Is.EquivalentTo(new[] { "feature" }));
			Assert.That((await repository.GetWorkspaceAsync(9, "admin", "test", CancellationToken.None)).SetupPromptDismissed, Is.False);
			Assert.That((await repository.GetHistoryAsync(9, "admin", 0, 20, CancellationToken.None)).Select(h => h.Action), Is.EquivalentTo(new[] { "learn" }));
		}
		[Test]
		public async Task Personal_choices_neither_conflict_with_shared_setup_nor_reset_on_a_catalog_release()
		{
			await using var db = Connect(_connection);
			await db.ExecuteAsync($"INSERT INTO {Q("Departments")} VALUES (719)");
			var repository = Repository(new UnitOfWork(Connections()));
			var admin = new AdminAssistActor(719, "admin"); var other = new AdminAssistActor(719, "other");
			await repository.UpdateWorkspaceAsync(admin, new SetupProgressCommand(0, "mode", Choice: "Review", CatalogVersion: "old"), CancellationToken.None);
			// Another administrator reading Explore holds a stale revision; personal choices must not fail or advance it.
			await repository.UpdateWorkspaceAsync(other, new SetupProgressCommand(0, "learn", "feature", "true", "old"), CancellationToken.None);
			await repository.UpdateWorkspaceAsync(other, new SetupProgressCommand(0, "interest", "feature", "true", "old"), CancellationToken.None);
			await repository.UpdateWorkspaceAsync(other, new SetupProgressCommand(0, "dismiss", Choice: "true", CatalogVersion: "old"), CancellationToken.None);
			Assert.That((await repository.GetWorkspaceAsync(719, admin.UserId, "old", CancellationToken.None)).Revision, Is.EqualTo(1));
			await repository.UpdateWorkspaceAsync(admin, new SetupProgressCommand(1, "mode", Choice: "Fresh", CatalogVersion: "old"), CancellationToken.None);
			Assert.ThrowsAsync<AdminAssistConcurrencyException>(async () => await repository.UpdateWorkspaceAsync(admin,
				new SetupProgressCommand(1, "mode", Choice: "Review", CatalogVersion: "old"), CancellationToken.None), "Shared setup keeps its revision check.");

			// A new catalog release keeps learning, interest and the dismissal instead of resetting them.
			var released = await repository.GetWorkspaceAsync(719, other.UserId, "new", CancellationToken.None);
			Assert.That(released.LearnedCapabilityIds, Is.EquivalentTo(new[] { "feature" }));
			Assert.That(released.InterestedCapabilityIds, Is.EquivalentTo(new[] { "feature" }));
			Assert.That(released.SetupPromptDismissed, Is.True);
			var changed = await repository.UpdateWorkspaceAsync(other, new SetupProgressCommand(0, "interest", "feature", "false", "new"), CancellationToken.None);
			Assert.That(changed.LearnedCapabilityIds, Is.EquivalentTo(new[] { "feature" }), "The other flag carries into the new release.");
			Assert.That(changed.InterestedCapabilityIds, Is.Empty);
			Assert.That(await db.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {Q("AdminAssistLearning")} WHERE {Q("DepartmentId")}=719 AND {Q("UserId")}='other' AND {Q("CapabilityId")}='feature'"), Is.EqualTo(1));
			Assert.That((await repository.GetWorkspaceAsync(719, admin.UserId, "new", CancellationToken.None)).LearnedCapabilityIds, Is.Empty, "Learning stays personal.");
		}
		[Test]
		public async Task Administrative_status_projection_is_tenant_scoped_bounded_and_applies_the_reset_window()
		{
			await using var db = Connect(_connection);
			var now = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
			await db.ExecuteAsync($"INSERT INTO {Q("AspNetUsers")} ({Q("Id")}) VALUES ('status-person'),('status-hidden'),('status-other'); INSERT INTO {Q("DepartmentMembers")} ({Q("DepartmentId")},{Q("UserId")},{Q("IsDeleted")},{Q("IsDisabled")},{Q("IsHidden")}) VALUES (13,'status-person',@False,@False,@False),(13,'status-hidden',@False,@False,@True),(7,'status-other',@False,@False,@False); INSERT INTO {Q("ActionLogs")} VALUES (1,'status-person',13,2,@Old,NULL),(2,'status-person',13,3,@Recent,NULL),(3,'status-person',7,4,@Recent,NULL),(4,'status-hidden',13,2,@Recent,NULL),(5,'status-other',7,2,@Recent,NULL)",
				new { False = false, True = true, Old = DateTime.SpecifyKind(now.AddHours(-2), DateTimeKind.Unspecified), Recent = DateTime.SpecifyKind(now.AddMinutes(-30), DateTimeKind.Unspecified) });
			var unit = new UnitOfWork(Connections());
			var repository = new ActionLogsRepository(Connections(), type == DatabaseTypes.Postgres ? new PostgreSqlConfiguration() : new SqlServerConfiguration(), unit, Mock.Of<IQueryFactory>());
			var current = await repository.ReadLatestForAdministrationAsync(13, false, now, 1, CancellationToken.None);
			Assert.That(current.Single().ActionLogId, Is.EqualTo(2));
			await db.ExecuteAsync($"DELETE FROM {Q("ActionLogs")} WHERE {Q("ActionLogId")}=2");
			Assert.That(await repository.ReadLatestForAdministrationAsync(13, false, now, 1, CancellationToken.None), Is.Empty);
			Assert.That((await repository.ReadLatestForAdministrationAsync(13, true, now, 1, CancellationToken.None)).Single().ActionLogId, Is.EqualTo(1));
			await db.ExecuteAsync($"INSERT INTO {Q("AspNetUsers")} ({Q("Id")}) VALUES ('status-second'); INSERT INTO {Q("DepartmentMembers")} ({Q("DepartmentId")},{Q("UserId")},{Q("IsDeleted")},{Q("IsDisabled")},{Q("IsHidden")}) VALUES (13,'status-second',@False,@False,@False); INSERT INTO {Q("ActionLogs")} VALUES (6,'status-second',13,2,@Recent,NULL)", new { False = false, Recent = DateTime.SpecifyKind(now.AddMinutes(-5), DateTimeKind.Unspecified) });
			Assert.ThrowsAsync<InvalidOperationException>(() => repository.ReadLatestForAdministrationAsync(13, true, now, 1, CancellationToken.None));
		}
		[Test]
		public async Task Profile_references_reject_cross_tenant_expired_and_non_numeric_identifiers()
		{
			await using var db = Connect(_connection);
			await db.ExecuteAsync($"INSERT INTO {Q("DepartmentGroups")} VALUES (7,101),(8,102); INSERT INTO {Q("Documents")} VALUES (7,201,NULL),(8,202,NULL),(7,203,@Expired)", new { Expired = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(-1), DateTimeKind.Unspecified) });
			var unit = new UnitOfWork(Connections()); var repository = Repository(unit);
			await unit.CreateOrGetConnectionAsync();
			try
			{
				var profile = new DepartmentOperatingProfile { SiteGroupReferences = new() { "101" }, StaffingPolicyReferences = new() { "201" } };
				Assert.That(await repository.ValidateOperatingProfileReferencesAsync(7, profile, DateTime.UtcNow, CancellationToken.None), Is.True);
				profile.SiteGroupReferences[0] = "102";
				Assert.That(await repository.ValidateOperatingProfileReferencesAsync(7, profile, DateTime.UtcNow, CancellationToken.None), Is.False);
				profile.SiteGroupReferences[0] = "101";
				foreach (var invalid in new[] { "202", "203", "-1", "policy-name" })
				{
					profile.StaffingPolicyReferences[0] = invalid;
					Assert.That(await repository.ValidateOperatingProfileReferencesAsync(7, profile, DateTime.UtcNow, CancellationToken.None), Is.False);
				}
			}
			finally { unit.DiscardChanges(); }
		}
		[Test]
		public async Task Digest_cursor_rotates_past_a_full_page_even_when_recipients_are_quiet()
		{
			var repository = Repository(new UnitOfWork(Connections()));
			Assert.That(await repository.TryLeaseAsync(12, Guid.NewGuid().ToString("D"), DateTime.UtcNow, CancellationToken.None), Is.True);
			for (var i = 0; i < 55; i++)
				await repository.SavePreferencesAsync(new AdminAssistActor(12, i.ToString("D3")), new AdminAssistPreferencesCommand(0, true, 0, 23), CancellationToken.None);
			var first = await repository.GetDigestPreferencesAsync(12, CancellationToken.None);
			Assert.That(first.Count, Is.EqualTo(50));
			await repository.AdvanceDigestCursorAsync(12, first.Last().UserId, CancellationToken.None);
			var second = await repository.GetDigestPreferencesAsync(12, CancellationToken.None);
			Assert.That(second.Count, Is.EqualTo(5));
			Assert.That(second.Select(p => p.UserId).Intersect(first.Select(p => p.UserId)), Is.Empty);
			await repository.AdvanceDigestCursorAsync(12, second.Last().UserId, CancellationToken.None);
			Assert.That((await repository.GetDigestPreferencesAsync(12, CancellationToken.None)).First().UserId, Is.EqualTo("000"));
		}
		[Test]
		public async Task Digest_claim_is_single_winner_and_revoked_preferences_cannot_be_claimed()
		{
			var repository = Repository(new UnitOfWork(Connections())); var actor = new AdminAssistActor(10, "admin");
			await repository.SavePreferencesAsync(actor, new AdminAssistPreferencesCommand(0, true, 20, 8), CancellationToken.None);
			var preference = await repository.GetPreferencesAsync(10, "admin", CancellationToken.None);
			async Task<bool> Claim() => await Repository(new UnitOfWork(Connections())).ClaimDigestAsync(preference, "2026-09-21", DateTime.UtcNow, CancellationToken.None);
			Assert.That((await Task.WhenAll(Claim(), Claim())).Count(v => v), Is.EqualTo(1));
			await repository.SavePreferencesAsync(actor, new AdminAssistPreferencesCommand(1, false, 20, 8), CancellationToken.None);
			Assert.That(await repository.ClaimDigestAsync(preference, "2026-09-28", DateTime.UtcNow, CancellationToken.None), Is.False);
		}
		[Test]
		public async Task Trace_write_is_idempotent_and_active_hold_prevents_retention()
		{
			var repository = Repository(new UnitOfWork(Connections()));
			var row = new AdminAssistDispatchTraceRow { AdminAssistDispatchTraceId = Guid.NewGuid().ToString("D"), DepartmentId = 11, CallId = 1, AttemptId = Guid.NewGuid().ToString("D"), Stage = "Selected", ResolverVersion = "1", OccurredOn = DateTime.UtcNow.AddYears(-5), Content = "{}" };
			await repository.SaveTraceAsync(row, CancellationToken.None); await repository.SaveTraceAsync(row, CancellationToken.None);
			await using var db = Connect(_connection);
			Assert.That(await db.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {Q("AdminAssistDispatchTraces")}"), Is.EqualTo(1));
			await db.ExecuteAsync($"INSERT INTO {Q("RmsRecordLegalHolds")} ({Q("DepartmentId")}) VALUES (11)");
			Assert.That(await repository.PurgeExpiredMetadataAsync(11, DateTime.UtcNow, CancellationToken.None), Is.Zero);
			await db.ExecuteAsync($"DELETE FROM {Q("RmsRecordLegalHolds")} WHERE {Q("DepartmentId")}=11");
			Assert.That(await repository.PurgeExpiredMetadataAsync(11, DateTime.UtcNow, CancellationToken.None), Is.EqualTo(1));
		}
		[OneTimeTearDown]
		public async Task Remove_only_this_fixture_database()
		{
			_runner?.Dispose(); if (_configured) DataConfig.DatabaseType = _previous;
			if (!_created) return;
			const string prefix = "adminassist_verification_";
			if (!_database.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParseExact(_database.Substring(prefix.Length), "N", out _)) throw new InvalidOperationException("Unexpected disposable database name.");
			if (type == DatabaseTypes.Postgres) NpgsqlConnection.ClearAllPools(); else SqlConnection.ClearAllPools();
			await using var master = Connect(_master);
			await master.ExecuteAsync(type == DatabaseTypes.Postgres ? "DROP DATABASE " + _database + " WITH (FORCE)" : "ALTER DATABASE " + _database + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + _database);
		}
	}
}
