namespace Resgrid.Config
{
	public static class ChatbotConfig
	{
		// General
		// NOTE: Chatbot enable/disable is governed by the Feature Toggle service
		// (FeatureFlagKeys.ChatbotTwilioTextIntegration), not by a config flag.
		public static int DefaultSessionTimeoutMinutes = 30;

		// NLU Configuration
		public static NluProviderType NluProvider = NluProviderType.Keyword;
		public static string MlNetModelPath = "";
		public static float MinimumIntentConfidence = 0.65f;
		public static float MinimumCloudConfidence = 0.75f;

		// Cloud LLM NLU Configuration
		public static CloudNluProviderType CloudNluProvider = CloudNluProviderType.OpenAiCompatible;
		public static string CloudNluApiEndpoint = "";
		public static string CloudNluApiKey = "";
		public static string CloudNluModelName = "deepseek-chat";
		public static int CloudNluTimeoutSeconds = 10;
		public static int CloudNluMaxRetries = 2;
		public static int CloudNluMaxTokens = 512;
		public static float CloudNluTemperature = 0.0f;
		public static string CloudNluSystemPrompt = "";
		public static bool CloudNluFallbackToKeyword = true;

		// Rate Limiting
		public static int MessagesPerUserPerMinute = 30;
		public static int MessagesPerDepartmentPerMinute = 120;

		// Session Store
		public static bool UseRedisSessionStore = false;
		public static string RedisConnectionString = "";

		// Platform Tokens (set via appsettings.json or environment variables)
		public static string DiscordBotToken = "";
		public static string SlackBotToken = "";
		public static string SlackAppToken = "";
		public static string TelegramBotToken = "";
		// Secret token sent by Telegram in the X-Telegram-Bot-Api-Secret-Token header
		// (configured via setWebhook). When set, inbound webhooks lacking a matching token are rejected.
		public static string TelegramWebhookSecretToken = "";
		public static string LinkingBaseUrl = "";

		// OAuth2 app credentials for Discord/Slack account linking (server-side code exchange).
		public static string DiscordClientId = "";
		public static string DiscordClientSecret = "";
		public static string SlackClientId = "";
		public static string SlackClientSecret = "";
		// Redirect URI registered with the OAuth apps; the platform returns the code here.
		public static string OAuthRedirectUri = "";

		// Message Logging
		public static int MessageLogRetentionDays = 90;
		public static bool LogMessageContent = true;

		// Linking Codes
		public static int LinkingCodeLength = 4;
		public static int LinkingCodeExpiryMinutes = 5;
		public static int MaxLinkingCodesPerUserPerDay = 3;

		// Proactive Notifications (Phase 4)
		public static bool EnableProactiveNotifications = false;
	}

	public enum NluProviderType
	{
		Keyword = 0,
		MLNet = 1,
		HybridLocal = 2,
		Cloud = 3,
		HybridCloud = 4
	}

	public enum CloudNluProviderType
	{
		OpenAiCompatible = 0,
		DeepSeek = 1,
		OpenAI = 2,
		AzureOpenAI = 3,
		Anthropic = 4
	}
}
