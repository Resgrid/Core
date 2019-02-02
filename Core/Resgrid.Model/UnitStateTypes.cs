using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Model
{
	public enum UnitStateTypes
	{

		[Description("Available")]
		[Display(Name = "Available")]
		Available = 0,

		[Description("Delayed")]
		[Display(Name = "Delayed")]
		Delayed = 1,

		[Description("Unavailable")]
		[Display(Name = "Unavailable")]
		Unavailable = 2,

		[Description("Committed")]
		[Display(Name = "Committed")]
		Committed = 3,

		[Description("Out Of Service")]
		[Display(Name = "Out Of Service")]
		OutOfService = 4,

		[Description("Responding")]
		[Display(Name = "Responding")]
		Responding = 5,

		[Description("On Scene")]
		[Display(Name = "On Scene")]
		OnScene = 6,

		[Description("Staging")]
		[Display(Name = "Staging")]
		Staging = 7,

		[Description("Returning")]
		[Display(Name = "Returning")]
		Returning = 8,

		[Description("Cancelled")]
		[Display(Name = "Cancelled")]
		Cancelled = 9,

		[Description("Released")]
		[Display(Name = "Released")]
		Released = 10,

		[Description("Manual")]
		[Display(Name = "Manual")]
		Manual = 11,

		[Description("Enroute")]
		[Display(Name = "Enroute")]
		Enroute = 12
	}
}