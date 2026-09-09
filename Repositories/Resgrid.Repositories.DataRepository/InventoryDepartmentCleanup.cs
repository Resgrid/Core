using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Inventories;
using CommandDefinition = Dapper.CommandDefinition;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>Inventory subtree of authorized department deletion. The caller holds the department lock
	/// and checks legal holds before invoking this helper; it never commits or grants independent purge access.</summary>
	internal static class InventoryDepartmentCleanup
	{
		internal static readonly int[] AuditTypes = Enum.GetValues<AuditLogTypes>()
			.Where(t => t.ToString().StartsWith("Inventory", StringComparison.Ordinal)).Select(t => (int)t).ToArray();

		internal static async Task DeleteWithinTransactionAsync(DbConnection connection, DbTransaction transaction, int departmentId, DatabaseTypes type, CancellationToken ct = default)
		{
			if (transaction == null || transaction.Connection != connection || departmentId <= 0) throw new InvalidOperationException("Inventory department cleanup requires the caller's scoped transaction.");
			var pg = type == DatabaseTypes.Postgres;
			string Q(string value) => pg ? value.ToLowerInvariant() : "[" + value + "]";
			async Task<bool> Exists(string table)
			{
				var result = await connection.ExecuteScalarAsync<int>(new CommandDefinition(pg
					? "SELECT CASE WHEN to_regclass(@Name) IS NULL THEN 0 ELSE 1 END"
					: "SELECT CASE WHEN OBJECT_ID(@Name,'U') IS NOT NULL THEN 1 WHEN HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','VIEW DEFINITION')=1 THEN 0 ELSE -1 END",
					new { Name = pg ? "public." + table.ToLowerInvariant() : "dbo." + table }, transaction, cancellationToken: ct));
				if (result < 0) throw new InvalidOperationException("Cannot verify the inventory department cleanup schema.");
				return result == 1;
			}
			var parameters = new { DepartmentId = departmentId, Triggers = InventoryWorkflowPayload.Triggers.ToArray(), AuditTypes };
			var triggerPredicate = Q("TriggerEventType") + (pg ? "=ANY(@Triggers)" : " IN @Triggers");
			if (await Exists("WorkflowRuns"))
			{
				if (await Exists("WorkflowRunLogs"))
					await connection.ExecuteAsync(new CommandDefinition($"DELETE FROM {Q("WorkflowRunLogs")} WHERE {Q("WorkflowRunId")} IN (SELECT {Q("WorkflowRunId")} FROM {Q("WorkflowRuns")} WHERE {Q("DepartmentId")}=@DepartmentId AND {triggerPredicate})", parameters, transaction, cancellationToken: ct));
				await connection.ExecuteAsync(new CommandDefinition($"DELETE FROM {Q("WorkflowRuns")} WHERE {Q("DepartmentId")}=@DepartmentId AND {triggerPredicate}", parameters, transaction, cancellationToken: ct));
			}
			if (await Exists("DomainEventOutbox"))
				await connection.ExecuteAsync(new CommandDefinition($"DELETE FROM {Q("DomainEventOutbox")} WHERE {Q("DepartmentId")}=@DepartmentId AND {Q("ProducerSubsystem")}='Inventory'", parameters, transaction, cancellationToken: ct));
			if (await Exists("AuditLogs") && AuditTypes.Length > 0)
				await connection.ExecuteAsync(new CommandDefinition($"DELETE FROM {Q("AuditLogs")} WHERE {Q("DepartmentId")}=@DepartmentId AND {Q("LogType")}{(pg ? "=ANY(@AuditTypes)" : " IN @AuditTypes")}", parameters, transaction, cancellationToken: ct));

			var present = new HashSet<string>(StringComparer.Ordinal);
			foreach (var table in InventoryTables.All.Values) if (await Exists(table)) present.Add(table);
			async Task Delete(string table)
			{
				if (present.Contains(table)) await connection.ExecuteAsync(new CommandDefinition($"DELETE FROM {Q(table)} WHERE {Q("DepartmentId")}=@DepartmentId", parameters, transaction, cancellationToken: ct));
			}
			if (present.Contains("InventoryCountItems"))
				await connection.ExecuteAsync(new CommandDefinition($"UPDATE {Q("InventoryCountItems")} SET {Q("TransactionId")}=NULL WHERE {Q("DepartmentId")}=@DepartmentId", parameters, transaction, cancellationToken: ct));
			foreach (var table in new[] { "InventoryAlertDeliveries", "InventoryAlerts", "RecordInventoryUsages", "InventoryTransferItems", "InventoryTransactions", "InventoryCountItems", "InventoryCounts", "InventoryPurchaseOrderItems", "InventoryPurchaseOrders", "InventoryVendors", "InventoryIssuances", "InventoryTransfers", "InventoryStocks", "InventoryKitItems" }) await Delete(table);
			// Break only the nullable asset -> location edge; changing container holder columns would violate its CHECK.
			// Protected Content and immutable historical transactions are never decoded or rewritten.
			if (present.Contains("InventoryAssets"))
				await connection.ExecuteAsync(new CommandDefinition($"UPDATE {Q("InventoryAssets")} SET {Q("CurrentLocationId")}=NULL WHERE {Q("DepartmentId")}=@DepartmentId", parameters, transaction, cancellationToken: ct));
			foreach (var table in new[] { "InventoryLocations", "InventoryAssets", "InventoryLots", "InventoryItems", "InventoryCategories", "InventoryKits" }) await Delete(table);
			// Keep the migration receipt/fence until every modern row is gone. Legacy deletion follows in the caller.
			await Delete("InventoryOperations");
		}
	}
}
