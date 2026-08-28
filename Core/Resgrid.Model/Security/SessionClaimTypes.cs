namespace Resgrid.Model.Security
{
	public static class SessionClaimTypes
	{
		public const string SessionId = "sid";
		public const string AuthenticationGeneration = "auth_ver";
		public const string WebEventingOnly = "web_eventing_only";

		/// <summary>
		/// Numeric UserSessionClientApplication value of the application the session authenticated
		/// as. Lets the API step down unattended clients structurally (ADP plan section 7.3:
		/// BigBoard gets safe shells for protected departments). Tokens issued before this claim
		/// existed simply lack it and read as the default Api client.
		/// </summary>
		public const string ClientApp = "client_app";
	}
}
