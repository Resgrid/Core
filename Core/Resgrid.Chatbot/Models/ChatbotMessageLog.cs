using System;

namespace Resgrid.Chatbot.Models
{
	public class ChatbotMessageLog
	{
		public string Id { get; set; }
		public int DepartmentId { get; set; }
		public string UserId { get; set; }
		public string SessionId { get; set; }
		public ChatbotPlatform Platform { get; set; }
		public string Direction { get; set; } // "inbound" or "outbound"
		public string MessageText { get; set; }
		public ChatbotIntentType? IntentType { get; set; }
		public bool Processed { get; set; }
		public string ErrorInfo { get; set; }
		public DateTime Timestamp { get; set; }
	}
}
