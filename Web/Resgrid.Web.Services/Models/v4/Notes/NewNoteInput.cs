using System;

namespace Resgrid.Web.Services.Models.v4.Notes;

/// <summary>
/// New note input
/// </summary>
public class NewNoteInput
{
	/// <summary>
	/// The title/subject of the note
	/// </summary>
	public string Title { get; set; }

	/// <summary>
	/// The body/content of the note
	/// </summary>
	public string Body { get; set; }

	/// <summary>
	/// Note Category
	/// </summary>
	public string Category { get; set; }

	/// <summary>
	/// Is the note only visible to admins
	/// </summary>
	public bool IsAdminOnly { get; set; }

	/// <summary>
	/// When the note expires on (optional)
	/// </summary>
	public DateTime? ExpiresOn { get; set; }
}
