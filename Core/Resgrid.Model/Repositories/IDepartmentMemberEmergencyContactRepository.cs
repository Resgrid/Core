using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IDepartmentMemberEmergencyContactRepository : IRepository<DepartmentMemberEmergencyContact>
	{
		/// <summary>The member's non-deleted emergency contacts for one department, primary first.</summary>
		Task<IEnumerable<DepartmentMemberEmergencyContact>> GetAllByDepartmentAndUserAsync(int departmentId, string userId);

		/// <summary>
		/// Hard-deletes every emergency contact a member holds in one department, soft-deleted rows
		/// included. Used when an account is deleted: a soft delete would leave the contact's name,
		/// phone and email in the database, which is the opposite of what a deletion promises.
		/// </summary>
		Task<int> DeleteAllByDepartmentAndUserAsync(int departmentId, string userId);
	}
}
