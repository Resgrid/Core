using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Shifts
{
	public class NewShiftView
	{
		public Shift Shift { get; set; }
		public ShiftAssignmentTypes AssignmentType { get; set; }
		public string Dates { get; set; }
		public List<DepartmentGroup> Groups { get; set; }

		/// <summary>Signups and trades on this shift wait for a supervisor (Shift.RequireApproval).</summary>
		public bool RequireApproval { get; set; }

		/// <summary>The caller supervises every group (department admin or department-wide shift manager).</summary>
		public bool CanManageAllGroups { get; set; }

		/// <summary>Groups a group-scoped supervisor may put on the shift; empty when <see cref="CanManageAllGroups"/>.</summary>
		public List<int> ManageableGroupIds { get; set; } = new List<int>();
	}
}
