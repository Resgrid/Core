using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Shifts
{
	public class ShiftStaffingView
	{
		public List<Shift> Shifts { get; set; }
		public int ShiftId { get; set; }
		public bool IsDepartmentAdmin { get; set; }
		public int GroupId { get; set; }
		public string Note { get; set; }

		public Dictionary<int, List<UnitStateRole>> CurrentUnitRoles { get; set; }
	}
}
