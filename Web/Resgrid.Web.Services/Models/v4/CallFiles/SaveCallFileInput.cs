using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Services.Models.v4.CallFiles
{
	/// <summary>
	/// Input to attach a file to a call
	/// </summary>
	public class SaveCallFileInput
	{
		/// <summary>
		/// Id of the Call
		/// </summary>
		[Required]
		public string CallId { get; set; }

		/// <summary>
		/// User Id of the user attaching the file
		/// </summary>
		[Required]
		public string UserId { get; set; }

		/// <summary>
		/// Type of the file (Audio = 1, Image = 2, File = 3, Video	= 4)
		/// </summary>
		public int Type { get; set; }

		/// <summary>
		/// Name of the file
		/// </summary>
		[Required]
		public string Name { get; set; }

		/// <summary>
		/// Base64 encoded string of the file being uploaded
		/// </summary>
		[Required]
		public string Data { get; set; }


		public string Latitude { get; set; }

		public string Longitude { get; set; }

		public string Note { get; set; }

		/// <summary>
		/// Department Id for the call file. Only used in System API Key (hosted multi-department) mode.
		/// When provided, overrides the department derived from the auth token.
		/// </summary>
		public string DepartmentId { get; set; }

		/// <summary>
		/// Optional idempotency key for an upload that may be sent again (an app's offline outbox sends its event id). The same
		/// key in the same department returns the first file instead of attaching a second copy. The <c>Idempotency-Key</c>
		/// header, when sent, takes precedence over this field.
		/// </summary>
		public string IdempotencyKey { get; set; }
	}
}
