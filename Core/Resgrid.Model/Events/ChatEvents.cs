namespace Resgrid.Model.Events
{
	/// <summary>
	/// Envelope for every realtime chat event. Published by the chat services via IEventAggregator,
	/// relayed by OutboundEventProvider onto the eventing topic (EventingTypes.ChatEvent) and routed by
	/// the eventing host Worker to SignalR client events based on <see cref="Kind"/>.
	/// </summary>
	public class ChatEventRaised
	{
		public int DepartmentId { get; set; }

		public string ChatChannelId { get; set; }

		/// <summary>One of <see cref="ChatEventKinds"/>.</summary>
		public string Kind { get; set; }

		/// <summary>
		/// Serialized DTO for the client (message payload, receipt update, moderation notice, ...).
		/// Kept as JSON so the eventing host can relay it without referencing service types.
		/// </summary>
		public string PayloadJson { get; set; }

		/// <summary>Target a single user's devices instead of the channel group (chatbot, DM invites, badges).</summary>
		public string TargetUserId { get; set; }
	}

	/// <summary>SignalR client event names for chat; the eventing Worker maps Kind straight to these.</summary>
	public static class ChatEventKinds
	{
		public const string MessageReceived = "chatMessageReceived";
		public const string MessageEdited = "chatMessageEdited";
		public const string MessageDeleted = "chatMessageDeleted";
		public const string ReactionUpdated = "chatReactionUpdated";
		public const string ReceiptUpdated = "chatReceiptUpdated";
		public const string ChannelUpdated = "chatChannelUpdated";
		public const string ChannelProvisioned = "chatChannelProvisioned";
		public const string ModerationApplied = "chatModerationApplied";
		public const string AckRequired = "chatMessageAckRequired";
		public const string ThreadUpdated = "chatThreadUpdated";
		public const string ChatbotMessageReceived = "chatbotMessageReceived";
		public const string ChatbotTyping = "chatbotTyping";
		public const string AccessRevoked = "chatAccessRevoked";
	}

	/// <summary>
	/// Payload for <see cref="ChatEventKinds.AccessRevoked"/> (ban/remove/lock). Tells the eventing
	/// host which user lost access to which channel so it can evict their connections from the
	/// channel group and notify their devices.
	/// </summary>
	public class ChatAccessRevokedPayload
	{
		public string ChannelId { get; set; }
		public string UserId { get; set; }
	}
}
