
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
		/// Relative URL path segment used to construct SAML SP Entity IDs.
		/// Example result: https://api.resgrid.com/saml/{configId}
		/// </summary>
		public static string SamlEntityIdBasePath = "/saml/";

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



