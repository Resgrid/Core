using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Security;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Registered passkeys (passkey plan section 5.1; workbook section 8.3 rules): each change is one guarded statement on
	/// its own connection, and a credential id is unique within its RP, so concurrent registration can never attach one
	/// credential to two users.
	/// </summary>
	public interface IUserPasskeyRepository
	{
		/// <summary>Inserts the credential; false when the RP already holds that credential id.</summary>
		Task<bool> TryInsertAsync(UserPasskey passkey, CancellationToken cancellationToken = default);

		Task<UserPasskey> GetAsync(string userPasskeyId, CancellationToken cancellationToken = default);

		/// <summary>The active credential with this id hash in the RP, or null. The caller compares the full id.</summary>
		Task<UserPasskey> GetActiveByCredentialAsync(string rpId, byte[] credentialIdHash, CancellationToken cancellationToken = default);

		/// <summary>Every active credential the user holds, in every client.</summary>
		Task<IReadOnlyList<UserPasskey>> GetActiveForUserAsync(string userId, CancellationToken cancellationToken = default);

		Task<int> CountActiveForUserAsync(string userId, CancellationToken cancellationToken = default);

		/// <summary>
		/// The user handle the user already has at this RP (from any of their credentials, revoked or not), so every
		/// credential for one account and RP shares one stable handle; null when there is none yet.
		/// </summary>
		Task<byte[]> GetUserHandleAsync(string userId, string rpId, CancellationToken cancellationToken = default);

		Task<bool> TryRenameAsync(string userPasskeyId, string userId, string displayName, CancellationToken cancellationToken = default);

		/// <summary>Revokes one active credential the user owns; false when it is not the user's or already revoked.</summary>
		Task<bool> TryRevokeAsync(string userPasskeyId, string userId, PasskeyRevocationReason reason, string actorUserId, DateTime utcNow,
			CancellationToken cancellationToken = default);

		/// <summary>Revokes every active credential the user holds for one client; returns the revoked row ids.</summary>
		Task<IReadOnlyList<string>> RevokeAllForClientAsync(string userId, int clientApplication, PasskeyRevocationReason reason, string actorUserId, DateTime utcNow,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Records a successful assertion only if the stored counter is still <paramref name="expectedSignCount"/> and the
		/// credential is active; false means another use or a revocation won the race and this assertion is refused.
		/// </summary>
		Task<bool> TryRecordUseAsync(string userPasskeyId, long expectedSignCount, long newSignCount, bool isBackedUp, int clientApplication,
			string installation, bool sharedMode, DateTime utcNow, CancellationToken cancellationToken = default);

		Task<bool> TrySetApprovalEnabledAsync(string userPasskeyId, string userId, bool enabled, CancellationToken cancellationToken = default);
	}
}
