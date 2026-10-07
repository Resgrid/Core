namespace Resgrid.Config
{
	/// <summary>
	/// Shared Lucene.NET search host (Unified Search plan section 9.2 / R7). Off by default: with the host disabled the
	/// Records queue keeps running on the indexed projection table and free-text search is reported as unavailable
	/// rather than silently degrading to LIKE. Environment keys follow RESGRID__SearchConfig__{Field}.
	/// </summary>
	public static class SearchConfig
	{
		/// <summary>Master switch for the search host in every process.</summary>
		public static bool Enabled = false;

		/// <summary>
		/// Local root directory for every index this process opens. With the object store enabled this is the per-pod
		/// cache (an emptyDir in Kubernetes); without it, the single shared directory (Compose bind mount).
		/// </summary>
		public static string IndexPath = "/data/search";

		/// <summary>Hard limit on hits a single query may return.</summary>
		public static int MaxResults = 200;

		/// <summary>
		/// Deepest window the search page and its CSV export may authorize for one narrowed query (a single family, a date
		/// range), so a "how many calls mention X" question gets a complete list and an exact count. Typeahead stays on MaxResults.
		/// </summary>
		public static int MaxPageWindow = 1000;

		/// <summary>IndexWriter RAM buffer before a flush.</summary>
		public static int RamBufferSizeMb = 16;

		/// <summary>Rows fetched per page while rebuilding or catching up a department.</summary>
		public static int IndexBatchSize = 500;

		/// <summary>Maximum departments one maintenance sweep rebuilds before yielding to the next run.</summary>
		public static int MaxRebuildsPerSweep = 5;

		/// <summary>
		/// Closed calls from this many calendar years (including the current one) enter a rebuild; active calls always do.
		/// 0 (the default) rebuilds every year the department has calls in: finding an old call is a common use of search,
		/// and a rebuild retires every call it does not re-project. A positive value also turns off the call history backfill.
		/// </summary>
		public static int CallRebuildYears = 0;

		/// <summary>Calls one department's history backfill reads per batch (M0261); each sweep gives every pending department one batch.</summary>
		public static int CallBackfillBatchSize = 250;

		/// <summary>Seconds of each maintenance sweep the call history backfill may use, after catch-up; 0 turns the backfill off.</summary>
		public static int CallBackfillSecondsPerSweep = 20;

		// ---- Object store (RustFS / any S3-compatible endpoint), plan R7. Empty endpoint = disabled. -------------

		/// <summary>S3-compatible endpoint, e.g. https://rustfs.internal:9000. Empty disables publish/pull.</summary>
		public static string S3Endpoint = "";

		public static string S3AccessKey = "";

		public static string S3SecretKey = "";

		public static string S3Bucket = "";

		public static string S3Region = "us-east-1";

		public static bool S3UseSsl = true;

		public static bool S3ForcePathStyle = true;

		/// <summary>Key prefix inside the bucket; each index lives under {S3Prefix}/{indexName}/.</summary>
		public static string S3Prefix = "search";

		/// <summary>How often a reader process re-reads the manifest (seconds). The records sweep commits at most once a minute.</summary>
		public static int ReaderPullSeconds = 30;

		/// <summary>Database publish lease duration (seconds) held by the writer around commit-and-upload.</summary>
		public static int PublishLeaseSeconds = 120;
	}
}
