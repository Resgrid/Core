namespace Resgrid.Web.Services.Models.v4.Messages;

/// <summary>
/// The result of sending a message
/// </summary>
public class SendMessageResult: StandardApiResponseV4Base
{
	/// <summary>
	/// Identifier of the new message
	/// </summary>
	public string Id { get; set; }
}
