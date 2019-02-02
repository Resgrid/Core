using System;

namespace Resgrid.Model.Services
{
	public interface IDeleteService
	{
		DeleteUserResults DeleteUser(int departmentId, string authroizingUserId, string UserIdToDelete);
		void DeleteGroup(int departmentGroupId);
	}
}