using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Shifts
{
	public class ShiftsIndexModel
	{
		public List<Shift> Shifts { get; set; }

		/// <summary>Supervises at least one group (or all of them): gets staffing.</summary>
		public bool IsUserAdminOrGroupAdmin { get; set; }

		/// <summary>Supervises a group or holds a shift trade approver role: gets the approvals queue.</summary>
		public bool HasApprovals { get; set; }

		/// <summary>Shifts whose every group the caller manages, so may edit or delete.</summary>
		public HashSet<int> ManageableShiftIds { get; set; } = new HashSet<int>();
		public bool CanUpdateShifts { get; set; }
		public bool CanDeleteShifts { get; set; }
		public int PendingApprovalsCount { get; set; }

		/// <summary>Dispatcher, shift manager or department admin: gets the dispatcher desk board and break planner (M0324).</summary>
		public bool ShowDispatcherDesk { get; set; }

		/// <summary>Department admin: gets the dispatcher desk settings.</summary>
		public bool ShowDispatcherDeskSettings { get; set; }

		/// <summary>Department admin or desk supervisor: gets the dispatcher activity report (the page refuses anyone else).</summary>
		public bool ShowDispatcherActivity { get; set; }
	}
}
