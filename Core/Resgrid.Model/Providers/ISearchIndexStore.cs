using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Search;

namespace Resgrid.Model.Providers
{
	/// <summary>
	/// The durable and distribution layer behind the per-pod Lucene caches (Unified Search plan R7). The object store
	/// is never the live Lucene directory: the single writer uploads immutable segment files after each commit and
	/// then swaps the manifest with a conditional PUT; readers poll the manifest and pull missing files into their
	/// local directory. An implementation whose <see cref="Enabled"/> is false makes every host behave as a plain
	/// local-directory host (single-host Compose).
	/// </summary>
	public interface ISearchIndexStore
	{
		bool Enabled { get; }

		/// <summary>The current manifest, or null when the prefix has never been published.</summary>
		Task<SearchIndexManifest> GetManifestAsync(string indexName, CancellationToken cancellationToken = default);

		/// <summary>Object names (relative to the index prefix) currently in the store, excluding the manifest.</summary>
		Task<HashSet<string>> ListFilesAsync(string indexName, CancellationToken cancellationToken = default);

		Task UploadFileAsync(string indexName, string fileName, string localPath, CancellationToken cancellationToken = default);

		Task DownloadFileAsync(string indexName, string fileName, string localPath, CancellationToken cancellationToken = default);

		Task DeleteFilesAsync(string indexName, IEnumerable<string> fileNames, CancellationToken cancellationToken = default);

		/// <summary>
		/// Writes the manifest conditionally: <paramref name="expectedETag"/> null means "must not exist yet"; otherwise
		/// the PUT carries If-Match. Throws <see cref="SearchIndexManifestConflictException"/> when the precondition
		/// fails. Returns the manifest with its new ETag.
		/// </summary>
		Task<SearchIndexManifest> PutManifestAsync(string indexName, SearchIndexManifest manifest, string expectedETag, CancellationToken cancellationToken = default);
	}
}
