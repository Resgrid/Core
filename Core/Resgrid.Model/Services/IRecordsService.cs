using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Repositories;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// The Records (RMS) operational aggregate: drafts, lifecycle transitions, immutable revisions,
	/// attachments, group scope and the search projection (RMS plan sections 4.1, 4.8, 5.3, 5.7). Every
	/// method is department-scoped; actor identity comes from the caller's authenticated state. Legal
	/// transitions (finalize, amend, void, submit) are online-only and never last-writer-wins.
	/// </summary>
	public interface IRecordsService
	{
		Task<RecordAggregate> CreateDraftAsync(int departmentId, string userId, RecordDraftInput input, CancellationToken cancellationToken = default);

		/// <summary>ETag-guarded draft save; throws <see cref="RecordConcurrencyException"/> on a stale RowVersion.</summary>
		Task<RecordAggregate> SaveDraftAsync(int departmentId, string userId, string recordId, long expectedRowVersion, RecordDraftInput input, CancellationToken cancellationToken = default);

		Task<RecordAggregate> SubmitForReviewAsync(int departmentId, string userId, string recordId, long expectedRowVersion, CancellationToken cancellationToken = default);

		Task<RecordAggregate> ReturnForCorrectionAsync(int departmentId, string userId, string recordId, string reasonCode, string reasonText, CancellationToken cancellationToken = default);

		Task<RecordAggregate> ApproveAsync(int departmentId, string userId, string recordId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Writes the immutable revision (finalize, or amend when an amendment draft is open), assigns the
		/// record number under the default OnFinalize policy, recomputes group scope, refreshes the search
		/// projection and enqueues the lifecycle event(s) in one transaction.
		/// </summary>
		Task<RecordAggregate> FinalizeAsync(int departmentId, string userId, string recordId, long expectedRowVersion, string attestationStatementVersion, string reasonCode, string reasonText, CancellationToken cancellationToken = default);

		/// <summary>Opens an amendment draft seeded from the current revision (at most one per Record).</summary>
		Task<RecordAggregate> OpenAmendmentAsync(int departmentId, string userId, string recordId, CancellationToken cancellationToken = default);

		/// <summary>Discards an open amendment draft; changes nothing and consumes no number.</summary>
		Task<RecordAggregate> AbandonAmendmentAsync(int departmentId, string userId, string recordId, CancellationToken cancellationToken = default);

		Task<RecordAggregate> VoidAsync(int departmentId, string userId, string recordId, string reasonCode, string reasonText, CancellationToken cancellationToken = default);

		Task<RecordAggregate> CancelAsync(int departmentId, string userId, string recordId, CancellationToken cancellationToken = default);

		Task<RecordAggregate> ReassignDraftAsync(int departmentId, string userId, string recordId, string newOwnerUserId, string reason, CancellationToken cancellationToken = default);

		Task<RecordAggregate> GetAsync(int departmentId, string recordId, bool includeRevisions = false);

		/// <summary>Records of the same definition already linked to the Call (duplicate warning, RMS plan section 4.7).</summary>
		Task<List<RmsOperationalRecord>> GetDuplicateCandidatesAsync(int departmentId, string definitionKey, int callId);

		/// <summary>Unfinalized Records a member still owns (plan 4.7 draft ownership transfer): surfaced before deactivation.</summary>
		Task<List<RmsOperationalRecord>> GetOutstandingForOwnerAsync(int departmentId, string userId);

		Task<List<RmsRecordSearchProjection>> QueryAsync(int departmentId, RmsRecordQuery query);

		Task<int> CountAsync(int departmentId, RmsRecordQuery query);
		/// <summary>Live projections by record id, in no particular order; the search path re-checks each before showing it.</summary>
		Task<List<RmsRecordSearchProjection>> GetProjectionsByIdsAsync(int departmentId, IEnumerable<string> recordIds);

		Task<List<int>> GetYearsAsync(int departmentId);

		/// <summary>Delta cursor for clients (plan section 5.3): projections modified after <paramref name="since"/>, oldest first, including tombstones (cancelled, voided, deleted).</summary>
		Task<List<RmsRecordSearchProjection>> GetChangesSinceAsync(int departmentId, DateTime? since, int take, string sinceId = null);

		Task<RmsRecordAttachment> AddAttachmentAsync(int departmentId, string userId, string recordId, string fileName, string contentType, byte[] data, string description, CancellationToken cancellationToken = default);

		/// <summary>Loads bytes; the caller authorizes per Record before serving.</summary>
		Task<RmsRecordAttachment> GetAttachmentAsync(int departmentId, string attachmentId);

		Task<bool> RemoveAttachmentAsync(int departmentId, string userId, string recordId, string attachmentId, CancellationToken cancellationToken = default);

		Task<List<RmsRevision>> GetRevisionsAsync(int departmentId, string recordId);

		/// <summary>The Record exactly as it stood at a revision, rendered from that revision's snapshot.</summary>
		Task<RecordSnapshot> GetRevisionSnapshotAsync(int departmentId, string revisionId);

		/// <summary>Field-level diff computed on demand from two snapshots; restricted fields are withheld for viewers without RecordRestricted_View.</summary>
		Task<List<RecordFieldDiff>> DiffRevisionsAsync(int departmentId, string fromRevisionId, string toRevisionId, bool canViewRestricted);

		Task RecordAccessAsync(int departmentId, string userId, string recordId, string revisionId, RmsAccessAuditAction action, string purpose = null, string ipAddress = null, RmsOriginClient originClient = RmsOriginClient.Web);
	}
}
