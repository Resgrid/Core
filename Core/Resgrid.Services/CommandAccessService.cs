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
	}
}
