using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <inheritdoc cref="ICommandAccessService" />
	public class CommandAccessService : PermissionGateServiceBase, ICommandAccessService
	{
		public CommandAccessService(
			IPermissionsService permissionsService,
			IDepartmentsService departmentsService,
			IDepartmentGroupsService departmentGroupsService,
			IPersonnelRolesService personnelRolesService,
			ICacheProvider cacheProvider)
			: base(permissionsService, departmentsService, departmentGroupsService, personnelRolesService, cacheProvider)
		{
		}

		protected override PermissionTypes PermissionType => PermissionTypes.CommandAppLogin;

		protected override string CacheKeyPrefix => "commandaccess";

		public Task<bool> CanUseCommandAsync(int departmentId, string userId) => IsAllowedAsync(departmentId, userId);

		public Task<List<string>> GetCommandUserIdsAsync(int departmentId) => GetAllowedUserIdsAsync(departmentId);

		public async Task<bool> CanAssistWithCommandAsync(int departmentId, string userId)
			=> await IsRestrictedAsync(departmentId) && await IsAllowedAsync(departmentId, userId);

		public Task<bool> CanViewCommandBoardsAsync(int departmentId, string userId)
			=> IsAllowedAsync(PermissionTypes.ViewCommandBoards, "commandview", departmentId, userId);

		public Task<bool> IsActiveDepartmentMemberAsync(int departmentId, string userId) => IsActiveMemberAsync(departmentId, userId);

		/// <summary>
		/// ViewCommandBoards with no row of its own follows the CommandAppLogin row: before it existed, CommandAppLogin also
		/// decided who reads boards, so a department that narrowed command keeps exactly the same readers on upgrade.
		/// </summary>
		protected override async Task<Permission> GetEffectivePermissionAsync(int departmentId, PermissionTypes permissionType)
		{
			var permission = await base.GetEffectivePermissionAsync(departmentId, permissionType);
			if (permission == null && permissionType == PermissionTypes.ViewCommandBoards)
				return await base.GetEffectivePermissionAsync(departmentId, PermissionTypes.CommandAppLogin);

			return permission;
		}
	}
}
