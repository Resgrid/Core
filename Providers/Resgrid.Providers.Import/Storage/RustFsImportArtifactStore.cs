using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model.Onboarding;

namespace Resgrid.Providers.Import.Storage
{
	/// <summary>
	/// The Department Import artifact store on RustFS (decision D1): the S3-compatible object store already deployed on the
	/// hosted clusters and in every self-hosted stack, configured exactly as the search index store is (path-style
	/// addressing, configurable SSL and region) but in its own bucket (ImportConfig.S3Bucket). Objects live at
	/// {S3Prefix}/{departmentId}/{workspaceId}/{artifactId} and only ever hold ciphertext. With no endpoint configured every
	/// operation refuses (fail closed); the workspace then says file storage is unavailable.
	/// </summary>
	public sealed class RustFsImportArtifactStore : IImportArtifactStore, IDisposable
	{
		private readonly Lazy<IAmazonS3> _client;

		public RustFsImportArtifactStore()
		{
			_client = new Lazy<IAmazonS3>(CreateClient, LazyThreadSafetyMode.ExecutionAndPublication);
		}

		/// <summary>Test seam: a client built elsewhere (an integration-suite RustFS or MinIO container).</summary>
		public RustFsImportArtifactStore(IAmazonS3 client)
		{
			_client = new Lazy<IAmazonS3>(() => client);
		}

		public bool IsConfigured => !string.IsNullOrWhiteSpace(ImportConfig.S3Endpoint) && !string.IsNullOrWhiteSpace(ImportConfig.S3Bucket);

		private static string Bucket => ImportConfig.S3Bucket;

		public async Task<ImportStoreHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
		{
			var health = new ImportStoreHealth { Configured = IsConfigured, CheckedOn = DateTime.UtcNow };
			if (!IsConfigured)
			{
				health.Reason = "not-configured";
				return health;
			}

			try
			{
				var probe = Prefix() + "_health/" + Guid.NewGuid().ToString("N");
				await _client.Value.PutObjectAsync(new PutObjectRequest { BucketName = Bucket, Key = probe, ContentBody = "ok" }, cancellationToken);
				await _client.Value.DeleteObjectAsync(Bucket, probe, cancellationToken);
				health.Healthy = true;
			}
			catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
			{
				health.Reason = "bucket-missing";
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Import artifact store health check failed");
				health.Reason = "unreachable";
			}

			return health;
		}

		public async Task<string> StartUploadAsync(string objectKey, CancellationToken cancellationToken = default)
		{
			EnsureConfigured();
			var response = await _client.Value.InitiateMultipartUploadAsync(new InitiateMultipartUploadRequest
			{
				BucketName = Bucket,
				Key = objectKey,
				ContentType = "application/octet-stream"
			}, cancellationToken);

			return response.UploadId;
		}

		public async Task<string> UploadPartAsync(string objectKey, string uploadId, int partNumber, byte[] content, CancellationToken cancellationToken = default)
		{
			EnsureConfigured();
			using var stream = new MemoryStream(content, writable: false);
			var response = await _client.Value.UploadPartAsync(new UploadPartRequest
			{
				BucketName = Bucket,
				Key = objectKey,
				UploadId = uploadId,
				PartNumber = partNumber,
				PartSize = content.Length,
				InputStream = stream
			}, cancellationToken);

			return response.ETag;
		}

		public async Task CompleteUploadAsync(string objectKey, string uploadId, IReadOnlyList<ImportArtifactPart> parts, CancellationToken cancellationToken = default)
		{
			EnsureConfigured();
			await _client.Value.CompleteMultipartUploadAsync(new CompleteMultipartUploadRequest
			{
				BucketName = Bucket,
				Key = objectKey,
				UploadId = uploadId,
				PartETags = parts.OrderBy(p => p.PartNumber).Select(p => new PartETag(p.PartNumber, p.ETag)).ToList()
			}, cancellationToken);
		}

