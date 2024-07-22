using System.ComponentModel.DataAnnotations;
using System.ComponentModel;

namespace Resgrid.Model
{
	/// <summary>
	/// Enumeration of the all the Base System Action Types that a user can perform in the system for both Users and Units
	/// </summary>
	public enum ActionBaseTypes
	{
		[Description("None")]
		[Display(Name = "None")]
		None = -1,

		[Description("Available")]
		[Display(Name = "Available")]
		Available = 0,

		[Description("Not Responding")]
		[Display(Name = "Not Responding")]
		NotResponding = 1,

		[Description("Responding")]
		[Display(Name = "Responding")]
		Responding = 2,

		[Description("On Scene")]
		[Display(Name = "On Scene")]
		OnScene = 3,

		[Description("Made Contact")]
		[Display(Name = "Made Contact")]
		MadeContact = 4,

		[Description("Investigating")]
		[Display(Name = "Investigating")]
		Investigating = 5,

		[Description("Dispatched")]
		[Display(Name = "Dispatched")]
		Dispatched = 6,

		[Description("Cleared")]
		[Display(Name = "Cleared")]
		Cleared = 7,

		[Description("Returning")]
		[Display(Name = "Returning")]
		Returning = 8,

		[Description("Staging")]
		[Display(Name = "Staging")]
		Staging = 9,

		[Description("Unavailable")]
		[Display(Name = "Unavailable")]
		Unavailable = 10
	}
}
