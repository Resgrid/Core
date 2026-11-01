using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Readiness
{
	/// <summary>checklists.due (plan §7.7): scheduled checklist occurrences in the next seven days (the Due page's rows).</summary>
	public sealed class ChecklistsDueWidgetData
	{
		/// <summary>Open now (period started, window not over).</summary>
		public int DueNow { get; set; }

		/// <summary>Missed or past their window and not submitted.</summary>
		public int Overdue { get; set; }

		/// <summary>Period not started yet.</summary>
		public int Upcoming { get; set; }

		public int Total { get; set; }

		/// <summary>More occurrences exist than the Due page's first page; the counts cover that page.</summary>
		public bool HasMore { get; set; }

		/// <summary>Start of the soonest upcoming period among the listed occurrences.</summary>
		public DateTime? NextDueUtc { get; set; }

		/// <summary>The widget lists only occurrences the viewer is assigned to (setting assignedOnly).</summary>
		public bool AssignedOnly { get; set; }

		public List<ChecklistsDueItem> Items { get; set; } = new List<ChecklistsDueItem>();
	}

	public sealed class ChecklistsDueItem
	{
		/// <summary>Opens /User/Checklists/Occurrence?id={OccurrenceId} (its Start button posts StartOccurrence).</summary>
		public string OccurrenceId { get; set; }

		public string Name { get; set; }

		public string TargetName { get; set; }

		/// <summary>ChecklistTargetType.</summary>
		public int TargetType { get; set; }

		/// <summary>ChecklistOccurrenceState: 0 in progress, 1 awaiting witness, 3 scheduled, 4 missed.</summary>
		public int State { get; set; }

		public DateTime? PeriodStartUtc { get; set; }

		public DateTime? WindowEndUtc { get; set; }

		public bool Overdue { get; set; }

		public bool CanStart { get; set; }

		public bool Assigned { get; set; }

		/// <summary>"danger" overdue, "warning" due now, "info" upcoming.</summary>
		public string Tone { get; set; }
	}
}
