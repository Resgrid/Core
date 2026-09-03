using System.Collections.Generic;

namespace Resgrid.Model
{
	public class RecordsGroupScopePreviewGroup
	{
		public int GroupId { get; set; }
		public string GroupName { get; set; }
		/// <summary>Members of the group (who keep seeing the group's Records once scoping is on).</summary>
		public int MemberCount { get; set; }
		/// <summary>Records anchored to the group through any anchor type (record group, author, participant, unit, share, call).</summary>
		public int RecordCount { get; set; }
	}

	/// <summary>
	/// What turning RecordsGroupVisibilityMode to GroupScoped would hide (RMS plan section 5.7.1, "Turning it on"):
	/// how many Records become invisible to how many users, by group, plus the legacy rows that stay
	/// department-wide because they have no resolvable anchor. Computed on demand for the Records Settings screen;
	/// nothing here changes state.
	/// </summary>
	public class RecordsGroupScopePreview
	{
		public int TotalRecords { get; set; }

		/// <summary>Records with no group anchor at all; they stay department-wide under scoping.</summary>
		public int RecordsWithoutGroupAnchor { get; set; }

		/// <summary>Legacy Logs rows with a null StationGroupId; never scoped in v1.</summary>
		public int LegacyLogsWithoutGroup { get; set; }

		/// <summary>Legacy UnitLogs rows; their group derives from the unit and they stay department-wide in v1.</summary>
		public int LegacyUnitLogs { get; set; }

		public int UsersInDepartment { get; set; }

		/// <summary>Members of no group: they keep only the always-visible cases (own, participant, unit crew, shared, admin).</summary>
		public int UsersWithoutGroup { get; set; }

		public int DepartmentAdmins { get; set; }

		public List<RecordsGroupScopePreviewGroup> Groups { get; set; } = new List<RecordsGroupScopePreviewGroup>();

		/// <summary>Records that a member of no group can no longer list once scoping is on (always-visible cases aside).</summary>
		public int RecordsHiddenFromUngroupedUsers => TotalRecords - RecordsWithoutGroupAnchor;
	}
}
