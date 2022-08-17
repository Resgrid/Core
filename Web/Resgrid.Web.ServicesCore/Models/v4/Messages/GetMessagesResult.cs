using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Messages;

/// <summary>
/// Gets the messages for a user
/// </summary>
public class GetMessagesResult : StandardApiResponseV4Base
{
	/// <summary>
	/// Response Data
	/// </summary>
	public List<MessageResultData> Data { get; set; }

	/// <summary>
	/// Default constructor
	/// </summary>
	public GetMessagesResult()
	{
		Data = new List<MessageResultData>();
	}
}

/// <summary>
/// Holds Resgrid message data
/// </summary>
public class MessageResultData
{
	/// <summary>
	/// Message Identifier for this message
	/// </summary>
	public string MessageId { get; set; }

	/// <summary>
	/// Subject of the Message
	/// </summary>
	public string Subject { get; set; }

	/// <summary>
	/// The name of the sending user
	/// </summary>
	public string SendingName { get; set; }

	/// <summary>
	/// UserId of the sending user
	/// </summary>
	public string SendingUserId { get; set; }

	/// <summary>
	/// Body of the message (may contain html)
	/// </summary>
	public string Body { get; set; }

	/// <summary>
	/// When the message was sent on (local time)
	/// </summary>
	public DateTime SentOn { get; set; }

	/// <summary>
	/// When the message was sent on (UTC Time)
	/// </summary>
	public DateTime SentOnUtc { get; set; }

	/// <summary>
	/// Message Type (0 = Normal, 1 = Callback, 2 = Poll)
	/// </summary>
	public int Type { get; set; }

	/// <summary>
	/// When does the message expire (used for Polls and Callbacks)
	/// </summary>
	public DateTime? ExpiredOn { get; set; }

	/// <summary>
	/// Has the user responded to the message (For Callback and Poll)
	/// </summary>
	public bool Responded { get; set; }

	/// <summary>
	/// Note for Response (for Callback and Poll)
	/// </summary>
	public string Note { get; set; }

	/// <summary>
	/// Responded/Read on (for Callback and Poll)
	/// </summary>
	public DateTime? RespondedOn { get; set; }

	/// <summary>
	/// Response Type
	/// </summary>
	public string ResponseType { get; set; }

	/// <summary>
	/// Was this message system generated (i.e. email import)
	/// </summary>
	public bool IsSystem { get; set; }

	/// <summary>
	/// Recipients of this message
	/// </summary>
	public List<MessageRecipientResultData> Recipients { get; set; }
}

/// <summary>
/// Holds Resgrid message recipient information for a specific message
/// </summary>
public class MessageRecipientResultData
{
	/// <summary>
	/// Message Identifier for this message
	/// </summary>
	public string MessageId { get; set; }

	/// <summary>
	/// UserId of the receiving user
	/// </summary>
	public string UserId { get; set; }

	/// <summary>
	/// Name of the receiving user
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// Responded/Read on (for Callback and Poll)
	/// </summary>
	public DateTime? RespondedOn { get; set; }

	/// <summary>
	/// Users Response to the message (for Callback and Poll)
	/// </summary>
	public string Response { get; set; }

	/// <summary>
	/// Note for Response (for Callback and Poll)
	/// </summary>
	public string Note { get; set; }
}
