using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Services.Models.v4.Calls
{
	/// <summary>
	/// Input to release (stand down) one member from an open call without closing the call
	/// </summary>
	public class ReleasePersonnelInput
	{
		/// <summary>
		/// Id of the call the member is released from
		/// </summary>
		[Required]
		public string CallId { get; set; }

		/// <summary>
		/// User id of the member to release
		/// </summary>
		[Required]
		public string UserId { get; set; }

		/// <summary>
		/// Optional. When true, the member is sent a cancellation notice for the call.
		/// </summary>
		public bool Notify { get; set; }
	}
}
