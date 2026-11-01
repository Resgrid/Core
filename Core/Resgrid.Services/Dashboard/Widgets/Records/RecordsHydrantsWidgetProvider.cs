using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Records;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Records
{
	/// <summary>
	/// records.hydrants (plan §7.8): IRecordsHydrantsService.GetSummaryAsync — out-of-service and test-due counts (the same queries
	/// the prevention summary uses) and the longest out-of-service hydrants with positions for the mini map. No address, owner,
	/// reason or notes text.
	/// </summary>
	[DashboardWidget("records.hydrants")]
	public class RecordsHydrantsWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IRecordsHydrantsService _recordsHydrantsService;

		public RecordsHydrantsWidgetProvider(IRecordsHydrantsService recordsHydrantsService)
		{
			_recordsHydrantsService = recordsHydrantsService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var maxRows = context.GetInt("maxRows", 25);
			var summary = await RecordsWidgetSupport.RunAsync(() => _recordsHydrantsService.GetSummaryAsync(context.DepartmentId, context.UserId, context.NowUtc, maxRows)) ?? new HydrantSummary();

			return new RecordsHydrantsWidgetData
			{
				OutOfService = summary.OutOfService,
				TestDue = summary.TestDue,
				OutOfServiceHydrants = (summary.OutOfServiceHydrants ?? new List<RmsHydrant>())
					.Where(h => h != null && h.DepartmentId == context.DepartmentId && h.DeletedOn == null && !h.InService)
					.Take(maxRows)
					.Select(h => new RecordsHydrantItem
					{
						HydrantId = h.RmsHydrantId,
						Number = RecordsWidgetSupport.SafeText(h.HydrantNumber),
						Type = h.Type,
						Latitude = h.Latitude,
						Longitude = h.Longitude,
						OutOfServiceSince = h.OutOfServiceSince
					}).ToList()
			};
		}
	}
}
