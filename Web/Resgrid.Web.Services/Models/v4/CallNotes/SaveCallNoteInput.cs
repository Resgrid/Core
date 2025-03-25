using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Services.Models.v4.CallNotes
{
	/// <summary>
	/// Input to attach a note to call
	/// </summary>
	public class SaveCallNoteInput
	{
		/// <summary>
		/// Id of the Call
		/// </summary>
		[Required]
		public string CallId { get; set; }

		/// <summary>
		/// UserId of the user adding the note
		/// </summary>
		[Required]
		public string UserId { get; set; }

		/// <summary>
		/// Note text to add
		/// </summary>
		[Required]
		public string Note { get; set; }

		/// <summary>
		/// Latitude of when the note was taken
		/// </summary>
		public string Latitude { get; set; }

		/// <summary>
		/// Longitude of when the note was taken
		/// </summary>
		public string Longitude { get; set; }
	}
}
