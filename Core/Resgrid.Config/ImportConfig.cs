namespace Resgrid.Config
{
	/// <summary>
	/// Department Import (Import &amp; Migration) host configuration (wizard plan sections 8.7, 13 and 14). Artifacts live only
	/// in the RustFS (S3-compatible) object store, in a bucket separate from the search bucket; an empty endpoint disables
	/// uploads (fail closed). Environment keys follow RESGRID__ImportConfig__{Field}.
	/// </summary>
	public static class ImportConfig
	{
		// ---- Artifact store (RustFS / any S3-compatible endpoint). Empty endpoint = uploads disabled. --------------

		/// <summary>S3-compatible endpoint, e.g. https://rustfs.internal:9000. Empty disables uploads and extraction.</summary>
		public static string S3Endpoint = "";

		public static string S3AccessKey = "";

		public static string S3SecretKey = "";

		/// <summary>Import bucket; separate from SearchConfig.S3Bucket so lifecycle and access policies stay independent.</summary>
		public static string S3Bucket = "resgrid-imports";

		public static string S3Region = "us-east-1";

		public static bool S3UseSsl = true;

		public static bool S3ForcePathStyle = true;

		/// <summary>Key prefix inside the bucket; objects live under {S3Prefix}/{departmentId}/{workspaceId}/{artifactId}.</summary>
		public static string S3Prefix = "imports";

		/// <summary>Plaintext bytes per upload chunk, encryption segment and multipart part (8 MiB; S3 parts must be at least 5 MiB).</summary>
		public static int ChunkSizeBytes = 8 * 1024 * 1024;

		// ---- Limits (decision D2) ----------------------------------------------------------------------------------

		public static long FreePlanMaxArtifactBytes = 50L * 1024 * 1024;
		public static long PaidPlanMaxArtifactBytes = 200L * 1024 * 1024;
		public static long FreePlanMaxPackageBytes = 200L * 1024 * 1024;
		public static long PaidPlanMaxPackageBytes = 1024L * 1024 * 1024;
		public static long FreePlanMaxWorkspaceBytes = 500L * 1024 * 1024;
		public static long PaidPlanMaxWorkspaceBytes = 2048L * 1024 * 1024;
		public static int FreePlanMaxRowsPerDataset = 20000;
		public static int PaidPlanMaxRowsPerDataset = 500000;
		public static int MaxColumns = 512;
		public static int MaxFieldLength = 32 * 1024;
		public static int MaxWorksheets = 50;

		/// <summary>ZIP guards (section 14.1): maximum compression ratio and entry count; nesting depth is always 1.</summary>
		public static int MaxZipCompressionRatio = 100;
		public static int MaxZipEntries = 2000;

		/// <summary>Analysis time budget per dataset, in seconds (section 14.1 parser limits).</summary>
		public static int ParseTimeBudgetSeconds = 300;

		// ---- Engine ------------------------------------------------------------------------------------------------

		/// <summary>Run lease (section 10.4): two minutes, renewed by a 30-second heartbeat.</summary>
		public static int RunLeaseSeconds = 120;
		public static int RunHeartbeatSeconds = 30;

		/// <summary>Work one claim may do before it checkpoints and releases, so one department cannot starve the others.</summary>
		public static int RunBudgetSeconds = 50;

		/// <summary>Rows per commit chunk (section 11.4); each chunk is its own unit of work.</summary>
		public static int CommitChunkRows = 200;

		/// <summary>Run-level retry ceiling for transient failures (backoff 1, 4, 16 minutes … capped at 6 hours).</summary>
		public static int MaxRunAttempts = 8;

		/// <summary>Rollback window in days (decision D7): after activation for a new department, after the commit for a live one.</summary>
		public static int RollbackWindowDays = 30;

		/// <summary>A running run whose cursor has not moved for this long is failed as IMP-RUN-STALLED (section 13.1).</summary>
		public static int StalledRunMinutes = 15;

		/// <summary>Abandoned multipart uploads are aborted after this many hours without progress.</summary>
		public static int AbandonedUploadHours = 24;

		/// <summary>Scan retries for a ScanPending artifact stop after this many hours.</summary>
		public static int ScanRetryHours = 24;

		/// <summary>Reminder after this many idle days; Abandoned and purged after <see cref="AbandonWorkspaceDays"/>.</summary>
		public static int ReminderIdleDays = 30;
		public static int AbandonWorkspaceDays = 90;

		/// <summary>Geocoding budget per workspace, preview only and administrator-initiated (decision D12).</summary>
		public static int GeocodingBudgetPerWorkspace = 500;

		/// <summary>Invitations sent per minute by the batched sender (section 15.4).</summary>
		public static int InvitationsPerMinute = 50;

		// ---- API extraction (section 14.9) --------------------------------------------------------------------------

		/// <summary>Comma-separated product pack keys disabled cluster-wide without a deploy (the kill switch).</summary>
		public static string DisabledPacks = "";

		/// <summary>Per-request timeout and response cap for source-product API calls.</summary>
		public static int ExtractionRequestTimeoutSeconds = 60;
		public static int ExtractionMaxResponseBytes = 16 * 1024 * 1024;
		public static int ExtractionMaxPages = 20000;

		/// <summary>Source API credentials are deleted this many hours after entry at the latest (section 8.8).</summary>
		public static int SourceCredentialHours = 24;
	}
}
