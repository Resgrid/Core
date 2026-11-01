using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// The personnel status table: the department's groups, each with the people the viewer may see (visibility matrix;
	/// disabled and hidden members left out), their latest status and staffing, sorted by the department's status weights
	/// and personnel sort order. One trailing group with a null <see cref="PersonnelStatusBoardGroup.Group"/> holds the
	/// ungrouped people. Built by IPersonnelStatusBoardService for the classic dashboard's table and the
	/// personnel.board widget, so both always list the same people in the same order.
	/// </summary>
	public class PersonnelStatusBoard
	{
		public Department Department { get; set; }

		/// <summary>Latest status per person inside the auto-available window (IActionLogsService.GetLastActionLogsForDepartmentAsync).</summary>
		public List<ActionLog> LastUserActionlogs { get; set; } = new List<ActionLog>();

		public List<DepartmentGroup> DepartmentGroups { get; set; } = new List<DepartmentGroup>();

		public List<DepartmentGroup> Stations { get; set; } = new List<DepartmentGroup>();

		/// <summary>The viewer's own group, or null.</summary>
		public DepartmentGroup UsersGroup { get; set; }

		/// <summary>The department's active personnel statuses, or null when it uses the built-in ones.</summary>
		public CustomState States { get; set; }

		/// <summary>The department's active staffing levels, or null when it uses the built-in ones.</summary>
		public CustomState StaffingLevels { get; set; }

		/// <summary>What a person with no status inside the auto-available hour shows: the department's Available status.</summary>
		public CustomStateDetail DefaultStatus { get; set; }

		/// <summary>Disabled or hidden members, who are never listed.</summary>
		public List<string> ExcludedUsers { get; set; } = new List<string>();

		/// <summary>The staffing row each grouped person was listed with (auto-generated Available when they have none).</summary>
		public IDictionary<string, UserState> DepartmentUserStates { get; set; } = new Dictionary<string, UserState>();

		/// <summary>The ungrouped people listed.</summary>
		public List<string> UnGroupedUsers { get; set; } = new List<string>();

		/// <summary>One entry per department group (in DepartmentGroups order), then the ungrouped group (Group null).</summary>
		public List<PersonnelStatusBoardGroup> Groups { get; set; } = new List<PersonnelStatusBoardGroup>();
	}

	public class PersonnelStatusBoardGroup
	{
		/// <summary>The department group; null for the ungrouped people.</summary>
		public DepartmentGroup Group { get; set; }

		public List<PersonnelStatusBoardEntry> Entries { get; set; } = new List<PersonnelStatusBoardEntry>();
	}

	public class PersonnelStatusBoardEntry
	{
		/// <summary>The department's sort weight for the person's status (9000 when the status has none).</summary>
		public int Weight { get; set; }

		public UserGroupRole UserInfo { get; set; }

		/// <summary>The person's latest status, or null when they set none inside the auto-available window.</summary>
		public ActionLog CurrentStatus { get; set; }

		/// <summary>The person's latest staffing; an auto-generated Available row (UserStateId 0) when they have none.</summary>
		public UserState CurrentStaffing { get; set; }
	}
}
