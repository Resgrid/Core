namespace Resgrid.Web.Areas.User.Models
{
	public class ChatbotSettingsModel : BaseUserModel
	{
		/// <summary>Null = no save attempted, true = saved, false = save failed. Drives a localized alert.</summary>
		public bool? Saved { get; set; }

		public bool IsEnabled { get; set; }

		/// <summary>Comma-separated platform names allowed for this department, or "*" for all.</summary>
		public string AllowedPlatforms { get; set; } = "*";

		public bool AllowDispatchViaChatbot { get; set; }

		public bool RequireConfirmationForStatusChange { get; set; }

		public bool RequireLinkingConfirmation { get; set; } = true;

		public bool ProactiveNotificationsEnabled { get; set; }

		public int? MessagesPerUserPerMinute { get; set; }

		public int? MessagesPerDepartmentPerMinute { get; set; }

		// Department's own LLM/AI provider (optional). When set, the chatbot keeps this department's
		// processing with their provider instead of the Resgrid system LLM.
		public string LlmApiEndpoint { get; set; }
		public string LlmModelName { get; set; }

		/// <summary>Write-only: a new API key to store. Never populated on read (see HasLlmApiKey).</summary>
		public string LlmApiKey { get; set; }

		/// <summary>True when an LLM API key is already stored (so the UI can indicate it without exposing it).</summary>
		public bool HasLlmApiKey { get; set; }
	}
}
