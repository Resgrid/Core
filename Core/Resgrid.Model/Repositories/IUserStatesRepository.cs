using System.Collections.Generic;

namespace Resgrid.Model.Repositories
{
	public interface IUserStatesRepository : IRepository<UserState>
	{
		List<UserState> GetLatestUserStatesForDepartment(int departmentId);
		UserState GetUserStateById(int userStateId);
	}
}
