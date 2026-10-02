using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Security;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Restricted factor recovery transactions (passkey plan section 5.4; workbook section 8.3): every state change is one
	/// guarded UPDATE whose success is exactly one row, so a recovery completes once however many requests race.
	/// </summary>
	public interface IFactorRecoveryTransactionRepository
	{
		Task InsertAsync(FactorRecoveryTransaction transaction, CancellationToken cancellationToken = default);

		Task<FactorRecoveryTransaction> GetBySecretHashAsync(byte[] secretHash, CancellationToken cancellationToken = default);

		/// <summary>Counts a wrong code for the new authenticator; the last allowed attempt exhausts the transaction.</summary>
		Task RecordFailedAttemptAsync(string transactionId, CancellationToken cancellationToken = default);

		/// <summary>Pending, unexpired and under its attempt limit, to completed.</summary>
		Task<bool> TryCompleteAsync(string transactionId, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Pending to canceled.</summary>
		Task<bool> TryCancelAsync(string transactionId, CancellationToken cancellationToken = default);

		Task<int> PurgeExpiredBeforeAsync(DateTime utcCutoff, CancellationToken cancellationToken = default);

		/// <summary>Removes every recovery transaction for an account being deleted.</summary>
		Task<int> DeleteForUserAsync(string userId, CancellationToken cancellationToken = default);
	}
}
