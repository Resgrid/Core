using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;

namespace Resgrid.Tests.Security
{
	/// <summary>
	/// Single-process stand-in with the same observable rules as MfaLoginTransactionRepository: guarded state changes that
	/// succeed for exactly one caller. Real cross-node atomicity is covered by MfaLoginTransactionDatabaseTests.
	/// </summary>
	internal sealed class InMemoryMfaLoginTransactionRepository : IMfaLoginTransactionRepository
	{
		private readonly object _gate = new();
		public readonly List<MfaLoginTransaction> Rows = new();
		public Exception ThrowOnRead { get; set; }

		public Task InsertAsync(MfaLoginTransaction transaction, CancellationToken cancellationToken = default)
		{
			lock (_gate) Rows.Add(Copy(transaction));
			return Task.CompletedTask;
		}

		public Task<MfaLoginTransaction> GetBySecretHashAsync(byte[] secretHash, CancellationToken cancellationToken = default)
		{
			if (ThrowOnRead != null) throw ThrowOnRead;
			lock (_gate) return Task.FromResult(Copy(Rows.FirstOrDefault(r => r.SecretHash.AsSpan().SequenceEqual(secretHash))));
		}

		public Task RecordFailedAttemptAsync(string transactionId, CancellationToken cancellationToken = default)
		{
			lock (_gate)
			{
				var row = Rows.FirstOrDefault(r => r.MfaLoginTransactionId == transactionId && r.State == (int)MfaLoginTransactionState.Pending);
				if (row != null)
				{
					row.Attempts++;
					if (row.Attempts >= row.MaxAttempts)
						row.State = (int)MfaLoginTransactionState.Exhausted;
				}
			}
			return Task.CompletedTask;
		}

		public Task<bool> TryCompleteAsync(string transactionId, int? method, string factorReference, DateTime? verifiedOnUtc, bool isRecovery,
			byte[] completionCodeHash, DateTime completionExpiresOnUtc, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			lock (_gate)
			{
				var row = Rows.FirstOrDefault(r => r.MfaLoginTransactionId == transactionId && r.State == (int)MfaLoginTransactionState.Pending
					&& r.ExpiresOnUtc > utcNow && r.Attempts < r.MaxAttempts);
				if (row == null) return Task.FromResult(false);
				row.State = (int)MfaLoginTransactionState.Completed;
				row.CompletionMethod = method;
				row.CompletionFactorReference = factorReference;
				row.CompletionVerifiedOnUtc = verifiedOnUtc;
				row.IsRecovery = isRecovery;
				row.CompletionCodeHash = completionCodeHash;
				row.CompletionExpiresOnUtc = completionExpiresOnUtc;
				return Task.FromResult(true);
			}
		}

		public Task<bool> TryRedeemAsync(string transactionId, byte[] completionCodeHash, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			lock (_gate)
			{
				var row = Rows.FirstOrDefault(r => r.MfaLoginTransactionId == transactionId && r.State == (int)MfaLoginTransactionState.Completed
					&& r.CompletionCodeHash != null && r.CompletionCodeHash.AsSpan().SequenceEqual(completionCodeHash) && r.CompletionExpiresOnUtc > utcNow);
				if (row == null) return Task.FromResult(false);
				row.State = (int)MfaLoginTransactionState.Redeemed;
				row.RedeemedOnUtc = utcNow;
				return Task.FromResult(true);
			}
		}

		public Task<bool> TryAbandonAsync(string transactionId, CancellationToken cancellationToken = default)
		{
			lock (_gate)
			{
				var row = Rows.SingleOrDefault(r => r.MfaLoginTransactionId == transactionId &&
					(r.TransactionState == MfaLoginTransactionState.Pending || r.TransactionState == MfaLoginTransactionState.Completed));
				if (row == null)
					return Task.FromResult(false);
				row.State = (int)MfaLoginTransactionState.Exhausted;
				return Task.FromResult(true);
			}
		}

		public Task<int> PurgeExpiredBeforeAsync(DateTime utcCutoff, CancellationToken cancellationToken = default)
		{
			lock (_gate) return Task.FromResult(Rows.RemoveAll(r => r.ExpiresOnUtc < utcCutoff));
		}

		private static MfaLoginTransaction Copy(MfaLoginTransaction row) => row == null ? null : (MfaLoginTransaction)typeof(object)
			.GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(row, null);
	}
}
