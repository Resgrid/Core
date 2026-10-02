using System;
using System.Security.Claims;

namespace Resgrid.Model.Security
{
	/// <summary>
	/// A server-brokered SSO sign-in (passkey plan section 7.7.2; workbook section 7.3). The server runs the IdP round trip,
	/// validates the result, and hands the client only a one-time <c>sso_code</c> at a registered return target; the client
	/// redeems it with its PKCE verifier. The row is the authority and every change is one compare-and-set. It stores
	/// hashes of the IdP state, nonce and code, the client's S256 challenge, and the IdP PKCE verifier encrypted; never an
	/// IdP token or assertion.
	/// </summary>
	public class SsoLoginTransaction
	{
		/// <summary>The step-up <see cref="Operation"/> that completes a password sign-in's login MFA transaction.</summary>
		public const string LoginOperation = "login";

		/// <summary>
		/// The step-up <see cref="Operation"/> that unlocks a locked shared session (plan section 12.5.3). It follows the
		/// sign-in rules, is bound to that session, and counts only if begun during the lock it answers.
		/// </summary>
		public const string SharedUnlockOperation = "shared_unlock";

		/// <summary>The public <c>sso_transaction_id</c>. Knowing it alone redeems nothing.</summary>
		public string SsoLoginTransactionId { get; set; }

		/// <summary>SHA-256 of the <c>state</c> (OIDC) or <c>RelayState</c> (SAML) sent to the IdP.</summary>
		public byte[] StateHash { get; set; }

		public int Purpose { get; set; }

		public int DepartmentId { get; set; }

		public string DepartmentSsoConfigId { get; set; }

		public int ProviderType { get; set; }

		public int ClientApplication { get; set; }

		/// <summary>Client-reported platform (ios, android, web, electron); a label only.</summary>
		public string Platform { get; set; }

		/// <summary>
		/// From a shared installation (plan section 12.5.2): the callback requires the provider's own sign-in to be fresh, so
		/// the next operator cannot ride on the last one's provider session.
		/// </summary>
		public bool SharedInstallation { get; set; }

		/// <summary>The registered return target the one-time code goes to.</summary>
		public string ReturnTarget { get; set; }

		/// <summary>The client's own CSRF value, echoed back on return.</summary>
		public string ClientState { get; set; }

		/// <summary>The client's PKCE S256 challenge; redemption must present its verifier.</summary>
		public string CodeChallenge { get; set; }

		/// <summary>SHA-256 of the OIDC nonce.</summary>
		public byte[] NonceHash { get; set; }

		/// <summary>The server's PKCE verifier toward the IdP, encrypted with the system key.</summary>
		public string EncryptedIdpCodeVerifier { get; set; }

		/// <summary>The SAML AuthnRequest ID the response must answer (<c>InResponseTo</c>).</summary>
		public string SamlRequestId { get; set; }

		/// <summary>For a step-up: the operation it serves (<c>MfaStepUpOperations</c>), or null for a login transaction.</summary>
		public string Operation { get; set; }

		/// <summary>For a step-up that completes a password sign-in: the login MFA transaction's row id.</summary>
		public string LoginTransactionId { get; set; }

		/// <summary>The provider step-up mapping version the request carried; null when none was requested.</summary>
		public long? FederatedMappingVersion { get; set; }

		/// <summary>The returned value the mapping counted as MFA (<c>kind:value</c>); null when there was none.</summary>
		public string FederatedMfaValue { get; set; }

		// Reauthentication and step-up bind to the signed-in session (or login transaction) and account they began from.
		public string SessionId { get; set; }
		public string ExpectedUserId { get; set; }
		public long? AuthenticationGeneration { get; set; }

		public DateTime CreatedOnUtc { get; set; }

		public DateTime ExpiresOnUtc { get; set; }

		public int State { get; set; }

		// Set by the one successful IdP callback.
		public string UserId { get; set; }

		/// <summary>When the IdP authenticated the user (<c>auth_time</c> or <c>AuthnInstant</c>), else the callback time.</summary>
		public DateTime? AuthenticatedOnUtc { get; set; }

		public byte[] CodeHash { get; set; }
		public DateTime? CodeExpiresOnUtc { get; set; }
		public DateTime? RedeemedOnUtc { get; set; }

		/// <summary>Value-free reason a callback failed, for audit.</summary>
		public string FailureCode { get; set; }

		public SsoTransactionPurpose TransactionPurpose => (SsoTransactionPurpose)Purpose;
		public SsoLoginTransactionState TransactionState => (SsoLoginTransactionState)State;
	}

	public enum SsoTransactionPurpose
	{
		Login = 1,

		/// <summary>Fresh first-factor proof for the signed-in session (plan sections 6.2 and 7.7.2 item 9).</summary>
		Reauthentication = 2,

		/// <summary>Provider step-up for an operation, or to complete a password sign-in (plan section 7.8).</summary>
		StepUp = 3,

		/// <summary>Provider step-up for a Protected Data Grant, redeemed only at <c>DataProtection/CompleteFederated</c>.</summary>
		AdpStepUp = 4,

		/// <summary>The managing member's test of a provider step-up mapping; it enables nothing until it succeeds.</summary>
		MappingTest = 5
	}

	public enum SsoLoginTransactionState
	{
		Pending = 0,

		/// <summary>The IdP result was validated and a one-time code issued.</summary>
		Authenticated = 1,

		Redeemed = 2,

		Failed = 3
	}

	public enum SsoBrokerOutcome
	{
		Succeeded = 0,
		Unavailable,
		InvalidRequest,
		ReturnTargetNotAllowed,
		TransactionInvalid,
		Expired,
		AlreadyUsed,
		VerificationFailed,
		IdentityMismatch,
		AccessDenied,
		ReauthenticationNotFresh,

