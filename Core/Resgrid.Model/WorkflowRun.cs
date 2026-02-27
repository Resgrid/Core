using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	public class WorkflowRun : IEntity
	{
		public string WorkflowRunId { get; set; }

		[Required]
		public string WorkflowId { get; set; }

		[ForeignKey("WorkflowId")]
		public virtual Workflow Workflow { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		/// <summary>Maps to <see cref="WorkflowRunStatus"/>.</summary>
		public int Status { get; set; }

		/// <summary>Maps to <see cref="WorkflowTriggerEventType"/>.</summary>
		public int TriggerEventType { get; set; }

		/// <summary>JSON-serialized event payload that triggered this run.</summary>
		public string InputPayload { get; set; }

		public DateTime StartedOn { get; set; }

		public DateTime? CompletedOn { get; set; }

		[MaxLength(4000)]
		public string ErrorMessage { get; set; }

		public int AttemptNumber { get; set; } = 1;

		public DateTime QueuedOn { get; set; }

		public virtual ICollection<WorkflowRunLog> Logs { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get => WorkflowRunId;
			set => WorkflowRunId = (string)value;
		}

		[NotMapped] public string TableName => "WorkflowRuns";
		[NotMapped] public string IdName => "WorkflowRunId";
		[NotMapped] public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties =>
			new[] { "IdValue", "IdType", "TableName", "IdName", "Workflow", "Logs" };
	}
}
