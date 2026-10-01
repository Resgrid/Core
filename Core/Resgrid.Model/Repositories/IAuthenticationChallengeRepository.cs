using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Security;

namespace Resgrid.Model.Repositories
{
	/// <summary>Compare-and-set challenge storage (workbook section 8.3). Each call uses its own connection.</summary>
	public interface IAuthenticationChallengeRepository
	{
		Task InsertAsync(AuthenticationChallenge challenge, CancellationToken cancellationToken = default);

		Task<AuthenticationChallenge> GetAsync(string challengeId, CancellationToken cancellationToken = default);

		Task<int> CountPendingForUserAsync(string userId, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Spends a pending, unexpired challenge with attempts left. True for exactly one caller.</summary>
		Task<bool> TryConsumeAsync(string challengeId, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Counts a failed verification; the challenge becomes Exhausted when it reaches its attempt limit.</summary>
		Task RecordFailedAttemptAsync(string challengeId, CancellationToken cancellationToken = default);

		/// <summary>Cancels every pending challenge for the user (credential revoked, MFA changed, sessions revoked).</summary>
		Task<int> CancelPendingForUserAsync(string userId, CancellationToken cancellationToken = default);

		Task<int> PurgeExpiredBeforeAsync(DateTime utcCutoff, CancellationToken cancellationToken = default);
	}
}
