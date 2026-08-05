using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using ProtoBuf;

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
	[ProtoContract]
	public class ChatbotDepartmentConfig : IEntity
	{
		[ProtoMember(1)]
		public string Id { get; set; }

		[ProtoMember(2)]
		public int DepartmentId { get; set; }

		[ProtoMember(3)]
		public bool IsEnabled { get; set; }

		/// <summary>Comma-separated platform names this department allows, or "*" for all.</summary>
		[ProtoMember(4)]
		public string AllowedPlatforms { get; set; } = "*";

		[ProtoMember(5)]
		public int MaxSessionsPerUser { get; set; } = 3;

		[ProtoMember(6)]
		public int SessionTtlMinutes { get; set; } = 30;

		[ProtoMember(7)]
		public bool AllowDispatchViaChatbot { get; set; }

		[ProtoMember(8)]
		public bool RequireConfirmationForStatusChange { get; set; }

		// --- Per-department LLM/AI override (added M0070). Key is encrypted at rest. ---
		[ProtoMember(9)]
		public string LlmApiEndpoint { get; set; }
		[ProtoMember(10)]
		public string LlmApiKey { get; set; }
		[ProtoMember(11)]
		public string LlmModelName { get; set; }

		// --- Per-department rate limits (null => fall back to system defaults). ---
		[ProtoMember(12)]
		public int? MessagesPerUserPerMinute { get; set; }
		[ProtoMember(13)]
		public int? MessagesPerDepartmentPerMinute { get; set; }

		[DefaultValue(true)]
		[ProtoMember(14)]
		public bool RequireLinkingConfirmation { get; set; } = true;
		[ProtoMember(15)]
		public bool ProactiveNotificationsEnabled { get; set; }

		[ProtoMember(16)]
		public DateTime CreatedAt { get; set; }
		[ProtoMember(17)]
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
