using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IWorkflowRunLogRepository : IRepository<WorkflowRunLog>
	{
		Task<IEnumerable<WorkflowRunLog>> GetByWorkflowRunIdAsync(string workflowRunId);
	}
}
