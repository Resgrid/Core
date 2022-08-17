using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Notes
{
	/// <summary>
	/// Result containing all the data required to populate the Notes form
	/// </summary>
	public class GetAllNotesResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<NotesResultData> Data { get; set; }
	}

	/// <summary>
	/// Data about a note in the system
	/// </summary>
	public class NotesResultData
	{
		/// <summary>
		/// Note Identification number
		/// </summary>
		public string NoteId { get; set; }

		/// <summary>
		/// UserId of the user who added the note
		/// </summary>
		public string UserId { get; set; }

		/// <summary>
		/// The Title of the note
		/// </summary>
		public string Title { get; set; }

		/// <summary>
		/// The Body of the Note (may contain HTML)
		/// </summary>
		public string Body { get; set; }

		/// <summary>
		/// The category color of the note
		/// </summary>
		public string Color { get; set; }

		/// <summary>
		/// The notes category
		/// </summary>
		public string Category { get; set; }

		/// <summary>
		/// When the note may expire
		/// </summary>
		public DateTime? ExpiresOn { get; set; }

		/// <summary>
		/// When the note was added on
		/// </summary>
		public DateTime AddedOn { get; set; }
	}
}
