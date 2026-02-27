using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	public class WorkflowRunLog : IEntity
	{
		public string WorkflowRunLogId { get; set; }

		[Required]
		public string WorkflowRunId { get; set; }

		[ForeignKey("WorkflowRunId")]
		public virtual WorkflowRun WorkflowRun { get; set; }

		[Required]
		public string WorkflowStepId { get; set; }

		[ForeignKey("WorkflowStepId")]
		public virtual WorkflowStep WorkflowStep { get; set; }

		/// <summary>Maps to <see cref="WorkflowRunStatus"/>.</summary>
		public int Status { get; set; }

		/// <summary>The Scriban-rendered content that was passed to the action executor.</summary>
		public string RenderedOutput { get; set; }

		[MaxLength(4000)]
		public string ActionResult { get; set; }

		[MaxLength(4000)]
		public string ErrorMessage { get; set; }

		public DateTime StartedOn { get; set; }

		public DateTime? CompletedOn { get; set; }

		public long? DurationMs { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get => WorkflowRunLogId;
			set => WorkflowRunLogId = (string)value;
		}

		[NotMapped] public string TableName => "WorkflowRunLogs";
		[NotMapped] public string IdName => "WorkflowRunLogId";
		[NotMapped] public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties =>
			new[] { "IdValue", "IdType", "TableName", "IdName", "WorkflowRun", "WorkflowStep" };
	}
}
