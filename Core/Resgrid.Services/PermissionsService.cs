using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class PermissionsService : IPermissionsService
	{
		private readonly IUsersService _usersService;
		private readonly IPermissionsRepository _permissionsRepository;

		public PermissionsService(IPermissionsRepository permissionsRepository, IUsersService usersService)
		{
			_permissionsRepository = permissionsRepository;
			_usersService = usersService;
		}

		public async Task<List<Permission>> GetAllPermissionsForDepartmentAsync(int departmentId)
		{
			var items = await _permissionsRepository.GetAllByDepartmentIdAsync(departmentId);

			if (items != null && items.Any())
				return items.ToList();

			return new List<Permission>();
		}

		public async Task<Permission> GetPermissionByDepartmentTypeAsync(int departmentId, PermissionTypes type)
		{
			return await _permissionsRepository.GetPermissionByDepartmentTypeAsync(departmentId, (int)type);
		}

		public async Task<Permission> SetPermissionForDepartmentAsync(int departmentId, string userId,
			PermissionTypes type, PermissionActions action,
			string data, bool lockToGroup, CancellationToken cancellationToken = default(CancellationToken))
		{
			var permission = await GetPermissionByDepartmentTypeAsync(departmentId, type) ?? new Permission();

			permission.DepartmentId = departmentId;
			permission.PermissionType = (int)type;
			permission.Action = (int)action;
			permission.Data = data;
			permission.LockToGroup = lockToGroup;
			permission.UpdatedBy = userId;
			permission.UpdatedOn = DateTime.UtcNow;

			return await _permissionsRepository.SaveOrUpdateAsync(permission, cancellationToken);
		}

		public bool IsUserAllowed(Permission permission, bool isUserDepartmentAdmin, bool isUserGroupAdmin,
			List<PersonnelRole> roles)
		{
			if (permission == null)
				return true;

			if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && isUserDepartmentAdmin)
			{
				return true;
			}
			else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins &&
			         (isUserDepartmentAdmin || isUserGroupAdmin))
			{
				return true;
			}
			else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles &&
			         isUserDepartmentAdmin)
			{
				return true;
			}
			else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles &&
			         !isUserDepartmentAdmin)
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

		public bool IsUserAllowed(Permission permission, int departmentId, int? sourceGroupId, int? userGroupId,
			bool isUserDepartmentAdmin, bool isUserGroupAdmin, List<PersonnelRole> roles)
		{
			if (permission == null)
				return true;

			if (permission.Action == (int)PermissionActions.DepartmentAdminsOnly && isUserDepartmentAdmin)
			{
				return true;
			}
			else if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && isUserDepartmentAdmin)
			{
				return true;
			}
			else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles &&
			         isUserDepartmentAdmin)
			{
				return true;
			}
			else if (permission.Action == (int)PermissionActions.Everyone)
			{
				return true;
			}

			if (permission.LockToGroup)
			{
				if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && isUserGroupAdmin)
				{
					if (sourceGroupId == userGroupId)
					{
						return true;
					}
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles)
				{
					if (sourceGroupId == userGroupId)
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
				}
			}
			else
			{
				if (permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && isUserGroupAdmin)
				{
					return true;
				}
				else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles)
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
			}

			return false;
		}

		public async Task<List<string>> GetAllowedUsersAsync(Permission permission, int departmentId,
			int? sourceGroupId, bool isUserDepartmentAdmin, bool isUserGroupAdmin, List<PersonnelRole> roles)
		{
			var allUsers =
				await _usersService.GetUserGroupAndRolesByDepartmentIdAsync(departmentId, true, false, false);

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
			else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles &&
			         isUserDepartmentAdmin)
			{
				return allUsers.Select(x => x.UserId).ToList();
			}
			else if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles &&
			         !isUserDepartmentAdmin)
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
