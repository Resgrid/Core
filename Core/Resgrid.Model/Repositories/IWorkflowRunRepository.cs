using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IWorkflowRunRepository : IRepository<WorkflowRun>
	{
		Task<IEnumerable<WorkflowRun>> GetByDepartmentIdPagedAsync(int departmentId, int page, int pageSize);
		Task<IEnumerable<WorkflowRun>> GetPendingAndRunningByDepartmentIdAsync(int departmentId);
		Task<IEnumerable<WorkflowRun>> GetRunsByWorkflowIdAsync(string workflowId, int page, int pageSize);
		Task<IEnumerable<WorkflowRun>> GetRunsByDepartmentInMinuteAsync(int departmentId);
		Task DeleteAllByWorkflowIdAsync(string workflowId);

		/// <summary>The initial run for a (WorkflowId, EventId) pair, or null; plan section 5.6 dedup (retries reuse it).</summary>
		Task<WorkflowRun> GetByWorkflowAndEventAsync(string workflowId, string eventId);
	}
}
