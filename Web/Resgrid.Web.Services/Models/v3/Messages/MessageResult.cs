using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Messages
{
	/// <summary>
	/// Holds Resgrid message data
	/// </summary>
	public class MessageResult
	{
		/// <summary>
		/// Message Identifier for this message
		/// </summary>
		public int Mid { get; set; }

		/// <summary>
		/// Subject of the Message
		/// </summary>
		public string Sub { get; set; }

		/// <summary>
		/// UserId of the sending user
		/// </summary>
		public string Uid { get; set; }

		/// <summary>
		/// Body of the message (may contain html)
		/// </summary>
		public string Bdy { get; set; }

		/// <summary>
		/// When the message was sent on
		/// </summary>
		public DateTime Son { get; set; }

		/// <summary>
		/// When the message was sent on (UTC Time)
		/// </summary>
		public DateTime SUtc { get; set; }

		/// <summary>
		/// Message Type (0 = Normal, 1 = Callback, 2 = Poll)
		/// </summary>
		public int Typ { get; set; }

		/// <summary>
		/// When does the message expire (used for Polls and Callbacks)
		/// </summary>
		public DateTime? Exp { get; set; }

		/// <summary>
		/// Has the user responded to the message (For Callback and Poll)
		/// </summary>
		public bool Rsp { get; set; }

		/// <summary>
		/// Note for Response (for Callback and Poll)
		/// </summary>
		public string Not { get; set; }

		/// <summary>
		/// Responded/Read on (for Callback and Poll)
		/// </summary>
		public DateTime? Ron { get; set; }

		/// <summary>
		/// Response Type
		/// </summary>
		public string Rty { get; set; }

		/// <summary>
		/// Was this message system generated (i.e. email import)
		/// </summary>
		public bool Sys { get; set; }

		public List<MessageRecipientResult> Rcpts { get; set; }
	}

	/// <summary>
	/// Holds Resgrid message recipient information for a specifc message
	/// </summary>
	public class MessageRecipientResult
	{
		/// <summary>
		/// Message Identifier for this message
		/// </summary>
		public int Mid { get; set; }

		/// <summary>
		/// UserId of the receiving user
		/// </summary>
		public string Uid { get; set; }

		/// <summary>
		/// Responded/Read on (for Callback and Poll)
		/// </summary>
		public DateTime? Ron { get; set; }

		/// <summary>
		/// Users Response to the message (for Callback and Poll)
		/// </summary>
		public string Rty { get; set; }

		/// <summary>
		/// Note for Response (for Callback and Poll)
		/// </summary>
		public string Not { get; set; }

	}
}
