using System;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Calls
{
	public class CallNoteResult
	{
		/// <summary>
		/// Call Id of the Note
		/// </summary>
		public int Cid { get; set; }

		/// <summary>
		/// Call Note Id
		/// </summary>
		public int Cnd { get; set; }

		/// <summary>
		/// UserId of the user who added the note
		/// </summary>
		public string Uid { get; set; }

		/// <summary>
		/// Note source
		/// </summary>
		public int Src { get; set; }

		/// <summary>
		/// Formatted Timestamp
		/// </summary>
		public string Tme { get; set; }

		/// <summary>
		/// Timestamp of when the note as added
		/// </summary>
		public DateTime Tsp { get; set; }

		/// <summary>
		/// Timestamp of when the note as added in Utc
		/// </summary>
		public DateTime TUtc { get; set; }

		/// <summary>
		/// Note content
		/// </summary>
		public string Not { get; set; }

		/// <summary>
		/// (Optional) Note Latitude
		/// </summary>
		public decimal? Lat { get; set; }

		/// <summary>
		/// (Optional) Note Longitude
		/// </summary>
		public decimal? Lng { get; set; }

		/// <summary>
		/// Full name of the user who submitted the note
		/// </summary>
		public string Fnm { get; set; }
	}
}
