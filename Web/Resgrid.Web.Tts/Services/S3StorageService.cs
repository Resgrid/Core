using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Resgrid.Web.Tts.Configuration;
using System.Net;

namespace Resgrid.Web.Tts.Services
{
	public sealed class S3StorageService : IStorageService
	{
		private const int MaxRetryAttempts = 3;

		private readonly IAmazonS3 _s3Client;
		private readonly S3StorageOptions _options;
		private readonly ILogger<S3StorageService> _logger;

		public S3StorageService(
			IAmazonS3 s3Client,
			IOptions<S3StorageOptions> options,
			ILogger<S3StorageService> logger)
		{
			_s3Client = s3Client;
			_options = options.Value;
			_logger = logger;
		}

		public async Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken)
		{
			try
			{
				await ExecuteWithRetryAsync(
					() => _s3Client.GetObjectMetadataAsync(
						new GetObjectMetadataRequest
						{
							BucketName = _options.Bucket,
							Key = objectKey
						},
						cancellationToken),
					$"checking metadata for {objectKey}",
					cancellationToken);

				return true;
			}
			catch (AmazonS3Exception ex) when (IsNotFound(ex))
			{
				return false;
			}
		}

		public async Task UploadAsync(string objectKey, Stream content, string contentType, CancellationToken cancellationToken)
		{
			MemoryStream? bufferedContent = null;
			var uploadContent = content;

			if (!content.CanSeek)
			{
				bufferedContent = new MemoryStream();
				await content.CopyToAsync(bufferedContent, cancellationToken);
				bufferedContent.Position = 0;
				uploadContent = bufferedContent;
			}

			try
			{
				await ExecuteWithRetryAsync(
					() =>
					{
						if (uploadContent.CanSeek)
						{
							uploadContent.Position = 0;
						}

						return _s3Client.PutObjectAsync(
							new PutObjectRequest
							{
								BucketName = _options.Bucket,
								Key = objectKey,
								InputStream = uploadContent,
								ContentType = contentType
							},
							cancellationToken);
					},
					$"uploading {objectKey}",
					cancellationToken);
			}
			finally
			{
				if (bufferedContent is not null)
				{
					await bufferedContent.DisposeAsync();
				}
			}
		}

		public async Task<TtsAudioContent?> GetObjectAsync(string objectKey, CancellationToken cancellationToken)
		{
			try
			{
				using var response = await ExecuteWithRetryAsync(
					() => _s3Client.GetObjectAsync(
						new GetObjectRequest
						{
							BucketName = _options.Bucket,
							Key = objectKey
						},
						cancellationToken),
					$"downloading {objectKey}",
					cancellationToken);

				await using var responseStream = response.ResponseStream;
				using var memoryStream = new MemoryStream();
				await responseStream.CopyToAsync(memoryStream, cancellationToken);

				var audioBytes = memoryStream.ToArray();
				var contentType = string.IsNullOrWhiteSpace(response.Headers.ContentType)
					? "audio/wav"
					: response.Headers.ContentType;
				var entityTag = string.IsNullOrWhiteSpace(response.ETag)
					? CreateEntityTag(audioBytes)
					: NormalizeEntityTag(response.ETag);
				var lastModified = response.LastModified == default
					? DateTimeOffset.UtcNow
					: new DateTimeOffset(DateTime.SpecifyKind(response.LastModified, DateTimeKind.Utc));

				return new TtsAudioContent(audioBytes, contentType, entityTag, lastModified);
			}
			catch (AmazonS3Exception ex) when (IsNotFound(ex))
			{
				return null;
			}
		}

		public Task<Uri> GetObjectUrlAsync(string objectKey, CancellationToken cancellationToken)
		{
			if (!string.IsNullOrWhiteSpace(_options.PublicBaseUrl))
			{
				return Task.FromResult(new Uri($"{_options.PublicBaseUrl.TrimEnd('/')}/{objectKey}"));
			}

			if (_options.UsePresignedUrls)
			{
				var url = _s3Client.GetPreSignedURL(new GetPreSignedUrlRequest
				{
					BucketName = _options.Bucket,
					Key = objectKey,
					Expires = DateTime.UtcNow.AddMinutes(_options.PresignedUrlExpiryMinutes)
				});

				return Task.FromResult(new Uri(url));
			}

			return Task.FromResult(BuildDirectObjectUrl(objectKey));
		}

