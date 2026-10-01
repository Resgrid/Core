using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Services.Models.v4.Sso
{
	// ── Result wrappers ───────────────────────────────────────────────────────

	/// <summary>Response wrapper for a list of SSO configurations.</summary>
	public class GetSsoConfigsResult : StandardApiResponseV4Base
	{
		/// <summary>List of SSO configurations for the department.</summary>
		public List<SsoConfigSummaryData> Data { get; set; } = new();
	}

	/// <summary>Response wrapper for a single SSO configuration.</summary>
	public class GetSsoConfigResult : StandardApiResponseV4Base
	{
		/// <summary>The SSO configuration.</summary>
		public SsoConfigDetailData Data { get; set; }
	}

	/// <summary>Response wrapper for a save (create/update) operation.</summary>
	public class SaveSsoConfigResult : StandardApiResponseV4Base
	{
		/// <summary>The ID of the saved SSO configuration.</summary>
		public string DepartmentSsoConfigId { get; set; }
	}

	/// <summary>Response wrapper for the department security policy.</summary>
	public class GetSecurityPolicyResult : StandardApiResponseV4Base
	{
		/// <summary>The department security policy.</summary>
		public SecurityPolicyData Data { get; set; }
	}

	/// <summary>Response wrapper for a save security policy operation.</summary>
	public class SaveSecurityPolicyResult : StandardApiResponseV4Base
	{
		/// <summary>The ID of the saved security policy.</summary>
		public int DepartmentSecurityPolicyId { get; set; }
	}

	/// <summary>Generic success/failure result.</summary>
	public class SsoOperationResult : StandardApiResponseV4Base
	{
		/// <summary>Whether the operation succeeded.</summary>
		public bool Success { get; set; }
	}

	/// <summary>Response for SCIM token rotation.</summary>
	public class RotateScimTokenResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// The new plaintext SCIM bearer token. Store this immediately — it will
		/// never be returned again after this response.
		/// </summary>
		public string ScimBearerToken { get; set; }
	}

	// ── Data objects (read) ───────────────────────────────────────────────────

	/// <summary>Summary of an SSO configuration (no secrets).</summary>
	public class SsoConfigSummaryData
	{
		/// <summary>Unique identifier of this SSO configuration.</summary>
		public string DepartmentSsoConfigId { get; set; }

		/// <summary>SSO provider protocol: "saml2" or "oidc".</summary>
		public string ProviderType { get; set; }

		/// <summary>Whether this SSO configuration is currently active.</summary>
		public bool IsEnabled { get; set; }

		/// <summary>The OIDC client ID or SAML entity ID (non-secret).</summary>
		public string Identifier { get; set; }

		/// <summary>OIDC authority URL or SAML metadata URL.</summary>
		public string EndpointUrl { get; set; }

		/// <summary>Whether local (password) login is permitted alongside SSO.</summary>
		public bool AllowLocalLogin { get; set; }

		/// <summary>Whether auto-provisioning of new users from IdP claims is enabled.</summary>
		public bool AutoProvisionUsers { get; set; }

		/// <summary>Whether SCIM 2.0 provisioning is enabled.</summary>
		public bool ScimEnabled { get; set; }

		/// <summary>Date/time the configuration was created.</summary>
		public DateTime CreatedOn { get; set; }

		/// <summary>Date/time the configuration was last updated.</summary>
		public DateTime? UpdatedOn { get; set; }
	}

	/// <summary>Full SSO configuration detail (no secrets returned).</summary>
	public class SsoConfigDetailData : SsoConfigSummaryData
	{
		/// <summary>OIDC authority / issuer URL.</summary>
		public string Authority { get; set; }

		/// <summary>OAuth 2.0 / OIDC client ID.</summary>
		public string ClientId { get; set; }

		/// <summary>SAML metadata URL.</summary>
		public string MetadataUrl { get; set; }

		/// <summary>SAML entity ID registered with the IdP.</summary>
		public string EntityId { get; set; }

		/// <summary>SAML Assertion Consumer Service URL.</summary>
		public string AssertionConsumerServiceUrl { get; set; }

		/// <summary>The IdP's SAML single sign-on URL that brokered sign-in sends its AuthnRequest to.</summary>
		public string IdpSsoUrl { get; set; }

		/// <summary>
		/// The OIDC redirect URI to register with the IdP for brokered sign-in (plan section 7.7.2 item 7). It is added
		/// alongside any existing registration, which older app builds keep using.
		/// </summary>
		public string OidcBrokerRedirectUri { get; set; }

		/// <summary>
		/// Each app's own redirect URIs for sign-in the app runs itself (older app versions, or while brokered sign-in is off):
		/// its native scheme, and its web edition's page where this deployment serves one. The department registers every one
		/// with its IdP; an app can only receive a redirect on an address it owns. OIDC only.
		/// </summary>
		public List<SsoAppRedirectUriData> OidcAppRedirectUris { get; set; }

		/// <summary>
		/// JSON attribute mapping from IdP claim names to Resgrid user fields.
		/// Example: {"email":"http://schemas/.../emailaddress","firstName":"given_name","lastName":"family_name"}
		/// </summary>
		public string AttributeMappingJson { get; set; }

		/// <summary>Default rank ID assigned to auto-provisioned users (null = no default rank).</summary>
		public int? DefaultRankId { get; set; }

		/// <summary>Whether a client secret is currently stored (true = secret exists, never returned).</summary>
		public bool HasClientSecret { get; set; }

		/// <summary>Whether an IdP certificate is currently stored.</summary>
		public bool HasIdpCertificate { get; set; }

		/// <summary>Whether a signing certificate is currently stored.</summary>
		public bool HasSigningCertificate { get; set; }

		/// <summary>Whether a SCIM bearer token is currently stored.</summary>
		public bool HasScimBearerToken { get; set; }
	}

	/// <summary>One of an app's legacy OIDC redirect URIs.</summary>
	public class SsoAppRedirectUriData
	{
		/// <summary>The app, as <c>X-Resgrid-Client</c> names it: responder, unit, dispatch or ic.</summary>
		public string Client { get; set; }

		public string DisplayName { get; set; }

		/// <summary>True for the app's web edition (a page on its web host), false for the native app's own scheme.</summary>
		public bool Web { get; set; }

		public string RedirectUri { get; set; }
	}

	/// <summary>Department security policy data.</summary>
	public class SecurityPolicyData
	{
		/// <summary>Database ID of the security policy (0 = not yet saved).</summary>
		public int DepartmentSecurityPolicyId { get; set; }

		/// <summary>Whether all department members must use MFA.</summary>
		public bool RequireMfa { get; set; }

		/// <summary>Whether password-based login is disabled (SSO-only mode).</summary>
		public bool RequireSso { get; set; }

		/// <summary>Idle session timeout in minutes (0 = system default).</summary>
		public int SessionTimeoutMinutes { get; set; }

		/// <summary>Maximum concurrent sessions per user (0 = unlimited).</summary>
		public int MaxConcurrentSessions { get; set; }

		/// <summary>Comma-separated CIDR blocks from which login is permitted. Empty = no restriction.</summary>
		public string AllowedIpRanges { get; set; }

		/// <summary>Password expiration in days (0 = disabled).</summary>
		public int PasswordExpirationDays { get; set; }

		/// <summary>Minimum required password length.</summary>
		public int MinPasswordLength { get; set; }

		/// <summary>Whether passwords must meet complexity requirements.</summary>
		public bool RequirePasswordComplexity { get; set; }

		/// <summary>Data classification level: 0=Unclassified, 1=CUI, 2=Confidential.</summary>
		public int DataClassificationLevel { get; set; }

		/// <summary>Accept a passkey as sign-in and step-up MFA.</summary>
		public bool AllowPasskeysForLoginMfa { get; set; }

		/// <summary>Accept a passkey for protected data.</summary>
		public bool AllowPasskeysForAdp { get; set; }

		/// <summary>Accept provider step-up for sign-in and step-up MFA.</summary>
		public bool AllowFederatedMfaForLoginMfa { get; set; }

		/// <summary>Accept provider step-up for protected data.</summary>
		public bool AllowFederatedMfaForAdp { get; set; }

		/// <summary>Accept Responder approval.</summary>
		public bool AllowResponderApproval { get; set; }

		/// <summary>Let recent sign-in MFA serve protected data.</summary>
		public bool AcceptRecentLoginMfaForAdp { get; set; }

		/// <summary>Let fresh unlock MFA serve protected data.</summary>
		public bool AcceptRecentUnlockMfaForAdp { get; set; }

		/// <summary>Minutes of no operator activity before a shared session locks.</summary>
		public int SharedIdleLockMinutes { get; set; }

		/// <summary>Hours after sign-in when a shared session ends.</summary>
		public int SharedShiftHours { get; set; }

		/// <summary>Apps whose sessions are always shared (flags: Unit 1, IC 2, Dispatch 4).</summary>
		public int SharedModeRequiredApps { get; set; }

		/// <summary>Advanced by the server whenever the sign-in MFA rules change; read-only.</summary>
		public long MfaPolicyVersion { get; set; }

		/// <summary>Date/time the policy was created.</summary>
		public DateTime CreatedOn { get; set; }

		/// <summary>Date/time the policy was last updated.</summary>
		public DateTime? UpdatedOn { get; set; }
	}

	// ── Input DTOs (write — secrets accepted but never echoed back) ───────────

	/// <summary>Input for creating or updating an SSO configuration.</summary>
	public class SaveSsoConfigInput
	{
		/// <summary>Provider type: "saml2" or "oidc".</summary>
		[Required]
		public string ProviderType { get; set; }

		/// <summary>Whether this SSO configuration should be active.</summary>
		public bool IsEnabled { get; set; }

		// ── OIDC fields ───────────────────────────────────────────────────────

		/// <summary>OIDC client identifier registered with the IdP.</summary>
		public string ClientId { get; set; }

		/// <summary>
		/// OIDC client secret (plaintext). Encrypted before storage.
		/// Omit or send null to leave an existing secret unchanged.
		/// </summary>
		public string ClientSecret { get; set; }

		/// <summary>OIDC authority / issuer URL (e.g. https://login.microsoftonline.com/{tenant}/v2.0).</summary>
		public string Authority { get; set; }

		// ── SAML 2.0 fields ───────────────────────────────────────────────────

		/// <summary>SAML IdP metadata URL.</summary>
		public string MetadataUrl { get; set; }

		/// <summary>SAML SP entity ID.</summary>
		public string EntityId { get; set; }

		/// <summary>SAML Assertion Consumer Service URL.</summary>
		public string AssertionConsumerServiceUrl { get; set; }

		/// <summary>The IdP's SAML single sign-on URL (HTTP-Redirect binding) for brokered sign-in. Optional; https.</summary>
		public string IdpSsoUrl { get; set; }

		/// <summary>
		/// IdP public certificate in PEM format (plaintext). Encrypted before storage.
		/// Omit or send null to leave an existing certificate unchanged.
		/// </summary>
		public string IdpCertificate { get; set; }

		/// <summary>
		/// SP signing certificate private key in PEM format (plaintext). Encrypted before storage.
		/// Omit or send null to leave an existing key unchanged.
		/// </summary>
		public string SigningCertificate { get; set; }

		// ── Shared / provisioning fields ──────────────────────────────────────

		/// <summary>JSON attribute mapping from IdP claims to Resgrid fields.</summary>
		public string AttributeMappingJson { get; set; }

		/// <summary>Whether local username/password login is permitted alongside SSO.</summary>
		public bool AllowLocalLogin { get; set; } = true;

		/// <summary>Whether new users arriving via SSO should be auto-provisioned.</summary>
		public bool AutoProvisionUsers { get; set; }

		/// <summary>Default rank ID for auto-provisioned users.</summary>
		public int? DefaultRankId { get; set; }

		/// <summary>Whether SCIM 2.0 provisioning is enabled.</summary>
		public bool ScimEnabled { get; set; }
	}

	/// <summary>Input for updating the department security policy.</summary>
	public class SaveSecurityPolicyInput
	{
		/// <summary>Whether all department members must use MFA.</summary>
		public bool RequireMfa { get; set; }

		/// <summary>Whether password-based login is disabled (SSO-only mode).</summary>
		public bool RequireSso { get; set; }

		/// <summary>Idle session timeout in minutes (0 = system default).</summary>
		[Range(0, 10080)]
		public int SessionTimeoutMinutes { get; set; }

		/// <summary>Maximum concurrent sessions per user (0 = unlimited).</summary>
		[Range(0, 100)]
		public int MaxConcurrentSessions { get; set; }

		/// <summary>Comma-separated CIDR blocks (e.g. "10.0.0.0/8,192.168.1.0/24"). Empty = no restriction.</summary>
		public string AllowedIpRanges { get; set; }

		/// <summary>Password expiration in days (0 = disabled).</summary>
		[Range(0, 3650)]
		public int PasswordExpirationDays { get; set; }

		/// <summary>Minimum required password length (0 = system default).</summary>
		[Range(0, 128)]
		public int MinPasswordLength { get; set; }

		/// <summary>Whether passwords must meet complexity requirements.</summary>
		public bool RequirePasswordComplexity { get; set; }

		/// <summary>Data classification level: 0=Unclassified, 1=CUI, 2=Confidential.</summary>
		[Range(0, 2)]
		public int DataClassificationLevel { get; set; }

		// Second-factor method switches (passkey plan section 10.1). Null keeps the stored value, so a client that does not
		// know these fields never switches anything off. Changing any of them requires the managing member.

		/// <summary>Accept a passkey bound to the requesting app as sign-in and step-up MFA. TOTP is always accepted.</summary>
		public bool? AllowPasskeysForLoginMfa { get; set; }

		/// <summary>Accept a passkey for Protected Data Grants, ADP management and protected workflows.</summary>
		public bool? AllowPasskeysForAdp { get; set; }

		/// <summary>Accept provider step-up for sign-in and step-up MFA (needs a tested mapping).</summary>
		public bool? AllowFederatedMfaForLoginMfa { get; set; }

		/// <summary>Accept provider step-up for protected data (needs a tested mapping).</summary>
		public bool? AllowFederatedMfaForAdp { get; set; }

		/// <summary>Accept Responder approval where the matching passkey switch is on.</summary>
		public bool? AllowResponderApproval { get; set; }

		/// <summary>Let recent same-session sign-in MFA serve protected data without another verification.</summary>
		public bool? AcceptRecentLoginMfaForAdp { get; set; }

		/// <summary>Let fresh same-operator unlock MFA serve protected data on a shared session.</summary>
		public bool? AcceptRecentUnlockMfaForAdp { get; set; }

		/// <summary>Shared sessions lock after this many idle minutes (1-15). Managing member only; omitted leaves it unchanged.</summary>
		public int? SharedIdleLockMinutes { get; set; }

		/// <summary>Shared sessions end this many hours after sign-in (1-24). Managing member only.</summary>
		public int? SharedShiftHours { get; set; }

		/// <summary>
		/// The apps whose sessions are always shared: flags Unit 1, IC 2, Dispatch 4. Sign-ins that do not name their app count
		/// too. Managing member only; a new app needs the deployment's shared-device mode.
		/// </summary>
		public int? SharedModeRequiredApps { get; set; }
	}
}

