using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// One piece of work routed to the dispatcher desk (M0324): a pending or scheduled call, or an inbound text, phone call, email
	/// or chat conversation. It is offered to one available dispatcher at a time; when nobody takes it it waits in the shared
	/// queue (calls) or goes to the department's overflow handling (live channels). State changes are conditional on
	/// <see cref="Version"/>, so two dispatchers never hold the same item.
	/// </summary>
	[Table("DispatcherQueueItems")]
	public class DispatcherQueueItem : IEntity
	{
		public const int PreviewMaxLength = 500;

		[Key]
		[Required]
		public string DispatcherQueueItemId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		/// <summary><see cref="DispatcherQueueItemKinds"/>.</summary>
		public int Kind { get; set; }

		/// <summary><see cref="DispatcherQueueItemStates"/>.</summary>
		public int State { get; set; }

		/// <summary><see cref="DispatcherQueueItemOutcomes"/>, once completed.</summary>
		public int Outcome { get; set; }

		/// <summary>The call it is about: the pending or scheduled call, or the call created from the conversation.</summary>
		public int? CallId { get; set; }

		/// <summary>Higher goes first: the call priority's weight, voice above text above email.</summary>
		public int Priority { get; set; }

		/// <summary>Who it came from: phone number (E.164), email address or chat handle.</summary>
		[MaxLength(256)]
		public string SourceAddress { get; set; }

		[MaxLength(200)]
		public string SourceName { get; set; }

		/// <summary>The department number or address it came in on.</summary>
		[MaxLength(256)]
		public string TargetAddress { get; set; }

		[MaxLength(250)]
		public string Subject { get; set; }

		/// <summary>The start of the message or the call's nature, for the queue list. Never more than <see cref="PreviewMaxLength"/>.</summary>
		[MaxLength(PreviewMaxLength)]
		public string Preview { get; set; }

		/// <summary>The provider's id for the live leg (Twilio CallSid for voice).</summary>
		[MaxLength(128)]
		public string ExternalReference { get; set; }

		/// <summary>Kind + normalized sender: later messages from the same sender join the open conversation instead of starting a new one.</summary>
		[MaxLength(300)]
		public string ConversationKey { get; set; }

		/// <summary>The dispatcher it is offered to or held by.</summary>
		[MaxLength(128)]
		public string AssignedUserId { get; set; }

		public DateTime? OfferedOn { get; set; }

		public DateTime? OfferExpiresOn { get; set; }

		public int OfferAttempts { get; set; }

		/// <summary>Comma-separated dispatchers who declined it or let the offer expire; it is not offered to them again.</summary>
		public string DeclinedUserIds { get; set; }

		public DateTime? AcceptedOn { get; set; }

		public DateTime? CompletedOn { get; set; }

		public string CompletedByUserId { get; set; }

		/// <summary>When it needs to be dealt with: a scheduled call's dispatch time. Null = now.</summary>
		public DateTime? DueOn { get; set; }

		/// <summary>Concurrency token: every state change is conditional on it and bumps it.</summary>
		public int Version { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		/// <summary>The last message, state change or touch; drives the conversation join window.</summary>
		public DateTime LastActivityOn { get; set; }

		/// <summary>When it went to overflow handling, and what that did (free text, for the history).</summary>
		public DateTime? OverflowedOn { get; set; }

		[MaxLength(250)]
		public string OverflowNote { get; set; }

		/// <summary>When dispatchers and supervisors were told it had waited too long.</summary>
		public DateTime? EscalatedOn { get; set; }

		[MaxLength(1000)]
		public string Note { get; set; }

		public List<string> GetDeclinedUserIds()
		{
			return (DeclinedUserIds ?? string.Empty).Split(',').Select(x => x.Trim()).Where(x => x.Length > 0)
				.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		}

		public bool IsOpen() => State == (int)DispatcherQueueItemStates.Queued || State == (int)DispatcherQueueItemStates.Offered ||
			State == (int)DispatcherQueueItemStates.Accepted;

		public bool IsLiveChannel() => Kind == (int)DispatcherQueueItemKinds.Text || Kind == (int)DispatcherQueueItemKinds.Voice ||
			Kind == (int)DispatcherQueueItemKinds.Email || Kind == (int)DispatcherQueueItemKinds.Chat || Kind == (int)DispatcherQueueItemKinds.Other;

		/// <summary>
		/// The one-line preview the desk lists and alerts show: a call's nature is stored rich text ("&lt;p&gt;…&lt;/p&gt;" from the editor), so
		/// markup is flattened to its text first; a texted "pt &lt;unconscious&gt;" holds no markup and stays as written.
		/// </summary>
		public static string TruncatePreview(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
				return null;

			var trimmed = Resgrid.Framework.StringHelpers.HtmlToMessageText(text, singleLine: true);
			if (trimmed.Length == 0)
				return null;

			return trimmed.Length <= PreviewMaxLength ? trimmed : trimmed.Substring(0, PreviewMaxLength - 1) + "…";
		}

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DispatcherQueueItemId; }
			set { DispatcherQueueItemId = (string)value; }
		}

		[NotMapped]
		public string TableName => "DispatcherQueueItems";

		[NotMapped]
		public string IdName => "DispatcherQueueItemId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>One step in a routed item's history (M0324): offers, declines, accepts, transfers, messages, completion.</summary>
	[Table("DispatcherQueueEvents")]
	public class DispatcherQueueEvent : IEntity
	{
		[Key]
		[Required]
		public string DispatcherQueueEventId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[Required]
		public string DispatcherQueueItemId { get; set; }

		/// <summary><see cref="DispatcherQueueEventTypes"/>.</summary>
		public int EventType { get; set; }

		/// <summary>Who did it (or who it was offered to); null for the system.</summary>
		[MaxLength(128)]
		public string UserId { get; set; }

		public DateTime OccurredOn { get; set; }

		[MaxLength(500)]
		public string Detail { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DispatcherQueueEventId; }
			set { DispatcherQueueEventId = (string)value; }
		}

		[NotMapped]
		public string TableName => "DispatcherQueueEvents";

		[NotMapped]
		public string IdName => "DispatcherQueueEventId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A message in a routed conversation (M0324): what the sender wrote, what the dispatcher replied, desk notes.</summary>
	[Table("DispatcherConversationMessages")]
	public class DispatcherConversationMessage : IEntity
	{
		public const int BodyMaxLength = 4000;

		[Key]
		[Required]
		public string DispatcherConversationMessageId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[Required]
		public string DispatcherQueueItemId { get; set; }

		/// <summary><see cref="DispatcherMessageDirections"/>.</summary>
		public int Direction { get; set; }

		[MaxLength(256)]
		public string FromAddress { get; set; }

		[MaxLength(256)]
		public string ToAddress { get; set; }

		public string Body { get; set; }

		/// <summary>The dispatcher who wrote an outbound message or note.</summary>
		[MaxLength(128)]
		public string UserId { get; set; }

		public DateTime CreatedOn { get; set; }

		[MaxLength(128)]
		public string ProviderMessageId { get; set; }

		/// <summary><see cref="DispatcherMessageDeliveryStates"/>.</summary>
		public int DeliveryState { get; set; }

		[MaxLength(500)]
		public string Error { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DispatcherConversationMessageId; }
			set { DispatcherConversationMessageId = (string)value; }
		}

		[NotMapped]
		public string TableName => "DispatcherConversationMessages";

		[NotMapped]
		public string IdName => "DispatcherConversationMessageId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
