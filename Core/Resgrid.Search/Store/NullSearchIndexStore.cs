using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Providers;
using Resgrid.Model.Search;

namespace Resgrid.Search
{
	/// <summary>No object store configured: every host is a plain local-directory host (single-host Compose, tests).</summary>
	public sealed class NullSearchIndexStore : ISearchIndexStore
	{
		public static readonly NullSearchIndexStore Instance = new NullSearchIndexStore();

		public bool Enabled => false;

		public Task<SearchIndexManifest> GetManifestAsync(string indexName, CancellationToken cancellationToken = default) => Task.FromResult<SearchIndexManifest>(null);

		public Task<HashSet<string>> ListFilesAsync(string indexName, CancellationToken cancellationToken = default) => Task.FromResult(new HashSet<string>());

		public Task UploadFileAsync(string indexName, string fileName, string localPath, CancellationToken cancellationToken = default) => throw Disabled();

		public Task DownloadFileAsync(string indexName, string fileName, string localPath, CancellationToken cancellationToken = default) => throw Disabled();

		public Task DeleteFilesAsync(string indexName, IEnumerable<string> fileNames, CancellationToken cancellationToken = default) => throw Disabled();

		public Task<SearchIndexManifest> PutManifestAsync(string indexName, SearchIndexManifest manifest, string expectedETag, CancellationToken cancellationToken = default) => throw Disabled();

		private static InvalidOperationException Disabled() => new InvalidOperationException("No search index object store is configured (SearchConfig.S3Endpoint is empty).");
	}
}
