
#pragma warning disable S2223 // Non-constant static fields should not be visible
#pragma warning disable CA2211 // Non-constant fields should not be visible
#pragma warning disable S1104 // Fields should not have public accessibility

namespace Resgrid.Config
{
	/// <summary>
	/// System-wide configuration for Single Sign-On (SSO), SAML 2.0, OIDC, and SCIM 2.0.
	/// All runtime URL paths and protocol-level constants used by the SSO/SCIM feature
	/// should be defined here rather than embedded inside services or providers.
	/// </summary>
	public static class SsoConfig
	{
		// ── SCIM 2.0 ─────────────────────────────────────────────────────────

		/// <summary>
		/// Relative path segment appended to <see cref="SystemBehaviorConfig.ResgridApiBaseUrl"/>
		/// to form the SCIM 2.0 connector base URL presented to identity providers.
		/// Example result: https://api.resgrid.com/scim/v2
		/// </summary>
		public static string ScimBasePath = "/scim/v2";

		/// <summary>
		/// Number of cryptographically random bytes used when generating a new SCIM bearer token.
		/// The resulting Base64-encoded token will be (ScimBearerTokenByteLength * 4/3) characters long.
		/// Default: 48 bytes → 64-character Base64 token.
		/// </summary>
		public static int ScimBearerTokenByteLength = 48;

		/// <summary>
		/// Compile-time constant for the HTTP header name that SCIM clients must include
		/// to identify the target department. Used in [FromHeader(Name = ...)] attributes.
		/// If you need to change this value, also update <see cref="ScimDepartmentIdHeaderName"/>.
		/// </summary>
		public const string ScimDepartmentIdHeader = "X-Department-Id";

		/// <summary>
		/// Runtime-configurable HTTP header name that SCIM clients must include to identify
		/// the target department when the bearer token alone is insufficient for routing
		/// (e.g. Microsoft Entra ID). Defaults to <see cref="ScimDepartmentIdHeader"/>.
		/// </summary>
		public static string ScimDepartmentIdHeaderName = ScimDepartmentIdHeader;

		// ── SSO / OIDC ────────────────────────────────────────────────────────

		/// <summary>
		/// Relative URL path for the SSO discovery endpoint that mobile apps call to
		/// retrieve department SSO settings before showing the login screen.
		/// A <c>departmentToken</c> query parameter is appended at runtime.
		/// Example result: https://api.resgrid.com/api/v4/connect/sso-config
		/// </summary>
		public static string SsoDiscoveryPath = "/api/v4/connect/sso-config";

		/// <summary>
		/// Relative URL path for the SAML 2.0 Assertion Consumer Service (ACS) used
		/// by mobile clients. A <c>departmentToken</c> query parameter is appended at runtime.
		/// Example result: https://api.resgrid.com/api/v4/connect/saml-mobile-callback
		/// </summary>
		public static string SamlAcsPath = "/api/v4/connect/saml-mobile-callback";

		/// <summary>
		/// Relative URL path of the page that starts a legacy (unbrokered) SAML sign-in for an app: it sends the browser to the
		/// department's IdP with an AuthnRequest. Discovery names it, with the department token, as <c>SamlLoginUrl</c>.
		/// Example result: https://api.resgrid.com/api/v4/connect/saml-mobile-login
		/// </summary>
		public static string SamlLoginPath = "/api/v4/connect/saml-mobile-login";

		/// <summary>
		/// Relative URL path segment used to construct SAML SP Entity IDs.
		/// Example result: https://api.resgrid.com/saml/{configId}
		/// </summary>
		public static string SamlEntityIdBasePath = "/saml/";

		// ── Server-brokered SSO (passkey plan section 7.7.2; workbook section 7.3) ──

		/// <summary>
		/// Rollout gate for server-brokered SSO (<c>Sso/Begin</c>, the OIDC callback and brokered SAML, <c>Sso/Redeem</c>).
		/// Off: those endpoints refuse, and the legacy client-run OIDC flow, SAML relay and <c>external-token</c> are unchanged.
		/// </summary>
		public static bool BrokeredSsoEnabled = false;

