using System;
using System.Collections.Generic;
using Resgrid.Model.Dashboard.Widgets.Operations;

namespace Resgrid.Model.Dashboard.Widgets.Personnel
{
	/// <summary>
	/// personnel.board (plan §7.3): the classic personnel status table (IPersonnelStatusBoardService: same people, groups and
	/// order). The staffing note is ADP-catalogued (userstates.note).
	/// </summary>
	public sealed class PersonnelBoardWidgetData
	{
		/// <summary>Top-level groups in board order, each followed by its child groups; the ungrouped people last (GroupId null).</summary>
		public List<PersonnelBoardGroup> Groups { get; set; } = new List<PersonnelBoardGroup>();

		public int TotalCount { get; set; }

		/// <summary>The department uses its own personnel statuses (the classic table then offers no reset buttons).</summary>
		public bool CustomStatuses { get; set; }

		/// <summary>Department admins: reset everyone to the department's Available status.</summary>
		public bool CanResetAll { get; set; }

		/// <summary>POST /User/Home/ResetAllToStandingBy (no parameters; redirects to the dashboard).</summary>
		public DashboardWidgetEndpoint ResetAll { get; set; }

		/// <summary>POST /User/Home/ResetGroupToStandingBy — groupId (query; redirects to the dashboard).</summary>
		public DashboardWidgetEndpoint ResetGroup { get; set; }

		/// <summary>The groups filter named groups that are no longer the department's; they were ignored.</summary>
		public bool GroupFilterIgnored { get; set; }
	}

	public sealed class PersonnelBoardGroup
	{
		/// <summary>The department group; null for the ungrouped people.</summary>
		public int? GroupId { get; set; }

		public int? ParentGroupId { get; set; }

		/// <summary>The group's name; null for the ungrouped people (the client labels it).</summary>
		public string Name { get; set; }

		/// <summary>The viewer's own group (highlighted, as on the classic table).</summary>
		public bool IsViewerGroup { get; set; }

		/// <summary>The viewer may reset this group (department or group admin, and the department uses the built-in statuses).</summary>
		public bool CanReset { get; set; }

		public List<PersonnelBoardRow> People { get; set; } = new List<PersonnelBoardRow>();
	}

	public sealed class PersonnelBoardRow
	{
		public string UserId { get; set; }

		public string Name { get; set; }

		public string RoleNames { get; set; }

		/// <summary>The latest status, or the department's Available status when none was set inside the auto-available window.</summary>
		public DashboardStatusChip Status { get; set; }

		/// <summary>When the status was set; null when the default is shown.</summary>
		public DateTime? StatusOnUtc { get; set; }

		/// <summary>The station's name the status points at; null for a call (see <see cref="StatusDestinationId"/>) or no destination.</summary>
		public string StatusDestination { get; set; }

		/// <summary>DestinationEntityTypes of the status's destination: 0 none, 1 station, 2 call, 3 POI.</summary>
		public int StatusDestinationType { get; set; }

		/// <summary>The destination's id (a call id for "Responding to call #…").</summary>
		public int? StatusDestinationId { get; set; }

		public DashboardStatusChip Staffing { get; set; }

		/// <summary>When the staffing was set; null for an auto-generated Available.</summary>
		public DateTime? StaffingOnUtc { get; set; }

		/// <summary>The staffing note (ADP userstates.note): plain, a shield when protected, or null when there is none.</summary>
		public DashboardProtectedText StaffingNote { get; set; }

		/// <summary>The viewer may open the person's profile (department admin, or admin of the row's group).</summary>
		public bool CanEdit { get; set; }

		public string Url { get; set; }
	}
}
