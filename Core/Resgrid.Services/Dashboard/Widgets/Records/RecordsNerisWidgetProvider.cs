using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Records;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Records
{
	/// <summary>records.neris (plan §7.8): the Records dashboard's incident report queues (shown there only while NERIS workflows are on).</summary>
	[DashboardWidget("records.neris")]
	public class RecordsNerisWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IRecordsDashboardService _recordsDashboardService;
		private readonly IRecordsAuthorizationService _recordsAuthorizationService;

		public RecordsNerisWidgetProvider(IRecordsDashboardService recordsDashboardService, IRecordsAuthorizationService recordsAuthorizationService)
		{
			_recordsDashboardService = recordsDashboardService;
			_recordsAuthorizationService = recordsAuthorizationService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			await RecordsWidgetSupport.RequireActiveMemberAsync(_recordsAuthorizationService, context);
			var dashboard = await RecordsWidgetSupport.RunAsync(() => _recordsDashboardService.GetAsync(context.DepartmentId, context.UserId, cancellationToken)) ?? new RecordsDashboard();

			return new RecordsNerisWidgetData
			{
				Incomplete = dashboard.IncidentIncomplete,
				AwaitingReview = dashboard.IncidentAwaitingReview,
				Submitted = dashboard.IncidentSubmitted,
				Accepted = dashboard.IncidentAccepted,
				Rejected = dashboard.IncidentRejected
			};
		}
	}
}
