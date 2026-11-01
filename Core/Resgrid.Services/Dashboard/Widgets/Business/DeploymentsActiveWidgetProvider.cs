using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Business;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Business
{
	/// <summary>
	/// deployments.active (plan §7.9). Mirrors DeploymentsController.Index: the page has no policy beyond the Operations.Deployments
	/// flag; a manager (ManageDeployments → Deployments_View, admins) sees the department's deployments, every other member only the
	/// ones they are rostered on. Rows: mine first. Queries: permissions, mine, and for a manager the open count and list.
	/// </summary>
	[DashboardWidget("deployments.active")]
	public class DeploymentsActiveWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IDeploymentService _deploymentService;
		private readonly IPermissionsService _permissionsService;

		public DeploymentsActiveWidgetProvider(IDeploymentService deploymentService, IPermissionsService permissionsService)
		{
			_deploymentService = deploymentService;
			_permissionsService = permissionsService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var departmentId = context.DepartmentId;
			var max = context.GetInt("maxRows", 5);
			var canViewAll = await DashboardModulePermissions.HoldsAnyAsync(_permissionsService, context.Viewer, PermissionTypes.ManageDeployments);

			var mine = (await _deploymentService.GetDeploymentsForUserAsync(departmentId, context.UserId, true) ?? new List<Deployment>())
				.Where(d => d.DepartmentId == departmentId && !d.IsDeleted && d.IsOpen).ToList();
			var mineIds = new HashSet<string>(mine.Select(d => d.DeploymentId), StringComparer.OrdinalIgnoreCase);

			var data = new DeploymentsActiveWidgetData { CanViewAll = canViewAll, MineCount = mine.Count, OpenCount = mine.Count };
			var candidates = mine;
			if (canViewAll)
			{
				data.OpenCount = await _deploymentService.CountDeploymentsForDepartmentAsync(departmentId, true);
				var department = await _deploymentService.GetDeploymentsForDepartmentAsync(departmentId, true, 0, max + mine.Count) ?? new List<Deployment>();
				candidates = mine.Concat(department.Where(d => d.DepartmentId == departmentId && !d.IsDeleted && !mineIds.Contains(d.DeploymentId))).ToList();
			}

			data.Rows = candidates
				.OrderByDescending(d => mineIds.Contains(d.DeploymentId))
				.ThenByDescending(d => d.StartOn ?? d.AddedOn)
				.Take(max)
				.Select(d => new DeploymentsActiveRow
				{
					DeploymentId = d.DeploymentId,
					Name = d.Name,
					IncidentNumber = d.IncidentNumber,
					Status = d.Status,
					StatusName = Enum.GetName(typeof(DeploymentStatuses), d.Status) ?? d.Status.ToString(),
					StartOn = d.StartOn,
					DaysDeployed = d.StartOn.HasValue && d.StartOn.Value <= context.NowUtc ? (int)(context.NowUtc - d.StartOn.Value).TotalDays : null,
					IsMine = mineIds.Contains(d.DeploymentId)
				}).ToList();

			return data;
		}
	}
}
