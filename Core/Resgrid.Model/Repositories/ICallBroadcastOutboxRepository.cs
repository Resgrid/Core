using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Pages owed to resources added to live calls (M0321); see <see cref="CallBroadcastOutboxEntry"/>.</summary>
	public interface ICallBroadcastOutboxRepository
	{
		Task InsertEntryAsync(CallBroadcastOutboxEntry entry, CancellationToken cancellationToken = default);

		Task<CallBroadcastOutboxEntry> GetEntryAsync(string callBroadcastOutboxId, CancellationToken cancellationToken = default);

		/// <summary>The call's rows not sent yet (pending, or claimed with a lapsed lease), oldest first.</summary>
		Task<IEnumerable<string>> GetOpenIdsForCallAsync(int callId, DateTime nowUtc, CancellationToken cancellationToken = default);

		/// <summary>Rows the worker may try now: pending and due, or claimed with a lapsed lease; oldest first.</summary>
		Task<IEnumerable<string>> GetDueIdsAsync(DateTime nowUtc, int take, CancellationToken cancellationToken = default);

		/// <summary>
		/// Claims the row for one sender (Pending, or Sending with a lapsed lease, to Sending; one more attempt). A pending row in
		/// backoff is claimed only when <paramref name="ignoreSchedule"/>. False when someone else has it or it is done.
		/// </summary>
		Task<bool> TryClaimAsync(string callBroadcastOutboxId, string claimToken, DateTime nowUtc, DateTime leaseExpiresOnUtc, bool ignoreSchedule,
			CancellationToken cancellationToken = default);

		/// <summary>Ends a row this claim holds (<see cref="CallBroadcastOutboxStates"/> Sent, Skipped or Expired).</summary>
		Task<bool> CompleteAsync(string callBroadcastOutboxId, string claimToken, CallBroadcastOutboxStates state, DateTime nowUtc, string error,
			CancellationToken cancellationToken = default);

		/// <summary>Hands a row this claim holds back as pending, due again at <paramref name="nextAttemptOnUtc"/>.</summary>
		Task<bool> RetryLaterAsync(string callBroadcastOutboxId, string claimToken, DateTime nextAttemptOnUtc, string error,
			CancellationToken cancellationToken = default);

		/// <summary>Marks a still-pending row superseded by a full rebroadcast; a row being sent right now is left to finish.</summary>
		Task<bool> TrySupersedeAsync(string callBroadcastOutboxId, DateTime nowUtc, CancellationToken cancellationToken = default);

		/// <summary>Deletes rows that ended before <paramref name="beforeUtc"/>.</summary>
		Task<int> PurgeCompletedBeforeAsync(DateTime beforeUtc, CancellationToken cancellationToken = default);
	}
}
