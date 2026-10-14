using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Model
{
	/// <summary>
	/// Canonical, system-level meaning of a staffing level (personnel availability), the staffing counterpart of
	/// <see cref="ActionBaseTypes"/>. A department names its staffing levels however it likes ("Duty Crew", "Pager Only",
	/// "Holiday"), and the base type is how Resgrid understands what a level means for dispatch, reporting and automations.
	///
	/// Stored on <c>CustomStateDetail.BaseType</c> for options of a staffing set (<see cref="CustomStateTypes.Staffing"/>);
	/// the same column holds an <see cref="ActionBaseTypes"/> value for status sets. Values 0-4 equal the built-in
	/// <see cref="UserStateTypes"/>, so a built-in staffing level is its own base type, and 0 (Available) is also what a
	/// staffing option edited before its base type was offered ended up with. Resolved to an availability class by
	/// <see cref="Resgrid.Model.Reporting.AvailabilityMatrix.ForStaffingBaseType"/>; a new value here MUST be classified there.
	/// </summary>
	public enum StaffingBaseTypes
	{
		[Description("No system meaning. The staffing level is informational only and does not affect availability, reporting or automations.")]
		[Display(Name = "None")]
		None = -1,

		[Description("The person is available to respond.")]
		[Display(Name = "Available")]
		Available = 0,

		[Description("The person can respond but will be delayed.")]
		[Display(Name = "Delayed")]
		Delayed = 1,

		[Description("The person is not available to respond.")]
		[Display(Name = "Unavailable")]
		Unavailable = 2,

		[Description("The person is committed to another assignment.")]
		[Display(Name = "Committed")]
		Committed = 3,

		[Description("The person is on shift, working at their station or post.")]
		[Display(Name = "On Shift")]
		OnShift = 4,

		[Description("The person is off duty and not expected to respond.")]
		[Display(Name = "Off Duty")]
		OffDuty = 5,

		[Description("The person is on call: off site but available to respond when called.")]
		[Display(Name = "On Call")]
		OnCall = 6
	}
}
