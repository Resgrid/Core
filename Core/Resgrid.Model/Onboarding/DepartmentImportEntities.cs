using System;
using System.Collections.Generic;

namespace Resgrid.Model.Onboarding
{
	/// <summary>
	/// Base row for every Department Import table (wizard plan section 8.6): an application-generated
	/// string GUID key and the owning department on every row. No foreign key to Departments; the tables
	/// are covered by UnlinkedDepartmentCleanup and DeletedDepartmentCleanup instead.
	/// </summary>
	public abstract class ImportRow : IEntity
	{
		[System.ComponentModel.DataAnnotations.Schema.NotMapped, Newtonsoft.Json.JsonIgnore]
		public object IdValue { get => Id; set => Id = (string)value; }
		[System.ComponentModel.DataAnnotations.Schema.NotMapped, Newtonsoft.Json.JsonIgnore]
		public string TableName => DepartmentImportTables.All[GetType()];
		[System.ComponentModel.DataAnnotations.Schema.NotMapped, Newtonsoft.Json.JsonIgnore]
		public string IdName => "Id";
		[System.ComponentModel.DataAnnotations.Schema.NotMapped, Newtonsoft.Json.JsonIgnore]
		public int IdType => 1;
		[System.ComponentModel.DataAnnotations.Schema.NotMapped, Newtonsoft.Json.JsonIgnore]
		public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "TableName", "IdName", "IdType", "IgnoredProperties" };

