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
CREATE TABLE {Q("FeatureFlags")} ({Q("FlagKey")} {text}(128) PRIMARY KEY,{Q("Name")} {text}(128),{Q("Description")} {text}(512),{Q("Category")} {text}(128),{Q("IsEnabledGlobally")} {boolean});
CREATE TABLE {Q("RmsRecordLegalHolds")} ({Q("DepartmentId")} int,{Q("ReleasedOn")} {date},{Q("RmsRecordLegalHoldId")} {text}(128),{Q("RecordId")} {text}(128),{Q("DefinitionKey")} {text}(128));
CREATE TABLE {Q("DepartmentGroups")} ({Q("DepartmentId")} int,{Q("DepartmentGroupId")} int);
CREATE TABLE {Q("Documents")} ({Q("DepartmentId")} int,{Q("DocumentId")} int,{Q("RemoveOn")} {date});
CREATE TABLE {Q("DepartmentMembers")} ({Q("DepartmentMemberId")} int,{Q("DepartmentId")} int,{Q("UserId")} {text}(128),{Q("IsDeleted")} {boolean},{Q("IsDisabled")} {boolean},{Q("IsHidden")} {boolean},{Q("PasswordLastSetOn")} {date});
CREATE TABLE {Q("AspNetUsers")} ({Q("Id")} {text}(128) PRIMARY KEY,{Q("TwoFactorEnabled")} {boolean},{Q("AuthenticationGeneration")} bigint);
CREATE TABLE {Q("ActionLogs")} ({Q("ActionLogId")} int PRIMARY KEY,{Q("UserId")} {text}(128),{Q("DepartmentId")} int,{Q("ActionTypeId")} int,{Q("Timestamp")} {date},{Q("GeoLocationData")} {text}(128));
INSERT INTO {Q("Departments")} VALUES (7),(8),(9),(10),(11),(12),(13);");
			}
			var source = new Mock<IMigrationSource>(); source.Setup(s => s.GetMigrations()).Returns(new IMigration[] { type == DatabaseTypes.Postgres ? new M0235_AddAdminAssistFoundationPg() : new M0235_AddAdminAssistFoundation() });
			_runner = new ServiceCollection().AddFluentMigratorCore().ConfigureRunner(r => { if (type == DatabaseTypes.Postgres) r.AddPostgres(); else r.AddSqlServer(); r.WithGlobalConnectionString(_connection); }).AddSingleton(source.Object).BuildServiceProvider();
			_runner.GetRequiredService<IMigrationRunner>().MigrateUp();
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
