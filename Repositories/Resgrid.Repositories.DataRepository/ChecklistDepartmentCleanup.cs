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
			if (!await Exists("ChecklistDefinitions")) return;
			if (await Exists("ChecklistAccessFence")) await connection.ExecuteScalarAsync<int>(new CommandDefinition(pg ? "SELECT id FROM checklistaccessfence WHERE id=1 FOR UPDATE" : "SELECT Id FROM ChecklistAccessFence WITH (UPDLOCK,HOLDLOCK) WHERE Id=1", transaction: transaction, cancellationToken: ct));
			await connection.ExecuteScalarAsync<int>(new CommandDefinition(pg ? "SELECT departmentid FROM departments WHERE departmentid=@DepartmentId FOR UPDATE" : "SELECT DepartmentId FROM Departments WITH (UPDLOCK,HOLDLOCK) WHERE DepartmentId=@DepartmentId", new { DepartmentId = departmentId }, transaction, cancellationToken: ct));
			// RMS uses the same department lock for legal holds. Preserve source readiness evidence
			// conservatively when any active hold exists, even if its content is encrypted.
			if (await Exists("RmsRecordLegalHolds") && await connection.ExecuteScalarAsync<int>(new CommandDefinition($"SELECT COUNT(*) FROM {Q("RmsRecordLegalHolds")} WHERE {Q("DepartmentId")}=@DepartmentId AND {Q("ReleasedOn")} IS NULL", new { DepartmentId = departmentId }, transaction, cancellationToken: ct)) > 0)
				throw new InvalidOperationException("Department readiness evidence is retained under an active legal hold.");
			var triggers = new[] { WorkflowTriggerEventType.ChecklistCompleted, WorkflowTriggerEventType.ChecklistFailed, WorkflowTriggerEventType.ChecklistMissed, WorkflowTriggerEventType.ChecklistScheduleChanged, WorkflowTriggerEventType.ChecklistOccurrenceSkipped }.Select(t => (int)t).ToArray();
			var triggerPredicate = Q("TriggerEventType") + (pg ? "=ANY(@Triggers)" : " IN @Triggers");
			var auditPredicate = Q("LogType") + (pg ? "=ANY(@AuditTypes)" : " IN @AuditTypes");
			var parameters = new { DepartmentId = departmentId, Triggers = triggers, AuditTypes = ReadinessHistoryFields.AuditTypes };
			if (await Exists("WorkflowRuns"))
			{
				if (await Exists("WorkflowRunLogs")) await connection.ExecuteAsync(new CommandDefinition($"DELETE FROM {Q("WorkflowRunLogs")} WHERE {Q("WorkflowRunId")} IN (SELECT {Q("WorkflowRunId")} FROM {Q("WorkflowRuns")} WHERE {Q("DepartmentId")}=@DepartmentId AND {triggerPredicate})", parameters, transaction, cancellationToken: ct));
				await connection.ExecuteAsync(new CommandDefinition($"DELETE FROM {Q("WorkflowRuns")} WHERE {Q("DepartmentId")}=@DepartmentId AND {triggerPredicate}", parameters, transaction, cancellationToken: ct));
			}
			if (await Exists("DomainEventOutbox")) await connection.ExecuteAsync(new CommandDefinition($"DELETE FROM {Q("DomainEventOutbox")} WHERE {Q("DepartmentId")}=@DepartmentId AND {Q("ProducerSubsystem")}='Checklists'", parameters, transaction, cancellationToken: ct));
			if (await Exists("AuditLogs")) await connection.ExecuteAsync(new CommandDefinition($"DELETE FROM {Q("AuditLogs")} WHERE {Q("DepartmentId")}=@DepartmentId AND {auditPredicate}", parameters, transaction, cancellationToken: ct));
			foreach (var table in new[] { "ChecklistReminders", "ChecklistCompletionFiles", "ChecklistCompletionItems", "ChecklistCompletions", "ChecklistOccurrences", "ChecklistSchedules", "ChecklistDefinitionVersions", "ChecklistDefinitions", "DepartmentChecklistSettings" })
				if (await Exists(table)) await connection.ExecuteAsync(new CommandDefinition($"DELETE FROM {Q(table)} WHERE {Q("DepartmentId")}=@DepartmentId", parameters, transaction, cancellationToken: ct));
		}
	}
}
