using System;

namespace Resgrid.Model.Services
{
	public interface IDeleteService
	{
		DeleteUserResults DeleteUser(int departmentId, string authroizingUserId, string UserIdToDelete);
		DeleteGroupResults DeleteGroup(int departmentGroupId, string currentUserId);
	}
}
