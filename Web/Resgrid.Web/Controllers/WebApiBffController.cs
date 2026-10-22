using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Resgrid.Config;
using Resgrid.Model.Security;

namespace Resgrid.Web.Controllers
{
	[Authorize]
	[Route("api/web-bff")]
	public class WebApiBffController : ControllerBase
	{
		private const long MaxRequestBodyBytes = 25L * 1024L * 1024L;

		private static readonly string[] AllowedPrefixes =
		{
			"api/v4/WeatherAlerts/",
			"api/v4/Geocoding/",
			"api/v4/Mapping/",
			"api/v4/Chat/",
			"api/v4/ChatModeration/",
			"api/v4/Chatbot/",
			"api/v4/Moderation/",
			"api/v4/AdminAssist/",
			// Avatars back every participant image the chat and personnel surfaces render, and
			// GetRecipients backs the message composer. Both are called by the app through this
			// facade, so leaving them off the list 404s them.
			"api/v4/Avatars/",
			"api/v4/Messages/",
			// Browser push: the page registers its own FCM web token and takes it off again on sign-out.
			"api/v4/Devices/"
		};

		private readonly IHttpClientFactory _httpClientFactory;
		private readonly IMemoryCache _memoryCache;
		private readonly IAntiforgery _antiforgery;

		public WebApiBffController(IHttpClientFactory httpClientFactory, IMemoryCache memoryCache,
			IAntiforgery antiforgery)
		{
			_httpClientFactory = httpClientFactory;
			_memoryCache = memoryCache;
			_antiforgery = antiforgery;
		}

