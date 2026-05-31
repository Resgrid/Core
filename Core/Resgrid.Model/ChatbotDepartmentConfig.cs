using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// Per-department chatbot configuration (ChatbotDepartmentConfigs table). Source of truth for a
	/// tenant's chatbot behavior; global <c>ChatbotConfig</c> statics are defaults when no row exists
	/// or a field is unset.
	///
	/// Note: the NLU *provider type* (keyword/ml/cloud) is intentionally a SYSTEM-level setting and is
	/// NOT configured here. A department MAY supply its own LLM/AI endpoint+key+model
	/// (<see cref="LlmApiEndpoint"/>/<see cref="LlmApiKey"/>/<see cref="LlmModelName"/>) so that, when
	/// the system uses cloud NLU, that department's processing stays with their own provider. The
	/// <c>NluProvider</c> column that exists on the table is deliberately left unmapped/unused.
	/// </summary>
	[Table("ChatbotDepartmentConfigs")]
	public class ChatbotDepartmentConfig : IEntity
	{
		public string Id { get; set; }

		public int DepartmentId { get; set; }

		public bool IsEnabled { get; set; }

		/// <summary>Comma-separated platform names this department allows, or "*" for all.</summary>
		public string AllowedPlatforms { get; set; } = "*";

		public int MaxSessionsPerUser { get; set; } = 3;

		public int SessionTtlMinutes { get; set; } = 30;

		public bool AllowDispatchViaChatbot { get; set; }

		public bool RequireConfirmationForStatusChange { get; set; }

		// --- Per-department LLM/AI override (added M0070). Key is encrypted at rest. ---
		public string LlmApiEndpoint { get; set; }
		public string LlmApiKey { get; set; }
		public string LlmModelName { get; set; }

		// --- Per-department rate limits (null => fall back to system defaults). ---
		public int? MessagesPerUserPerMinute { get; set; }
		public int? MessagesPerDepartmentPerMinute { get; set; }

		public bool RequireLinkingConfirmation { get; set; } = true;
		public bool ProactiveNotificationsEnabled { get; set; }

		public DateTime CreatedAt { get; set; }
		public DateTime? UpdatedAt { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get => Id;
			set => Id = (string)value;
		}

		[NotMapped] public string TableName => "ChatbotDepartmentConfigs";
		[NotMapped] public string IdName => "Id";
		[NotMapped] public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties =>
			new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
