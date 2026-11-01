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
	/// logs.recent (plan §7.8): IWorkLogsService.GetRecentLogSummaryAsync — the department's legacy logs written in the last N
	/// days (one bounded query of header columns; the Logs list shows every department log to Log_View holders), by type.
	/// </summary>
	[DashboardWidget("logs.recent")]
	public class LogsRecentWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IWorkLogsService _workLogsService;

		public LogsRecentWidgetProvider(IWorkLogsService workLogsService)
		{
			_workLogsService = workLogsService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var days = context.GetInt("days", 7);
			var maxRows = context.GetInt("maxRows", 5);
			var since = context.NowUtc.AddDays(-days);
			var summary = await _workLogsService.GetRecentLogSummaryAsync(context.DepartmentId, since, maxRows) ?? new WorkLogSummary { SinceUtc = since };

			return new LogsRecentWidgetData
			{
				SinceUtc = since,
				Total = summary.Total,
				Truncated = summary.Truncated,
				ByType = (summary.CountsByType ?? new Dictionary<int, int>()).OrderByDescending(p => p.Value).ThenBy(p => p.Key)
					.Select(p => new LogsRecentTypeCount { LogType = p.Key, Count = p.Value }).ToList(),
				Recent = (summary.Recent ?? new List<WorkLogHeader>()).Take(maxRows).Select(l => new LogsRecentItem
				{
					LogId = l.LogId,
					LogType = l.LogType ?? 0,
					CallId = l.CallId,
					StartedOn = l.StartedOn,
					LoggedOn = l.LoggedOn
				}).ToList()
			};
		}
	}
}
