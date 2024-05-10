namespace Resgrid.Config
{
	/// <summary>
	/// Config settings for JWT's used in the website and api
	/// </summary>
	public static class JwtConfig
	{
		public static string Key = "";

		public static string Issuer = "resgrid.local";

		public static string Audience = "resgrid.local";

		public static int Duration = 30;

		public static string EventsClientId = "RGEvents";

		public static string EventsClientSecret = "";


	}
}
