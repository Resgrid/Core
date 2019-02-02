using System;
using System.Collections.Generic;

namespace Resgrid.Model.Services
{
	public interface IUserStateService
	{
		UserState GetUserStateById(int userStateId);
		UserState GetLastUserStateByUserId(string userId);
		UserState CreateUserState(string userId, int departmentId, int userStateType);
		List<UserState> GetStatesForDepartment(int departmentId);
		void DeleteStatesForUser(string userId);
		List<UserState> GetLatestStatesForDepartment(int departmentId, bool bypassCache = false);
		UserState CreateUserState(string userId, int departmentId, int userStateType, string note);
		UserState CreateUserState(string userId, int departmentId, int userStateType, string note, DateTime timeStamp);
		UserState GetPerviousUserState(string userId, int userStateId);
		List<UserState> GetSAlltatesForDepartmentInDateRange(int departmentId, DateTime startDate, DateTime endDate);
		void InvalidateLatestStatesForDepartmentCache(int departmentId);
	}
}