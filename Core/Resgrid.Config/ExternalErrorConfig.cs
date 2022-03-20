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
		public static string ExternalErrorServiceUrl = "";
		public static string ExternalErrorServiceUrlForWebsite = "";
		public static string ExternalErrorServiceUrlForWebjobs = "";
		#endregion Sentry Settings
	}

	public enum ErrorLoggerTypes
	{
		Elk,
		Sentry
	}
}
