using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Checklists;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Readiness;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Readiness
{
	/// <summary>
	/// checklists.compliance (plan §7.7): IChecklistsService.GetComplianceSummaryAsync over the last N department-local days, the
	/// window the Compliance page opens with (today − N .. tomorrow). Results outside the viewer's checklist-results scope are
	/// withheld by the service; the trend is bucketed by department-local day like the page.
	/// </summary>
	[DashboardWidget("checklists.compliance")]
	public class ChecklistsComplianceWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IChecklistsService _checklistsService;

		public ChecklistsComplianceWidgetProvider(IChecklistsService checklistsService)
		{
			_checklistsService = checklistsService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var days = context.GetInt("days", 30);
			var maxRows = context.GetInt("maxRows", 5);
			var zone = ReadinessWidgetSupport.DepartmentZone(context);
			var today = ReadinessWidgetSupport.DepartmentToday(context, zone);
			var query = new ChecklistReportQuery
			{
				FromUtc = DateTime.SpecifyKind(ReadinessWidgetSupport.ToUtc(today.AddDays(-days), zone), DateTimeKind.Utc),
				UntilUtc = DateTime.SpecifyKind(ReadinessWidgetSupport.ToUtc(today.AddDays(1), zone), DateTimeKind.Utc)
			};

			var actor = ReadinessWidgetSupport.ChecklistActor(context);
			var report = await ReadinessWidgetSupport.ChecklistsAsync(() => _checklistsService.GetComplianceSummaryAsync(actor, query)) ?? new ChecklistComplianceSummary();
			var groups = report.Groups ?? new System.Collections.Generic.List<ChecklistComplianceGroup>();
			var entries = report.Entries ?? new System.Collections.Generic.List<ChecklistReportEntry>();

			var data = new ChecklistsComplianceWidgetData
			{
				FromUtc = query.FromUtc,
				UntilUtc = query.UntilUtc,
				Expected = groups.Sum(g => g.Expected),
				Completed = groups.Sum(g => g.Completed),
				OnTime = groups.Sum(g => g.OnTime),
				Missed = groups.Sum(g => g.Missed),
				Skipped = groups.Sum(g => g.Skipped),
				PartiallyRestricted = report.IsRedacted || report.UnavailableSources?.Count > 0
			};
			data.CompletionRate = data.Expected == 0 ? null : Math.Round(100m * data.Completed / data.Expected, 1);

			data.Targets = groups.Where(g => g.Expected > 0)
				.OrderBy(g => g.CompletionRate ?? 100m).ThenByDescending(g => g.Missed).ThenBy(g => g.Target?.Name, StringComparer.OrdinalIgnoreCase)
				.Take(maxRows)
				.Select(g => new ChecklistsComplianceTarget
				{
					TargetType = (int)(g.Target?.Type ?? 0),
					TargetId = g.Target?.Id,
					Name = ReadinessWidgetSupport.SafeText(g.Target?.Name),
					Expected = g.Expected,
					Completed = g.Completed,
					Missed = g.Missed,
					CompletionRate = g.CompletionRate
				}).ToList();

			// Buckets are department-local calendar days (ChecklistsController.ComplianceView): shifting UTC midnight would mislabel them.
			data.Trend = entries.Where(e => e.Expected && e.DueUtc.HasValue)
				.GroupBy(e => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(e.DueUtc.Value, DateTimeKind.Utc), zone).Date)
				.Select(g => new ChecklistsComplianceDay { Day = g.Key, Expected = g.Count(), Missed = g.Count(e => e.Missed) })
				.OrderBy(d => d.Day).ToList();

			return data;
		}
	}
}
