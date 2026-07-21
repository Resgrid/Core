namespace Resgrid.Config
{
	/// <summary>Operational and security limits for the live Incident Command system.</summary>
	public static class IncidentCommandConfig
	{
		/// <summary>Maximum size of an incident attachment in bytes (25 MiB by default).</summary>
		public static int MaxAttachmentBytes = 25 * 1024 * 1024;

		/// <summary>Maximum status-note body length.</summary>
		public static int MaxNoteLength = 16000;

		/// <summary>How many hourly forecast periods the commander app receives by default.</summary>
		public static int ForecastHours = 24;
	}
}
