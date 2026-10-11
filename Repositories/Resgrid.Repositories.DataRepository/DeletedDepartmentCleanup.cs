using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using CommandDefinition = Dapper.CommandDefinition;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Inventories;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>
	/// Leftovers of departments deleted before department deletion purged every table without a foreign key to Departments
	/// (decided 2026-10-11): ADP keys, member sensitive data, workflow credentials, payroll, chat and the rest. Purges them with
	/// the cleanups department deletion runs, so the two cannot drift apart. RMS rows keep their own sweep (worker 43, which
	/// also owns their search marker), and the retained tables (platform audit trails, sessions and login rows, search
	/// markers) are not in any cleanup list. Neither is AuditLogs: the delete clears it, then DeleteService writes the
	/// deletion-executed row as the durable trail of the deletion, so a deleted department's AuditLogs rows are kept (only
	/// readiness-type rows go, with the readiness subtree). An active Records legal hold keeps everything, as it defers a live
	/// deletion.
	/// </summary>
	public static class DeletedDepartmentCleanup
	{
		/// <summary>Every table the purge clears by DepartmentId: where discovery looks for leftovers.</summary>
		public static IReadOnlyList<string> Tables { get; } = ChecklistDepartmentCleanup.Tables.Concat(InventoryTables.All.Values)
			.Concat(AdminAssistDepartmentCleanup.Tables).Concat(UnlinkedDepartmentCleanup.AllTables).Concat(DataProtectionDepartmentCleanup.Tables)
			.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

		/// <summary>
		/// Departments that still own rows in <see cref="Tables"/> but no longer exist. One pass over the tables (an anti-join
		/// each), so it belongs in a daily job. Ids below 1 are never departments and are ignored.
		/// </summary>
		public static async Task<List<int>> GetDeletedDepartmentIdsAsync(DbConnection connection, DbTransaction transaction, DatabaseTypes type, CancellationToken ct = default)
		{
			string Q(string value) => DepartmentCleanupCommands.Q(type, value);
			var pg = type == DatabaseTypes.Postgres;
			var present = (await connection.QueryAsync<string>(new CommandDefinition(pg
				? "SELECT table_name FROM information_schema.columns WHERE table_schema='public' AND column_name='departmentid' AND table_name=ANY(@Tables)"
				: "SELECT t.name FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id WHERE SCHEMA_NAME(t.schema_id)='dbo' AND c.name='DepartmentId' AND t.name IN @Tables",
				new { Tables = Tables.Select(t => pg ? t.ToLowerInvariant() : t).ToArray() }, transaction, cancellationToken: ct))).OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
			// An empty result must mean the tables are not installed, not that this principal cannot see the schema.
			if (present.Count == 0)
			{
				await DepartmentCleanupCommands.ExistsAsync(connection, transaction, type, "Departments", ct);
				return new List<int>();
			}

			var owners = string.Join(" UNION ", present.Select(t =>
				$"SELECT o.{Q("DepartmentId")} AS {Q("DepartmentId")} FROM {Q(t)} o WHERE o.{Q("DepartmentId")} > 0 AND NOT EXISTS (SELECT 1 FROM {Q("Departments")} d WHERE d.{Q("DepartmentId")}=o.{Q("DepartmentId")})"));
			return (await connection.QueryAsync<int>(new CommandDefinition($"SELECT l.{Q("DepartmentId")} FROM ({owners}) l ORDER BY l.{Q("DepartmentId")}",
				transaction: transaction, cancellationToken: ct))).ToList();
		}

		/// <summary>
		/// Purges one deleted department's leftovers in the caller's transaction: the readiness subtree, the unlinked tables,
		/// then ADP with the keys last. Throws when the department still exists. A live hold returns without deleting.
		/// </summary>
		public static async Task<DeletedDepartmentPurgeOutcome> PurgeWithinTransactionAsync(DbConnection connection, DbTransaction transaction, int departmentId, DatabaseTypes type, CancellationToken ct = default)
		{
			DepartmentCleanupCommands.RequireTransaction(connection, transaction, departmentId, "Deleted department");
			string Q(string value) => DepartmentCleanupCommands.Q(type, value);
			var parameters = new { DepartmentId = departmentId };
			// On SQL Server the range lock holds until commit, so the purge never races a row for this id.
			var existing = await connection.ExecuteScalarAsync<int>(new CommandDefinition(type == DatabaseTypes.Postgres
				? "SELECT COUNT(*) FROM departments WHERE departmentid=@DepartmentId"
				: "SELECT COUNT(*) FROM Departments WITH (UPDLOCK,HOLDLOCK) WHERE DepartmentId=@DepartmentId", parameters, transaction, cancellationToken: ct));
			if (existing > 0) throw new InvalidOperationException("A deleted-department purge cannot purge a department that still exists.");

			if (await DepartmentCleanupCommands.ExistsAsync(connection, transaction, type, "RmsRecordLegalHolds", ct) && await connection.ExecuteScalarAsync<int>(new CommandDefinition(
				$"SELECT COUNT(*) FROM {Q("RmsRecordLegalHolds")} WHERE {Q("DepartmentId")}=@DepartmentId AND {Q("ReleasedOn")} IS NULL", parameters, transaction, cancellationToken: ct)) > 0)
				return DeletedDepartmentPurgeOutcome.LegalHold;
			if (await DataProtectionDepartmentCleanup.HasLiveOperationLockAsync(connection, transaction, departmentId, type, ct))
				return DeletedDepartmentPurgeOutcome.ProtectedDataMigration;

			await ChecklistDepartmentCleanup.DeleteSubtreeWithinTransactionAsync(connection, transaction, departmentId, type, ct);
			await UnlinkedDepartmentCleanup.DeleteWithinTransactionAsync(connection, transaction, departmentId, type, ct);
			await DataProtectionDepartmentCleanup.DeleteWithinTransactionAsync(connection, transaction, departmentId, type, ct);
			return DeletedDepartmentPurgeOutcome.Purged;
		}
	}
}
