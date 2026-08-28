using Newtonsoft.Json;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	/// <summary>
	/// A department-wide mutation freeze (reads continue) held by the ADP migration worker during an
	/// active migration window. At most one active (ReleasedUtc IS NULL) lock per department, enforced
	/// by a filtered/partial unique index. The worker heartbeats HeartbeatUtc; when the heartbeat goes
	/// stale past ExpiresUtc the lock reports Expired and enforcement ends automatically — dispatch
	/// availability beats migration progress.
	/// </summary>
	[Table("DepartmentOperationLocks")]
	[ProtoContract]
	public class DepartmentOperationLock : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int DepartmentOperationLockId { get; set; }

		[Required]
		[ProtoMember(2)]
		public int DepartmentId { get; set; }

		/// <summary>DepartmentOperationLockType value.</summary>
		[Required]
		[ProtoMember(3)]
		public int LockType { get; set; }

		/// <summary>Human-readable, value-free reason shown in client banners and BackOffice.</summary>
		[MaxLength(512)]
		[ProtoMember(4)]
		public string Reason { get; set; }

		[MaxLength(128)]
		[ProtoMember(5)]
		public string CorrelationId { get; set; }

		[ProtoMember(6)]
		public DateTime AppliedUtc { get; set; }

		/// <summary>Workload/user identity that applied the lock.</summary>
		[MaxLength(256)]
		[ProtoMember(7)]
		public string AppliedByIdentity { get; set; }

		[ProtoMember(8)]
		public DateTime HeartbeatUtc { get; set; }

		/// <summary>Safety valve — enforcement ends automatically once past this with a stale heartbeat.</summary>
		[ProtoMember(9)]
		public DateTime ExpiresUtc { get; set; }

		/// <summary>Projected end of the migration window, surfaced to clients in the lock banner.</summary>
		[ProtoMember(10)]
		public DateTime? ProjectedEndUtc { get; set; }

		[ProtoMember(11)]
		public DateTime? ReleasedUtc { get; set; }

		[MaxLength(256)]
		[ProtoMember(12)]
		public string ReleasedBy { get; set; }

		/// <summary>DepartmentOperationLockReleaseKind value; null while active.</summary>
		[ProtoMember(13)]
		public int? ReleaseKind { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DepartmentOperationLockId; }
			set { DepartmentOperationLockId = (int)value; }
		}

		[NotMapped]
		public string TableName => "DepartmentOperationLocks";

		[NotMapped]
		public string IdName => "DepartmentOperationLockId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
