using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Resgrid.Web.Tts.Configuration;
using System.Net;
using System.Net.Http;

namespace Resgrid.Web.Tts.Services
{
	public sealed class S3StorageService : IStorageService
	{
		private const int MaxRetryAttempts = 3;
		private const int PresignedPutUrlExpiryMinutes = 5;

		private readonly IAmazonS3 _s3Client;
		private readonly S3StorageOptions _options;
		private readonly ILogger<S3StorageService> _logger;
		private readonly IHttpClientFactory _httpClientFactory;

		public S3StorageService(
			IAmazonS3 s3Client,
			IOptions<S3StorageOptions> options,
			ILogger<S3StorageService> logger,
			IHttpClientFactory httpClientFactory)
		{
			_s3Client = s3Client;
			_options = options.Value;
			_logger = logger;
			_httpClientFactory = httpClientFactory;
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
			catch (AmazonUnmarshallingException ex) when (ex.InnerException is FormatException formatException)
			{
				return await HandleMalformedMetadataResponseAsync(objectKey, ex, formatException, cancellationToken);
			}
		}

		private async Task<bool> HandleMalformedMetadataResponseAsync(
			string objectKey,
			AmazonUnmarshallingException exception,
			FormatException formatException,
			CancellationToken cancellationToken)
		{
			_logger.LogWarning(
				exception,
				"The S3 client could not parse the metadata response for {ObjectKey}. Verifying existence with a presigned HEAD request. Inner format error: {InnerFormatErrorMessage}",
				objectKey,
				formatException.Message);

			_logger.LogDebug(
				formatException,
				"Inner FormatException while parsing the metadata response for {ObjectKey}. Last known location: {LastKnownLocation}.",
				objectKey,
				exception.LastKnownLocation ?? "unknown");

			try
			{
				var exists = await ExistsWithPresignedHeadAsync(objectKey, cancellationToken);

				_logger.LogDebug(
					"Presigned HEAD verification after the metadata parsing failure reported that {ObjectKey} {ExistenceState}.",
					objectKey,
					exists ? "exists" : "does not exist");

				return exists;
			}
			catch (AmazonServiceException verificationException)
			{
				_logger.LogWarning(
					verificationException,
					"Unable to verify whether {ObjectKey} exists after the metadata parsing failure. Assuming the object exists because S3 returned a response before the unmarshalling error.",
					objectKey);
			}
			catch (HttpRequestException verificationException)
			{
				_logger.LogWarning(
					verificationException,
					"Unable to verify whether {ObjectKey} exists after the metadata parsing failure due to connectivity. Assuming the object exists because S3 returned a response before the unmarshalling error.",
					objectKey);
			}
			catch (TaskCanceledException verificationException) when (!cancellationToken.IsCancellationRequested)
			{
				_logger.LogWarning(
					verificationException,
					"Unable to verify whether {ObjectKey} exists after the metadata parsing failure due to timeout. Assuming the object exists because S3 returned a response before the unmarshalling error.",
					objectKey);
			}
			catch (IOException verificationException)
			{
				_logger.LogWarning(
					verificationException,
					"Unable to verify whether {ObjectKey} exists after the metadata parsing failure due to IO. Assuming the object exists because S3 returned a response before the unmarshalling error.",
					objectKey);
			}

			// The metadata request reached S3 and only failed while the SDK parsed the response.
			// If the explicit presigned HEAD verification also fails, preserve the best-effort
			// behavior and assume the object exists so callers such as CacheService understand
			// this path can still return an optimistic result.
			return true;
		}

		public async Task UploadAsync(string objectKey, Stream content, string contentType, CancellationToken cancellationToken)
		{
			var payload = await ReadContentBytesAsync(content, cancellationToken);

			try
			{
				await ExecuteWithRetryAsync(
					() => UploadWithSdkAsync(objectKey, payload, contentType, cancellationToken),
					$"uploading {objectKey}",
					cancellationToken);
			}
			catch (FormatException ex)
			{
				await HandleMalformedPutResponseAsync(objectKey, payload, contentType, ex, cancellationToken);
			}
		}

