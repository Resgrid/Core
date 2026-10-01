using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Security;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Server-brokered SSO for every client (passkey plan section 7.7.2; workbook section 7.3): begin, the IdP callbacks
	/// (OIDC and SAML), and one-time redemption with PKCE. It establishes the first factor only; login MFA follows through
	/// the login transaction, and nothing here issues a token.
	/// </summary>
	public interface ISsoBrokerService
	{
		/// <summary>Whether the gate is on and the return-target registry is valid.</summary>
		bool IsEnabled { get; }

		/// <summary>The OIDC redirect URI departments register with their IdP.</summary>
		string OidcRedirectUri { get; }

		/// <summary>Whether a department's configuration can run brokered SSO (a SAML IdP needs its SSO URL).</summary>
		bool SupportsBrokered(DepartmentSsoConfig config);

		/// <summary>The opaque, system-encrypted department token clients send instead of a department code.</summary>
		string DepartmentTokenFor(Department department);

		/// <summary>Resolves the department from a department token, a department code, or a username's default department.</summary>
		Task<Department> ResolveDepartmentAsync(string departmentToken, string departmentCode, string username, CancellationToken cancellationToken = default);

		Task<SsoBeginResult> BeginAsync(SsoBeginRequest request, CancellationToken cancellationToken = default);

		Task<SsoCallbackResult> CompleteOidcCallbackAsync(string state, string code, string error, string clientIpAddress,
			CancellationToken cancellationToken = default);

		/// <summary>True when a SAML RelayState belongs to a brokered transaction rather than the legacy relay.</summary>
		bool IsBrokeredRelayState(string relayState);

		/// <summary>
		/// Where a legacy (unbrokered) SAML sign-in starts: the department's IdP SSO URL with an AuthnRequest (signed when the
		/// department has an SP key) whose RelayState is the app's own tagged value, so the response returns through the legacy
		/// relay to that app. Nothing is stored; the exchange validates the response as it does any the relay carries. Null
		/// when the configuration cannot start one (the same pieces a brokered SAML sign-in needs) or the key cannot be used.
		/// <paramref name="forceAuthn"/> asks the IdP to authenticate the member again, for a shared installation whose browser
		/// may still hold the last operator's IdP session (plan section 12.5.2).
		/// </summary>
		string LegacySamlSignInUrl(DepartmentSsoConfig config, string departmentCode, string relayState, bool forceAuthn);

		Task<SsoCallbackResult> CompleteSamlCallbackAsync(string relayState, string samlResponse, string clientIpAddress,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Redeems the one-time code with the client's PKCE verifier, once, for the client that began it. Only a transaction
		/// for one of <paramref name="purposes"/> is redeemed (default: login and reauthentication, what <c>Sso/Redeem</c>
		/// serves), so a code sent to the wrong endpoint is refused without being spent.
		/// </summary>
		Task<SsoRedemptionResult> RedeemAsync(string transactionId, string code, string codeVerifier, UserSessionClientApplication client,
			CancellationToken cancellationToken = default, params SsoTransactionPurpose[] purposes);

		/// <summary>Records an id_token as used until it expires; false when it was already used (plan section 7.7.2 item 8).</summary>
		Task<bool> TryRecordIdTokenUseAsync(string idToken, DateTime expiresOnUtc, CancellationToken cancellationToken = default);
	}

	/// <summary>The deployment's registered SSO return targets, per client (workbook section 7.3).</summary>
	public interface ISsoReturnTargetRegistry
	{
		bool IsReady { get; }

		System.Collections.Generic.IReadOnlyList<string> Problems { get; }

		bool IsAllowed(UserSessionClientApplication client, string returnTarget);
	}
}
