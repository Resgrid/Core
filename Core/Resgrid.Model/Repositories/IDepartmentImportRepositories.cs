using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Onboarding;

namespace Resgrid.Model.Repositories
{
	// Department Import repositories (M0273–M0276, wizard plan section 8.6). Every department-scoped read takes the
	// department id and filters by it (section 14.2); the few cross-department reads are the maintenance and
	// BackOffice queue sweeps and say so in their names.

	/// <summary>Members shared by every Import* repository.</summary>
	public interface IImportRowRepository<T> : IRepository<T> where T : ImportRow
	{
		/// <summary>The row with this id in this department, or null (never trusts an id alone).</summary>
		Task<T> GetByIdAsync(int departmentId, string id, CancellationToken cancellationToken = default);

		/// <summary>Inserts many rows in a few multi-row statements, inside the ambient unit of work when there is one.</summary>
		Task InsertManyAsync(IReadOnlyList<T> rows, CancellationToken cancellationToken = default);
	}

	/// <summary>Rows owned by a workspace.</summary>
	public interface IImportWorkspaceChildRepository<T> : IImportRowRepository<T> where T : ImportRow
	{
		Task<List<T>> GetAllForWorkspaceAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default);

		Task<int> DeleteAllForWorkspaceAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default);
	}

	public interface IImportWorkspacesRepository : IImportRowRepository<ImportWorkspace>
	{
		Task<List<ImportWorkspace>> GetAllForDepartmentAsync(int departmentId, CancellationToken cancellationToken = default);

		/// <summary>Optimistic save: writes the row only when its stored Revision equals <paramref name="expectedRevision"/>; the row's Revision is incremented.</summary>
		Task<bool> UpdateWithRevisionAsync(ImportWorkspace workspace, int expectedRevision, CancellationToken cancellationToken = default);

		Task TouchAsync(int departmentId, string workspaceId, DateTime now, CancellationToken cancellationToken = default);

		/// <summary>Cross-department (maintenance): open workspaces idle since before <paramref name="idleBefore"/>.</summary>
		Task<List<ImportWorkspace>> GetIdleOpenWorkspacesAsync(DateTime idleBefore, int take, CancellationToken cancellationToken = default);

		/// <summary>Cross-department (BackOffice queue): workspaces updated since <paramref name="since"/> or not closed, newest activity first.</summary>
		Task<List<ImportWorkspace>> GetQueueAsync(DateTime since, int? departmentId, int skip, int take, CancellationToken cancellationToken = default);

		/// <summary>Cross-department (BackOffice deep links carry only the workspace id): the workspace with this id, or null.</summary>
		Task<ImportWorkspace> FindByIdAnyDepartmentAsync(string workspaceId, CancellationToken cancellationToken = default);

		/// <summary>Cross-department (maintenance): workspaces whose staging retention has passed.</summary>
		Task<List<ImportWorkspace>> GetPastStagingRetentionAsync(DateTime now, int take, CancellationToken cancellationToken = default);
	}

	public interface IImportSourcesRepository : IImportWorkspaceChildRepository<ImportSource>
	{
		/// <summary>Cross-department (maintenance): sources still holding credentials past their deadline.</summary>
		Task<List<ImportSource>> GetWithExpiredCredentialsAsync(DateTime now, int take, CancellationToken cancellationToken = default);

		Task<int> ClearCredentialsAsync(int departmentId, string sourceId, CancellationToken cancellationToken = default);
	}

	public interface IImportArtifactsRepository : IImportWorkspaceChildRepository<ImportArtifact>
	{
		/// <summary>
		/// Records one uploaded part. Conditional on the stored SizeBytes still equalling <paramref name="expectedSizeBytes"/>
		/// and the upload still in progress, so two racing chunk requests cannot both append.
		/// </summary>
		Task<bool> TryAppendPartAsync(int departmentId, string artifactId, long expectedSizeBytes, long newSizeBytes,
			string uploadedPartsJson, DateTime now, CancellationToken cancellationToken = default);

		/// <summary>Cross-department (maintenance): in-progress uploads with no progress since <paramref name="before"/>.</summary>
		Task<List<ImportArtifact>> GetStaleUploadsAsync(DateTime before, int take, CancellationToken cancellationToken = default);

		/// <summary>Cross-department (maintenance): completed artifacts still waiting for a scan.</summary>
		Task<List<ImportArtifact>> GetPendingScansAsync(int take, CancellationToken cancellationToken = default);

		/// <summary>Cross-department (maintenance): artifacts whose retention deadline has passed and are not yet purged.</summary>
		Task<List<ImportArtifact>> GetPastRetentionAsync(DateTime now, int take, CancellationToken cancellationToken = default);
	}

	public interface IImportDatasetsRepository : IImportWorkspaceChildRepository<ImportDataset>
	{
		Task<List<ImportDataset>> GetForArtifactAsync(int departmentId, string artifactId, CancellationToken cancellationToken = default);

		/// <summary>Optimistic save on the dataset Revision (a stale approval or a concurrent fix loses).</summary>
		Task<bool> UpdateWithRevisionAsync(ImportDataset dataset, int expectedRevision, CancellationToken cancellationToken = default);
	}

	public interface IImportColumnMapsRepository : IImportRowRepository<ImportColumnMap>
	{
		Task<List<ImportColumnMap>> GetSavedForDepartmentAsync(int departmentId, string entityKey, CancellationToken cancellationToken = default);

		Task<List<ImportColumnMap>> GetForWorkspaceAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default);

		Task<int> DeleteUnsavedForWorkspaceAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default);
	}

	public interface IImportValueMapsRepository : IImportWorkspaceChildRepository<ImportValueMap>
	{
		Task<List<ImportValueMap>> GetForDatasetAsync(int departmentId, string datasetId, CancellationToken cancellationToken = default);

		Task<int> DeleteForDatasetAsync(int departmentId, string datasetId, CancellationToken cancellationToken = default);
	}

	public interface IImportStagedRowsRepository : IImportWorkspaceChildRepository<ImportStagedRow>
	{
		/// <summary>Keyset page of a dataset's rows in row-number order, after <paramref name="afterRowNumber"/>.</summary>
		Task<List<ImportStagedRow>> GetPageAsync(int departmentId, string datasetId, int afterRowNumber, int take, CancellationToken cancellationToken = default);

		Task<List<ImportStagedRow>> GetByIdsAsync(int departmentId, IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default);

		Task<int> DeleteForDatasetAsync(int departmentId, string datasetId, CancellationToken cancellationToken = default);

		/// <summary>Writes the ladder/plan/commit fields of one row (payloads, state, decision, outcome, target).</summary>
		Task UpdateStateAsync(ImportStagedRow row, CancellationToken cancellationToken = default);

		/// <summary>Writes the L7 plan of one row: decision, matched target and its version at preview, and the row's state.</summary>
		Task<int> SetPlanAsync(int departmentId, string rowId, int decision, string targetKey, string targetVersion, int validationState, DateTime now,
			CancellationToken cancellationToken = default);

		/// <summary>Clears the commit outcome of a dataset's rows after a rollback, so a later commit writes them again.</summary>
		Task<int> ResetOutcomesAsync(int departmentId, string datasetId, DateTime now, CancellationToken cancellationToken = default);

		/// <summary>Writes the commit outcome of one row (inside the chunk transaction when there is one).</summary>
		Task<int> SetOutcomeAsync(int departmentId, string rowId, int outcome, string targetKey, DateTime now, CancellationToken cancellationToken = default);

		/// <summary>Content hashes, decisions and outcomes of a dataset's rows in row order (approval digest, counts, deferred work).</summary>
		Task<List<ImportStagedRow>> GetDigestInputsAsync(int departmentId, string datasetId, CancellationToken cancellationToken = default);
	}

	public interface IImportIssuesRepository : IImportWorkspaceChildRepository<ImportIssue>
	{
		Task<List<ImportIssue>> GetForDatasetAsync(int departmentId, string datasetId, CancellationToken cancellationToken = default);

		/// <summary>Filtered keyset page for the Issues tab (newest-first by row number within code).</summary>
		Task<List<ImportIssue>> GetPageAsync(int departmentId, string workspaceId, string datasetId, int? severity, int? status, string code,
			int skip, int take, CancellationToken cancellationToken = default);

		Task<int> CountOpenBlockingAsync(int departmentId, string workspaceId, string datasetId, CancellationToken cancellationToken = default);

		/// <summary>Removes the open system issues of a dataset at or above a ladder level (an incremental re-run replaces them).</summary>
		Task<int> DeleteOpenSystemIssuesAsync(int departmentId, string datasetId, int fromLevel, CancellationToken cancellationToken = default);

		Task<int> DeleteForDatasetAsync(int departmentId, string datasetId, CancellationToken cancellationToken = default);
	}

	public interface IImportExternalIdsRepository : IImportRowRepository<ImportExternalId>
	{
		Task<ImportExternalId> GetByIdentityAsync(int departmentId, string sourceSystemKey, string entityType, string sourceId, CancellationToken cancellationToken = default);

		Task<List<ImportExternalId>> GetForSourceIdsAsync(int departmentId, string sourceSystemKey, string entityType, IReadOnlyCollection<string> sourceIds,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Inserts a Claimed entry; returns false (and inserts nothing) when the identity is already claimed. The unique
		/// index serializes concurrent claimers (section 11.3).
		/// </summary>
		Task<bool> TryClaimAsync(ImportExternalId claim, CancellationToken cancellationToken = default);

		/// <summary>Moves a claim to Written/Linked/Released with its target key and run.</summary>
		Task<int> SetStateAsync(int departmentId, string id, ImportExternalIdState state, string targetKey, string runId, DateTime now,
			CancellationToken cancellationToken = default);

		Task<List<ImportExternalId>> GetClaimedForRunAsync(int departmentId, string runId, CancellationToken cancellationToken = default);

		Task<List<ImportExternalId>> GetForRunAsync(int departmentId, string runId, CancellationToken cancellationToken = default);

		Task<int> DeleteForWorkspaceAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default);
	}

	public interface IImportRunsRepository : IImportWorkspaceChildRepository<ImportRun>
	{
		/// <summary>
		/// Cross-department (worker 47): leases the next runnable run — Queued, a retryable Failed whose next attempt is due,
		/// or a Running/Reconciling run whose lease expired — oldest service first so departments are served round-robin.
		/// Uses READPAST / SKIP LOCKED so two workers never lease the same run.
		/// </summary>
		Task<ImportRun> ClaimNextAsync(string leaseOwner, DateTime leaseUntil, DateTime now, int maxAttempts, CancellationToken cancellationToken = default);

		/// <summary>Leases one specific run (the RabbitMQ nudge path); null when it is not runnable or another worker holds it.</summary>
		Task<ImportRun> ClaimByIdAsync(string runId, string leaseOwner, DateTime leaseUntil, DateTime now, int maxAttempts, CancellationToken cancellationToken = default);

		/// <summary>Extends the lease; false when the lease was lost.</summary>
		Task<bool> HeartbeatAsync(string runId, string leaseOwner, DateTime leaseUntil, DateTime now, CancellationToken cancellationToken = default);

		/// <summary>Persists a leased run's progress; false (nothing written) when this owner no longer holds the lease.</summary>
		Task<bool> SaveLeasedAsync(ImportRun run, string leaseOwner, CancellationToken cancellationToken = default);

		Task<int> RequestCancelAsync(int departmentId, string runId, ImportRunCancelRequest request, DateTime now, CancellationToken cancellationToken = default);

		/// <summary>Runs of the department that are queued, running, paused, reconciling or awaiting a checker.</summary>
		Task<List<ImportRun>> GetOpenForDepartmentAsync(int departmentId, CancellationToken cancellationToken = default);

		/// <summary>Cross-department (watchdog): running runs whose cursor has not moved since <paramref name="cursorBefore"/>.</summary>
		Task<List<ImportRun>> GetStalledAsync(DateTime cursorBefore, int take, CancellationToken cancellationToken = default);

		/// <summary>Cross-department (BackOffice): the most recent runs, open first.</summary>
		Task<List<ImportRun>> GetRecentAsync(DateTime since, int take, CancellationToken cancellationToken = default);
	}

	public interface IImportChangeJournalRepository : IImportRowRepository<ImportChangeJournalEntry>
	{
		/// <summary>A run's journal in reverse sequence order (rollback replays it backwards).</summary>
		Task<List<ImportChangeJournalEntry>> GetForRunDescendingAsync(int departmentId, string runId, CancellationToken cancellationToken = default);

		Task<long> GetMaxSequenceAsync(int departmentId, string runId, CancellationToken cancellationToken = default);

		Task<int> MarkRolledBackAsync(int departmentId, string id, ImportRollbackOutcome outcome, string rollbackRunId, DateTime now,
			CancellationToken cancellationToken = default);

		Task<int> DeleteForWorkspaceAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default);
	}

	public interface IImportReconciliationsRepository : IImportRowRepository<ImportReconciliation>
	{
		Task<List<ImportReconciliation>> GetForRunAsync(int departmentId, string runId, CancellationToken cancellationToken = default);

		Task<int> DeleteForRunAsync(int departmentId, string runId, CancellationToken cancellationToken = default);
	}

	public interface IImportSupportGrantsRepository : IImportRowRepository<ImportSupportGrant>
	{
		Task<List<ImportSupportGrant>> GetForWorkspaceAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default);

		/// <summary>Cross-department (maintenance): active grants past their expiry.</summary>
		Task<List<ImportSupportGrant>> GetExpiredActiveAsync(DateTime now, int take, CancellationToken cancellationToken = default);

		/// <summary>Cross-department (BackOffice approvals): contract authorizations waiting for a second SuperAdmin.</summary>
		Task<List<ImportSupportGrant>> GetPendingApprovalAsync(int take, CancellationToken cancellationToken = default);
	}

	public interface IImportSupportRequestsRepository : IImportRowRepository<ImportSupportRequest>
	{
		Task<List<ImportSupportRequest>> GetForWorkspaceAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default);

		/// <summary>Cross-department (BackOffice request queue).</summary>
		Task<List<ImportSupportRequest>> GetQueueAsync(bool openOnly, int skip, int take, CancellationToken cancellationToken = default);

		/// <summary>Cross-department (worker 48 outbox): requests whose helpdesk ticket is pending and due.</summary>
		Task<List<ImportSupportRequest>> GetHelpdeskDueAsync(DateTime now, int take, CancellationToken cancellationToken = default);

		Task<bool> NumberExistsAsync(string number, CancellationToken cancellationToken = default);
	}

	public interface IImportMessagesRepository : IImportWorkspaceChildRepository<ImportMessage>
	{
		Task<List<ImportMessage>> GetPageAsync(int departmentId, string workspaceId, int skip, int take, CancellationToken cancellationToken = default);

		Task<int> CountUnreadByDepartmentAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default);

		Task<int> MarkReadByDepartmentAsync(int departmentId, string workspaceId, DateTime now, CancellationToken cancellationToken = default);

		Task<int> MarkReadByStaffAsync(int departmentId, string workspaceId, DateTime now, CancellationToken cancellationToken = default);

		/// <summary>Cross-department (worker 48 outbox): staff replies waiting to be mirrored to their request's helpdesk ticket.</summary>
		Task<List<ImportMessage>> GetHelpdeskDueAsync(DateTime now, int take, CancellationToken cancellationToken = default);
	}

	public interface IImportSuggestionsRepository : IImportWorkspaceChildRepository<ImportSuggestion>
	{
		Task<List<ImportSuggestion>> GetProposedAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default);
	}

	/// <summary>Decided configuration values of a workspace (M0281, wizard plan section 7.5): one row per item.</summary>
	public interface IImportConfigurationChangesRepository : IImportWorkspaceChildRepository<ImportConfigurationChange>
	{
		/// <summary>Optimistic save: writes the row only when its stored Revision equals <paramref name="expectedRevision"/>; the row's Revision is incremented.</summary>
		Task<bool> UpdateWithRevisionAsync(ImportConfigurationChange change, int expectedRevision, CancellationToken cancellationToken = default);

		/// <summary>The rows a commit run was approved with (ApprovedRunId).</summary>
		Task<List<ImportConfigurationChange>> GetForRunAsync(int departmentId, string runId, CancellationToken cancellationToken = default);
	}

	public interface IImportEventsRepository : IImportWorkspaceChildRepository<ImportEvent>
	{
		Task<List<ImportEvent>> GetPageAsync(int departmentId, string workspaceId, int skip, int take, CancellationToken cancellationToken = default);

		/// <summary>Cross-department (worker 48 digest): events with this action since <paramref name="since"/>, oldest first.</summary>
		Task<List<ImportEvent>> GetByActionSinceAsync(string action, DateTime since, int take, CancellationToken cancellationToken = default);
	}
	/// <summary>Import invitations on the Invites table (M0278): pending sends, per-workspace lists and department-scoped lookups.</summary>
	public interface IImportInvitesRepository : IRepository<Invite>
	{
		/// <summary>Cross-department (batched sender): pending import invitations due now, oldest first.</summary>
		Task<List<Invite>> GetDueAsync(DateTime now, int maxAttempts, int take, CancellationToken cancellationToken = default);

		Task<List<Invite>> GetForWorkspaceAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default);

		/// <summary>The invitation with this code (the code is a random GUID: the acceptance link carries nothing else).</summary>
		Task<Invite> GetByCodeAsync(Guid code, CancellationToken cancellationToken = default);

		/// <summary>Looked up by department and code, never by code or email alone (section 15.4).</summary>
		Task<Invite> GetByDepartmentAndCodeAsync(int departmentId, Guid code, CancellationToken cancellationToken = default);

		/// <summary>Records one send attempt's result.</summary>
		Task<int> SetSendStateAsync(int inviteId, InviteSendStates state, int attempts, DateTime? sentOn, DateTime? nextAttempt, CancellationToken cancellationToken = default);
	}
}
