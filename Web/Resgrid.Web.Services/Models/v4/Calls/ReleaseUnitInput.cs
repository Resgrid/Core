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
		/// Optional. When true, the unit is sent a cancellation notice for the call (as for a unit taken off an edited call).
		/// </summary>
		public bool Notify { get; set; }
	}
}
