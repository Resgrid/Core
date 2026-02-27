using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IWorkflowCredentialRepository : IRepository<WorkflowCredential>
	{
		Task<IEnumerable<WorkflowCredential>> GetAllByDepartmentIdAsync(int departmentId);
	}
}

