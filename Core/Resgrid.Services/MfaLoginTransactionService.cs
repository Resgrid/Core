using System;
using System.Linq;
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
	/// <inheritdoc cref="IMfaLoginTransactionService"/>
	public sealed class MfaLoginTransactionService : IMfaLoginTransactionService
	{
		/// <summary>A secret or completion code is 32 random bytes in base64url; anything much longer is not one.</summary>
		private const int MaxSecretLength = 128;

		private readonly IMfaLoginTransactionRepository _transactions;
		private readonly IMfaPolicyService _policy;
		private readonly IDepartmentSsoService _departmentSso;
		private readonly IIdentityUserRepository _identityUsers;
		private readonly IPasskeyService _passkeys;
		private readonly IMfaApprovalService _approvals;
		private readonly IPasskeyFeatureGates _gates;
		private readonly TimeProvider _time;

		public MfaLoginTransactionService(IMfaLoginTransactionRepository transactions, IMfaPolicyService policy, IDepartmentSsoService departmentSso,
			IIdentityUserRepository identityUsers, IPasskeyService passkeys, IMfaApprovalService approvals, IPasskeyFeatureGates gates, TimeProvider time)
		{
			_approvals = approvals;
			_gates = gates;
			_transactions = transactions;
			_policy = policy;
			_departmentSso = departmentSso;
			_identityUsers = identityUsers;
			_passkeys = passkeys;
			_time = time;
		}

		public bool IsEnabled => TwoFactorConfig.LoginMfaTransactionEnabled;

		private static TimeSpan Lifetime => TimeSpan.FromSeconds(Math.Max(30, TwoFactorConfig.LoginMfaTransactionLifetimeSeconds));
		private static TimeSpan CompletionLifetime => TimeSpan.FromSeconds(Math.Max(10, TwoFactorConfig.LoginMfaCompletionCodeLifetimeSeconds));

		public async Task<MfaLoginTransactionStart> BeginAsync(MfaLoginTransactionRequest request, CancellationToken cancellationToken = default)
		{
			var (start, _) = await InsertAsync(request, cancellationToken);
			return start;
		}

		public Task<MfaLoginCompletion> BeginCompletedAsync(MfaLoginTransactionRequest request, CancellationToken cancellationToken = default) =>
			BeginCompletedAsync(request, null, null, null, cancellationToken);

		public Task<MfaLoginCompletion> BeginCompletedAsync(MfaLoginTransactionRequest request, MfaEvidenceMethod method, string factorReference,
			DateTime verifiedOnUtc, CancellationToken cancellationToken = default) =>
			BeginCompletedAsync(request, (MfaEvidenceMethod?)method, factorReference, (DateTime?)verifiedOnUtc, cancellationToken);

		private async Task<MfaLoginCompletion> BeginCompletedAsync(MfaLoginTransactionRequest request, MfaEvidenceMethod? method, string factorReference,
			DateTime? verifiedOnUtc, CancellationToken cancellationToken)
		{
			var (start, transaction) = await InsertAsync(request, cancellationToken);
			var completion = await CompleteAsync(transaction, method, factorReference, verifiedOnUtc, cancellationToken);
			return completion.Succeeded
				? new MfaLoginCompletion
				{
					Outcome = completion.Outcome,
					Transaction = start.Secret,
					CompletionCode = completion.CompletionCode,
					ExpiresInSeconds = completion.ExpiresInSeconds
				}
				: completion;
		}

		private async Task<(MfaLoginTransactionStart Start, MfaLoginTransaction Transaction)> InsertAsync(MfaLoginTransactionRequest request,
			CancellationToken cancellationToken)
		{
			if (request == null || string.IsNullOrWhiteSpace(request.UserId))
				throw new ArgumentException("A login transaction needs the user whose first factor was verified.", nameof(request));
			if (request.FirstFactorMethod != MfaEvidenceMethod.Password && request.FirstFactorMethod != MfaEvidenceMethod.Sso)
				throw new ArgumentException("A login transaction starts from a password or SSO first factor.", nameof(request));

			// A passkey counts as enrolled only when one is bound to the app signing in (plan section 3 item 14).
			var passkeyEnrolled = await _passkeys.HasActiveForClientAsync(request.UserId, request.ClientApplication, cancellationToken);
			var federatedEnrolled = request.DepartmentId is > 0 &&
				await _departmentSso.IsFederatedMfaAvailableAsync(request.DepartmentId.Value, request.UserId, cancellationToken);
			var approvalEnrolled = await _approvals.IsAvailableAsync(request.UserId, request.ClientApplication, cancellationToken);
			var choice = await _policy.GetMethodChoiceAsync(request.UserId, request.TotpEnrolled, request.DepartmentId, MfaMethodScope.Login,
				passkeyEnrolled, federatedEnrolled, approvalEnrolled, cancellationToken);

			var now = _time.GetUtcNow().UtcDateTime;
			var secret = NewSecret();
			var departmentPolicy = await DepartmentPolicyAsync(request.DepartmentId, cancellationToken);
			var transaction = new MfaLoginTransaction
			{
				MfaLoginTransactionId = Guid.NewGuid().ToString(),
				SecretHash = Hash(secret),
				UserId = request.UserId,
				DepartmentId = request.DepartmentId,
				ClientApplication = (int)request.ClientApplication,
				ClientId = Limit(request.ClientId, 128),
				FirstFactorMethod = (int)request.FirstFactorMethod,
				FirstFactorVerifiedOnUtc = request.FirstFactorVerifiedOnUtc,
				DepartmentSsoConfigId = Limit(request.DepartmentSsoConfigId, 128),
				AuthenticationGeneration = request.AuthenticationGeneration,
				MfaPolicyVersion = departmentPolicy?.MfaPolicyVersion ?? 0,
				Scopes = Limit(string.Join(" ", (request.Scopes ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal)), 512),
				// What the member's Responder is shown if asked to approve: the session's own rule, decided now.
				SharedMode = SharedSessionRules.SourceFor(departmentPolicy, request.ClientApplication, request.SharedModeRequested,
					_gates.SharedDeviceModeEnabled) != SharedModeSource.None,
				InstallationLabel = Limit(request.InstallationLabel?.Trim(), 256),
				CreatedOnUtc = now,
				ExpiresOnUtc = now.Add(Lifetime),
				MaxAttempts = Math.Max(1, TwoFactorConfig.LoginMfaTransactionMaxAttempts),
				State = (int)MfaLoginTransactionState.Pending
			};
			await _transactions.InsertAsync(transaction, cancellationToken);

			return (new MfaLoginTransactionStart { Secret = secret, ExpiresInSeconds = (int)Lifetime.TotalSeconds, Choice = choice }, transaction);
		}

		public async Task<MfaLoginTransactionResult> OpenAsync(string secret, UserSessionClientApplication client, CancellationToken cancellationToken = default)
		{
			var lookup = await LookupAsync(secret, client, cancellationToken);
			if (lookup.Transaction == null)
				return lookup;

			var transaction = lookup.Transaction;
			var now = _time.GetUtcNow().UtcDateTime;
			var outcome = transaction.TransactionState switch
			{
				MfaLoginTransactionState.Pending when transaction.ExpiresOnUtc <= now => MfaLoginTransactionOutcome.Expired,
				MfaLoginTransactionState.Pending when transaction.Attempts >= transaction.MaxAttempts => MfaLoginTransactionOutcome.TooManyAttempts,
				MfaLoginTransactionState.Pending => MfaLoginTransactionOutcome.Usable,
				MfaLoginTransactionState.Exhausted => MfaLoginTransactionOutcome.TooManyAttempts,
				_ => MfaLoginTransactionOutcome.AlreadyUsed
			};
			if (outcome != MfaLoginTransactionOutcome.Usable)
				return MfaLoginTransactionResult.Of(outcome);

			return await CheckCurrentAsync(transaction, cancellationToken);
		}

		public async Task<bool> IsMethodAcceptedAsync(MfaLoginTransaction transaction, MfaEvidenceMethod method, CancellationToken cancellationToken = default)
		{
			// A recovery code always completes a login, as recovery (plan section 7.6 row 16); it never counts as MFA later.
			if (method == MfaEvidenceMethod.RecoveryCode)
				return true;
			if (method != MfaEvidenceMethod.Totp && method != MfaEvidenceMethod.Passkey && method != MfaEvidenceMethod.Federated &&
				method != MfaEvidenceMethod.PasskeyApproval)
				return false;

			return await _policy.IsMethodAcceptedAsync(transaction.DepartmentId, MfaMethodScope.Login, method, cancellationToken);
		}

		public async Task RecordFailedAttemptAsync(MfaLoginTransaction transaction, CancellationToken cancellationToken = default)
		{
			try
			{
				await _transactions.RecordFailedAttemptAsync(transaction.MfaLoginTransactionId, cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				// The failure is already refused and counted against the account lockout; log the missed count loudly.
				Logging.LogException(ex, "A failed login MFA attempt could not be counted against its transaction.");
			}
		}

		public Task<MfaLoginCompletion> CompleteAsync(MfaLoginTransaction transaction, MfaEvidenceMethod method, string factorReference,
			DateTime verifiedOnUtc, CancellationToken cancellationToken = default) =>
			CompleteAsync(transaction, (MfaEvidenceMethod?)method, factorReference, (DateTime?)verifiedOnUtc, cancellationToken);

		private async Task<MfaLoginCompletion> CompleteAsync(MfaLoginTransaction transaction, MfaEvidenceMethod? method, string factorReference,
			DateTime? verifiedOnUtc, CancellationToken cancellationToken)
		{
			var now = _time.GetUtcNow().UtcDateTime;
			var code = NewSecret();
			var completed = await _transactions.TryCompleteAsync(transaction.MfaLoginTransactionId, (int?)method, Limit(factorReference, 256),
				verifiedOnUtc, method == MfaEvidenceMethod.RecoveryCode, Hash(code), now.Add(CompletionLifetime), now, cancellationToken);

			// Exactly one completion per transaction: a concurrent winner, an expiry or exhaustion leaves nothing to issue.
			return completed
				? new MfaLoginCompletion { Outcome = MfaLoginTransactionOutcome.Usable, CompletionCode = code, ExpiresInSeconds = (int)CompletionLifetime.TotalSeconds }
				: new MfaLoginCompletion { Outcome = MfaLoginTransactionOutcome.AlreadyUsed };
		}

		public Task<bool> AbandonAsync(MfaLoginTransaction transaction, CancellationToken cancellationToken = default) =>
			_transactions.TryAbandonAsync(transaction.MfaLoginTransactionId, cancellationToken);

		public async Task<MfaLoginTransactionResult> RedeemAsync(string secret, string completionCode, UserSessionClientApplication client,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(completionCode) || completionCode.Length > MaxSecretLength)
				return MfaLoginTransactionResult.Of(MfaLoginTransactionOutcome.Invalid);

			var lookup = await LookupAsync(secret, client, cancellationToken);
			if (lookup.Transaction == null)
				return lookup;

			var transaction = lookup.Transaction;
			var now = _time.GetUtcNow().UtcDateTime;
			switch (transaction.TransactionState)
			{
				case MfaLoginTransactionState.Completed when transaction.CompletionExpiresOnUtc <= now:
					return MfaLoginTransactionResult.Of(MfaLoginTransactionOutcome.Expired);
				case MfaLoginTransactionState.Completed:
					break;
				case MfaLoginTransactionState.Pending:
					return MfaLoginTransactionResult.Of(MfaLoginTransactionOutcome.Invalid);
				default:
					// A lost token response is recovered by a new login; a completion is never issued twice (workbook 7.1).
					return MfaLoginTransactionResult.Of(MfaLoginTransactionOutcome.AlreadyUsed);
			}

			// The account and department are rechecked before the code is spent: a password change, revocation or policy
			// change since the first factor voids the login (plan section 5.2).
			var current = await CheckCurrentAsync(transaction, cancellationToken);
			if (!current.IsUsable)
				return current;

			// Provider MFA counts only under the tested mapping it was verified with; a mapping change since voids it.
			if (transaction.CompletionMethod == (int)MfaEvidenceMethod.Federated && !await FederatedMappingCurrentAsync(transaction, cancellationToken))
				return MfaLoginTransactionResult.Of(MfaLoginTransactionOutcome.PolicyChanged);

			// A Responder approval counts only while its passkey and Responder session do (plan section 7.9 revocation).
			if (transaction.CompletionMethod == (int)MfaEvidenceMethod.PasskeyApproval && !await ApproverStillValidAsync(transaction, cancellationToken))
				return MfaLoginTransactionResult.Of(MfaLoginTransactionOutcome.SessionRevoked);

			try
			{
				if (!await _transactions.TryRedeemAsync(transaction.MfaLoginTransactionId, Hash(completionCode), now, cancellationToken))
					return MfaLoginTransactionResult.Of(MfaLoginTransactionOutcome.Invalid);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "A login MFA completion could not be redeemed; the sign-in was refused.");
				return MfaLoginTransactionResult.Of(MfaLoginTransactionOutcome.Unavailable);
			}

			transaction.State = (int)MfaLoginTransactionState.Redeemed;
			transaction.RedeemedOnUtc = now;
			return MfaLoginTransactionResult.Of(MfaLoginTransactionOutcome.Usable, transaction);
		}

		/// <summary>The transaction for a secret, bound to the client that started it; nothing about it leaks on a mismatch.</summary>
		private async Task<MfaLoginTransactionResult> LookupAsync(string secret, UserSessionClientApplication client, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(secret) || secret.Length > MaxSecretLength)
				return MfaLoginTransactionResult.Of(MfaLoginTransactionOutcome.Invalid);

			MfaLoginTransaction transaction;
			try
			{
				transaction = await _transactions.GetBySecretHashAsync(Hash(secret), cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				// Authoritative state is unavailable: the login is refused, never treated as complete (plan section 3 item 4).
				Logging.LogException(ex, "Login MFA transaction lookup failed; the sign-in was refused.");
				return MfaLoginTransactionResult.Of(MfaLoginTransactionOutcome.Unavailable);
			}

			return transaction == null || transaction.ClientApplication != (int)client
				? MfaLoginTransactionResult.Of(MfaLoginTransactionOutcome.Invalid)
				: MfaLoginTransactionResult.Of(MfaLoginTransactionOutcome.Usable, transaction);
		}

		private async Task<MfaLoginTransactionResult> CheckCurrentAsync(MfaLoginTransaction transaction, CancellationToken cancellationToken)
		{
			try
			{
				var user = await _identityUsers.GetByIdAsync(transaction.UserId);
				if (user == null || user.AuthenticationGeneration != transaction.AuthenticationGeneration)
					return MfaLoginTransactionResult.Of(MfaLoginTransactionOutcome.SessionRevoked);

				if (await PolicyVersionAsync(transaction.DepartmentId, cancellationToken) != transaction.MfaPolicyVersion)
					return MfaLoginTransactionResult.Of(MfaLoginTransactionOutcome.PolicyChanged);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "Login MFA transaction state could not be rechecked; the sign-in was refused.");
				return MfaLoginTransactionResult.Of(MfaLoginTransactionOutcome.Unavailable);
			}

			return MfaLoginTransactionResult.Of(MfaLoginTransactionOutcome.Usable, transaction);
		}

		private async Task<bool> ApproverStillValidAsync(MfaLoginTransaction transaction, CancellationToken cancellationToken)
		{
			try
			{
				return await _approvals.IsApproverValidAsync(transaction.UserId, transaction.CompletionFactorReference, transaction.AuthenticationGeneration,
					cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "The approving Responder could not be rechecked; the sign-in was refused.");
				return false;
			}
		}

		private async Task<bool> FederatedMappingCurrentAsync(MfaLoginTransaction transaction, CancellationToken cancellationToken)
		{
			try
			{
				var config = transaction.DepartmentId is > 0
					? await _departmentSso.GetTestedFederatedMfaConfigAsync(transaction.DepartmentId.Value, cancellationToken)
					: null;
				return config != null && string.Equals(transaction.CompletionFactorReference,
					FederatedMfaMapping.FactorReferenceFor(config.DepartmentSsoConfigId, config.FederatedMfaMappingVersion), StringComparison.Ordinal);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "The provider step-up mapping could not be rechecked; the sign-in was refused.");
				return false;
			}
		}

		private async Task<long> PolicyVersionAsync(int? departmentId, CancellationToken cancellationToken) =>
			(await DepartmentPolicyAsync(departmentId, cancellationToken))?.MfaPolicyVersion ?? 0;

		private async Task<DepartmentSecurityPolicy> DepartmentPolicyAsync(int? departmentId, CancellationToken cancellationToken) =>
			departmentId is > 0 ? await _departmentSso.GetSecurityPolicyForDepartmentAsync(departmentId.Value, cancellationToken) : null;

		private static string NewSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

		private static byte[] Hash(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes(secret));

		private static string Limit(string value, int length) =>
			string.IsNullOrWhiteSpace(value) ? null : value.Length <= length ? value : value[..length];
	}
}
