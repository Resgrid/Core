using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// Persistence entity for the ChatbotMessageLog table (created in M0068). Currently written only
	/// for chatbot messages the pipeline could NOT handle with a structured intent (no intent match,
	/// conversational-LLM fallback answered or failed, or the pipeline errored) so unmet feature
	/// requests can be mined per department. ErrorInfo carries the machine-readable reason.
	/// </summary>
	[Table("ChatbotMessageLog")]
	public class ChatbotMessageLog : IEntity
	{
		public const string ReasonUnmatched = "unmatched";
		public const string ReasonFallbackAnswered = "fallback_answered";
		public const string ReasonFallbackError = "fallback_error";
		public const string ReasonPipelineError = "pipeline_error";

		public string Id { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		public string UserId { get; set; }

		public string SessionId { get; set; }

		[Required]
		public int Platform { get; set; }

		[Required]
		public string Direction { get; set; }

		public string MessageText { get; set; }

		public int? IntentType { get; set; }

		public bool Processed { get; set; }

		public string ErrorInfo { get; set; }

		public DateTime Timestamp { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get => Id;
			set => Id = (string)value;
		}

		[NotMapped] public string TableName => "ChatbotMessageLog";
		[NotMapped] public string IdName => "Id";
		[NotMapped] public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties =>
			new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
