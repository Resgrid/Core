namespace Resgrid.Model
{
	/// <summary>
	/// One person on a shift that is running right now (see IShiftsService.GetOnShiftPersonnelAsync).
	/// A person on two overlapping shifts appears once per shift.
	/// </summary>
	public class OnShiftAssignment
	{
		public string UserId { get; set; }

		public int ShiftId { get; set; }

		public string ShiftName { get; set; }

		/// <summary>
		/// The group the person is covering on this shift: the signup's group, or the group an assigned
		/// shift placed them in. Null when the shift doesn't say; callers fall back to the person's own group.
		/// </summary>
		public int? DepartmentGroupId { get; set; }
	}
}
