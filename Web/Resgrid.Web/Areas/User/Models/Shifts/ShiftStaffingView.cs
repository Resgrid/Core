using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Shifts
{
	public class ShiftStaffingView
	{
		public List<Shift> Shifts { get; set; }
		public int ShiftId { get; set; }

		/// <summary>The caller supervises every group, so also sets the non-group personnel.</summary>
		public bool IsDepartmentAdmin { get; set; }
		public int GroupId { get; set; }
		public string Note { get; set; }

		/// <summary>Groups a group-scoped supervisor may staff; empty when <see cref="IsDepartmentAdmin"/>.</summary>
		public List<int> ManageableGroupIds { get; set; } = new List<int>();

		public Dictionary<int, List<UnitStateRole>> CurrentUnitRoles { get; set; }
	}
}
