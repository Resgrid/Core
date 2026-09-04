using System.ComponentModel.DataAnnotations;

namespace Resgrid.Model
{
	/// <summary>
	/// Which physical RMS aggregate a Record reference points at. Per RMS plan decision 26 there is no
	/// physical RmsRecord table; RmsRecord/RmsRevision in cross-plan contracts mean the
	/// (DepartmentId, RecordId, RecordKind, RevisionId) reference shape exposed by both aggregates.
	/// </summary>
	public enum RmsRecordKind
	{
		/// <summary>RmsOperationalRecord: locked Logs-parity types and department-owned definitions.</summary>
		Operational = 1,

		/// <summary>RmsIncidentReport: the NERIS incident report aggregate (RMS-2).</summary>
		IncidentReport = 2
	}

	/// <summary>
	/// One lifecycle state machine serves every preset (RMS plan section 5.7). A preset is a subset of
	/// permitted transitions, never a different machine. Persisted as an integer; append-only.
	/// </summary>
	public enum RmsRecordState
	{
		/// <summary>The only state that autosaves; the only state with ETag draft merge.</summary>
		Draft = 1,

		/// <summary>Author submitted for review (Review Required and Approval presets only).</summary>
		ReadyForReview = 2,

		/// <summary>Reviewer returned with a reason code; re-opens as Draft on the next save.</summary>
		Returned = 3,

		/// <summary>Approval/Acknowledgement preset only; approver may not be the author.</summary>
		Approved = 4,

		/// <summary>An immutable revision plus attestation has been written.</summary>
		Finalized = 5,

		/// <summary>An amendment revision has been finalized; the prior revision is retained verbatim.</summary>
		Amended = 6,

		/// <summary>Terminal. History retained; no further transitions.</summary>
		Voided = 7,

		/// <summary>Terminal. A non-finalized Record was abandoned; reserved numbers are released per policy.</summary>
		Cancelled = 8,

		/// <summary>Reporting-destination states (RMS-2); only for definitions that declare a destination.</summary>
		Submitted = 9,
		Accepted = 10,
		Rejected = 11,
		Corrected = 12
	}

	/// <summary>Governed lifecycle presets (RMS plan section 4.1). Three are sufficient for launch.</summary>
	public enum RmsLifecyclePreset
	{
		/// <summary>Draft -> Finalized. Author holding Record_Create + Record_Finalize.</summary>
		QuickEntry = 1,

		/// <summary>Draft -> ReadyForReview -> (Returned -> Draft)* -> Finalized.</summary>
		ReviewRequired = 2,

		/// <summary>Draft -> ReadyForReview -> Approved -> Finalized, with the same return path.</summary>
		ApprovalAcknowledgement = 3
	}

	/// <summary>How many Records may exist per Call for a definition (RMS plan section 5.2.1).</summary>
	public enum RmsRecordCardinality
	{
		/// <summary>Unique on (DepartmentId, CallId, ReportingEntityId, DefinitionKey). NERIS and any locked submission definition.</summary>
		SingleAuthoritative = 1,

		/// <summary>No uniqueness rule; each Record is independent. Run, Work, Coroner, Callback, and department definitions.</summary>
		MultiplePerCall = 2,

		/// <summary>Unique on (DepartmentId, CallId, DefinitionKey, SubjectType, SubjectId). Unit Activity, responder exposure.</summary>
		OnePerSubjectPerCall = 3
	}

	/// <summary>
	/// Where a prefilled value came from (RMS plan section 4.2). Records provenance; gates nothing.
	/// Every prefilled time is freely editable in Draft; after finalization a change is an amendment.
	/// </summary>
	public enum RmsSourceKind
	{
		None = 0,
		Dispatch = 1,
		App = 2,
		Derived = 3,
		Imported = 4
	}

	/// <summary>
	/// Anchors that populate the materialized RmsRecordGroupScope set (RMS plan section 5.7.1). v1 ships
	/// one fixed inclusive set: RecordGroup, Author, Participant, Unit, Share. Call is deferred.
	/// </summary>
	public enum RmsGroupScopeAnchorType
	{
		RecordGroup = 1,
		Author = 2,
		Participant = 3,
		Unit = 4,
		Share = 5,
		Call = 6
	}

	/// <summary>Transition that created an immutable RmsRevision.</summary>
	public enum RmsRevisionTransition
	{
		Finalized = 1,
		Amended = 2,
		Voided = 3
	}

	/// <summary>State of a department's Records cutover row (RMS plan section 4.1, rollback decision frame).</summary>
	public enum RmsDepartmentCutoverState
	{
		/// <summary>Records is active; the legacy Log/UnitLog write guard is engaged.</summary>
		Active = 1,

		/// <summary>Clean revert or drain-and-revert completed; Logs writes reopened. ActivatedOn retained in history.</summary>
		Reverted = 2
	}

	/// <summary>Bounded origin marker for audit and Workflow filtering; never a device identifier.</summary>
	public enum RmsOriginClient
	{
		System = 0,
		Web = 1,
		Responder = 2,
		Unit = 3,
		IncidentCommand = 4,
		Dispatch = 5,
		Api = 6
	}

	/// <summary>Department setting 75: cross-group Record visibility mode. v1 is on/off only.</summary>
	public enum RecordsGroupVisibilityMode
	{
		DepartmentWide = 0,
		GroupScoped = 1
	}

	/// <summary>Who may act on a permission row; stored as the Permission.Action integer.</summary>
	public enum RmsNumberAssignment
	{
		/// <summary>Sequence advances only when an immutable revision is written; abandoned drafts consume nothing.</summary>
		OnFinalize = 1,

		/// <summary>Number reserved at draft creation; an abandoned reservation is recorded as a voided number, never reused.</summary>
		OnCreate = 2
	}

	/// <summary>Delivery state of a DomainEventOutbox row.</summary>
	public enum DomainEventOutboxState
	{
		Pending = 0,
		Dispatched = 1,
		Failed = 2,
		Skipped = 3
	}

	/// <summary>Locked Logs-parity record types, mirrored from LogTypes with Unit Activity added.</summary>
	public enum RmsOperationalRecordType
	{
		Run = 1,
		Training = 2,
		Work = 4,
		Meeting = 5,
		Coroner = 6,
		Callback = 7,
		UnitActivity = 8
	}
}
