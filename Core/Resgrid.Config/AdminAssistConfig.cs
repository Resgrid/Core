namespace Resgrid.Config
{
	/// <summary>Host-owned bounds for deterministic administrative assistance. No inference settings.</summary>
	public static class AdminAssistConfig
	{
		public static int SnapshotTimeoutSeconds = 20;
		public static int MaxEvidenceRows = 2000;
		public static int EvidenceFreshnessSeconds = 60;
		public static int MaxHistoryPageSize = 100;
		public static int TraceRetentionDays = 90;
		public static int AggregateRetentionDays = 365;
		public static int PersonalLearningRetentionDays = 365;
		public static bool CaptureDispatchTraces = false;
		public static bool DrainDispatchTraceQueue = false;
		public static string TraceQueueName = "adminassisttraces-v1";
		public static bool SendAdminDigests = false;
	}
}
