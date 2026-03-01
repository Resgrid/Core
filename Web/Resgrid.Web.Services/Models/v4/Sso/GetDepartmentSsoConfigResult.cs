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

		/// <summary>Whether local username/password login is permitted in addition to SSO.</summary>
		public bool AllowLocalLogin { get; set; }

		/// <summary>Whether the department security policy requires SSO for all logins.</summary>
		public bool RequireSso { get; set; }

		/// <summary>Whether the department security policy requires MFA.</summary>
		public bool RequireMfa { get; set; }

		/// <summary>
		/// The redirect URI the mobile app must use for the OIDC authorization-code flow.
		/// Format: resgrid://auth/callback
		/// </summary>
		public string OidcRedirectUri { get; set; }

		/// <summary>
		/// OIDC scopes the mobile app should request (space-separated).
		/// e.g. "openid email profile offline_access"
		/// </summary>
		public string OidcScopes { get; set; }
	}
}

