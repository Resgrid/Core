namespace Resgrid.Web.Services.Models.v4.Sso
{
	/// <summary>
	/// Response for the SSO configuration discovery endpoint consumed by mobile apps.
	/// </summary>
	public class GetDepartmentSsoConfigResult : StandardApiResponseV4Base
	{
		/// <summary>Response data.</summary>
		public GetDepartmentSsoConfigResultData Data { get; set; }

		/// <summary>Default constructor.</summary>
		public GetDepartmentSsoConfigResult()
		{
			Data = new GetDepartmentSsoConfigResultData();
		}
	}

	/// <summary>
	/// SSO configuration data returned to mobile clients.
	/// Only non-secret fields are included — credentials are never exposed.
	/// </summary>
	public class GetDepartmentSsoConfigResultData
	{
		/// <summary>Whether SSO is enabled for this department.</summary>
		public bool SsoEnabled { get; set; }

		/// <summary>The SSO provider protocol ("saml2" or "oidc").</summary>
		public string ProviderType { get; set; }

		/// <summary>OIDC authority/issuer URL (OIDC only).</summary>
		public string Authority { get; set; }

		/// <summary>OIDC client ID (OIDC only — public client, safe to expose).</summary>
		public string ClientId { get; set; }

		/// <summary>SAML metadata URL (SAML only).</summary>
		public string MetadataUrl { get; set; }

		/// <summary>SAML entity ID / service-provider identifier (SAML only).</summary>
		public string EntityId { get; set; }

		/// <summary>
		/// Where an app opens a legacy (unbrokered) SAML sign-in, with <c>RelayState=&lt;app&gt;.&lt;nonce&gt;</c> added: this
		/// server's start page, which sends the browser to the department's IdP with an AuthnRequest. The response returns to
		/// the app through <c>connect/saml-mobile-callback</c>. SAML only, and only when the department's configuration can
		/// start one (its IdP SSO URL, entity ID, ACS URL and IdP certificate).
		/// </summary>
		public string SamlLoginUrl { get; set; }

		/// <summary>Whether local username/password login is permitted in addition to SSO.</summary>
		public bool AllowLocalLogin { get; set; }

		/// <summary>Whether the department security policy requires SSO for all logins.</summary>
		public bool RequireSso { get; set; }

		/// <summary>Whether the department security policy requires MFA.</summary>
		public bool RequireMfa { get; set; }

		/// <summary>
		/// The redirect URI the calling app uses for the legacy (unbrokered) OIDC authorization-code flow: its own scheme, by
		/// the app named in <c>X-Resgrid-Client</c> (for example <c>resgridunit://auth/callback</c>). A caller that names no
		/// app gets Responder's <c>resgrid://auth/callback</c>.
		/// </summary>
		public string OidcRedirectUri { get; set; }

		/// <summary>
		/// OIDC scopes the mobile app should request (space-separated).
		/// e.g. "openid email profile offline_access"
		/// </summary>
		public string OidcScopes { get; set; }

		/// <summary>The department, for clients that must name it again (for example Dispatch's department selection).</summary>
		public int? DepartmentId { get; set; }

		/// <summary>
		/// Opaque, system-encrypted department token: send it as <c>department_token</c> (legacy <c>external-token</c>) or
		/// <c>DepartmentToken</c> (<c>Sso/Begin</c>) instead of a department code or username.
		/// </summary>
		public string DepartmentToken { get; set; }

		/// <summary>True when this department can sign in through server-brokered SSO (<c>Sso/Begin</c>).</summary>
		public bool BrokeredSsoAvailable { get; set; }
	}
}

