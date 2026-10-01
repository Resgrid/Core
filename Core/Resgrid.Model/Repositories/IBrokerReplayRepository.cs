using System;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Shared, single-use record of broker request ids and session-assertion ids (passkey workbook section 6.2). It
	/// replaces the per-process replay cache, so a replay is refused by every broker replica. A fault is thrown, and the
	/// broker treats it as a refusal.
	/// </summary>
	public interface IBrokerReplayRepository
	{
		/// <summary>
		/// Records the key once. True only for the first claim; false when it was already claimed, even if that claim has
		/// expired but not yet been purged.
		/// </summary>
		Task<bool> TryClaimAsync(string replayKey, BrokerReplayKind kind, DateTime expiresOnUtc, DateTime utcNow,
			CancellationToken cancellationToken = default);

		Task<int> PurgeExpiredBeforeAsync(DateTime utcCutoff, CancellationToken cancellationToken = default);
	}

	public enum BrokerReplayKind
	{
		RequestId = 1,
		SessionAssertion = 2,

		/// <summary>An IdP id_token, recorded until its expiry so it is accepted once (passkey plan section 7.7.2 item 8).</summary>
		IdToken = 3
	}
}
