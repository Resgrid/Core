using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Resgrid.Web.Tts.Configuration;

namespace Resgrid.Web.Tts.Services
{
	public sealed class S3StorageService : IStorageService
	{
		private const int MaxRetryAttempts = 3;
		private const int PresignedUrlExpiryMinutes = 5;

		// The AWS SDK could not parse the Expiration header value "2026-05-04T00:00:00Z"
		// returned by RustFS / MinIO.  We handle all header parsing ourselves to avoid
		// this class of failure entirely.
		private static readonly string[] Iso8601Formats =
		{
			"yyyy-MM-ddTHH:mm:ssZ",
			"yyyy-MM-ddTHH:mm:ss.fZ",
			"yyyy-MM-ddTHH:mm:ss.ffZ",
			"yyyy-MM-ddTHH:mm:ss.fffZ",
			"yyyy-MM-ddTHH:mm:ss.ffffZ",
			"yyyy-MM-ddTHH:mm:ss.fffffZ",
			"yyyy-MM-ddTHH:mm:ss.ffffffZ",
			"yyyy-MM-ddTHH:mm:ss.fffffffZ",
			"yyyy-MM-ddTHH:mm:sszzz",
			"yyyy-MM-ddTHH:mm:ss.fzzz",
			"yyyy-MM-ddTHH:mm:ss.ffzzz",
			"yyyy-MM-ddTHH:mm:ss.fffzzz",
			"yyyy-MM-ddTHH:mm:ss.ffffzzz",
			"yyyy-MM-ddTHH:mm:ss.fffffzzz",
			"yyyy-MM-ddTHH:mm:ss.ffffffzzz",
			"yyyy-MM-ddTHH:mm:ss.fffffffzzz",
		};

		private readonly IHttpClientFactory _httpClientFactory;
		private readonly S3StorageOptions _options;
		private readonly ILogger<S3StorageService> _logger;

		public S3StorageService(
			IHttpClientFactory httpClientFactory,
			IOptions<S3StorageOptions> options,
			ILogger<S3StorageService> logger)
		{
			_httpClientFactory = httpClientFactory;
			_options = options.Value;
			_logger = logger;
		}

		// -----------------------------------------------------------------
		//  IStorageService  implementation
		// -----------------------------------------------------------------

		public async Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken)
		{
			try
			{
				using var response = await SendSignedRequestAsync(
					HttpMethod.Head,
					objectKey,
					content: null,
					contentType: null,
					cancellationToken);

				return response.StatusCode switch
				{
					HttpStatusCode.OK => true,
					HttpStatusCode.NotFound => false,
					_ => throw new HttpRequestException(
						$"HEAD {objectKey} returned unexpected status {(int)response.StatusCode}.",
						null,
						response.StatusCode)
				};
			}
			catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
			{
				return false;
			}
		}

		public async Task UploadAsync(string objectKey, Stream content, string contentType, CancellationToken cancellationToken)
		{
			using var memoryStream = new MemoryStream();
			await content.CopyToAsync(memoryStream, cancellationToken);
			var payload = memoryStream.ToArray();

			await ExecuteWithRetryAsync(
				async () =>
				{
					using var response = await SendSignedRequestAsync(
						HttpMethod.Put,
						objectKey,
						payload,
						contentType,
						cancellationToken);

					response.EnsureSuccessStatusCode();
				},
				$"uploading {objectKey}",
				cancellationToken);
		}

		public async Task<TtsAudioContent?> GetObjectAsync(string objectKey, CancellationToken cancellationToken)
		{
			try
			{
				using var response = await SendSignedRequestAsync(
					HttpMethod.Get,
					objectKey,
					content: null,
					contentType: null,
					cancellationToken);

				response.EnsureSuccessStatusCode();

				var responseContentType = response.Content.Headers.ContentType?.ToString();
				var contentType = string.IsNullOrWhiteSpace(responseContentType)
					? "audio/wav"
					: responseContentType;

				var entityTag = response.Headers.ETag?.Tag;
				if (string.IsNullOrWhiteSpace(entityTag))
				{
					entityTag = response.Content.Headers.TryGetValues("ETag", out var etagValues)
						? string.Join(",", etagValues)
						: null;
				}

				var lastModified = response.Content.Headers.LastModified
					?? DateTimeOffset.UtcNow;

				var audioBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

				return new TtsAudioContent(
					audioBytes,
					contentType,
					NormalizeEntityTag(entityTag),
					lastModified);
			}
			catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
			{
				return null;
			}
		}

		public Task<Uri> GetObjectUrlAsync(string objectKey, CancellationToken cancellationToken)
		{
			// If a public base URL is configured, use it directly.
			if (!string.IsNullOrWhiteSpace(_options.PublicBaseUrl))
			{
				return Task.FromResult(new Uri($"{_options.PublicBaseUrl.TrimEnd('/')}/{objectKey}"));
			}

			if (_options.UsePresignedUrls)
			{
				var url = CreatePresignedGetUrl(objectKey);
				return Task.FromResult(new Uri(url));
			}

			return Task.FromResult(BuildDirectObjectUrl(objectKey));
		}

