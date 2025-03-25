namespace Resgrid.Web.Services.Models.v4.Notes
{
	/// <summary>
	/// Result containing a single note from the system
	/// </summary>
	public class GetNoteResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public NotesResultData Data { get; set; }
	}
}
