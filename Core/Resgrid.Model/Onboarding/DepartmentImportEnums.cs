using System;

namespace Resgrid.Model.Onboarding
{
	// Department Import (Import & Migration) persisted enums. Every value is stored in an Import* table
	// column, so members are append-only: never renumber, never reuse (wizard plan section 18.3 I10).

	/// <summary>How a workspace writes into the department (plan section 5.2).</summary>
	public enum ImportWorkspaceMode
	{
		NewDepartment = 1,
		ExistingAdd = 2,
		ExistingReviewedUpdate = 3,
		ConfigurationPresetOnly = 4,
		IncrementalContinuation = 5,
		HistoricalArchive = 6,
		DepartmentPackage = 7
	}

	/// <summary>
	/// Workspace lifecycle (plan section 10.2). Derived from datasets and runs, never set by hand except
	/// Cancelled; Abandoned is set by the maintenance worker after 90 days without activity.
	/// </summary>
	public enum ImportWorkspaceStatus
	{
		Draft = 0,
		Collecting = 1,
		Reviewing = 2,
		Importing = 3,
		Verifying = 4,
		ReadyToActivate = 5,
		Active = 6,
		Closed = 7,
		Abandoned = 8,
		Cancelled = 9
	}

	/// <summary>Who drives the workspace (plan section 17.1). Orthogonal to the mode.</summary>
	public enum ImportOperatingModel
	{
		SelfService = 0,
		ManagedMigration = 1
	}

	/// <summary>Dataset lifecycle (plan section 10.2).</summary>
	public enum ImportDatasetStatus
	{
		Uploading = 0,
		Scanning = 1,
		Analyzing = 2,
		NeedsMapping = 3,
		NeedsAttention = 4,
		Ready = 5,
		Approved = 6,
		Importing = 7,
		Imported = 8,
		Verified = 9,
		Excluded = 10,
		Superseded = 11,
		Failed = 12
	}

	/// <summary>What an artifact holds.</summary>
	public enum ImportArtifactKind
	{
		Upload = 1,

		/// <summary>The encrypted raw-page snapshot written by an API extraction run (plan section 8.8).</summary>
		ExtractionSnapshot = 2,

		/// <summary>A generated download kept for audit (mapping or error report).</summary>
		Report = 3,

		/// <summary>
		/// A department logo uploaded on the Configure step (PNG or JPEG, at most 2 MB). It is decoded and re-encoded into
		/// renditions when the run writes it, never analysed into data sets and never served as uploaded.
		/// </summary>
		Logo = 4
	}

	/// <summary>Multipart upload progress of an artifact (plan section 8.7).</summary>
	public enum ImportArtifactUploadState
	{
		InProgress = 0,
		Completed = 1,
		Aborted = 2,
		Expired = 3,
		Purged = 4
	}

	/// <summary>Malware scan state (plan section 14.1). Text formats may be NotRequired when scanning is off.</summary>
	public enum ImportArtifactScanState
	{
		Pending = 0,
		Clean = 1,
		Infected = 2,
		Failed = 3,
		NotRequired = 4,
		Rejected = 5
	}

	/// <summary>Formats the readers understand (plan section 8.3).</summary>
	public enum ImportFileFormat
	{
		Unknown = 0,
		Csv = 1,
		Tsv = 2,
		Xlsx = 3,
		Json = 4,
		JsonLines = 5,
		Zip = 6,
		Kml = 7,
		Kmz = 8,
		GeoJson = 9,
		Gpx = 10,
		NfirsFlat = 11
	}

	/// <summary>Where a column map came from (plan section 8.4).</summary>
	public enum ImportColumnMapOrigin
	{
		Administrator = 1,
		ResgridTemplate = 2,
		ProductPack = 3,
		SupportSuggestion = 4,
		MapLibrary = 5,
		StaffOperator = 6
	}

	/// <summary>A value-map decision for one distinct source value (plan section 6.3 step 7).</summary>
	public enum ImportValueMapAction
	{
		MapToExisting = 1,
		CreateNew = 2,
		LeaveEmpty = 3,
		KeepAsSourceLabel = 4
	}

	/// <summary>Who made a decision (value map, issue resolution, suggestion acceptance).</summary>
	public enum ImportActorKind
	{
		System = 0,
		Administrator = 1,
		ResgridStaff = 2,
		SupportSuggestion = 3
	}

	/// <summary>Validation state of a staged row after the ladder.</summary>
	public enum ImportRowValidationState
	{
		Pending = 0,
		Valid = 1,
		Warning = 2,
		Blocked = 3,
		Excluded = 4
	}

	/// <summary>The writer decision for a staged row (plan section 11.1 PlanAsync).</summary>
	public enum ImportRowDecision
	{
		Undecided = 0,
		Create = 1,
		Update = 2,
		Link = 3,
		Skip = 4,
		Unchanged = 5,

		/// <summary>An account that exists only in another department: invited to join at activation (section 12.3).</summary>
		InviteToJoin = 6,

		/// <summary>A removed member of this department: brought back as a dormant member, acknowledged at approval (section 12.3).</summary>
		Reactivate = 7
	}

	/// <summary>Final per-row outcome after commit ("nothing is silent", principle 15).</summary>
	public enum ImportRowOutcome
	{
		None = 0,
		Created = 1,
		Updated = 2,
		Linked = 3,
		Unchanged = 4,
		Skipped = 5,
		Failed = 6
	}

	public enum ImportIssueSeverity
	{
		Info = 0,
		Warning = 1,
		Blocking = 2
	}

	public enum ImportIssueStatus
	{
		Open = 0,
		Resolved = 1,
		Accepted = 2,
		Excluded = 3
	}

