using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>Whether an intake conversation still takes messages (persisted, M0325).</summary>
	public enum IntakeConversationStates
	{
		Open = 1,
		Closed = 2
	}

	/// <summary>Who answers the texter right now (persisted, M0325).</summary>
	public enum IntakeConversationHandlers
	{
		/// <summary>Enhanced AI asks the follow-up questions (Ai.Intake, self-hosted model, not an ADP department).</summary>
		Assistant = 1,

		/// <summary>Nobody automated: the welcome said a dispatcher will reply; the pending call waits for one.</summary>
		Unassisted = 2,

		/// <summary>A dispatcher connected (desk accept or "Take conversation" on the call); AI stays silent.</summary>
		Dispatcher = 3
	}

	/// <summary>Persisted, M0325.</summary>
	public enum IntakeConversationMessageDirections
	{
		Inbound = 1,
		Outbound = 2
	}

	/// <summary>Persisted, M0325.</summary>
	public enum IntakeConversationAuthors
	{
		/// <summary>The member of the public who texted.</summary>
		Participant = 1,

		/// <summary>Enhanced AI.</summary>
		Assistant = 2,

		/// <summary>A dispatcher (AuthorUserId).</summary>
		Dispatcher = 3,

		/// <summary>Resgrid's own fixed texts: welcome, connected, closed.</summary>
		System = 4
	}

	/// <summary>Persisted, M0325.</summary>
	public enum IntakeConversationMessageStatuses
	{
		Received = 1,
		Queued = 2,
		Sent = 3,
		Failed = 4
	}

	/// <summary>
	/// An SMS thread between a member of the public and a department (M0325; plan §8). Opened by a text to the department's
	/// dedicated number (which then creates the pending call, D7); every reply to the texter goes from the master Resgrid number
	/// (<see cref="ReplyFromAddress"/>, D5), and the texter's replies to the master number come back here while the thread is open.
	/// The message text is never stored here: each message is a note on <see cref="CallId"/> (ADP-protected like any note).
	/// </summary>
	[Table("IntakeConversations")]
	public class IntakeConversation : IEntity
	{
		[Key]
		[Required]
		public string IntakeConversationId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		public string IntakeChannelId { get; set; }

		/// <summary>The pending call the conversation feeds (always set once the first message is handled).</summary>
		public int? CallId { get; set; }

		/// <summary>An <see cref="IntakeChannelTypes"/> value (Sms today).</summary>
		public int ChannelType { get; set; }

		/// <summary>The texter (E.164 for SMS).</summary>
		[MaxLength(256)]
		public string ParticipantAddress { get; set; }

		[MaxLength(200)]
		public string ParticipantName { get; set; }

		/// <summary>The department number the texter wrote to (the dedicated number).</summary>
		[MaxLength(256)]
		public string InboundAddress { get; set; }

		/// <summary>The number Resgrid answers from (the region's master number) and the texter replies to.</summary>
		[MaxLength(256)]
		public string ReplyFromAddress { get; set; }

		/// <summary>An <see cref="IntakeConversationStates"/> value.</summary>
		public int State { get; set; } = (int)IntakeConversationStates.Open;

		/// <summary>An <see cref="IntakeConversationHandlers"/> value.</summary>
		public int Handler { get; set; }

		/// <summary>The connected dispatcher while <see cref="Handler"/> is Dispatcher.</summary>
		public string AssignedUserId { get; set; }

		public DateTime? AssignedOn { get; set; }

		/// <summary>Replies Enhanced AI has sent (capped by IntakeConfig.ConversationMaxAssistantTurns).</summary>
		public int AssistantTurns { get; set; }

		/// <summary>The texter sent STOP: nothing more is sent, the thread is closed.</summary>
		public bool OptedOut { get; set; }

		/// <summary>The dispatcher desk's queue item for this conversation, when a desk router took it (desk writes it).</summary>
		public string DeskItemId { get; set; }

		public DateTime StartedOn { get; set; }

		public DateTime? LastInboundOn { get; set; }

		public DateTime LastActivityOn { get; set; }

		public DateTime? ClosedOn { get; set; }

		[MaxLength(200)]
		public string CloseReason { get; set; }

		[NotMapped]
		public bool IsOpen => State == (int)IntakeConversationStates.Open;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return IntakeConversationId; }
			set { IntakeConversationId = (string)value; }
		}

		[NotMapped]
		public string TableName => "IntakeConversations";

		[NotMapped]
		public string IdName => "IntakeConversationId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "IsOpen" };
	}

	/// <summary>
	/// Delivery log for one message of an <see cref="IntakeConversation"/> (M0325). The text is the call note <see cref="CallNoteId"/>.
	/// </summary>
	[Table("IntakeConversationMessages")]
	public class IntakeConversationMessage : IEntity
	{
		[Key]
		[Required]
		public string IntakeConversationMessageId { get; set; }

		[Required]
		public string IntakeConversationId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>An <see cref="IntakeConversationMessageDirections"/> value.</summary>
		public int Direction { get; set; }

		/// <summary>An <see cref="IntakeConversationAuthors"/> value.</summary>
		public int Author { get; set; }

		public string AuthorUserId { get; set; }

		public int? CallNoteId { get; set; }

		/// <summary>Twilio MessageSid (inbound: idempotency per conversation; outbound: the send's id).</summary>
		[MaxLength(256)]
		public string ProviderMessageId { get; set; }

		/// <summary>An <see cref="IntakeConversationMessageStatuses"/> value.</summary>
		public int Status { get; set; }

		[MaxLength(1000)]
		public string Error { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime? SentOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return IntakeConversationMessageId; }
			set { IntakeConversationMessageId = (string)value; }
		}

		[NotMapped]
		public string TableName => "IntakeConversationMessages";

		[NotMapped]
		public string IdName => "IntakeConversationMessageId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>One message of a conversation as the UI shows it: the delivery row plus the note text (read through the call).</summary>
	public sealed class IntakeConversationEntry
	{
		public string MessageId { get; set; }
		public int Direction { get; set; }
		public int Author { get; set; }
		public string AuthorUserId { get; set; }
		/// <summary>The dispatcher's first name for Dispatcher messages.</summary>
		public string AuthorName { get; set; }
		public string Text { get; set; }
		public int Status { get; set; }
		public DateTime CreatedOn { get; set; }
	}

	/// <summary>A conversation with its messages, oldest first.</summary>
	public sealed class IntakeConversationView
	{
		public IntakeConversation Conversation { get; set; }
		public List<IntakeConversationEntry> Messages { get; set; } = new List<IntakeConversationEntry>();
	}

	/// <summary>Outcome of a dispatcher action on a conversation.</summary>
	public enum IntakeConversationActionResults
	{
		Done = 1,
		NotFound = 2,
		Closed = 3,
		/// <summary>Another dispatcher is connected (Connect/Send by someone else).</summary>
		HeldByAnotherDispatcher = 4,
		/// <summary>The texter opted out (STOP); nothing can be sent.</summary>
		OptedOut = 5,
		/// <summary>The message could not be handed to the carrier (logged on the message row).</summary>
		SendFailed = 6,
		InvalidMessage = 7
	}

	/// <summary>What a desk router did with a new conversation.</summary>
	public enum IntakeConversationRouting
	{
		/// <summary>No dispatcher routing for this department (no desk, desk off): AI or unassisted handling as usual.</summary>
		NotRouted = 0,

		/// <summary>Offered or queued to on-duty dispatchers; AI keeps asking questions until one connects.</summary>
		Offered = 1
	}
}
