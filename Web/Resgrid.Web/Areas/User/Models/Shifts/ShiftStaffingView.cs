using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Shifts
{
	public class ShiftStaffingView
	{
		/// <summary><see cref="StaffingKind"/> for an assigned or signup shift.</summary>
		public const string ShiftKind = "shift";

		/// <summary><see cref="StaffingKind"/> for a static shift (workshift).</summary>
		public const string WorkshiftKind = "workshift";

		public List<Shift> Shifts { get; set; }
		public int ShiftId { get; set; }

		/// <summary>Static shifts the caller may staff; empty unless <see cref="CanStaffWorkshifts"/>.</summary>
		public List<Workshift> Workshifts { get; set; } = new List<Workshift>();
		public string WorkshiftId { get; set; }

		/// <summary>Which kind of shift the form staffs: <see cref="ShiftKind"/> or <see cref="WorkshiftKind"/>.</summary>
		public string StaffingKind { get; set; } = ShiftKind;

		/// <summary>The day to open on (MM/dd/yyyy), from a link or after saving.</summary>
		public string Day { get; set; }

		/// <summary>The caller supervises every group, so also sets the non-group personnel.</summary>
		public bool IsDepartmentAdmin { get; set; }

		/// <summary>Static shifts are department wide, so only department-wide supervisors staff them.</summary>
		public bool CanStaffWorkshifts { get; set; }
		public int GroupId { get; set; }
		public string Note { get; set; }

		/// <summary>Groups a group-scoped supervisor may staff; empty when <see cref="IsDepartmentAdmin"/>.</summary>
		public List<int> ManageableGroupIds { get; set; } = new List<int>();

		public Dictionary<int, List<UnitStateRole>> CurrentUnitRoles { get; set; }
	}
}
