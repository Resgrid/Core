using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <inheritdoc cref="IFactorRecoveryService"/>
	public sealed class FactorRecoveryService : IFactorRecoveryService
	{
		private const int MaxSecretLength = 128;

		private readonly IFactorRecoveryTransactionRepository _transactions;
		private readonly IIdentityUserRepository _identityUsers;
		private readonly TimeProvider _time;

		public FactorRecoveryService(IFactorRecoveryTransactionRepository transactions, IIdentityUserRepository identityUsers, TimeProvider time)
		{
			_transactions = transactions;
			_identityUsers = identityUsers;
			_time = time;
		}

		public bool IsEnabled => TwoFactorConfig.LoginMfaTransactionEnabled;

		private static TimeSpan Lifetime => TimeSpan.FromMinutes(Math.Max(1, TwoFactorConfig.FactorRecoveryLifetimeMinutes));

		public async Task<FactorRecoveryStart> BeginAsync(MfaLoginTransaction login, CancellationToken cancellationToken = default)
		{
			if (login == null || string.IsNullOrWhiteSpace(login.UserId))
				throw new ArgumentException("A recovery starts from a verified first factor.", nameof(login));

			var now = _time.GetUtcNow().UtcDateTime;
			var secret = NewSecret();
			var transaction = new FactorRecoveryTransaction
			{
				FactorRecoveryTransactionId = Guid.NewGuid().ToString(),
				SecretHash = Hash(secret),
				UserId = login.UserId,
				ClientApplication = login.ClientApplication,
				FirstFactorMethod = login.FirstFactorMethod,
				FirstFactorVerifiedOnUtc = login.FirstFactorVerifiedOnUtc,
				DepartmentSsoConfigId = login.DepartmentSsoConfigId,
				DepartmentId = login.DepartmentId,
				AuthenticationGeneration = login.AuthenticationGeneration,
				CreatedOnUtc = now,
				ExpiresOnUtc = now.Add(Lifetime),
				MaxAttempts = Math.Max(1, TwoFactorConfig.FactorRecoveryMaxAttempts),
				State = (int)FactorRecoveryState.Pending
			};
			await _transactions.InsertAsync(transaction, cancellationToken);
			return new FactorRecoveryStart { Secret = secret, ExpiresInSeconds = (int)Lifetime.TotalSeconds, Transaction = transaction };
		}

		public async Task<FactorRecoveryResult> OpenAsync(string secret, UserSessionClientApplication client, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(secret) || secret.Length > MaxSecretLength)
				return FactorRecoveryResult.Of(FactorRecoveryOutcome.Invalid);

			try
			{
				var transaction = await _transactions.GetBySecretHashAsync(Hash(secret), cancellationToken);
				if (transaction == null || transaction.ClientApplication != (int)client)
					return FactorRecoveryResult.Of(FactorRecoveryOutcome.Invalid);

				var now = _time.GetUtcNow().UtcDateTime;
				var outcome = transaction.RecoveryState switch
				{
					FactorRecoveryState.Pending when transaction.ExpiresOnUtc <= now => FactorRecoveryOutcome.Expired,
					FactorRecoveryState.Pending when transaction.Attempts >= transaction.MaxAttempts => FactorRecoveryOutcome.TooManyAttempts,
					FactorRecoveryState.Pending => FactorRecoveryOutcome.Usable,
					FactorRecoveryState.Exhausted => FactorRecoveryOutcome.TooManyAttempts,
					_ => FactorRecoveryOutcome.AlreadyUsed
				};
				if (outcome != FactorRecoveryOutcome.Usable)
					return FactorRecoveryResult.Of(outcome);

				// A password change or any revocation since the recovery began voids it (plan section 5.4).
				var user = await _identityUsers.GetByIdAsync(transaction.UserId);
				return user == null || user.AuthenticationGeneration != transaction.AuthenticationGeneration
					? FactorRecoveryResult.Of(FactorRecoveryOutcome.SessionRevoked)
					: FactorRecoveryResult.Of(FactorRecoveryOutcome.Usable, transaction);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				// Authoritative state is unavailable: recovery is refused, never assumed.
				Logging.LogException(ex, "A factor recovery could not be read; it was refused.");
				return FactorRecoveryResult.Of(FactorRecoveryOutcome.Unavailable);
			}
		}

		public async Task RecordFailedAttemptAsync(FactorRecoveryTransaction transaction, CancellationToken cancellationToken = default)
		{
			try
			{
				await _transactions.RecordFailedAttemptAsync(transaction.FactorRecoveryTransactionId, cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "A failed factor recovery attempt could not be counted.");
			}
		}

		public Task<bool> TryCompleteAsync(FactorRecoveryTransaction transaction, CancellationToken cancellationToken = default) =>
			_transactions.TryCompleteAsync(transaction.FactorRecoveryTransactionId, _time.GetUtcNow().UtcDateTime, cancellationToken);

		public async Task<FactorRecoveryOutcome> CancelAsync(string secret, UserSessionClientApplication client, CancellationToken cancellationToken = default)
		{
			var opened = await OpenAsync(secret, client, cancellationToken);
			if (!opened.IsUsable)
				return opened.Outcome;

			return await _transactions.TryCancelAsync(opened.Transaction.FactorRecoveryTransactionId, cancellationToken)
				? FactorRecoveryOutcome.Usable
				: FactorRecoveryOutcome.AlreadyUsed;
		}

		private static string NewSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

		private static byte[] Hash(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes(secret));
	}
}
