using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using FluentMigrator.Runner;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model.Repositories.Connection;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Providers.Migrations.Migrations;
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Servers.SqlServer;
using Resgrid.Repositories.DataRepository.Transactions;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Runs the real SQL Server department deletion against the full migration chain. Every table with a
	/// foreign-key path to Departments or AspNetUsers gets one generated row per department, so a table the
	/// delete script never clears (or clears after its parent) fails here instead of in the scheduled worker
	/// (RESGRID-WEBJOBS-76: FK_Workshifts_Department, FK_ContactCategories_Department).
	/// </summary>
	[TestFixture, NonParallelizable]
	public class DepartmentDeletionDatabaseTests
	{
		private const string DatabasePrefix = "department_deletion_verification_";

		// Tables the generic seeder cannot populate because a CHECK constraint needs two distinct parent rows
		// of the same table (CK_InventoryTransfers_Locations). Their deletion is covered by InventoryDatabaseTests.
		private static readonly HashSet<string> UnseedableTables = new(StringComparer.OrdinalIgnoreCase)
		{
			"InventoryTransfers", "InventoryTransferItems"
		};

		// Values for module CHECK rules the generic values break. DBNull forces an optional key null so the
		// sibling holder key (the user, which exercises the member-account delete order) is the one populated.
		private static readonly Dictionary<string, object> ColumnOverrides = new(StringComparer.OrdinalIgnoreCase)
		{
			["InventoryLocations.LocationType"] = 3,
			["InventoryLocations.GroupId"] = DBNull.Value,
			["InventoryLocations.UnitId"] = DBNull.Value,
			["InventoryLocations.ContainerAssetId"] = DBNull.Value,
			["InventoryIssuances.IssuedToUnitId"] = DBNull.Value,
			["RecordInventoryUsages.SourceType"] = 0,
			// An active Records legal hold defers department deletion; the generic hold row must be a released one.
			["RmsRecordLegalHolds.ReleasedOn"] = new DateTime(2026, 1, 2),
			// CK_RmsRecordValues_OneColumnGroup: exactly one value column set on an unprotected value.
			["RmsRecordValues.TextValue"] = "seed",
			// A live ADP migration lock defers department deletion; the generic lock row must be a released one.
			["DepartmentOperationLocks.ReleasedUtc"] = new DateTime(2026, 1, 2),
		};

		// Tables with a DepartmentId column but no foreign-key path to Departments. Nothing forces the delete script to
		// clear them, so they are orphaned silently (a leftover RMS cutover kept deleted department 13275 in every Records
		// sweep, 2026-10-11). The 97 gaps recorded when this guard was added (2026-10-10) were all resolved on 2026-10-11:
		// purged by the delete script, or moved to RetainedTables with a reason. Nothing may be added here without a
		// tracked follow-up.
		private static readonly HashSet<string> KnownOrphanedTables = new(StringComparer.OrdinalIgnoreCase);

		// Department rows that must outlive the department, each by design (docs/architecture/department-deletion-orphaned-tables-2026-10-11.md).
		private static readonly Dictionary<string, string> RetainedTables = new(StringComparer.OrdinalIgnoreCase)
		{
			// Erasure markers: no content. The search sweeps erase the deleted department's Lucene documents, then remove them.
			["RmsSearchIndexStates"] = "records index erasure marker (RecordsSearchIndexMaintenanceService)",
			["SearchIndexStates"] = "global index erasure marker (SearchIndexMaintenanceService)",
			// Platform audit trails, retained by decision.
			["SystemAudits"] = "platform security audit log",
			["AdpAuditEvents"] = "value-free, hash-chained ADP audit; deleting rows breaks the chain",
			["ProtectedWorkflowDisclosures"] = "value-free, hash-chained ledger of protected data disclosures",
			// User security records. The delete revokes live sessions; the CleanOIDC worker purges all of these on retention.
			["UserSessions"] = "sign-in access record, revoked by the delete and purged on RevokedSessionRetentionDays",
			["UserSessionMfaEvidence"] = "purged when it expires",
			["UserMfaActivity"] = "user MFA history, purged on MfaActivityRetentionDays",
			["MfaLoginTransactions"] = "transient, purged an hour after it expires",
			["SsoLoginTransactions"] = "transient, purged an hour after it expires",
			["FactorRecoveryTransactions"] = "transient, purged an hour after it expires",
			["AuthenticationChallenges"] = "transient, purged an hour after it expires",
			["MfaApprovalRequests"] = "kept a day for the user's approval rate limit, then purged"
		};

		private string _master, _connection, _database;
		private bool _created, _configured;
		private DatabaseTypes _previousType;
		private string _previousConnection;
		private int _counter;

		private List<ColumnInfo> _columns;
		private Dictionary<string, List<ColumnInfo>> _columnsByTable;
		private Dictionary<string, List<string>> _primaryKeys;
		private Dictionary<string, List<ForeignKeyInfo>> _foreignKeysByChild;
		private HashSet<string> _triggeredTables;
		private HashSet<string> _scope, _requiredScope;
		private readonly Dictionary<string, Dictionary<string, object>> _shared = new(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<string> _sharedInProgress = new(StringComparer.OrdinalIgnoreCase);

		[OneTimeSetUp]
		public async Task CreateAFullyMigratedDisposableDatabase()
		{
			var configured = Environment.GetEnvironmentVariable("RESGRID_CHECKLIST_SQLSERVER_TEST_CONNECTION");
			if (string.IsNullOrWhiteSpace(configured)) Assert.Ignore("Set RESGRID_CHECKLIST_SQLSERVER_TEST_CONNECTION to enable the disposable department deletion database test.");
			// Refuse application database names; the connection is used only to create a unique disposable database.
			var builder = new SqlConnectionStringBuilder(configured);
			if (!string.IsNullOrEmpty(builder.InitialCatalog) && !string.Equals(builder.InitialCatalog, "master", StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException("The department deletion test requires a SQL Server master connection.");
			builder.InitialCatalog = "master"; _master = builder.ConnectionString;
			_database = DatabasePrefix + Guid.NewGuid().ToString("N");
			await using (var master = new SqlConnection(_master)) { await master.ExecuteAsync("CREATE DATABASE " + _database); _created = true; }
			_connection = new SqlConnectionStringBuilder(_master) { InitialCatalog = _database }.ConnectionString;

			using (var runner = new ServiceCollection().AddFluentMigratorCore()
				.ConfigureRunner(r => r.AddSqlServer().WithGlobalConnectionString(_connection).ScanIn(typeof(M0001_InitialMigration).Assembly).For.All())
				.BuildServiceProvider(false))
			using (var scope = runner.CreateScope())
				scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();

			_previousType = DataConfig.DatabaseType; _previousConnection = DataConfig.CoreConnectionString; _configured = true;
			DataConfig.DatabaseType = DatabaseTypes.SqlServer; DataConfig.CoreConnectionString = _connection;
		}

		[OneTimeTearDown]
		public async Task RemoveOnlyThisFixturesDatabase()
		{
			if (_configured) { DataConfig.DatabaseType = _previousType; DataConfig.CoreConnectionString = _previousConnection; }
			if (!_created) return;
			if (!_database.StartsWith(DatabasePrefix, StringComparison.Ordinal) || !Guid.TryParseExact(_database.Substring(DatabasePrefix.Length), "N", out _))
				throw new InvalidOperationException("Unexpected department deletion fixture database name.");
			SqlConnection.ClearAllPools();
			await using var master = new SqlConnection(_master);
			await master.ExecuteAsync("ALTER DATABASE " + _database + " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + _database);
		}

		[Test]
		public async Task DeletingADepartmentClearsEveryForeignKeyedRowAndLeavesOtherDepartmentsIntact()
		{
			await using var db = new SqlConnection(_connection);
			await db.OpenAsync();
			await LoadSchemaAsync(db);

			var failures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			var deleted = await SeedDepartmentAsync(db, failures);
			var kept = await SeedDepartmentAsync(db, failures);

			string Reason(string t) => t + ": " + (failures.TryGetValue(t, out var f) ? f : "no attempt (parent not seeded)");
			foreach (var optional in _scope.Except(_requiredScope).Where(t => !deleted.Linked.Contains(t)).OrderBy(t => t))
				TestContext.Out.WriteLine("Not verified (reaches the department only through optional keys): " + Reason(optional));
			var unseeded = _requiredScope.Where(t => !deleted.Linked.Contains(t)).Except(UnseedableTables, StringComparer.OrdinalIgnoreCase).OrderBy(t => t).ToList();
			unseeded.Should().BeEmpty("every table with a required foreign-key path to Departments or AspNetUsers must be seeded, or listed in UnseedableTables with a reason; failures:\n"
				+ string.Join("\n", unseeded.Select(Reason)));

			var result = await new DeleteRepository().DeleteDepartmentAndUsersAsync(deleted.DepartmentId);
			result.Should().BeTrue();

			var remaining = new List<string>();
			var lost = new List<string>();
			foreach (var (table, row) in deleted.AllRows())
				if (await RowExistsAsync(db, table, row)) remaining.Add(table);
			foreach (var (table, row) in kept.AllRows())
				if (!await RowExistsAsync(db, table, row)) lost.Add(table);

			TestContext.Out.WriteLine($"Verified {deleted.AllRows().Count()} deleted and {kept.AllRows().Count()} retained rows across {deleted.Linked.Count} tables.");
			remaining.Should().BeEmpty("the deleted department's rows must all be removed");
			lost.Should().BeEmpty("another department's rows must survive the deletion");
		}

		[Test]
		public async Task DeletingADepartmentClearsEveryTableWithADepartmentIdColumnEvenWithoutAForeignKey()
		{
			await using var db = new SqlConnection(_connection);
			await db.OpenAsync();
			await LoadSchemaAsync(db);

			var unlinked = DepartmentTablesWithoutForeignKey();
			// Seed them, and the tables hanging off them, as department rows in dependency order. A child must reference its
			// department's own parent row: one shared parent makes the second insert hit the parent's unique indexes.
			_scope.UnionWith(WithTablesHangingOff(unlinked));

			var failures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			var deleted = await SeedDepartmentAsync(db, failures);
			var kept = await SeedDepartmentAsync(db, failures);

			var seededTables = unlinked.Where(t => deleted.Rows.ContainsKey(t)).ToList();
			var keptTables = unlinked.Where(t => kept.Rows.ContainsKey(t)).ToList();
			var unseeded = unlinked.Where(t => !deleted.Rows.ContainsKey(t) && !UnseedableTables.Contains(t))
				.Select(t => t + ": " + (failures.TryGetValue(t, out var f) ? f : "no attempt")).ToList();
			TestContext.Out.WriteLine($"Seeded {seededTables.Count} of {unlinked.Count} department tables without a foreign key to Departments ({keptTables.Count} for both departments).");
			unseeded.Should().BeEmpty("every DepartmentId table must be seedable, or listed in UnseedableTables with a reason");

			(await new DeleteRepository().DeleteDepartmentAndUsersAsync(deleted.DepartmentId)).Should().BeTrue();

			async Task<int> Count(string table, int departmentId) =>
				await db.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM [dbo].[{table}] WHERE [DepartmentId] = @DepartmentId", new { DepartmentId = departmentId });
			var remaining = new List<string>();
			var lost = new List<string>();
			foreach (var table in seededTables)
				if (await Count(table, deleted.DepartmentId) > 0) remaining.Add(table);
			foreach (var table in keptTables)
				if (await Count(table, kept.DepartmentId) == 0) lost.Add(table);
			TestContext.Out.WriteLine("Left behind by the delete script: " + string.Join(", ", remaining));

			remaining.Except(KnownOrphanedTables, StringComparer.OrdinalIgnoreCase).Except(RetainedTables.Keys, StringComparer.OrdinalIgnoreCase).Should()
				.BeEmpty("the delete script must clear every DepartmentId table; add it to the purge (or, only with a reason, to RetainedTables)");
			KnownOrphanedTables.Where(t => seededTables.Contains(t, StringComparer.OrdinalIgnoreCase) && !remaining.Contains(t, StringComparer.OrdinalIgnoreCase)).Should()
				.BeEmpty("a known gap is now cleared; remove it from KnownOrphanedTables");
			RetainedTables.Keys.Where(t => seededTables.Contains(t, StringComparer.OrdinalIgnoreCase) && !remaining.Contains(t, StringComparer.OrdinalIgnoreCase)).Should()
				.BeEmpty("a retained table keeps the deleted department's rows by decision; purging it needs that decision revisited");
			lost.Should().BeEmpty("another department's rows must survive the deletion");
		}

		[Test]
		public async Task ALiveProtectedDataMigrationLockDefersTheDeletionUntilItIsReleased()
		{
			await using var db = new SqlConnection(_connection);
			await db.OpenAsync();
			await LoadSchemaAsync(db);
			var department = await SeedBareDepartmentAsync(db);
			var lockId = (await InsertOrThrowAsync(db, "DepartmentOperationLocks", department, new Dictionary<string, object>
			{
				["LockType"] = (int)Resgrid.Model.DepartmentOperationLockType.AdpMigration,
				["ReleasedUtc"] = DBNull.Value,
				["ExpiresUtc"] = DateTime.UtcNow.AddHours(1)
			}))["DepartmentOperationLockId"];
			await InsertOrThrowAsync(db, "DepartmentDataProtectionKeys", department, null);

			Func<Task> held = () => new DeleteRepository().DeleteDepartmentAndUsersAsync(department.DepartmentId);
			await held.Should().ThrowAsync<Resgrid.Model.DepartmentDeletionDeferredException>().WithMessage(DataProtectionDepartmentCleanup.MigrationLockMessage);
			(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Departments WHERE DepartmentId = @DepartmentId", new { department.DepartmentId })).Should().Be(1, "a deferred deletion rolls back");

			await db.ExecuteAsync("UPDATE DepartmentOperationLocks SET ReleasedUtc = SYSUTCDATETIME() WHERE DepartmentOperationLockId = @lockId", new { lockId });
			(await new DeleteRepository().DeleteDepartmentAndUsersAsync(department.DepartmentId)).Should().BeTrue();
			foreach (var table in new[] { "Departments", "DepartmentOperationLocks", "DepartmentDataProtectionKeys" })
				(await db.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM [{table}] WHERE DepartmentId = @DepartmentId", new { department.DepartmentId })).Should().Be(0, table);
		}

		[Test]
		public async Task DeletingADepartmentRevokesItsLiveSessionsAndLeavesThemToTheRetentionPurge()
		{
			await using var db = new SqlConnection(_connection);
			await db.OpenAsync();
			await LoadSchemaAsync(db);
			var deleted = await SeedBareDepartmentAsync(db);
			var kept = await SeedBareDepartmentAsync(db);
			var live = new Dictionary<string, object> { ["State"] = (int)Resgrid.Model.UserSessionState.Active, ["RevokedOn"] = DBNull.Value, ["RevocationReason"] = DBNull.Value };
			var deletedSession = (await InsertOrThrowAsync(db, "UserSessions", deleted, live))["UserSessionId"];
			var keptSession = (await InsertOrThrowAsync(db, "UserSessions", kept, live))["UserSessionId"];

			(await new DeleteRepository().DeleteDepartmentAndUsersAsync(deleted.DepartmentId)).Should().BeTrue();

			var revoked = await db.QuerySingleAsync<(int State, int? RevocationReason, DateTime? RevokedOn)>(
				"SELECT [State], RevocationReason, RevokedOn FROM UserSessions WHERE UserSessionId = @Id", new { Id = deletedSession });
			revoked.State.Should().Be((int)Resgrid.Model.UserSessionState.Revoked);
			revoked.RevocationReason.Should().Be((int)Resgrid.Model.UserSessionRevocationReason.DepartmentDeleted);
			revoked.RevokedOn.Should().NotBeNull("the revocation starts the retention clock");
			(await db.ExecuteScalarAsync<int>("SELECT [State] FROM UserSessions WHERE UserSessionId = @Id", new { Id = keptSession })).Should().Be((int)Resgrid.Model.UserSessionState.Active);
		}

		[Test]
		public async Task TheGlobalSearchMarkerOutlivesTheDepartmentAndIsDroppedOnlyOnceItIsDeleted()
		{
			await using var db = new SqlConnection(_connection);
			await db.OpenAsync();
			await LoadSchemaAsync(db);
			var deleted = await SeedBareDepartmentAsync(db);
			var kept = await SeedBareDepartmentAsync(db);
			var marker = new Dictionary<string, object> { ["IndexName"] = Resgrid.Model.Search.SearchIndexNames.Global };
			foreach (var department in new[] { deleted, kept })
			{
				await InsertOrThrowAsync(db, "SearchIndexStates", department, marker);
				await InsertOrThrowAsync(db, "SearchProjections", department, null);
			}
			Task<int> Count(string table, SeededDepartment department) =>
				db.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM [{table}] WHERE DepartmentId = @DepartmentId", new { department.DepartmentId });

			(await new DeleteRepository().DeleteDepartmentAndUsersAsync(deleted.DepartmentId)).Should().BeTrue();
			(await Count("SearchProjections", deleted)).Should().Be(0);
			(await Count("SearchIndexStates", deleted)).Should().Be(1, "the global index sweep still needs the marker");

			// A department deleted before its delete script purged projections still has them; they go with the marker.
			await InsertOrThrowAsync(db, "SearchProjections", deleted, null);
			var provider = new Mock<IConnectionProvider>();
			provider.Setup(p => p.Create()).Returns(() => new SqlConnection(_connection));
			using var unit = new UnitOfWork(provider.Object);
			var states = new SearchIndexStatesRepository(provider.Object, new SqlServerConfiguration(), unit, Mock.Of<IQueryFactory>());

			(await states.GetDeletedDepartmentIdsAsync()).Should().Contain(deleted.DepartmentId).And.NotContain(kept.DepartmentId);
			(await states.DeleteForDeletedDepartmentAsync(kept.DepartmentId)).Should().Be(0, "a live department's marker is never dropped");
			(await states.DeleteForDeletedDepartmentAsync(deleted.DepartmentId)).Should().Be(1);
			(await Count("SearchProjections", deleted)).Should().Be(0);
			(await states.GetDeletedDepartmentIdsAsync()).Should().NotContain(deleted.DepartmentId);
			(await Count("SearchIndexStates", kept)).Should().Be(1);
			(await Count("SearchProjections", kept)).Should().Be(1);
		}

		[Test]
		public async Task TheSweepPurgesWhatAnEarlierDeletionLeftInEveryTableWithoutAForeignKeyUnlessAHoldKeepsIt()
		{
			await using var db = new SqlConnection(_connection);
			await db.OpenAsync();
			await LoadSchemaAsync(db);

			var unlinked = DepartmentTablesWithoutForeignKey();
			// RMS keeps its own sweep (worker 43). AuditLogs is cleared by the delete, but DeleteService then writes the
			// deletion-executed row as the durable trail of the deletion, so a deleted department's AuditLogs are never swept.
			bool Kept(string table) => table.StartsWith("Rms", StringComparison.OrdinalIgnoreCase) || RetainedTables.ContainsKey(table)
				|| string.Equals(table, "AuditLogs", StringComparison.OrdinalIgnoreCase);
			unlinked.Where(t => !Kept(t)).Except(DeletedDepartmentCleanup.Tables, StringComparer.OrdinalIgnoreCase).Should()
				.BeEmpty("the sweep must cover every table the delete purges without a foreign key");
			DeletedDepartmentCleanup.Tables.Where(Kept).Should().BeEmpty("a retained, RMS or audit trail table is never swept");

			// Seed only the FK-less tables, and what hangs off them, for two departments. Then delete one the way the script did
			// before those tables were purged: its Departments row goes and every FK-less row stays.
			_scope = WithTablesHangingOff(unlinked);
			_scope.UnionWith(new[] { "Departments", "AspNetUsers" });
			var failures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			var deleted = await SeedDepartmentAsync(db, failures);
			var kept = await SeedDepartmentAsync(db, failures);
			unlinked.Where(t => !deleted.Rows.ContainsKey(t) && !UnseedableTables.Contains(t)).Select(t => t + ": " + (failures.TryGetValue(t, out var f) ? f : "no attempt"))
				.Should().BeEmpty("every DepartmentId table must be seedable");
			await db.ExecuteAsync("DELETE FROM DepartmentMembers WHERE DepartmentId = @DepartmentId; DELETE FROM Departments WHERE DepartmentId = @DepartmentId", new { deleted.DepartmentId });
			var executedTrail = (await InsertOrThrowAsync(db, "AuditLogs", deleted, new Dictionary<string, object> { ["LogType"] = (int)Resgrid.Model.AuditLogTypes.DeleteDepartmentRequestExecuted }))["AuditLogId"];

			var repository = new DeleteRepository();
			Task<int> Count(string table, int departmentId) =>
				db.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM [dbo].[{table}] WHERE [DepartmentId] = @DepartmentId", new { DepartmentId = departmentId });
			(await repository.GetDeletedDepartmentIdsWithLeftoversAsync()).Should().Contain(deleted.DepartmentId).And.NotContain(kept.DepartmentId);

			// An active Records legal hold, or a running ADP migration, keeps everything for a later pass.
			await db.ExecuteAsync("UPDATE RmsRecordLegalHolds SET ReleasedOn = NULL WHERE DepartmentId = @DepartmentId", new { deleted.DepartmentId });
			(await repository.PurgeDeletedDepartmentLeftoversAsync(deleted.DepartmentId)).Should().Be(Resgrid.Model.DeletedDepartmentPurgeOutcome.LegalHold);
			await db.ExecuteAsync("UPDATE RmsRecordLegalHolds SET ReleasedOn = SYSUTCDATETIME() WHERE DepartmentId = @DepartmentId", new { deleted.DepartmentId });
			await db.ExecuteAsync("UPDATE DepartmentOperationLocks SET ReleasedUtc = NULL, ExpiresUtc = DATEADD(HOUR, 1, SYSUTCDATETIME()) WHERE DepartmentId = @DepartmentId", new { deleted.DepartmentId });
			(await repository.PurgeDeletedDepartmentLeftoversAsync(deleted.DepartmentId)).Should().Be(Resgrid.Model.DeletedDepartmentPurgeOutcome.ProtectedDataMigration);
			(await Count("DepartmentDataProtectionKeys", deleted.DepartmentId)).Should().Be(1, "a held pass deletes nothing");
			await db.ExecuteAsync("UPDATE DepartmentOperationLocks SET ReleasedUtc = SYSUTCDATETIME() WHERE DepartmentId = @DepartmentId", new { deleted.DepartmentId });

			(await repository.PurgeDeletedDepartmentLeftoversAsync(deleted.DepartmentId)).Should().Be(Resgrid.Model.DeletedDepartmentPurgeOutcome.Purged);

			var remaining = new List<string>();
			foreach (var table in unlinked.Where(t => deleted.Rows.ContainsKey(t)))
				if (await Count(table, deleted.DepartmentId) > 0) remaining.Add(table);
			TestContext.Out.WriteLine("Left after the sweep: " + string.Join(", ", remaining.Where(t => !t.StartsWith("Rms", StringComparison.OrdinalIgnoreCase))) + " and the RMS tables");
			remaining.Where(t => !Kept(t)).Should().BeEmpty("the sweep must clear every FK-less table the delete purges");
			RetainedTables.Keys.Where(t => deleted.Rows.ContainsKey(t)).Except(remaining, StringComparer.OrdinalIgnoreCase).Should().BeEmpty("retained rows outlive the sweep too");
			(await Count("RmsRecordLegalHolds", deleted.DepartmentId)).Should().Be(1, "RMS rows are worker 43's to purge");
			(await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE AuditLogId = @executedTrail", new { executedTrail })).Should().Be(1, "the deletion-executed audit row is the durable trail of the deletion");
			(await repository.GetDeletedDepartmentIdsWithLeftoversAsync()).Should().NotContain(deleted.DepartmentId);

			var lost = new List<string>();
			foreach (var table in unlinked.Where(t => kept.Rows.ContainsKey(t)))
				if (await Count(table, kept.DepartmentId) == 0) lost.Add(table);
			lost.Should().BeEmpty("another department's rows must survive the sweep");
			Func<Task> live = () => repository.PurgeDeletedDepartmentLeftoversAsync(kept.DepartmentId);
			await live.Should().ThrowAsync<InvalidOperationException>("a department that still exists is never swept");
		}

		/// <summary>Tables with an int DepartmentId column but no foreign-key path to Departments or AspNetUsers.</summary>
		private List<string> DepartmentTablesWithoutForeignKey() => _columnsByTable
			.Where(t => !_scope.Contains(t.Key) && t.Value.Any(c => string.Equals(c.Name, "DepartmentId", StringComparison.OrdinalIgnoreCase) && c.Type == "int"))
			.Select(t => t.Key).OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();

		/// <summary>The tables plus every table outside the foreign-key scope that hangs off them.</summary>
		private HashSet<string> WithTablesHangingOff(IEnumerable<string> tables)
		{
			var extended = new HashSet<string>(tables, StringComparer.OrdinalIgnoreCase);
			for (var added = true; added;)
			{
				added = false;
				foreach (var fk in _foreignKeysByChild.Values.SelectMany(f => f))
					if (extended.Contains(fk.Parent) && !_scope.Contains(fk.Child) && extended.Add(fk.Child)) added = true;
			}
			return extended;
		}

		/// <summary>A department with only its managing member: enough for the delete to run end to end.</summary>
		private async Task<SeededDepartment> SeedBareDepartmentAsync(SqlConnection db)
		{
			var seeded = new SeededDepartment
			{
				ManagingUserId = "deletion-manager-" + Guid.NewGuid().ToString("N"),
				MemberUserId = "deletion-member-" + Guid.NewGuid().ToString("N")
			};
			seeded.Rows["AspNetUsers"] = await InsertOrThrowAsync(db, "AspNetUsers", seeded, new Dictionary<string, object> { ["Id"] = seeded.ManagingUserId });
			seeded.Rows["Departments"] = await InsertOrThrowAsync(db, "Departments", seeded, new Dictionary<string, object> { ["ManagingUserId"] = seeded.ManagingUserId });
			seeded.DepartmentId = Convert.ToInt32(seeded.Rows["Departments"]["DepartmentId"]);
			await InsertOrThrowAsync(db, "DepartmentMembers", seeded, new Dictionary<string, object> { ["UserId"] = seeded.ManagingUserId });
			return seeded;
		}

		private sealed class ColumnInfo
		{
			public string Table { get; set; }
			public string Name { get; set; }
			public string Type { get; set; }
			public short MaxLength { get; set; }
			public bool Nullable { get; set; }
			public bool Identity { get; set; }
			public bool Computed { get; set; }
			public bool HasDefault { get; set; }
			public int Ordinal { get; set; }
		}

		private sealed class ForeignKeyColumn
		{
			public string Name { get; set; }
			public string Child { get; set; }
			public string ChildColumn { get; set; }
			public string Parent { get; set; }
			public string ParentColumn { get; set; }
			public int Ordinal { get; set; }
		}

		private sealed class ForeignKeyInfo
		{
			public string Name;
			public string Child;
			public string Parent;
			public List<(string Child, string Parent)> Columns;
			public bool IsTrivialSelfReference => Child == Parent && Columns.All(c => string.Equals(c.Child, c.Parent, StringComparison.OrdinalIgnoreCase));
		}

		private sealed class SeededDepartment
		{
			public int DepartmentId;
			public string ManagingUserId;
			public string MemberUserId;
			public readonly Dictionary<string, Dictionary<string, object>> Rows = new(StringComparer.OrdinalIgnoreCase);
			public readonly HashSet<string> Linked = new(StringComparer.OrdinalIgnoreCase) { "AspNetUsers", "Departments" };
			public readonly List<(string Table, Dictionary<string, object> Row)> Extra = new();

			public IEnumerable<(string Table, Dictionary<string, object> Row)> AllRows() =>
				Rows.Where(r => Linked.Contains(r.Key)).Select(r => (r.Key, r.Value)).Concat(Extra);
		}

		private async Task LoadSchemaAsync(SqlConnection db)
		{
			_columns = (await db.QueryAsync<ColumnInfo>(@"
				SELECT t.name AS [Table], c.name AS Name, ty.name AS Type, c.max_length AS MaxLength, c.is_nullable AS Nullable,
					c.is_identity AS [Identity], c.is_computed AS Computed, CAST(CASE WHEN c.default_object_id <> 0 THEN 1 ELSE 0 END AS bit) AS HasDefault, c.column_id AS Ordinal
				FROM sys.tables t
				JOIN sys.columns c ON c.object_id = t.object_id
				JOIN sys.types ty ON ty.user_type_id = c.user_type_id
				WHERE t.is_ms_shipped = 0 AND SCHEMA_NAME(t.schema_id) = 'dbo'")).ToList();
			_columnsByTable = _columns.GroupBy(c => c.Table, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.OrderBy(c => c.Ordinal).ToList(), StringComparer.OrdinalIgnoreCase);

			_primaryKeys = (await db.QueryAsync<(string Table, string Column)>(@"
				SELECT t.name, c.name
				FROM sys.indexes i
				JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
				JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
				JOIN sys.tables t ON t.object_id = i.object_id
				WHERE i.is_primary_key = 1 AND SCHEMA_NAME(t.schema_id) = 'dbo'"))
				.GroupBy(k => k.Table, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.Select(k => k.Column).ToList(), StringComparer.OrdinalIgnoreCase);

			_foreignKeysByChild = (await db.QueryAsync<ForeignKeyColumn>(@"
				SELECT fk.name AS Name, OBJECT_NAME(fk.parent_object_id) AS Child, c.name AS ChildColumn,
					OBJECT_NAME(fk.referenced_object_id) AS Parent, rc.name AS ParentColumn, fkc.constraint_column_id AS Ordinal
				FROM sys.foreign_keys fk
				JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
				JOIN sys.columns c ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id
				JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
				WHERE SCHEMA_NAME(fk.schema_id) = 'dbo'"))
				.GroupBy(k => k.Name)
				.Select(g => new ForeignKeyInfo
				{
					Name = g.Key, Child = g.First().Child, Parent = g.First().Parent,
					Columns = g.OrderBy(k => k.Ordinal).Select(k => (k.ChildColumn, k.ParentColumn)).ToList()
				})
				.GroupBy(k => k.Child, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

			_triggeredTables = new HashSet<string>(await db.QueryAsync<string>(
				"SELECT OBJECT_NAME(parent_id) FROM sys.triggers WHERE parent_class = 1 AND is_disabled = 0"), StringComparer.OrdinalIgnoreCase);

			// Scope: Departments, AspNetUsers and every table with a foreign-key path to either. Tables whose path
			// runs only through required keys always hold department rows and must all be seeded and verified.
			_scope = Closure(_ => true);
			_requiredScope = Closure(IsRequired);
		}

		private HashSet<string> Closure(Func<ForeignKeyInfo, bool> follow)
		{
			var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Departments", "AspNetUsers" };
			var added = true;
			while (added)
			{
				added = false;
				foreach (var fk in _foreignKeysByChild.Values.SelectMany(f => f))
					if (tables.Contains(fk.Parent) && follow(fk) && tables.Add(fk.Child)) added = true;
			}
			return tables;
		}

		private List<ForeignKeyInfo> ForeignKeys(string table) => _foreignKeysByChild.TryGetValue(table, out var fks) ? fks : new List<ForeignKeyInfo>();

		private ColumnInfo Column(string table, string column) => _columnsByTable[table].First(c => string.Equals(c.Name, column, StringComparison.OrdinalIgnoreCase));

		private bool IsRequired(ForeignKeyInfo fk) => fk.Columns.All(c => !Column(fk.Child, c.Child).Nullable);

		private async Task<SeededDepartment> SeedDepartmentAsync(SqlConnection db, Dictionary<string, string> failures)
		{
			var seeded = new SeededDepartment
			{
				ManagingUserId = "deletion-manager-" + Guid.NewGuid().ToString("N"),
				MemberUserId = "deletion-member-" + Guid.NewGuid().ToString("N")
			};

			// The member row is the parent every user foreign key points at: the per-member cursor deletes that
			// account before the department-level deletes run, which is the ordering the script must survive.
			seeded.Rows["AspNetUsers"] = await InsertOrThrowAsync(db, "AspNetUsers", seeded, new Dictionary<string, object> { ["Id"] = seeded.MemberUserId });
			seeded.Extra.Add(("AspNetUsers", await InsertOrThrowAsync(db, "AspNetUsers", seeded, new Dictionary<string, object> { ["Id"] = seeded.ManagingUserId })));
			seeded.Rows["Departments"] = await InsertOrThrowAsync(db, "Departments", seeded, new Dictionary<string, object> { ["ManagingUserId"] = seeded.ManagingUserId });
			seeded.DepartmentId = Convert.ToInt32(seeded.Rows["Departments"]["DepartmentId"]);

			foreach (var table in SeedOrder())
			{
				if (seeded.Rows.ContainsKey(table)) continue;
				var overrides = table == "DepartmentMembers" ? new Dictionary<string, object> { ["UserId"] = seeded.MemberUserId } : null;
				var (row, error) = await TryInsertAsync(db, table, seeded, overrides);
				if (row == null) { failures[table] = error; continue; }
				seeded.Rows[table] = row;
				if (IsLinked(table, row, seeded)) { seeded.Linked.Add(table); failures.Remove(table); }
				else failures[table] = "seeded only with its optional department keys left null";
			}

			// The managing member's own membership row, deleted only after the Departments row.
			if (!seeded.Linked.Contains("DepartmentMembers"))
				seeded.Extra.Add(("DepartmentMembers", await InsertOrThrowAsync(db, "DepartmentMembers", seeded, new Dictionary<string, object> { ["UserId"] = seeded.MemberUserId })));
			seeded.Extra.Add(("DepartmentMembers", await InsertOrThrowAsync(db, "DepartmentMembers", seeded, new Dictionary<string, object> { ["UserId"] = seeded.ManagingUserId })));

			await BackfillOptionalKeysAsync(db, seeded);
			var changed = true;
			while (changed)
			{
				changed = false;
				foreach (var (table, row) in seeded.Rows)
					if (!seeded.Linked.Contains(table) && IsLinked(table, row, seeded)) { seeded.Linked.Add(table); failures.Remove(table); changed = true; }
			}

			return seeded;
		}

		/// <summary>Seeding in dependency order leaves optional keys to later tables null (cycles such as work orders
		/// and their recurrence versions); fill them now so the delete has to break those cycles too.</summary>
		private async Task BackfillOptionalKeysAsync(SqlConnection db, SeededDepartment seeded)
		{
			foreach (var (table, row) in seeded.Rows.ToList())
			{
				if (!_primaryKeys.TryGetValue(table, out var keys)) continue;
				foreach (var fk in ForeignKeys(table).Where(f => f.Parent != table && !IsRequired(f)))
				{
					if (fk.Columns.All(c => row.TryGetValue(c.Child, out var v) && v != null && v != DBNull.Value)) continue;
					if (fk.Columns.Any(c => ColumnOverrides.ContainsKey(table + "." + c.Child))) continue;
					if (!seeded.Rows.TryGetValue(fk.Parent, out var parent)) continue;

					var parameters = new DynamicParameters();
					var sets = fk.Columns.Select((c, i) => { parameters.Add("v" + i, parent[c.Parent]); return $"[{c.Child}] = @v{i}"; }).ToList();
					var where = keys.Select(k => { parameters.Add("k_" + k, row[k]); return $"[{k}] = @k_{k}"; }).ToList();
					try
					{
						await db.ExecuteAsync($"UPDATE [dbo].[{table}] SET {string.Join(", ", sets)} WHERE {string.Join(" AND ", where)}", parameters);
						foreach (var c in fk.Columns) row[c.Child] = parent[c.Parent];
					}
					catch (SqlException) { } // a module CHECK rule rejects the extra key; the row stays as seeded
				}
			}
		}

		/// <summary>Scope tables ordered so every required (non-nullable) parent is seeded first.</summary>
		private List<string> SeedOrder()
		{
			var pending = new HashSet<string>(_scope, StringComparer.OrdinalIgnoreCase);
			var order = new List<string>();
			while (pending.Count > 0)
			{
				var ready = pending.Where(t => ForeignKeys(t).Where(IsRequired)
					.All(fk => fk.Parent == t || !_scope.Contains(fk.Parent) || !pending.Contains(fk.Parent))).OrderBy(t => t).ToList();
				if (ready.Count == 0) { order.AddRange(pending.OrderBy(t => t)); break; } // cycle of required keys; attempt anyway
				order.AddRange(ready);
				pending.ExceptWith(ready);
			}
			return order;
		}

		private async Task<Dictionary<string, object>> InsertOrThrowAsync(SqlConnection db, string table, SeededDepartment seeded, Dictionary<string, object> overrides)
		{
			var (row, error) = await TryInsertAsync(db, table, seeded, overrides);
			if (row == null) throw new InvalidOperationException($"Could not seed {table}: {error}");
			return row;
		}

		/// <summary>Level 0 fills optional foreign keys too (widest delete-order coverage); level 1 leaves them
		/// null for CHECK rules; level 2 also gives non-key integers unique values for unique indexes.</summary>
		private async Task<(Dictionary<string, object> Row, string Error)> TryInsertAsync(SqlConnection db, string table, SeededDepartment seeded, Dictionary<string, object> overrides)
		{
			string error = null;
			for (var level = 0; level <= 2; level++)
			{
				var values = await BuildValuesAsync(db, table, seeded, overrides, level);
				if (values.Error != null) { error = values.Error; continue; }
				try { return (await InsertAsync(db, table, values.Values), null); }
				catch (SqlException e) { error = e.Message; }
			}
			return (null, error);
		}

		/// <summary>True when a non-null foreign key chains the row to the department's roots. A row that reached its
		/// department only through optional keys left null cannot block the delete, so it is not verified.</summary>
		private bool IsLinked(string table, Dictionary<string, object> row, SeededDepartment seeded) =>
			ForeignKeys(table).Any(fk => fk.Parent != table && seeded.Linked.Contains(fk.Parent)
				&& fk.Columns.All(c => row.TryGetValue(c.Child, out var v) && v != null && v != DBNull.Value));

		private async Task<(Dictionary<string, object> Values, string Error)> BuildValuesAsync(SqlConnection db, string table, SeededDepartment seeded, Dictionary<string, object> overrides, int level)
		{
			var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
			foreach (var column in _columnsByTable[table])
				if (ColumnOverrides.TryGetValue(table + "." + column.Name, out var forced)) values[column.Name] = forced;
			if (overrides != null) foreach (var o in overrides) values[o.Key] = o.Value;
			var keyColumns = _primaryKeys.TryGetValue(table, out var pk) ? pk : new List<string>();

			foreach (var fk in ForeignKeys(table).Where(f => !f.IsTrivialSelfReference).OrderBy(f => IsRequired(f) ? 0 : 1))
			{
				var required = IsRequired(fk);
				Dictionary<string, object> parent = null;
				if (fk.Parent == table) parent = null;
				else if (_scope.Contains(fk.Parent)) { if (seeded != null) seeded.Rows.TryGetValue(fk.Parent, out parent); }
				else parent = await SharedAsync(db, fk.Parent);

				if (parent == null || (!required && level >= 1))
				{
					if (required) return (null, $"required parent {fk.Parent} ({fk.Name}) was not seeded");
					foreach (var column in fk.Columns.Where(c => Column(table, c.Child).Nullable && !values.ContainsKey(c.Child)))
						values[column.Child] = DBNull.Value;
					continue;
				}
				foreach (var column in fk.Columns)
					if (!values.TryGetValue(column.Child, out var existing) || (existing == DBNull.Value && required))
						values[column.Child] = parent[column.Parent];
			}

			foreach (var column in _columnsByTable[table])
			{
				if (column.Identity || column.Computed || column.Type == "timestamp" || values.ContainsKey(column.Name)) continue;
				var isKey = keyColumns.Contains(column.Name, StringComparer.OrdinalIgnoreCase);
				if (table != "Departments" && string.Equals(column.Name, "DepartmentId", StringComparison.OrdinalIgnoreCase) && column.Type == "int")
				{ values[column.Name] = seeded?.DepartmentId ?? 0; continue; }
				if (seeded != null && string.Equals(column.Name, "UserId", StringComparison.OrdinalIgnoreCase) && column.Type.EndsWith("char", StringComparison.Ordinal))
				{ values[column.Name] = seeded.MemberUserId; continue; }
				if (!isKey && (column.Nullable || column.HasDefault)) continue;
				var value = Generate(column, isKey || level >= 2);
				if (value == null) return (null, $"unsupported column type {column.Type} for {table}.{column.Name}");
				values[column.Name] = value;
			}

			return (values, null);
		}

		private object Generate(ColumnInfo column, bool unique)
		{
			switch (column.Type)
			{
				case "int": case "bigint": return unique ? ++_counter : 1;
				case "smallint": return unique ? (short)(++_counter % short.MaxValue) : (short)1;
				case "tinyint": return unique ? (byte)(++_counter % 250 + 1) : (byte)1;
				case "bit": return false;
				case "decimal": case "numeric": case "money": case "smallmoney": return 1m;
				case "float": return 1d;
				case "real": return 1f;
				case "date": case "datetime": case "datetime2": case "smalldatetime": return new DateTime(2026, 1, 1);
				case "datetimeoffset": return new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
				case "time": return TimeSpan.FromHours(1);
				case "uniqueidentifier": return Guid.NewGuid();
				case "nvarchar": case "nchar": case "varchar": case "char": case "ntext": case "text": case "sysname": return Text(column);
				case "varbinary": case "binary": case "image": return unique ? Bytes(column) : new byte[] { 1 };
				case "xml": return "<seed/>";
				default: return null;
			}
		}

		/// <summary>Unique text, 64 characters where the column allows (hash-shaped columns check the length).</summary>
		private string Text(ColumnInfo column)
		{
			var chars = column.MaxLength == -1 || column.Type == "ntext" || column.Type == "text" ? 64
				: column.Type.StartsWith("n", StringComparison.Ordinal) ? column.MaxLength / 2 : column.MaxLength;
			var length = Math.Min(chars, 64);
			var text = (++_counter).ToString().PadLeft(length, 'a');
			return text.Length > length ? text.Substring(text.Length - length) : text;
		}

		/// <summary>Unique bytes, 32 where the column allows (hash columns carry unique indexes).</summary>
		private byte[] Bytes(ColumnInfo column)
		{
			var bytes = new byte[column.MaxLength > 0 ? Math.Min((int)column.MaxLength, 32) : 32];
			var counter = BitConverter.GetBytes(++_counter);
			Array.Copy(counter, bytes, Math.Min(counter.Length, bytes.Length));
			return bytes;
		}

		/// <summary>One row per out-of-scope parent table (lookup data, addresses), shared by both departments.</summary>
		private async Task<Dictionary<string, object>> SharedAsync(SqlConnection db, string table)
		{
			if (_shared.TryGetValue(table, out var row)) return row;
			if (!_sharedInProgress.Add(table)) return null;
			try
			{
				var (inserted, _) = await TryInsertAsync(db, table, null, null);
				if (inserted != null) _shared[table] = inserted;
				return inserted;
			}
			finally { _sharedInProgress.Remove(table); }
		}

		private async Task<Dictionary<string, object>> InsertAsync(SqlConnection db, string table, Dictionary<string, object> values)
		{
			var parameters = new DynamicParameters();
			var columns = new List<string>();
			var slots = new List<string>();
			foreach (var (name, value) in values)
			{
				columns.Add("[" + name + "]");
				if (value == null || value == DBNull.Value) { slots.Add("NULL"); continue; }
				var slot = "@p" + parameters.ParameterNames.Count();
				parameters.Add(slot, value);
				slots.Add(slot);
			}

			var sql = new StringBuilder();
			var identity = _columnsByTable[table].FirstOrDefault(c => c.Identity);
			var keyColumns = _primaryKeys.TryGetValue(table, out var pk) ? pk : null;
			var output = identity == null && keyColumns == null && !_triggeredTables.Contains(table) ? " OUTPUT INSERTED.*" : "";
			sql.Append(columns.Count == 0
				? $"INSERT INTO [dbo].[{table}]{output} DEFAULT VALUES;"
				: $"INSERT INTO [dbo].[{table}] ({string.Join(",", columns)}){output} VALUES ({string.Join(",", slots)});");
			if (identity != null) sql.Append($" SELECT * FROM [dbo].[{table}] WHERE [{identity.Name}] = SCOPE_IDENTITY();");
			else if (keyColumns != null) sql.Append($" SELECT * FROM [dbo].[{table}] WHERE {string.Join(" AND ", keyColumns.Select(k => $"[{k}] = @k_{k}"))};");
			if (identity == null && keyColumns != null)
				foreach (var key in keyColumns) parameters.Add("k_" + key, values[key]);

			var result = (IDictionary<string, object>)await db.QuerySingleOrDefaultAsync(sql.ToString(), parameters);
			return result == null
				? new Dictionary<string, object>(values, StringComparer.OrdinalIgnoreCase)
				: new Dictionary<string, object>(result, StringComparer.OrdinalIgnoreCase);
		}

		private async Task<bool> RowExistsAsync(SqlConnection db, string table, Dictionary<string, object> row)
		{
			if (!_primaryKeys.TryGetValue(table, out var keys)) return false;
			var parameters = new DynamicParameters();
			foreach (var key in keys) parameters.Add("k_" + key, row[key]);
			return await db.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM [dbo].[{table}] WHERE {string.Join(" AND ", keys.Select(k => $"[{k}] = @k_{k}"))}", parameters) > 0;
		}
	}
}
