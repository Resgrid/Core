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

		/// <summary>
		/// GIF search provider: "giphy" is the only supported value (Tenor stopped accepting new API
		/// clients in January 2026 and was removed). Empty disables GIF search.
		/// </summary>
		public static string GifProvider = "";
		public static string GiphyApiKey = "";

		/// <summary>
		/// Giphy content-rating cap for GIF search/trending results. Allowed values: "g", "pg",
		/// "pg-13" — anything else (including "r") falls back to the workplace-safe default "g".
		/// </summary>
		public static string GifRating = "g";

		/// <summary>Allowed CDN hosts for GIF message metadata urls (https only); anything else is dropped server-side.</summary>
		public static string[] GifCdnHosts = new[]
		{
			"giphy.com", "i.giphy.com", "media.giphy.com",
			"media0.giphy.com", "media1.giphy.com", "media2.giphy.com", "media3.giphy.com", "media4.giphy.com"
		};

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
		public static bool ChatbotFallbackEnabled = false;

		/// <summary>Max chat messages a user can send per rate-limit window.</summary>
		public static int SendRateLimitPerWindow = 30;

		/// <summary>Max reactions a user can add per rate-limit window.</summary>
		public static int ReactionRateLimitPerWindow = 60;

		/// <summary>Max attachment uploads a user can perform per rate-limit window.</summary>
		public static int UploadRateLimitPerWindow = 10;

		/// <summary>Max GIF searches a user can perform per rate-limit window.</summary>
		public static int GifSearchRateLimitPerWindow = 20;

		/// <summary>Length of the per-user sliding rate-limit window in seconds.</summary>
		public static int RateLimitWindowSeconds = 10;

		/// <summary>Max transcript exports a department can request per export rate-limit window (bulk-PII exfiltration guard).</summary>
		public static int ExportRateLimitPerWindow = 10;

		/// <summary>Length of the per-department export rate-limit window in seconds (default 1 hour).</summary>
		public static int ExportRateLimitWindowSeconds = 3600;
	}
}
