using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// Records (RMS) operational Record header: the aggregate root for locked Logs-parity types and, later,
	/// department-owned definitions (RMS plan section 5.2). Pins the definition/version, lifecycle preset
	/// and state, numbering, ownership, review/approval/finalize/void/cancel facts and the ETag
	/// RowVersion. Typed field values live in <see cref="RmsOperationalRecordDetail"/>; immutable
	/// history in <see cref="RmsRevision"/>. Exposes the cross-plan RmsRecord reference shape
	/// (DepartmentId, RecordId, RecordKind, RevisionId).
	/// </summary>
	[Table("RmsOperationalRecords")]
	public class RmsOperationalRecord : IEntity
	{
		/// <summary>Client-compatible GUID string; assigned by the client for offline drafts or by the server.</summary>
		public string RmsOperationalRecordId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>Immutable random ID allocated at insert; read by nothing until Protected Data enrollment (plan section 5.9.1).</summary>
		public string ProtectionId { get; set; }

		/// <summary>Stable definition key, e.g. <see cref="RmsDefinitionKeys.Training"/>.</summary>
		public string DefinitionKey { get; set; }

		public int DefinitionVersion { get; set; }

		/// <summary><see cref="RmsOperationalRecordType"/> for locked definitions; null for department-owned ones.</summary>
		public int? RecordType { get; set; }

		/// <summary><see cref="RmsLifecyclePreset"/> the Record was created under.</summary>
		public int LifecyclePreset { get; set; }

		/// <summary><see cref="RmsRecordState"/>.</summary>
		public int State { get; set; }

		/// <summary>Authoritative record number, assigned at finalization by default (plan section 4.1). Never reused.</summary>
		public string RecordNumber { get; set; }

		/// <summary>Non-authoritative draft reference issued at creation, e.g. D-7KQ2M. Retained on the finalized Record.</summary>
		public string DraftReference { get; set; }

		/// <summary>Server-authored list/queue display text; never a protected-candidate value.</summary>
		public string DisplaySummary { get; set; }

		public int? StationGroupId { get; set; }

		public int? CallId { get; set; }

		public string ExternalId { get; set; }

		public string AuthorUserId { get; set; }

		/// <summary>Author's group at creation; a snapshot, so a later transfer never rewrites visibility.</summary>
		public int? AuthorGroupIdSnapshot { get; set; }

		/// <summary>Current draft owner; equals the author unless reassigned (Record_Reassign).</summary>
		public string OwnerUserId { get; set; }

		public DateTime? StartedOn { get; set; }

		public DateTime? EndedOn { get; set; }

		public DateTime? ReviewDueOn { get; set; }

		public DateTime? SubmittedForReviewOn { get; set; }

		public DateTime? ReturnedOn { get; set; }

		public string ReturnReasonCode { get; set; }

		public string ReturnReasonText { get; set; }

		/// <summary>Audited review round-trip count.</summary>
		public int ReturnCount { get; set; }

		public string ReviewerUserId { get; set; }

		public DateTime? ApprovedOn { get; set; }

		public string ApproverUserId { get; set; }

		public DateTime? FinalizedOn { get; set; }

		public string FinalizedByUserId { get; set; }

		/// <summary>The latest immutable revision; null until first finalization.</summary>
		public string CurrentRevisionId { get; set; }

		public int RevisionCount { get; set; }

		/// <summary>Non-null while an amendment draft is open against that revision (at most one per Record).</summary>
		public string AmendsRevisionId { get; set; }

		public DateTime? VoidedOn { get; set; }

		public string VoidedByUserId { get; set; }

		public string VoidReasonCode { get; set; }

		public string VoidReasonText { get; set; }

		public DateTime? CancelledOn { get; set; }

		public string CancelledByUserId { get; set; }

		/// <summary>Scoped idempotency key for create; unique per department when present.</summary>
		public string IdempotencyKey { get; set; }

		/// <summary><see cref="RmsOriginClient"/>.</summary>
		public int OriginClient { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime ModifiedOn { get; set; }

		public string ModifiedByUserId { get; set; }

		/// <summary>Optimistic-concurrency counter (ETag). Incremented by the service on every draft save.</summary>
		public long RowVersion { get; set; }

		/// <summary>When the retention sweep purged the content (RMS-3, worker 43); the row itself survives as a tombstone.</summary>
		public DateTime? PurgedOn { get; set; }

		public DateTime? DeletedOn { get; set; }

		[NotMapped]
		public string RecordId => RmsOperationalRecordId;

		[NotMapped]
		public RmsRecordKind RecordKind => RmsRecordKind.Operational;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return RmsOperationalRecordId; }
			set { RmsOperationalRecordId = value?.ToString(); }
		}

		[NotMapped]
		public string TableName => "RmsOperationalRecords";

		[NotMapped]
		public string IdName => "RmsOperationalRecordId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "RecordId", "RecordKind" };
	}
}
