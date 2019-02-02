using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Model
{
	/// <summary>
	/// The types of a users staffing level (state)
	/// </summary>
	public enum UserStateTypes
	{
		/// <summary>
		/// User is staffing as Available
		/// </summary>
		[Display(Name = "Available")]
		[Description("Available")]
		Available = 0,

		/// <summary>
		/// Delayed response to calls
		/// </summary>
		[Display(Name = "Delayed")]
		[Description("Delayed")]
		Delayed = 1,

		/// <summary>
		/// User is Unavailable to respond
		/// </summary>
		[Display(Name = "Unavailable")]
		[Description("Unavailable")]
		Unavailable = 2,

		/// <summary>
		/// User is committed on another call/operation
		/// </summary>
		[Display(Name = "Committed")]
		[Description("Committed")]
		Committed = 3,

		/// <summary>
		/// User is On Shift
		/// </summary>
		[Display(Name = "On Shift")]
		[Description("On Shift")]
		OnShift = 4
	}
}