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
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;

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
		internal const string ClientIdHeader = "X-Resgrid-Broker-Client";
		internal const string HostHeader = "X-Resgrid-Broker-Host";

		// Informational only: lets the broker's legacy-key log name the calling process during the migration window.
		private static readonly string HostName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "unknown";
		internal const string BrokerUnavailableErrorCode = "broker_unavailable";

		// The client is scoped (its audit repository is), so the connection pool must outlive it: a handler per
		// scope would open a new TLS connection per request and leave the old sockets in TIME_WAIT.
		private static readonly HttpMessageHandler SharedHandler = new SocketsHttpHandler
		{
			PooledConnectionLifetime = TimeSpan.FromMinutes(5)
		};

		private readonly HttpClient _httpClient;
		private readonly IAdpAuditRepository _audit;
		private readonly IBrokerSessionAssertionService _assertions;
		private readonly IProtectedGrantContext _grantContext;

		public ProtectedDataBrokerClient(IAdpAuditRepository audit, IBrokerSessionAssertionService assertions,
			IProtectedGrantContext grantContext)
			: this(SharedHandler, audit, disposeHandler: false, assertions, grantContext)
		{
		}

		/// <summary>Test seam: inject a message handler.</summary>
		public ProtectedDataBrokerClient(HttpMessageHandler handler, IAdpAuditRepository audit,
			IBrokerSessionAssertionService assertions = null, IProtectedGrantContext grantContext = null)
			: this(handler, audit, disposeHandler: true, assertions, grantContext)
		{
		}

		private ProtectedDataBrokerClient(HttpMessageHandler handler, IAdpAuditRepository audit, bool disposeHandler,
			IBrokerSessionAssertionService assertions, IProtectedGrantContext grantContext)
		{
			_audit = audit;
			_assertions = assertions;
			_grantContext = grantContext;
			_httpClient = new HttpClient(handler, disposeHandler)
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

		/// <summary>
		/// The purpose-bound workload decrypt lane (RMS plan section 5.9.2): no grant, the workload key plus a
		/// named purpose the broker must have been configured to allow for this department. A broker without
		/// the lane answers 404 and the caller fails closed with <c>workload_purpose_denied</c>.
		/// </summary>
		public Task<ProtectedDataBrokerResult> DecryptForWorkloadAsync(int departmentId, string purpose, string requestId,
			IReadOnlyList<ProtectedFieldOperationItem> items, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(purpose))
				return Task.FromResult(Failed("workload_purpose_denied"));
			return SendAsync("api/v1/broker/workload/decrypt?purpose=" + Uri.EscapeDataString(purpose.Trim()), departmentId, null, requestId, items, cancellationToken);
		}

		private async Task<ProtectedDataBrokerResult> SendAsync(string path, int departmentId, string grantToken,
			string requestId, IReadOnlyList<ProtectedFieldOperationItem> items, CancellationToken cancellationToken)
		{
			await _audit.AppendAsync(new AdpAuditEvent { DepartmentId = departmentId, Layer = "application",
				Operation = path.Contains("decrypt") ? "decrypt" : "encrypt", Outcome = "requested", CorrelationId = requestId }, cancellationToken);
			var result = await SendCoreAsync(path, departmentId, grantToken, requestId, items, cancellationToken);
			await _audit.AppendAsync(new AdpAuditEvent { DepartmentId = departmentId, Layer = "application",
				Operation = path.Contains("decrypt") ? "decrypt" : "encrypt", Outcome = result.Success ? "completed" : "denied",
				CorrelationId = requestId }, cancellationToken);
			return result;
		}

		private async Task<ProtectedDataBrokerResult> SendCoreAsync(string path, int departmentId, string grantToken,
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
				AddCredentialHeaders(request);

				var assertion = TryMintSessionAssertion(path, departmentId, grantToken, requestId, items);
				if (assertion != null)
					request.Headers.TryAddWithoutValidation(BrokerSessionAssertion.HeaderName, assertion);

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

		/// <summary>
		/// This host's broker credential (passkey plan section 8.5): its own id and key when configured, otherwise the
		/// legacy shared key, which the broker accepts only during the migration window.
		/// </summary>
		internal static void AddCredentialHeaders(HttpRequestMessage request)
		{
			if (!string.IsNullOrWhiteSpace(DataProtectionConfig.BrokerClientId) && !string.IsNullOrWhiteSpace(DataProtectionConfig.BrokerClientKey))
			{
				request.Headers.TryAddWithoutValidation(ClientIdHeader, DataProtectionConfig.BrokerClientId.Trim());
				request.Headers.TryAddWithoutValidation(WorkloadKeyHeader, DataProtectionConfig.BrokerClientKey);
			}
			else
			{
				request.Headers.TryAddWithoutValidation(WorkloadKeyHeader, DataProtectionConfig.BrokerApiKey);
			}

			request.Headers.TryAddWithoutValidation(HostHeader, HostName);
		}

		/// <summary>
		/// The identity tier's statement of which live session is behind an attended request (passkey workbook section
		/// 6.2), minted only when a user grant is presented and the request passed session validation. Workload calls and
		/// release receipts carry none. A mint failure sends the request without one; the broker decides whether that is
		/// acceptable (never for a version 2 grant).
		/// </summary>
		private string TryMintSessionAssertion(string path, int departmentId, string grantToken, string requestId,
			IReadOnlyList<ProtectedFieldOperationItem> items)
		{
			if (_assertions == null || _grantContext == null || string.IsNullOrWhiteSpace(grantToken) ||
				grantToken.StartsWith("adpr.", StringComparison.Ordinal) || _grantContext.IsWorkloadCaller)
				return null;

			var operation = path.EndsWith("/decrypt", StringComparison.Ordinal) ? "decrypt"
				: path.EndsWith("/encrypt", StringComparison.Ordinal) ? "encrypt" : null;
			var session = _grantContext.Session;
			if (operation == null || session == null || string.IsNullOrWhiteSpace(_grantContext.UserId) || !_assertions.CanMint)
				return null;

			try
			{
				return _assertions.Mint(new BrokerSessionAssertion
				{
					UserId = _grantContext.UserId,
					SessionId = session.SessionId,
					AuthenticationGeneration = session.AuthenticationGeneration,
					DepartmentId = departmentId,
					ClientApplication = session.ClientApplication,
					SessionLockVersion = session.SessionLockVersion,
					CredentialIssuedOnUtc = session.CredentialIssuedOnUtc,
					RequestDigest = BrokerRequestDigest.Compute(operation, departmentId, requestId, items)
				});
			}
			catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException ||
				ex is System.Security.Cryptography.CryptographicException)
			{
				Logging.LogError($"Broker session assertion could not be minted: {ex.GetType().Name}.");
				return null;
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
