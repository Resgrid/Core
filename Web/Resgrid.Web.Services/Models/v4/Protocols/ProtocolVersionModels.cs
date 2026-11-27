using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Protocols
{
	/// <summary>One protocol version (P1). Content is included only for a single-version read.</summary>
	public class ProtocolVersionResultData
	{
		public string ProtocolVersionId { get; set; }
		public string ProtocolId { get; set; }
		/// <summary>"major.minor"; null while the version is a draft or in review.</summary>
		public string Label { get; set; }
		/// <summary>ProtocolVersionStatuses: 0 Draft, 1 InReview, 2 Approved, 3 Published, 4 Superseded, 5 Retired, 6 Legacy.</summary>
		public int Status { get; set; }
		public DateTime? EffectiveOn { get; set; }
		public DateTime? ExpiresOn { get; set; }
		/// <summary>SHA-256 (hex) of the sanitized content, the hash approvals and call snapshots carry.</summary>
		public string ContentHash { get; set; }
		/// <summary>The sanitized HTML (single-version reads only).</summary>
		public string ContentHtml { get; set; }
		/// <summary>The block document (schema 1: html blocks and heading anchors), single-version reads only.</summary>
		public string ContentJson { get; set; }
		public string ChangeSummary { get; set; }
		public bool RequiresAcknowledgment { get; set; }
		public string Locale { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime? SubmittedOn { get; set; }
		public DateTime? ApprovedOn { get; set; }
		public DateTime? PublishedOn { get; set; }
		/// <summary>The draft's row version: send it back with SaveDraft so a save over someone else's is refused.</summary>
		public long RowVersion { get; set; }
		public List<ProtocolApprovalResultData> Approvals { get; set; } = new List<ProtocolApprovalResultData>();
	}

	public class ProtocolApprovalResultData
	{
		public int Round { get; set; }
		public int StepOrder { get; set; }
		public string ApproverUserId { get; set; }
		public int? ApproverRoleId { get; set; }
		/// <summary>ProtocolApprovalDecisions: 0 Pending, 1 Approved, 2 Rejected, 3 Withdrawn.</summary>
		public int Decision { get; set; }
		public string DecidedByUserId { get; set; }
		public DateTime? DecidedOn { get; set; }
		public string Comment { get; set; }
		/// <summary>The L2 signature (SignatureRecords) the approval carries.</summary>
		public int? SignatureRecordId { get; set; }
	}

	public class GetProtocolVersionsResult : StandardApiResponseV4Base
	{
		public List<ProtocolVersionResultData> Data { get; set; } = new List<ProtocolVersionResultData>();
	}

	public class GetProtocolVersionResult : StandardApiResponseV4Base
	{
		public ProtocolVersionResultData Data { get; set; }
	}

	/// <summary>A draft save (P1). <see cref="ExpectedRowVersion"/> is the draft's RowVersion as read.</summary>
	public class SaveProtocolDraftInput
	{
		public int ProtocolId { get; set; }
		public string ContentHtml { get; set; }
		/// <summary>P3 (optional): a block document (schema 2) for the draft; when set, <see cref="ContentHtml"/> is ignored.</summary>
		public string ContentJson { get; set; }
		public string ChangeSummary { get; set; }
		public bool RequiresAcknowledgment { get; set; }
		public string Locale { get; set; }
		public long ExpectedRowVersion { get; set; }
	}

	public class ProtocolVersionActionInput
	{
		public int ProtocolVersionId { get; set; }
		/// <summary>Submit: the change summary. Approve and reject: the comment (required to reject).</summary>
		public string Comment { get; set; }
	}

	public class PublishProtocolVersionInput
	{
		public int ProtocolVersionId { get; set; }
		/// <summary>True for a new major version (2.0), false for a minor one (1.3).</summary>
		public bool Major { get; set; }
		public string ChangeSummary { get; set; }
		/// <summary>UTC; null or now publishes at once, a later time schedules it (worker 84 activates it).</summary>
		public DateTime? EffectiveOnUtc { get; set; }
		public DateTime? ExpiresOnUtc { get; set; }
	}

	public class RecordProtocolReviewInput
	{
		public int ProtocolId { get; set; }
	}

	/// <summary>The outcome of a lifecycle action: the version, or why it was refused (with who changed it, for a stale save).</summary>
	public class ProtocolLifecycleActionResult : StandardApiResponseV4Base
	{
		public ProtocolVersionResultData Data { get; set; }
		/// <summary>The refusal (ProtocolLifecycleErrors name, e.g. "StaleCopy", "SignatureRequired"), or null.</summary>
		public string Error { get; set; }
		public string ConflictUserId { get; set; }
		public DateTime? ConflictOn { get; set; }
		public int? SignatureRecordId { get; set; }
	}

	public class ProtocolReviewQueueItemData
	{
		public string ProtocolId { get; set; }
		public string Code { get; set; }
		public string Name { get; set; }
		public string ProtocolVersionId { get; set; }
		public DateTime? SubmittedOn { get; set; }
		public string SubmittedByUserId { get; set; }
		public int StepOrder { get; set; }
		public int StepsTotal { get; set; }
		public bool CanDecide { get; set; }
		public int OpenComments { get; set; }
	}

	public class GetProtocolReviewQueueResult : StandardApiResponseV4Base
	{
		public List<ProtocolReviewQueueItemData> Data { get; set; } = new List<ProtocolReviewQueueItemData>();
	}

	public class ProtocolSignatureResultData
	{
		public int SignatureRecordId { get; set; }
		public string SignerUserId { get; set; }
		public string SignerName { get; set; }
		/// <summary>SignatureIntents: 0 Attest, 1 Approve, 2 Review, 3 Witness, 4 Verify.</summary>
		public int Intent { get; set; }
		/// <summary>1 attest (session), 2 step-up.</summary>
		public int Level { get; set; }
		/// <summary>SignatureMethods: 0 Session, 1 StepUp, 2 Passkey.</summary>
		public int Method { get; set; }
		public string StatementText { get; set; }
		public string ArtifactSha256 { get; set; }
		public DateTime SignedOn { get; set; }
		/// <summary>SignatureVerificationStates: 0 Valid, 1 ArtifactChanged, 2 Voided, 3 SubjectMissing.</summary>
		public int Verification { get; set; }
	}

	public class GetProtocolSignaturesResult : StandardApiResponseV4Base
	{
		public List<ProtocolSignatureResultData> Data { get; set; } = new List<ProtocolSignatureResultData>();
	}
}
