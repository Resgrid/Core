namespace Resgrid.Config
{
	/// <summary>
	/// Configuration for OpenID Connect (https://documentation.openiddict.com/)
	/// </summary>
	public static class OidcConfig
	{
		public static string Key = "";

		public static string ConnectionString = "Server=rgdevserver;Database=ResgridOIDC;User Id=resgrid_app;Password=resgrid123;MultipleActiveResultSets=True;";

		public static int AccessTokenExpiryMinutes = 1440;

		public static int RefreshTokenExpiryDays = 30;

		public static string EncryptionCert = "";

		public static string SigningCert = "";
	}
}
