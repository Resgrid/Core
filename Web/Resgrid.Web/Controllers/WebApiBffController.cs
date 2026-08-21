using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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
		private static readonly string[] AllowedPrefixes =
		{
			"api/v4/WeatherAlerts/",
			"api/v4/Geocoding/",
			"api/v4/Mapping/",
			"api/v4/Chat/",
			"api/v4/ChatModeration/",
			"api/v4/Chatbot/",
			"api/v4/Moderation/"
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
		public async Task Proxy(string path, CancellationToken cancellationToken)
		{
			var normalizedPath = (path ?? string.Empty).TrimStart('/');
			if (!IsAllowed(normalizedPath))
			{
				Response.StatusCode = StatusCodes.Status404NotFound;
				return;
			}

			if (!HttpMethods.IsGet(Request.Method) && !HttpMethods.IsHead(Request.Method))
			{
				try { await _antiforgery.ValidateRequestAsync(HttpContext); }
				catch (AntiforgeryValidationException)
				{
					Response.StatusCode = StatusCodes.Status400BadRequest;
					return;
				}
			}

			if (Request.ContentLength > 25L * 1024L * 1024L)
			{
				Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
				return;
			}

			try
			{
				var bearer = await GetServerTokenAsync(false, cancellationToken);
				if (string.IsNullOrWhiteSpace(bearer))
				{
					Response.StatusCode = StatusCodes.Status401Unauthorized;
					return;
				}

				var apiBase = new Uri(SystemBehaviorConfig.ResgridApiBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
				var target = new Uri(apiBase, normalizedPath + Request.QueryString);
				var canonicalPath = Uri.UnescapeDataString(target.AbsolutePath).TrimStart('/');
				if (!apiBase.IsBaseOf(target) || !IsAllowed(canonicalPath))
				{
					Response.StatusCode = StatusCodes.Status404NotFound;
					return;
				}
				using var outbound = new HttpRequestMessage(new HttpMethod(Request.Method), target);
				outbound.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
				outbound.Headers.TryAddWithoutValidation("Accept", Request.Headers.Accept.ToArray());
				outbound.Headers.TryAddWithoutValidation("Accept-Language", Request.Headers.AcceptLanguage.ToArray());
				outbound.Headers.TryAddWithoutValidation("X-Resgrid-Client", "web");

				if (Request.ContentLength.GetValueOrDefault() > 0 || HttpMethods.IsPost(Request.Method) ||
					HttpMethods.IsPut(Request.Method) || HttpMethods.IsPatch(Request.Method))
				{
					outbound.Content = new StreamContent(Request.Body);
					if (MediaTypeHeaderValue.TryParse(Request.ContentType, out var contentType))
						outbound.Content.Headers.ContentType = contentType;
				}

				using var upstream = await _httpClientFactory.CreateClient("ResgridWebBff")
					.SendAsync(outbound, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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
				Resgrid.Framework.Logging.LogException(ex, "Web API facade request failed.");
				if (!Response.HasStarted)
					Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
			}
		}

		[HttpPost("eventing-token")]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> EventingToken(CancellationToken cancellationToken)
		{
			var token = await GetServerTokenAsync(true, cancellationToken);
			return string.IsNullOrWhiteSpace(token)
				? Unauthorized()
				: Ok(new { accessToken = token, expiresIn = 120 });
		}

		private async Task<string> GetServerTokenAsync(bool eventingOnly, CancellationToken cancellationToken)
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
			if (_memoryCache.TryGetValue(cacheKey, out string cachedToken))
				return cachedToken;

			var tokenUri = new Uri(new Uri(SystemBehaviorConfig.ResgridApiBaseUrl.TrimEnd('/') + "/"),
				"api/v4/connect/token");
			using var request = new HttpRequestMessage(HttpMethod.Post, tokenUri);
			request.Headers.TryAddWithoutValidation("X-Resgrid-Internal-Key", ApiConfig.BackendInternalApikey);
			request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
			{
				["grant_type"] = "web_session",
				["user_id"] = userId,
				["session_id"] = sessionId,
				["auth_ver"] = generation,
				["department_id"] = departmentId,
				["token_use"] = eventingOnly ? "eventing" : "api"
			});

			using var response = await _httpClientFactory.CreateClient("ResgridWebBff")
				.SendAsync(request, cancellationToken);
			if (!response.IsSuccessStatusCode)
				return null;

			await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
			var token = await JsonSerializer.DeserializeAsync<BffTokenResponse>(stream,
				new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken);
			if (string.IsNullOrWhiteSpace(token?.AccessToken))
				return null;

			_memoryCache.Set(cacheKey, token.AccessToken, eventingOnly
				? TimeSpan.FromSeconds(90)
				: TimeSpan.FromMinutes(Math.Max(1, SessionSecurityConfig.WebBffAccessTokenLifetimeMinutes - 1)));
			return token.AccessToken;
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
		}
	}
}
