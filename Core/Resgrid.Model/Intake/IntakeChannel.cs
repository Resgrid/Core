using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>Where an intake message arrives (persisted; never renumber).</summary>
	public enum IntakeChannelTypes
	{
		/// <summary>A text message to the department's dedicated number, or to the master Resgrid number from a listed sender.</summary>
		Sms = 1,

		/// <summary>Email to the department's intake address (local part of the dispatch email domain).</summary>
		Email = 2,

		/// <summary>WhatsApp message (Twilio WhatsApp sender).</summary>
		WhatsApp = 3,

		/// <summary>Slack message in a linked workspace channel (Resgrid's Slack app).</summary>
		Slack = 4,

		/// <summary>Microsoft Teams message in a linked team channel (Resgrid's Teams bot).</summary>
		Teams = 5,

		/// <summary>Telegram message in a linked chat (Resgrid's Telegram bot).</summary>
		Telegram = 6,

		/// <summary>Discord message in a linked channel (Resgrid's Discord bot).</summary>
		Discord = 7,

		/// <summary>Google Chat message in a linked space (Resgrid's Google Chat app).</summary>
		GoogleChat = 8,

		/// <summary>An HTTPS POST to the channel's signed webhook URL (any other system: CAD bridges, Zapier, alarm panels).</summary>
		Webhook = 9,

		/// <summary>A phone call to the department's dedicated number, taken by Enhanced AI voice intake.</summary>
		Voice = 10
	}

	/// <summary>How an intake message becomes a call (persisted).</summary>
	public enum IntakeProcessingModes
	{
		/// <summary>The department's call email/text template (<see cref="TemplateFormat"/>) parses the message; no AI.</summary>
		Template = 1,

		/// <summary>The message is saved as a pending call and Enhanced AI fills the type, address, priority and summary.</summary>
		EnhancedAi = 2
	}

	/// <summary>
	/// A department's intake channel (M0323): one address on one transport whose messages become PENDING calls a dispatcher
	/// reviews and sends. Channels never dispatch by themselves. The address format depends on <see cref="ChannelType"/>:
	/// SMS / Voice / WhatsApp = E.164 number ("+17755550100"); Email = the local part of the dispatch email domain; Slack =
	/// "teamId/channelId"; Teams = "tenantId/conversationId"; Telegram / Discord = chat or channel id; Google Chat = space name;
	/// Webhook = generated token (the URL path segment). No foreign keys; the department purge removes the rows.
	/// </summary>
	[Table("IntakeChannels")]
	public class IntakeChannel : IEntity
	{
		public const int MaxNameLength = 100;
		public const int MaxAddressLength = 256;

		[Key]
		[Required]
		public string IntakeChannelId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		/// <summary>Shown on the pending call ("Hospital transfer line", "Mutual aid Slack").</summary>
		[MaxLength(MaxNameLength)]
		public string Name { get; set; }

		/// <summary>An <see cref="IntakeChannelTypes"/> value.</summary>
		public int ChannelType { get; set; }

		/// <summary>The channel's address on its transport (normalized; see the class remarks).</summary>
		[MaxLength(MaxAddressLength)]
		public string Address { get; set; }

		/// <summary>An <see cref="IntakeProcessingModes"/> value.</summary>
		public int ProcessingMode { get; set; } = (int)IntakeProcessingModes.Template;

		/// <summary>A <see cref="CallEmailTypes"/> value for <see cref="IntakeProcessingModes.Template"/>; null = Generic.</summary>
		public int? TemplateFormat { get; set; }

		/// <summary>
		/// Comma-separated sender patterns allowed to create calls (phone numbers with * wildcards, email addresses or @domains,
		/// platform user ids); blank = anyone who can reach the address. Senders outside the list are recorded and ignored.
		/// </summary>
		public string AllowedSenders { get; set; }

		/// <summary>Priority for calls the template or AI leaves without one; null = the department default.</summary>
		public int? DefaultPriority { get; set; }

		/// <summary>Call type id for calls the template or AI leaves without one; null = none.</summary>
		public int? DefaultCallTypeId { get; set; }

		/// <summary>Reply to the sender that the message was received (sent from the master Resgrid number for SMS).</summary>
		public bool SendAcknowledgement { get; set; }

		/// <summary>
		/// Webhook channels only: the HMAC-SHA256 signing secret, stored protected (data protection purpose
		/// "Resgrid.Intake.WebhookSecret"); null for other channel types.
		/// </summary>
		public string WebhookSecretProtected { get; set; }

		public bool IsEnabled { get; set; } = true;

		public bool IsDeleted { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime CreatedOn { get; set; }

		public string UpdatedByUserId { get; set; }

		public DateTime? UpdatedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return IntakeChannelId; }
			set { IntakeChannelId = (string)value; }
		}

		[NotMapped]
		public string TableName => "IntakeChannels";

		[NotMapped]
		public string IdName => "IntakeChannelId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>What happened to an intake message (persisted).</summary>
	public enum IntakeMessageStates
	{
		Received = 1,

		/// <summary>A pending call was created (<see cref="IntakeMessage.CallId"/>).</summary>
		CallCreated = 2,

		/// <summary>The same provider message arrived again; nothing was created.</summary>
		Duplicate = 3,

		/// <summary>The sender is not on the channel's allowed list.</summary>
		SenderNotAllowed = 4,

		/// <summary>The channel is disabled, the feature is off, or the department may not use it (plan, add-on, fair-use limit).</summary>
		Refused = 5,

		/// <summary>Creating the call failed; <see cref="IntakeMessage.Reason"/> says why.</summary>
		Failed = 6,

		/// <summary>
		/// The sender already has an open SMS conversation with the department (plan §8 D15, M0325): the text joined it as a note on
		/// that conversation's call (<see cref="IntakeMessage.CallId"/>) instead of creating a second pending call.
		/// </summary>
		AddedToConversation = 7,

		/// <summary>
		/// The sender is on the department's blocked list (M0326, a dispatcher dismissed one of their calls as spam): nothing was
		/// created, nothing is sent back, and the message does not count toward the monthly volume.
		/// </summary>
		SenderBlocked = 8,

		/// <summary>
		/// The sender reached IntakeConfig.MaxCallsPerSenderPerHour (M0326): the message joined their newest open intake call as a note
		/// (<see cref="IntakeMessage.CallId"/>) instead of creating another pending call.
		/// </summary>
		AddedToCall = 9
	}

	/// <summary>
	/// Why an intake call is held out of the dispatcher desk's offers and badged on pending lists (M0326, persisted; never renumber).
	/// A screening only labels the call: it is still a pending call any dispatcher can open, dispatch or close.
	/// </summary>
	public enum IntakeScreenings
	{
		None = 0,

		/// <summary>Enhanced AI read the message as reporting no incident (spam, advertising, a wrong number, a test).</summary>
		LikelyNotIncident = 1,

		/// <summary>The sender already made IntakeConfig.MaxCallsPerSenderPerHour calls this hour and none of them is still open.</summary>
		RepeatSender = 2,

		/// <summary>A dispatcher dismissed the call as spam (it is cancelled).</summary>
		DismissedAsSpam = 3
	}

	/// <summary>
	/// One message received on an intake channel (M0323): the idempotency claim (unique per channel and provider message id)
	/// and the audit trail. The message text itself is not kept here; it lives on the pending call it created (where ADP
	/// protects it). Purged after <c>IntakeConfig.MessageRetentionDays</c> by worker 80.
	/// </summary>
	[Table("IntakeMessages")]
	public class IntakeMessage : IEntity
	{
		[Key]
		[Required]
		public string IntakeMessageId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		public string IntakeChannelId { get; set; }

		/// <summary>An <see cref="IntakeChannelTypes"/> value.</summary>
		public int ChannelType { get; set; }

		/// <summary>The transport's message id (Twilio MessageSid / CallSid, email Message-ID, Slack event ts…); unique per channel.</summary>
		[MaxLength(256)]
		public string ProviderMessageId { get; set; }

		/// <summary>Who sent it (number, email address, platform user id); not a member id.</summary>
		[MaxLength(256)]
		public string Sender { get; set; }

		/// <summary>An <see cref="IntakeMessageStates"/> value.</summary>
		public int State { get; set; }

		public int? CallId { get; set; }

		[MaxLength(1000)]
		public string Reason { get; set; }

		public DateTime ReceivedOn { get; set; }

		public DateTime? CompletedOn { get; set; }

		/// <summary>An <see cref="IntakeScreenings"/> value on the row that created the call (M0326); null = never screened.</summary>
		public int? Screening { get; set; }

		public DateTime? ScreenedOn { get; set; }

		/// <summary>The dispatcher who dismissed the call (<see cref="IntakeScreenings.DismissedAsSpam"/>); null for automatic screenings.</summary>
		public string ScreenedByUserId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return IntakeMessageId; }
			set { IntakeMessageId = (string)value; }
		}

		[NotMapped]
		public string TableName => "IntakeMessages";

		[NotMapped]
		public string IdName => "IntakeMessageId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A message handed to <c>IIntakeService</c> by a transport (SMS, email, chat, webhook, voice).</summary>
	public class IntakeEnvelope
	{
		/// <summary>The transport (<see cref="IntakeChannelTypes"/>).</summary>
		public IntakeChannelTypes ChannelType { get; set; }

		/// <summary>The address the message was sent to, normalized as <see cref="IntakeChannel.Address"/> is.</summary>
		public string ChannelAddress { get; set; }

		/// <summary>When the transport already knows the channel (e.g. webhook token lookup), its id; resolution is skipped.</summary>
		public string IntakeChannelId { get; set; }

		public string ProviderMessageId { get; set; }

		public string Sender { get; set; }

		/// <summary>Display name of the sender when the transport gives one.</summary>
		public string SenderName { get; set; }

		public string Subject { get; set; }

		/// <summary>The message text (for voice: the assembled transcript).</summary>
		public string Body { get; set; }

		public DateTime ReceivedOnUtc { get; set; }

		/// <summary>Voice intake: the session's answers by step, for the AI to read as structured input.</summary>
		public IDictionary<string, string> Answers { get; set; }

		/// <summary>Webhook: the sender system's own id for the request (kept on the call as its external identifier).</summary>
		public string ExternalId { get; set; }

		/// <summary>Webhook: a location the sender already knows (used when the template leaves the call without one).</summary>
		public string Address { get; set; }

		public double? Latitude { get; set; }

		public double? Longitude { get; set; }

		/// <summary>
		/// A refusal the transport already decided (the dedicated number lapsed, …): the message is claimed and recorded as Refused
		/// with this reason, and no call is created.
		/// </summary>
		public string RefusalReason { get; set; }

		/// <summary>
		/// SMS to a department's dedicated number (plan §8 D15): the text opens an SMS conversation continued from the master number
		/// (or joins the sender's open one with the department) instead of getting the one-off acknowledgement.
		/// </summary>
		public bool OpensConversation { get; set; }
	}

	/// <summary>The outcome of one intake message.</summary>
	public class IntakeResult
	{
		public IntakeMessageStates State { get; set; }

		public int DepartmentId { get; set; }

		public string IntakeChannelId { get; set; }

		public int? CallId { get; set; }

		public string Reason { get; set; }

		/// <summary>An acknowledgement the transport should send back (null = none).</summary>
		public string Acknowledgement { get; set; }

		/// <summary>The SMS conversation the message opened or joined (its welcome replaces the acknowledgement), else null.</summary>
		public string IntakeConversationId { get; set; }
	}
}
