using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.BadgeCounts
{
	/// <summary>
	/// Open work orders assigned to the member or a role they hold — v4 WorkOrders/GetWorkOrders?AssignedToMe, counted.
	/// Behind Maintenance.WorkOrders and the Maintenance module switch.
	/// </summary>
	public class WorkOrdersBadgeCountProvider : ModuleBadgeCountProvider
	{
		private readonly IWorkOrderCountRepository _workOrderCountRepository;
		private readonly IPersonnelRolesService _personnelRolesService;

		public WorkOrdersBadgeCountProvider(IWorkOrderCountRepository workOrderCountRepository, IPersonnelRolesService personnelRolesService, IFeatureToggleService featureToggleService)
			: base(featureToggleService)
		{
			_workOrderCountRepository = workOrderCountRepository;
			_personnelRolesService = personnelRolesService;
		}

		public override string Key => BadgeCountKeys.WorkOrders;

		public override string RequiredPolicy => null;

		protected override string RequiredFlag => FeatureFlagKeys.MaintenanceWorkOrders;

		protected override bool IsDisabled(DepartmentModuleSettings settings) => settings.MaintenanceDisabled;

		protected override async Task<BadgeCount> CountAsync(BadgeCountContext context)
		{
			var roles = await _personnelRolesService.GetRolesForUserAsync(context.UserId, context.DepartmentId);
			var roleIds = roles?.Where(role => role != null).Select(role => role.PersonnelRoleId).Distinct().ToArray() ?? new int[0];

			return new BadgeCount(Key, await _workOrderCountRepository.CountOpenAssignedAsync(context.DepartmentId, context.UserId, roleIds));
		}
	}
}
