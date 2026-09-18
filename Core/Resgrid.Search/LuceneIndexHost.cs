using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lucene.Net.Analysis;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model.Providers;
using Resgrid.Model.Search;
using Directory = Lucene.Net.Store.Directory;

namespace Resgrid.Search
{
	/// <summary>
	/// The shared Lucene host, one process-wide instance per index (Unified Search plan section 3.4, R7). The worker
	/// process opens the single IndexWriter and reads near-real-time through it; every other process opens a
	/// read-only SearcherManager over the committed segments. The live directory is always local. When the object
	/// store is enabled the writer publishes every commit (immutable segment files first, then segments_N, then the
	/// manifest with a conditional PUT) and readers pull the manifest into their local cache; without a store the
	/// single shared directory and Lucene's own file locking coordinate the processes as before.
	/// </summary>
	public class LuceneIndexHost : IDisposable
	{
		private const string ManifestFileName = "manifest.json";
		private const string LockFileName = "write.lock";

		private readonly object _sync = new object();
		private readonly object _writerSyncGate = new object();
		private readonly bool _ownsDirectory;
		private readonly ISearchIndexStore _store;
		private readonly string _localPathOverride;
		private Directory _directory;
		private IndexWriter _writer;
		private SnapshotDeletionPolicy _snapshots;
		private SearcherManager _searcherManager;
		private bool _searcherIsNrt;
		private bool _writerSyncedFromStore;
		private bool _disposed;

		private string _appliedRevision;
		private long _appliedGeneration = -1;
		private string _manifestETag;
		private DateTime _lastPullAttemptUtc = DateTime.MinValue;
		private Task _pullTask;

		/// <summary>Production constructor: the configured local path under SearchConfig.IndexPath.</summary>
		public LuceneIndexHost(string indexName, Analyzer analyzer, ISearchIndexStore store)
		{
			// Deliberately no I/O here. This type is a container singleton, so opening the configured path from the
			// constructor makes every process that merely composes its container depend on the local volume being
			// present and writable — a process with search disabled must still start. The directory opens on use.
			IndexName = indexName ?? throw new ArgumentNullException(nameof(indexName));
			Analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
			_store = store ?? NullSearchIndexStore.Instance;
			_ownsDirectory = true;
		}

		/// <summary>Test seam: host any directory (e.g. RAMDirectory or a temp FSDirectory) without touching the configured path.</summary>
		public LuceneIndexHost(string indexName, Analyzer analyzer, Directory directory, bool ownsDirectory = false, ISearchIndexStore store = null)
		{
			IndexName = indexName ?? throw new ArgumentNullException(nameof(indexName));
			Analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
			_directory = directory ?? throw new ArgumentNullException(nameof(directory));
			_ownsDirectory = ownsDirectory;
			_store = store ?? NullSearchIndexStore.Instance;
			if (directory is FSDirectory fs)
				_localPathOverride = fs.Directory.FullName;
		}

		public string IndexName { get; }

		public Analyzer Analyzer { get; }

		public string IndexPath => _localPathOverride ?? Path.Combine(SearchConfig.IndexPath ?? string.Empty, IndexName);

		public bool Enabled => SearchConfig.Enabled;

		/// <summary>True when an object store is configured and this host has a real local path to sync into.</summary>
		public bool StoreEnabled => _store.Enabled && (_localPathOverride != null || _directory == null || _directory is FSDirectory);

		public string LastSyncedRevision => _appliedRevision;

		public DateTime? LastSyncedOnUtc { get; private set; }

