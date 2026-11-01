using System;
using System.Collections.Generic;
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
	/// checklists.due (plan §7.7): the Due page's first page (IChecklistsService.DueAsync — the next seven days, target
	/// authorization and assignment exactly as the page), counted and optionally narrowed to the viewer's assignments.
	/// </summary>
	[DashboardWidget("checklists.due")]
	public class ChecklistsDueWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IChecklistsService _checklistsService;

		public ChecklistsDueWidgetProvider(IChecklistsService checklistsService)
		{
			_checklistsService = checklistsService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var maxRows = context.GetInt("maxRows", 5);
			var assignedOnly = context.GetBool("assignedOnly", true);
			var actor = ReadinessWidgetSupport.ChecklistActor(context);

			var page = await ReadinessWidgetSupport.ChecklistsAsync(() => _checklistsService.DueAsync(actor, 0, includeNext: true)) ?? new List<ChecklistOccurrenceView>();
			var data = new ChecklistsDueWidgetData { HasMore = page.Count > 50, AssignedOnly = assignedOnly };
			var now = context.NowUtc;

			var rows = page.Take(50).Where(v => v?.Occurrence != null && (!assignedOnly || v.Assigned)).Select(v => Item(v, now)).ToList();
			data.Overdue = rows.Count(r => r.Overdue);
			data.DueNow = rows.Count(r => !r.Overdue && r.PeriodStartUtc.HasValue && r.PeriodStartUtc <= now);
			data.Upcoming = rows.Count(r => !r.Overdue && (!r.PeriodStartUtc.HasValue || r.PeriodStartUtc > now));
			data.Total = rows.Count;
			data.NextDueUtc = rows.Where(r => !r.Overdue && r.PeriodStartUtc > now).Select(r => r.PeriodStartUtc).OrderBy(d => d).FirstOrDefault();

			data.Items = rows.OrderBy(r => r.Overdue ? 0 : r.PeriodStartUtc <= now ? 1 : 2)
				.ThenBy(r => r.WindowEndUtc ?? r.PeriodStartUtc ?? DateTime.MaxValue)
				.Take(maxRows).ToList();
			return data;
		}

		private static ChecklistsDueItem Item(ChecklistOccurrenceView view, DateTime now)
		{
			var occurrence = view.Occurrence;
			var overdue = occurrence.State == (int)ChecklistOccurrenceState.Missed || occurrence.MissedOn.HasValue || occurrence.WindowEndUtc.HasValue && occurrence.WindowEndUtc < now;
			var dueNow = !overdue && occurrence.PeriodStartUtc.HasValue && occurrence.PeriodStartUtc <= now;

			return new ChecklistsDueItem
			{
				OccurrenceId = occurrence.Id,
				Name = ReadinessWidgetSupport.SafeText(view.Name),
				TargetName = ReadinessWidgetSupport.SafeText(view.Target?.Name),
				TargetType = (int)(view.Target?.Type ?? (ChecklistTargetType)occurrence.TargetType),
				State = occurrence.State,
				PeriodStartUtc = occurrence.PeriodStartUtc,
				WindowEndUtc = occurrence.WindowEndUtc,
				Overdue = overdue,
				CanStart = view.CanStart,
				Assigned = view.Assigned,
				Tone = overdue ? "danger" : dueNow ? "warning" : "info"
			};
		}
	}
}
