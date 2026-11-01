using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Personnel
{
	/// <summary>
	/// personnel.staffingSummary (plan §7.3): the people the viewer can see counted by staffing level, overall and per group,
	/// with who is on duty from the running shifts (IShiftsService.GetOnDutyUserIdsForGroupsAsync).
	/// </summary>
	public sealed class PersonnelStaffingSummaryWidgetData
	{
		public int Total { get; set; }

		/// <summary>Staffing with an Available or On Shift meaning.</summary>
		public int AvailableCount { get; set; }

		/// <summary>People rostered on a running shift; null when no running shift covers any group.</summary>
		public int? OnDutyCount { get; set; }

		/// <summary>Each staffing level in use with its count, largest first.</summary>
		public List<PersonnelSummaryItem> ByStaffing { get; set; } = new List<PersonnelSummaryItem>();

		/// <summary>Per group, by name; ungrouped people are not listed here (they are in <see cref="Total"/>).</summary>
		public List<PersonnelStaffingGroup> ByGroup { get; set; } = new List<PersonnelStaffingGroup>();

		public string PersonnelUrl { get; set; } = "/User/Personnel/Index";
	}

	public sealed class PersonnelStaffingGroup
	{
		public int GroupId { get; set; }

		public string Name { get; set; }

		public int Total { get; set; }

		public int AvailableCount { get; set; }

		/// <summary>Rostered on a running shift for the group; null when no running shift covers it.</summary>
		public int? OnDutyCount { get; set; }
	}
}
