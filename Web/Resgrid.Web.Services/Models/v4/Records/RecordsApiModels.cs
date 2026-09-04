using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Records
{
	/// <summary>Capability manifest for the Records module (RMS plan sections 5.4, 5.9.1).</summary>
	public class RecordsCapabilitiesResult : StandardApiResponseV4Base
	{
		public RecordsCapabilitiesData Data { get; set; }
	}

	public class RecordsCapabilitiesData
	{
		/// <summary>The contract version this server speaks; a client below a definition's MinimumClientCapability fails closed for authoring.</summary>
		public string ContractVersion { get; set; }
		public bool ModuleEnabled { get; set; }
		public bool RecordsUsable { get; set; }
		public bool Activated { get; set; }
		public DateTime? ActivatedOn { get; set; }
		public string CutoverState { get; set; }
		public string GroupVisibilityMode { get; set; }
		public RecordsPermissionsData Permissions { get; set; } = new RecordsPermissionsData();
		public RecordsFieldClientsData FieldClients { get; set; } = new RecordsFieldClientsData();
		public List<RecordDefinitionData> Definitions { get; set; } = new List<RecordDefinitionData>();
		public RecordsSearchCapabilityData Search { get; set; } = new RecordsSearchCapabilityData();
		/// <summary>Protected Data block (plan 5.9.1): same shape before and after enrollment.</summary>
		public RecordsProtectionData Protection { get; set; } = new RecordsProtectionData();
		public int UploadChunkSize { get; set; }
		public long MaxAttachmentBytes { get; set; }
		public long ServerTimestampMs { get; set; }
	}

	public class RecordsPermissionsData
	{
		public bool CanView { get; set; }
		public bool CanCreate { get; set; }
		public bool CanReview { get; set; }
		public bool CanApprove { get; set; }
		public bool CanFinalize { get; set; }
		public bool CanSubmit { get; set; }
		public bool CanAmend { get; set; }
		public bool CanVoid { get; set; }
		public bool CanExport { get; set; }
		public bool CanShare { get; set; }
		public bool CanReassign { get; set; }
		public bool CanViewRestricted { get; set; }
		public bool CanViewLegacy { get; set; }
		public bool IsDepartmentAdmin { get; set; }
	}

	/// <summary>Per-app Field Records flags; a field client whose flag is off is refused on create/edit.</summary>
	public class RecordsFieldClientsData
	{
		public bool Responder { get; set; }
		public bool Unit { get; set; }
		public bool IncidentCommand { get; set; }
		public bool Dispatch { get; set; }
	}

	public class RecordDefinitionData
	{
		public string Key { get; set; }
		public int Version { get; set; }
		public string Name { get; set; }
		public int? RecordType { get; set; }
		public string RecordKind { get; set; }
		public int LifecyclePreset { get; set; }
		public string LifecyclePresetName { get; set; }
		public string Cardinality { get; set; }
		public bool Restricted { get; set; }
		public string NumberPrefix { get; set; }
		public bool RequiresCall { get; set; }
		public bool SupportsParticipants { get; set; }
		public bool SupportsUnits { get; set; }
		public bool SupportsAttachments { get; set; }
		public string MinimumClientCapability { get; set; }
		public bool Locked { get; set; }
		public List<RecordFieldData> Fields { get; set; } = new List<RecordFieldData>();
	}

	public class RecordFieldData
	{
		public string Key { get; set; }
		public string Section { get; set; }
		public string Type { get; set; }
		public bool Required { get; set; }
		public bool RequiredToFinalize { get; set; }
		public bool Restricted { get; set; }
	}

	public class RecordsSearchCapabilityData
	{
		public bool Available { get; set; }
		public bool NarrativeAvailable { get; set; }
	}

	public class RecordsProtectionData
	{
		/// <summary>NotInstalled until the department is enrolled; then the DepartmentDataProtectionState name.</summary>
		public string State { get; set; } = "NotInstalled";
		public int CatalogVersion { get; set; }
		public string GrantExpiresOn { get; set; }
		public int? StepUpWindowMinutes { get; set; }
		public string MinimumClientVersion { get; set; }
	}

	/// <summary>Paged list of records the caller may see.</summary>
	public class RecordsResult : StandardApiResponseV4Base
	{
		public List<RecordSummaryData> Data { get; set; } = new List<RecordSummaryData>();
		public int Total { get; set; }
		/// <summary>Search only: the text was not applied because the search host is off or unavailable.</summary>
		public bool SearchDegraded { get; set; }
		public bool Truncated { get; set; }
	}

	/// <summary>Safe projection columns only (plan 5.10): never narrative, restricted detail or attachment bytes.</summary>
	public class RecordSummaryData
	{
		public string RecordId { get; set; }
		public string RecordKind { get; set; }
		public string RecordNumber { get; set; }
		public string DraftReference { get; set; }
		public string DefinitionKey { get; set; }
		public int DefinitionVersion { get; set; }
		public int? RecordType { get; set; }
		public int State { get; set; }
		public string StateName { get; set; }
		public DateTime? OccurredOn { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime? FinalizedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public int? StationGroupId { get; set; }
		public int? CallId { get; set; }
		public string CallNumber { get; set; }
		public string AuthorUserId { get; set; }
		public string OwnerUserId { get; set; }
		public string ReviewerUserId { get; set; }
		public string DisplaySummary { get; set; }
		public bool IsLegacy { get; set; }
		public long RowVersion { get; set; }
		/// <summary>Delta feed: the record is gone for the client (deleted, cancelled or voided); remove it locally.</summary>
		public bool IsTombstone { get; set; }
		public DateTime? DeletedOn { get; set; }
	}

	/// <summary>Delta cursor (plan 5.3): persist ServerTimestampMs and pass it back as <c>since</c>.</summary>
	public class RecordsChangesResult : StandardApiResponseV4Base
	{
		public RecordsChangesData Data { get; set; } = new RecordsChangesData();
	}

	public class RecordsChangesData
	{
		public long Since { get; set; }
		public long ServerTimestampMs { get; set; }
		public bool HasMore { get; set; }
		public List<RecordSummaryData> Records { get; set; } = new List<RecordSummaryData>();
	}

	public class RecordResult : StandardApiResponseV4Base
	{
		public RecordData Data { get; set; }
	}

	/// <summary>A hydrated Record: header, working details, participants, units, attachment metadata and revision summaries. Restricted fields are withheld without RecordRestricted_View.</summary>
	public class RecordData
	{
		public string RecordId { get; set; }
		public string RecordKind { get; set; }
		public string DefinitionKey { get; set; }
		public int DefinitionVersion { get; set; }
		public int? RecordType { get; set; }
		public string RecordTypeName { get; set; }
		public int LifecyclePreset { get; set; }
		public string LifecyclePresetName { get; set; }
		public int State { get; set; }
		public string StateName { get; set; }
		public string RecordNumber { get; set; }
		public string DraftReference { get; set; }
		public string DisplaySummary { get; set; }
		public int? StationGroupId { get; set; }
		public int? CallId { get; set; }
		public string ExternalId { get; set; }
		public string AuthorUserId { get; set; }
		public string OwnerUserId { get; set; }
		public string ReviewerUserId { get; set; }
		public string ApproverUserId { get; set; }
		public DateTime? StartedOn { get; set; }
		public DateTime? EndedOn { get; set; }
		public DateTime? ReviewDueOn { get; set; }
		public DateTime? SubmittedForReviewOn { get; set; }
		public DateTime? ReturnedOn { get; set; }
		public string ReturnReasonCode { get; set; }
		public string ReturnReasonText { get; set; }
		public int ReturnCount { get; set; }
		public DateTime? ApprovedOn { get; set; }
		public DateTime? FinalizedOn { get; set; }
		public string FinalizedByUserId { get; set; }
		public string CurrentRevisionId { get; set; }
		public int RevisionCount { get; set; }
		public string AmendsRevisionId { get; set; }
		public DateTime? VoidedOn { get; set; }
		public string VoidReasonCode { get; set; }
		public string VoidReasonText { get; set; }
		public DateTime? CancelledOn { get; set; }
		public int OriginClient { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public long RowVersion { get; set; }
		/// <summary>Weak ETag of the header row (<c>W/"{RowVersion}"</c>); send it back as If-Match or RowVersion on saves and commands.</summary>
		public string ETag { get; set; }
		public bool IsEditable { get; set; }
		public bool IsRestricted { get; set; }
		public List<string> WithheldFields { get; set; } = new List<string>();
		public List<string> AvailableTransitions { get; set; } = new List<string>();
		public RecordDetailsData Details { get; set; } = new RecordDetailsData();
		public List<RecordParticipantData> Participants { get; set; } = new List<RecordParticipantData>();
		public List<RecordUnitResponseData> Units { get; set; } = new List<RecordUnitResponseData>();
		public List<RecordAttachmentData> Attachments { get; set; } = new List<RecordAttachmentData>();
		public List<RecordRevisionData> Revisions { get; set; } = new List<RecordRevisionData>();
		public List<int> GroupScopeIds { get; set; } = new List<int>();
	}

	public class RecordDetailsData
	{
		public string Narrative { get; set; }
		public string InitialReport { get; set; }
		public string Type { get; set; }
		public string Course { get; set; }
		public string CourseCode { get; set; }
		public string Instructors { get; set; }
		public string Cause { get; set; }
		public string InvestigatedByUserId { get; set; }
		public string ContactName { get; set; }
		public string ContactNumber { get; set; }
		public string OtherPersonnel { get; set; }
		public string Location { get; set; }
		public string OtherAgencies { get; set; }
		public string OtherUnits { get; set; }
		public string BodyLocation { get; set; }
		public string PronouncedDeceasedBy { get; set; }
		public string CaseNumber { get; set; }
		public string Destination { get; set; }
		public string Facilitator { get; set; }
		public int? UnitId { get; set; }
		public DateTime? ActivityOn { get; set; }
		public string CallNumber { get; set; }
		public string CallName { get; set; }
		public string CallType { get; set; }
		public int? CallPriority { get; set; }
		public DateTime? CallLoggedOn { get; set; }
		public string CallAddress { get; set; }
		public string CallNature { get; set; }
	}

	public class RecordParticipantData
	{
		public string UserId { get; set; }
		public string DisplayName { get; set; }
		public int? GroupId { get; set; }
		public string GroupName { get; set; }
		public int? UnitId { get; set; }
		public string Role { get; set; }
	}

	public class RecordUnitResponseData
	{
		public int UnitId { get; set; }
		public string UnitName { get; set; }
		public string UnitType { get; set; }
		public int? StationGroupId { get; set; }
		public DateTime? Dispatched { get; set; }
		public DateTime? Enroute { get; set; }
		public DateTime? OnScene { get; set; }
		public DateTime? Released { get; set; }
		public DateTime? InQuarters { get; set; }
	}

	public class RecordAttachmentData
	{
		public string AttachmentId { get; set; }
		public string RecordId { get; set; }
		public string FileName { get; set; }
		public string ContentType { get; set; }
		public long ByteSize { get; set; }
		public string Checksum { get; set; }
		public string Description { get; set; }
		public string UploadedByUserId { get; set; }
		public DateTime UploadedOn { get; set; }
		public int ScanState { get; set; }
		public string ScanStateName { get; set; }
	}

	public class RecordRevisionData
	{
		public string RevisionId { get; set; }
		public int RevisionNumber { get; set; }
		public int Transition { get; set; }
		public string TransitionName { get; set; }
		public string PriorRevisionId { get; set; }
		public string Checksum { get; set; }
		public string ActorUserId { get; set; }
		public string ReasonCode { get; set; }
		public string ReasonText { get; set; }
		public string AttestationStatementVersion { get; set; }
		public DateTime? AttestedOn { get; set; }
		public DateTime CreatedOn { get; set; }
	}

	/// <summary>Create or save a draft. Dates are UTC. Every list replaces the draft rows wholesale.</summary>
	public class SaveRecordDraftInput
	{
		/// <summary>Null on create.</summary>
		public string RecordId { get; set; }
		/// <summary>Required on save (or send If-Match); the row version the client last saw.</summary>
		public long? RowVersion { get; set; }
		/// <summary>Scoped idempotency key for offline-created drafts: the same key returns the same record.</summary>
		public string IdempotencyKey { get; set; }
		/// <summary>Client-generated GUID for an offline-created draft; server-assigned when null.</summary>
		public string ClientRecordId { get; set; }
		public string DefinitionKey { get; set; }
		public int? CallId { get; set; }
		public int? StationGroupId { get; set; }
		public string ExternalId { get; set; }
		public DateTime? StartedOn { get; set; }
		public DateTime? EndedOn { get; set; }
		public RecordDetailsInput Details { get; set; } = new RecordDetailsInput();
		public List<RecordParticipantInputData> Participants { get; set; } = new List<RecordParticipantInputData>();
		public List<RecordUnitResponseInputData> Units { get; set; } = new List<RecordUnitResponseInputData>();
		public string DuplicateContinueReason { get; set; }
		/// <summary>RmsOriginClient: 2 Responder, 3 Unit, 4 IncidentCommand, 5 Dispatch, 6 Api (default). Field clients are gated by their Records.Field.* flag.</summary>
		public int? OriginClient { get; set; }
	}

	public class RecordDetailsInput
	{
		public string Narrative { get; set; }
		public string InitialReport { get; set; }
		public string Type { get; set; }
		public string Course { get; set; }
		public string CourseCode { get; set; }
		public string Instructors { get; set; }
		public string Cause { get; set; }
		public string InvestigatedByUserId { get; set; }
		public string ContactName { get; set; }
		public string ContactNumber { get; set; }
		public string OtherPersonnel { get; set; }
		public string Location { get; set; }
		public string OtherAgencies { get; set; }
		public string OtherUnits { get; set; }
		public string BodyLocation { get; set; }
		public string PronouncedDeceasedBy { get; set; }
		public string CaseNumber { get; set; }
		public string Destination { get; set; }
		public string Facilitator { get; set; }
		public int? UnitId { get; set; }
		public DateTime? ActivityOn { get; set; }
	}

	public class RecordParticipantInputData
	{
		public string UserId { get; set; }
		public int? UnitId { get; set; }
		public string Role { get; set; }
	}

	public class RecordUnitResponseInputData
	{
		public int UnitId { get; set; }
		public DateTime? Dispatched { get; set; }
		public DateTime? Enroute { get; set; }
		public DateTime? OnScene { get; set; }
		public DateTime? Released { get; set; }
		public DateTime? InQuarters { get; set; }
	}

	/// <summary>A lifecycle command. RowVersion (or If-Match) guards ETag-checked transitions; IdempotencyKey replays the first outcome.</summary>
	public class RecordCommandInput
	{
		public string RecordId { get; set; }
		public long? RowVersion { get; set; }
		public string IdempotencyKey { get; set; }
		public string ReasonCode { get; set; }
		public string ReasonText { get; set; }
		/// <summary>Finalize: the author confirmed the attestation statement.</summary>
		public bool Attested { get; set; }
		public string AttestationStatementVersion { get; set; }
		/// <summary>Reassign: the new draft owner.</summary>
		public string NewOwnerUserId { get; set; }
		public int? OriginClient { get; set; }
	}

	/// <summary>409: the client's row version is stale. Reconcile against Current and the changed field paths; never last-writer-wins.</summary>
	public class RecordConflictResult : StandardApiResponseV4Base
	{
		public RecordConflictData Data { get; set; }
	}

	public class RecordConflictData
	{
		public string RecordId { get; set; }
		public long ExpectedRowVersion { get; set; }
		public long CurrentRowVersion { get; set; }
		public int CurrentState { get; set; }
		public string CurrentStateName { get; set; }
		public string CurrentRevisionId { get; set; }
		public List<string> ChangedFieldPaths { get; set; } = new List<string>();
		public RecordData Current { get; set; }
	}

	public class RecordRevisionsResult : StandardApiResponseV4Base
	{
		public List<RecordRevisionData> Data { get; set; } = new List<RecordRevisionData>();
	}

	/// <summary>One revision rendered from its snapshot; restricted fields withheld without RecordRestricted_View.</summary>
	public class RecordRevisionSnapshotResult : StandardApiResponseV4Base
	{
		public RecordRevisionSnapshotData Data { get; set; }
	}

	public class RecordRevisionSnapshotData
	{
		public RecordRevisionData Revision { get; set; }
		public RecordData Snapshot { get; set; }
	}

	public class RecordDiffResult : StandardApiResponseV4Base
	{
		public RecordDiffData Data { get; set; }
	}

	public class RecordDiffData
	{
		public string RecordId { get; set; }
		public string FromRevisionId { get; set; }
		public string ToRevisionId { get; set; }
		public List<RecordFieldDiffData> Diffs { get; set; } = new List<RecordFieldDiffData>();
	}

	public class RecordFieldDiffData
	{
		public string Section { get; set; }
		public string FieldKey { get; set; }
		public string OldValue { get; set; }
		public string NewValue { get; set; }
		public bool Withheld { get; set; }
	}

	public class RecordAttachmentsResult : StandardApiResponseV4Base
	{
		public List<RecordAttachmentData> Data { get; set; } = new List<RecordAttachmentData>();
	}

	public class RecordAttachmentContentResult : StandardApiResponseV4Base
	{
		public RecordAttachmentContentData Data { get; set; }
	}

	public class RecordAttachmentContentData : RecordAttachmentData
	{
		/// <summary>Base64 content.</summary>
		public string Data { get; set; }
	}

	/// <summary>Open a resumable upload: declare size and SHA-256 up front (plan 5.3).</summary>
	public class BeginRecordUploadInput
	{
		public string RecordId { get; set; }
		public string FileName { get; set; }
		public string ContentType { get; set; }
		public long ByteSize { get; set; }
		/// <summary>Lower-case hex SHA-256 of the whole file.</summary>
		public string Sha256 { get; set; }
	}

	public class RecordUploadResult : StandardApiResponseV4Base
	{
		public RecordUploadData Data { get; set; }
	}

	public class RecordUploadData
	{
		public string UploadId { get; set; }
		public string RecordId { get; set; }
		public string FileName { get; set; }
		public string ContentType { get; set; }
		public long DeclaredSize { get; set; }
		/// <summary>Resume from here: the next chunk's Offset.</summary>
		public long ReceivedBytes { get; set; }
		public int ChunkSize { get; set; }
		public int ChunkCount { get; set; }
		public int State { get; set; }
		public string StateName { get; set; }
		public DateTime ExpiresOn { get; set; }
		public string AttachmentId { get; set; }
	}

	public class RecordUploadChunkInput
	{
		public string UploadId { get; set; }
		/// <summary>Byte offset of this chunk; must equal the session's ReceivedBytes.</summary>
		public long Offset { get; set; }
		/// <summary>Base64 chunk, at most ChunkSize bytes.</summary>
		public string Data { get; set; }
	}

	public class CompleteRecordUploadInput
	{
		public string UploadId { get; set; }
		public string Description { get; set; }
	}

	public class RecordUploadIdInput
	{
		public string UploadId { get; set; }
	}

	public class RecordAttachmentIdInput
	{
		public string RecordId { get; set; }
		public string AttachmentId { get; set; }
	}

	public class RecordAttachmentResult : StandardApiResponseV4Base
	{
		public RecordAttachmentData Data { get; set; }
	}
}
