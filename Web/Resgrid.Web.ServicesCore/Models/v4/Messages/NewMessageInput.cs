using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Messages;

/// <summary>
/// New message input
/// </summary>
public class NewMessageInput
{
	/// <summary>
	/// The title/subject of the message
	/// </summary>
	public string Title { get; set; }

	/// <summary>
	/// The body/content of the message
	/// </summary>
	public string Body { get; set; }

	/// <summary>
	/// Type type of the message (0 = Normal\Message, 1 = Callback, 2 = Poll)
	/// </summary>
	public int Type { get; set; }

	/// <summary>
	/// Who to send the message to
	/// </summary>
	public List<MessageRecipientInput> Recipients { get; set; }
}

/// <summary>
/// The result of getting all recipients for the system
/// </summary>
public class MessageRecipientInput
{
	/// <summary>
	/// The Id value of the recipient, it will be a Guid value for Users and an Int for all others
	/// </summary>
	public string Id { get; set; }

	/// <summary>
	/// The type of the Recipient
	/// </summary>
	public int Type { get; set; }

	/// <summary>
	/// The recipient's display name
	/// </summary>
	public string Name { get; set; }
}
