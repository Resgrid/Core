using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Model.Reporting
{
	/// <summary>
	/// Canonical, cross-department operational availability classification for a person or unit.
	/// Produced by <see cref="AvailabilityMatrix"/> from a built-in or custom status' base type.
	/// This is the single vocabulary the Dispatch app and reporting use to answer
	/// "is this resource available for a call?" regardless of a department's custom status labels.
	/// </summary>
	public enum AvailabilityClass
	{
		/// <summary>Status could not be mapped to a known base type.</summary>
		[Description("Unknown")]
		[Display(Name = "Unknown")]
		Unknown = 0,

		/// <summary>Available to be assigned/dispatched to a call.</summary>
		[Description("Available")]
		[Display(Name = "Available")]
		Available = 1,

		/// <summary>Engaged on a call/assignment and not available for another.</summary>
		[Description("Committed")]
		[Display(Name = "Committed")]
		Committed = 2,

		/// <summary>Out of service / not responding / unavailable.</summary>
		[Description("Unavailable")]
		[Display(Name = "Unavailable")]
		Unavailable = 3,

		/// <summary>Available but with a delay (e.g. delayed response).</summary>
		[Description("Delayed")]
		[Display(Name = "Delayed")]
		Delayed = 4
	}
}