		private async Task HandleMalformedPutResponseAsync(
			string objectKey,
			byte[] payload,
			string contentType,
			FormatException exception,
			CancellationToken cancellationToken)
		{
			_logger.LogWarning(
				exception,
				"The S3 client could not parse the PUT response for {ObjectKey}. Verifying whether the upload persisted before falling back to a presigned PUT upload.",
				objectKey);

			if (await WasUploadPersistedAsync(objectKey, cancellationToken))
			{
				_logger.LogInformation(
					"The upload for {ObjectKey} was verified after the PUT response parsing failure. Treating the upload as successful.",
					objectKey);

				return;
			}

			_logger.LogWarning(
				"The upload for {ObjectKey} could not be verified after the PUT response parsing failure. Retrying with a presigned PUT upload.",
				objectKey);

			await UploadWithPresignedUrlAsync(objectKey, payload, contentType, cancellationToken);
		}

		private async Task<bool> WasUploadPersistedAsync(string objectKey, CancellationToken cancellationToken)
		{
			try
			{
				return await ExistsAsync(objectKey, cancellationToken);
			}
			catch (AmazonServiceException ex)
			{
				_logger.LogWarning(
					ex,
					"Unable to verify whether {ObjectKey} exists after the PUT response parsing failure. Falling back to a presigned PUT upload.",
					objectKey);
			}
			catch (FormatException ex)
			{
				_logger.LogWarning(
					ex,
					"Unable to verify whether {ObjectKey} exists after the PUT response parsing failure because the metadata response could not be parsed. Falling back to a presigned PUT upload.",
					objectKey);
			}
			catch (HttpRequestException ex)
			{
				_logger.LogWarning(
					ex,
					"Unable to verify whether {ObjectKey} exists after the PUT response parsing failure due to connectivity. Falling back to a presigned PUT upload.",
					objectKey);
			}
			catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
			{
				_logger.LogWarning(
					ex,
					"Unable to verify whether {ObjectKey} exists after the PUT response parsing failure due to timeout. Falling back to a presigned PUT upload.",
					objectKey);
			}
			catch (IOException ex)
			{
				_logger.LogWarning(
					ex,
					"Unable to verify whether {ObjectKey} exists after the PUT response parsing failure due to IO. Falling back to a presigned PUT upload.",
					objectKey);
			}

			return false;
		}

		private Task<PutObjectResponse> UploadWithSdkAsync(string objectKey, byte[] payload, string contentType, CancellationToken cancellationToken)
		{
			return _s3Client.PutObjectAsync(
				new PutObjectRequest
				{
					BucketName = _options.Bucket,
					Key = objectKey,
					InputStream = new MemoryStream(payload, writable: false),
					ContentType = contentType
				},
				cancellationToken);
		}

		private async Task UploadWithPresignedUrlAsync(string objectKey, byte[] payload, string contentType, CancellationToken cancellationToken)
		{
			var client = _httpClientFactory.CreateClient(nameof(S3StorageService));

			for (var attempt = 1; attempt <= MaxRetryAttempts; attempt++)
			{
				using var request = new HttpRequestMessage(HttpMethod.Put, CreatePresignedPutUrl(objectKey, contentType));
				request.Content = new ByteArrayContent(payload);

				if (!string.IsNullOrWhiteSpace(contentType))
				{
					request.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
				}

				try
				{
					using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

					if (response.IsSuccessStatusCode)
					{
						return;
					}

					var exception = new HttpRequestException(
						$"Presigned PUT upload for {objectKey} failed with status code {(int)response.StatusCode}.",
						null,
						response.StatusCode);

					if (attempt < MaxRetryAttempts && IsTransientStatusCode(response.StatusCode))
					{
						await DelayRetryAsync($"uploading {objectKey} via presigned PUT", attempt, exception, cancellationToken);
						continue;
					}

					response.EnsureSuccessStatusCode();
				}
				catch (HttpRequestException ex) when (attempt < MaxRetryAttempts && (!ex.StatusCode.HasValue || IsTransientStatusCode(ex.StatusCode.Value)))
				{
					await DelayRetryAsync($"uploading {objectKey} via presigned PUT", attempt, ex, cancellationToken);
				}
				catch (IOException ex) when (attempt < MaxRetryAttempts)
				{
					await DelayRetryAsync($"uploading {objectKey} via presigned PUT", attempt, ex, cancellationToken);
				}
				catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested && attempt < MaxRetryAttempts)
				{
					await DelayRetryAsync($"uploading {objectKey} via presigned PUT", attempt, ex, cancellationToken);
				}
			}

			throw new InvalidOperationException($"Presigned PUT upload retry loop terminated unexpectedly for {objectKey}.");
		}

