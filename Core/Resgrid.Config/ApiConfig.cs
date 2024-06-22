namespace Resgrid.Config
{
	/// <summary>
	/// Config settings for the operation of the Resgrid API
	/// </summary>
	public static class ApiConfig
	{
		/// <summary>
		/// Allowed Hostnames for CORS access. NOTE: CANNOT BE SET VIA THE CONFIG FILE.
		/// </summary>
		public const string CorsAllowedHostnames = "ionic://localhost, ionic://localhost:8080, http://localhost:8080, http://localhost, https://localhost, http://localhost:8100, http://localhost:8101, http://resgrid.com, https://resgrid.com, http://bigboard.resgrid.com, https://bigboard.resgrid.com, http://unit.resgrid.com, https://unit.resgrid.com, http://responder.resgrid.com, https://responder.resgrid.com, http://dispatch.resgrid.com, https://dispatch.resgrid.com, http://resgrid.local, https://resgrid.local, http://rgdevserver, https://rgdevserver, http://web.resgrid.local, https://web.resgrid.local, http://unit.resgrid.local, https://unit.resgrid.local, http://dispatch.resgrid.local, https://dispatch.resgrid.local, http://responder.resgrid.local, https://responder.resgrid.local, http://bigboard.resgrid.local, https://bigboard.resgrid.local";

		/// <summary>
		/// Allowed Methods (verbs) for CORS access. NOTE: CANNOT BE SET VIA THE CONFIG FILE.
		/// </summary>
		public const string CorsAllowedMethods = "GET,POST,PUT,DELETE,OPTIONS";

		/// <summary>
		/// Key used for authing with the backend internal apis
		/// </summary>
		public static string BackendInternalApikey = "";

		/// <summary>
		/// Used to bypass SSL checks, which is needed for self-signed certs
		/// </summary>
		public static bool BypassSslChecks = false;
	}
}
