namespace Resgrid.Config
{
	public static class ChatConfig
	{
		public static string DepartmentChatPrefix = "D1_";
		public static string DepartmentGroupChatPrefix = "DG1_";
		public static string NovuBackendUrl = "";
		public static string NovuSocketUrl = "";
		public static string NovuEnvironmentId = "";
		public static string NovuApplicationId = "";
		public static string NovuSecretKey = "";

		public static string NovuUnitFcmProviderId = "firebase-cloud-messaging-7Z5wHFPpQ";
		public static string NovuUnitApnsProviderId = "unit-apns";
		public static string NovuResponderFcmProviderId = "respond-firebase-cloud-messaging";
		public static string NovuResponderApnsProviderId = "respond-apns";
		public static string NovuICFcmProviderId = "ic-firebase-cloud-messaging";
		public static string NovuICApnsProviderId = "ic-apns";
		public static string NovuDispatchUnitWorkflowId = "unit-dispatch";
		public static string NovuDispatchUserWorkflowId = "user-dispatch";
		public static string NovuMessageUserWorkflowId = "user-message";
		public static string NovuNotificationUserWorkflowId = "user-notification";

		/// <summary>Novu workflow triggered for realtime chat message push notifications.</summary>
		public static string NovuChatWorkflowId = "user-chat-message";

		/// <summary>GIF search provider: "giphy" or "tenor". Empty disables GIF search.</summary>
		public static string GifProvider = "";
		public static string GiphyApiKey = "";
		public static string TenorApiKey = "";

		public static int MaxMessageLength = 4000;
		public static int MaxAttachmentSizeMb = 10;

		/// <summary>Default per-department chat retention in days when no ChatDepartmentSettings row exists (0 = keep forever).</summary>
		public static int DefaultRetentionDays = 0;

		/// <summary>Minimum ms between typing-indicator rebroadcasts per user per channel.</summary>
		public static int TypingThrottleMs = 3000;

		/// <summary>TTL for chat presence entries in Redis.</summary>
		public static int PresenceTtlSeconds = 60;

		public static bool LinkPreviewEnabled = true;
		public static int LinkPreviewTimeoutMs = 5000;

		/// <summary>Allows the chatbot to fall back to conversational LLM replies when no intent matches.</summary>
		public static bool ChatbotFallbackEnabled = true;
	}
}
