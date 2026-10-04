namespace Resgrid.Config
{
	/// <summary>
	/// Browser and desktop push for every web client: Core Web (written into the page) and the web and
	/// Electron editions of the Responder, Unit, IC and Dispatch apps (served by v4 Config/GetConfig).
	/// Delivery goes through Novu's web FCM integrations (ChatConfig.Novu*WebFcmProviderId), so this has
	/// to be a Firebase web app in the Firebase project those integrations' service account belongs to.
	/// Every value is a public client identifier, none of them a secret. Web push stays off while any of
	/// the Firebase values or the VAPID key is empty.
	/// </summary>
	public static class WebPushConfig
	{
		public static string FirebaseApiKey = "";
		public static string FirebaseAuthDomain = "";
		public static string FirebaseProjectId = "";
		public static string FirebaseMessagingSenderId = "";
		public static string FirebaseAppId = "";

		/// <summary>The public half of the project's Web Push certificate (Firebase console, Cloud Messaging).</summary>
		public static string FirebaseVapidKey = "";

		/// <summary>
		/// Most browser/desktop tokens kept on one subscriber's web channel. Registering past the cap drops the
		/// oldest, which bounds the tokens a browser abandoned without signing out leaves behind.
		/// </summary>
		public static int MaxTokensPerSubscriber = 10;

		public static bool IsConfigured()
		{
			return !string.IsNullOrWhiteSpace(FirebaseApiKey) && !string.IsNullOrWhiteSpace(FirebaseProjectId) &&
				!string.IsNullOrWhiteSpace(FirebaseMessagingSenderId) && !string.IsNullOrWhiteSpace(FirebaseAppId) &&
				!string.IsNullOrWhiteSpace(FirebaseVapidKey);
		}
	}
}
