namespace Resgrid.Config
{
	/// <summary>
	/// Settings for the web interface and features\functions that support the web UI.
	/// </summary>
	public static class WebConfig
	{
		/// <summary>
		/// The recaptcha private key
		/// </summary>
		public static string RecaptchaPrivateKey = "";

		/// <summary>
		/// The recaptcha public key
		/// </summary>
		public static string RecaptchaPublicKey = "";

		/// <summary>
		/// The ingress proxy network
		/// </summary>
		public static string IngressProxyNetwork = "10.42.0.0";

		/// <summary>
		/// The ingress proxy network cidr
		/// </summary>
		public static int IngressProxyNetworkCidr = 16;
	}
}
