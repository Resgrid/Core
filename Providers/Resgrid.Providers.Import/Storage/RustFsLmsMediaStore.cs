using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Resgrid.Config;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Import.Storage
{
	/// <summary>
	/// The LMS media store (Training T2, decision 9): RustFS through the S3 API, configured like the import artifact store (path-style
	/// addressing, configurable SSL and region) but in its own bucket (LmsConfig.S3Bucket) so lifecycle and access policies stay
	/// independent. Objects live at {S3Prefix}/{departmentId}/{assetId}/... Browsers fetch HLS segments and the MP4 rendition through
	/// presigned GET URLs. With no endpoint configured every write refuses (fail closed) and nothing streams. It sits beside the
	/// import store because this provider already owns the RustFS client and is registered in every host.
	/// </summary>
	public sealed class RustFsLmsMediaStore : ILmsMediaStore, IDisposable
	{
		private readonly Lazy<IAmazonS3> _client;

		public RustFsLmsMediaStore()
		{
			_client = new Lazy<IAmazonS3>(CreateClient, LazyThreadSafetyMode.ExecutionAndPublication);
		}

		/// <summary>Test seam: a client built elsewhere (a RustFS or MinIO container).</summary>
		public RustFsLmsMediaStore(IAmazonS3 client)
		{
			_client = new Lazy<IAmazonS3>(() => client);
		}

		public bool IsConfigured => !string.IsNullOrWhiteSpace(LmsConfig.S3Endpoint) && !string.IsNullOrWhiteSpace(LmsConfig.S3Bucket);

		private static string Bucket => LmsConfig.S3Bucket;

		public string KeyFor(int departmentId, int lmsMediaAssetId, string relativePath)
		{
			var prefix = string.IsNullOrWhiteSpace(LmsConfig.S3Prefix) ? "lms" : LmsConfig.S3Prefix.Trim('/');
			var asset = lmsMediaAssetId > 0 ? lmsMediaAssetId.ToString(CultureInfo.InvariantCulture) : "shared";
			return prefix + "/" + departmentId.ToString(CultureInfo.InvariantCulture) + "/" + asset + "/" + (relativePath ?? string.Empty).TrimStart('/');
		}

		public async Task PutAsync(string objectKey, Stream content, string contentType, CancellationToken cancellationToken = default)
		{
			EnsureConfigured();
			await _client.Value.PutObjectAsync(new PutObjectRequest
			{
				BucketName = Bucket,
				Key = objectKey,
				InputStream = content,
				AutoCloseStream = false,
				ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType
			}, cancellationToken);
		}

		public async Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken = default)
		{
			EnsureConfigured();
			var response = await _client.Value.GetObjectAsync(Bucket, objectKey, cancellationToken);
			return response.ResponseStream;
		}

		public async Task<byte[]> ReadAllAsync(string objectKey, CancellationToken cancellationToken = default)
		{
			EnsureConfigured();
			try
			{
				using var response = await _client.Value.GetObjectAsync(Bucket, objectKey, cancellationToken);
				using var buffer = new MemoryStream();
				await response.ResponseStream.CopyToAsync(buffer, cancellationToken);
				return buffer.ToArray();
			}
			catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
			{
				return null;
			}
		}

		public async Task<long?> GetSizeAsync(string objectKey, CancellationToken cancellationToken = default)
		{
			EnsureConfigured();
			try
			{
				var metadata = await _client.Value.GetObjectMetadataAsync(Bucket, objectKey, cancellationToken);
				return metadata.ContentLength;
			}
			catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
			{
				return null;
			}
		}

		public string GetSignedUrl(string objectKey, DateTime expiresOnUtc)
		{
			EnsureConfigured();
			return _client.Value.GetPreSignedURL(new GetPreSignedUrlRequest
			{
				BucketName = Bucket,
				Key = objectKey,
				Verb = HttpVerb.GET,
				Expires = expiresOnUtc,
				Protocol = LmsConfig.S3UseSsl ? Protocol.HTTPS : Protocol.HTTP
			});
		}

		public async Task<int> DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default)
		{
			EnsureConfigured();
			var deleted = 0;
			string continuation = null;
			do
			{
				var page = await _client.Value.ListObjectsV2Async(new ListObjectsV2Request { BucketName = Bucket, Prefix = prefix, ContinuationToken = continuation }, cancellationToken);
				var keys = page.S3Objects?.Select(o => new KeyVersion { Key = o.Key }).ToList() ?? new List<KeyVersion>();
				if (keys.Count > 0)
				{
					await _client.Value.DeleteObjectsAsync(new DeleteObjectsRequest { BucketName = Bucket, Objects = keys }, cancellationToken);
					deleted += keys.Count;
				}
				continuation = page.IsTruncated == true ? page.NextContinuationToken : null;
			} while (continuation != null);
			return deleted;
		}

		private void EnsureConfigured()
		{
			if (!IsConfigured)
				throw new InvalidOperationException("LMS-MEDIA-STORE-UNAVAILABLE");
		}

		private static IAmazonS3 CreateClient()
		{
			var config = new AmazonS3Config
			{
				ServiceURL = LmsConfig.S3Endpoint,
				ForcePathStyle = LmsConfig.S3ForcePathStyle,
				UseHttp = !LmsConfig.S3UseSsl,
				AuthenticationRegion = string.IsNullOrWhiteSpace(LmsConfig.S3Region) ? "us-east-1" : LmsConfig.S3Region
			};
			// Trimmed for the same reason as the search and import stores: a Kubernetes Secret can carry a trailing newline.
			var credentials = new BasicAWSCredentials((LmsConfig.S3AccessKey ?? string.Empty).Trim(), (LmsConfig.S3SecretKey ?? string.Empty).Trim());
			return new AmazonS3Client(credentials, config);
		}

		public void Dispose()
		{
			if (_client.IsValueCreated)
				_client.Value.Dispose();
		}
	}
}
