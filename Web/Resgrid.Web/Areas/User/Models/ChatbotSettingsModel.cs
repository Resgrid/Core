using System.ComponentModel.DataAnnotations;
using Resgrid.Web.Attributes;

namespace Resgrid.Web.Areas.User.Models
{
	public class ChatbotSettingsModel : BaseUserModel
	{
		/// <summary>Null = no save attempted, true = saved, false = save failed. Drives a localized alert.</summary>
		public bool? Saved { get; set; }

		public bool IsEnabled { get; set; }

		/// <summary>Comma-separated platform names allowed for this department, or "*" for all.</summary>
		[StringLength(500, ErrorMessage = "Allowed platforms cannot exceed 500 characters.")]
		public string AllowedPlatforms { get; set; } = "*";

		public bool AllowDispatchViaChatbot { get; set; }

		public bool RequireConfirmationForStatusChange { get; set; }

		public bool RequireLinkingConfirmation { get; set; } = true;

		public bool ProactiveNotificationsEnabled { get; set; }

		public int? MessagesPerUserPerMinute { get; set; }

		public int? MessagesPerDepartmentPerMinute { get; set; }

		// Department's own LLM/AI provider (optional). When set, the chatbot keeps this department's
		// processing with their provider instead of the Resgrid system LLM.
		[StringLength(500, ErrorMessage = "API endpoint cannot exceed 500 characters.")]
		public string LlmApiEndpoint { get; set; }

		[StringLength(200, ErrorMessage = "Model name cannot exceed 200 characters.")]
		public string LlmModelName { get; set; }

		/// <summary>
		/// Write-only: a new API key to store. Never populated on read (see HasLlmApiKey).
		/// Cap is 700 UTF-8 bytes (encryption operates on bytes) so the AES+base64 ciphertext
		/// fits the 1000-char LlmApiKey column; StringLength adds the client-side char cap.
		/// </summary>
		[StringLength(700, ErrorMessage = "API key cannot exceed 700 characters.")]
		[MaxUtf8Bytes(700, ErrorMessage = "API key cannot exceed 700 bytes when UTF-8 encoded (non-ASCII characters count as multiple bytes).")]
		public string LlmApiKey { get; set; }

		/// <summary>True when an LLM API key is already stored (so the UI can indicate it without exposing it).</summary>
		public bool HasLlmApiKey { get; set; }
	}
}
