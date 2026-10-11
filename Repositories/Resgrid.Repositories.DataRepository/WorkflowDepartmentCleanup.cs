using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>
	/// Workflow subtree of an authorized department deletion, in the caller's transaction. Runs after the checklist, inventory
	/// and RMS cleanups, which remove their own runs first (RMS finds its runs through the records it is about to delete).
	/// Credentials hold encrypted secrets and go with the department. ProtectedWorkflowDisclosures is the hash-chained
	/// disclosure ledger and is retained by design (decided 2026-10-11).
	/// </summary>
	public static class WorkflowDepartmentCleanup
	{
		/// <summary>Every table this cleanup deletes by DepartmentId (run logs and steps go through their parents).</summary>
		internal static readonly string[] Tables = { "WorkflowRuns", "WorkflowProtectedReleases", "WorkflowDailyUsages", "Workflows", "WorkflowCredentials" };

		public static async Task DeleteWithinTransactionAsync(DbConnection connection, DbTransaction transaction, int departmentId, DatabaseTypes type, CancellationToken ct = default)
		{
			DepartmentCleanupCommands.RequireTransaction(connection, transaction, departmentId, "Workflow");
			// Foreign keys: WorkflowRunLogs -> WorkflowRuns -> Workflows, WorkflowSteps -> Workflows and WorkflowCredentials.
			await DepartmentCleanupCommands.DeleteChildAsync(connection, transaction, type, departmentId, "WorkflowRunLogs", false, ct, ("WorkflowRuns", "WorkflowRunId", "WorkflowRunId"));
			await DepartmentCleanupCommands.DeleteByDepartmentAsync(connection, transaction, type, departmentId, new[] { "WorkflowRuns", "WorkflowProtectedReleases" }, ct);
			await DepartmentCleanupCommands.DeleteChildAsync(connection, transaction, type, departmentId, "WorkflowSteps", false, ct,
				("Workflows", "WorkflowId", "WorkflowId"), ("WorkflowCredentials", "WorkflowCredentialId", "WorkflowCredentialId"));
			await DepartmentCleanupCommands.DeleteByDepartmentAsync(connection, transaction, type, departmentId, new[] { "WorkflowDailyUsages", "Workflows", "WorkflowCredentials" }, ct);
		}
	}
}
