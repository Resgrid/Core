namespace Resgrid.Config
{
	/// <summary>
	/// NERIS integration (RMS plan section 5.5, RMS-2). Submission is off unless a department has an enabled
	/// RmsNerisProfile AND this switch is on; Records is useful without NERIS (plan decision 21).
	/// Environment keys: RESGRID:NerisConfig:Enabled, :BaseUrl, :SandboxBaseUrl, :TimeoutSeconds, :MaxAttempts.
	/// </summary>
	public static class NerisConfig
	{
		/// <summary>Master switch for outbound NERIS calls in every process.</summary>
		public static bool Enabled = false;

		/// <summary>Production API root of the pinned contract.</summary>
		public static string BaseUrl = "https://api.neris.fsri.org/v1";

		/// <summary>Sandbox root used by sandbox profiles. Empty disables outbound requests; production is never a fallback.</summary>
		public static string SandboxBaseUrl = "";

		/// <summary>Contract version the provider was generated against (Providers/Resgrid.Providers.Neris/Contract).</summary>
		public static string ContractVersion = "1.4.78";

		public static int TimeoutSeconds = 30;

		/// <summary>Delivery attempts before a submission is marked Failed (trigger 111).</summary>
		public static int MaxAttempts = 5;

		/// <summary>Base backoff between attempts in minutes; doubled per attempt.</summary>
		public static int RetryBackoffMinutes = 5;

		/// <summary>Submissions one worker sweep claims.</summary>
		public static int BatchSize = 25;

		/// <summary>Lease held by a worker while it talks to the destination.</summary>
		public static int LeaseSeconds = 300;

		/// <summary>How often a submission awaiting the destination's review is polled for status.</summary>
		public static int StatusPollMinutes = 30;
	}
}
