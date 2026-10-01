using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Security;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Brokered SSO transactions (workbook section 8.3): every state change is one guarded UPDATE whose success is exactly
	/// one row, on its own connection, so an IdP result is accepted once and its code redeemed once across nodes.
	/// </summary>
	public interface ISsoLoginTransactionRepository
	{
		Task InsertAsync(SsoLoginTransaction transaction, CancellationToken cancellationToken = default);

		Task<SsoLoginTransaction> GetAsync(string transactionId, CancellationToken cancellationToken = default);

		Task<SsoLoginTransaction> GetByStateHashAsync(byte[] stateHash, CancellationToken cancellationToken = default);

		/// <summary>
		/// Records the validated IdP result, the value a provider step-up mapping counted as MFA (if any) and the code's hash
		/// on a pending, unexpired transaction. True once.
		/// </summary>
		Task<bool> TryAuthenticateAsync(string transactionId, string userId, DateTime authenticatedOnUtc, string federatedMfaValue, byte[] codeHash,
			DateTime codeExpiresOnUtc, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Marks a pending transaction failed, so its state cannot be used again. True once.</summary>
		Task<bool> TryFailAsync(string transactionId, string failureCode, CancellationToken cancellationToken = default);

		/// <summary>Redeems an authenticated transaction whose code matches and has not expired. True once.</summary>
		Task<bool> TryRedeemAsync(string transactionId, byte[] codeHash, DateTime utcNow, CancellationToken cancellationToken = default);

		Task<int> PurgeExpiredBeforeAsync(DateTime utcCutoff, CancellationToken cancellationToken = default);
	}
}
