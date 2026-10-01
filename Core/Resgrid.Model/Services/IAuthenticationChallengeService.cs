using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Security;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Issues and spends WebAuthn ceremony challenges (passkey plan section 5.2). The caller verifies the ceremony between
	/// <see cref="GetForCompletionAsync"/> and <see cref="TryConsumeAsync"/>; only one completion can ever consume a
	/// challenge, on any node, and a storage fault never counts as success.
	/// </summary>
	public interface IAuthenticationChallengeService
	{
		/// <summary>
		/// Stores a new pending challenge for the binding with the given server options. Returns null when the user already
		/// holds the maximum number of pending challenges.
		/// </summary>
		Task<AuthenticationChallenge> CreateAsync(AuthenticationChallengeBinding binding, string rpId, string optionsJson,
			CancellationToken cancellationToken = default);

		/// <summary>Loads the challenge and checks it is pending, unexpired and issued to exactly this binding.</summary>
		Task<AuthenticationChallengeResult> GetForCompletionAsync(string challengeId, AuthenticationChallengeBinding binding,
			CancellationToken cancellationToken = default);

		/// <summary>Spends the challenge after a successful verification; true for exactly one caller.</summary>
		Task<bool> TryConsumeAsync(string challengeId, CancellationToken cancellationToken = default);

		/// <summary>Counts a failed verification against the challenge.</summary>
		Task RecordFailedAttemptAsync(string challengeId, CancellationToken cancellationToken = default);

		/// <summary>Cancels every pending challenge for the user.</summary>
		Task CancelPendingForUserAsync(string userId, CancellationToken cancellationToken = default);
	}
}
