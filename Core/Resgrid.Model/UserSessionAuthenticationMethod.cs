namespace Resgrid.Model
{
	public enum UserSessionAuthenticationMethod
	{
		LegacyUnknown = 0,
		LocalPassword = 1,
		OidcSso = 2,
		SamlSso = 3,
		Recovery = 4
	}
}
