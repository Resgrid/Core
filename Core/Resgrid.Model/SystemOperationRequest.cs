using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// A request for the worker (command 76) to run one <see cref="SystemOperationTypes"/> operation now (registry M0267).
	/// BackOffice inserts Pending rows; the worker claims the oldest, keeps HeartbeatOn fresh while it runs, and records
	/// the outcome. The table is the durable trigger path on purpose: the bus queues are not durable and the cache may be
	/// the thing that just failed. A system record, not department data: TargetDepartmentId only narrows the operation.
	/// </summary>
	public class SystemOperationRequest : IEntity
	{
		public const int ReasonMaxLength = 500;
		public const int ProgressMaxLength = 500;
		public const int ResultMaxLength = 2000;

		[Key]
		[Required]
		[MaxLength(128)]
		public string SystemOperationRequestId { get; set; }

		/// <summary>A <see cref="SystemOperationTypes"/> value.</summary>
		public int OperationType { get; set; }

		/// <summary>The one department the operation covers, or null for every department (or no department).</summary>
		public int? TargetDepartmentId { get; set; }

		/// <summary>A <see cref="SystemOperationStatuses"/> value.</summary>
		public int Status { get; set; }

		/// <summary>A <see cref="SystemOperationSources"/> value.</summary>
		public int Source { get; set; }

		/// <summary>The staff member's e-mail (or subject), or "system" for an automatic request.</summary>
		[Required]
		[MaxLength(256)]
		public string RequestedBy { get; set; }

		[MaxLength(ReasonMaxLength)]
		public string Reason { get; set; }

		public DateTime RequestedOn { get; set; }

		public DateTime? StartedOn { get; set; }

		/// <summary>Refreshed by the running worker; a Running row whose heartbeat goes stale was abandoned.</summary>
		public DateTime? HeartbeatOn { get; set; }

		public DateTime? CompletedOn { get; set; }

		/// <summary>Machine and process that claimed the request.</summary>
		[MaxLength(256)]
		public string WorkerName { get; set; }

		/// <summary>The latest progress line the operation reported while running.</summary>
		[MaxLength(ProgressMaxLength)]
		public string Progress { get; set; }

		/// <summary>The outcome summary, or the failure.</summary>
		[MaxLength(ResultMaxLength)]
		public string Result { get; set; }

		[MaxLength(256)]
		public string CancelledBy { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get => SystemOperationRequestId;
			set => SystemOperationRequestId = (string)value;
		}

		[NotMapped]
		public string TableName => "SystemOperationRequests";

		[NotMapped]
		public string IdName => "SystemOperationRequestId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties =>
			new[] { "IdValue", "IdType", "TableName", "IdName" };

		[NotMapped]
		[JsonIgnore]
		public SystemOperationTypes OperationTypeValue => (SystemOperationTypes)OperationType;

		[NotMapped]
		[JsonIgnore]
		public SystemOperationStatuses StatusValue => (SystemOperationStatuses)Status;

		[NotMapped]
		[JsonIgnore]
		public bool IsFinished => Status is (int)SystemOperationStatuses.Completed or (int)SystemOperationStatuses.Failed or (int)SystemOperationStatuses.Cancelled;
	}
}
