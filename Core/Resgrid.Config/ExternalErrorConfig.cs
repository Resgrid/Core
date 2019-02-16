namespace Resgrid.Config
{
	public static class ExternalErrorConfig
	{
		#region Elk Settings
		public static string ElkServiceUrl = "http://localhost:9200";
		#endregion Elk Settings

		#region Sentry Settings
		public static string ExternalErrorServiceUrl = "";
		public static string ExternalErrorServiceUrlForWebsite = "";
		#endregion Sentry Settings
	}

	public enum ErrorLoggerTypes
	{
		Elk,
		Sentry
	}
}
