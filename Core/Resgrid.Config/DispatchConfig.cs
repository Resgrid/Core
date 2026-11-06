namespace Resgrid.Config
{
	/// <summary>System-wide dispatch behaviour.</summary>
	public static class DispatchConfig
	{
		/// <summary>
		/// Runs the dispatch re-notify sweep (worker 78). Departments still choose whether they re-notify (Dispatch Settings,
		/// off by default); turning this off stops every re-notification without touching their settings.
		/// </summary>
		public static bool RenotifyEnabled = true;

		/// <summary>
		/// Locks the re-notify interval and retry count at <c>DispatchRenotifyConfig.DefaultIntervalMinutes</c> (5) and
		/// <c>DefaultMaxRetries</c> (3) for system stability. While true the settings page shows them read-only, a posted value is
		/// ignored and the sweep uses the defaults whatever is stored.
		/// </summary>
		public static bool RenotifyTimingLocked = true;

		/// <summary>
		/// Locks re-notification to push for system stability. Text message, email, voice call and chat platform re-notification
		/// are built, but while this is true the settings page shows them unticked and disabled, the Dispatch Settings save and
		/// DepartmentSettingsService store push only whatever is posted, and the sweep sends push only whatever is stored.
		/// </summary>
		public static bool RenotifyChannelsLocked = true;

		/// <summary>The most due rows one sweep handles; the rest wait for the next minute.</summary>
		public static int RenotifySweepBatchSize = 500;

		/// <summary>Days a finished re-notification row is kept before the sweep purges it.</summary>
		public static int RenotifyRetentionDays = 30;
	}
}
