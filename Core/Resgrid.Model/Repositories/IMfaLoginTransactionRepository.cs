using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Security;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Restricted login transactions (workbook section 8.3): every state change is one guarded UPDATE whose success is
	/// exactly one row, on its own connection, so two nodes can never both complete or redeem one transaction.
	/// </summary>
	public interface IMfaLoginTransactionRepository
	{
		Task InsertAsync(MfaLoginTransaction transaction, CancellationToken cancellationToken = default);

		Task<MfaLoginTransaction> GetBySecretHashAsync(byte[] secretHash, CancellationToken cancellationToken = default);

		/// <summary>Counts a failed second factor; the transaction is exhausted in the same statement at its limit.</summary>
		Task RecordFailedAttemptAsync(string transactionId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Moves a pending, unexpired transaction under its attempt limit to Completed with the verified factor and the
		/// completion code's hash. True for exactly one caller. A null method completes a login that needed no second factor.
		/// </summary>
		Task<bool> TryCompleteAsync(string transactionId, int? method, string factorReference, DateTime? verifiedOnUtc, bool isRecovery,
			byte[] completionCodeHash, DateTime completionExpiresOnUtc, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Redeems a completed transaction whose code matches and has not expired. True for exactly one caller.</summary>
		Task<bool> TryRedeemAsync(string transactionId, byte[] completionCodeHash, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>
		/// Ends a pending or completed-but-unredeemed transaction, as exhausted: the user said they did not start this
		/// sign-in (plan section 7.9 "not me"), so nothing more is issued from it.
		/// </summary>
		Task<bool> TryAbandonAsync(string transactionId, CancellationToken cancellationToken = default);

		Task<int> PurgeExpiredBeforeAsync(DateTime utcCutoff, CancellationToken cancellationToken = default);
	}
}
