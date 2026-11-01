using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.CostRecovery.CalOesMars;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Business;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Business
{
	/// <summary>
	/// costRecovery.queue (plan §7.9). Mirrors CalOesMarsController: the Queue is a field action every member reaches, scoped by the
	/// manager rule (ManageMutualAidReimbursement / admin → agency queue, otherwise the member's own items); the readiness dashboard
	/// needs the view claim, which only the manager permission issues.
	/// </summary>
	[DashboardWidget("costRecovery.queue")]
	public class CostRecoveryQueueWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly ICalOesMarsService _calOesMarsService;
		private readonly IPermissionsService _permissionsService;

		public CostRecoveryQueueWidgetProvider(ICalOesMarsService calOesMarsService, IPermissionsService permissionsService)
		{
			_calOesMarsService = calOesMarsService;
			_permissionsService = permissionsService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var departmentId = context.DepartmentId;
			var max = context.GetInt("maxRows", 5);
			var manager = await DashboardModulePermissions.HoldsAnyAsync(_permissionsService, context.Viewer, PermissionTypes.ManageMutualAidReimbursement);

			var items = (await _calOesMarsService.GetActionQueueAsync(departmentId, context.UserId, manager) ?? new List<CalOesMarsQueueItem>())
				.Where(i => i?.WorkItem != null && i.WorkItem.DepartmentId == departmentId && (manager || i.IsMine)).ToList();

			var data = new CostRecoveryQueueWidgetData
			{
				ManagerScope = manager,
				Total = items.Count,
				Mine = items.Count(i => i.IsMine),
				WithErrors = items.Count(i => i.ErrorCount > 0),
				Rows = items.OrderByDescending(i => i.AgeDays).Take(max).Select(i => new CostRecoveryQueueRow
				{
					WorkItemId = i.WorkItem.CalOesMarsWorkItemId,
					RecordType = Enum.GetName(typeof(CalOesMarsRecordTypes), i.WorkItem.RecordType) ?? i.WorkItem.RecordType.ToString(),
					LocalState = Enum.GetName(typeof(CalOesMarsLocalStates), i.WorkItem.LocalState) ?? i.WorkItem.LocalState.ToString(),
					DeploymentName = i.DeploymentName,
					IncidentNumber = i.IncidentNumber,
					RequestNumber = i.RequestNumber,
					ErrorCount = i.ErrorCount,
					WarningCount = i.WarningCount,
					AgeDays = i.AgeDays,
					IsMine = i.IsMine
				}).ToList()
			};

			if (manager)
			{
				var readiness = await _calOesMarsService.GetAgencyReadinessAsync(departmentId, DashboardDepartmentClock.Today(context));
				if (readiness != null)
				{
					var readinessItems = readiness.Items ?? new List<CalOesMarsReadinessItem>();
					data.Readiness = new CostRecoveryReadiness
					{
						IsReady = readiness.IsReady,
						Blockers = readinessItems.Count(r => r.Severity == (int)CalOesMarsReadinessSeverities.Blocker),
						Warnings = readinessItems.Count(r => r.Severity == (int)CalOesMarsReadinessSeverities.Warning),
						AuthorityProfileCurrent = readiness.AuthorityProfileCurrent,
						OpenWorkItems = readiness.OpenWorkItems,
						ReturnedWorkItems = readiness.ReturnedWorkItems,
						InvoicesAwaitingLocalApproval = readiness.InvoicesAwaitingLocalApproval
					};
				}
			}

			return data;
		}
	}
}
