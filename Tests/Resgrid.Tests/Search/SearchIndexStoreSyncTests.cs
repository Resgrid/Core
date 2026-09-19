using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lucene.Net.Store;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model.Providers;
using Resgrid.Model.Search;
using Resgrid.Model.Services;
using Resgrid.Search;

namespace Resgrid.Tests.Search
{
	/// <summary>
	/// The object-store publish/pull cycle (Unified Search plan R7) against an in-memory store with S3 conditional-PUT
	/// semantics: a writer publishes after commit, a reader in another directory pulls and serves, superseded objects
	/// are pruned, a new writer pulls before it opens, and a manifest published elsewhere makes the next publish fail.
	/// </summary>
	[TestFixture]
	public class SearchIndexStoreSyncTests
	{
		private readonly List<string> _dirs = new List<string>();
		private readonly List<IDisposable> _hosts = new List<IDisposable>();

		[SetUp]
		public void SetUp()
		{
			SearchConfig.Enabled = true;
		}

		[TearDown]
		public void TearDown()
		{
			foreach (var host in _hosts) { try { host.Dispose(); } catch { } }
			_hosts.Clear();
			foreach (var dir in _dirs) { try { System.IO.Directory.Delete(dir, true); } catch { } }
			_dirs.Clear();
			SearchConfig.Enabled = false;
		}

		private string TempDir()
		{
			var dir = Path.Combine(Path.GetTempPath(), "rg-search-" + Guid.NewGuid().ToString("N"));
			System.IO.Directory.CreateDirectory(dir);
			_dirs.Add(dir);
			return dir;
		}

		private LuceneGlobalIndexHost Host(string dir, ISearchIndexStore store)
		{
			var host = new LuceneGlobalIndexHost(FSDirectory.Open(dir), true, store);
			_hosts.Add(host);
			return host;
		}

		[Test]
		public async Task Writer_publishes_reader_pulls_and_superseded_objects_are_pruned()
		{
			var store = new InMemorySearchIndexStore();
			var writer = Host(TempDir(), store);
			var indexer = new LuceneGlobalSearchIndexer(writer);

			await indexer.IndexAsync(new[] { GlobalSearchTests.Projection(1, SearchEntityTypes.Call, "1", "Brush fire on Ridge Rd") }, "1.0.0");
			await indexer.CommitAsync();

			store.Manifests.Should().ContainKey(SearchIndexNames.Global);
			var first = store.Manifests[SearchIndexNames.Global];
			first.Files.Select(f => f.Name).Should().Contain(n => n.StartsWith("segments_"));
			store.Objects[SearchIndexNames.Global].Keys.Should().BeEquivalentTo(first.Files.Select(f => f.Name));

			var reader = Host(TempDir(), store);
			(await reader.PullAsync()).Should().BeTrue();
			var search = new LuceneGlobalSearchService(reader);
			(await search.SearchAsync(1, new GlobalSearchQuery { Text = "brush" })).Hits.Should().ContainSingle();
			reader.LastSyncedRevision.Should().Be(first.Revision);

			await indexer.IndexAsync(new[] { GlobalSearchTests.Projection(1, SearchEntityTypes.Unit, "2", "Brush 41") }, "1.0.0");
			await indexer.ExpungeDeletesAsync(); // force-merge + commit + publish + prune

			var second = store.Manifests[SearchIndexNames.Global];
			second.Revision.Should().NotBe(first.Revision);
			store.Objects[SearchIndexNames.Global].Keys.Should().BeEquivalentTo(second.Files.Select(f => f.Name), "objects no longer referenced by the manifest are deleted");

			(await reader.PullAsync()).Should().BeTrue();
			(await search.SearchAsync(1, new GlobalSearchQuery { Text = "brush" })).Hits.Should().HaveCount(2);
			(await reader.PullAsync()).Should().BeFalse("nothing new to pull");
			System.IO.Directory.EnumerateFiles(reader.IndexPath).Select(Path.GetFileName).Where(n => n != "write.lock").Should().BeEquivalentTo(second.Files.Select(f => f.Name), "stale local files are removed after a pull");
		}

		[Test]
		public async Task A_new_writer_pulls_the_published_index_before_opening()
		{
			var store = new InMemorySearchIndexStore();
			var first = Host(TempDir(), store);
			var indexer = new LuceneGlobalSearchIndexer(first);
			await indexer.IndexAsync(new[] { GlobalSearchTests.Projection(1, SearchEntityTypes.Note, "9", "Hydrant flow test") }, "1.0.0");
			await indexer.CommitAsync();
			first.Dispose();

			var replacement = Host(TempDir(), store);
			var replacementIndexer = new LuceneGlobalSearchIndexer(replacement);
			await replacementIndexer.IndexAsync(new[] { GlobalSearchTests.Projection(1, SearchEntityTypes.Note, "10", "Hydrant map") }, "1.0.0");
			await replacementIndexer.CommitAsync();

			var search = new LuceneGlobalSearchService(replacement);
			(await search.SearchAsync(1, new GlobalSearchQuery { Text = "hydrant" })).Hits.Should().HaveCount(2, "the replacement writer started from the published revision, not an empty directory");
		}

