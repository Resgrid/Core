using System;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.ProtectedData
{
	/// <summary>
	/// OpenBao Transit implementation of <see cref="IKeyWrappingProvider"/> (ADP plan sections 2.4,
	/// 4.1, 13, A.8-A.9). Authenticates with the cert auth method over mTLS and uses exactly the two
	/// paths the broker policy grants: transit/datakey/wrapped/{key} and transit/decrypt/{key}, both
	/// with the mandatory per-department derived-key context (context = base64(DepartmentId)), so
	/// cross-department unwrap fails cryptographically at the KMS, not merely in broker code.
	///
	/// Fail-closed by design: authentication failure, HTTP errors, timeouts and malformed responses
	/// all throw — there is no fallback token, no cached credential, and no degradation to local
	/// crypto. Tokens are short-lived; an expiring token is replaced by a fresh cert login rather
	/// than renewed, so a revoked certificate stops service at the next refresh. This type belongs
	/// on Protected Data Broker hosts ONLY (register via ProtectedDataProviderModule); Web/API/worker
	/// hosts keep the fail-closed NotConfiguredKeyWrappingProvider.
	/// </summary>
	public class OpenBaoTransitKeyWrappingProvider : IKeyWrappingProvider, IDisposable
	{
		// Refresh the token this many seconds before its lease actually expires.
		private const int TokenRefreshSkewSeconds = 60;

		private readonly HttpClient _httpClient;
		private readonly SemaphoreSlim _tokenLock = new SemaphoreSlim(1, 1);
		private string _token;
		private DateTime _tokenExpiresUtc = DateTime.MinValue;

		public OpenBaoTransitKeyWrappingProvider()
			: this(CreateMtlsHandler())
		{
		}

		/// <summary>Test seam: inject a message handler; production uses the mTLS handler above.</summary>
		public OpenBaoTransitKeyWrappingProvider(HttpMessageHandler handler)
		{
			if (string.IsNullOrWhiteSpace(DataProtectionConfig.OpenBaoAddress))
				throw new InvalidOperationException("DataProtectionConfig.OpenBaoAddress is not configured.");

			_httpClient = new HttpClient(handler, disposeHandler: true)
			{
				BaseAddress = new Uri(DataProtectionConfig.OpenBaoAddress.TrimEnd('/') + "/"),
				Timeout = TimeSpan.FromMilliseconds(DataProtectionConfig.OpenBaoTimeoutMs > 0
					? DataProtectionConfig.OpenBaoTimeoutMs
					: 10000)
			};
		}

		public string ProviderType => "OpenBaoTransit";

		public async Task<WrappedDataKey> GenerateWrappedDataKeyAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			var mount = DataProtectionConfig.OpenBaoTransitMount;
			var keyName = DataProtectionConfig.OpenBaoTransitKeyName;

			var response = await SendAuthenticatedAsync(
				$"v1/{mount}/datakey/wrapped/{keyName}",
				new JObject
				{
					["context"] = DepartmentContext(departmentId),
					["bits"] = 256
				}, cancellationToken);

			var ciphertext = response["data"]?["ciphertext"]?.Value<string>();
			if (string.IsNullOrWhiteSpace(ciphertext))
				throw new CryptographicException("OpenBao datakey/wrapped returned no ciphertext.");

			return new WrappedDataKey
			{
				// The vault:vN:... token IS the wrapped blob; stored verbatim and handed back to
				// transit/decrypt unchanged.
				WrappedKeyBase64 = ciphertext,
				ProviderType = ProviderType,
				ProviderKeyReference = $"{mount}/{keyName}",
				ProviderKeyVersion = response["data"]?["key_version"]?.Value<int?>() ?? 0
			};
		}

		public async Task<byte[]> UnwrapDataKeyAsync(int departmentId, string wrappedKeyBase64, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(wrappedKeyBase64))
				throw new ArgumentException("Wrapped key is required.", nameof(wrappedKeyBase64));

			var mount = DataProtectionConfig.OpenBaoTransitMount;
			var keyName = DataProtectionConfig.OpenBaoTransitKeyName;

			var response = await SendAuthenticatedAsync(
				$"v1/{mount}/decrypt/{keyName}",
				new JObject
				{
					["ciphertext"] = wrappedKeyBase64,
					["context"] = DepartmentContext(departmentId)
				}, cancellationToken);

			var plaintextBase64 = response["data"]?["plaintext"]?.Value<string>();
			if (string.IsNullOrWhiteSpace(plaintextBase64))
				throw new CryptographicException("OpenBao decrypt returned no plaintext.");

			// Decode straight into pinned memory so the caller's ZeroMemory is not defeated by GC
			// compaction copies; the intermediate managed buffer from FromBase64String is avoided.
			var byteCount = ComputeBase64ByteCount(plaintextBase64);
			var dek = GC.AllocateArray<byte>(byteCount, pinned: true);
			if (!Convert.TryFromBase64String(plaintextBase64, dek, out var written) || written != byteCount)
			{
				CryptographicOperations.ZeroMemory(dek);
				throw new CryptographicException("OpenBao decrypt returned malformed plaintext encoding.");
			}

			return dek;
		}

		/// <summary>Per-department derived-key context: base64(DepartmentId), matching the A.8 ceremony.</summary>
		private static string DepartmentContext(int departmentId) =>
			Convert.ToBase64String(Encoding.UTF8.GetBytes(departmentId.ToString(CultureInfo.InvariantCulture)));

		private async Task<JObject> SendAuthenticatedAsync(string path, JObject body, CancellationToken cancellationToken)
		{
			var token = await EnsureTokenAsync(cancellationToken);

			using var request = new HttpRequestMessage(HttpMethod.Post, path)
			{
				Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json")
			};
			request.Headers.Add("X-Vault-Token", token);

			using var response = await _httpClient.SendAsync(request, cancellationToken);
			var content = await response.Content.ReadAsStringAsync(cancellationToken);

			if (!response.IsSuccessStatusCode)
			{
				// Never log the request body, token, or response content — value-free status only.
				Logging.LogError($"OpenBao request to '{path}' failed with status {(int)response.StatusCode}; failing closed.");
				throw new CryptographicException($"OpenBao request failed with status {(int)response.StatusCode}.");
			}

			try
			{
				return JObject.Parse(content);
			}
			catch (JsonReaderException)
			{
				throw new CryptographicException("OpenBao returned a malformed response.");
			}
		}

		private async Task<string> EnsureTokenAsync(CancellationToken cancellationToken)
		{
			if (TokenIsFresh())
				return _token;

			await _tokenLock.WaitAsync(cancellationToken);
			try
			{
				if (TokenIsFresh())
					return _token;

				// A fresh cert login replaces the expiring token instead of renewing it, so a revoked
				// broker certificate stops service at the next refresh. Login failure fails closed;
				// there is deliberately no cached or long-lived fallback (plan section 2.2).
				var loginBody = new JObject();
				if (!string.IsNullOrWhiteSpace(DataProtectionConfig.OpenBaoCertAuthRoleName))
					loginBody["name"] = DataProtectionConfig.OpenBaoCertAuthRoleName;

				using var request = new HttpRequestMessage(HttpMethod.Post, "v1/auth/cert/login")
				{
					Content = new StringContent(loginBody.ToString(Formatting.None), Encoding.UTF8, "application/json")
				};

				using var response = await _httpClient.SendAsync(request, cancellationToken);
				var content = await response.Content.ReadAsStringAsync(cancellationToken);

				if (!response.IsSuccessStatusCode)
				{
					Logging.LogError($"OpenBao cert login failed with status {(int)response.StatusCode}; protected operations fail closed.");
					throw new CryptographicException($"OpenBao authentication failed with status {(int)response.StatusCode}.");
				}

				JObject parsed;
				try
				{
					parsed = JObject.Parse(content);
				}
				catch (JsonReaderException)
				{
					throw new CryptographicException("OpenBao authentication returned a malformed response.");
				}

				var token = parsed["auth"]?["client_token"]?.Value<string>();
				var leaseSeconds = parsed["auth"]?["lease_duration"]?.Value<int?>() ?? 0;
				if (string.IsNullOrWhiteSpace(token) || leaseSeconds <= 0)
					throw new CryptographicException("OpenBao authentication returned no usable token.");

				_token = token;
				_tokenExpiresUtc = DateTime.UtcNow.AddSeconds(Math.Max(leaseSeconds - TokenRefreshSkewSeconds, 1));
				return _token;
			}
			finally
			{
				_tokenLock.Release();
			}
		}

		private bool TokenIsFresh() => !string.IsNullOrEmpty(_token) && DateTime.UtcNow < _tokenExpiresUtc;

		private static int ComputeBase64ByteCount(string base64)
		{
			var padding = 0;
			if (base64.EndsWith("==", StringComparison.Ordinal))
				padding = 2;
			else if (base64.EndsWith("=", StringComparison.Ordinal))
				padding = 1;

			return (base64.Length * 3 / 4) - padding;
		}

		private static SocketsHttpHandler CreateMtlsHandler()
		{
			if (string.IsNullOrWhiteSpace(DataProtectionConfig.OpenBaoClientCertificatePath))
				throw new InvalidOperationException(
					"DataProtectionConfig.OpenBaoClientCertificatePath is not configured. The OpenBao cert auth method requires the broker's mTLS client certificate.");

			var certificate = string.IsNullOrEmpty(DataProtectionConfig.OpenBaoClientCertificatePassword)
				? X509CertificateLoader.LoadPkcs12FromFile(DataProtectionConfig.OpenBaoClientCertificatePath, null)
				: X509CertificateLoader.LoadPkcs12FromFile(DataProtectionConfig.OpenBaoClientCertificatePath,
					DataProtectionConfig.OpenBaoClientCertificatePassword);

			var handler = new SocketsHttpHandler();
			handler.SslOptions.ClientCertificates = new X509CertificateCollection { certificate };
			return handler;
		}

		public void Dispose()
		{
			_httpClient.Dispose();
			_tokenLock.Dispose();
		}
	}
}
