using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IDepartmentMemberSensitiveDataRepository : IRepository<DepartmentMemberSensitiveData>
	{
		Task<DepartmentMemberSensitiveData> GetByDepartmentAndUserAsync(int departmentId, string userId);

		/// <summary>Every member row for one department — one query for list and report screens.</summary>
		Task<IEnumerable<DepartmentMemberSensitiveData>> GetAllByDepartmentIdAsync(int departmentId);

		/// <summary>
		/// Departments that still have members whose legacy global-profile data (identification
		/// number or addresses) has not been moved onto a department-scoped row. Drives the
		/// relocation worker, and reads zero once the move is complete — which is the precondition
		/// for the contract migration that drops the legacy columns.
		/// </summary>
		Task<IEnumerable<int>> GetDepartmentIdsWithOutstandingLegacyProfileDataAsync();
	}
}
