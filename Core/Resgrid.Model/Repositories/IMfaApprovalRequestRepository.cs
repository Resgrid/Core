using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Security;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Responder approval requests (passkey plan section 5.6; workbook section 8.3). Every transition is one guarded UPDATE
	/// on its own connection whose success is exactly one row: a request is approved once and consumed once by the
	/// requester that made it, approval never revives an expired or canceled request, and a denial is terminal.
	/// </summary>
	public interface IMfaApprovalRequestRepository
	{
		/// <summary>
		/// Inserts the request unless the user already has a pending one; false when another pending request exists (a
		/// concurrent request won). Expired pending rows are closed first, so they never block.
		/// </summary>
		Task<bool> TryInsertPendingAsync(MfaApprovalRequest request, DateTime utcNow, CancellationToken cancellationToken = default);

		Task<MfaApprovalRequest> GetAsync(string approvalRequestId, CancellationToken cancellationToken = default);

		/// <summary>The user's pending, unexpired request, if any.</summary>
		Task<MfaApprovalRequest> GetPendingForUserAsync(string userId, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>The user's most recent requests, newest first, for the suspension rule.</summary>
		Task<IReadOnlyList<MfaApprovalRequest>> GetRecentForUserAsync(string userId, int take, CancellationToken cancellationToken = default);

		Task<int> CountCreatedSinceAsync(string userId, DateTime sinceUtc, CancellationToken cancellationToken = default);

		/// <summary>Counts a wrong number on a pending, unexpired request; the last allowed attempt denies it. Returns the new state, or null when nothing changed.</summary>
		Task<MfaApprovalRequestState?> RecordWrongNumberAsync(string approvalRequestId, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Pending and unexpired, under its attempt limit, to approved by this Responder session and passkey.</summary>
		Task<bool> TryApproveAsync(string approvalRequestId, string approverSessionId, string approverPasskeyId, DateTime utcNow,
			CancellationToken cancellationToken = default);

		/// <summary>Pending to denied, with the reason.</summary>
		Task<bool> TryDenyAsync(string approvalRequestId, MfaApprovalEndReason reason, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Pending to canceled, only by the requester that made it.</summary>
		Task<bool> TryCancelAsync(string approvalRequestId, MfaApprovalRequesterKind requesterKind, string requesterId, DateTime utcNow,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Approved to consumed, once, by the requester that made it, no later than <paramref name="graceAfterExpiry"/> past the
		/// request's expiry (the requester's last poll may land just after it).
		/// </summary>
		Task<bool> TryConsumeAsync(string approvalRequestId, MfaApprovalRequesterKind requesterKind, string requesterId, DateTime utcNow,
			TimeSpan graceAfterExpiry, CancellationToken cancellationToken = default);

		/// <summary>Cancels the user's pending requests with the reason (a newer request, or the approver revoked); returns how many.</summary>
		Task<int> CancelPendingForUserAsync(string userId, MfaApprovalEndReason reason, DateTime utcNow, CancellationToken cancellationToken = default);

		Task<int> PurgeCreatedBeforeAsync(DateTime utcCutoff, CancellationToken cancellationToken = default);
	}
}
