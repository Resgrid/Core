using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Enhanced AI destination advice rows (AiDestinationAdvices, M0323). Every read starts at the department.</summary>
	public interface IAiDestinationAdvicesRepository : IRepository<AiDestinationAdvice>
	{
		/// <summary>One of the department's advice rows, or null.</summary>
		Task<AiDestinationAdvice> GetForDepartmentAsync(int departmentId, string adviceId, CancellationToken cancellationToken = default);

		/// <summary>The call's newest Open advice, or null.</summary>
		Task<AiDestinationAdvice> GetOpenForCallAsync(int departmentId, int callId, CancellationToken cancellationToken = default);

		/// <summary>The call's newest advice in any state (its input digest says whether the call changed since), or null.</summary>
		Task<AiDestinationAdvice> GetLatestForCallAsync(int departmentId, int callId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Marks the call's Open advice created before <paramref name="exceptAdviceId"/> (CreatedOn, then id) Superseded; a newer open row
		/// is left alone, so two publishers racing on one call always leave the newest advice open. A null id supersedes every Open row.
		/// Returns how many changed.
		/// </summary>
		Task<int> SupersedeOpenAsync(int departmentId, int callId, string exceptAdviceId, DateTime nowUtc, CancellationToken cancellationToken = default);

		/// <summary>
		/// Moves the advice from <paramref name="fromState"/> to <paramref name="toState"/>, recording who and when; false when it is not
		/// in <paramref name="fromState"/> any more (two people pressing Apply: only one wins).
		/// </summary>
		Task<bool> TryTransitionAsync(int departmentId, string adviceId, AiDestinationAdviceStates fromState, AiDestinationAdviceStates toState, string userId,
			DateTime? resolvedOnUtc, CancellationToken cancellationToken = default);
	}

	/// <summary>Destination review requests (AiDestinationReviews, M0323): at most one waiting (unprocessed) row per call.</summary>
	public interface IAiDestinationReviewsRepository : IRepository<AiDestinationReview>
	{
		/// <summary>
		/// Queues a review: inserts the call's waiting row, or folds the trigger into the one already waiting. A waiting row keeps its
		/// <see cref="AiDestinationReview.RequestedOn"/> (so a stream of notes cannot postpone it forever); a row a worker has already
		/// claimed moves it to <paramref name="nowUtc"/>, so the finished pass sees the newer request and leaves the row for another run.
		/// </summary>
		Task QueueAsync(int departmentId, int callId, AiDestinationReviewTriggers trigger, DateTime nowUtc, CancellationToken cancellationToken = default);

		/// <summary>Waiting rows requested at or before <paramref name="dueBeforeUtc"/> and not claimed (or whose claim lapsed), oldest first.</summary>
		Task<List<AiDestinationReview>> GetDueAsync(DateTime dueBeforeUtc, DateTime leaseCutoffUtc, int take, CancellationToken cancellationToken = default);

		/// <summary>Claims a waiting row (lease); false when another worker holds it or it was processed.</summary>
		Task<bool> TryClaimAsync(string reviewId, DateTime claimedOnUtc, DateTime leaseCutoffUtc, CancellationToken cancellationToken = default);

		/// <summary>
		/// Finishes a claimed row. When a trigger arrived while it was being processed (RequestedOn moved past the claim) the row is
		/// released for another run instead and false is returned.
		/// </summary>
		Task<bool> CompleteAsync(string reviewId, DateTime claimedOnUtc, string outcome, DateTime processedOnUtc, CancellationToken cancellationToken = default);

		/// <summary>Gives a claimed row back unprocessed (busy, cancelled) so a later pass takes it.</summary>
		Task ReleaseAsync(string reviewId, DateTime claimedOnUtc, CancellationToken cancellationToken = default);

		/// <summary>Deletes processed rows processed before <paramref name="cutoffUtc"/>.</summary>
		Task<int> DeleteProcessedBeforeAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default);
	}
}
