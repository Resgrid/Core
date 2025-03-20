namespace Resgrid.Web.Services.Models.v4.Messages;

/// <summary>
/// The result of deleting a message
/// </summary>
public class DeleteMessageResult: StandardApiResponseV4Base
{
	/// <summary>
	/// Identifier of the new message
	/// </summary>
	public string Id { get; set; }
}
