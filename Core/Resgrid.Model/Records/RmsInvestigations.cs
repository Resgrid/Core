using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	public enum RmsInvestigationCaseState
	{
		Open = 1,
		Active = 2,
		/// <summary>Findings recorded; awaiting approval by a reviewer who is not their author.</summary>
		PendingReview = 3,
		Closed = 4
	}

	/// <summary>Case-level role (RMS plan section 4.4: "restricted investigator roles and case-level authorization").</summary>
	public enum RmsInvestigationRole
	{
		Lead = 1,
		Investigator = 2,
		/// <summary>May approve findings; may not author them.</summary>
		Reviewer = 3,
		ReadOnly = 4
	}

	/// <summary>NFPA 921 cause classification.</summary>
	public enum RmsFireCauseClassification
	{
		UnderInvestigation = 0,
		Accidental = 1,
		Natural = 2,
		Incendiary = 3,
		Undetermined = 4
	}

	public enum RmsInvestigationNoteKind
	{
		SceneNote = 1,
		Interview = 2,
		Observation = 3,
		Diagram = 4,
		Timeline = 5,
		Other = 6
	}

	public enum RmsInvestigationEvidenceKind
	{
		Physical = 1,
		Photograph = 2,
		Document = 3,
		Digital = 4,
		Sample = 5
	}

	public enum RmsEvidenceState
	{
		Collected = 1,
		InStorage = 2,
		Transferred = 3,
		Released = 4,
		Destroyed = 5
	}

	public enum RmsReferralState
	{
		Sent = 1,
		Acknowledged = 2,
		Declined = 3,
		Closed = 4
	}

	/// <summary>
	/// An investigation case (RMS plan section 4.4, RMS-5). Restricted end to end: reading any part of it needs
	/// <c>RecordRestricted_View</c> plus membership on the case, department administrators included. Findings are
	/// kept apart from the official NERIS incident revision; a discrepancy is recorded as a recommended amendment and
	/// the incident report is changed only through its own explicit amendment path.
	/// </summary>
	public class RmsInvestigationCase : IEntity
	{
		public string RmsInvestigationCaseId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		/// <summary>INV-{year}-{sequence}.</summary>
		public string CaseNumber { get; set; }
		public string Title { get; set; }
		/// <summary><see cref="RmsInvestigationCaseState"/>.</summary>
		public int State { get; set; }
		public DateTime OpenedOn { get; set; }
		public string OpenedByUserId { get; set; }
		public string LeadInvestigatorUserId { get; set; }
		public string RmsOccupancyId { get; set; }
		public int? CallId { get; set; }
		public string IncidentSummary { get; set; }
		/// <summary><see cref="RmsFireCauseClassification"/>.</summary>
		public int CauseClassification { get; set; }
		public string CauseDetail { get; set; }
		public string OriginDescription { get; set; }
		public string Findings { get; set; }
		public string FindingsAuthorUserId { get; set; }
		public DateTime? FindingsRecordedOn { get; set; }
		public DateTime? FindingsApprovedOn { get; set; }
		public string FindingsApprovedByUserId { get; set; }
		/// <summary>The investigation found the official incident revision needs correcting; the amendment itself is explicit and separate.</summary>
		public bool RecommendsIncidentAmendment { get; set; }
		public DateTime? ClosedOn { get; set; }
		public string ClosedByUserId { get; set; }
		public string ClosureReason { get; set; }
		public bool IsProtected { get; set; }
		public int ProtectedCatalogVersion { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }
		public DateTime? DeletedOn { get; set; }

		public bool IsClosed => State == (int)RmsInvestigationCaseState.Closed;

		public object IdValue { get => RmsInvestigationCaseId; set => RmsInvestigationCaseId = (string)value; }
		public string TableName => "RmsInvestigationCases";
		public string IdName => "RmsInvestigationCaseId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "IsClosed" };
	}

	/// <summary>An incident report linked to a case, pinned to the official revision current when the link was made.</summary>
	public class RmsInvestigationCaseIncident : IEntity
	{
		public string RmsInvestigationCaseIncidentId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string RmsInvestigationCaseId { get; set; }
		/// <summary>RmsIncidentReport id (the Record).</summary>
		public string RecordId { get; set; }
		public string PinnedRevisionId { get; set; }
		public string RecordNumber { get; set; }
		public DateTime LinkedOn { get; set; }
		public string LinkedByUserId { get; set; }

		public object IdValue { get => RmsInvestigationCaseIncidentId; set => RmsInvestigationCaseIncidentId = (string)value; }
		public string TableName => "RmsInvestigationCaseIncidents";
		public string IdName => "RmsInvestigationCaseIncidentId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	public class RmsInvestigationCaseMember : IEntity
	{
		public string RmsInvestigationCaseMemberId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string RmsInvestigationCaseId { get; set; }
		public string UserId { get; set; }
		/// <summary><see cref="RmsInvestigationRole"/>.</summary>
		public int Role { get; set; }
		public DateTime AddedOn { get; set; }
		public string AddedByUserId { get; set; }
		public DateTime? RemovedOn { get; set; }
		public string RemovedByUserId { get; set; }

		public bool IsActive => RemovedOn == null;

		public object IdValue { get => RmsInvestigationCaseMemberId; set => RmsInvestigationCaseMemberId = (string)value; }
		public string TableName => "RmsInvestigationCaseMembers";
		public string IdName => "RmsInvestigationCaseMemberId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "IsActive" };
	}

	/// <summary>Scene notes, interviews, observations. Locked when the case closes; never edited after that.</summary>
	public class RmsInvestigationNote : IEntity
	{
		public string RmsInvestigationNoteId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string RmsInvestigationCaseId { get; set; }
		/// <summary><see cref="RmsInvestigationNoteKind"/>.</summary>
		public int Kind { get; set; }
		public DateTime OccurredOn { get; set; }
		public string AuthorUserId { get; set; }
		/// <summary>Interview subject or note title; personal information when it names a witness.</summary>
		public string Subject { get; set; }
		public string Body { get; set; }
		public bool IsLocked { get; set; }
		public bool IsProtected { get; set; }
		public int ProtectedCatalogVersion { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }
		public DateTime? DeletedOn { get; set; }

		public object IdValue { get => RmsInvestigationNoteId; set => RmsInvestigationNoteId = (string)value; }
		public string TableName => "RmsInvestigationNotes";
		public string IdName => "RmsInvestigationNoteId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	public class RmsInvestigationEvidence : IEntity
	{
		public string RmsInvestigationEvidenceId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string RmsInvestigationCaseId { get; set; }
		/// <summary>EV-{year}-{sequence}, unique per department.</summary>
		public string EvidenceNumber { get; set; }
		/// <summary><see cref="RmsInvestigationEvidenceKind"/>.</summary>
		public int Kind { get; set; }
		public string Description { get; set; }
		public DateTime CollectedOn { get; set; }
		public string CollectedByUserId { get; set; }
		public string CollectedFrom { get; set; }
		/// <summary><see cref="RmsEvidenceState"/>.</summary>
		public int State { get; set; }
		public string CurrentCustodianUserId { get; set; }
		public string CurrentCustodianExternal { get; set; }
		public string StorageLocation { get; set; }
		public bool IsProtected { get; set; }
		public int ProtectedCatalogVersion { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }
		public DateTime? DeletedOn { get; set; }

		public object IdValue { get => RmsInvestigationEvidenceId; set => RmsInvestigationEvidenceId = (string)value; }
		public string TableName => "RmsInvestigationEvidence";
		public string IdName => "RmsInvestigationEvidenceId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Chain of custody: append-only, one row per hand-off.</summary>
	public class RmsInvestigationCustody : IEntity
	{
		public string RmsInvestigationCustodyId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string RmsInvestigationEvidenceId { get; set; }
		public int Sequence { get; set; }
		public DateTime TransferredOn { get; set; }
		public string FromUserId { get; set; }
		public string FromExternal { get; set; }
		public string ToUserId { get; set; }
		public string ToExternal { get; set; }
		public string Reason { get; set; }
		/// <summary><see cref="RmsEvidenceState"/> after this transfer.</summary>
		public int ResultingState { get; set; }
		public string RecordedByUserId { get; set; }
		public DateTime CreatedOn { get; set; }
		public bool IsProtected { get; set; }
		public int ProtectedCatalogVersion { get; set; }

		public object IdValue { get => RmsInvestigationCustodyId; set => RmsInvestigationCustodyId = (string)value; }
		public string TableName => "RmsInvestigationCustody";
		public string IdName => "RmsInvestigationCustodyId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	public class RmsInvestigationReferral : IEntity
	{
		public string RmsInvestigationReferralId { get; set; }
		public int DepartmentId { get; set; }
		public string ProtectionId { get; set; }
		public string RmsInvestigationCaseId { get; set; }
		public string Agency { get; set; }
		public DateTime ReferredOn { get; set; }
		public string ReferredByUserId { get; set; }
		public string Reason { get; set; }
		public string ReferenceNumber { get; set; }
		/// <summary><see cref="RmsReferralState"/>.</summary>
		public int State { get; set; }
		public bool IsProtected { get; set; }
		public int ProtectedCatalogVersion { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }

		public object IdValue { get => RmsInvestigationReferralId; set => RmsInvestigationReferralId = (string)value; }
		public string TableName => "RmsInvestigationReferrals";
		public string IdName => "RmsInvestigationReferralId";
		public int IdType => 1;
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
