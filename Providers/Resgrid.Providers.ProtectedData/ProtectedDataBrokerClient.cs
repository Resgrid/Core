using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.ProtectedData
{
	/// <summary>
	/// HTTP client for the Protected Data Broker's field-crypto endpoints (ADP plan section 3.1
	/// steps 7-9). Safe on Web/API hosts: it carries no key material and performs no cryptography —
	/// it forwards ciphertext/plaintext plus the caller's grant and the workload key, and maps every
	/// transport fault to a closed failure (broker_unavailable) with no partial results. Field
	/// values are never logged here; error handling touches only status codes and value-free error
	/// codes.
	/// </summary>
	public class ProtectedDataBrokerClient : IProtectedDataBrokerClient, IDisposable
	{
		internal const string WorkloadKeyHeader = "X-Resgrid-Broker-Key";
		internal const string BrokerUnavailableErrorCode = "broker_unavailable";

		private readonly HttpClient _httpClient;

		public ProtectedDataBrokerClient()
			: this(new HttpClientHandler())
		{
		}

		/// <summary>Test seam: inject a message handler.</summary>
		public ProtectedDataBrokerClient(HttpMessageHandler handler)
		{
			_httpClient = new HttpClient(handler, disposeHandler: true)
			{
				Timeout = TimeSpan.FromMilliseconds(DataProtectionConfig.BrokerTimeoutMs > 0
					? DataProtectionConfig.BrokerTimeoutMs
					: 10000)
			};
		}

		public bool IsConfigured => TryGetHttpsBaseUri(out _);

		/// <summary>
		/// The broker base URI, HTTPS only: requests carry the workload key, the caller's grant and
		/// protected field values — a plaintext http endpoint would expose all three, so it reads as
		/// "no broker configured" (fail closed) with a value-free log.
		/// </summary>
		private static bool TryGetHttpsBaseUri(out Uri baseUri)
		{
			baseUri = null;
			var configured = DataProtectionConfig.BrokerBaseUrl;
			if (string.IsNullOrWhiteSpace(configured))
				return false;

			if (!Uri.TryCreate(configured.TrimEnd('/') + "/", UriKind.Absolute, out baseUri))
			{
				Logging.LogError("DataProtectionConfig.BrokerBaseUrl is not a valid absolute URI; treating the broker as unconfigured.");
				baseUri = null;
				return false;
			}

			if (!string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
			{
				Logging.LogError("DataProtectionConfig.BrokerBaseUrl must use HTTPS; plaintext broker transport is prohibited. Treating the broker as unconfigured.");
				baseUri = null;
				return false;
			}

			return true;
		}

		public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
		{
			if (!TryGetHttpsBaseUri(out var baseUri))
				return false;

			try
			{
				using var response = await _httpClient.GetAsync(new Uri(baseUri, "health"), cancellationToken);
				return response.IsSuccessStatusCode;
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is UriFormatException)
			{
				return false;
			}
		}

		public Task<ProtectedDataBrokerResult> DecryptAsync(int departmentId, string grantToken, string requestId,
			IReadOnlyList<ProtectedFieldOperationItem> items, CancellationToken cancellationToken = default) =>
			SendAsync("api/v1/broker/decrypt", departmentId, grantToken, requestId, items, cancellationToken);

		public Task<ProtectedDataBrokerResult> EncryptAsync(int departmentId, string grantToken, string requestId,
			IReadOnlyList<ProtectedFieldOperationItem> items, CancellationToken cancellationToken = default) =>
			SendAsync("api/v1/broker/encrypt", departmentId, grantToken, requestId, items, cancellationToken);

		private async Task<ProtectedDataBrokerResult> SendAsync(string path, int departmentId, string grantToken,
			string requestId, IReadOnlyList<ProtectedFieldOperationItem> items, CancellationToken cancellationToken)
		{
			// HTTPS-only, enforced per request: the payload carries the workload key, the grant and
			// protected field values. A non-HTTPS configuration fails closed here.
			if (!TryGetHttpsBaseUri(out var baseUri))
				return Failed(BrokerUnavailableErrorCode);

			try
			{
				var payload = JsonConvert.SerializeObject(new
				{
					departmentId,
					grantToken,
					requestId,
					items
				});

				using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, path))
				{
					Content = new StringContent(payload, Encoding.UTF8, "application/json")
				};
				request.Headers.TryAddWithoutValidation(WorkloadKeyHeader, DataProtectionConfig.BrokerApiKey);

				using var response = await _httpClient.SendAsync(request, cancellationToken);
				var body = await response.Content.ReadAsStringAsync(cancellationToken);

				if (!response.IsSuccessStatusCode)
				{
					// The broker's failure body is a value-free result with an error code; surface it
					// when parseable, else the generic closed failure.
					var failure = TryDeserialize(body);
					if (failure != null && !string.IsNullOrWhiteSpace(failure.ErrorCode))
						return Failed(failure.ErrorCode);

					Logging.LogError($"Protected Data Broker call {path} failed with HTTP {(int)response.StatusCode}.");
					return Failed(BrokerUnavailableErrorCode);
				}

				var result = TryDeserialize(body);
				if (result == null)
				{
					Logging.LogError($"Protected Data Broker call {path} returned an unparseable body.");
					return Failed(BrokerUnavailableErrorCode);
				}

				return result;
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is UriFormatException)
			{
				Logging.LogError($"Protected Data Broker call {path} failed: {ex.GetType().Name}.");
				return Failed(BrokerUnavailableErrorCode);
			}
		}

		private static ProtectedDataBrokerResult TryDeserialize(string body)
		{
			try
			{
				return JsonConvert.DeserializeObject<ProtectedDataBrokerResult>(body);
			}
			catch (JsonException)
			{
				return null;
			}
		}

		private static ProtectedDataBrokerResult Failed(string errorCode) =>
			new ProtectedDataBrokerResult { Success = false, ErrorCode = errorCode };

		public void Dispose() => _httpClient.Dispose();
	}
}