		/// <summary>
		/// Relative path of the OIDC redirect URI every department registers with its IdP for brokered SSO, appended to
		/// <see cref="SystemBehaviorConfig.ResgridApiBaseUrl"/>. Example result: https://api.resgrid.com/api/v4/connect/oidc-callback
		/// </summary>
		public static string OidcCallbackPath = "/api/v4/connect/oidc-callback";

		/// <summary>
		/// The deployment's return-target registry: where the server may send the one-time <c>sso_code</c>, per client,
		/// matched exactly. Entries are <c>client=target,target</c> separated by ";", where client is web, responder, unit,
		/// dispatch or ic. A target is an https URL, an app's own custom scheme (never shared between apps), or an RFC 8252
		/// loopback redirect written <c>http://127.0.0.1:*/path</c> (any port). Example:
		/// <c>web=https://app.resgrid.com/Account/SsoReturn;unit=resgridunit://sso-return,https://unit.resgrid.com/sso-return,http://127.0.0.1:*/sso-return</c>.
		/// Empty means brokered SSO has nowhere to return and is unavailable.
		/// </summary>
		public static string BrokeredReturnTargets = "";

		/// <summary>
		/// Where each app's web build is served, for the SSO pages' list of redirect URIs a department registers with its IdP
		/// (a web build's legacy OIDC sign-in returns to its own page: <c>/auth/callback</c>, or <c>/login/sso</c> for Dispatch).
		/// Entries are <c>client=origin</c> separated by ";", where client is responder, unit, dispatch or ic and origin is
		/// <c>https://host[:port]</c> (http only for localhost or a .local host). An app with no entry has no web build here.
		/// Defaults to the development hosts, like the other URL settings. The hosted US service's web editions are
		/// <c>responder=https://responder.resgrid.com;unit=https://unit.resgrid.com;dispatch=https://dispatch.resgrid.com</c>;
		/// a region lists its own hosts once it serves web editions. Only shown to admins: nothing is redirected by it.
		/// </summary>
		public static string AppWebOrigins = "responder=https://responder.resgrid.local;unit=https://unit.resgrid.local;dispatch=https://dispatch.resgrid.local";

		/// <summary>How long a brokered SSO transaction waits for the IdP. Non-sliding.</summary>
		public static int BrokeredTransactionLifetimeSeconds = 600;

		/// <summary>How long the one-time <c>sso_code</c> can be redeemed.</summary>
		public static int BrokeredCodeLifetimeSeconds = 60;

		/// <summary>
		/// For SSO reauthentication: the most time the IdP's own sign-in (<c>auth_time</c> or <c>AuthnInstant</c>) may
		/// predate the callback. Sent as OIDC <c>max_age</c>; SAML sends <c>ForceAuthn</c>.
		/// </summary>
		public static int ReauthenticationMaxAgeSeconds = 300;

		// ── Feature flags ─────────────────────────────────────────────────────

		/// <summary>
		/// When <c>true</c>, the SSO / SCIM feature is available for departments to configure.
		/// When <c>false</c>, the SSO / SCIM navigation entries and controllers are hidden
		/// system-wide regardless of department configuration.
		/// </summary>
		public static bool SsoFeatureEnabled = true;

		/// <summary>
		/// When <c>true</c>, SCIM 2.0 provisioning endpoints are enabled system-wide.
		/// When <c>false</c>, SCIM endpoints return HTTP 503 regardless of department config.
		/// </summary>
		public static bool ScimFeatureEnabled = true;

		/// <summary>
		/// When <c>true</c>, the system enforces IP-range restrictions from
		/// DepartmentSecurityPolicy.AllowedIpRanges on every login attempt.
		/// Disable in development environments where NAT/proxy addresses are unpredictable.
		/// </summary>
		public static bool IpRangeEnforcementEnabled = true;
	}
}

#pragma warning restore CA2211 // Non-constant fields should not be visible
#pragma warning restore S2223 // Non-constant static fields should not be visible
#pragma warning restore S1104 // Fields should not have public accessibility