		[AcceptVerbs("GET", "POST", "PUT", "PATCH", "DELETE")]
		[Route("{**path}")]
		// The API runs the work; a disconnect is handed on to it (an Admin Assist answer nobody will read stops there).
		[Filters.CancelOnClientDisconnect]
		public async Task Proxy(string path, CancellationToken cancellationToken)
		{
			// Per-user authenticated payloads on the app's own origin: never let a browser, CDN or any
			// intermediary retain them, and never let a sniffed content type turn a proxied body into
			// script on this origin. Set before any early return so error responses carry them too.
			Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
			Response.Headers.Pragma = "no-cache";
			Response.Headers["X-Content-Type-Options"] = "nosniff";

			var normalizedPath = (path ?? string.Empty).TrimStart('/');
			if (!IsAllowed(normalizedPath))
			{
				Response.StatusCode = StatusCodes.Status404NotFound;
				return;
			}

			if (!HttpMethods.IsGet(Request.Method))
			{
				// A form-carried antiforgery token makes validation read the body. Without buffering the
				// proxy would then forward an already-consumed stream and the upstream API would see an
				// empty request. A header-carried token never reaches this path.
				if (Request.HasFormContentType)
					Request.EnableBuffering(bufferThreshold: 64 * 1024, bufferLimit: MaxRequestBodyBytes);

				try { await _antiforgery.ValidateRequestAsync(HttpContext); }
				catch (AntiforgeryValidationException)
				{
					Response.StatusCode = StatusCodes.Status400BadRequest;
					return;
				}
				catch (IOException)
				{
					// The buffering limit tripped while the form was read.
					Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
					return;
				}

				if (Request.Body.CanSeek)
					Request.Body.Position = 0;
			}

			if (Request.ContentLength > MaxRequestBodyBytes)
			{
				Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
				return;
			}

			// A chunked request has no Content-Length, so the check above cannot see it. Lower Kestrel's
			// per-request limit instead: it is enforced as the body is read, whatever the framing.
			var maxBodySize = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
			if (maxBodySize is { IsReadOnly: false })
				maxBodySize.MaxRequestBodySize = MaxRequestBodyBytes;

			try
			{
				var token = await GetServerTokenAsync(false, cancellationToken);
				if (token == null)
				{
					// No usable session claims means this caller can never get a token; an upstream failure
					// is transient. Collapsing both to 401 makes the client sign the user out over a blip.
					Response.StatusCode = HasSessionClaims()
						? StatusCodes.Status503ServiceUnavailable
						: StatusCodes.Status401Unauthorized;
					return;
				}

				var bearer = token.AccessToken;

				var apiBase = new Uri(SystemBehaviorConfig.ResgridApiBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
				var target = new Uri(apiBase, normalizedPath + Request.QueryString);
				var canonicalPath = Uri.UnescapeDataString(target.AbsolutePath).TrimStart('/');
				if (!apiBase.IsBaseOf(target) || !IsAllowed(canonicalPath))
				{
					Response.StatusCode = StatusCodes.Status404NotFound;
					return;
				}
				// Admin Assist protection remains attended: forward the caller's grant only to the
				// validated first-party API, never the inference transport or token-exchange endpoint.
				string forwardedGrant = null;
				var adminAssist = canonicalPath.StartsWith("api/v4/AdminAssist/", StringComparison.OrdinalIgnoreCase);
				if (adminAssist && Request.Headers.TryGetValue("X-Resgrid-Protected-Grant", out var protectedGrant))
				{
					if (protectedGrant.Count != 1 || protectedGrant[0]?.Length > 8192 || protectedGrant[0]?.IndexOfAny(new[] { '\r', '\n' }) >= 0)
					{
						Response.StatusCode = StatusCodes.Status400BadRequest;
						return;
					}
					if (!string.IsNullOrWhiteSpace(protectedGrant[0]))
						forwardedGrant = protectedGrant[0];
				}

				// Without this every proxied call reaches the API as the web pod's address, so audit rows
				// and any per-IP policy upstream record the proxy rather than the person who acted.
				var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();

				var hasBody = Request.ContentLength.GetValueOrDefault() > 0 || HttpMethods.IsPost(Request.Method) ||
					HttpMethods.IsPut(Request.Method) || HttpMethods.IsPatch(Request.Method);

				HttpRequestMessage CreateOutbound()
				{
					var message = new HttpRequestMessage(new HttpMethod(Request.Method), target);
					message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
					message.Headers.TryAddWithoutValidation("Accept", Request.Headers.Accept.ToArray());
					message.Headers.TryAddWithoutValidation("Accept-Language", Request.Headers.AcceptLanguage.ToArray());
					message.Headers.TryAddWithoutValidation("X-Resgrid-Client", "web");
					if (forwardedGrant != null)
						message.Headers.TryAddWithoutValidation("X-Resgrid-Protected-Grant", forwardedGrant);
					if (!string.IsNullOrWhiteSpace(clientIp))
						message.Headers.TryAddWithoutValidation("X-Forwarded-For", clientIp);

					if (hasBody)
					{
						message.Content = new StreamContent(Request.Body);
						if (MediaTypeHeaderValue.TryParse(Request.ContentType, out var contentType))
							message.Content.Headers.ContentType = contentType;
					}

					return message;
				}

				using var outbound = CreateOutbound();
				using var proxyClient = _httpClientFactory.CreateClient("ResgridWebBff");
				// These handlers own a 90-second deadline plus bounded settlement. Other BFF routes
				// retain their 30-second timeout; disconnect cancellation still flows to the API.
				if (new[] { "api/v4/AdminAssist/Ask", "api/v4/AdminAssist/Conversation", "api/v4/AdminAssist/ConversationExport",
					"api/v4/AdminAssist/PlanDraft", "api/v4/AdminAssist/PlanCreate", "api/v4/AdminAssist/Plan", "api/v4/AdminAssist/PlanCommand", "api/v4/AdminAssist/PlanExport", "api/v4/AdminAssist/Plans",
					"api/v4/AdminAssist/Diagnose", "api/v4/AdminAssist/Diagnostic", "api/v4/AdminAssist/DiagnosticSupportPreview", "api/v4/AdminAssist/DiagnosticSupportExport" }
					.Any(route => canonicalPath.TrimEnd('/').Equals(route, StringComparison.OrdinalIgnoreCase)))
					proxyClient.Timeout = TimeSpan.FromSeconds(100);

				// Only a bodiless GET is safe to repeat; anything carrying a body has already consumed it.
				using var upstream = await SendWithReplayAsync(proxyClient, outbound,
					!hasBody && HttpMethods.IsGet(Request.Method) ? CreateOutbound : null,
					HttpCompletionOption.ResponseHeadersRead, cancellationToken);
				Response.StatusCode = (int)upstream.StatusCode;
				if (upstream.Content.Headers.ContentType != null)
					Response.ContentType = upstream.Content.Headers.ContentType.ToString();
				if (upstream.Content.Headers.ContentDisposition != null)
					Response.Headers.ContentDisposition = upstream.Content.Headers.ContentDisposition.ToString();
				if (upstream.Content.Headers.ContentLength.HasValue)
					Response.ContentLength = upstream.Content.Headers.ContentLength.Value;
				await upstream.Content.CopyToAsync(Response.Body, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				// An upstream timeout, reset or client disconnect is a handled dependency failure surfaced as
				// 503, not a process-fatal condition -- Fatal here buries real incidents under proxy noise.
				Resgrid.Framework.Logging.LogError(ex, $"Web API facade request failed for {normalizedPath}.");
				if (!Response.HasStarted)
					Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
			}
		}

		[HttpPost("eventing-token")]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> EventingToken(CancellationToken cancellationToken)
		{
			Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
			Response.Headers["X-Content-Type-Options"] = "nosniff";

			var token = await GetServerTokenAsync(true, cancellationToken);
			if (token == null)
				return HasSessionClaims() ? StatusCode(StatusCodes.Status503ServiceUnavailable) : Unauthorized();

			// Report what is actually left on this token, not its full minted lifetime: the same token is
			// served from cache for most of its life, so a fixed number tells later callers they have far
			// more time than they do and they reconnect with an already-expired token.
			var remaining = (int)Math.Floor((token.ExpiresOnUtc - DateTimeOffset.UtcNow).TotalSeconds);
			return Ok(new { accessToken = token.AccessToken, expiresIn = Math.Max(0, remaining) });
		}

		private bool HasSessionClaims() =>
			!string.IsNullOrWhiteSpace(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(ClaimTypes.PrimarySid)) &&
			!string.IsNullOrWhiteSpace(User.FindFirstValue(SessionClaimTypes.SessionId)) &&
			!string.IsNullOrWhiteSpace(User.FindFirstValue(SessionClaimTypes.AuthenticationGeneration)) &&
			!string.IsNullOrWhiteSpace(User.FindFirstValue(ClaimTypes.PrimaryGroupSid)) &&
			!string.IsNullOrWhiteSpace(ApiConfig.BackendInternalApikey);

		private async Task<CachedBffToken> GetServerTokenAsync(bool eventingOnly, CancellationToken cancellationToken)
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(ClaimTypes.PrimarySid);
			var sessionId = User.FindFirstValue(SessionClaimTypes.SessionId);
			var generation = User.FindFirstValue(SessionClaimTypes.AuthenticationGeneration);
			var departmentId = User.FindFirstValue(ClaimTypes.PrimaryGroupSid);
			if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(sessionId) ||
				string.IsNullOrWhiteSpace(generation) || string.IsNullOrWhiteSpace(departmentId) ||
				string.IsNullOrWhiteSpace(ApiConfig.BackendInternalApikey))
				return null;

			var cacheKey = $"web-bff:{(eventingOnly ? "eventing" : "api")}:{sessionId}:{generation}:{departmentId}";
			if (_memoryCache.TryGetValue(cacheKey, out CachedBffToken cachedToken) && cachedToken != null)
				return cachedToken;

			var authentication = await HttpContext.AuthenticateAsync();
			var credentialIssuedOn = authentication?.Properties?.IssuedUtc;

			var tokenUri = new Uri(new Uri(SystemBehaviorConfig.ResgridApiBaseUrl.TrimEnd('/') + "/"),
				"api/v4/connect/token");

			HttpRequestMessage CreateExchange()
			{
				var message = new HttpRequestMessage(HttpMethod.Post, tokenUri);
				message.Headers.TryAddWithoutValidation("X-Resgrid-Internal-Key", ApiConfig.BackendInternalApikey);
				message.Content = new FormUrlEncodedContent(new Dictionary<string, string>
				{
					["grant_type"] = "web_session",
					["user_id"] = userId,
					["session_id"] = sessionId,
					["auth_ver"] = generation,
					["department_id"] = departmentId,
					["token_use"] = eventingOnly ? "eventing" : "api",
					["credential_issued_on"] = credentialIssuedOn.HasValue
						? credentialIssuedOn.Value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)
						: string.Empty
				});
				return message;
			}

