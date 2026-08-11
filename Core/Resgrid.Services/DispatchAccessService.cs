using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <inheritdoc cref="IDispatchAccessService" />
	public class DispatchAccessService : PermissionGateServiceBase, IDispatchAccessService
	{
		public DispatchAccessService(
			IPermissionsService permissionsService,
			IDepartmentsService departmentsService,
			IDepartmentGroupsService departmentGroupsService,
			IPersonnelRolesService personnelRolesService,
			ICacheProvider cacheProvider)
			: base(permissionsService, departmentsService, departmentGroupsService, personnelRolesService, cacheProvider)
		{
		}

		protected override PermissionTypes PermissionType => PermissionTypes.DispatchAppLogin;

		protected override string CacheKeyPrefix => "dispatchaccess";

		public Task<bool> CanUseDispatchAsync(int departmentId, string userId) => IsAllowedAsync(departmentId, userId);

		public Task<List<string>> GetDispatchUserIdsAsync(int departmentId) => GetAllowedUserIdsAsync(departmentId);
	}
}