		/// <summary>True while this process holds the writer (worker); false in reader processes.</summary>
		public bool IsWriter { get { lock (_sync) { return _writer != null; } } }

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
					Logging.LogException(ex, $"Search index '{IndexName}' existence check failed.");
					return false;
				}
			}
		}

		/// <summary>
		/// Opens (once) the single IndexWriter. Only the worker process should ever call this. With the object store
		/// enabled the latest published revision is pulled first, so a fresh pod never starts from an empty directory
		/// and diverges from what readers already hold (plan R7 writer sequence step 1).
		/// </summary>
		public IndexWriter GetWriter()
		{
			EnsureWriterSyncedFromStore();
			lock (_sync)
			{
				ThrowIfDisposed();
				if (_writer != null)
					return _writer;

				_snapshots = new SnapshotDeletionPolicy(new KeepOnlyLastCommitDeletionPolicy());
				var config = new IndexWriterConfig(LuceneIndexVersion.Version, Analyzer)
				{
					OpenMode = OpenMode.CREATE_OR_APPEND,
					MergePolicy = new TieredMergePolicy { ForceMergeDeletesPctAllowed = 0 },
					RAMBufferSizeMB = Math.Max(1, SearchConfig.RamBufferSizeMb),
					IndexDeletionPolicy = _snapshots
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

		/// <summary>The reader for this process, or null when no index exists locally yet (reader processes: until the first pull completes).</summary>
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

				if (StoreEnabled)
					StartBackgroundPullIfDue(force: _appliedRevision == null);

				if (!DirectoryReader.IndexExists(Store))
					return null;

				_searcherManager = new SearcherManager(Store, null);
				_searcherIsNrt = false;
				return _searcherManager;
			}
		}

		/// <summary>Refreshes the reader; in a reader process with the store enabled also schedules a manifest poll when one is due.</summary>
		public void MaybeRefresh()
		{
			SearcherManager manager;
			lock (_sync)
			{
				manager = _searcherManager;
				if (_writer == null && StoreEnabled)
					StartBackgroundPullIfDue(force: false);
			}

			try { manager?.MaybeRefresh(); }
			catch (Exception ex) { Logging.LogException(ex, $"Search index '{IndexName}' reader refresh failed."); }
		}

		/// <summary>
		/// Writer startup pull (plan R7 writer sequence step 1), taken on a separate gate so the pull's own refresh of the
		/// reader can take <c>_sync</c>: holding <c>_sync</c> while waiting on the pull thread would deadlock.
		/// </summary>
		private void EnsureWriterSyncedFromStore()
		{
			if (!StoreEnabled || _writerSyncedFromStore)
				return;

			lock (_writerSyncGate)
			{
				if (_writerSyncedFromStore)
					return;
				try
				{
					// No SynchronizationContext in the worker; the blocking wait cannot deadlock now that _sync is free.
					Task.Run(() => PullCoreAsync(CancellationToken.None)).GetAwaiter().GetResult();
				}
				catch (Exception ex)
				{
					// A store outage must not stop the writer from working locally; publish will fail loudly later.
					Logging.LogException(ex, $"Search index '{IndexName}' could not be pulled from the object store before opening the writer; continuing with the local directory.");
				}
				_writerSyncedFromStore = true;
			}
		}

		/// <summary>Serializes mutations with the committed-segment erasure pass.</summary>
		public int Write(Func<IndexWriter, int> mutation)
		{
			EnsureWriterSyncedFromStore();
			lock (_sync) { ThrowIfDisposed(); return mutation(GetWriter()); }
		}

		/// <summary>Commits the writer. Publishing is a separate step so the caller can hold the database lease around it.</summary>
		public void Commit()
		{
			EnsureWriterSyncedFromStore();
			lock (_sync)
			{
				ThrowIfDisposed();
				GetWriter().Commit();
				_searcherManager?.MaybeRefresh();
			}
		}

		/// <summary>Force-merges deletes and commits; throws if deleted documents remain in the committed index (erasure proof).</summary>
		public void ExpungeDeletes()
		{
			EnsureWriterSyncedFromStore();
			lock (_sync)
			{
				ThrowIfDisposed();
				var writer = GetWriter();
				writer.ForceMergeDeletes(true);
				writer.Commit();
				_searcherManager?.MaybeRefreshBlocking();
				writer.DeleteUnusedFiles();
				using var committed = DirectoryReader.Open(Store);
				if (committed.HasDeletions) throw new InvalidOperationException("Deleted documents remain in the committed index; erasure cannot be acknowledged.");
			}
		}

		/// <summary>
		/// Publishes the latest commit to the object store (plan R7 writer sequence steps 4–6): immutable files that the
		/// bucket lacks, then segments_N, then the manifest under a conditional PUT, then prune. No-op without a store.
		/// Throws <see cref="SearchIndexManifestConflictException"/> when another writer has published meanwhile.
		/// </summary>
		public async Task<SearchIndexManifest> PublishAsync(string publishedBy, CancellationToken cancellationToken = default)
		{
			if (!StoreEnabled)
				return null;

			EnsureWriterSyncedFromStore();
			IndexCommit commit;
			IndexWriter writer;
			lock (_sync)
			{
				ThrowIfDisposed();
				writer = GetWriter();
				if (!DirectoryReader.IndexExists(Store))
					return null;
				commit = _snapshots.Snapshot();
			}

			try
			{
				var localFiles = commit.FileNames.Where(f => !string.Equals(f, LockFileName, StringComparison.OrdinalIgnoreCase)).Distinct().ToList();
				var segmentsFile = commit.SegmentsFileName;
				var remote = await _store.ListFilesAsync(IndexName, cancellationToken) ?? new HashSet<string>(StringComparer.Ordinal);

				foreach (var file in localFiles.Where(f => !string.Equals(f, segmentsFile, StringComparison.Ordinal)))
				{
					cancellationToken.ThrowIfCancellationRequested();
					if (remote.Contains(file))
						continue;
					await _store.UploadFileAsync(IndexName, file, Path.Combine(IndexPath, file), cancellationToken);
				}

				await _store.UploadFileAsync(IndexName, segmentsFile, Path.Combine(IndexPath, segmentsFile), cancellationToken);

				var manifest = new SearchIndexManifest
				{
					IndexName = IndexName,
					Revision = Guid.NewGuid().ToString("N"),
					SegmentsGeneration = commit.Generation,
					SchemaVersion = 1,
					PublishedOnUtc = DateTime.UtcNow,
					PublishedBy = publishedBy,
					Files = localFiles.Select(f => new SearchIndexManifestFile { Name = f, Length = SafeLength(Path.Combine(IndexPath, f)) }).ToList()
				};

				string expected = _manifestETag;
				if (expected == null)
				{
					// Fresh process that never read the manifest: adopt the remote only when we synced from it.
					var current = await _store.GetManifestAsync(IndexName, cancellationToken);
					if (current != null)
					{
						if (!_writerSyncedFromStore || !string.Equals(current.Revision, _appliedRevision, StringComparison.Ordinal))
							throw new SearchIndexManifestConflictException(IndexName, $"Search index '{IndexName}' has a published manifest (revision {current.Revision}) this writer did not start from; refusing to overwrite it.");
						expected = current.ETag;
					}
				}

				var stored = await _store.PutManifestAsync(IndexName, manifest, expected, cancellationToken);
				_manifestETag = stored?.ETag;
				_appliedRevision = manifest.Revision;
				_appliedGeneration = manifest.SegmentsGeneration;
				LastSyncedOnUtc = DateTime.UtcNow;

				var keep = new HashSet<string>(localFiles, StringComparer.Ordinal);
				var stale = remote.Where(r => !keep.Contains(r)).ToList();
				if (stale.Count > 0)
					await _store.DeleteFilesAsync(IndexName, stale, cancellationToken);

				return manifest;
			}
			finally
			{
				lock (_sync)
				{
					try { _snapshots.Release(commit); } catch (Exception ex) { Logging.LogException(ex); }
					try { writer.DeleteUnusedFiles(); } catch (Exception ex) { Logging.LogException(ex); }
				}
			}
		}

		/// <summary>Pulls the latest published revision into the local directory (reader processes; writer at startup). Returns true when files changed.</summary>
		public Task<bool> PullAsync(CancellationToken cancellationToken = default)
		{
			if (!StoreEnabled)
				return Task.FromResult(false);
			return PullCoreAsync(cancellationToken);
		}

		/// <summary>
		/// Writer recovery after a manifest conflict: drop the local writer and directory, pull the published revision and
		/// let the maintenance sweep re-index from the projection tables (its checkpoint only advances after a publish).
		/// </summary>
		public async Task ResetFromStoreAsync(CancellationToken cancellationToken = default)
		{
			lock (_sync)
			{
				ThrowIfDisposed();
				try { _searcherManager?.Dispose(); } catch (Exception ex) { Logging.LogException(ex); }
				try { _writer?.Dispose(); } catch (Exception ex) { Logging.LogException(ex); }
				_searcherManager = null;
				_writer = null;
				_snapshots = null;
				WipeLocalFiles();
				_appliedRevision = null;
				_appliedGeneration = -1;
				_manifestETag = null;
				_writerSyncedFromStore = false;
			}

			if (StoreEnabled)
				await PullCoreAsync(cancellationToken);
		}

		private void StartBackgroundPullIfDue(bool force)
		{
			// Called under _sync.
			if (_pullTask != null && !_pullTask.IsCompleted)
				return;
			var due = force || (DateTime.UtcNow - _lastPullAttemptUtc).TotalSeconds >= Math.Max(5, SearchConfig.ReaderPullSeconds);
			if (!due)
				return;
			_lastPullAttemptUtc = DateTime.UtcNow;
			_pullTask = Task.Run(async () =>
			{
				try { await PullCoreAsync(CancellationToken.None); }
				catch (Exception ex) { Logging.LogException(ex, $"Search index '{IndexName}' pull from the object store failed."); }
			});
		}

		private async Task<bool> PullCoreAsync(CancellationToken cancellationToken)
		{
			_lastPullAttemptUtc = DateTime.UtcNow;
			var manifest = await _store.GetManifestAsync(IndexName, cancellationToken);
			if (manifest == null)
				return false;
			if (string.Equals(manifest.Revision, _appliedRevision, StringComparison.Ordinal))
			{
				_manifestETag = manifest.ETag ?? _manifestETag;
				return false;
			}

			var localPath = IndexPath;
			System.IO.Directory.CreateDirectory(localPath);

			// A generation that went backwards means the writer was rebuilt from scratch: file names will be reused
			// with different content, so start from an empty directory rather than trusting name+length matches.
			if (_appliedGeneration >= 0 && manifest.SegmentsGeneration < _appliedGeneration)
			{
				lock (_sync) { WipeLocalFiles(keepOpenHandles: true); }
			}

			var wanted = new Dictionary<string, long>(StringComparer.Ordinal);
			foreach (var f in manifest.Files ?? new List<SearchIndexManifestFile>())
				wanted[f.Name] = f.Length;

			string segmentsFile = wanted.Keys.FirstOrDefault(n => n.StartsWith("segments_", StringComparison.Ordinal));
			foreach (var pair in wanted.Where(p => !string.Equals(p.Key, segmentsFile, StringComparison.Ordinal)))
				await DownloadIfNeededAsync(localPath, pair.Key, pair.Value, cancellationToken);
			if (segmentsFile != null)
				await DownloadIfNeededAsync(localPath, segmentsFile, wanted[segmentsFile], cancellationToken);

			foreach (var existing in System.IO.Directory.EnumerateFiles(localPath))
			{
				var name = Path.GetFileName(existing);
				if (wanted.ContainsKey(name) || string.Equals(name, LockFileName, StringComparison.OrdinalIgnoreCase) || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
					continue;
				try { File.Delete(existing); }
				catch (Exception ex) { Logging.LogException(ex, $"Search index '{IndexName}': stale local file {name} could not be removed yet."); }
			}

			_appliedRevision = manifest.Revision;
			_appliedGeneration = manifest.SegmentsGeneration;
			_manifestETag = manifest.ETag;
			LastSyncedOnUtc = DateTime.UtcNow;

			lock (_sync)
			{
				if (_disposed)
					return true;
				if (_searcherManager != null && !_searcherIsNrt)
				{
					try { _searcherManager.MaybeRefreshBlocking(); }
					catch (Exception ex) { Logging.LogException(ex, $"Search index '{IndexName}' reader refresh after pull failed."); }
				}
			}

			return true;
		}

		private async Task DownloadIfNeededAsync(string localPath, string name, long length, CancellationToken cancellationToken)
		{
			var target = Path.Combine(localPath, name);
			if (File.Exists(target) && new FileInfo(target).Length == length && !name.StartsWith("segments_", StringComparison.Ordinal))
				return;

			var tmp = target + ".tmp";
			try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
			await _store.DownloadFileAsync(IndexName, name, tmp, cancellationToken);
			if (File.Exists(target))
				File.Delete(target);
			File.Move(tmp, target);
		}

		private void WipeLocalFiles(bool keepOpenHandles = false)
		{
			// Called under _sync.
			try
			{
				if (!System.IO.Directory.Exists(IndexPath))
					return;
				foreach (var file in System.IO.Directory.EnumerateFiles(IndexPath))
				{
					var name = Path.GetFileName(file);
					if (string.Equals(name, LockFileName, StringComparison.OrdinalIgnoreCase))
						continue;
					try { File.Delete(file); }
					catch (Exception ex) { if (!keepOpenHandles) Logging.LogException(ex); }
				}
			}
			catch (Exception ex) { Logging.LogException(ex); }
		}

		private static long SafeLength(string path)
		{
			try { return new FileInfo(path).Length; } catch { return 0; }
		}

		private Directory OpenConfiguredDirectory()
		{
			var path = IndexPath;
			System.IO.Directory.CreateDirectory(path);
			return FSDirectory.Open(path);
		}

		private void ThrowIfDisposed()
		{
			if (_disposed)
				throw new ObjectDisposedException(GetType().Name);
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

	/// <summary>Every host and every process must agree on the Lucene version.</summary>
	public static class LuceneIndexVersion
	{
		public const Lucene.Net.Util.LuceneVersion Version = Lucene.Net.Util.LuceneVersion.LUCENE_48;
	}
}