		private string CreatePresignedHeadUrl(string objectKey)
		{
			return _s3Client.GetPreSignedURL(new GetPreSignedUrlRequest
			{
				BucketName = _options.Bucket,
				Key = objectKey,
				Verb = HttpVerb.HEAD,
				Protocol = GetPresignedUrlProtocol(),
				Expires = DateTime.UtcNow.AddMinutes(PresignedPutUrlExpiryMinutes)
			});
		}

		private async Task<bool> ExistsWithPresignedHeadAsync(string objectKey, CancellationToken cancellationToken)
		{
			var client = _httpClientFactory.CreateClient(nameof(S3StorageService));

			for (var attempt = 1; attempt <= MaxRetryAttempts; attempt++)
			{
				using var request = new HttpRequestMessage(HttpMethod.Head, CreatePresignedHeadUrl(objectKey));

				try
				{
					using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

					if (response.StatusCode == HttpStatusCode.NotFound)
					{
						return false;
					}

					if (response.IsSuccessStatusCode)
					{
						return true;
					}

					var exception = new HttpRequestException(
						$"Presigned HEAD existence check for {objectKey} failed with status code {(int)response.StatusCode}.",
						null,
						response.StatusCode);

					if (attempt < MaxRetryAttempts && IsTransientStatusCode(response.StatusCode))
					{
						await DelayRetryAsync($"checking existence of {objectKey} via presigned HEAD", attempt, exception, cancellationToken);
						continue;
					}

					response.EnsureSuccessStatusCode();
				}
				catch (HttpRequestException ex) when (attempt < MaxRetryAttempts && (!ex.StatusCode.HasValue || IsTransientStatusCode(ex.StatusCode.Value)))
				{
					await DelayRetryAsync($"checking existence of {objectKey} via presigned HEAD", attempt, ex, cancellationToken);
				}
				catch (IOException ex) when (attempt < MaxRetryAttempts)
				{
					await DelayRetryAsync($"checking existence of {objectKey} via presigned HEAD", attempt, ex, cancellationToken);
				}
				catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested && attempt < MaxRetryAttempts)
				{
					await DelayRetryAsync($"checking existence of {objectKey} via presigned HEAD", attempt, ex, cancellationToken);
				}
			}

			throw new InvalidOperationException($"Presigned HEAD existence check retry loop terminated unexpectedly for {objectKey}.");
		}

		private string CreatePresignedPutUrl(string objectKey, string contentType)
		{
			return _s3Client.GetPreSignedURL(new GetPreSignedUrlRequest
			{
				BucketName = _options.Bucket,
				Key = objectKey,
				Verb = HttpVerb.PUT,
				Protocol = GetPresignedUrlProtocol(),
				ContentType = contentType,
				Expires = DateTime.UtcNow.AddMinutes(PresignedPutUrlExpiryMinutes)
			});
		}

		private static async Task<byte[]> ReadContentBytesAsync(Stream content, CancellationToken cancellationToken)
		{
			if (content.CanSeek)
			{
				content.Position = 0;
			}

			using var memoryStream = new MemoryStream();
			await content.CopyToAsync(memoryStream, cancellationToken);
			return memoryStream.ToArray();
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
					Protocol = GetPresignedUrlProtocol(),
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
			return IsTransientStatusCode(exception.StatusCode)
				   || exception.InnerException is HttpRequestException
				   || exception.InnerException is IOException;
		}

		private static bool IsTransientStatusCode(HttpStatusCode statusCode)
		{
			return statusCode == HttpStatusCode.RequestTimeout
				   || statusCode == HttpStatusCode.TooManyRequests
				   || (int)statusCode >= 500;
		}

		private static bool IsNotFound(AmazonS3Exception exception)
		{
			return exception.StatusCode == HttpStatusCode.NotFound
				   || string.Equals(exception.ErrorCode, "NoSuchKey", StringComparison.OrdinalIgnoreCase)
				   || string.Equals(exception.ErrorCode, "NotFound", StringComparison.OrdinalIgnoreCase);
		}

		private Protocol GetPresignedUrlProtocol()
		{
			if (Uri.TryCreate(_options.Endpoint, UriKind.Absolute, out var endpointUri))
			{
				if (string.Equals(endpointUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
				{
					return Protocol.HTTP;
				}

				if (string.Equals(endpointUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
				{
					return Protocol.HTTPS;
				}
			}

			return _options.UseSsl ? Protocol.HTTPS : Protocol.HTTP;
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
