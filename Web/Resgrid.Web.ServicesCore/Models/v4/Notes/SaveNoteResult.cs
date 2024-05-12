namespace Resgrid.Web.Services.Models.v4.Notes;

/// <summary>
/// The result of saving a note
/// </summary>
public class SaveNoteResult: StandardApiResponseV4Base
{
	/// <summary>
	/// Identifier of the new npte
	/// </summary>
	public string Id { get; set; }
}
