namespace Resgrid.Config
{
	/// <summary>
	/// Configuration for working with external error tracking systems like Elk and Sentry
	/// </summary>
	public static class ExternalErrorConfig
	{
		/// <summary>
		/// The current operating enviorment for the code, i.e. prod, qa, dev
		/// </summary>
		public static string Environment = "dev";

		#region Elk Settings
		public static string ElkServiceUrl = "http://localhost:9200";
		#endregion Elk Settings

		#region Sentry Settings
		public static string ExternalErrorServiceUrlForApi = "";
		public static string ExternalErrorServiceUrlForWebsite = "";
		public static string ExternalErrorServiceUrlForWebjobs = "";
		public static string ExternalErrorServiceUrlForEventing = "";
		public static double SentryPerfSampleRate = 1.0;
		#endregion Sentry Settings

		#region Application Insights Settings
		public static bool ApplicationInsightsEnabled = false;
		public static string ApplicationInsightsInstrumentationKey = "";
		public static string ApplicationInsightsConnectionString = "";
		#endregion Application Insights Settings
	}

	public enum ErrorLoggerTypes
	{
		Elk,
		Sentry
	}
}
