using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Readiness
{
	/// <summary>checklists.compliance (plan §7.7): the Compliance page's summary over the last N department-local days.</summary>
	public sealed class ChecklistsComplianceWidgetData
	{
		public DateTime FromUtc { get; set; }

		public DateTime UntilUtc { get; set; }

		public int Expected { get; set; }

		public int Completed { get; set; }

		public int OnTime { get; set; }

		public int Missed { get; set; }

		public int Skipped { get; set; }

		/// <summary>Completed / expected, 0–100; null when nothing was expected.</summary>
		public decimal? CompletionRate { get; set; }

		/// <summary>Some results were withheld by the viewer's checklist-results scope (the page's "restricted results" notice).</summary>
		public bool PartiallyRestricted { get; set; }

		/// <summary>Targets (unit, group, person, asset) with the lowest completion first, at most maxRows.</summary>
		public List<ChecklistsComplianceTarget> Targets { get; set; } = new List<ChecklistsComplianceTarget>();

		/// <summary>One point per department-local day that had expected checks.</summary>
		public List<ChecklistsComplianceDay> Trend { get; set; } = new List<ChecklistsComplianceDay>();
	}

	public sealed class ChecklistsComplianceTarget
	{
		/// <summary>ChecklistTargetType.</summary>
		public int TargetType { get; set; }

		public string TargetId { get; set; }

		public string Name { get; set; }

		public int Expected { get; set; }

		public int Completed { get; set; }

		public int Missed { get; set; }

		public decimal? CompletionRate { get; set; }
	}

	public sealed class ChecklistsComplianceDay
	{
		/// <summary>The department-local calendar day (date part only).</summary>
		public DateTime Day { get; set; }

		public int Expected { get; set; }

		public int Missed { get; set; }
	}
}
