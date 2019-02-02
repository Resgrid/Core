using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class PermissionsService: IPermissionsService
	{
		private readonly IUsersService _usersService;
		private readonly IGenericDataRepository<Permission> _permissionsRepository;

		public PermissionsService(IGenericDataRepository<Permission> permissionsRepository, IUsersService usersService)
		{
			_permissionsRepository = permissionsRepository;
			_usersService = usersService;
		}

		public List<Permission> GetAllPermissionsForDepartment(int departmentId)
		{
			return _permissionsRepository.GetAll().Where(x => x.DepartmentId == departmentId).ToList();
		}

		public Permission GetPermisionByDepartmentType(int departmentId, PermissionTypes type)
		{
			return _permissionsRepository.GetAll().FirstOrDefault(x => x.DepartmentId == departmentId && x.PermissionType == (int)type);
		}

		public Permission SetPermissionForDepartment(int departmentId, string userId, PermissionTypes type, PermissionActions action, string data, bool lockToGroup)
		{
			var permission = GetPermisionByDepartmentType(departmentId, type) ?? new Permission();

			permission.DepartmentId = departmentId;
			permission.PermissionType = (int) type;
			permission.Action = (int)action;
			permission.Data = data;
			permission.LockToGroup = lockToGroup;
			permission.UpdatedBy = userId;
			permission.UpdatedOn = DateTime.UtcNow;

			_permissionsRepository.SaveOrUpdate(permission);

			return permission;
		}

		public bool IsUserAllowed(Permission permission, bool isUserDepartmentAdmin, bool isUserGroupAdmin, List<PersonnelRole> roles)
		{
			if (permission == null)
				return true;

			if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && isUserDepartmentAdmin)
			{
				return true;
			}
			else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && (isUserDepartmentAdmin || isUserGroupAdmin))
			{
				return true;
			}
			else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && isUserDepartmentAdmin)
			{
				return true;
			}
			else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && !isUserDepartmentAdmin)
			{
				if (!String.IsNullOrWhiteSpace(permission.Data))
				{
					var roleIds = permission.Data.Split(char.Parse(",")).Select(int.Parse);
					var role = from r in roles
							   where roleIds.Contains(r.PersonnelRoleId)
							   select r;

					if (role.Any())
					{
						return true;
					}
				}
			}
			else if (permission.Action == (int)PermissionActions.Everyone)
			{
				return true;
			}

			return false;
		}

		public List<string> GetAllowedUsers(Permission permission, int departmentId, int? sourceGroupId, bool isUserDepartmentAdmin, bool isUserGroupAdmin, List<PersonnelRole> roles)
		{
			var allUsers = _usersService.GetUserGroupAndRolesByDepartmentId(departmentId, true, false, false);

			if (permission == null)
				return allUsers.Select(x => x.UserId).ToList();

			if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && isUserDepartmentAdmin)
			{
				return allUsers.Select(x => x.UserId).ToList();
			}
			else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && isUserDepartmentAdmin)
			{
				return allUsers.Select(x => x.UserId).ToList();
			}
			else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && isUserDepartmentAdmin)
			{
				if (permission.LockToGroup)
				{
					return allUsers.Where(x => x.DepartmentGroupId == sourceGroupId).Select(x => x.UserId).ToList();
				}
				else
				{
					return allUsers.Select(x => x.UserId).ToList();
				}
			}
			else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && isUserDepartmentAdmin)
			{
				return allUsers.Select(x => x.UserId).ToList();
			}
			else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles && !isUserDepartmentAdmin)
			{
				var roleIds = permission.Data.Split(char.Parse(",")).Select(int.Parse);
				var role = from r in roles
									 where roleIds.Contains(r.PersonnelRoleId)
									 select r;

				if (role.Any())
				{
					if (permission.LockToGroup)
					{
						return allUsers.Where(x => x.DepartmentGroupId == sourceGroupId).Select(x => x.UserId).ToList();
					}
					else
					{
						return allUsers.Select(x => x.UserId).ToList();
					}
				}
			}
			else if (permission.Action == (int)PermissionActions.Everyone)
			{
				return allUsers.Select(x => x.UserId).ToList();
			}

			return new List<string>();
		}
	}
}
