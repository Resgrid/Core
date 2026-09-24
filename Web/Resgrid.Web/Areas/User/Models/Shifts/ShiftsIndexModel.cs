using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Shifts
{
	public class ShiftsIndexModel
	{
		public List<Shift> Shifts { get; set; }

		/// <summary>Supervises at least one group (or all of them): gets staffing and approvals.</summary>
		public bool IsUserAdminOrGroupAdmin { get; set; }

		/// <summary>Shifts whose every group the caller manages, so may edit or delete.</summary>
		public HashSet<int> ManageableShiftIds { get; set; } = new HashSet<int>();
		public bool CanUpdateShifts { get; set; }
		public bool CanDeleteShifts { get; set; }
		public int PendingApprovalsCount { get; set; }
	}
}