		// -----------------------------------------------------------------
		//  HTTP request helpers
		// -----------------------------------------------------------------

		private async Task<HttpResponseMessage> SendSignedRequestAsync(
			HttpMethod method,
			string objectKey,
			byte[]? content,
			string? contentType,
			CancellationToken cancellationToken)
		{
			var url = BuildObjectUrl(objectKey);
			using var request = new HttpRequestMessage(method, url);

			// Add the Date header for SigV4 signing.
			var now = DateTimeOffset.UtcNow;
			request.Headers.Add("x-amz-date", now.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture));

			if (content is not null)
			{
				request.Content = new ByteArrayContent(content);
				if (!string.IsNullOrWhiteSpace(contentType))
				{
					request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
				}

				// Compute and add the content SHA-256 header.
				var payloadHash = HexSha256(content);
				request.Headers.Add("x-amz-content-sha256", payloadHash);
				request.Content.Headers.Add("x-amz-content-sha256", payloadHash);
			}
			else
			{
				request.Headers.Add("x-amz-content-sha256", "UNSIGNED-PAYLOAD");
			}

			// Add the Host header required by SigV4.
			request.Headers.Host = GetHost();

			// Compute and add the Authorization header.
			var scope = BuildScope(now);
			var signedHeaders = "host;x-amz-content-sha256;x-amz-date";
			var signature = CalculateSignature(method, objectKey, content, contentType, now, scope, signedHeaders);
			var credential = $"{_options.AccessKey}/{scope}";

			request.Headers.TryAddWithoutValidation(
				"Authorization",
				$"AWS4-HMAC-SHA256 Credential={credential},SignedHeaders={signedHeaders},Signature={signature}");

