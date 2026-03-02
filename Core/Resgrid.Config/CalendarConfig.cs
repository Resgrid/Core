namespace Resgrid.Config
{
	/// <summary>
	/// Configuration settings for the Calendar feature, including iCal feed export and external sync.
	/// Values can be overridden via ResgridConfig.json using keys like "CalendarConfig.ICalFeedEnabled".
	/// </summary>
	public static class CalendarConfig
	{
		/// <summary>
		/// Feature flag to enable or disable the public unauthenticated iCal feed endpoint.
		/// Set to false to prevent external calendar applications from subscribing.
		/// </summary>
		public static bool ICalFeedEnabled = true;

		/// <summary>
		/// The PRODID value written into generated iCal (RFC 5545) files.
		/// Format: -//Company//Product//Language
		/// </summary>
		public static string ICalProductId = "-//Resgrid//Calendar//EN";

		/// <summary>
		/// How long (in minutes) a subscribing calendar client may cache the feed response.
		/// This value is written into the X-WR-CACHETIME header on feed responses.
		/// </summary>
		public static int ICalFeedCacheDurationMinutes = 15;
	}
}

