using System;
using Newtonsoft.Json.Linq;

namespace Resgrid.Model.Events
{
	/// <summary>
	/// A native Records trigger (WorkflowTriggerEventType 100-115) as WorkflowEventProvider hands it to the
	/// workflow pipeline: the outbox envelope plus the already-safe payload (record / record_change / extra)
	/// parsed into a JObject, so ADP redaction and WorkflowTemplateContextBuilder both see fields rather
	/// than an opaque JSON string. Built from the DomainEventDispatchedEvent the outbox dispatcher publishes;
	/// nothing here is rehydrated from current record state (RMS plan section 5.6).
	/// </summary>
	public class RecordsWorkflowEvent
	{
		public int DepartmentId { get; set; }
		public string EventId { get; set; }
		public string EventName { get; set; }
		public int SchemaVersion { get; set; }
		public string AggregateType { get; set; }
		public string AggregateId { get; set; }
		public long Sequence { get; set; }
		public int TriggerEventType { get; set; }
		public string CorrelationId { get; set; }
		public string CausationId { get; set; }

		/// <summary>Safe origin name (Web, Responder, Unit, IncidentCommand, Dispatch, Api, System); never a device identifier.</summary>
		public string OriginClient { get; set; }
		public bool IsReplay { get; set; }
		public DateTime OccurredOn { get; set; }

		/// <summary>The dispatched payload: <c>record</c>, <c>record_change</c> and optional <c>extra</c> objects.</summary>
		public JObject Payload { get; set; } = new JObject();

		public static RecordsWorkflowEvent From(DomainEventDispatchedEvent dispatched)
		{
			if (dispatched == null)
				return null;

			JObject payload;
			try
			{
				payload = string.IsNullOrWhiteSpace(dispatched.PayloadJson) ? new JObject() : JObject.Parse(dispatched.PayloadJson);
			}
			catch (Exception)
			{
				// A malformed payload still yields a well-formed event; the template simply sees empty namespaces.
				payload = new JObject();
			}

			return new RecordsWorkflowEvent
			{
				DepartmentId = dispatched.DepartmentId,
				EventId = dispatched.EventId,
				EventName = dispatched.EventName,
				SchemaVersion = dispatched.SchemaVersion,
				AggregateType = dispatched.AggregateType,
				AggregateId = dispatched.AggregateId,
				Sequence = dispatched.Sequence,
				TriggerEventType = dispatched.TriggerEventType.GetValueOrDefault(),
				CorrelationId = dispatched.CorrelationId,
				CausationId = dispatched.CausationId,
				OriginClient = Enum.IsDefined(typeof(RmsOriginClient), dispatched.OriginClient)
					? ((RmsOriginClient)dispatched.OriginClient).ToString()
					: RmsOriginClient.System.ToString(),
				IsReplay = dispatched.IsReplay,
				OccurredOn = dispatched.OccurredOn,
				Payload = payload
			};
		}
	}
}
