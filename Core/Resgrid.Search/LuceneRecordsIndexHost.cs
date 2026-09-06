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
		private readonly bool _ownsDirectory;
		private Directory _directory;
		private IndexWriter _writer;
		private SearcherManager _searcherManager;
		private bool _searcherIsNrt;
		private bool _disposed;

		public LuceneRecordsIndexHost()
		{
			// Deliberately no I/O here. This type is a container singleton, so opening the configured path from the
			// constructor makes every process that merely composes its container depend on the shared volume being
			// present and writable — a process with search disabled must still start. The directory opens on use.
			_ownsDirectory = true;
			Analyzer = new StandardAnalyzer(RecordsIndexFields.Version);
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

		/// <summary>The backing store, opening the configured path on first use.</summary>
		private Directory Store
		{
			get
			{
				if (_directory != null)
					return _directory;

				lock (_sync)
					return _directory ??= OpenConfiguredDirectory();
			}
		}

		public bool IndexExists
		{
			get
			{
				try { return DirectoryReader.IndexExists(Store); }
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
					MergePolicy = new TieredMergePolicy { ForceMergeDeletesPctAllowed = 0 },
					RAMBufferSizeMB = Math.Max(1, SearchConfig.RamBufferSizeMb)
				};
				_writer = new IndexWriter(Store, config);

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

				if (!DirectoryReader.IndexExists(Store))
					return null;

				_searcherManager = new SearcherManager(Store, null);
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

		/// <summary>Serializes mutations with the committed-segment erasure pass.</summary>
		public int Write(Func<IndexWriter, int> mutation)
		{
			lock (_sync) { ThrowIfDisposed(); return mutation(GetWriter()); }
		}

		public void ExpungeDeletes()
		{
			lock (_sync)
			{
				ThrowIfDisposed();
				var writer = GetWriter();
				writer.ForceMergeDeletes(true);
				writer.Commit();
				_searcherManager?.MaybeRefreshBlocking();
				writer.DeleteUnusedFiles();
				using var committed = DirectoryReader.Open(Store);
				if (committed.HasDeletions) throw new InvalidOperationException("Deleted records remain in the committed index; erasure cannot be acknowledged.");
			}
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
				// _directory, never Store: a host that was disposed without ever indexing must not open the
				// configured path on its way out.
				if (_ownsDirectory && _directory != null)
				{
					try { _directory.Dispose(); } catch (Exception ex) { Logging.LogException(ex); }
				}
				try { Analyzer.Dispose(); } catch (Exception ex) { Logging.LogException(ex); }
			}
		}
	}
}
