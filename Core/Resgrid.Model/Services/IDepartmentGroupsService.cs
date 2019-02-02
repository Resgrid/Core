using Microsoft.AspNet.Identity.EntityFramework6;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IDepartmentGroupsService
	{
		DepartmentGroup Save(DepartmentGroup departmentGroup);
		List<DepartmentGroup> GetAllGroupsForDepartment(int departmentId);
		DepartmentGroup GetGroupById(int departmentGroupId, bool bypassCache = true);
		void DeleteGroupById(int groupId);
		DepartmentGroup Update(DepartmentGroup departmentGroup);
		bool IsUserInAGroup(string userId, int departmentId);
		bool IsUserInAGroup(string userId, int excludedGroupId, int departmentId);
		void DeleteUserFromGroups(string userId, int departmentId);
		List<DepartmentGroup> GetAllGroupsForDepartmentUnlimited(int departmentId);
		List<DepartmentGroup> GetAllChildDepartmentGroups(int parentDepartmentGroupId);
		List<DepartmentGroup> GetAllStationGroupsForDepartment(int departmentId);
		DepartmentGroup GetGroupForUser(string userId, int departmentId);
		List<string> AllGroupedUserIdsForDepartment(int departmentId);
		Coordinates GetMapCenterCoordinatesForGroup(int departmentGroupId);
		bool MoveUserIntoGroup(string userId, int groupId, bool isAdmin, int departmentId);
		List<DepartmentGroupMember> GetAllMembersForGroup(int groupId);
		DepartmentGroup GetGroupByDispatchEmailCode(string code);
		List<DepartmentGroupMember> GetAllAdminsForGroup(int groupId);
		Dictionary<string, DepartmentGroup> GetAllDepartmentGroupsForDepartment(int departmentId);
		DepartmentGroup GetGroupByMessageEmailCode(string code);
		bool IsUserAGroupAdmin(string userId, int departmentId);
		List<DepartmentGroupMember> GetAllMembersForGroupAndChildGroups(DepartmentGroup group);
		List<IdentityUser> GetAllUsersForGroup(int groupId);
		void InvalidateGroupInCache(int groupId);
		Task<List<DepartmentGroup>> GetAllGroupsForDepartmentUnlimitedAsync(int departmentId);
		Task<List<DepartmentGroup>> GetAllGroupsForDepartmentAsync(int departmentId);
		DepartmentGroupMember GetGroupMemberForUser(string userId, int departmentId);
		DepartmentGroupMember SaveGroupMember(DepartmentGroupMember depMember);
	}
}
