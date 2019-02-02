using System;
using System.Collections.Generic;

namespace Resgrid.Model.Services
{
	public interface IPermissionsService
	{
		List<Permission> GetAllPermissionsForDepartment(int departmentId);
		Permission SetPermissionForDepartment(int departmentId, string userId, PermissionTypes type, PermissionActions action, string data, bool lockToGroup);
		Permission GetPermisionByDepartmentType(int departmentId, PermissionTypes type);
		bool IsUserAllowed(Permission permission, bool isUserDepartmentAdmin, bool isUserGroupAdmin, List<PersonnelRole> roles);
		List<string> GetAllowedUsers(Permission permission, int departmentId, int? sourceGroupId, bool isUserDepartmentAdmin, bool isUserGroupAdmin, List<PersonnelRole> roles);
	}
}