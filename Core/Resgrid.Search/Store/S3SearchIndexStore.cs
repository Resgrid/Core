using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Newtonsoft.Json;
using Resgrid.Config;
using Resgrid.Model.Providers;
using Resgrid.Model.Search;

namespace Resgrid.Search
{
	/// <summary>
	/// RustFS / S3-compatible implementation of the search index object store (Unified Search plan R7, verified against
	/// RustFS 1.0.0 on 2026-09-17: conditional PUT and bucket default encryption are CI-gated there). Objects live under
	/// {S3Prefix}/{indexName}/{fileName}; the manifest is {S3Prefix}/{indexName}/manifest.json. The manifest PUT is
	/// conditional: If-None-Match: * on first publish, If-Match: {etag} afterwards, so a stale writer can never win.
	/// ETags are passed through exactly as the store returned them (quoted), which is what the store expects.
	/// Encryption at rest is the bucket's default encryption; no SSE headers are sent per object.
	/// </summary>
	public sealed class S3SearchIndexStore : ISearchIndexStore, IDisposable
	{
		private const string ManifestName = "manifest.json";
		private readonly Lazy<IAmazonS3> _client;

		public S3SearchIndexStore()
		{
			_client = new Lazy<IAmazonS3>(CreateClient, LazyThreadSafetyMode.ExecutionAndPublication);
		}

		/// <summary>Test seam.</summary>
		public S3SearchIndexStore(IAmazonS3 client)
		{
			_client = new Lazy<IAmazonS3>(() => client);
		}

		public bool Enabled => !string.IsNullOrWhiteSpace(SearchConfig.S3Endpoint) && !string.IsNullOrWhiteSpace(SearchConfig.S3Bucket);

		private static IAmazonS3 CreateClient()
		{
			var config = new AmazonS3Config
			{
				ServiceURL = SearchConfig.S3Endpoint,
				ForcePathStyle = SearchConfig.S3ForcePathStyle,
				UseHttp = !SearchConfig.S3UseSsl,
				AuthenticationRegion = string.IsNullOrWhiteSpace(SearchConfig.S3Region) ? "us-east-1" : SearchConfig.S3Region
			};
			var credentials = new BasicAWSCredentials(SearchConfig.S3AccessKey ?? string.Empty, SearchConfig.S3SecretKey ?? string.Empty);
			return new AmazonS3Client(credentials, config);
		}

		private static string Prefix(string indexName)
		{
			var prefix = (SearchConfig.S3Prefix ?? string.Empty).Trim('/');
			return string.IsNullOrEmpty(prefix) ? indexName + "/" : prefix + "/" + indexName + "/";
		}

		private static string Key(string indexName, string fileName) => Prefix(indexName) + fileName;

		public async Task<SearchIndexManifest> GetManifestAsync(string indexName, CancellationToken cancellationToken = default)
		{
			try
			{
				using var response = await _client.Value.GetObjectAsync(new GetObjectRequest { BucketName = SearchConfig.S3Bucket, Key = Key(indexName, ManifestName) }, cancellationToken);
				using var reader = new StreamReader(response.ResponseStream);
				var json = await reader.ReadToEndAsync();
				var manifest = JsonConvert.DeserializeObject<SearchIndexManifest>(json) ?? new SearchIndexManifest { IndexName = indexName };
				manifest.ETag = response.ETag;
				return manifest;
			}
			catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound || string.Equals(ex.ErrorCode, "NoSuchKey", StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}
		}

		public async Task<HashSet<string>> ListFilesAsync(string indexName, CancellationToken cancellationToken = default)
		{
			var prefix = Prefix(indexName);
			var names = new HashSet<string>(StringComparer.Ordinal);
			string token = null;
			do
			{
				var response = await _client.Value.ListObjectsV2Async(new ListObjectsV2Request { BucketName = SearchConfig.S3Bucket, Prefix = prefix, ContinuationToken = token }, cancellationToken);
				foreach (var obj in response.S3Objects ?? new List<S3Object>())
				{
					var name = obj.Key.Substring(prefix.Length);
					if (name.Length == 0 || name.Contains('/') || string.Equals(name, ManifestName, StringComparison.Ordinal))
						continue;
					names.Add(name);
				}
				token = response.IsTruncated == true ? response.NextContinuationToken : null;
			} while (token != null);

			return names;
		}

		public Task UploadFileAsync(string indexName, string fileName, string localPath, CancellationToken cancellationToken = default)
		{
			return _client.Value.PutObjectAsync(new PutObjectRequest
			{
				BucketName = SearchConfig.S3Bucket,
				Key = Key(indexName, fileName),
				FilePath = localPath,
				ContentType = "application/octet-stream",
				DisablePayloadSigning = false
			}, cancellationToken);
		}

		public async Task DownloadFileAsync(string indexName, string fileName, string localPath, CancellationToken cancellationToken = default)
		{
			using var response = await _client.Value.GetObjectAsync(new GetObjectRequest { BucketName = SearchConfig.S3Bucket, Key = Key(indexName, fileName) }, cancellationToken);
			await response.WriteResponseStreamToFileAsync(localPath, false, cancellationToken);
		}

		public async Task DeleteFilesAsync(string indexName, IEnumerable<string> fileNames, CancellationToken cancellationToken = default)
		{
			foreach (var batch in (fileNames ?? Enumerable.Empty<string>()).Distinct().Chunk(1000))
			{
				await _client.Value.DeleteObjectsAsync(new DeleteObjectsRequest
				{
					BucketName = SearchConfig.S3Bucket,
					Objects = batch.Select(n => new KeyVersion { Key = Key(indexName, n) }).ToList(),
					Quiet = true
				}, cancellationToken);
			}
		}

		public async Task<SearchIndexManifest> PutManifestAsync(string indexName, SearchIndexManifest manifest, string expectedETag, CancellationToken cancellationToken = default)
		{
			var request = new PutObjectRequest
			{
				BucketName = SearchConfig.S3Bucket,
				Key = Key(indexName, ManifestName),
				ContentBody = JsonConvert.SerializeObject(manifest),
				ContentType = "application/json"
			};
			if (expectedETag == null)
				request.IfNoneMatch = "*";
			else
				request.IfMatch = expectedETag;

			try
			{
				var response = await _client.Value.PutObjectAsync(request, cancellationToken);
				manifest.ETag = response.ETag;
				return manifest;
			}
			catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed || ex.StatusCode == HttpStatusCode.Conflict
				|| string.Equals(ex.ErrorCode, "PreconditionFailed", StringComparison.OrdinalIgnoreCase) || string.Equals(ex.ErrorCode, "ConditionalRequestConflict", StringComparison.OrdinalIgnoreCase))
			{
				throw new SearchIndexManifestConflictException(indexName, $"Search index '{indexName}' manifest was published by another writer (store returned {(int)ex.StatusCode} {ex.ErrorCode}).");
			}
		}

		public void Dispose()
		{
			if (_client.IsValueCreated)
				_client.Value.Dispose();
		}
	}
}
