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

		// Domain-event envelope (RMS plan section 5.6). EventId is the immutable idempotency key: one initial run
		// per (WorkflowId, EventId), retries reuse the run. Null for the legacy in-process events.
		[MaxLength(36)]
		public string EventId { get; set; }

		public int? EventSchemaVersion { get; set; }

		[MaxLength(36)]
		public string CorrelationId { get; set; }

		[MaxLength(36)]
		public string CausationId { get; set; }

		/// <summary>Per-aggregate sequence from the outbox, so out-of-order execution is diagnosable.</summary>
		public long? RecordSequence { get; set; }

		/// <summary>Maps to RmsOriginClient for Records events.</summary>
		public int? OriginClient { get; set; }

		[MaxLength(36)]
		public string AggregateId { get; set; }

		/// <summary>Why the run was recorded as Skipped without executing (WorkflowRunSkipReasons); null for executed runs.</summary>
		[MaxLength(100)]
		public string SkipReason { get; set; }

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
