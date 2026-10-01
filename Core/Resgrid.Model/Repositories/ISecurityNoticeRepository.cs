using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Security;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// The user-level security notice outbox (passkey plan section 6.4; workbook section 8.3 rule 7). A sender claims a
	/// notice with a lease in one guarded statement, so two nodes never send the same notice at once, and every result is
	/// recorded only by the sender holding the lease.
	/// </summary>
	public interface ISecurityNoticeRepository
	{
		Task InsertAsync(SecurityNotice notice, CancellationToken cancellationToken = default);

		Task<SecurityNotice> GetAsync(string securityNoticeId, CancellationToken cancellationToken = default);

		/// <summary>Claims one pending, due, unleased notice for <paramref name="owner"/> until <paramref name="leaseUntilUtc"/>.</summary>
		Task<bool> TryClaimAsync(string securityNoticeId, string owner, DateTime utcNow, DateTime leaseUntilUtc, CancellationToken cancellationToken = default);

		/// <summary>Claims up to <paramref name="batchSize"/> pending, due, unleased notices, skipping rows another sender holds.</summary>
		Task<IReadOnlyList<SecurityNotice>> ClaimDueAsync(string owner, DateTime utcNow, DateTime leaseUntilUtc, int batchSize,
			CancellationToken cancellationToken = default);

		Task<bool> MarkSentAsync(string securityNoticeId, string owner, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Counts a failed attempt and schedules the next one, releasing the lease.</summary>
		Task<bool> MarkRetryAsync(string securityNoticeId, string owner, DateTime nextAttemptOnUtc, string failure, CancellationToken cancellationToken = default);

		/// <summary>Counts the last failed attempt and ends the notice as failed.</summary>
		Task<bool> MarkFailedAsync(string securityNoticeId, string owner, string failure, CancellationToken cancellationToken = default);

		/// <summary>Whether the user was already sent (or is being sent) this kind of notice since <paramref name="sinceUtc"/>.</summary>
		Task<bool> ExistsSinceAsync(string userId, SecurityNoticeKind kind, DateTime sinceUtc, CancellationToken cancellationToken = default);

		/// <summary>Removes sent and failed notices created before the cutoff.</summary>
		Task<int> PurgeFinishedBeforeAsync(DateTime utcCutoff, CancellationToken cancellationToken = default);

		/// <summary>Removes every notice for an account being deleted.</summary>
		Task<int> DeleteForUserAsync(string userId, CancellationToken cancellationToken = default);
	}
}
