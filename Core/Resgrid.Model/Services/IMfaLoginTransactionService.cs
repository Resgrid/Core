using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Security;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// The method-neutral login MFA transaction (passkey plan sections 5.2, 7.2 and 7.5 rule 3). The first factor starts
	/// it; any accepted second factor (TOTP, a passkey bound to this app, or a recovery code) completes it once; the
	/// completion code is redeemed once at the token endpoint for the normal token response. The password or IdP token is
	/// never resent or kept. Verifying the factor itself is the caller's job; this service owns the transaction state.
	/// </summary>
	public interface IMfaLoginTransactionService
	{
		bool IsEnabled { get; }

		/// <summary>Starts a transaction and returns its secret once, with the methods it accepts.</summary>
		Task<MfaLoginTransactionStart> BeginAsync(MfaLoginTransactionRequest request, CancellationToken cancellationToken = default);

		/// <summary>
		/// The transaction for a secret, if it is still pending for this client, unexpired, under its attempt limit, and
		/// neither the account's generation nor the department's MFA policy has moved.
		/// </summary>
		Task<MfaLoginTransactionResult> OpenAsync(string secret, UserSessionClientApplication client, CancellationToken cancellationToken = default);

		/// <summary>Whether the department accepts <paramref name="method"/> for this login now (plan section 7.6 rows 1-4).</summary>
		Task<bool> IsMethodAcceptedAsync(MfaLoginTransaction transaction, MfaEvidenceMethod method, CancellationToken cancellationToken = default);

		Task RecordFailedAttemptAsync(MfaLoginTransaction transaction, CancellationToken cancellationToken = default);

		/// <summary>Records the verified factor and issues the one-use completion code.</summary>
		Task<MfaLoginCompletion> CompleteAsync(MfaLoginTransaction transaction, MfaEvidenceMethod method, string factorReference,
			DateTime verifiedOnUtc, CancellationToken cancellationToken = default);

		/// <summary>
		/// Starts a transaction that is already complete, for a login whose first factor needs no second factor (brokered
		/// SSO for an account without MFA, plan section 7.7.2 step 5). It is redeemed at the token endpoint like any other,
		/// so the session is created in one place, and it records no second factor.
		/// </summary>
		Task<MfaLoginCompletion> BeginCompletedAsync(MfaLoginTransactionRequest request, CancellationToken cancellationToken = default);

		/// <summary>
		/// As <see cref="BeginCompletedAsync(MfaLoginTransactionRequest, CancellationToken)"/>, for an SSO sign-in whose own
		/// round trip already satisfied MFA at the provider (plan section 7.8 flow 1): the completion records that method.
		/// </summary>
		Task<MfaLoginCompletion> BeginCompletedAsync(MfaLoginTransactionRequest request, MfaEvidenceMethod method, string factorReference,
			DateTime verifiedOnUtc, CancellationToken cancellationToken = default);

		/// <summary>Ends a pending transaction that will issue nothing, because factor recovery takes over from it (plan section 5.4).</summary>
		Task<bool> AbandonAsync(MfaLoginTransaction transaction, CancellationToken cancellationToken = default);

		/// <summary>Redeems the completion code once, rechecking the account generation and department policy.</summary>
		Task<MfaLoginTransactionResult> RedeemAsync(string secret, string completionCode, UserSessionClientApplication client,
			CancellationToken cancellationToken = default);
	}
}
