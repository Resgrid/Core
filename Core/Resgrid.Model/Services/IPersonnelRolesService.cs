using System;
using System.Collections.Generic;

namespace Resgrid.Model.Services
{
	public interface IPersonnelRolesService
	{
		List<PersonnelRole> GetRolesForDepartment(int departmentId);
		PersonnelRole GetRoleById(int roleId);
		PersonnelRole SaveRole(PersonnelRole role);
		PersonnelRole GetRoleByDepartmentAndName(int departmentId, string name);
		void DeleteRoleById(int roleId);
		void DeleteRoleUsers(List<PersonnelRoleUser> users);
		List<PersonnelRole> GetRolesForUser(string userId, int departmentId);
		void RemoveUserFromAllRoles(string userId, int departmentId);
		List<PersonnelRole> GetRolesForDepartmentUnlimited(int departmentId);
		void SetRolesForUser(int departmentId, string userId, string[] roleIds);
		List<PersonnelRoleUser> GetAllMembersOfRole(int roleId);
		Dictionary<string, List<PersonnelRole>> GetAllRolesForUsersInDepartment(int departemntId);
		List<PersonnelRole> GetAllRolesForDepartment(int departmentId);
	}
}