using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Durable, compare-and-set MFA factor state (passkey plan section 5, workbook section 8). Every consume is one
	/// guarded statement whose success is exactly one changed row, so two API nodes can never both accept the same
	/// TOTP time step or recovery code. No TOTP seed or plaintext recovery code is stored here.
	/// </summary>
	public interface IUserMfaStateRepository
	{
		/// <summary>
		/// Records <paramref name="timeStep"/> as the user's last accepted TOTP step when it is newer than the stored one.
		/// Returns false when the step (or a later one) was already accepted — a replayed code.
		/// </summary>
		Task<bool> TryConsumeTotpTimeStepAsync(string userId, long timeStep, DateTime utcNow, CancellationToken cancellationToken = default);

		Task<UserTotpState> GetTotpStateAsync(string userId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Stamps when the current authenticator was enrolled or replaced, and whether that happened in a shared vehicle or
		/// workstation session (plan section 6.5). The time-step row must already exist.
		/// </summary>
		Task RecordTotpEnrollmentAsync(string userId, DateTime utcNow, TotpEnrollmentContext context, CancellationToken cancellationToken = default);

		Task<int> CountUnusedRecoveryCodesAsync(string userId, CancellationToken cancellationToken = default);

		/// <summary>Marks one unused code used. Returns false when no unused code has that hash.</summary>
		Task<bool> TryRedeemRecoveryCodeAsync(string userId, byte[] codeHash, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>
		/// Atomically replaces every recovery code for the user (and deletes any legacy plaintext token). An empty set
		/// leaves the user with no recovery codes.
		/// </summary>
		Task ReplaceRecoveryCodesAsync(string userId, IReadOnlyCollection<byte[]> codeHashes, int hashVersion, DateTime utcNow,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// One-time migration of the legacy ";"-joined plaintext token: deletes the token only if it still holds
		/// <paramref name="legacyTokenValue"/> and inserts the hashed codes in the same transaction. Returns false when
		/// another request already migrated or changed it.
		/// </summary>
		Task<bool> ImportLegacyRecoveryCodesAsync(string userId, string legacyTokenValue, IReadOnlyCollection<byte[]> codeHashes,
			int hashVersion, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>The user's last successful MFA method (<c>MfaEvidenceMethod</c> value), or null when none is recorded.</summary>
		Task<int?> GetPreferredMethodAsync(string userId, CancellationToken cancellationToken = default);

		/// <summary>Records the user's last successful MFA method (plan section 7.5 rule 5).</summary>
		Task SetPreferredMethodAsync(string userId, int method, DateTime utcNow, CancellationToken cancellationToken = default);
	}

	public sealed class UserTotpState
	{
		public string UserId { get; set; }
		public long LastAcceptedTimeStep { get; set; }
		public DateTime LastAcceptedOnUtc { get; set; }
		public DateTime? EnrolledOnUtc { get; set; }

		/// <summary>The current authenticator was set up in a shared session, so its setup key may have been seen there.</summary>
		public bool EnrolledInSharedMode { get; set; }

		/// <summary>The app (<c>UserSessionClientApplication</c>) the current authenticator was set up from.</summary>
		public int? EnrolledClientApplication { get; set; }

		/// <summary>The installation label it was set up on; a label, not proof of a device.</summary>
		public string EnrolledInstallation { get; set; }
	}

	/// <summary>Where an authenticator was set up (plan section 6.5).</summary>
	public sealed record TotpEnrollmentContext(bool SharedMode, int? ClientApplication = null, string Installation = null);
}