		public async Task AbortUploadAsync(string objectKey, string uploadId, CancellationToken cancellationToken = default)
		{
			if (!IsConfigured || string.IsNullOrWhiteSpace(uploadId))
				return;

			try
			{
				await _client.Value.AbortMultipartUploadAsync(Bucket, objectKey, uploadId, cancellationToken);
			}
			catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
			{
				// Already aborted or completed: nothing to do.
			}
		}

		public async Task PutAsync(string objectKey, byte[] content, CancellationToken cancellationToken = default)
		{
			EnsureConfigured();
			using var stream = new MemoryStream(content, writable: false);
			await _client.Value.PutObjectAsync(new PutObjectRequest
			{
				BucketName = Bucket,
				Key = objectKey,
				InputStream = stream,
				ContentType = "application/octet-stream"
			}, cancellationToken);
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

		public async Task<byte[]> ReadRangeAsync(string objectKey, long offset, int length, CancellationToken cancellationToken = default)
		{
			EnsureConfigured();
			if (length <= 0)
				return Array.Empty<byte>();

			using var response = await _client.Value.GetObjectAsync(new GetObjectRequest
			{
				BucketName = Bucket,
				Key = objectKey,
				ByteRange = new ByteRange(offset, offset + length - 1)
			}, cancellationToken);

			using var buffer = new MemoryStream(length);
			await response.ResponseStream.CopyToAsync(buffer, cancellationToken);
			return buffer.ToArray();
		}

		public async Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default)
		{
			EnsureConfigured();
			await _client.Value.DeleteObjectAsync(Bucket, objectKey, cancellationToken);
		}

		public async Task<int> DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default)
		{
			EnsureConfigured();
			var deleted = 0;
			string continuation = null;
			do
			{
				var page = await _client.Value.ListObjectsV2Async(new ListObjectsV2Request
				{
					BucketName = Bucket,
					Prefix = prefix,
					ContinuationToken = continuation
				}, cancellationToken);

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

		public async Task<IReadOnlyList<int>> ListDepartmentIdsAsync(CancellationToken cancellationToken = default)
		{
			EnsureConfigured();
			var result = new List<int>();
			string continuation = null;
			do
			{
				var page = await _client.Value.ListObjectsV2Async(new ListObjectsV2Request
				{
					BucketName = Bucket,
					Prefix = Prefix(),
					Delimiter = "/",
					ContinuationToken = continuation
				}, cancellationToken);

				foreach (var common in page.CommonPrefixes ?? new List<string>())
				{
					var segment = common.Substring(Prefix().Length).TrimEnd('/');
					if (int.TryParse(segment, out var departmentId) && departmentId > 0)
						result.Add(departmentId);
				}

				continuation = page.IsTruncated == true ? page.NextContinuationToken : null;
			} while (continuation != null);

			return result;
		}

		private static string Prefix() => ImportArtifactKeys.Root;

		private void EnsureConfigured()
		{
			if (!IsConfigured)
				throw new ImportArtifactIntegrityException("IMP-STORE-UNAVAILABLE");
		}

		private static IAmazonS3 CreateClient()
		{
			var config = new AmazonS3Config
			{
				ServiceURL = ImportConfig.S3Endpoint,
				ForcePathStyle = ImportConfig.S3ForcePathStyle,
				UseHttp = !ImportConfig.S3UseSsl,
				AuthenticationRegion = string.IsNullOrWhiteSpace(ImportConfig.S3Region) ? "us-east-1" : ImportConfig.S3Region
			};
			// Trimmed for the same reason as the search store: a Kubernetes Secret can carry a trailing newline.
			var credentials = new BasicAWSCredentials((ImportConfig.S3AccessKey ?? string.Empty).Trim(), (ImportConfig.S3SecretKey ?? string.Empty).Trim());
			return new AmazonS3Client(credentials, config);
		}

		public void Dispose()
		{
			if (_client.IsValueCreated)
				_client.Value.Dispose();
		}
	}
}
