using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Model.Custom;

namespace Resgrid.Model.Repositories
{
	public interface IDepartmentsRepository : IRepository<Department>
	{
		Department GetDepartmentById(int departmentId);
		Department GetDepartmentWithMembersById(int departmentId);
		Department GetDepartmentWithMembersByUserId(string userId);
		ValidateUserForDepartmentResult GetValidateUserForDepartmentData(string userName);
		Department GetDepartmentForUserByUsername(string userName);
		DepartmentReport GetDepartmentReport(int departmentId);
		List<PersonName> GetAllPersonnelNamesForDepartment(int departmentId);
		Task<Department> GetDepartmentForUserByUsernameAsync(string userName);
	}
}
