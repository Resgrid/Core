using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Services.Models.v4.IntakeConversations
{
	/// <summary>A call's intake SMS conversation and its messages (or none), and whether the caller may take it.</summary>
	public class IntakeConversationResult : StandardApiResponseV4Base
	{
		public IntakeConversationResultData Data { get; set; } = new IntakeConversationResultData();
	}

	public class IntakeConversationResultData
	{
		/// <summary>The conversation feeding the call (open, else the newest closed one); null when the call has none.</summary>
		public IntakeConversationData Conversation { get; set; }

		/// <summary>Oldest first.</summary>
		public List<IntakeConversationMessageData> Messages { get; set; } = new List<IntakeConversationMessageData>();

		/// <summary>The caller may edit the call, so may take (connect), text, release and close the conversation.</summary>
		public bool CanConnect { get; set; }

		/// <summary>
		/// POST results: an IntakeConversationActionResults name — Done, NotFound, Closed, HeldByAnotherDispatcher, OptedOut, SendFailed,
		/// InvalidMessage. Null on GetForCall.
		/// </summary>
		public string Outcome { get; set; }
	}

	public class IntakeConversationData
	{
		public string Id { get; set; }

		public int? CallId { get; set; }

		/// <summary>1 Open, 2 Closed.</summary>
		public int State { get; set; }

		/// <summary>1 Assistant (Enhanced AI asks), 2 Unassisted (waiting for a dispatcher), 3 Dispatcher (connected).</summary>
		public int Handler { get; set; }

		/// <summary>The connected dispatcher's user id while Handler is 3.</summary>
		public string AssignedUserId { get; set; }

		/// <summary>The connected dispatcher's name.</summary>
		public string AssignedName { get; set; }

		/// <summary>The texter's number (E.164); masked ("***1234") unless the caller may edit the call.</summary>
		public string ParticipantAddress { get; set; }

		/// <summary>ISO 8601 UTC.</summary>
		public DateTime StartedOn { get; set; }

		/// <summary>ISO 8601 UTC.</summary>
		public DateTime LastActivityOn { get; set; }

		/// <summary>The texter sent STOP: nothing more can be sent.</summary>
		public bool OptedOut { get; set; }

		/// <summary>Why it closed (OptedOut, Idle, CallClosed, or the dispatcher's reason); null while open.</summary>
		public string CloseReason { get; set; }

		/// <summary>The dispatcher desk's item for the conversation, when the desk took it.</summary>
		public string DeskItemId { get; set; }
	}

	public class IntakeConversationMessageData
	{
		public string Id { get; set; }

		/// <summary>1 Inbound (from the texter), 2 Outbound (to the texter).</summary>
		public int Direction { get; set; }

		/// <summary>1 Participant, 2 Assistant (Enhanced AI), 3 Dispatcher, 4 System (Resgrid's fixed texts).</summary>
		public int Author { get; set; }

		/// <summary>The dispatcher's first name for Author 3; null otherwise.</summary>
		public string AuthorName { get; set; }

		/// <summary>The message text (read from the call's notes; REDACTED without a grant in a protected department; null when unavailable). Plain text.</summary>
		public string Text { get; set; }

		/// <summary>1 Received, 2 Queued, 3 Sent, 4 Failed.</summary>
		public int Status { get; set; }

		/// <summary>ISO 8601 UTC.</summary>
		public DateTime CreatedOn { get; set; }
	}

	public class ConnectIntakeConversationInput
	{
		[Required]
		public string ConversationId { get; set; }

		/// <summary>Take the conversation from the dispatcher connected now.</summary>
		public bool TakeOver { get; set; }
	}

	public class SendIntakeConversationInput
	{
		[Required]
		public string ConversationId { get; set; }

		/// <summary>The text (1 to IntakeConfig.ConversationMaxOutboundLength characters, 640 by default).</summary>
		[Required]
		public string Text { get; set; }
	}

	public class ReleaseIntakeConversationInput
	{
		[Required]
		public string ConversationId { get; set; }
	}

	public class CloseIntakeConversationInput
	{
		[Required]
		public string ConversationId { get; set; }

		[MaxLength(200)]
		public string Reason { get; set; }

		/// <summary>Text the texter that the conversation ended (from the master number).</summary>
		public bool Notify { get; set; }
	}
}
