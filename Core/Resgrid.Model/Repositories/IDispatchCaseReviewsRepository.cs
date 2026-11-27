using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Dispatch case reviews (Protocols P5 QA, M0391). Every read and write is scoped to the department; the state changes that may happen only
	/// once (send, acknowledge, the dispatcher's reply) are single conditional statements, so two clicks cannot both win.
	/// </summary>
	public interface IDispatchCaseReviewsRepository : IRepository<DispatchCaseReview>
	{
		Task<DispatchCaseReview> GetByIdForDepartmentAsync(int departmentId, int dispatchCaseReviewId);

		/// <summary>The officers' queue: Queued and InReview reviews, oldest sample first.</summary>
		Task<List<DispatchCaseReview>> GetOpenAsync(int departmentId, int take);

		/// <summary>The dispatcher's sent and acknowledged reviews (compared case-insensitively), newest first.</summary>
		Task<List<DispatchCaseReview>> GetSentForDispatcherAsync(int departmentId, string dispatcherUserId, int take);

		/// <summary>Sent and acknowledged reviews of calls logged in the window, for the compliance report.</summary>
		Task<List<DispatchCaseReview>> GetSentForReportAsync(int departmentId, DateTime fromUtc, DateTime toUtc, int? dispatchProtocolId, int? protocolVersionId,
			string dispatcherUserId, int take);

		/// <summary>How many reviews the department has in each status.</summary>
		Task<Dictionary<int, int>> GetStatusCountsAsync(int departmentId);

		/// <summary>
		/// Call protocols with a recorded case on the department's (not deleted) calls logged in the window, filtered as asked, with whether a
		/// review already exists. Short columns only (no case JSON), newest call first, at most <paramref name="take"/>.
		/// </summary>
		Task<List<DispatchCaseCandidate>> GetCandidatesAsync(int departmentId, DispatchCaseCandidateFilter filter, int take);

		/// <summary>The recorded case JSON of the department's call protocols (CallProtocolId → CaseJson).</summary>
		Task<Dictionary<int, string>> GetCaseJsonAsync(int departmentId, IReadOnlyCollection<int> callProtocolIds);

		/// <summary>Inserts a sampled review; false when the call protocol was reviewed already (the unique index; a concurrent sample).</summary>
		Task<bool> TryInsertAsync(DispatchCaseReview review, CancellationToken cancellationToken = default);

		/// <summary>Writes the scores, status and reviewer fields when the stored RowVersion is still <paramref name="expectedRowVersion"/> and the review is open.</summary>
		Task<bool> TrySaveScoresAsync(DispatchCaseReview review, long expectedRowVersion, CancellationToken cancellationToken = default);

		/// <summary>Assigns an open review to an officer when it is still at <paramref name="expectedRowVersion"/>.</summary>
		Task<bool> TryAssignAsync(int departmentId, int dispatchCaseReviewId, string assigneeUserId, long expectedRowVersion, DateTime nowUtc, CancellationToken cancellationToken = default);

		/// <summary>Sent → Acknowledged for the review's dispatcher, once; the optional comment becomes the one reply when there is none yet.</summary>
		Task<bool> TryAcknowledgeAsync(int departmentId, int dispatchCaseReviewId, string dispatcherUserId, string comment, DateTime nowUtc, CancellationToken cancellationToken = default);

		/// <summary>The dispatcher's one reply on a sent or acknowledged review.</summary>
		Task<bool> TryCommentAsync(int departmentId, int dispatchCaseReviewId, string dispatcherUserId, string comment, DateTime nowUtc, CancellationToken cancellationToken = default);

		/// <summary>Takes an open (never sent) review out of the queue.</summary>
		Task<bool> TryDeleteOpenAsync(int departmentId, int dispatchCaseReviewId, CancellationToken cancellationToken = default);
	}
}
