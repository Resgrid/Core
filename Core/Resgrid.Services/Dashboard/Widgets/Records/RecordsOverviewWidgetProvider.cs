using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Records;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Records
{
	/// <summary>records.overview (plan §7.8): IRecordsDashboardService.GetAsync — the Records dashboard's group-scope-aware queues.</summary>
	[DashboardWidget("records.overview")]
	public class RecordsOverviewWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IRecordsDashboardService _recordsDashboardService;
		private readonly IRecordsAuthorizationService _recordsAuthorizationService;

		public RecordsOverviewWidgetProvider(IRecordsDashboardService recordsDashboardService, IRecordsAuthorizationService recordsAuthorizationService)
		{
			_recordsDashboardService = recordsDashboardService;
			_recordsAuthorizationService = recordsAuthorizationService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			await RecordsWidgetSupport.RequireActiveMemberAsync(_recordsAuthorizationService, context);
			var dashboard = await RecordsWidgetSupport.RunAsync(() => _recordsDashboardService.GetAsync(context.DepartmentId, context.UserId, cancellationToken)) ?? new RecordsDashboard();

			return new RecordsOverviewWidgetData
			{
				Drafts = dashboard.OperationalDrafts,
				AwaitingReview = dashboard.OperationalAwaitingReview,
				Returned = dashboard.OperationalReturned,
				Overdue = dashboard.Overdue,
				IncidentIncomplete = dashboard.IncidentIncomplete,
				IncidentAwaitingReview = dashboard.IncidentAwaitingReview,
				DisclosuresOpen = dashboard.DisclosuresOpen,
				DisclosuresOverdue = dashboard.DisclosuresOverdue,
				WarningCount = dashboard.Warnings?.Count ?? 0
			};
		}
	}
}
