using System;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Notes
{
	/// <summary>
	/// Data about a note in the system
	/// </summary>
	public class NotesResult
	{
		/// <summary>
		/// Note Identificaiton number
		/// </summary>
		public int Nid { get; set; }

		/// <summary>
		/// UserId of the user who added the note
		/// </summary>
		public string Uid { get; set; }

		/// <summary>
		/// The Title of the note
		/// </summary>
		public string Ttl { get; set; }

		/// <summary>
		/// The Body of the Note (may contain HTML)
		/// </summary>
		public string Bdy { get; set; }

		/// <summary>
		/// The category color of the note
		/// </summary>
		public string Clr { get; set; }

		/// <summary>
		/// The notes category
		/// </summary>
		public string Cat { get; set; }

		/// <summary>
		/// When the note may expire
		/// </summary>
		public DateTime? Exp { get; set; }

		/// <summary>
		/// When the note was added on
		/// </summary>
		public DateTime Adn { get; set; }
	}
}