		/// <summary>The provider's response carried no value the department's mapping counts as MFA.</summary>
		FederatedNotSatisfied,

		ServiceUnavailable
	}

	public static class SsoBrokerOutcomes
	{
		public static string ErrorCode(SsoBrokerOutcome outcome) => outcome switch
		{
			SsoBrokerOutcome.Unavailable => "sso_unavailable",
			SsoBrokerOutcome.InvalidRequest => "invalid_request",
			SsoBrokerOutcome.ReturnTargetNotAllowed => "return_target_not_allowed",
			SsoBrokerOutcome.TransactionInvalid => "sso_transaction_invalid",
			SsoBrokerOutcome.Expired => "sso_transaction_expired",
			SsoBrokerOutcome.AlreadyUsed => "sso_transaction_invalid",
			SsoBrokerOutcome.VerificationFailed => "sso_verification_failed",
			SsoBrokerOutcome.IdentityMismatch => "federated_identity_mismatch",
			SsoBrokerOutcome.AccessDenied => "access_denied",
			SsoBrokerOutcome.ReauthenticationNotFresh => "reauthentication_not_fresh",
			SsoBrokerOutcome.FederatedNotSatisfied => "federated_mfa_not_satisfied",
			SsoBrokerOutcome.ServiceUnavailable => "service_unavailable",
			_ => null
		};

		public static string PurposeName(SsoTransactionPurpose purpose) => purpose switch
		{
			SsoTransactionPurpose.Login => "login",
			SsoTransactionPurpose.Reauthentication => "reauth",
			SsoTransactionPurpose.StepUp => "step_up",
			SsoTransactionPurpose.AdpStepUp => "adp_step_up",
			SsoTransactionPurpose.MappingTest => "mapping_test",
			_ => null
		};

		public static SsoTransactionPurpose? PurposeFrom(string name) => (name ?? "login").Trim().ToLowerInvariant() switch
		{
			"login" => SsoTransactionPurpose.Login,
			"reauth" => SsoTransactionPurpose.Reauthentication,
			"step_up" => SsoTransactionPurpose.StepUp,
			"adp_step_up" => SsoTransactionPurpose.AdpStepUp,
			_ => null
		};
	}

	/// <summary>Everything <c>Sso/Begin</c> needs; the department is already resolved and the caller already known.</summary>
	public sealed class SsoBeginRequest
	{
		public int DepartmentId { get; init; }
		public string DepartmentCode { get; init; }
		public SsoTransactionPurpose Purpose { get; init; }
		public UserSessionClientApplication ClientApplication { get; init; }
		public string Platform { get; init; }
		public string ReturnTarget { get; init; }
		public string ClientState { get; init; }
		public string CodeChallenge { get; init; }
		public string CodeChallengeMethod { get; init; }

		// Reauthentication, step-up and mapping tests: the validated session asking.
		public string SessionId { get; init; }
		public string UserId { get; init; }
		public long? AuthenticationGeneration { get; init; }

		/// <summary>Step-up: the operation it serves, or <c>login</c> with <see cref="LoginTransactionId"/>.</summary>
		public string Operation { get; init; }

		/// <summary>Step-up that completes a password sign-in: the login MFA transaction (with <see cref="UserId"/>).</summary>
		public string LoginTransactionId { get; init; }

		/// <summary>
		/// A shared vehicle or workstation installation (plan section 12.5.2): the provider must authenticate the operator
		/// again (OIDC <c>prompt=login</c> with <c>max_age=0</c>; SAML <c>ForceAuthn</c>), and the callback refuses a provider
		/// sign-in that is not fresh, so the previous operator's provider session never signs the next one in.
		/// </summary>
		public bool SharedInstallation { get; init; }
	}

	public sealed class SsoBeginResult
	{
		public SsoBrokerOutcome Outcome { get; init; }
		public string AuthorizeUrl { get; init; }
		public string TransactionId { get; init; }
		public int ExpiresInSeconds { get; init; }

		public bool Succeeded => Outcome == SsoBrokerOutcome.Succeeded;

		public static SsoBeginResult Of(SsoBrokerOutcome outcome) => new() { Outcome = outcome };
	}

	/// <summary>Where the IdP callback sends the browser: the registered return target, with a code or an error.</summary>
	public sealed class SsoCallbackResult
	{
		public SsoBrokerOutcome Outcome { get; init; }

		/// <summary>Null when no trusted return target is known (an unknown or foreign state): the callback shows an error.</summary>
		public string RedirectUrl { get; init; }

		public bool Succeeded => Outcome == SsoBrokerOutcome.Succeeded;
	}

	public sealed class SsoRedemptionResult
	{
		public SsoBrokerOutcome Outcome { get; init; }
		public SsoLoginTransaction Transaction { get; init; }

		public bool Succeeded => Outcome == SsoBrokerOutcome.Succeeded;

		public static SsoRedemptionResult Of(SsoBrokerOutcome outcome, SsoLoginTransaction transaction = null) =>
			new() { Outcome = outcome, Transaction = transaction };
	}

	/// <summary>A validated IdP identity from a brokered response, with when the IdP authenticated the user if it said.</summary>
	public sealed class SsoIdentityAssertion
	{
		public ClaimsPrincipal Principal { get; init; }
		public DateTime? AuthenticatedAtUtc { get; init; }

		/// <summary>The assertion's AuthnContextClassRef values, for provider step-up mappings.</summary>
		public System.Collections.Generic.IReadOnlyCollection<string> AuthnContextClassRefs { get; init; }
	}
}
