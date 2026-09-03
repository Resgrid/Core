using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// One row of the reusable transactional domain-event outbox (RMS plan section 5.6, M0153). Written
	/// in the same transaction as the producing state change; delivered by the in-process post-commit
	/// dispatcher and swept by worker command 40. Transport only: the producer owns the event name,
	/// schema version, payload contract and trigger semantics. <see cref="PayloadJson"/> is the safe,
	/// already-projected payload (SafeWorkflowEvent built before serialization; never a protected value).
	/// </summary>
	[Table("DomainEventOutbox")]
	public class DomainEventOutboxEntry : IEntity
	{
		public long DomainEventOutboxId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>Immutable event identity; the (WorkflowId, EventId) deduplication key downstream.</summary>
		public string EventId { get; set; }

		public string ProducerSubsystem { get; set; }

		public string EventName { get; set; }

		public int SchemaVersion { get; set; }

		public string AggregateType { get; set; }

		public string AggregateId { get; set; }

		public int? AggregateVersion { get; set; }

		/// <summary>Per-aggregate sequence so delayed or out-of-order execution is diagnosable.</summary>
		public long Sequence { get; set; }

		/// <summary><see cref="WorkflowTriggerEventType"/> when the event maps to a Workflow trigger.</summary>
		public int? TriggerEventType { get; set; }

		public string PayloadJson { get; set; }

		public string CorrelationId { get; set; }

		public string CausationId { get; set; }

		/// <summary><see cref="RmsOriginClient"/>.</summary>
		public int OriginClient { get; set; }

		public string OriginWorkflowRunId { get; set; }

		public int HopCount { get; set; }

		/// <summary><see cref="DomainEventOutboxState"/>.</summary>
		public int State { get; set; }

		public int Attempts { get; set; }

		public DateTime? NextAttemptOn { get; set; }

		public string LastError { get; set; }

		public string LeaseOwner { get; set; }

		public DateTime? LeaseExpiresOn { get; set; }

		public DateTime OccurredOn { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime? DispatchedOn { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DomainEventOutboxId; }
			set { DomainEventOutboxId = Convert.ToInt64(value); }
		}

		[NotMapped]
		public string TableName => "DomainEventOutbox";

		[NotMapped]
		public string IdName => "DomainEventOutboxId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
