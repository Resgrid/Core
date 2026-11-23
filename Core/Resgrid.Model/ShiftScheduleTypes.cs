using System.ComponentModel.DataAnnotations;

namespace Resgrid.Model
{
	/// <summary>
	/// Stored in <see cref="Shift.ScheduleType"/>. No service generates days from a rotation: every shift's days are the
	/// dates picked for it, so new shifts are always <see cref="Manual"/> (ShiftsService.SaveShiftAsync) and the web no longer
	/// shows the type. The other values stay only so rows stored with them still read; do not offer them or build on them
	/// (a real rotation-pattern engine is future work, phase SH1).
	/// </summary>
	public enum ShiftScheduleTypes
	{
		/// <summary>Legacy value on old rows; not written.</summary>
		[Display(Name = "Custom")]
		Custom					= 0,

		/// <summary>Days are the dates picked for the shift. The only value written.</summary>
		[Display(Name = "Manual")]
		Manual					= 1,

		/// <summary>Legacy value on old rows; not written and no days are generated from it.</summary>
		[Display(Name = "24/48 (W-O-O)")]
		TwentyFourFortyEight	= 2,

		/// <summary>Legacy value on old rows; not written and no days are generated from it.</summary>
		[Display(Name = "24/72 (W-O-O-O)")]
		TwentyFourSeventyTwo	= 3,

		/// <summary>Legacy value on old rows; not written and no days are generated from it.</summary>
		[Display(Name = "48/96 (W-W-O-O-O-O)")]
		FortyEightNintySix		= 4,
	}
}