			// The web_session grant only validates the session and mints a short-lived access token (its one
			// write, the shared-session idle lock, is a compare-and-swap that lands once), so it is safe to repeat.
			using var request = CreateExchange();
			using var response = await SendWithReplayAsync(_httpClientFactory.CreateClient("ResgridWebBff"), request,
				CreateExchange, HttpCompletionOption.ResponseContentRead, cancellationToken);
			if (!response.IsSuccessStatusCode)
				return null;

			await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
			var token = await JsonSerializer.DeserializeAsync<BffTokenResponse>(stream,
				new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken);
			if (string.IsNullOrWhiteSpace(token?.AccessToken))
				return null;

			// The token endpoint is the authority on lifetime; the config values are only a fallback for a
			// response that omits expires_in.
			var lifetime = token.ExpiresIn > 0
				? TimeSpan.FromSeconds(token.ExpiresIn)
				: TimeSpan.FromMinutes(eventingOnly
					? Math.Max(1, SessionSecurityConfig.WebEventingAccessTokenLifetimeMinutes)
					: Math.Max(1, SessionSecurityConfig.WebBffAccessTokenLifetimeMinutes));

			var cached = new CachedBffToken(token.AccessToken, DateTimeOffset.UtcNow.Add(lifetime));

			// Retire the cached copy early so a token handed out at the end of its cache window still has
			// usable life left on it. Scaled to the token's own lifetime rather than fixed: a flat margin
			// is most of a short token and a rounding error on a long one.
			var margin = TimeSpan.FromSeconds(Math.Clamp(lifetime.TotalSeconds * 0.2, 30, 120));
			var cacheFor = lifetime - margin;
			if (cacheFor > TimeSpan.Zero)
				_memoryCache.Set(cacheKey, cached, cacheFor);

