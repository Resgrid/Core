using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IWorkflowRepository : IRepository<Workflow>
	{
		Task<IEnumerable<Workflow>> GetAllActiveByDepartmentAndEventTypeAsync(int departmentId, int triggerEventType);
		Task<IEnumerable<Workflow>> GetAllByDepartmentIdAsync(int departmentId);
		Task<Workflow> GetByDepartmentAndEventTypeAsync(int departmentId, int triggerEventType);

		/// <summary>
		/// Atomically deletes a workflow and all its dependent child records (WorkflowRunLogs,
		/// WorkflowRuns, WorkflowSteps) within a single database transaction, preventing FK
		/// constraint violations caused by concurrent run inserts racing with deletion.
		/// </summary>
		Task DeleteWorkflowWithAllDependenciesAsync(string workflowId);
	}
}