		private async Task<TResponse> ExecuteWithRetryAsync<TResponse>(Func<Task<TResponse>> operation, string operationName, CancellationToken cancellationToken)
		{
			for (var attempt = 1; attempt <= MaxRetryAttempts; attempt++)
			{
				try
				{
					return await operation();
				}
				catch (AmazonS3Exception ex) when (IsNotFound(ex))
				{
					throw;
				}
				catch (AmazonServiceException ex) when (attempt < MaxRetryAttempts && IsTransient(ex))
				{
					await DelayRetryAsync(operationName, attempt, ex, cancellationToken);
				}
				catch (HttpRequestException ex) when (attempt < MaxRetryAttempts)
				{
					await DelayRetryAsync(operationName, attempt, ex, cancellationToken);
				}
				catch (IOException ex) when (attempt < MaxRetryAttempts)
				{
					await DelayRetryAsync(operationName, attempt, ex, cancellationToken);
				}
			}

			throw new InvalidOperationException($"S3 operation retry loop terminated unexpectedly for {operationName}.");
		}

		private async Task DelayRetryAsync(string operationName, int attempt, Exception exception, CancellationToken cancellationToken)
		{
			var delay = TimeSpan.FromMilliseconds(150 * Math.Pow(2, attempt - 1));

			_logger.LogWarning(
				exception,
				"Transient S3 failure during {OperationName} on attempt {Attempt}. Retrying in {DelayMs} ms.",
				operationName,
				attempt,
				delay.TotalMilliseconds);

			await Task.Delay(delay, cancellationToken);
		}

		private bool IsTransient(AmazonServiceException exception)
		{
			return exception.StatusCode == HttpStatusCode.RequestTimeout
				   || (int)exception.StatusCode >= 500
				   || exception.InnerException is HttpRequestException
				   || exception.InnerException is IOException;
		}

		private static bool IsNotFound(AmazonS3Exception exception)
		{
			return exception.StatusCode == HttpStatusCode.NotFound
				   || string.Equals(exception.ErrorCode, "NoSuchKey", StringComparison.OrdinalIgnoreCase)
				   || string.Equals(exception.ErrorCode, "NotFound", StringComparison.OrdinalIgnoreCase);
		}

		private Uri BuildDirectObjectUrl(string objectKey)
		{
			if (!string.IsNullOrWhiteSpace(_options.Endpoint))
			{
				var endpointUri = GetEndpointUri();
				var authority = endpointUri.IsDefaultPort
					? endpointUri.Host
					: $"{endpointUri.Host}:{endpointUri.Port}";

				if (_options.ForcePathStyle)
				{
					return new Uri($"{endpointUri.Scheme}://{authority}/{_options.Bucket}/{objectKey}");
				}

				return new Uri($"{endpointUri.Scheme}://{_options.Bucket}.{authority}/{objectKey}");
			}

			return new Uri($"https://{_options.Bucket}.s3.{_options.Region}.amazonaws.com/{objectKey}");
		}

		private Uri GetEndpointUri()
		{
			if (Uri.TryCreate(_options.Endpoint, UriKind.Absolute, out var uri))
			{
				return uri;
			}

			var scheme = _options.UseSsl ? Uri.UriSchemeHttps : Uri.UriSchemeHttp;
			return new Uri($"{scheme}://{_options.Endpoint}");
		}

		private static string NormalizeEntityTag(string entityTag)
		{
			var trimmed = entityTag.Trim();

			return trimmed.StartsWith("\"", StringComparison.Ordinal)
				? trimmed
				: $"\"{trimmed.Trim('\"')}\"";
		}

		private static string CreateEntityTag(byte[] audioBytes)
		{
			using var sha256 = System.Security.Cryptography.SHA256.Create();
			return $"\"{Convert.ToHexString(sha256.ComputeHash(audioBytes)).ToLowerInvariant()}\"";
		}
	}
}
