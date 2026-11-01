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
	/// records.analytics (plan §7.8): IRecordsAnalyticsService.GetExecutiveSummaryAsync over the last N days (group-scoped viewers
	/// get figures over the Records they may open, as on the Analytics page).
	/// </summary>
	[DashboardWidget("records.analytics")]
	public class RecordsAnalyticsWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IRecordsAnalyticsService _recordsAnalyticsService;

		public RecordsAnalyticsWidgetProvider(IRecordsAnalyticsService recordsAnalyticsService)
		{
			_recordsAnalyticsService = recordsAnalyticsService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var days = context.GetInt("days", RecordsAnalyticsLimits.DefaultWindowDays);
			var query = new RecordsAnalyticsQuery { Start = context.NowUtc.AddDays(-days), End = context.NowUtc };
			var summary = await RecordsWidgetSupport.RunAsync(() => _recordsAnalyticsService.GetExecutiveSummaryAsync(context.DepartmentId, context.UserId, query, cancellationToken))
				?? new RecordsExecutiveSummary();

			return new RecordsAnalyticsWidgetData
			{
				StartUtc = summary.Start == default ? query.Start.Value : summary.Start,
				EndUtc = summary.End == default ? query.End.Value : summary.End,
				GroupScoped = summary.GroupScoped,
				Truncated = summary.Truncated,
				OverdueNow = summary.OverdueNow,
				WentOverdue = summary.WentOverdue,
				OpenDrafts = summary.OpenDrafts,
				AwaitingReview = summary.AwaitingReview,
				MedianHoursToFinalize = summary.MedianHoursToFinalize,
				MedianReviewHours = summary.MedianReviewHours,
				ReturnRatePercent = summary.ReturnRatePercent,
				Kpis = (summary.Kpis ?? new List<RecordsKpi>()).Where(k => k != null).Select(k => new RecordsAnalyticsKpi
				{
					Key = k.Key, Current = k.Current, Prior = k.Prior, ChangePercent = k.ChangePercent
				}).ToList(),
				ByMonth = (summary.ByMonth ?? new List<RecordsAnalyticsCount>()).Where(m => m != null).Select(m => new RecordsAnalyticsPoint
				{
					Key = m.Key, Label = RecordsWidgetSupport.SafeText(m.Label), Count = m.Count
				}).ToList()
			};
		}
	}
}
