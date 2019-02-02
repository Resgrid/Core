using System.Collections.Generic;

namespace Resgrid.Model.Repositories
{
	public interface IDepartmentGroupMembersRepository : IRepository<DepartmentGroupMember>
	{
		List<DepartmentGroupMember> GetAllMembersForGroup(int groupId);
	}
}
