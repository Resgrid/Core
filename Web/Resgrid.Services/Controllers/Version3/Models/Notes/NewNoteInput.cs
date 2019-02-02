namespace Resgrid.Web.Services.Controllers.Version3.Models.Notes
{
	/// <summary>
	/// Input to save a new note in the Resgrid system
	/// </summary>
	public class NewNoteInput
	{
		/// <summary>
		/// Title of the note
		/// </summary>
		public string Ttl { get; set; }

		/// <summary>
		/// Body of the note
		/// </summary>
		public string Bdy { get; set; }

		/// <summary>
		/// The Category of the note
		/// </summary>
		public string Cat { get; set; }

		/// <summary>
		/// Admins Only
		/// </summary>
		public bool Ado { get; set; }
	}
}