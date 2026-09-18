using Lucene.Net.Analysis.Standard;
using Resgrid.Model.Providers;
using Resgrid.Model.Search;
using Directory = Lucene.Net.Store.Directory;

namespace Resgrid.Search
{
	/// <summary>
	/// The RMS-owned <c>records</c> index on the shared host (RMS plan section 5.10). Kept as its own type so the RMS
	/// indexer, search service, maintenance service and tests are untouched by the host generalisation (plan R4 Phase 1b).
	/// </summary>
	public sealed class LuceneRecordsIndexHost : LuceneIndexHost
	{
		/// <summary>Local-directory host without an object store (tests, single-host Compose). Autofac prefers the store constructor.</summary>
		public LuceneRecordsIndexHost()
			: this(NullSearchIndexStore.Instance)
		{
		}

		public LuceneRecordsIndexHost(ISearchIndexStore store)
			: base(SearchIndexNames.Records, new StandardAnalyzer(RecordsIndexFields.Version), store)
		{
		}

		/// <summary>Test seam: host any directory (e.g. RAMDirectory) without touching the configured path.</summary>
		public LuceneRecordsIndexHost(Directory directory, bool ownsDirectory = false, ISearchIndexStore store = null)
			: base(SearchIndexNames.Records, new StandardAnalyzer(RecordsIndexFields.Version), directory, ownsDirectory, store)
		{
		}
	}

	/// <summary>The Unified Search <c>global</c> index: every Tier 1 entity family through SearchProjections (plan R3, R4 Phase 2).</summary>
	public sealed class LuceneGlobalIndexHost : LuceneIndexHost
	{
		public LuceneGlobalIndexHost()
			: this(NullSearchIndexStore.Instance)
		{
		}

		public LuceneGlobalIndexHost(ISearchIndexStore store)
			: base(SearchIndexNames.Global, GlobalIndexFields.CreateIndexAnalyzer(), store)
		{
		}

		public LuceneGlobalIndexHost(Directory directory, bool ownsDirectory = false, ISearchIndexStore store = null)
			: base(SearchIndexNames.Global, GlobalIndexFields.CreateIndexAnalyzer(), directory, ownsDirectory, store)
		{
		}
	}
}
