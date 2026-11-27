namespace Resgrid.Config
{
	/// <summary>
	/// Training T5 content standards (LMS add-on): the caps a SCORM or cmi5 upload must stay inside, how long a launch stays valid, the LTI
	/// token lifetimes and the xAPI outbox retry policy. Packages live in the LMS media store (<see cref="LmsConfig"/>); with no store
	/// configured uploads are refused and nothing launches (fail closed).
	/// </summary>
	public static class LmsStandardsConfig
	{
		/// <summary>The largest package zip accepted (500 MB).</summary>
		public static long MaxPackageBytes = 500L * 1024 * 1024;

		/// <summary>The most files a package may hold.</summary>
		public static int MaxPackageFiles = 10000;

		/// <summary>The most a package may unpack to (2 GB).</summary>
		public static long MaxUncompressedBytes = 2L * 1024 * 1024 * 1024;

		/// <summary>The largest single file inside a package (1 GB).</summary>
		public static long MaxPackageFileBytes = 1024L * 1024 * 1024;

		/// <summary>An entry that unpacks to more than this many times its compressed size (and over 10 MB) is refused as a zip bomb.</summary>
		public static int MaxCompressionRatio = 200;

		/// <summary>How long a content launch token stays valid (the session in the player).</summary>
		public static int LaunchTokenHours = 8;

		/// <summary>How long an LTI login hint stays usable (single use).</summary>
		public static int LtiLaunchMinutes = 5;

		/// <summary>
		/// Round 10 A8: how long an in-app launch link (v4 Lms/GetLessonLaunch) stays usable before the app's web view opens it (single use).
		/// Clamped to 1–5 minutes.
		/// </summary>
		public static int AppLaunchMinutes = 5;

		/// <summary>
		/// Round 10 A8: the origins of the app builds that show the in-app launch page inside an iframe (Omni's web and desktop builds), separated
		/// by spaces, commas or semicolons: <c>https://host[:port]</c> (http only for localhost or a .local host) or an app's own scheme such as
		/// <c>app://bundle</c>. Only these may frame the launch page and the content an app launch runs, and only these receive the page's
		/// completion message from window.parent. Empty (the default): no app may frame it; a native web view (react-native-webview), which
		/// shows the page at the top level, needs nothing here. Example: <c>https://responder.resgrid.com app://bundle</c>.
		/// </summary>
		public static string AppFrameOrigins = "";

		/// <summary>How long an LTI id_token is valid.</summary>
		public static int LtiIdTokenMinutes = 5;

		/// <summary>How long an AGS access token is valid.</summary>
		public static int LtiAccessTokenMinutes = 60;

		/// <summary>A retired platform key stays in the published key set this long.</summary>
		public static int LtiRetiredKeyDays = 7;

		/// <summary>The most statements a cmi5 registration keeps (cmi5 keeps full results within this bound).</summary>
		public static int MaxCmi5StatementsPerRegistration = 5000;

		/// <summary>xAPI statements sent per LRS request.</summary>
		public static int XapiBatchSize = 50;

		/// <summary>Delivery attempts before a statement is marked failed.</summary>
		public static int XapiMaxAttempts = 12;

		/// <summary>The longest wait between two delivery attempts (minutes).</summary>
		public static int XapiMaxBackoffMinutes = 360;

		/// <summary>Delivered statements are purged after this many days.</summary>
		public static int XapiDeliveredRetentionDays = 30;

		/// <summary>Outbound requests (LRS, tool key sets) give up after this many seconds.</summary>
		public static int HttpTimeoutSeconds = 20;

		/// <summary>
		/// Mixed into the pseudonymous learner id (the LTI subject, the cmi5/xAPI account name, the SCORM learner id). Changing it changes
		/// every learner's id at every tool and LRS, so set it once.
		/// </summary>
		public static string LearnerIdPepper = "";
	}
}