			return cached;
		}

		/// <summary>
		/// A pooled keep-alive connection the far end reset or closed as the request went out (idle-close race,
		/// ingress reload, API pod roll) fails the send, and the handler will not replay once bytes are on the
		/// wire. When the caller supplies <paramref name="rebuildForReplay"/> the request is rebuilt and sent
		/// once more on a fresh connection; null means the request is not safe to repeat.
		/// </summary>
		private static async Task<HttpResponseMessage> SendWithReplayAsync(HttpClient client, HttpRequestMessage request,
			Func<HttpRequestMessage> rebuildForReplay, HttpCompletionOption completionOption, CancellationToken cancellationToken)
		{
			try
			{
				return await client.SendAsync(request, completionOption, cancellationToken);
			}
			catch (HttpRequestException ex) when (rebuildForReplay != null && IsDroppedConnection(ex))
			{
				using var replay = rebuildForReplay();
				return await client.SendAsync(replay, completionOption, cancellationToken);
			}
		}

		private static bool IsDroppedConnection(HttpRequestException ex)
		{
			for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
				if (inner is IOException or SocketException)
					return true;
			return false;
		}

		private static bool IsAllowed(string path)
		{
			if (path.Contains("..", StringComparison.Ordinal) || path.Contains('\\') || path.Contains(':'))
				return false;
			return AllowedPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
		}

		private sealed class BffTokenResponse
		{
			[JsonPropertyName("access_token")]
			public string AccessToken { get; set; }

			[JsonPropertyName("expires_in")]
			public int ExpiresIn { get; set; }
		}

		private sealed record CachedBffToken(string AccessToken, DateTimeOffset ExpiresOnUtc);
	}
}
