using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Security;

namespace Resgrid.Model.Repositories
{
	/// <summary>The account's recent verifications (plan section 6.5).</summary>
	public interface IMfaActivityRepository
	{
		Task InsertAsync(MfaActivity activity, CancellationToken cancellationToken = default);

		Task<int> CountDeniedSinceAsync(string userId, DateTime sinceUtc, CancellationToken cancellationToken = default);

		/// <summary>The user's activity since <paramref name="sinceUtc"/>, newest first, at most <paramref name="take"/>.</summary>
		Task<IReadOnlyList<MfaActivity>> GetRecentAsync(string userId, DateTime sinceUtc, int take, CancellationToken cancellationToken = default);

		Task<MfaActivity> GetAsync(string mfaActivityId, CancellationToken cancellationToken = default);

		/// <summary>Marks the user's own activity reported, once, in one guarded statement.</summary>
		Task<bool> TryMarkReportedAsync(string mfaActivityId, string userId, DateTime reportedOnUtc, CancellationToken cancellationToken = default);

		Task<int> PurgeBeforeAsync(DateTime utcCutoff, CancellationToken cancellationToken = default);

		Task<int> DeleteForUserAsync(string userId, CancellationToken cancellationToken = default);
	}
}