		[Test]
		public async Task Publish_fails_when_another_writer_published_first()
		{
			var store = new InMemorySearchIndexStore();
			var writer = Host(TempDir(), store);
			var indexer = new LuceneGlobalSearchIndexer(writer);
			await indexer.IndexAsync(new[] { GlobalSearchTests.Projection(1, SearchEntityTypes.Call, "1", "First") }, "1.0.0");
			await indexer.CommitAsync();

			// Another writer swaps the manifest underneath us.
			var current = store.Manifests[SearchIndexNames.Global];
			await store.PutManifestAsync(SearchIndexNames.Global, new SearchIndexManifest { IndexName = SearchIndexNames.Global, Revision = "elsewhere", SegmentsGeneration = current.SegmentsGeneration, Files = current.Files }, current.ETag);

			await indexer.IndexAsync(new[] { GlobalSearchTests.Projection(1, SearchEntityTypes.Call, "2", "Second") }, "1.0.0");
			Func<Task> act = () => indexer.CommitAsync();
			await act.Should().ThrowAsync<SearchIndexManifestConflictException>();
			store.Manifests[SearchIndexNames.Global].Revision.Should().Be("elsewhere", "the losing writer never overwrites the other writer's manifest");
		}

		[Test]
		public async Task Without_a_store_commit_is_local_only()
		{
			var writer = Host(TempDir(), null);
			var indexer = new LuceneGlobalSearchIndexer(writer);
			await indexer.IndexAsync(new[] { GlobalSearchTests.Projection(1, SearchEntityTypes.Call, "1", "Local only") }, "1.0.0");
			await indexer.CommitAsync();
			writer.StoreEnabled.Should().BeFalse();
			writer.LastSyncedRevision.Should().BeNull();
		}
	}

	/// <summary>S3 semantics in memory: immutable objects, one manifest per index, If-None-Match:* / If-Match on the manifest.</summary>
	public sealed class InMemorySearchIndexStore : ISearchIndexStore
	{
		private readonly object _sync = new object();
		private int _etag;

		public Dictionary<string, Dictionary<string, byte[]>> Objects { get; } = new Dictionary<string, Dictionary<string, byte[]>>();
		public Dictionary<string, SearchIndexManifest> Manifests { get; } = new Dictionary<string, SearchIndexManifest>();

		public bool Enabled => true;

		public Task<SearchIndexManifest> GetManifestAsync(string indexName, CancellationToken cancellationToken = default)
		{
			lock (_sync)
			{
				return Task.FromResult(Manifests.TryGetValue(indexName, out var m) ? Clone(m) : null);
			}
		}

		public Task<HashSet<string>> ListFilesAsync(string indexName, CancellationToken cancellationToken = default)
		{
			lock (_sync)
			{
				return Task.FromResult(Objects.TryGetValue(indexName, out var files) ? new HashSet<string>(files.Keys) : new HashSet<string>());
			}
		}

		public Task UploadFileAsync(string indexName, string fileName, string localPath, CancellationToken cancellationToken = default)
		{
			var bytes = File.ReadAllBytes(localPath);
			lock (_sync)
			{
				if (!Objects.TryGetValue(indexName, out var files))
					Objects[indexName] = files = new Dictionary<string, byte[]>();
				files[fileName] = bytes;
			}
			return Task.CompletedTask;
		}

		public Task DownloadFileAsync(string indexName, string fileName, string localPath, CancellationToken cancellationToken = default)
		{
			byte[] bytes;
			lock (_sync)
			{
				if (!Objects.TryGetValue(indexName, out var files) || !files.TryGetValue(fileName, out bytes))
					throw new FileNotFoundException(fileName);
			}
			File.WriteAllBytes(localPath, bytes);
			return Task.CompletedTask;
		}

		public Task DeleteFilesAsync(string indexName, IEnumerable<string> fileNames, CancellationToken cancellationToken = default)
		{
			lock (_sync)
			{
				if (Objects.TryGetValue(indexName, out var files))
					foreach (var name in fileNames) files.Remove(name);
			}
			return Task.CompletedTask;
		}

		public Task<SearchIndexManifest> PutManifestAsync(string indexName, SearchIndexManifest manifest, string expectedETag, CancellationToken cancellationToken = default)
		{
			lock (_sync)
			{
				Manifests.TryGetValue(indexName, out var current);
				if (expectedETag == null && current != null)
					throw new SearchIndexManifestConflictException(indexName, "manifest exists");
				if (expectedETag != null && (current == null || current.ETag != expectedETag))
					throw new SearchIndexManifestConflictException(indexName, "etag mismatch");
				var stored = Clone(manifest);
				stored.ETag = "\"" + (++_etag) + "\"";
				Manifests[indexName] = stored;
				manifest.ETag = stored.ETag;
				return Task.FromResult(manifest);
			}
		}

		private static SearchIndexManifest Clone(SearchIndexManifest m)
		{
			return new SearchIndexManifest
			{
				IndexName = m.IndexName,
				Revision = m.Revision,
				SegmentsGeneration = m.SegmentsGeneration,
				SchemaVersion = m.SchemaVersion,
				PublishedOnUtc = m.PublishedOnUtc,
				PublishedBy = m.PublishedBy,
				Files = (m.Files ?? new List<SearchIndexManifestFile>()).Select(f => new SearchIndexManifestFile { Name = f.Name, Length = f.Length }).ToList(),
				ETag = m.ETag
			};
		}
	}
}
