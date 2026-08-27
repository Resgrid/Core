using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IDepartmentProtectedDataEgressPolicyRepository : IRepository<DepartmentProtectedDataEgressPolicy>
	{
		Task<DepartmentProtectedDataEgressPolicy> GetByDepartmentIdAsync(int departmentId);
	}
}
