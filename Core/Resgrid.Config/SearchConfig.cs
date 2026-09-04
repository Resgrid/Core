namespace Resgrid.Config
{
	/// <summary>
	/// Shared Lucene.NET search host (Unified Search plan section 9.2, absorbed by RMS-1). Off by default: with the
	/// host disabled the Records queue keeps running on the indexed projection table and free-text search is
	/// reported as unavailable rather than silently degrading to LIKE.
	/// </summary>
	public static class SearchConfig
	{
		/// <summary>Master switch for the search host in every process.</summary>
		public static bool Enabled = false;

		/// <summary>Root directory shared by the API/Web reader and the worker writer (Docker volume).</summary>
		public static string IndexPath = "/data/search";

		/// <summary>Hard limit on hits a single query may return.</summary>
		public static int MaxResults = 200;

		/// <summary>IndexWriter RAM buffer before a flush.</summary>
		public static int RamBufferSizeMb = 16;

		/// <summary>Rows fetched per page while rebuilding or catching up a department.</summary>
		public static int IndexBatchSize = 500;

		/// <summary>Maximum departments one maintenance sweep rebuilds before yielding to the next run.</summary>
		public static int MaxRebuildsPerSweep = 5;
	}
}