	/// <summary>How an issue left the Open state.</summary>
	public enum ImportIssueResolutionKind
	{
		None = 0,
		ValueEdited = 1,
		RuleApplied = 2,
		RowsExcluded = 3,
		WarningAccepted = 4,
		FixedByReupload = 5,
		ResolvedBySystem = 6,
		SuggestionAccepted = 7,
		MappingChanged = 8
	}

	/// <summary>The verification ladder levels (plan section 9).</summary>
	public enum ImportLadderLevel
	{
		L0FileSafety = 0,
		L1Parse = 1,
		L2Shape = 2,
		L3Type = 3,
		L4RowRules = 4,
		L5FileRules = 5,
		L6References = 6,
		L7Department = 7,
		L8Consequences = 8,
		L9ApprovalIntegrity = 9,
		L10Reconciliation = 10
	}

	/// <summary>Claim state of an external-id map entry (plan section 11.3).</summary>
	public enum ImportExternalIdState
	{
		Claimed = 0,
		Written = 1,
		Linked = 2,
		Released = 3
	}

	public enum ImportRunKind
	{
		Analysis = 1,
		Commit = 2,
		Rollback = 3,
		Reconcile = 4,
		Extract = 5,
		Activation = 6
	}

	/// <summary>Run lifecycle (plan section 10.2).</summary>
	public enum ImportRunStatus
	{
		Queued = 0,
		Running = 1,
		Paused = 2,
		Reconciling = 3,
		Completed = 4,
		Failed = 5,
		Cancelled = 6,

		/// <summary>Managed migration: frozen digest waiting for a second staff member (section 17.8).</summary>
		AwaitingChecker = 7
	}

	/// <summary>What a cancel request asks the worker to do at the next chunk boundary.</summary>
	public enum ImportRunCancelRequest
	{
		None = 0,
		Pause = 1,
		Cancel = 2
	}

	public enum ImportWriteMode
	{
		CreateOnly = 1,
		ReviewedUpdate = 2,
		Replace = 3
	}

	/// <summary>Journal operation (plan section 8.6 ImportChangeJournal).</summary>
	public enum ImportJournalOperation
	{
		Create = 1,
		Update = 2,
		Link = 3,
		Setting = 4,

		/// <summary>A removed membership brought back (dormant); before-values hold the membership flags and what was added.</summary>
		Reactivate = 5,

		/// <summary>A record a Replace retired (soft-deleted) because the file did not contain it; rollback restores it.</summary>
		Retire = 6
	}

	/// <summary>What a rollback did with one journal entry (section 11.8).</summary>
	public enum ImportRollbackOutcome
	{
		None = 0,
		Deleted = 1,
		SoftDeleted = 2,
		Restored = 3,
		MembershipRemoved = 4,
		ManualReconciliation = 5
	}

	public enum ImportReconciliationState
	{
		Matched = 0,
		Explained = 1,
		Unexplained = 2
	}

	/// <summary>Staff access levels granted by the department (plan section 17.2).</summary>
	public enum ImportSupportAccessLevel
	{
		None = 0,
		Observe = 1,
		Inspect = 2,
		Prepare = 3,
		Operate = 4
	}

	/// <summary>Actions an Operate grant allows (plan section 17.8). Prepare is always allowed.</summary>
	[Flags]
	public enum ImportOperateActions
	{
		None = 0,
		Prepare = 1,
		Commit = 2,
		Rollback = 4,
		Activate = 8,
		SendInvitations = 16,
		Replace = 32,

		/// <summary>The default when an administrator grants Operate: everything except Replace.</summary>
		Default = Prepare | Commit | Rollback | Activate | SendInvitations
	}

	public enum ImportGrantAuthorizationKind
	{
		DepartmentAdministrator = 1,

		/// <summary>Requested by one SuperAdmin and approved by a different one (section 17.8).</summary>
		Contract = 2
	}

	public enum ImportGrantStatus
	{
		Active = 0,

		/// <summary>Contract authorization waiting for the second SuperAdmin.</summary>
		PendingApproval = 1,
		Revoked = 2,
		Expired = 3,
		Rejected = 4
	}

	public enum ImportSupportRequestCategory
	{
		Error = 1,
		DataProblem = 2,
		Question = 3,
		RequestManagedMigration = 4
	}

	public enum ImportSupportRequestStatus
	{
		Open = 0,
		Acknowledged = 1,
		WaitingOnDepartment = 2,
		Resolved = 3,
		Closed = 4
	}

	public enum ImportSupportRequestPriority
	{
		Low = 0,
		Normal = 1,
		High = 2,
		Urgent = 3
	}

	/// <summary>Outbox state of the helpdesk ticket mirror (plan section 17.5).</summary>
	public enum ImportHelpdeskState
	{
		NotConfigured = 0,
		Pending = 1,
		Created = 2,
		Failed = 3
	}

	public enum ImportSuggestionKind
	{
		ColumnMap = 1,
		ValueMap = 2,
		IssueResolution = 3,
		Note = 4
	}

	public enum ImportSuggestionStatus
	{
		Proposed = 0,
		Accepted = 1,
		Rejected = 2,
		Superseded = 3
	}

	/// <summary>DepartmentMember.ImportActivationState (ProtoMember 14). Null means not imported, or active.</summary>
	public enum ImportActivationState
	{
		Dormant = 1,
		Invited = 2,
		Activated = 3
	}

	/// <summary>Sensitivity of a contract field (plan section 8.3); drives staging encryption and staff visibility.</summary>
	public enum ImportFieldSensitivity
	{
		Internal = 0,
		Personal = 1,
		Restricted = 2
	}
}
