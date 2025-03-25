namespace Resgrid.Web.Services.Models.v4.Messages;

/// <summary>
/// The result of responding to a message
/// </summary>
public class RespondToMessageResult: StandardApiResponseV4Base
{
	/// <summary>
	/// Identifier of the new message response
	/// </summary>
	public string Id { get; set; }
}
