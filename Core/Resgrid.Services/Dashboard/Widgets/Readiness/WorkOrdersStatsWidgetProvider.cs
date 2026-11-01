using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Readiness;
using Resgrid.Model.Services;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Services.Dashboard.Widgets.Readiness
{
	/// <summary>
	/// workOrders.stats (plan §7.7): IWorkOrderReportingService.GetWorkOrderStatsAsync (the Reports page) over the last N days,
	/// scoped by the service to the work orders the viewer may read. Costs are not carried (labour rates are protected).
	/// </summary>
	[DashboardWidget("workOrders.stats")]
	public class WorkOrdersStatsWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IWorkOrderReportingService _workOrderReportingService;

		public WorkOrdersStatsWidgetProvider(IWorkOrderReportingService workOrderReportingService)
		{
			_workOrderReportingService = workOrderReportingService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var days = context.GetInt("days", 90);
			var until = context.NowUtc;
			var query = new WorkOrderReportQuery { FromUtc = until.AddDays(-days), UntilUtc = until };
			var actor = ReadinessWidgetSupport.ChecklistActor(context);

			var stats = await ReadinessWidgetSupport.WorkOrdersAsync(() => _workOrderReportingService.GetWorkOrderStatsAsync(actor, query)) ?? new WorkOrderStats();

			return new WorkOrdersStatsWidgetData
			{
				FromUtc = stats.FromUtc == default ? query.FromUtc.Value : stats.FromUtc,
				UntilUtc = stats.UntilUtc == default ? query.UntilUtc.Value : stats.UntilUtc,
				Total = stats.Total,
				Open = stats.Open,
				Overdue = stats.Overdue,
				PreventiveDue = stats.PreventiveDue,
				PreventiveOnTime = stats.PreventiveOnTime,
				PreventiveCompliancePercent = stats.PreventiveCompliancePercent,
				MeanTimeToRepairHours = stats.MeanTimeToRepairHours,
				DowntimeHours = stats.DowntimeHours,
				ResponseSlaBreaches = stats.ResponseSlaBreaches,
				RepairSlaBreaches = stats.RepairSlaBreaches,
				OpenByPriority = (stats.OpenByPriority ?? new int[4]).ToArray(),
				OpenByAge = (stats.OpenByAge ?? new int[4]).ToArray()
			};
		}
	}
}
