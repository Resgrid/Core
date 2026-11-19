using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Services.Models.v4.Calls
{
	/// <summary>
	/// Input to release (stand down) one unit from an open call without closing the call
	/// </summary>
	public class ReleaseUnitInput
	{
		/// <summary>
		/// Id of the call the unit is released from
		/// </summary>
		[Required]
		public string CallId { get; set; }

		/// <summary>
		/// Id of the unit to release
		/// </summary>
		[Required]
		public string UnitId { get; set; }

		/// <summary>
		/// Optional. When true, asks for a cancellation notice to be sent to the unit (as for a unit taken off an edited call).
		/// Best effort: a release that succeeds does not confirm the notice went out, and repeating the request returns "already
		/// released" rather than resending it.
		/// </summary>
		public bool Notify { get; set; }
	}
}
