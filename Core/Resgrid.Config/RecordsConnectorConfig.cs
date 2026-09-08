namespace Resgrid.Config
{
	/// <summary>
	/// External ordering-system connectors for mutual-aid deployments (RMS plan section 4.1). Off unless this
	/// switch is on and a credential passphrase is set: a connector holds an outbound credential encrypted under
	/// the passphrase, so without one no connector can be created. Environment keys:
	/// RESGRID:RecordsConnectorConfig:Enabled, :CredentialPassphrase, :TimeoutSeconds, :MaxFeedBytes,
	/// :MinPollIntervalMinutes, :DefaultMaxRequestsPerHour, :AllowHttp.
	/// </summary>
	public static class RecordsConnectorConfig
	{
		/// <summary>Master switch for every connector in every process. Off means no poll, no push, no create.</summary>
		public static bool Enabled = false;

		/// <summary>Passphrase for the symmetric encryption of stored connector credentials. Empty refuses connector creation.</summary>
		public static string CredentialPassphrase = "";

		public static int TimeoutSeconds = 30;

		/// <summary>Largest feed page accepted; anything bigger is refused unread.</summary>
		public static int MaxFeedBytes = 5 * 1024 * 1024;

		/// <summary>A connector cannot poll faster than this, whatever it is configured to.</summary>
		public static int MinPollIntervalMinutes = 15;

		public static int DefaultMaxRequestsPerHour = 12;

		/// <summary>http feed roots are refused unless this is on (local development only).</summary>
		public static bool AllowHttp = false;

		/// <summary>Consecutive failures after which a connector is switched off until an administrator looks.</summary>
		public static int DisableAfterConsecutiveFailures = 10;

		/// <summary>Connector runs kept per department for the run log.</summary>
		public static int RunHistoryToKeep = 200;
	}
}
