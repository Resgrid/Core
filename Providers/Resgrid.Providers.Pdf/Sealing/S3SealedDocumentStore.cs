using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Resgrid.Config;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.PdfProvider.Sealing
{
	/// <summary>
	/// Sealed documents in RustFS (any S3-compatible endpoint), the Department Import store pattern with its own bucket
	/// (SealingConfig.S3*). The bucket is provisioned ahead of time; a blank endpoint ⇒ not configured (sealing stays pending).
	/// </summary>
	public sealed class S3SealedDocumentStore : ISealedDocumentStore, IDisposable
	{
		private readonly Lazy<IAmazonS3> _client;

		public S3SealedDocumentStore()
		{
			_client = new Lazy<IAmazonS3>(CreateClient, LazyThreadSafetyMode.ExecutionAndPublication);
		}

		/// <summary>Test seam: a client built elsewhere.</summary>
		public S3SealedDocumentStore(IAmazonS3 client)
		{
			_client = new Lazy<IAmazonS3>(() => client);
		}

		public bool IsConfigured => !string.IsNullOrWhiteSpace(SealingConfig.S3Endpoint) && !string.IsNullOrWhiteSpace(SealingConfig.S3Bucket);

		private static string Bucket => SealingConfig.S3Bucket;

		public async Task PutAsync(string objectKey, byte[] content, string contentType, CancellationToken cancellationToken = default)
		{
			EnsureConfigured();
			using var stream = new MemoryStream(content ?? Array.Empty<byte>(), false);
			await _client.Value.PutObjectAsync(new PutObjectRequest
			{
				BucketName = Bucket,
				Key = objectKey,
				InputStream = stream,
				ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/pdf" : contentType,
				AutoCloseStream = false
			}, cancellationToken);
		}

		public async Task<byte[]> GetAsync(string objectKey, CancellationToken cancellationToken = default)
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

		public async Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default)
		{
			EnsureConfigured();
			await _client.Value.DeleteObjectAsync(Bucket, objectKey, cancellationToken);
		}

		private void EnsureConfigured()
		{
			if (!IsConfigured)
				throw new InvalidOperationException("sealing_store_not_configured");
		}

		private static IAmazonS3 CreateClient()
		{
			var config = new AmazonS3Config
			{
				ServiceURL = SealingConfig.S3Endpoint,
				ForcePathStyle = SealingConfig.S3ForcePathStyle,
				UseHttp = !SealingConfig.S3UseSsl,
				AuthenticationRegion = string.IsNullOrWhiteSpace(SealingConfig.S3Region) ? "us-east-1" : SealingConfig.S3Region
			};
			// Trimmed: a Kubernetes Secret can carry a trailing newline.
			var credentials = new BasicAWSCredentials((SealingConfig.S3AccessKey ?? string.Empty).Trim(), (SealingConfig.S3SecretKey ?? string.Empty).Trim());
			return new AmazonS3Client(credentials, config);
		}

		public void Dispose()
		{
			if (_client.IsValueCreated)
				_client.Value.Dispose();
		}
	}
}
