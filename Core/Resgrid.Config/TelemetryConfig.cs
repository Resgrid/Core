namespace Resgrid.Config
{
	public static class TelemetryConfig
	{
		public static string Exporter = "";

		public static TelemetryExporters ExporterType = TelemetryExporters.None;
		public static string PostHogUrl = "";
		public static string PostHogApiKey = "";

		public static string AptabaseUrl = "";
		public static string AptabaseWebApiKey = "";
		public static string AptabaseServicesApiKey = "";
		public static string AptabaseResponderApiKey = "";
		public static string AptabaseUnitApiKey = "";
		public static string AptabaseBigBoardApiKey = "";
		public static string AptabaseDispatchApiKey = "";

		public static string CountlyUrl = "";
		public static string CountlyWebKey = "";

		public static string GetAnalyticsKey()
		{
			if (ExporterType == TelemetryExporters.PostHog)
				return PostHogApiKey;
			else if (ExporterType == TelemetryExporters.Aptabase)
				return AptabaseWebApiKey;
			return string.Empty;
		}
	}

	public enum TelemetryExporters
	{
		None = 0,
		PostHog = 1,
		Aptabase = 2
	}
}
