using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// The category of Resgrid traffic being delivered to a user's chat platforms. Used by
	/// <see cref="Resgrid.Model.Services.IChatbotOutboundService"/> so per-type delivery preferences can
	/// be honored as they are added.
	/// </summary>
	public enum ChatbotOutboundType
	{
		Dispatch = 0,
		Message = 1,
		Notification = 2,
		Reminder = 3
	}

	/// <summary>
	/// A platform-agnostic outbound payload handed to the chat delivery channel. The chatbot provider
	/// converts it into a platform-appropriate message.
	/// </summary>
	public class ChatbotOutboundMessage
	{
		public ChatbotOutboundType Type { get; set; }
		public string Title { get; set; }
		public string Body { get; set; }
		public string ReferenceId { get; set; }
	}

	/// <summary>Which chat platforms a single outbound send reached (for fallback decisions/auditing).</summary>
	public class ChatbotOutboundResult
	{
		public List<string> DeliveredPlatforms { get; set; } = new List<string>();
		public bool AnyDelivered => DeliveredPlatforms.Count > 0;
	}
}
