#pragma warning disable S2223 // Non-constant static fields should not be visible
#pragma warning disable CA2211 // Non-constant fields should not be visible
#pragma warning disable S1104 // Fields should not have public accessibility

namespace Resgrid.Config
{
	public static class OidcConfig
	{
		/// <summary>
		/// The underlying database engine for the OIDC database (Does not support Mongo)
		/// </summary>
		public static DatabaseTypes DatabaseType = DatabaseTypes.SqlServer;

		public static string Key = "";

		public static string ConnectionString = "Server=rgdevserver;Database=ResgridOIDC;User Id=resgrid_odic;Password=resgrid123;MultipleActiveResultSets=True;TrustServerCertificate=True;";

		public static int AccessTokenExpiryMinutes = 1440;

		public static int RefreshTokenExpiryDays = 365;

		public static int NonMobileRefreshTokenExpiryDays = 2;

		public static string EncryptionCert = "";

		public static string SigningCert = "";
	}
}

#pragma warning restore CA2211 // Non-constant fields should not be visible
#pragma warning restore S2223 // Non-constant static fields should not be visible
#pragma warning restore S1104 // Fields should not have public accessibility
