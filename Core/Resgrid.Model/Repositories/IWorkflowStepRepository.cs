using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IWorkflowStepRepository : IRepository<WorkflowStep>
	{
		Task<IEnumerable<WorkflowStep>> GetAllByWorkflowIdAsync(string workflowId);
	}
}

