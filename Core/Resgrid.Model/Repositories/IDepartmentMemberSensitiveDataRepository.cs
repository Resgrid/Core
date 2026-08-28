using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IDepartmentMemberSensitiveDataRepository : IRepository<DepartmentMemberSensitiveData>
	{
		Task<DepartmentMemberSensitiveData> GetByDepartmentAndUserAsync(int departmentId, string userId);
	}
}
