using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// Which department groups a user may supervise shifts for (approve signups and trades, add or remove people on a
	/// day). <see cref="AllGroups"/> covers department admins and department-wide shift managers; otherwise
	/// <see cref="GroupIds"/> holds the groups (and their child groups) the user is a group admin of, which is how a
	/// contracted provider manages only their own teams.
	/// </summary>
	public class ShiftManagementScope
	{
		public bool AllGroups { get; set; }

		public HashSet<int> GroupIds { get; set; } = new HashSet<int>();

		public bool IsSupervisor => AllGroups || GroupIds.Count > 0;

		public bool CanManageGroup(int? departmentGroupId)
		{
			if (AllGroups)
				return true;

			return departmentGroupId.HasValue && GroupIds.Contains(departmentGroupId.Value);
		}

		/// <summary>
		/// May edit the shift itself (details, days, groups): every group on the shift is one this user manages.
		/// </summary>
		public bool CanManageShift(Shift shift)
		{
			if (AllGroups)
				return true;

			var groups = shift?.Groups?.Where(x => x != null).ToList();

			return groups != null && groups.Any() && groups.All(x => GroupIds.Contains(x.DepartmentGroupId));
		}

		/// <summary>
		/// Supervises at least one group on the shift, so has something to approve or edit on its days.
		/// </summary>
		public bool CanSuperviseShift(Shift shift)
		{
			if (AllGroups)
				return true;

			return shift?.Groups != null && shift.Groups.Any(x => x != null && GroupIds.Contains(x.DepartmentGroupId));
		}

		/// <summary>
		/// May act on a slot in the given group of the shift. A slot with no group belongs to the whole shift, so it
		/// needs the whole shift.
		/// </summary>
		public bool CanManageShiftGroup(Shift shift, int? departmentGroupId)
		{
			return departmentGroupId.HasValue ? CanManageGroup(departmentGroupId) : CanManageShift(shift);
		}

		public static ShiftManagementScope None()
		{
			return new ShiftManagementScope();
		}
	}
}