			return await CreateClient().SendAsync(request, cancellationToken);
		}

		private string CreatePresignedGetUrl(string objectKey)
		{
			var now = DateTimeOffset.UtcNow;
			var expires = PresignedUrlExpiryMinutes * 60;
			var scope = BuildScope(now);
			var signedHeaders = "host";

			var canonicalUri = BuildCanonicalUri(objectKey);
			var host = GetHost();

			// Query params for presigned URL (must be sorted).
			var queryParams = new SortedDictionary<string, string>(StringComparer.Ordinal)
			{
				["X-Amz-Algorithm"] = "AWS4-HMAC-SHA256",
				["X-Amz-Credential"] = $"{_options.AccessKey}/{scope}",
				["X-Amz-Date"] = now.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture),
				["X-Amz-Expires"] = expires.ToString(CultureInfo.InvariantCulture),
				["X-Amz-SignedHeaders"] = signedHeaders,
			};

			var canonicalQueryString = string.Join("&",
				queryParams.Select(kvp => $"{UrlEncode(kvp.Key)}={UrlEncode(kvp.Value)}"));

			// Null content for presigned GET — payload hash is UNSIGNED-PAYLOAD.
			var signature = CalculateSignature(
				HttpMethod.Get,
				objectKey,
				content: null,
				contentType: null,
				now,
				scope,
				signedHeaders,
				canonicalQueryStringOverride: canonicalQueryString);

			// Build final URL.
			var baseUrl = BuildObjectUrl(objectKey);
			return $"{baseUrl}?{canonicalQueryString}&X-Amz-Signature={signature}";
		}

		// -----------------------------------------------------------------
		//  Signature V4 helpers
		// -----------------------------------------------------------------

		private string CalculateSignature(
			HttpMethod method,
			string objectKey,
			byte[]? content,
			string? contentType,
			DateTimeOffset now,
			string scope,
			string signedHeaders,
			string? canonicalQueryStringOverride = null)
		{
			var canonicalUri = BuildCanonicalUri(objectKey);
			var canonicalQueryString = canonicalQueryStringOverride ?? string.Empty;

			var canonicalHeaders =
				$"host:{GetHost()}\n" +
				$"x-amz-content-sha256:{(content is not null ? HexSha256(content) : "UNSIGNED-PAYLOAD")}\n" +
				$"x-amz-date:{now:yyyyMMddTHHmmssZ}\n";

			var payloadHash = content is not null ? HexSha256(content) : "UNSIGNED-PAYLOAD";

			var canonicalRequest = string.Join('\n',
				method.Method,
				canonicalUri,
				canonicalQueryString,
				canonicalHeaders,
				signedHeaders,
				payloadHash);

			var stringToSign = string.Join('\n',
				"AWS4-HMAC-SHA256",
				now.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture),
				scope,
				HexSha256(Encoding.UTF8.GetBytes(canonicalRequest)));

			var signingKey = DeriveSigningKey(now);
			var signature = HmacSha256(signingKey, Encoding.UTF8.GetBytes(stringToSign));

			return Convert.ToHexString(signature).ToLowerInvariant();
		}

		private byte[] DeriveSigningKey(DateTimeOffset now)
		{
			var date = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

			var kDate = HmacSha256(
				Encoding.UTF8.GetBytes($"AWS4{_options.SecretKey}"),
				Encoding.UTF8.GetBytes(date));

			var kRegion = HmacSha256(kDate, Encoding.UTF8.GetBytes(_options.Region));

			var kService = HmacSha256(kRegion, Encoding.UTF8.GetBytes("s3"));

			return HmacSha256(kService, Encoding.UTF8.GetBytes("aws4_request"));
		}

		// -----------------------------------------------------------------
		//  URL builders
		// -----------------------------------------------------------------

		private string BuildObjectUrl(string objectKey)
		{
			var endpointUri = GetEndpointUri();
			var authority = endpointUri.IsDefaultPort
				? endpointUri.Host
				: $"{endpointUri.Host}:{endpointUri.Port}";

			if (_options.ForcePathStyle)
			{
				return $"{endpointUri.Scheme}://{authority}/{_options.Bucket}/{objectKey}";
			}

			return $"{endpointUri.Scheme}://{_options.Bucket}.{authority}/{objectKey}";
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

		private string BuildCanonicalUri(string objectKey) => $"/{_options.Bucket}/{objectKey}";

		private string GetHost()
		{
			var endpointUri = GetEndpointUri();

			if (_options.ForcePathStyle)
			{
				return endpointUri.IsDefaultPort
					? endpointUri.Host
					: $"{endpointUri.Host}:{endpointUri.Port}";
			}

			return endpointUri.IsDefaultPort
				? $"{_options.Bucket}.{endpointUri.Host}"
				: $"{_options.Bucket}.{endpointUri.Host}:{endpointUri.Port}";
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

		private string BuildScope(DateTimeOffset now)
		{
			return string.Concat(
				now.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
				"/",
				_options.Region,
				"/s3/aws4_request");
		}

		// -----------------------------------------------------------------
		//  Retry helpers
		// -----------------------------------------------------------------

		private async Task ExecuteWithRetryAsync(
			Func<Task> operation,
			string operationName,
			CancellationToken cancellationToken)
		{
			for (var attempt = 1; attempt <= MaxRetryAttempts; attempt++)
			{
				try
				{
					await operation();
					return;
				}
				catch (HttpRequestException ex) when (attempt < MaxRetryAttempts && IsTransientStatusCode(ex.StatusCode))
				{
					await DelayRetryAsync(operationName, attempt, ex, cancellationToken);
				}
				catch (IOException ex) when (attempt < MaxRetryAttempts)
				{
					await DelayRetryAsync(operationName, attempt, ex, cancellationToken);
				}
				catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested && attempt < MaxRetryAttempts)
				{
					await DelayRetryAsync(operationName, attempt, ex, cancellationToken);
				}
			}

			throw new InvalidOperationException(
				$"S3 operation retry loop terminated unexpectedly for {operationName}.");
		}

		private async Task DelayRetryAsync(
			string operationName,
			int attempt,
			Exception exception,
			CancellationToken cancellationToken)
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

		private static bool IsTransientStatusCode(HttpStatusCode? statusCode)
		{
			return statusCode.HasValue
				&& (statusCode == HttpStatusCode.RequestTimeout
					|| statusCode == HttpStatusCode.TooManyRequests
					|| (int)statusCode >= 500);
		}

		// -----------------------------------------------------------------
		//  HttpClient management
		// -----------------------------------------------------------------

		private HttpClient CreateClient()
	{
		var client = _httpClientFactory.CreateClient(nameof(S3StorageService));

		// Set a reasonable timeout. This should be generous enough for
		// large audio file uploads / downloads while still failing fast
		// on a genuinely hung connection.
		client.Timeout = TimeSpan.FromMinutes(2);

		return client;
	}

	// -----------------------------------------------------------------
	//  Crypto / encoding helpers

		private static byte[] HmacSha256(byte[] key, byte[] data)
		{
			using var hmac = new HMACSHA256(key);
			return hmac.ComputeHash(data);
		}

		private static string HexSha256(byte[] data)
		{
			return Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
		}

		private static string UrlEncode(string value)
		{
			return Uri.EscapeDataString(value);
		}

		private static string NormalizeEntityTag(string? entityTag)
		{
			if (string.IsNullOrWhiteSpace(entityTag))
			{
				return $"\"{Guid.NewGuid():N}\"";
			}

			var trimmed = entityTag.Trim();

			return trimmed.StartsWith('"')
				? trimmed
				: $"\"{trimmed}\"";
		}
	}
}
