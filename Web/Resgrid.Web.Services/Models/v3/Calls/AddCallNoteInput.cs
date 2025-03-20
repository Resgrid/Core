using System;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Calls
{
	/// <summary>
	/// Input to attach a note to call
	/// </summary>
	public class AddCallNoteInput
	{
		/// <summary>
		/// Id of the Call
		/// </summary>
		public int CallId { get; set; }

		/// <summary>
		/// UserId of the user adding the note
		/// </summary>
		public string UserId { get; set; }

		/// <summary>
		/// Note text to add
		/// </summary>
		public string Note { get; set; }

		/// <summary>
		/// Latitude of when the note was taken
		/// </summary>
		public double? Lat { get; set; }

		/// <summary>
		/// Longitude of when the note was taken
		/// </summary>
		public double? Lon { get; set; }
	}
}
