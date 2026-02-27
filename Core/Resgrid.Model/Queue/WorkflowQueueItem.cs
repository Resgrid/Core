using System;
using ProtoBuf;

namespace Resgrid.Model.Queue
{
	/// <summary>Message payload enqueued to RabbitMQ for async workflow execution.</summary>
	[ProtoContract]
	public class WorkflowQueueItem
	{
		[ProtoMember(1)]
		public string WorkflowId { get; set; }

		[ProtoMember(2)]
		public string WorkflowRunId { get; set; }

		[ProtoMember(3)]
		public int DepartmentId { get; set; }

		[ProtoMember(4)]
		public string DepartmentCode { get; set; }

		[ProtoMember(5)]
		public int TriggerEventType { get; set; }

		[ProtoMember(6)]
		public string EventPayloadJson { get; set; }

		[ProtoMember(7)]
		public int AttemptNumber { get; set; } = 1;

		[ProtoMember(8)]
		public DateTime EnqueuedOn { get; set; } = DateTime.UtcNow;
	}
}
