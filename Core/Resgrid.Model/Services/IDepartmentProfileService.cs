using System;
using System.Collections.Generic;

namespace Resgrid.Model.Services
{
	public interface IDepartmentProfileService
	{
		List<DepartmentProfile> GetAll();
		List<DepartmentProfile> GetAllActive();
		DepartmentProfile GetDepartmentProfileByDepartmentId(int departmentId);
		DepartmentProfile SaveDepartmentProfile(DepartmentProfile profile);
		DepartmentProfile GetOrInitializeDepartmentProfile(int departmentId);
		List<DepartmentProfileArticle> GetArticlesForDepartment(int departmentProfileId);
		DepartmentProfileArticle SaveArticle(DepartmentProfileArticle article);
		List<DepartmentProfileArticle> GetVisibleArticlesForDepartment(int departmentProfileId);
		List<DepartmentProfileMessage> GetVisibleMessagesForDepartment(int departmentProfileId);
		DepartmentProfileUser GetUserByIdentity(string id);
		DepartmentProfileUser SaveUser(DepartmentProfileUser user);
		List<DepartmentProfileUserFollow> GetFollowsForUser(string userId);
		List<DepartmentProfileArticle> GetArticlesForUser(string userId);
		DepartmentProfile GetProfileById(int departmentProfileId);
		DepartmentProfileUserFollow SaveFollow(DepartmentProfileUserFollow follow);
		DepartmentProfileUserFollow FollowDepartment(string userId, int departmentProfileId, string code);
		DepartmentProfileUserFollow GetFollowForUserDepartment(string userId, int departmentProfileId);
		void DeleteFollow(DepartmentProfileUserFollow follow);
		void UnFollowDepartment(string userId, int departmentProfileId);
	}
}