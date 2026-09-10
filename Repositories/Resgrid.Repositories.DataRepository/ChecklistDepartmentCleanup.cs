using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using CommandDefinition = Dapper.CommandDefinition;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Inventories;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>Readiness subtree of an authorized department deletion, in the caller's transaction.
	/// This is not an independent purge API or a substitute for department retention authorization.</summary>
	public static class ChecklistDepartmentCleanup
	{
		public static async Task DeleteWithinTransactionAsync(DbConnection connection, DbTransaction transaction, int departmentId, DatabaseTypes type, CancellationToken ct = default)
		{
			if (transaction == null || transaction.Connection != connection || departmentId <= 0) throw new InvalidOperationException("Department cleanup requires a scoped transaction.");
			var pg = type == DatabaseTypes.Postgres;
			string Q(string value) => pg ? value.ToLowerInvariant() : "[" + value + "]";
			async Task<bool> Exists(string table)
			{
				var result = await connection.ExecuteScalarAsync<int>(new CommandDefinition(pg ? "SELECT CASE WHEN to_regclass(@Name) IS NULL THEN 0 ELSE 1 END" : "SELECT CASE WHEN OBJECT_ID(@Name,'U') IS NOT NULL THEN 1 WHEN HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','VIEW DEFINITION')=1 THEN 0 ELSE -1 END", new { Name = (pg ? "public." + table.ToLowerInvariant() : "dbo." + table) }, transaction, cancellationToken: ct));
				if (result < 0) throw new InvalidOperationException("Cannot verify the department cleanup schema."); return result == 1;
			}
			var hasChecklists = await Exists("ChecklistDefinitions");
			if (await Exists("ChecklistAccessFence")) await connection.ExecuteScalarAsync<int>(new CommandDefinition(pg ? "SELECT id FROM checklistaccessfence WHERE id=1 FOR UPDATE" : "SELECT Id FROM ChecklistAccessFence WITH (UPDLOCK,HOLDLOCK) WHERE Id=1", transaction: transaction, cancellationToken: ct));
			var lockedDepartment = await connection.ExecuteScalarAsync<int>(new CommandDefinition(pg ? "SELECT departmentid FROM departments WHERE departmentid=@DepartmentId FOR UPDATE" : "SELECT DepartmentId FROM Departments WITH (UPDLOCK,HOLDLOCK) WHERE DepartmentId=@DepartmentId", new { DepartmentId = departmentId }, transaction, cancellationToken: ct));
			if (lockedDepartment != departmentId) throw new InvalidOperationException("Department cleanup could not lock the requested department.");
			// RMS uses the same department lock for legal holds. Preserve source readiness evidence
			// conservatively when any active hold exists, even if its content is encrypted.
			if (await Exists("RmsRecordLegalHolds") && await connection.ExecuteScalarAsync<int>(new CommandDefinition($"SELECT COUNT(*) FROM {Q("RmsRecordLegalHolds")} WHERE {Q("DepartmentId")}=@DepartmentId AND {Q("ReleasedOn")} IS NULL", new { DepartmentId = departmentId }, transaction, cancellationToken: ct)) > 0)
				throw new InvalidOperationException("Department readiness evidence is retained under an active legal hold.");
			await InventoryDepartmentCleanup.DeleteWithinTransactionAsync(connection, transaction, departmentId, type, ct);
			if (!hasChecklists) return;
			var triggers = ChecklistWorkflowPayload.Triggers.Except(InventoryWorkflowPayload.Triggers).ToArray();
			var triggerPredicate = Q("TriggerEventType") + (pg ? "=ANY(@Triggers)" : " IN @Triggers");
			var auditPredicate = Q("LogType") + (pg ? "=ANY(@AuditTypes)" : " IN @AuditTypes");
			var producerPredicate = Q("ProducerSubsystem") + (pg ? "=ANY(@Producers)" : " IN @Producers");
			var parameters = new { DepartmentId = departmentId, Producers = ChecklistWorkflowPayload.ReadinessProducers.Where(p => p != "Inventory").ToArray(), Triggers = triggers, AuditTypes = ReadinessHistoryFields.AuditTypes.Except(InventoryDepartmentCleanup.AuditTypes).ToArray() };
			if (await Exists("WorkflowRuns"))
			{
				if (await Exists("WorkflowRunLogs")) await connection.ExecuteAsync(new CommandDefinition($"DELETE FROM {Q("WorkflowRunLogs")} WHERE {Q("WorkflowRunId")} IN (SELECT {Q("WorkflowRunId")} FROM {Q("WorkflowRuns")} WHERE {Q("DepartmentId")}=@DepartmentId AND {triggerPredicate})", parameters, transaction, cancellationToken: ct));
				await connection.ExecuteAsync(new CommandDefinition($"DELETE FROM {Q("WorkflowRuns")} WHERE {Q("DepartmentId")}=@DepartmentId AND {triggerPredicate}", parameters, transaction, cancellationToken: ct));
			}
			if (await Exists("DomainEventOutbox")) await connection.ExecuteAsync(new CommandDefinition($"DELETE FROM {Q("DomainEventOutbox")} WHERE {Q("DepartmentId")}=@DepartmentId AND {producerPredicate}", parameters, transaction, cancellationToken: ct));
			if (await Exists("AuditLogs")) await connection.ExecuteAsync(new CommandDefinition($"DELETE FROM {Q("AuditLogs")} WHERE {Q("DepartmentId")}=@DepartmentId AND {auditPredicate}", parameters, transaction, cancellationToken: ct));
			foreach (var table in new[] { "WorkOrderPartMovements", "WorkOrderVendorCharges", "WorkOrderOperationReceipts", "WorkOrderPolicies", "WorkOrderReportSnapshots", "WorkOrderFailureIntents", "WorkOrderSafetyHolds", "WorkOrderRecurrenceChanges", "WorkOrderMeterReadings", "ReadinessProBillingAccounts", "WorkOrderNotifications", "WorkOrderFiles", "WorkOrderParts", "WorkOrderLabors", "WorkOrderActivities", "WorkOrders", "WorkOrderRecurrenceVersions", "WorkOrderRecurrences", "ChecklistReminders", "ChecklistCompletionFiles", "ChecklistCompletionItems", "ChecklistCompletions", "ChecklistOccurrences", "ChecklistSchedules", "ChecklistDefinitionVersions", "ChecklistDefinitions", "DepartmentChecklistSettings" })
				if (await Exists(table)) await connection.ExecuteAsync(new CommandDefinition($"DELETE FROM {Q(table)} WHERE {Q("DepartmentId")}=@DepartmentId", parameters, transaction, cancellationToken: ct));
		}
	}
}
