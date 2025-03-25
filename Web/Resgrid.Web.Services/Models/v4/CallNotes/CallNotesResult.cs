using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.CallNotes
{
	/// <summary>
	/// Gets the notes for a call
	/// </summary>
	public class CallNotesResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public List<CallNoteResultData> Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public CallNotesResult()
		{
			Data = new List<CallNoteResultData>();
		}
	}

	/// <summary>
	///
	/// </summary>
	public class CallNoteResultData
	{
		/// <summary>
		/// Call Id of the Note
		/// </summary>
		public string CallId { get; set; }

		/// <summary>
		/// Call Note Id
		/// </summary>
		public string CallNoteId { get; set; }

		/// <summary>
		/// UserId of the user who added the note
		/// </summary>
		public string UserId { get; set; }

		/// <summary>
		/// Note source
		/// </summary>
		public int Source { get; set; }

		/// <summary>
		/// Formatted Timestamp
		/// </summary>
		public string TimestampFormatted { get; set; }

		/// <summary>
		/// Timestamp of when the note as added
		/// </summary>
		public DateTime Timestamp { get; set; }

		/// <summary>
		/// Timestamp of when the note as added in Utc
		/// </summary>
		public DateTime TimestampUtc { get; set; }

		/// <summary>
		/// Note content
		/// </summary>
		public string Note { get; set; }

		/// <summary>
		/// (Optional) Note Latitude
		/// </summary>
		public decimal? Latitude { get; set; }

		/// <summary>
		/// (Optional) Note Longitude
		/// </summary>
		public decimal? Longitude { get; set; }

		/// <summary>
		/// Full name of the user who submitted the note
		/// </summary>
		public string FullName { get; set; }
	}
}
