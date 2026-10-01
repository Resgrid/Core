using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Authentication
{
	/// <summary>
	/// The HTTP side of brokered OIDC (passkey plan section 7.7.2): discovery and signing keys, cached per authority for
	/// an hour, and the authorization-code exchange. Every URL must be https, redirects are not followed, responses are
	/// size-limited, and a failure returns nothing rather than a guess.
	/// </summary>
	public sealed class OidcProviderClient : IOidcProviderClient
	{
		private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(1);

		// A key rotation may refetch, but never more often than this per authority.
		private static readonly TimeSpan RefreshFloor = TimeSpan.FromSeconds(30);

		private static readonly HttpClient Http = new(new SocketsHttpHandler
		{
			AllowAutoRedirect = false,
			PooledConnectionLifetime = TimeSpan.FromMinutes(10)
		})
		{
			Timeout = TimeSpan.FromSeconds(10),
			MaxResponseContentBufferSize = 1_048_576
		};

		private static readonly ConcurrentDictionary<string, (OidcProviderMetadata Metadata, DateTime FetchedOnUtc)> Cache =
			new(StringComparer.OrdinalIgnoreCase);

		public async Task<OidcProviderMetadata> GetMetadataAsync(string authority, bool forceRefresh = false, CancellationToken cancellationToken = default)
		{
			if (!IsHttps(authority))
				return null;

			var key = authority.TrimEnd('/');
			var now = DateTime.UtcNow;
			if (Cache.TryGetValue(key, out var cached))
			{
				var age = now - cached.FetchedOnUtc;
				if (age < CacheLifetime && (!forceRefresh || age < RefreshFloor))
					return cached.Metadata;
			}

			try
			{
				using var discovery = JsonDocument.Parse(await Http.GetStringAsync($"{key}/.well-known/openid-configuration", cancellationToken));
				var root = discovery.RootElement;
				var issuer = Read(root, "issuer");
				var authorizationEndpoint = Read(root, "authorization_endpoint");
				var tokenEndpoint = Read(root, "token_endpoint");
				var jwksUri = Read(root, "jwks_uri");
				if (!IsHttps(issuer) || !IsHttps(authorizationEndpoint) || !IsHttps(tokenEndpoint) || !IsHttps(jwksUri))
					return null;

				var metadata = new OidcProviderMetadata
				{
					Issuer = issuer,
					AuthorizationEndpoint = authorizationEndpoint,
					TokenEndpoint = tokenEndpoint,
					JwksJson = await Http.GetStringAsync(jwksUri, cancellationToken)
				};
				Cache[key] = (metadata, now);
				return metadata;
			}
			catch (Exception ex) when (ex is HttpRequestException || ex is JsonException || ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)
			{
				Framework.Logging.LogDebug($"OIDC discovery failed for {key}: {ex.GetType().Name}: {ex.Message}");
				return null;
			}
		}

		public async Task<OidcCodeExchangeResult> ExchangeCodeAsync(string tokenEndpoint, IReadOnlyDictionary<string, string> form,
			CancellationToken cancellationToken = default)
		{
			if (!IsHttps(tokenEndpoint) || form == null)
				return new OidcCodeExchangeResult { Error = "invalid_request" };

			try
			{
				using var content = new FormUrlEncodedContent(form);
				using var response = await Http.PostAsync(tokenEndpoint, content, cancellationToken);
				var body = await response.Content.ReadAsStringAsync(cancellationToken);
				using var json = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
				var idToken = Read(json.RootElement, "id_token");
				return response.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(idToken)
					? new OidcCodeExchangeResult { IdToken = idToken }
					: new OidcCodeExchangeResult { Error = Read(json.RootElement, "error") ?? "invalid_grant" };
			}
			catch (Exception ex) when (ex is HttpRequestException || ex is JsonException || ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)
			{
				Framework.Logging.LogDebug($"OIDC code exchange failed: {ex.GetType().Name}: {ex.Message}");
				return new OidcCodeExchangeResult { Error = "temporarily_unavailable" };
			}
		}

		private static string Read(JsonElement element, string name) =>
			element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
				? value.GetString()
				: null;

		private static bool IsHttps(string value) =>
			Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo);
	}
}
