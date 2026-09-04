using System;
using System.IO;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Resgrid.Config;
using Resgrid.Framework;
using Directory = Lucene.Net.Store.Directory;

namespace Resgrid.Search
{
	/// <summary>
	/// The shared Lucene host for the records index (Unified Search plan section 3.4): one process-wide
	/// instance per index. The worker process opens the single IndexWriter and reads near-real-time through it;
	/// every other process opens a read-only SearcherManager over the committed segments and refreshes on demand.
	/// Cross-process coordination is Lucene's own directory locking on the shared volume.
	/// </summary>
	public sealed class LuceneRecordsIndexHost : IDisposable
	{
		private readonly object _sync = new object();
		private readonly Directory _directory;
		private readonly bool _ownsDirectory;
		private IndexWriter _writer;
		private SearcherManager _searcherManager;
		private bool _searcherIsNrt;
		private bool _disposed;

		public LuceneRecordsIndexHost()
			: this(OpenConfiguredDirectory(), true)
		{
		}

		/// <summary>Test seam: host any directory (e.g. RAMDirectory) without touching the configured path.</summary>
		public LuceneRecordsIndexHost(Directory directory, bool ownsDirectory = false)
		{
			_directory = directory ?? throw new ArgumentNullException(nameof(directory));
			_ownsDirectory = ownsDirectory;
			Analyzer = new StandardAnalyzer(RecordsIndexFields.Version);
		}

		public Analyzer Analyzer { get; }

		public string IndexPath => Path.Combine(SearchConfig.IndexPath ?? string.Empty, RecordsIndexFields.IndexName);

		public bool Enabled => SearchConfig.Enabled;

		public bool IndexExists
		{
			get
			{
				try { return DirectoryReader.IndexExists(_directory); }
				catch (Exception ex)
				{
					Logging.LogException(ex, "Records search index existence check failed.");
					return false;
				}
			}
		}

		/// <summary>Opens (once) the single IndexWriter. Only the worker process should ever call this.</summary>
		public IndexWriter GetWriter()
		{
			lock (_sync)
			{
				ThrowIfDisposed();
				if (_writer != null)
					return _writer;

				var config = new IndexWriterConfig(RecordsIndexFields.Version, Analyzer)
				{
					OpenMode = OpenMode.CREATE_OR_APPEND,
					RAMBufferSizeMB = Math.Max(1, SearchConfig.RamBufferSizeMb)
				};
				_writer = new IndexWriter(_directory, config);

				// A reader opened before the writer existed keeps working; from here on prefer the NRT view.
				if (_searcherManager != null && !_searcherIsNrt)
				{
					_searcherManager.Dispose();
					_searcherManager = null;
				}

				return _writer;
			}
		}

		/// <summary>The reader for this process, or null when no index has been created yet.</summary>
		public SearcherManager GetSearcherManager()
		{
			lock (_sync)
			{
				ThrowIfDisposed();
				if (_searcherManager != null)
					return _searcherManager;

				if (_writer != null)
				{
					_searcherManager = new SearcherManager(_writer, true, null);
					_searcherIsNrt = true;
					return _searcherManager;
				}

				if (!DirectoryReader.IndexExists(_directory))
					return null;

				_searcherManager = new SearcherManager(_directory, null);
				_searcherIsNrt = false;
				return _searcherManager;
			}
		}

		public void MaybeRefresh()
		{
			SearcherManager manager;
			lock (_sync)
			{
				manager = _searcherManager;
			}

			try { manager?.MaybeRefresh(); }
			catch (Exception ex) { Logging.LogException(ex, "Records search reader refresh failed."); }
		}

		private static Directory OpenConfiguredDirectory()
		{
			var path = Path.Combine(SearchConfig.IndexPath ?? string.Empty, RecordsIndexFields.IndexName);
			System.IO.Directory.CreateDirectory(path);
			return FSDirectory.Open(path);
		}

		private void ThrowIfDisposed()
		{
			if (_disposed)
				throw new ObjectDisposedException(nameof(LuceneRecordsIndexHost));
		}

		public void Dispose()
		{
			lock (_sync)
			{
				if (_disposed)
					return;
				_disposed = true;

				try { _searcherManager?.Dispose(); } catch (Exception ex) { Logging.LogException(ex); }
				try { _writer?.Dispose(); } catch (Exception ex) { Logging.LogException(ex); }
				if (_ownsDirectory)
				{
					try { _directory.Dispose(); } catch (Exception ex) { Logging.LogException(ex); }
				}
				Analyzer.Dispose();
			}
		}
	}
}
