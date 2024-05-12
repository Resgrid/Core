namespace Resgrid.Config
{
	public static class OidcConfig
	{
		public static string Key = "";

		/// <summary>
		///
		/// </summary>
		public static string ConnectionString = "Server=rgdevserver;Database=ResgridOIDC;User Id=resgrid_odic;Password=resgrid123;MultipleActiveResultSets=True;TrustServerCertificate=True;";

		public static int AccessTokenExpiryMinutes = 1440;

		public static int RefreshTokenExpiryDays = 365;

		public static int NonMobileRefreshTokenExpiryDays = 2;

		public static string EncryptionCert = "";

		public static string SigningCert = "";
	}
}
