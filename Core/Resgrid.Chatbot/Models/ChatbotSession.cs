using System;
using System.Collections.Generic;

namespace Resgrid.Chatbot.Models
{
	public enum ChatbotDialogState
	{
		Idle = 0,
		Classifying = 1,
		Dispatching = 2,
		AwaitingResponse = 3,
		AwaitingConfirmation = 4,
		AwaitingParameter = 5,
		AwaitingAuth = 6,
		Completed = 7,

		/// <summary>
		/// Step-up security-PIN prompt for a confirmed destructive action (the user already replied
		/// YES; the pending intent executes once the correct PIN is supplied).
		/// </summary>
		AwaitingPin = 8
	}

	public class ChatbotSession
	{
		public string SessionId { get; set; }
		public string UserId { get; set; }
		public int DepartmentId { get; set; }
		public ChatbotPlatform Platform { get; set; }

		/// <summary>
		/// The resolved user's preferred culture code (e.g. "en", "es"), from <c>UserProfile.Language</c>,
		/// set by the ingress when the session is created. Handlers pass this to <c>ChatbotResources.Get</c>
		/// so responses render in the user's language; null/unknown falls back to English at lookup time.
		/// </summary>
		public string Culture { get; set; }

		public ChatbotDialogState State { get; set; }
		public ChatbotIntentType? PendingIntent { get; set; }
		public Dictionary<string, string> Context { get; set; } = new Dictionary<string, string>();
		public List<ChatbotMessage> RecentMessages { get; set; } = new List<ChatbotMessage>();
		public DateTime CreatedAt { get; set; }
		public DateTime LastActivity { get; set; }
		public int TtlMinutes { get; set; } = 30;

		public bool IsExpired()
		{
			return DateTime.UtcNow > LastActivity.AddMinutes(TtlMinutes);
		}

		public void Reset()
		{
			State = ChatbotDialogState.Idle;
			PendingIntent = null;
			Context.Clear();
			RecentMessages.Clear();
		}
	}
}
