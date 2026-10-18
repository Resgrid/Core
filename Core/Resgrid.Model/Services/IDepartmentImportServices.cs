using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Onboarding;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Who may use Department Import and whether it is on (wizard plan section 5.1). Fails closed: any lookup error
	/// reads as "not allowed". Re-checked at every service and worker boundary, never trusted from a queued id (14.2).
	/// </summary>
	public interface IDepartmentImportAccessService
	{
		/// <summary>The master flag Onboarding.MigrationWizard is on for the department.</summary>
		Task<bool> IsEnabledAsync(int departmentId);

		/// <summary>The operator-only flag Onboarding.ManagedMigration is on (gates the Operate grant level).</summary>
		Task<bool> IsManagedMigrationEnabledAsync(int departmentId);

		/// <summary>
		/// The user may manage imports: the flag is on, the user is a current department administrator (permission 72 is
		/// department-admins-only) and, when <paramref name="records"/>, also holds 73.
		/// </summary>
		Task<bool> CanManageAsync(int departmentId, string userId, bool records = false);

		/// <summary>Whether the artifact store is configured and healthy (uploads fail closed otherwise).</summary>
		Task<ImportStoreHealth> GetStoreHealthAsync(CancellationToken cancellationToken = default);
	}

	/// <summary>Thrown for a refused import operation; carries a stable code the UI localizes ("IMP-..." or "import_...").</summary>
	public sealed class DepartmentImportException : InvalidOperationException
	{
		public DepartmentImportException(string code, int statusCode = 400) : base(code)
		{
			Code = code;
			StatusCode = statusCode;
		}

		public string Code { get; }
		public int StatusCode { get; }
	}

	/// <summary>Workspace, source and dataset lifecycle (plan sections 6, 8.6, 10.2).</summary>
	public interface IDepartmentImportService
	{
		Task<List<ImportWorkspace>> GetWorkspacesAsync(int departmentId, CancellationToken cancellationToken = default);

		/// <summary>The workspace with this id in this department, or null.</summary>
		Task<ImportWorkspace> GetWorkspaceAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default);

		Task<ImportWorkspace> CreateWorkspaceAsync(int departmentId, string userId, ImportWorkspaceMode mode, string name, string internalReference,
			DateTime? targetActivationDate, CancellationToken cancellationToken = default);

		Task<ImportWorkspace> UpdateWorkspaceAsync(int departmentId, string userId, string workspaceId, string name, string internalReference,
			DateTime? targetActivationDate, int expectedRevision, CancellationToken cancellationToken = default);

		/// <summary>Step 3: the chosen domains, products and owners.</summary>
		Task<ImportWorkspace> SaveDomainPlanAsync(int departmentId, string userId, string workspaceId, ImportDomainPlan plan, int expectedRevision,
			CancellationToken cancellationToken = default);

		Task<ImportWorkspace> CancelWorkspaceAsync(int departmentId, string userId, string workspaceId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Records a product the department is moving from (a catalog pack, plan section 7.6): its glossary is shown while
		/// matching and its words are offered as terminology on the Configure step. It does not change any data set's
		/// source-system key. A product already chosen, an unknown one or a reserved key is refused.
		/// </summary>
		Task<ImportSource> AddSourceAsync(int departmentId, string actor, ImportActorKind actorKind, string workspaceId, string productKey,
			CancellationToken cancellationToken = default);

		/// <summary>Removes a chosen product; refused for a continuation source and for one a data set declares as its source.</summary>
		Task RemoveSourceAsync(int departmentId, string actor, ImportActorKind actorKind, string workspaceId, string sourceId, CancellationToken cancellationToken = default);

		Task<List<ImportSource>> GetSourcesAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Earlier workspaces of this department that imported rows and so can be continued (incremental continuation, plan
		/// section 5.2): most recent first, never this workspace.
		/// </summary>
		Task<List<ImportContinuationCandidate>> GetContinuationCandidatesAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default);

		/// <summary>The workspace's continuation source (<see cref="ImportSourceProducts.Continuation"/>), or null.</summary>
		Task<ImportSource> GetContinuationAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Declares that this workspace continues an earlier one: every data set without a declared source then uses the earlier
		/// import's source-system key, so its Source IDs are recognized (unchanged, or reviewed updates) and other files may refer
		/// to its records by them. <paramref name="earlierWorkspaceId"/> null stops continuing. Refused once this workspace has
		/// written anything (the key would split); re-runs the checks of every live data set.
		/// </summary>
		Task<ImportSource> SetContinuationAsync(int departmentId, string actor, ImportActorKind actorKind, string workspaceId, string earlierWorkspaceId,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// How the workspace treats records that already exist (Add only or Review updates). The preview and every row decision
		/// are planned for this mode, so changing it withdraws approvals and re-runs the checks; approval must name the same mode.
		/// Replace is not available here (section 5.2).
		/// </summary>
		Task<ImportWorkspace> SetWriteModeAsync(int departmentId, string actor, ImportActorKind actorKind, string workspaceId, ImportWriteMode mode,
			CancellationToken cancellationToken = default);

		/// <summary>Whether Replace may be chosen now: Resgrid support holds Prepare access or more to the workspace (section 5.2).</summary>
		Task<bool> HasReplaceSupportAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default);

		Task<List<ImportArtifact>> GetArtifactsAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default);

		Task<List<ImportDataset>> GetDatasetsAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default);

		Task<ImportDataset> GetDatasetAsync(int departmentId, string datasetId, CancellationToken cancellationToken = default);

		/// <summary>Confirms or changes a dataset's destination entity; re-runs analysis when the destination changed.</summary>
		Task<ImportDataset> SetDestinationAsync(int departmentId, string actor, ImportActorKind actorKind, string datasetId, string entityKey, int expectedRevision,
			CancellationToken cancellationToken = default);

		/// <summary>Removes a dataset from the import, or puts it back (its rows are kept until the retention purge).</summary>
		Task<ImportDataset> ExcludeDatasetAsync(int departmentId, string actor, ImportActorKind actorKind, string datasetId, bool excluded,
			CancellationToken cancellationToken = default);

		/// <summary>Re-derives the workspace status from its datasets and runs and persists it when it changed (10.2).</summary>
		Task<ImportWorkspace> RefreshStatusAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default);

		/// <summary>Appends a workspace timeline event (value-free details).</summary>
		Task RecordEventAsync(int departmentId, string workspaceId, ImportActorKind actorKind, string actorIdentity, string action,
			object details = null, string correlationId = null, CancellationToken cancellationToken = default);

		Task<List<ImportEvent>> GetEventsAsync(int departmentId, string workspaceId, int skip, int take, CancellationToken cancellationToken = default);

		Task<ImportWorkspaceSummary> GetSummaryAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default);
	}

	/// <summary>The hub's numbers and next best action (plan section 6.2).</summary>
	public sealed class ImportWorkspaceSummary
	{
		public ImportWorkspace Workspace { get; set; }
		public List<ImportDataset> Datasets { get; set; } = new List<ImportDataset>();
		public List<ImportRun> OpenRuns { get; set; } = new List<ImportRun>();
		public int RowsAnalysed { get; set; }
		public int RowsReady { get; set; }
		public int RowsNeedingAttention { get; set; }
		public int RowsImported { get; set; }
		public int PeopleToInvite { get; set; }
		public int OpenBlockingIssues { get; set; }
		public int UnreadMessages { get; set; }
		public int OpenSupportRequests { get; set; }
		public int ReadyDatasets { get; set; }
		public int TotalDatasets { get; set; }
		/// <summary>Localization key of the next best action and its value-free arguments.</summary>
		public string NextActionKey { get; set; }
		public string NextActionDatasetId { get; set; }
		public List<string> NextActionArgs { get; set; } = new List<string>();
		public ImportStoreHealth StoreHealth { get; set; }
	}

	/// <summary>Chunked, resumable uploads into the artifact store (plan section 8.7).</summary>
	public interface IImportUploadService
	{
		/// <summary>Begins an upload; returns the artifact with its multipart upload started.</summary>
		Task<ImportUploadStatus> StartAsync(int departmentId, string userId, string workspaceId, string fileName, long sizeBytes, string sha256,
			string contentType, string sourceId, bool staffAuthored = false, CancellationToken cancellationToken = default);

		/// <summary>Appends the next chunk (1-based part number); idempotent for a part already received with the same hash.</summary>
		Task<ImportUploadStatus> AppendAsync(int departmentId, string artifactId, int partNumber, byte[] chunk, string chunkSha256,
			CancellationToken cancellationToken = default);

		/// <summary>Completes the upload and queues the analysis run (scan, detection, staging).</summary>
		Task<ImportUploadStatus> CompleteAsync(int departmentId, string userId, string artifactId, CancellationToken cancellationToken = default);

		Task<ImportUploadStatus> GetStatusAsync(int departmentId, string artifactId, CancellationToken cancellationToken = default);

		/// <summary>Maintenance: aborts uploads with no progress for ImportConfig.AbandonedUploadHours.</summary>
		Task<int> AbortStaleUploadsAsync(CancellationToken cancellationToken = default);
	}

	public sealed class ImportUploadStatus
	{
		public string ArtifactId { get; set; }
		public int ChunkSizeBytes { get; set; }
		public long ReceivedBytes { get; set; }
		public long ExpectedBytes { get; set; }
		public int NextPartNumber { get; set; }
		public bool Completed { get; set; }
		public string AnalysisRunId { get; set; }
		public string ErrorCode { get; set; }
	}

	/// <summary>Analysis: scan, detection, dataset split, profiling, staging and ladder levels L1–L6 (plan sections 9, 10.1).</summary>
	public interface IImportAnalysisService
	{
		/// <summary>Analyzes one artifact inside a leased Analysis run. Returns false when the run should retry later.</summary>
		Task<ImportStepResult> AnalyzeArtifactAsync(ImportRun run, CancellationToken cancellationToken = default);

		/// <summary>Re-stages and re-validates one dataset after a mapping or value-map change (incremental ladder, section 9).</summary>
		Task<ImportStepResult> RevalidateDatasetAsync(int departmentId, string datasetId, CancellationToken cancellationToken = default);
	}

	/// <summary>The outcome of one bounded step of a run.</summary>
	public sealed class ImportStepResult
	{
		public bool Completed { get; set; }
		/// <summary>Transient failure: retry with backoff (section 11.4).</summary>
		public bool Retry { get; set; }
		/// <summary>Deterministic failure or a gate lost: pause and wait for the administrator.</summary>
		public bool Pause { get; set; }
		public string ErrorCode { get; set; }

		public static ImportStepResult Done() => new ImportStepResult { Completed = true };
		public static ImportStepResult More() => new ImportStepResult();
		public static ImportStepResult Transient(string code) => new ImportStepResult { Retry = true, ErrorCode = code };
		public static ImportStepResult Paused(string code) => new ImportStepResult { Pause = true, ErrorCode = code };
		public static ImportStepResult Failed(string code) => new ImportStepResult { ErrorCode = code };
	}

	/// <summary>Run queueing, leasing and execution (plan sections 10.4, 11.4, 13).</summary>
	public interface IImportRunService
	{
		/// <summary>Queues a run and sends the RabbitMQ nudge (worker 47 sweeps anything the nudge missed).</summary>
		Task<ImportRun> QueueAsync(ImportRun run, CancellationToken cancellationToken = default);

		Task<ImportRun> GetRunAsync(int departmentId, string runId, CancellationToken cancellationToken = default);

		Task<List<ImportRun>> GetRunsAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default);

		/// <summary>Pause or cancel at the next chunk boundary.</summary>
		Task<ImportRun> RequestCancelAsync(int departmentId, string userId, string runId, ImportRunCancelRequest request, CancellationToken cancellationToken = default);

		/// <summary>Resumes a paused run (clears the cancel request and re-queues it).</summary>
		Task<ImportRun> ResumeAsync(int departmentId, string userId, string runId, CancellationToken cancellationToken = default);

		/// <summary>Worker 47: leases and processes runs until the budget is spent. Returns how many runs were served.</summary>
		Task<int> ProcessAvailableAsync(string workerName, TimeSpan budget, CancellationToken cancellationToken = default);

		/// <summary>The RabbitMQ nudge path: leases and processes one specific run if it is runnable.</summary>
		Task<bool> ProcessRunAsync(string runId, string workerName, TimeSpan budget, CancellationToken cancellationToken = default);

		/// <summary>Watchdog: fails stalled runs (IMP-RUN-STALLED) so their next attempt resumes.</summary>
		Task<int> FailStalledRunsAsync(CancellationToken cancellationToken = default);
	}

	/// <summary>A kind-specific run executor (analysis, commit, rollback, reconcile, extract, activation).</summary>
	public interface IImportRunHandler
	{
		ImportRunKind Kind { get; }

		/// <summary>
		/// Does one bounded slice of the run (until done, or until <paramref name="deadline"/>), persisting the cursor so a
		/// crash at any point resumes. Never throws for expected failures; returns the step outcome.
		/// </summary>
		Task<ImportStepResult> ExecuteAsync(ImportRun run, ImportRunLease lease, DateTime deadline, CancellationToken cancellationToken);
	}

	/// <summary>The lease a handler holds; heartbeats extend it and fail when it was lost.</summary>
	public sealed class ImportRunLease
	{
		public ImportRunLease(string owner, Func<CancellationToken, Task<bool>> heartbeat, Func<CancellationToken, Task<bool>> save)
		{
			Owner = owner;
			Heartbeat = heartbeat;
			Save = save;
		}

		public string Owner { get; }

		/// <summary>Extends the lease; false when another worker took it (stop immediately).</summary>
		public Func<CancellationToken, Task<bool>> Heartbeat { get; }

		/// <summary>Persists the run's cursor/counts under the lease; false when the lease was lost.</summary>
		public Func<CancellationToken, Task<bool>> Save { get; }
	}

	/// <summary>
	/// Low-level run queueing: inserts a Queued run and sends the RabbitMQ nudge (a lost nudge only delays the start until
	/// worker 47's minute sweep). Depends on nothing but the run repository and the queue, so any service can queue work.
	/// </summary>
	public interface IImportRunQueue
	{
		Task<ImportRun> QueueAsync(ImportRun run, CancellationToken cancellationToken = default);

		/// <summary>Re-sends the nudge for an existing queued run (resume, retry now).</summary>
		Task NudgeAsync(ImportRun run);
	}

	/// <summary>The workspace timeline shared by the Core and BackOffice consoles (ImportEvents).</summary>
	public interface IImportTimelineService
	{
		Task RecordAsync(int departmentId, string workspaceId, ImportActorKind actorKind, string actorIdentity, string action,
			object details = null, string correlationId = null, CancellationToken cancellationToken = default);

		Task<List<ImportEvent>> GetAsync(int departmentId, string workspaceId, int skip, int take, CancellationToken cancellationToken = default);
	}

	/// <summary>Worker 48 (hourly) and the watchdog sub-pass (plan section 13.2). Never throws.</summary>
	public interface IDepartmentImportMaintenanceService
	{
		/// <summary>The 5-minute watchdog inside worker 47's tick: stalled runs, expired leases, pending scans.</summary>
		Task<string> RunWatchdogAsync(CancellationToken cancellationToken = default);

		/// <summary>The hourly pass: expiry, retention purge, reminders, helpdesk outbox, orphaned objects, health summary.</summary>
		Task<string> RunHourlyAsync(CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// Targeted notices to the workspace owner (EventTypes 50 and 51, [NotMapped]; plan sections 13.1 and 17.5). The
	/// Notify* methods enqueue a NotificationItem after commit; NotificationBroadcastLogic calls Deliver*, which re-reads the
	/// run or workspace and sends a value-free message (codes and counts only) to the owner, or to an active administrator
	/// when the owner is no longer one.
	/// </summary>
	public interface IDepartmentImportNotificationService
	{
		Task NotifyRunFinishedAsync(ImportRun run, CancellationToken cancellationToken = default);

		Task NotifyAbandonmentReminderAsync(ImportWorkspace workspace, CancellationToken cancellationToken = default);

		/// <summary>A staff message, suggestion or support-request status change (51).</summary>
		Task NotifySupportActivityAsync(int departmentId, string workspaceId, string activity, CancellationToken cancellationToken = default);

		/// <summary>Delivers an EventTypes 50 item; <paramref name="value"/> is "run:{runId}" or "reminder:{workspaceId}".</summary>
		Task<bool> DeliverRunNoticeAsync(int departmentId, string value, CancellationToken cancellationToken = default);

		/// <summary>
		/// A notice every active administrator receives, not just the workspace owner (51): a contract authorization, or the
		/// advance notice of a managed activation or invitation send (section 17.8).
		/// </summary>
		Task NotifyAdministratorsAsync(int departmentId, string workspaceId, string activity, CancellationToken cancellationToken = default);

		/// <summary>Delivers an EventTypes 51 item; <paramref name="value"/> is "{workspaceId}|{activity}", with "|all" for every administrator.</summary>
		Task<bool> DeliverSupportNoticeAsync(int departmentId, string value, CancellationToken cancellationToken = default);
	}

	/// <summary>Helpdesk ticket outbox (plan section 17.5): a Freshdesk outage never blocks a support request.</summary>
	public interface IDepartmentImportSupportOutbox
	{
		/// <summary>
		/// Creates the helpdesk tickets of due requests, then mirrors due staff replies to their request's ticket as private notes
		/// (a reply waits while its ticket is still being created). Returns how many tickets and notes were sent.
		/// </summary>
		Task<int> ProcessDueAsync(CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// The administrator's side of Import Support (plan sections 17.2 and 17.5): consent grants to Resgrid staff, support
	/// requests mirrored to the helpdesk, the shared message thread and staff suggestions. Raising a grant needs step-up,
	/// which the controller enforces; every change is on the workspace timeline and in the audit log.
	/// </summary>
	public interface IDepartmentImportSupportService
	{
		Task<List<ImportSupportGrant>> GetGrantsAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default);

		/// <summary>The grant in force for the workspace, or null (expired and revoked grants never count).</summary>
		Task<ImportSupportGrant> GetActiveGrantAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default);

		Task<ImportSupportGrant> GrantAccessAsync(int departmentId, string userId, string workspaceId, ImportSupportAccessLevel level, int days,
			ImportOperateActions allowedActions, bool requireDepartmentSignOff, string message, string ipAddress, CancellationToken cancellationToken = default);

		Task RevokeAccessAsync(int departmentId, string userId, string workspaceId, string ipAddress, CancellationToken cancellationToken = default);

		Task<ImportSupportRequest> OpenRequestAsync(int departmentId, string userId, string workspaceId, ImportSupportRequestCategory category, string subject,
			string description, string datasetId, string runId, IReadOnlyCollection<string> issueCodes, CancellationToken cancellationToken = default);

		Task<List<ImportSupportRequest>> GetRequestsAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default);

		/// <summary>The administrator confirms a resolved request or reopens it.</summary>
		Task<ImportSupportRequest> ConfirmOrReopenAsync(int departmentId, string userId, string requestId, bool reopen, CancellationToken cancellationToken = default);

		Task<ImportMessage> PostMessageAsync(int departmentId, string userId, string workspaceId, string body, string supportRequestId,
			CancellationToken cancellationToken = default);

		Task<List<ImportMessage>> GetThreadAsync(int departmentId, string workspaceId, int skip, int take, bool markRead, CancellationToken cancellationToken = default);

		Task<List<ImportSuggestion>> GetSuggestionsAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default);

		/// <summary>Accepting is the administrator's action, attributed to them with Source = SupportSuggestion; it re-validates.</summary>
		Task<ImportSuggestion> DecideSuggestionAsync(int departmentId, string userId, string suggestionId, bool accept, CancellationToken cancellationToken = default);
	}

	/// <summary>Column and value mapping (plan sections 6.3 steps 6–7 and 8.4).</summary>
	public interface IImportMappingService
	{
		Task<ImportMappingView> GetMappingAsync(int departmentId, string datasetId, CancellationToken cancellationToken = default);

		/// <summary>Saves a new version of the dataset's column map, invalidates any approval and re-runs the ladder.</summary>
		Task<ImportColumnMap> SaveColumnMapAsync(int departmentId, string actor, ImportActorKind actorKind, string datasetId, ImportColumnMapBody body,
			int expectedRevision, CancellationToken cancellationToken = default);

		/// <summary>Keeps the dataset's mapping as a named, department-owned map for later files.</summary>
		Task<ImportColumnMap> SaveMapAsAsync(int departmentId, string userId, string datasetId, string name, CancellationToken cancellationToken = default);

		Task<List<ImportColumnMap>> GetSavedMapsAsync(int departmentId, string entityKey, CancellationToken cancellationToken = default);

		Task<ImportColumnMap> ApplySavedMapAsync(int departmentId, string actor, ImportActorKind actorKind, string datasetId, string mapId,
			CancellationToken cancellationToken = default);

		/// <summary>Distinct source values of a choice or reference field with counts, suggestions and current decisions.</summary>
		Task<List<ImportValueCandidate>> GetValueCandidatesAsync(int departmentId, string datasetId, string fieldKey, CancellationToken cancellationToken = default);

		Task<int> SaveValueMapsAsync(int departmentId, string actor, ImportActorKind actorKind, string datasetId, string fieldKey,
			IReadOnlyList<ImportValueDecision> decisions, CancellationToken cancellationToken = default);

		/// <summary>Queues a revalidation run for datasets (the incremental ladder after a fix).</summary>
		Task<ImportRun> RevalidateAsync(int departmentId, string workspaceId, IReadOnlyCollection<string> datasetIds, CancellationToken cancellationToken = default);

		/// <summary>
		/// Answers how a column writes its dates ("dd/MM/yyyy" day first, "MM/dd/yyyy" month first, ...; any separator), once per
		/// column (section 6.4); null goes back to working it out from the values. Saves a new map version and re-runs the checks.
		/// </summary>
		Task<ImportColumnMap> SetDateFormatAsync(int departmentId, string actor, ImportActorKind actorKind, string datasetId, string sourceColumn, string format,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Sets the same value for every row of a field (it fills rows whose cell is empty or that have no column for it); null or
		/// empty removes it. Refused for Source ID and unique fields, and for a value the field cannot hold.
		/// </summary>
		Task<ImportColumnMap> SetConstantAsync(int departmentId, string actor, ImportActorKind actorKind, string datasetId, string fieldKey, string value,
			CancellationToken cancellationToken = default);
	}

	/// <summary>An earlier workspace that wrote rows, offered to be continued (no values, no file names).</summary>
	public sealed class ImportContinuationCandidate
	{
		public string WorkspaceId { get; set; }
		public string Name { get; set; }
		public int DatasetsImported { get; set; }
		public DateTime? LastImportedOn { get; set; }
	}

	public sealed class ImportMappingView
	{
		public ImportDataset Dataset { get; set; }
		public ImportEntityContract Contract { get; set; }
		public ImportColumnMap Map { get; set; }
		public ImportColumnMapBody Body { get; set; }
		public ImportDatasetProfile Profile { get; set; }
		public List<ImportEntityContract> Destinations { get; set; } = new List<ImportEntityContract>();
		public IReadOnlyCollection<string> Transforms { get; set; } = Array.Empty<string>();
		/// <summary>The date formats a column can be told to read (ImportColumnMapBody.Formats, keyed by source column).</summary>
		public IReadOnlyCollection<string> DateFormats { get; set; } = Array.Empty<string>();
	}

	public sealed class ImportValueCandidate
	{
		public string SourceValue { get; set; }
		public int RowCount { get; set; }
		/// <summary>Suggested choice code or existing record key, with its confidence (0–100).</summary>
		public string SuggestedCode { get; set; }
		public string SuggestedLabel { get; set; }
		public int Confidence { get; set; }
		public ImportValueMapAction? Decision { get; set; }
		public string DecidedCode { get; set; }
	}

	public sealed class ImportValueDecision
	{
		public string SourceValue { get; set; }
		public ImportValueMapAction Action { get; set; }
		public string TargetCode { get; set; }
		public int Confidence { get; set; }
	}

	/// <summary>Issue groups, resolutions and inline fixes (plan sections 6.4 and 9).</summary>
	public interface IImportIssueService
	{
		Task<List<ImportIssueGroup>> GetGroupsAsync(int departmentId, string workspaceId, string datasetId, bool openOnly, CancellationToken cancellationToken = default);

		Task<List<ImportIssue>> GetIssuesAsync(int departmentId, string workspaceId, string datasetId, string code, int? status, int skip, int take,
			CancellationToken cancellationToken = default);

		/// <summary>Accepts warnings (never blocking issues) so their rows import as they are.</summary>
		Task<int> AcceptAsync(int departmentId, string actor, ImportActorKind actorKind, string workspaceId, IReadOnlyCollection<string> issueIds,
			CancellationToken cancellationToken = default);

		/// <summary>Excludes the rows behind the issues from the import; re-validates.</summary>
		Task<int> ExcludeRowsAsync(int departmentId, string actor, ImportActorKind actorKind, string workspaceId, IReadOnlyCollection<string> issueIds,
			CancellationToken cancellationToken = default);

		Task<int> ReopenAsync(int departmentId, string actor, ImportActorKind actorKind, string workspaceId, IReadOnlyCollection<string> issueIds,
			CancellationToken cancellationToken = default);

		/// <summary>Inline fix: replaces one source cell of a staged row (validated as typed by the re-run), then re-validates.</summary>
		Task EditValueAsync(int departmentId, string actor, ImportActorKind actorKind, string stagedRowId, string column, string value,
			CancellationToken cancellationToken = default);

		/// <summary>Bulk fix: sets one source column to a value on every row behind the issues ("leave these empty" is an empty value).</summary>
		Task<int> SetValueForIssuesAsync(int departmentId, string actor, ImportActorKind actorKind, string workspaceId, IReadOnlyCollection<string> issueIds,
			string value, CancellationToken cancellationToken = default);

		/// <summary>The decrypted source cells of a staged row, for the editable grid (administrator only; never staff below Inspect).</summary>
		Task<ImportRowView> GetRowAsync(int departmentId, string stagedRowId, CancellationToken cancellationToken = default);
	}

	public sealed class ImportIssueGroup
	{
		public string DatasetId { get; set; }
		public string DatasetName { get; set; }
		public string Code { get; set; }
		public string FieldKey { get; set; }
		public string ColumnName { get; set; }
		public int Severity { get; set; }
		public int Level { get; set; }
		public int Count { get; set; }
		public int OpenCount { get; set; }
		public List<int> SampleRows { get; set; } = new List<int>();
		public List<string> IssueIds { get; set; } = new List<string>();
		public string Parameters { get; set; }
		public bool NeedsDepartment { get; set; }
	}

	public sealed class ImportRowView
	{
		public string StagedRowId { get; set; }
		public string DatasetId { get; set; }
		public int RowNumber { get; set; }
		public List<string> Headers { get; set; } = new List<string>();
		public List<string> Cells { get; set; } = new List<string>();
		public int ValidationState { get; set; }
		public bool Excluded { get; set; }
	}
	/// <summary>
	/// Ladder levels L7–L9 (wizard plan sections 9, 10.2, 11.6 and 12): each row's decision against the department's
	/// data, conflicts, consequences, plan capacity, entitlement gates and the approval digest.
	/// </summary>
	public interface IImportPlanService
	{
		/// <summary>Plans one dataset (L7/L8): row decisions and targets, conflict and consequence issues. Runs after L1–L6.</summary>
		Task<ImportDatasetPlanSummary> PlanDatasetAsync(int departmentId, string datasetId, Func<CancellationToken, Task<bool>> heartbeat,
			CancellationToken cancellationToken = default);

		/// <summary>SHA-256 over the approved datasets' staged state, decisions, maps, versions and write mode (section 10.2).</summary>
		Task<string> ComputeApprovalDigestAsync(int departmentId, IReadOnlyList<string> datasetIds, ImportWriteMode writeMode, CancellationToken cancellationToken = default);

		/// <summary>One fresh read of the plan's remaining personnel and unit seats (two Billing API calls; null = unknown).</summary>
		Task<ImportCapacity> ReadCapacityAsync(int departmentId);

		/// <summary>The contract's entitlement gate (flags, module, add-on); null when allowed, else IMP-ENTITLEMENT-MISSING.</summary>
		Task<string> CheckGateAsync(int departmentId, string entityKey, string catalogVersion);

		/// <summary>The source-system instance key a dataset's rows belong to (scopes the external-id map).</summary>
		Task<string> SourceSystemKeyAsync(ImportDataset dataset, CancellationToken cancellationToken = default);

		/// <summary>Labels of the dataset's "keep as custom field" columns.</summary>
		Task<List<string>> GetKeptCustomFieldLabelsAsync(ImportDataset dataset, CancellationToken cancellationToken = default);
	}

	public sealed class ImportDatasetPlanSummary
	{
		public string DatasetId { get; set; }
		public bool Planned { get; set; }
		/// <summary>ImportRowDecision name → rows.</summary>
		public Dictionary<string, int> Decisions { get; set; } = new Dictionary<string, int>();
		/// <summary>Consequence key → rows (L8).</summary>
		public Dictionary<string, int> Consequences { get; set; } = new Dictionary<string, int>();
		public int PersonnelSeats { get; set; }
		public int UnitSeats { get; set; }
		public int Blocked { get; set; }
		public string GateCode { get; set; }
	}

	/// <summary>Remaining plan seats; null means unknown (Billing API unavailable), which allows no new personnel or units.</summary>
	public sealed class ImportCapacity
	{
		public int? Personnel { get; set; }
		public int? Units { get; set; }
		/// <summary>Entity plans: personnel and units draw on one pool.</summary>
		public bool Shared { get; set; }
		public bool Known => Personnel.HasValue && Units.HasValue;

		public bool Covers(int personnel, int units) =>
			Known && (Shared ? personnel + units <= Personnel.Value : personnel <= Personnel.Value && units <= Units.Value);
	}

	/// <summary>Preview, approval (step-up and second approver) and rollback requests (sections 10.2, 11.8, plan MW-08).</summary>
	public interface IImportApprovalService
	{
		Task<ImportPreview> GetPreviewAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Approves the ready datasets as one wave: acknowledges the stated consequences, re-reads capacity, computes the
		/// approval digest and queues the commit run (AwaitingChecker when a second approver is required). Step-up is the
		/// caller's (controller attribute) responsibility.
		/// </summary>
		Task<ImportRun> ApproveAsync(int departmentId, string userId, string workspaceId, ImportApprovalRequest request, CancellationToken cancellationToken = default);

		/// <summary>
		/// Managed migration (section 17.8): a staff operator under an Operate grant requests an import on the department's
		/// behalf. The run always waits for a different staff checker (and, when the grant asks for it, the department).
		/// </summary>
		Task<ImportRun> RequestStaffCommitAsync(int departmentId, ImportAuthority authority, string workspaceId, ImportApprovalRequest request,
			CancellationToken cancellationToken = default);

		/// <summary>The second (distinct) administrator confirms an AwaitingChecker run; the digest must still match.</summary>
		Task<ImportRun> ApproveAsCheckerAsync(int departmentId, string userId, string runId, CancellationToken cancellationToken = default);

		/// <summary>What a rollback of a commit run would delete, restore or leave (section 11.8).</summary>
		Task<ImportRollbackPreview> GetRollbackPreviewAsync(int departmentId, string commitRunId, CancellationToken cancellationToken = default);

		/// <summary>Queues a Rollback run over a completed commit run's journal, inside the rollback window (decision D7).</summary>
		Task<ImportRun> RequestRollbackAsync(int departmentId, string userId, string commitRunId, CancellationToken cancellationToken = default);
	}

	public sealed class ImportApprovalRequest
	{
		/// <summary>
		/// Datasets to approve; null or empty = every Ready dataset of the workspace. The workspace's accepted configuration
		/// values are always part of the wave, so a wave may be configuration alone.
		/// </summary>
		public List<string> DatasetIds { get; set; } = new List<string>();
		public ImportWriteMode WriteMode { get; set; } = ImportWriteMode.CreateOnly;
		/// <summary>Consequence keys the administrator read and accepted (L8).</summary>
		public List<string> AcknowledgedConsequences { get; set; } = new List<string>();
		public bool SuppressExpiredCertificationNotices { get; set; } = true;
		public bool RelaxCertificationEnforcement { get; set; } = true;
	}

	public sealed class ImportPreview
	{
		public ImportWorkspace Workspace { get; set; }
		public List<ImportPreviewDataset> Datasets { get; set; } = new List<ImportPreviewDataset>();
		public List<ImportPreviewConsequence> Consequences { get; set; } = new List<ImportPreviewConsequence>();
		public int PersonnelSeatsNeeded { get; set; }
		public int UnitSeatsNeeded { get; set; }
		public ImportCapacity Capacity { get; set; }
		public bool RequiresSecondApprover { get; set; }
		public bool CanApprove { get; set; }
		/// <summary>Localization keys of the reasons approval is not possible yet.</summary>
		public List<string> Blockers { get; set; } = new List<string>();
		public ImportRun PendingRun { get; set; }
		/// <summary>The pending run was requested by Resgrid staff (managed migration).</summary>
		public bool PendingRunIsStaff { get; set; }
		/// <summary>The pending staff run still needs its second Resgrid staff member before the department can sign.</summary>
		public bool PendingRunWaitsForStaff { get; set; }
		/// <summary>Accepted configuration values the wave will write first (Configure step, section 7.5).</summary>
		public List<ImportConfigurationChange> Configuration { get; set; } = new List<ImportConfigurationChange>();
		/// <summary>Item key -> value-free reason an accepted configuration value cannot be written as it stands.</summary>
		public Dictionary<string, string> ConfigurationIssues { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
	}

	public sealed class ImportPreviewDataset
	{
		public string DatasetId { get; set; }
		public string Name { get; set; }
		public string EntityKey { get; set; }
		public int Status { get; set; }
		public int Wave { get; set; }
		public int Create { get; set; }
		public int Update { get; set; }
		public int Link { get; set; }
		public int Unchanged { get; set; }
		public int Skip { get; set; }
		public int InviteToJoin { get; set; }
		/// <summary>Removed members brought back as dormant members (each takes a personnel seat).</summary>
		public int Reactivate { get; set; }
		public int Blocked { get; set; }
		public int Excluded { get; set; }
		public int OpenBlockingIssues { get; set; }
	}

	public sealed class ImportPreviewConsequence
	{
		public string Key { get; set; }
		public string DatasetId { get; set; }
		public int Count { get; set; }
		public bool Acknowledged { get; set; }
		public string IssueId { get; set; }
	}

	public sealed class ImportRollbackPreview
	{
		public ImportRun CommitRun { get; set; }
		public bool CanRollback { get; set; }
		public string RefusalCode { get; set; }
		public DateTime? WindowEndsOn { get; set; }
		/// <summary>Entity key → journal operations (Create/Update/Link/Setting) → entries.</summary>
		public Dictionary<string, Dictionary<string, int>> Entries { get; set; } = new Dictionary<string, Dictionary<string, int>>();
	}
	/// <summary>
	/// Verification, activation and invitations (wizard plan section 15): the activation checklist, invitations created
	/// without sending and sent in provider-safe batches, administrator flags applied after confirmation, sign-off, and
	/// the two ways an invitation is accepted (an imported person sets a password; an existing account joins).
	/// </summary>
	public interface IImportActivationService
	{
		Task<ImportActivationChecklist> GetChecklistAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default);

		/// <summary>Creates pending invitations (nothing is sent here) for the chosen dormant people and existing accounts.</summary>
		Task<int> InvitePeopleAsync(int departmentId, string userId, string workspaceId, ImportInvitationRequest request, CancellationToken cancellationToken = default);

		/// <summary>Grants department administrator to imported people flagged "will become administrator", after per-person confirmation.</summary>
		Task<int> ApplyAdministratorFlagsAsync(int departmentId, string userId, string workspaceId, IReadOnlyCollection<string> userIds, CancellationToken cancellationToken = default);

		/// <summary>Records sign-off; a new department becomes Active and the staging retention clock starts.</summary>
		Task<ImportWorkspace> SignOffAsync(int departmentId, string userId, string workspaceId, CancellationToken cancellationToken = default);

		// ---- on the department's behalf (managed migration, section 17.8): the same steps under a staff authority whose
		// operator and checker are both recorded; the on-behalf-of administrator must still hold the import permission ----
		Task<int> InviteOnBehalfAsync(int departmentId, ImportAuthority authority, string workspaceId, ImportInvitationRequest request,
			CancellationToken cancellationToken = default);

		Task<int> ApplyAdministratorsOnBehalfAsync(int departmentId, ImportAuthority authority, string workspaceId, IReadOnlyCollection<string> userIds,
			CancellationToken cancellationToken = default);

		Task<ImportWorkspace> SignOffOnBehalfAsync(int departmentId, ImportAuthority authority, string workspaceId, CancellationToken cancellationToken = default);

		/// <summary>Staff-requested activation and invitation runs of the workspace not yet carried out, for the Verify &amp; activate tab.</summary>
		Task<List<ImportManagedActivationView>> GetManagedRunsAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default);

		/// <summary>
		/// The department's say on a staff-requested activation or invitation run: approve it once two Resgrid staff have (the
		/// third signature, which also starts it before its notice period ends), or cancel it before it is carried out.
		/// </summary>
		Task<ImportRun> DecideManagedRunAsync(int departmentId, string userId, string runId, bool approve, CancellationToken cancellationToken = default);

		Task<bool> RevokeInvitationAsync(int departmentId, string userId, int inviteId, CancellationToken cancellationToken = default);

		Task<bool> ResendInvitationAsync(int departmentId, string userId, int inviteId, CancellationToken cancellationToken = default);

		/// <summary>Worker 47: sends due invitations, ImportConfig.InvitationsPerMinute at a time, outside quiet hours. Returns how many were sent.</summary>
		Task<int> SendDueInvitationsAsync(CancellationToken cancellationToken = default);

		/// <summary>An imported person accepts: sets their first password, confirms their email and becomes Activated.</summary>
		Task<ImportInvitationAcceptance> AcceptAccountInvitationAsync(Guid code, string password, CancellationToken cancellationToken = default);

		/// <summary>An existing Resgrid account accepts an invitation to join; the membership is created now.</summary>
		Task<ImportInvitationAcceptance> AcceptJoinInvitationAsync(Guid code, string signedInUserId, CancellationToken cancellationToken = default);
	}

	public sealed class ImportInvitationRequest
	{
		/// <summary>Dormant people to invite; empty = every dormant person the workspace created.</summary>
		public List<string> UserIds { get; set; } = new List<string>();
		/// <summary>Also invite the existing Resgrid accounts the files named ("invite to join").</summary>
		public bool IncludeExistingAccounts { get; set; }
		/// <summary>Not sent before this time (UTC); null = as soon as possible outside quiet hours.</summary>
		public DateTime? SendAfter { get; set; }
	}

	public enum ImportManagedActivationKind
	{
		/// <summary>Invitations to the dormant people (and, optionally, existing accounts) the workspace created.</summary>
		Invitations = 1,
		/// <summary>Confirm imported administrators and/or record the activation sign-off.</summary>
		Activation = 2
	}

	/// <summary>A staff operator's activation or invitation request (section 17.8), stored on its run until it is carried out.</summary>
	public sealed class ImportManagedActivationRequest
	{
		public ImportManagedActivationKind Kind { get; set; }
		/// <summary>Invitations: who and when.</summary>
		public ImportInvitationRequest Invitations { get; set; } = new ImportInvitationRequest();
		/// <summary>Activation: flagged administrators to confirm.</summary>
		public List<string> AdministratorUserIds { get; set; } = new List<string>();
		/// <summary>Activation: record the sign-off once everything else is done.</summary>
		public bool SignOff { get; set; }

		// Set by Core when the request is made.
		/// <summary>Not carried out before this time: the department's 24-hour notice under a contract authorization.</summary>
		public DateTime? NotBefore { get; set; }
		/// <summary>People the request reaches (invitations) or administrators it confirms, for the department's notice.</summary>
		public int People { get; set; }
		public DateTime? DepartmentApprovedOn { get; set; }
		public string DepartmentApprovedBy { get; set; }
	}

	/// <summary>A staff-requested activation or invitation run as the department sees it.</summary>
	public sealed class ImportManagedActivationView
	{
		public ImportRun Run { get; set; }
		public ImportManagedActivationRequest Request { get; set; }
		public string Operator { get; set; }
		public string Checker { get; set; }
		public string EngagementReference { get; set; }
		/// <summary>A second Resgrid staff member has not approved yet; the department cannot act yet.</summary>
		public bool WaitsForStaff { get; set; }
		/// <summary>Both staff approved and the grant asks for the department's approval.</summary>
		public bool WaitsForDepartment { get; set; }
		/// <summary>Approved and scheduled for <see cref="ImportManagedActivationRequest.NotBefore"/>; an administrator may start it sooner.</summary>
		public bool InNoticePeriod { get; set; }
	}

	public sealed class ImportInvitationAcceptance
	{
		public bool Succeeded { get; set; }
		/// <summary>invite_missing, invite_completed, invite_wrong_account, department_full, password_rejected…</summary>
		public string ErrorCode { get; set; }
		public List<string> PasswordErrors { get; set; } = new List<string>();
		public int DepartmentId { get; set; }
		public string UserId { get; set; }
	}

	public sealed class ImportActivationChecklist
	{
		public ImportWorkspace Workspace { get; set; }
		public int ReconciliationChecks { get; set; }
		public int UnexplainedChecks { get; set; }
		public int OpenRollbackItems { get; set; }
		public int PeopleCreated { get; set; }
		public int PeopleDormant { get; set; }
		public int PeopleInvited { get; set; }
		public int PeopleActivated { get; set; }
		public int ExistingAccountsToInvite { get; set; }
		public int InvitationsPending { get; set; }
		public int InvitationsSent { get; set; }
		public int InvitationsFailed { get; set; }
		public int InvitationsAccepted { get; set; }
		/// <summary>Imported people flagged "will become administrator" who are not administrators yet.</summary>
		public List<ImportActivationPerson> PendingAdministrators { get; set; } = new List<ImportActivationPerson>();
		/// <summary>Dormant people not invited yet (id and display name only).</summary>
		public List<ImportActivationPerson> Dormant { get; set; } = new List<ImportActivationPerson>();
		public List<ImportActivationItem> Items { get; set; } = new List<ImportActivationItem>();
		public bool SignedOff { get; set; }
		public bool CanSignOff { get; set; }
	}

	public sealed class ImportActivationPerson
	{
		public string UserId { get; set; }
		public string Name { get; set; }
		public int? ActivationState { get; set; }
	}

	public sealed class ImportActivationItem
	{
		/// <summary>Localization key suffix ("reconcile", "practice", "invitations", "administrators", "sign-off").</summary>
		public string Key { get; set; }
		public bool Done { get; set; }
		public bool Required { get; set; }
		/// <summary>The owning screen, for items completed elsewhere (Communication Test, Admin Assist).</summary>
		public string Url { get; set; }
	}
	/// <summary>
	/// The Resgrid starter workbook and "download current data" (wizard plan section 6.3 step 4): one sheet per entity
	/// with "{label} [field_key]" headers the template detector recognizes, an example row, and the department's own
	/// names as drop-down lists. Text comes from <paramref name="localize"/> (the administrator's language).
	/// </summary>
	public interface IImportTemplateService
	{
		/// <summary>Entities whose current records can be downloaded (no personal or protected data).</summary>
		IReadOnlyList<string> ExportableEntities { get; }

		Task<ImportTemplateFile> BuildStarterWorkbookAsync(int departmentId, IReadOnlyCollection<string> entityKeys, Func<string, string> localize,
			CancellationToken cancellationToken = default);

		Task<ImportTemplateFile> BuildCsvTemplateAsync(int departmentId, string entityKey, Func<string, string> localize, CancellationToken cancellationToken = default);

		Task<ImportTemplateFile> ExportCurrentDataAsync(int departmentId, IReadOnlyCollection<string> entityKeys, Func<string, string> localize,
			CancellationToken cancellationToken = default);
	}

	public sealed class ImportTemplateFile
	{
		public string FileName { get; set; }
		public string ContentType { get; set; }
		public byte[] Content { get; set; }
	}
	/// <summary>
	/// Resgrid staff over Import &amp; Migration (wizard plan section 17), called in-process by BackOffice with a
	/// <see cref="StaffActor"/>. Every method re-checks the staff role and, where data or writes are involved, the
	/// workspace's active grant, its level, allowed actions and expiry. Projections never carry ADP-protected values;
	/// file names and column headers appear only from Observe up. Approvals stay two-person: an operator who requests a
	/// staff-run import or rollback can never be its checker.
	/// </summary>
	public interface IDepartmentImportStaffService
	{
		/// <summary>Every workspace across departments (or one department), newest activity first, with its queue bucket.</summary>
		Task<List<ImportStaffQueueRow>> GetQueueAsync(StaffActor staff, int? departmentId, int skip, int take, CancellationToken cancellationToken = default);

		Task<ImportStaffWorkspaceView> GetWorkspaceAsync(StaffActor staff, int departmentId, string workspaceId, CancellationToken cancellationToken = default);

		/// <summary>Counts for the BackOffice home dashboard tile (section 17.3): metadata only, across departments.</summary>
		Task<ImportStaffDashboard> GetDashboardAsync(StaffActor staff, CancellationToken cancellationToken = default);

		/// <summary>The department a workspace belongs to (BackOffice and Freshdesk deep links carry only the workspace id); null when unknown.</summary>
		Task<int?> FindWorkspaceDepartmentAsync(StaffActor staff, string workspaceId, CancellationToken cancellationToken = default);

		Task<List<ImportSupportRequest>> GetRequestQueueAsync(StaffActor staff, bool openOnly, int skip, int take, CancellationToken cancellationToken = default);

		Task<ImportSupportRequest> AssignRequestAsync(StaffActor staff, int departmentId, string requestId, string assignee, CancellationToken cancellationToken = default);

		Task<ImportSupportRequest> SetRequestStatusAsync(StaffActor staff, int departmentId, string requestId, ImportSupportRequestStatus status,
			CancellationToken cancellationToken = default);

		Task<ImportMessage> PostMessageAsync(StaffActor staff, int departmentId, string workspaceId, string body, string requestId, CancellationToken cancellationToken = default);

		// ---- recovery actions (section 17.4) ----
		Task<ImportRun> RetryRunNowAsync(StaffActor staff, int departmentId, string runId, CancellationToken cancellationToken = default);
		Task<ImportRun> ReleaseExpiredLeaseAsync(StaffActor staff, int departmentId, string runId, CancellationToken cancellationToken = default);
		Task<ImportRun> PauseRunAsync(StaffActor staff, int departmentId, string runId, CancellationToken cancellationToken = default);
		Task<ImportRun> RequeueAnalysisAsync(StaffActor staff, int departmentId, string workspaceId, IReadOnlyCollection<string> datasetIds, CancellationToken cancellationToken = default);
		Task<ImportWorkspace> EscalateAsync(StaffActor staff, int departmentId, string workspaceId, string note, CancellationToken cancellationToken = default);
		Task<int> ExtendRetentionAsync(StaffActor staff, int departmentId, string workspaceId, int days, CancellationToken cancellationToken = default);
		Task<int> PurgeArtifactsAsync(StaffActor staff, int departmentId, string workspaceId, CancellationToken cancellationToken = default);

		/// <summary>A suggestion the administrator accepts or rejects (Observe for maps, Inspect for issue resolutions).</summary>
		Task<ImportSuggestion> SuggestAsync(StaffActor staff, int departmentId, string workspaceId, ImportSuggestionKind kind, string datasetId, string payloadJson,
			string targetRef, string note, CancellationToken cancellationToken = default);

		/// <summary>
		/// Inspect or above (Support role): up to 20 sampled rows of one issue group, to diagnose it (section 17.2). Personal
		/// values are partially masked; restricted and protected (ADP) values are never returned. Every view goes on the
		/// department's timeline with its reason, and the department is notified.
		/// </summary>
		Task<ImportStaffSampleView> GetIssueSamplesAsync(StaffActor staff, int departmentId, string workspaceId, string datasetId, string code, string reason,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Prepare or above (Support role): the decrypted file as it was uploaded, under a watermarked file name (section 17.4).
		/// Refused while the department has Advanced Data Protection (the raw file can hold protected values), for a file that
		/// is not a completed, clean upload, and after the file was purged. Every download goes on the department's timeline
		/// with its reason, and the department is notified. The caller disposes the stream.
		/// </summary>
		Task<ImportStaffArtifactDownload> OpenArtifactAsync(StaffActor staff, int departmentId, string workspaceId, string artifactId, string reason,
			CancellationToken cancellationToken = default);

		// ---- staff uploads (Prepare and Operate, section 17.6): labeled as staff-authored, analysed like any other upload ----
		Task<ImportUploadStatus> StartUploadAsync(StaffActor staff, int departmentId, string workspaceId, string fileName, long sizeBytes, string sha256, string contentType,
			CancellationToken cancellationToken = default);

		Task<ImportUploadStatus> AppendUploadAsync(StaffActor staff, int departmentId, string workspaceId, string artifactId, int partNumber, byte[] chunk, string chunkSha256,
			CancellationToken cancellationToken = default);

		Task<ImportUploadStatus> CompleteUploadAsync(StaffActor staff, int departmentId, string workspaceId, string artifactId, CancellationToken cancellationToken = default);

		// ---- workbench (section 17.8): the administrator's matching and fixing steps, done on the department's behalf ----
		// Matching (destination, columns, values) needs Prepare or Operate and is recorded as Resgrid staff; fixing issues and
		// reading or editing staged rows needs Operate and the Migration role. ADP-protected values are never returned.

		/// <summary>A data set's mapping. Source examples are cleared for protected fields and masked for personal, restricted or sensitive-looking columns below Operate.</summary>
		Task<ImportMappingView> GetMappingAsync(StaffActor staff, int departmentId, string workspaceId, string datasetId, CancellationToken cancellationToken = default);

		Task<ImportDataset> SetDestinationAsync(StaffActor staff, int departmentId, string workspaceId, string datasetId, string entityKey, int expectedRevision,
			CancellationToken cancellationToken = default);

		Task<ImportDataset> ExcludeDatasetAsync(StaffActor staff, int departmentId, string workspaceId, string datasetId, bool excluded,
			CancellationToken cancellationToken = default);

		/// <summary>The workspace's write mode on the department's behalf (Prepare); the preview is planned again.</summary>
		Task<ImportWorkspace> SetWriteModeAsync(StaffActor staff, int departmentId, string workspaceId, ImportWriteMode mode, CancellationToken cancellationToken = default);

		/// <summary>Earlier workspaces of the department this one may continue (Prepare; names only).</summary>
		Task<List<ImportContinuationCandidate>> GetContinuationCandidatesAsync(StaffActor staff, int departmentId, string workspaceId,
			CancellationToken cancellationToken = default);

		/// <summary>Declares or stops a continuation on the department's behalf (Prepare); see IDepartmentImportService.SetContinuationAsync.</summary>
		Task<ImportSource> SetContinuationAsync(StaffActor staff, int departmentId, string workspaceId, string earlierWorkspaceId,
			CancellationToken cancellationToken = default);

		/// <summary>Records a product the department is moving from on its behalf (Prepare); see IDepartmentImportService.AddSourceAsync.</summary>
		Task<ImportStaffProductRow> AddProductAsync(StaffActor staff, int departmentId, string workspaceId, string productKey, CancellationToken cancellationToken = default);

		/// <summary>Removes a chosen product on the department's behalf (Prepare); see IDepartmentImportService.RemoveSourceAsync.</summary>
		Task RemoveProductAsync(StaffActor staff, int departmentId, string workspaceId, string sourceId, CancellationToken cancellationToken = default);

		Task<ImportColumnMap> SaveColumnMapAsync(StaffActor staff, int departmentId, string workspaceId, string datasetId, ImportColumnMapBody body,
			int expectedRevision, CancellationToken cancellationToken = default);

		/// <summary>The department's saved maps for the data set's destination.</summary>
		Task<List<ImportColumnMap>> GetSavedMapsAsync(StaffActor staff, int departmentId, string workspaceId, string datasetId, CancellationToken cancellationToken = default);

		Task<ImportColumnMap> ApplySavedMapAsync(StaffActor staff, int departmentId, string workspaceId, string datasetId, string mapId,
			CancellationToken cancellationToken = default);

		/// <summary>Distinct source values of a choice or reference field; refused for protected or restricted fields.</summary>
		Task<List<ImportValueCandidate>> GetValueCandidatesAsync(StaffActor staff, int departmentId, string workspaceId, string datasetId, string fieldKey,
			CancellationToken cancellationToken = default);

		Task<int> SaveValueMapsAsync(StaffActor staff, int departmentId, string workspaceId, string datasetId, string fieldKey,
			IReadOnlyList<ImportValueDecision> decisions, CancellationToken cancellationToken = default);

		/// <summary>Bulk tool: copies one data set's value decisions for a field to other data sets of the workspace with the same destination.</summary>
		Task<int> ApplyValueMapsAsync(StaffActor staff, int departmentId, string workspaceId, string sourceDatasetId, string fieldKey,
			IReadOnlyCollection<string> targetDatasetIds, CancellationToken cancellationToken = default);

		/// <summary>Operate: issue groups with their issue ids, for fixing.</summary>
		Task<List<ImportIssueGroup>> GetIssueGroupsAsync(StaffActor staff, int departmentId, string workspaceId, string datasetId, bool openOnly,
			CancellationToken cancellationToken = default);

		/// <summary>Operate: the issues of one group (row numbers and staged row ids; no values).</summary>
		Task<List<ImportIssue>> GetIssuesAsync(StaffActor staff, int departmentId, string workspaceId, string datasetId, string code, int skip, int take,
			CancellationToken cancellationToken = default);

		/// <summary>Operate: accept warnings, exclude rows, reopen, or set one value on every row behind the issues.</summary>
		Task<int> ResolveIssuesAsync(StaffActor staff, int departmentId, string workspaceId, ImportStaffIssueAction action, IReadOnlyCollection<string> issueIds,
			string value, CancellationToken cancellationToken = default);

		/// <summary>
		/// Operate: one staged row's cells for an inline fix. Cells of protected fields are withheld. Every reveal goes on the
		/// department's timeline with the reason.
		/// </summary>
		Task<ImportStaffRowView> RevealRowAsync(StaffActor staff, int departmentId, string workspaceId, string stagedRowId, string reason,
			CancellationToken cancellationToken = default);

		/// <summary>Operate: replaces one cell of a staged row (never a protected field's cell) and re-validates.</summary>
		Task EditValueAsync(StaffActor staff, int departmentId, string workspaceId, string stagedRowId, string column, string value,
			CancellationToken cancellationToken = default);

		// ---- workbench: configuration (section 7.5, Configure step) ----
		/// <summary>
		/// Observe or more: the workspace's Configure step as the department sees it (values, suggestions, decisions, issues).
		/// These are the department's own settings, not imported records; permissions are shown but never previewed for staff.
		/// </summary>
		Task<ImportConfigurationView> GetConfigurationAsync(StaffActor staff, int departmentId, string workspaceId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Prepare or more and a Support, Migration or SuperAdmin role: accepts, enters or declines a value as a draft on the
		/// department's behalf (recorded as Resgrid staff; the department approves the wave that writes it). Never a
		/// permission, and never over a value the department decided itself.
		/// </summary>
		Task<ImportConfigurationChange> DecideConfigurationAsync(StaffActor staff, int departmentId, string workspaceId, ImportConfigurationDecision decision,
			CancellationToken cancellationToken = default);

		/// <summary>Prepare or more: accepts every waiting suggestion of the group (or all), except permissions.</summary>
		Task<int> AcceptConfigurationSuggestionsAsync(StaffActor staff, int departmentId, string workspaceId, string group, CancellationToken cancellationToken = default);

		/// <summary>Prepare or more: forgets a decision staff made (never one the department made).</summary>
		Task<bool> ResetConfigurationAsync(StaffActor staff, int departmentId, string workspaceId, string itemKey, CancellationToken cancellationToken = default);

		/// <summary>Prepare or more: uploads a logo (validated as the department's own upload is) and accepts it as the proposed logo.</summary>
		Task<ImportConfigurationChange> UploadConfigurationLogoAsync(StaffActor staff, int departmentId, string workspaceId, string fileName, string contentType,
			byte[] data, CancellationToken cancellationToken = default);

		// ---- managed activation and invitations (section 17.8) ----
		/// <summary>Operate and the Migration role: the activation checklist and the staff activation or invitation runs not yet carried out.</summary>
		Task<ImportManagedActivationSnapshot> GetManagedActivationAsync(StaffActor staff, int departmentId, string workspaceId,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// The operator asks to send invitations or to carry out activation steps (confirm administrators, sign off). The run waits
		/// for a different staff checker and, when the grant asks for it, an administrator. Under a contract authorization every
		/// administrator is told now and nothing happens for 24 hours unless an administrator approves sooner.
		/// </summary>
		Task<ImportRun> RequestManagedActivationAsync(StaffActor staff, int departmentId, string workspaceId, ImportManagedActivationRequest request,
			CancellationToken cancellationToken = default);

		// ---- managed migration (section 17.8) ----
		/// <summary>
		/// A migration operator opens a managed-migration workspace for a department with Onboarding.ManagedMigration on. It is
		/// owned by the department's managing user; staff act in it only once an Operate grant (department or contract) is active.
		/// </summary>
		Task<ImportWorkspace> CreateManagedWorkspaceAsync(StaffActor staff, int departmentId, ImportWorkspaceMode mode, string name, string engagementReference,
			CancellationToken cancellationToken = default);

		/// <summary>The same preview the administrator approves from, for the operator and the checker (Operate grant).</summary>
		Task<ImportPreview> GetManagedPreviewAsync(StaffActor staff, int departmentId, string workspaceId, CancellationToken cancellationToken = default);

		/// <summary>A SuperAdmin requests a contract authorization (Operate) for a department; a different SuperAdmin must approve it.</summary>
		Task<ImportSupportGrant> RequestContractAuthorizationAsync(StaffActor staff, int departmentId, string workspaceId, string engagementReference, string reason, int days,
			ImportOperateActions allowedActions, CancellationToken cancellationToken = default);

		Task<ImportSupportGrant> ApproveContractAuthorizationAsync(StaffActor staff, int departmentId, string grantId, bool approve, CancellationToken cancellationToken = default);

		Task<List<ImportSupportGrant>> GetPendingAuthorizationsAsync(StaffActor staff, int take, CancellationToken cancellationToken = default);

		/// <summary>Staff-run runs waiting for a checker, across departments.</summary>
		Task<List<ImportRun>> GetAwaitingCheckerAsync(StaffActor staff, int take, CancellationToken cancellationToken = default);

		Task<ImportRun> RequestManagedCommitAsync(StaffActor staff, int departmentId, string workspaceId, ImportApprovalRequest request, CancellationToken cancellationToken = default);

		Task<ImportRun> RequestManagedRollbackAsync(StaffActor staff, int departmentId, string commitRunId, CancellationToken cancellationToken = default);

		/// <summary>The checker (Migration or SuperAdmin, never the operator) approves a staff-run commit or rollback.</summary>
		Task<ImportRun> ApproveManagedRunAsync(StaffActor staff, int departmentId, string runId, string reason, CancellationToken cancellationToken = default);

		/// <summary>
		/// A checker rejects, or the operator withdraws, a staff run still waiting for its checker (or an approved activation or
		/// invitation run still in its notice period); the run is cancelled.
		/// </summary>
		Task<ImportRun> RejectManagedRunAsync(StaffActor staff, int departmentId, string runId, string reason, CancellationToken cancellationToken = default);
	}

	/// <summary>A staff download of an uploaded file (section 17.4): the plaintext stream and the watermarked name to save it under.</summary>
	public sealed class ImportStaffArtifactDownload : IDisposable
	{
		public Stream Content { get; set; }
		public string FileName { get; set; }
		public long Length { get; set; }

		public void Dispose() => Content?.Dispose();
	}

	/// <summary>Sampled rows of one issue group as staff at Inspect may see them (section 17.2).</summary>
	public sealed class ImportStaffSampleView
	{
		public string DatasetId { get; set; }
		public string Code { get; set; }
		/// <summary>Issues with this code in the data set (the sample shows at most 20 of their rows).</summary>
		public int IssueCount { get; set; }
		public List<ImportStaffSampleRow> Rows { get; set; } = new List<ImportStaffSampleRow>();
	}

	public sealed class ImportStaffSampleRow
	{
		public int RowNumber { get; set; }
		/// <summary>The column the issue is about, when it names one.</summary>
		public string IssueColumn { get; set; }
		public List<ImportStaffCell> Cells { get; set; } = new List<ImportStaffCell>();
	}

	public sealed class ImportManagedActivationSnapshot
	{
		public ImportActivationChecklist Checklist { get; set; }
		public List<ImportManagedActivationView> Runs { get; set; } = new List<ImportManagedActivationView>();
		/// <summary>The active grant is a contract authorization: requests wait for the 24-hour notice.</summary>
		public bool NoticeRequired { get; set; }
		/// <summary>The active grant asks for the department's approval of every staff run.</summary>
		public bool DepartmentSignOffRequired { get; set; }
		public ImportOperateActions AllowedActions { get; set; }
	}

	/// <summary>The import tile on the BackOffice home dashboard (section 17.3).</summary>
	public sealed class ImportStaffDashboard
	{
		public int OpenSupportRequests { get; set; }
		public DateTime? OldestOpenRequestOn { get; set; }
		/// <summary>Running or reconciling runs whose cursor has not moved for ImportConfig.StalledRunMinutes.</summary>
		public int StalledRuns { get; set; }
		public int FailedRunsLast24Hours { get; set; }
		/// <summary>Staff-run commits, rollbacks and activation steps waiting for a second staff member.</summary>
		public int AwaitingChecker { get; set; }
		public int NeedsEngineering { get; set; }
	}

	public enum ImportStaffIssueAction
	{
		Accept = 1,
		ExcludeRows = 2,
		Reopen = 3,
		SetValue = 4
	}

	/// <summary>A staged row as staff may see it: a withheld cell has no value and <see cref="ImportStaffCell.Withheld"/> set.</summary>
	public sealed class ImportStaffRowView
	{
		public string StagedRowId { get; set; }
		public string DatasetId { get; set; }
		public int RowNumber { get; set; }
		public bool Excluded { get; set; }
		public int ValidationState { get; set; }
		public List<ImportStaffCell> Cells { get; set; } = new List<ImportStaffCell>();
	}

	public sealed class ImportStaffCell
	{
		public string Column { get; set; }
		/// <summary>The contract field the column is bound to, or null.</summary>
		public string FieldKey { get; set; }
		public string Value { get; set; }
		/// <summary>True for a protected (ADP) field, or a restricted one in a sample: the value is never sent to staff.</summary>
		public bool Withheld { get; set; }
		/// <summary>The value is partially masked (a personal field, or a column that looks sensitive).</summary>
		public bool Masked { get; set; }
	}

	public enum ImportStaffQueueBucket
	{
		SupportRequestsOpen = 1,
		Stalled = 2,
		FailedRetrying = 3,
		NeedsEngineering = 4,
		AwaitingChecker = 5,
		Running = 6,
		AwaitingAdministrator = 7,
		RecentlyCompleted = 8
	}

	public sealed class ImportStaffQueueRow
	{
		public int DepartmentId { get; set; }
		public string WorkspaceId { get; set; }
		public int Mode { get; set; }
		public int Status { get; set; }
		public int OperatingModel { get; set; }
		public ImportStaffQueueBucket Bucket { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime LastActivityOn { get; set; }
		public int GrantLevel { get; set; }
		public DateTime? GrantExpiresOn { get; set; }
		public int OpenRequests { get; set; }
		public int OpenBlockingIssues { get; set; }
		public int ReadyDatasets { get; set; }
		public int TotalDatasets { get; set; }
		public string CurrentRunId { get; set; }
		public int? CurrentRunStatus { get; set; }
		public DateTime? LastHeartbeatOn { get; set; }
		public int Attempts { get; set; }
		public string LastErrorCode { get; set; }
		public bool NeedsEngineering { get; set; }
	}

	/// <summary>A workspace as staff may see it at the active grant's level (section 17.2).</summary>
	public sealed class ImportStaffWorkspaceView
	{
		public ImportWorkspace Workspace { get; set; }
		public ImportSupportGrant ActiveGrant { get; set; }
		public int Level { get; set; }
		public List<ImportStaffDatasetRow> Datasets { get; set; } = new List<ImportStaffDatasetRow>();
		public List<ImportStaffIssueRow> Issues { get; set; } = new List<ImportStaffIssueRow>();
		public List<ImportRun> Runs { get; set; } = new List<ImportRun>();
		public List<ImportReconciliation> Reconciliations { get; set; } = new List<ImportReconciliation>();
		public List<ImportSupportRequest> Requests { get; set; } = new List<ImportSupportRequest>();
		public List<ImportMessage> Messages { get; set; } = new List<ImportMessage>();
		public List<ImportSuggestion> Suggestions { get; set; } = new List<ImportSuggestion>();
		public List<ImportSupportGrant> Grants { get; set; } = new List<ImportSupportGrant>();
		public List<ImportEvent> Timeline { get; set; } = new List<ImportEvent>();
		public List<ImportStaffArtifactRow> Artifacts { get; set; } = new List<ImportStaffArtifactRow>();
		/// <summary>The earlier import this workspace continues (its label is that workspace's name): Observe and up.</summary>
		public ImportSource Continuation { get; set; }
		/// <summary>The products the department says it is moving from (plan section 7.6): Observe and up. Never credentials.</summary>
		public List<ImportStaffProductRow> Products { get; set; } = new List<ImportStaffProductRow>();
	}

	/// <summary>A product a workspace is moving from, as staff see it: which product and when, nothing else of the source row.</summary>
	public sealed class ImportStaffProductRow
	{
		public string Id { get; set; }
		public string ProductKey { get; set; }
		public string Label { get; set; }
		public DateTime CreatedOn { get; set; }
	}

	public sealed class ImportStaffDatasetRow
	{
		public string Id { get; set; }
		/// <summary>Sheet name: Observe and up (it can carry personal data).</summary>
		public string Name { get; set; }
		public string EntityKey { get; set; }
		public string ContractVersion { get; set; }
		public int Status { get; set; }
		public int RowCount { get; set; }
		public int ReadyCount { get; set; }
		public int WarningCount { get; set; }
		public int BlockedCount { get; set; }
		public int CreatedCount { get; set; }
		public int FailedCount { get; set; }
		/// <summary>Source column headers and their matched fields: Observe and up.</summary>
		public List<(string Header, string FieldKey)> Columns { get; set; } = new List<(string, string)>();
	}

	public sealed class ImportStaffIssueRow
	{
		public string DatasetId { get; set; }
		public string Code { get; set; }
		public int Severity { get; set; }
		public int Count { get; set; }
		public int OpenCount { get; set; }
		/// <summary>Row numbers: Observe and up.</summary>
		public List<int> Rows { get; set; } = new List<int>();
	}

	public sealed class ImportStaffArtifactRow
	{
		public string Id { get; set; }
		/// <summary>Observe and up.</summary>
		public string FileName { get; set; }
		public long SizeBytes { get; set; }
		public int ScanState { get; set; }
		public int UploadState { get; set; }
		public string RejectionCode { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime? RetentionDeadline { get; set; }
		public DateTime? PurgedOn { get; set; }
	}
}