		public string Id { get; set; } = Guid.NewGuid().ToString();
		public int DepartmentId { get; set; }
	}

	/// <summary>Root of an import: one department's migration effort (plan section 8.6).</summary>
	public sealed class ImportWorkspace : ImportRow
	{
		public string Name { get; set; }
		public string InternalReference { get; set; }
		/// <summary><see cref="ImportWorkspaceMode"/>.</summary>
		public int Mode { get; set; }
		/// <summary><see cref="ImportWorkspaceStatus"/>.</summary>
		public int Status { get; set; }
		/// <summary><see cref="ImportOperatingModel"/>.</summary>
		public int OperatingModel { get; set; }
		public string OwnerUserId { get; set; }
		public DateTime? TargetActivationDate { get; set; }
		/// <summary>The DepartmentOperatingProfile revision the workspace was described against.</summary>
		public int? OperatingProfileRevision { get; set; }
		/// <summary>Import catalog version pinned at creation (contracts, packs, presets; section 7.4).</summary>
		public string CatalogVersion { get; set; }
		/// <summary><see cref="ImportWriteMode"/> default for runs.</summary>
		public int DefaultWriteMode { get; set; }
		public bool RequireSecondApprover { get; set; }
		/// <summary>JSON <see cref="ImportDomainPlan"/>: chosen domains, products and ownership (step 3).</summary>
		public string DomainPlan { get; set; }
		/// <summary>Denormalized current support access level (<see cref="ImportSupportAccessLevel"/>), for the status strip and queue.</summary>
		public int SupportAccessLevel { get; set; }
		public DateTime? SupportAccessExpiresOn { get; set; }
		/// <summary>Staff flagged the workspace "Needs engineering" (section 17.4).</summary>
		public bool NeedsEngineering { get; set; }
		public string EngineeringNote { get; set; }
		/// <summary>Optimistic concurrency revision; every workspace mutation increments it.</summary>
		public int Revision { get; set; } = 1;
		public string CreatedBy { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime UpdatedOn { get; set; }
		public DateTime LastActivityOn { get; set; }
		public DateTime? ActivatedOn { get; set; }
		public DateTime? ClosedOn { get; set; }
		public DateTime? CancelledOn { get; set; }
		public DateTime? AbandonReminderSentOn { get; set; }
		/// <summary>Retention deadline for staging (activation + 30 days new; 90 days live).</summary>
		public DateTime? StagingRetentionUntil { get; set; }
		/// <summary>When every administrator was last sent the daily digest of staff value views (section 17.2); null = never.</summary>
		public DateTime? StaffViewDigestSentOn { get; set; }
	}

	/// <summary>A declared source system for a workspace (plan section 8.6, 8.8).</summary>
	public sealed class ImportSource : ImportRow
	{
		public string WorkspaceId { get; set; }
		/// <summary>Product key from the pack catalog ("aladtec", "active911", "spreadsheet", "resgrid").</summary>
		public string ProductKey { get; set; }
		public string ProductVersion { get; set; }
		/// <summary>Display label the administrator gave this source ("Aladtec export March").</summary>
		public string Label { get; set; }
		/// <summary>Stable source-system instance key used in the external-id map (section 11.3).</summary>
		public string SourceSystemKey { get; set; }
		/// <summary>Comma-separated contract keys this source owns.</summary>
		public string OwnedDomains { get; set; }
		public string AdapterKey { get; set; }
		public string AdapterVersion { get; set; }
		public string PackVersion { get; set; }
		public DateTime? ExtractionWindowStart { get; set; }
		public DateTime? ExtractionWindowEnd { get; set; }
		/// <summary>
		/// Write-only source API credentials encrypted for the department. Never projected, logged or
		/// shown again; deleted when extraction ends or 24 hours after entry (section 8.8).
		/// </summary>
		public string EncryptedCredentials { get; set; }
		public DateTime? CredentialsExpireOn { get; set; }
		public string Region { get; set; }
		public string ConnectorReference { get; set; }
		public string CreatedBy { get; set; }
		public DateTime CreatedOn { get; set; }
	}

	/// <summary>
	/// Uploaded or extracted file metadata. The bytes live only in the RustFS artifact store, client-side
	/// encrypted with a per-artifact data key (plan section 8.7, conformance I14).
	/// </summary>
	public sealed class ImportArtifact : ImportRow
	{
		public string WorkspaceId { get; set; }
		public string SourceId { get; set; }
		/// <summary><see cref="ImportArtifactKind"/>.</summary>
		public int Kind { get; set; }
		/// <summary>Sanitized original name (display only; never part of a storage path).</summary>
		public string OriginalName { get; set; }
		public string DeclaredContentType { get; set; }
		public long ExpectedSizeBytes { get; set; }
		public long SizeBytes { get; set; }
		/// <summary>Whole-file SHA-256 the client declared before upload (hex).</summary>
		public string Sha256Declared { get; set; }
		/// <summary>Whole-file SHA-256 recomputed over the reassembled plaintext (hex).</summary>
		public string Sha256Verified { get; set; }
		/// <summary><see cref="ImportFileFormat"/>.</summary>
		public int DetectedFormat { get; set; }
		public string DetectedEncoding { get; set; }
		public string DetectedDelimiter { get; set; }
		/// <summary><see cref="ImportArtifactScanState"/>.</summary>
		public int ScanState { get; set; }
		public string ScanEngine { get; set; }
		public int ScanAttempts { get; set; }
		public DateTime? ScannedOn { get; set; }
		public string ObjectKey { get; set; }
		public string MultipartUploadId { get; set; }
		/// <summary>JSON list of <see cref="ImportArtifactPart"/>; an interrupted upload resumes after the last one.</summary>
		public string UploadedParts { get; set; }
		/// <summary>The artifact's data key, wrapped by EncryptionService.EncryptForDepartment.</summary>
		public string WrappedDataKey { get; set; }
		/// <summary><see cref="ImportFieldSensitivity"/> of the most sensitive content detected.</summary>
		public int Classification { get; set; }
		/// <summary><see cref="ImportArtifactUploadState"/>.</summary>
		public int UploadState { get; set; }
		/// <summary>Stable issue code when the artifact was rejected at L0 (no values).</summary>
		public string RejectionCode { get; set; }
		public bool StaffAuthored { get; set; }
		public string CreatedBy { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime UpdatedOn { get; set; }
		public DateTime? CompletedOn { get; set; }
		public DateTime? RetentionDeadline { get; set; }
		public DateTime? PurgedOn { get; set; }
	}

	/// <summary>One file or worksheet bound to one entity contract (plan section 8.6).</summary>
	public sealed class ImportDataset : ImportRow
	{
		public string WorkspaceId { get; set; }
		public string ArtifactId { get; set; }
		public string SourceId { get; set; }
		/// <summary>Worksheet name for XLSX; null for single-table files.</summary>
		public string SheetName { get; set; }
		public int SheetIndex { get; set; }
		public string Name { get; set; }
		/// <summary>The entity contract key ("personnel", "units", ...); null until the destination is confirmed.</summary>
		public string EntityKey { get; set; }
		public string ContractVersion { get; set; }
		/// <summary>The detected or chosen pack/template key ("resgrid-template", "first-due.personnel").</summary>
		public string DetectedProfileKey { get; set; }
		/// <summary>Detection confidence 0–100 (L2).</summary>
		public int DetectionConfidence { get; set; }
		public bool DestinationConfirmed { get; set; }
		public string ColumnMapId { get; set; }
		/// <summary><see cref="ImportDatasetStatus"/>.</summary>
		public int Status { get; set; }
		/// <summary>JSON column profile: header, inferred type, fill rate, distinct count, sampled examples.</summary>
		public string Profile { get; set; }
		public int ColumnCount { get; set; }
		public int RowCount { get; set; }
		public int ReadyCount { get; set; }
		public int WarningCount { get; set; }
		public int BlockedCount { get; set; }
		public int ExcludedCount { get; set; }
		public int CreatedCount { get; set; }
		public int UpdatedCount { get; set; }
		public int LinkedCount { get; set; }
		public int UnchangedCount { get; set; }
		public int SkippedCount { get; set; }
		public int FailedCount { get; set; }
		/// <summary>Dependency wave from the contract graph (section 10.3).</summary>
		public int Wave { get; set; }
		/// <summary>Content digest of the staged state; any material change invalidates an approval.</summary>
		public string StagingDigest { get; set; }
		public string ApprovedRunId { get; set; }
		public string SupersededById { get; set; }
		public string FailureCode { get; set; }
		public bool StaffAuthored { get; set; }
		public int Revision { get; set; } = 1;
		public DateTime CreatedOn { get; set; }
		public DateTime UpdatedOn { get; set; }
		public DateTime? AnalyzedOn { get; set; }
	}

	/// <summary>A named, versioned column mapping (plan section 8.4).</summary>
	public sealed class ImportColumnMap : ImportRow
	{
		/// <summary>Null for a saved department map not tied to one workspace.</summary>
		public string WorkspaceId { get; set; }
		public string Name { get; set; }
		public string EntityKey { get; set; }
		public string ContractVersion { get; set; }
		public int Version { get; set; } = 1;
		/// <summary>JSON <see cref="ImportColumnMapBody"/>.</summary>
		public string Body { get; set; }
		/// <summary><see cref="ImportColumnMapOrigin"/>.</summary>
		public int Origin { get; set; }
		/// <summary>Saved for reuse in later workspaces ("Save mapping as").</summary>
		public bool IsSaved { get; set; }
		/// <summary>Header-set signature used to offer this map for a matching file.</summary>
		public string HeaderSignature { get; set; }
		public bool StaffAuthored { get; set; }
		public string CreatedBy { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime UpdatedOn { get; set; }
	}

	/// <summary>One value crosswalk decision for one distinct source value of one dataset field (section 6.3 step 7).</summary>
	public sealed class ImportValueMap : ImportRow
	{
		public string WorkspaceId { get; set; }
		public string DatasetId { get; set; }
		public string FieldKey { get; set; }
		/// <summary>SHA-256 of the normalized source value (lookup key; the value itself may be personal).</summary>
		public string SourceValueHash { get; set; }
		/// <summary>The distinct source value (codes such as "ENG"); purged with staging.</summary>
		public string SourceValue { get; set; }
		/// <summary><see cref="ImportValueMapAction"/>.</summary>
		public int Action { get; set; }
		/// <summary>Choice code or the target record key (string form) chosen.</summary>
		public string TargetCode { get; set; }
		public int Confidence { get; set; }
		public int RowCount { get; set; }
		/// <summary><see cref="ImportActorKind"/>.</summary>
		public int DecidedByKind { get; set; }
		public string DecidedBy { get; set; }
		public DateTime? DecidedOn { get; set; }
	}

	/// <summary>
	/// One staged source row. <see cref="SourcePayload"/> keeps every source cell (unmapped columns are
	/// retained until the purge); <see cref="CanonicalPayload"/> is the typed record after L3. Both are
	/// encrypted with the workspace data key when the row carries Personal or Restricted fields (section 14.4).
	/// </summary>
	public sealed class ImportStagedRow : ImportRow
	{
		public string WorkspaceId { get; set; }
		public string DatasetId { get; set; }
		public int RowNumber { get; set; }
		/// <summary>The row's source identity (external id or documented deterministic key).</summary>
		public string SourceKey { get; set; }
		public string SourcePayload { get; set; }
		public string CanonicalPayload { get; set; }
		public bool IsEncrypted { get; set; }
		/// <summary>SHA-256 of the canonical payload, for unchanged detection and the approval digest.</summary>
		public string ContentHash { get; set; }
		/// <summary><see cref="ImportRowValidationState"/>.</summary>
		public int ValidationState { get; set; }
		/// <summary><see cref="ImportRowDecision"/>.</summary>
		public int Decision { get; set; }
		/// <summary><see cref="ImportRowOutcome"/>.</summary>
		public int Outcome { get; set; }
		/// <summary>Target record key once linked or written (string form).</summary>
		public string TargetKey { get; set; }
		/// <summary>JSON array of "entity:sourceKey" references this row depends on.</summary>
		public string DependencyKeys { get; set; }
		/// <summary>Target ModifiedOn/revision captured at preview for reviewed-update concurrency (section 11.7).</summary>
		public string TargetVersionAtPreview { get; set; }
		public bool IsExample { get; set; }
		public bool Excluded { get; set; }
		public bool EditedByStaff { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime UpdatedOn { get; set; }
	}

	/// <summary>One ladder finding (plan section 9). Parameters carry column names and codes, never values.</summary>
	public sealed class ImportIssue : ImportRow
	{
		public string WorkspaceId { get; set; }
		public string DatasetId { get; set; }
		/// <summary>Set for file-level (L0) issues raised before a dataset exists.</summary>
		public string ArtifactId { get; set; }
		public string StagedRowId { get; set; }
		public int? RowNumber { get; set; }
		public string ColumnName { get; set; }
		public string FieldKey { get; set; }
		public string EntityKey { get; set; }
		/// <summary>Stable issue code ("IMP-PHONE-UNPARSEABLE").</summary>
		public string Code { get; set; }
		/// <summary><see cref="ImportIssueSeverity"/>.</summary>
		public int Severity { get; set; }
		/// <summary><see cref="ImportLadderLevel"/>.</summary>
		public int Level { get; set; }
		/// <summary>JSON object of value-free parameters (column, expected format, counts).</summary>
		public string Parameters { get; set; }
		/// <summary>Idempotency key: dataset + row + column + code (section 13.3).</summary>
		public string IdentityKey { get; set; }
		/// <summary><see cref="ImportIssueStatus"/>.</summary>
		public int Status { get; set; }
		/// <summary><see cref="ImportIssueResolutionKind"/>.</summary>
		public int ResolutionKind { get; set; }
		/// <summary><see cref="ImportActorKind"/>.</summary>
		public int ResolvedByKind { get; set; }
		public string ResolvedBy { get; set; }
		public DateTime? ResolvedOn { get; set; }
		/// <summary>Issue routed to the department because fixing it needs a protected value (section 14.4).</summary>
		public bool NeedsDepartment { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime UpdatedOn { get; set; }
	}

	/// <summary>
	/// The external-id map: (DepartmentId, SourceSystemKey, EntityType, SourceId) is unique. Every created
	/// target is claimed here before it is written (claim-before-create, section 11.3).
	/// </summary>
	public sealed class ImportExternalId : ImportRow
	{
		public string WorkspaceId { get; set; }
		public string SourceSystemKey { get; set; }
		public string EntityType { get; set; }
		public string SourceId { get; set; }
		/// <summary>Target key: the pre-generated string key, or the integer identity as text once written.</summary>
		public string TargetKey { get; set; }
		/// <summary><see cref="ImportExternalIdState"/>.</summary>
		public int State { get; set; }
		public string RunId { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime UpdatedOn { get; set; }
	}

	/// <summary>A unit of leasing, retry, cancellation and rollback (plan section 10.2).</summary>
	public sealed class ImportRun : ImportRow
	{
		public string WorkspaceId { get; set; }
		/// <summary><see cref="ImportRunKind"/>.</summary>
		public int Kind { get; set; }
		/// <summary><see cref="ImportRunStatus"/>.</summary>
		public int Status { get; set; }
		/// <summary>The artifact an Analysis or Extract run works on.</summary>
		public string ArtifactId { get; set; }
		/// <summary>JSON array of dataset ids this run covers (one wave).</summary>
		public string DatasetIds { get; set; }
		/// <summary>The commit run a rollback or reconcile run works on.</summary>
		public string ParentRunId { get; set; }
		/// <summary><see cref="ImportWriteMode"/>.</summary>
		public int WriteMode { get; set; }
		public string LeaseOwner { get; set; }
		public DateTime? LeaseExpiresOn { get; set; }
		public DateTime? HeartbeatOn { get; set; }
		public int Attempts { get; set; }
		public DateTime? NextAttemptOn { get; set; }
		/// <summary>JSON <see cref="ImportRunCursor"/>.</summary>
		public string Cursor { get; set; }
		/// <summary>When the cursor last moved (stall detection, section 13.1).</summary>
		public DateTime? CursorMovedOn { get; set; }
		/// <summary>SHA-256 approval digest frozen at approval (section 10.2).</summary>
		public string ApprovalDigest { get; set; }
		public string RequestedBy { get; set; }
		public DateTime RequestedOn { get; set; }
		public string ApprovedBy { get; set; }
		public DateTime? ApprovedOn { get; set; }
		public string SecondApprovedBy { get; set; }
		public DateTime? SecondApprovedOn { get; set; }
		/// <summary>JSON <see cref="ImportAuthority"/> (who the run acts as, and staff identities when managed).</summary>
		public string Authority { get; set; }
		/// <summary>JSON <see cref="ImportRunVersions"/> pinned on the run (engine, contracts, packs, preset).</summary>
		public string Versions { get; set; }
		/// <summary>JSON <see cref="ImportRunCounts"/>.</summary>
		public string Counts { get; set; }
		/// <summary>JSON <see cref="ImportConsequenceDecisions"/> stated at approval (L8).</summary>
		public string ConsequenceDecisions { get; set; }
		/// <summary>JSON <c>ImportManagedActivationRequest</c> of a staff-requested activation or invitation run (section 17.8); null otherwise.</summary>
		public string Request { get; set; }
		public string CorrelationId { get; set; }
		public string LastErrorCode { get; set; }
		/// <summary><see cref="ImportRunCancelRequest"/>.</summary>
		public int CancelRequested { get; set; }
		public int? DepartmentLockId { get; set; }
		public DateTime? StartedOn { get; set; }
		public DateTime? CompletedOn { get; set; }
		public DateTime UpdatedOn { get; set; }
	}

	/// <summary>Per-row journal of what a commit run did, so rollback can undo it (section 11.8).</summary>
	public sealed class ImportChangeJournalEntry : ImportRow
	{
		public string WorkspaceId { get; set; }
		public string RunId { get; set; }
		public long Sequence { get; set; }
		public string EntityType { get; set; }
		public string TargetKey { get; set; }
		public string StagedRowId { get; set; }
		/// <summary><see cref="ImportJournalOperation"/>.</summary>
		public int Operation { get; set; }
		/// <summary>JSON before-values of the fields the import changed; encrypted when protected.</summary>
		public string BeforeValues { get; set; }
		public bool BeforeValuesEncrypted { get; set; }
		public string AfterHash { get; set; }
		/// <summary>Target ModifiedOn/revision at write; rollback refuses when it moved (section 11.8).</summary>
		public string TargetVersionAtWrite { get; set; }
		/// <summary>True when this run created the account (rollback may delete it, section 11.8).</summary>
		public bool CreatedAccount { get; set; }
		/// <summary><see cref="ImportRollbackOutcome"/>.</summary>
		public int RollbackOutcome { get; set; }
		public string RollbackRunId { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime? RolledBackOn { get; set; }
	}

	/// <summary>One reconciliation check result (section 15.1).</summary>
	public sealed class ImportReconciliation : ImportRow
	{
		public string WorkspaceId { get; set; }
		public string RunId { get; set; }
		public string CheckId { get; set; }
		public string EntityKey { get; set; }
		public long Expected { get; set; }
		public long Actual { get; set; }
		/// <summary><see cref="ImportReconciliationState"/>.</summary>
		public int State { get; set; }
		/// <summary>Localization key of a system explanation, or null.</summary>
		public string ExplanationKey { get; set; }
		/// <summary>Administrator's own explanation of a difference.</summary>
		public string Explanation { get; set; }
		public string ExplainedBy { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime? ExplainedOn { get; set; }
	}

	/// <summary>Department consent to Resgrid staff (sections 17.2 and 17.8).</summary>
	public sealed class ImportSupportGrant : ImportRow
	{
		public string WorkspaceId { get; set; }
		/// <summary><see cref="ImportSupportAccessLevel"/>.</summary>
		public int Level { get; set; }
		/// <summary><see cref="ImportOperateActions"/> flags (Operate only).</summary>
		public int AllowedActions { get; set; }
		/// <summary><see cref="ImportGrantAuthorizationKind"/>.</summary>
		public int AuthorizationKind { get; set; }
		/// <summary><see cref="ImportGrantStatus"/>.</summary>
		public int Status { get; set; }
		public string EngagementReference { get; set; }
		public string Reason { get; set; }
		/// <summary>The administrator who granted it (department authorization).</summary>
		public string GrantedByUserId { get; set; }
		/// <summary>Contract authorization: the requesting SuperAdmin (staff identity).</summary>
		public string RequestedByStaff { get; set; }
		/// <summary>Contract authorization: the approving, different SuperAdmin.</summary>
		public string ApprovedByStaff { get; set; }
		/// <summary>The department identity staff writes are attributed to (section 17.8).</summary>
		public string OnBehalfOfUserId { get; set; }
		public bool RequireDepartmentSignOff { get; set; }
		public string Message { get; set; }
		public DateTime GrantedOn { get; set; }
		public DateTime ExpiresOn { get; set; }
		public string RevokedBy { get; set; }
		public DateTime? RevokedOn { get; set; }
		public int Version { get; set; } = 1;
	}

	/// <summary>A department's import support request ("ticket", section 17.5).</summary>
	public sealed class ImportSupportRequest : ImportRow
	{
		public string WorkspaceId { get; set; }
		/// <summary>Short, human-friendly request number shown to the administrator.</summary>
		public string Number { get; set; }
		public string DatasetId { get; set; }
		public string RunId { get; set; }
		/// <summary>Comma-separated issue codes the request is about (no values).</summary>
		public string IssueCodes { get; set; }
		/// <summary><see cref="ImportSupportRequestCategory"/>.</summary>
		public int Category { get; set; }
		public string Subject { get; set; }
		public string Description { get; set; }
		/// <summary><see cref="ImportSupportRequestPriority"/>.</summary>
		public int Priority { get; set; }
		/// <summary><see cref="ImportSupportRequestStatus"/>.</summary>
		public int Status { get; set; }
		public string AssigneeIdentity { get; set; }
		/// <summary>JSON value-free context chips captured at opening.</summary>
		public string Context { get; set; }
		/// <summary><see cref="ImportHelpdeskState"/>.</summary>
		public int HelpdeskState { get; set; }
		public int HelpdeskAttempts { get; set; }
		public DateTime? HelpdeskNextAttemptOn { get; set; }
		public string HelpdeskTicketId { get; set; }
		public string HelpdeskTicketUrl { get; set; }
		public string LinkedGrantId { get; set; }
		public string OpenedBy { get; set; }
		public DateTime OpenedOn { get; set; }
		public DateTime? FirstResponseOn { get; set; }
		public DateTime? ResolvedOn { get; set; }
		public DateTime? ClosedOn { get; set; }
		public DateTime UpdatedOn { get; set; }
	}

	/// <summary>The shared administrator/support thread for a workspace (section 17.5).</summary>
	public sealed class ImportMessage : ImportRow
	{
		public string WorkspaceId { get; set; }
		public string SupportRequestId { get; set; }
		/// <summary><see cref="ImportActorKind"/> (System, Administrator, ResgridStaff).</summary>
		public int AuthorKind { get; set; }
		/// <summary>User id, or staff email/sub.</summary>
		public string AuthorIdentity { get; set; }
		/// <summary>Plain text body (HTML-encoded at render). Null for localized system messages.</summary>
		public string Body { get; set; }
		/// <summary>Localization key for a system message.</summary>
		public string BodyKey { get; set; }
		/// <summary>JSON array of value-free arguments for <see cref="BodyKey"/>.</summary>
		public string BodyArgs { get; set; }
		public DateTime CreatedOn { get; set; }
		/// <summary>
		/// <see cref="ImportHelpdeskState"/> of the message's copy on the request's helpdesk ticket (section 17.5): staff replies to a
		/// request with a ticket are mirrored as private notes through the worker 48 outbox; NotConfigured for everything else.
		/// </summary>
		public int HelpdeskState { get; set; }
		public int HelpdeskAttempts { get; set; }
		public DateTime? HelpdeskNextAttemptOn { get; set; }
		public DateTime? ReadByDepartmentOn { get; set; }
		public DateTime? ReadByStaffOn { get; set; }
	}

	/// <summary>A staff-proposed fix shown to the administrator as "Suggested by Resgrid Support" (section 17.5).</summary>
	public sealed class ImportSuggestion : ImportRow
	{
		public string WorkspaceId { get; set; }
		public string DatasetId { get; set; }
		/// <summary><see cref="ImportSuggestionKind"/>.</summary>
		public int Kind { get; set; }
		/// <summary>JSON payload (a column map body, value map decisions or an issue resolution).</summary>
		public string Payload { get; set; }
		/// <summary>Issue id, field key or other reference the suggestion applies to.</summary>
		public string TargetRef { get; set; }
		public string Note { get; set; }
		/// <summary><see cref="ImportSuggestionStatus"/>.</summary>
		public int Status { get; set; }
		public string SuggestedBy { get; set; }
		public DateTime SuggestedOn { get; set; }
		public string DecidedBy { get; set; }
		public DateTime? DecidedOn { get; set; }
	}

	/// <summary>Workspace timeline shared by the Core and BackOffice consoles (section 8.6).</summary>
	public sealed class ImportEvent : ImportRow
	{
		public string WorkspaceId { get; set; }
		/// <summary><see cref="ImportActorKind"/>.</summary>
		public int ActorKind { get; set; }
		public string ActorIdentity { get; set; }
		/// <summary>Stable action name ("WorkspaceCreated", "RunApproved", "StaffCommitted").</summary>
		public string Action { get; set; }
		/// <summary>Sanitized, value-free JSON details.</summary>
		public string Details { get; set; }
		public string CorrelationId { get; set; }
		public DateTime CreatedOn { get; set; }
	}

	/// <summary>Product keys of <see cref="ImportSource"/> rows the engine creates itself.</summary>
	public static class ImportSourceProducts
	{
		/// <summary>
		/// The workspace continues an earlier import (plan section 5.2): its data sets without a declared source share the
		/// earlier import's source-system key. At most one per workspace; its Label is the earlier workspace's name and its
		/// ConnectorReference that workspace's id.
		/// </summary>
		public const string Continuation = "continuation";
	}

	public static class DepartmentImportTables
	{
		public static readonly IReadOnlyDictionary<Type, string> All = new Dictionary<Type, string>
		{
			[typeof(ImportWorkspace)] = "ImportWorkspaces",
			[typeof(ImportSource)] = "ImportSources",
			[typeof(ImportArtifact)] = "ImportArtifacts",
			[typeof(ImportDataset)] = "ImportDatasets",
			[typeof(ImportColumnMap)] = "ImportColumnMaps",
			[typeof(ImportValueMap)] = "ImportValueMaps",
			[typeof(ImportStagedRow)] = "ImportStagedRows",
			[typeof(ImportIssue)] = "ImportIssues",
			[typeof(ImportExternalId)] = "ImportExternalIds",
			[typeof(ImportRun)] = "ImportRuns",
			[typeof(ImportChangeJournalEntry)] = "ImportChangeJournal",
			[typeof(ImportReconciliation)] = "ImportReconciliations",
			[typeof(ImportSupportGrant)] = "ImportSupportGrants",
			[typeof(ImportSupportRequest)] = "ImportSupportRequests",
			[typeof(ImportMessage)] = "ImportMessages",
			[typeof(ImportSuggestion)] = "ImportSuggestions",
			[typeof(ImportEvent)] = "ImportEvents",
			[typeof(ImportConfigurationChange)] = "ImportConfigurationChanges"
		};
	}
}
