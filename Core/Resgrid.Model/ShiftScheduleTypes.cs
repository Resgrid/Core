using System.ComponentModel.DataAnnotations;

namespace Resgrid.Model
{
	public enum ShiftScheduleTypes
	{
		[Display(Name = "Custom")]
		Custom					= 0,

		[Display(Name = "Manual")]
		Manual					= 1,

		[Display(Name = "24/48 (W-O-O)")]
		TwentyFourFortyEight	= 2,

		[Display(Name = "24/72 (W-O-O-O)")]
		TwentyFourSeventyTwo	= 3,

		[Display(Name = "48/96 (W-W-O-O-O-O)")]
		FortyEightNintySix		= 4,
	}
}