namespace Resgrid.Model
{
	/// <summary>
	/// Defines the external identity provider (IdP) protocol used for SSO.
	/// </summary>
	public enum SsoProviderType
	{
		/// <summary>SAML 2.0 service-provider-initiated or IdP-initiated SSO.</summary>
		Saml2 = 0,

		/// <summary>OpenID Connect authorization-code flow.</summary>
		Oidc = 1
	}
}

