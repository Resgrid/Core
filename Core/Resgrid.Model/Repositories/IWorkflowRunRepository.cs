using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IWorkflowRunRepository : IRepository<WorkflowRun>
	{
		/// <summary>Claim a checklist event attempt once; duplicate queue deliveries cannot execute it twice.</summary>
		Task<bool> TryStartChecklistRunAsync(string runId, string workflowId, int departmentId, int attemptNumber, string safePayload);
		Task<IEnumerable<WorkflowRun>> GetByDepartmentIdPagedAsync(int departmentId, int page, int pageSize);
		Task<IEnumerable<WorkflowRun>> GetPendingAndRunningByDepartmentIdAsync(int departmentId);
		Task<IEnumerable<WorkflowRun>> GetRunsByWorkflowIdAsync(string workflowId, int page, int pageSize);
		Task<IEnumerable<WorkflowRun>> GetRunsByDepartmentInMinuteAsync(int departmentId);
		Task DeleteAllByWorkflowIdAsync(string workflowId);

		/// <summary>The initial run for a (WorkflowId, EventId) pair, or null; plan section 5.6 dedup (retries reuse it).</summary>
		Task<WorkflowRun> GetByWorkflowAndEventAsync(string workflowId, string eventId);
		Task<List<WorkflowRun>> GetByWorkflowsAndEventAsync(int departmentId, IReadOnlyCollection<string> workflowIds, string eventId);

		/// <summary>
		/// Run counts per workflow and status for runs started at or after <paramref name="sinceUtc"/>, plus every pending, running or
		/// retrying run whenever it started (home dashboard Workflow Health). Aggregates only; never the payload or error text.
		/// </summary>
		Task<IEnumerable<WorkflowRunStatusCount>> GetStatusCountsAsync(int departmentId, DateTime sinceUtc);
	}
}
