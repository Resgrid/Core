using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Business;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Business
{
	/// <summary>
	/// deployments.timeReports (plan §7.9): approvers' queue (TimeReports_Approve gate, the approve action's rule). One aggregate read
	/// (ITimeTrackingService.GetTimeReportCountsAsync).
	/// </summary>
	[DashboardWidget("deployments.timeReports")]
	public class DeploymentTimeReportsWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly ITimeTrackingService _timeTrackingService;

		public DeploymentTimeReportsWidgetProvider(ITimeTrackingService timeTrackingService)
		{
			_timeTrackingService = timeTrackingService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var counts = await _timeTrackingService.GetTimeReportCountsAsync(context.DepartmentId) ?? new DeploymentTimeReportCounts();

			return new DeploymentTimeReportsWidgetData
			{
				AwaitingApproval = counts.AwaitingApproval,
				ApprovedUnbilled = counts.ApprovedUnbilled,
				OldestAwaitingOn = counts.OldestAwaitingOn,
				OldestUnbilledApprovedOn = counts.OldestUnbilledApprovedOn
			};
		}
	}
}
