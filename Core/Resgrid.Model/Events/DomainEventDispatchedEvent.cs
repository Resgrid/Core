namespace Resgrid.Model.Events
{
	/// <summary>
	/// Raised on the in-process event aggregator when the DomainEventOutbox dispatches a row (post-commit
	/// in the producing process, or from worker command 40's catch-up sweep). WorkflowEventProvider and
	/// other consumers subscribe to this; the outbox row remains the durable source until every intended
	/// consumer has acknowledged. The payload is already the safe projection.
	/// </summary>
	public class DomainEventDispatchedEvent
	{
		public int DepartmentId { get; set; }
		public string EventId { get; set; }
		public string ProducerSubsystem { get; set; }
		public string EventName { get; set; }
		public int SchemaVersion { get; set; }
		public string AggregateType { get; set; }
		public string AggregateId { get; set; }
		public long Sequence { get; set; }
		public int? TriggerEventType { get; set; }
		public string PayloadJson { get; set; }
		public string CorrelationId { get; set; }
		public string CausationId { get; set; }
		public int OriginClient { get; set; }
		public bool IsReplay { get; set; }
		public System.DateTime OccurredOn { get; set; }
	}
}
