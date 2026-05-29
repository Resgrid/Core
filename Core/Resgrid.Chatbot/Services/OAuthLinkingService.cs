using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Resgrid.Chatbot.Config;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;
using Resgrid.Framework;

namespace Resgrid.Chatbot.Services
{
	/// <summary>
	/// OAuth2 linking service for Discord and Slack. Performs the authorization-code exchange
	/// SERVER-SIDE: the caller never supplies the external user id. Instead the user is sent to the
	/// platform's authorize page (with a CSRF <c>state</c> bound to their Resgrid user), the platform
	/// returns a <c>code</c>, and this service exchanges it for a token and reads the verified
	/// external user id from the platform. This prevents a caller from linking an arbitrary id.
	/// </summary>
	public class OAuthLinkingService
	{
		private static readonly HttpClient _http = new HttpClient();

		// CSRF state -> initiating user/platform. In-memory; states are short-lived and single-use.
		private static readonly ConcurrentDictionary<string, OAuthState> _states = new();

		private readonly IChatbotUserIdentityService _userIdentityService;

		public OAuthLinkingService(IChatbotUserIdentityService userIdentityService)
		{
			_userIdentityService = userIdentityService;
		}

		/// <summary>
		/// Begins an OAuth link: generates a CSRF state bound to the user and returns the platform
		/// authorize URL the user should be redirected to.
		/// </summary>
		public OAuthStartResult StartLink(string resgridUserId, ChatbotPlatform platform)
		{
			if (string.IsNullOrWhiteSpace(resgridUserId))
				return OAuthStartResult.Fail("Missing user.");

			if (platform != ChatbotPlatform.Discord && platform != ChatbotPlatform.Slack)
				return OAuthStartResult.Fail("OAuth linking is only supported for Discord and Slack.");

			var (clientId, _) = GetCredentials(platform);
			if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(ChatbotConfig.OAuthRedirectUri))
				return OAuthStartResult.Fail("OAuth is not configured for this platform.");

			PurgeExpiredStates();

			var state = GenerateState();
			_states[state] = new OAuthState { UserId = resgridUserId, Platform = platform, CreatedAt = DateTime.UtcNow };

			var redirect = Uri.EscapeDataString(ChatbotConfig.OAuthRedirectUri);
			var url = platform == ChatbotPlatform.Discord
				? $"https://discord.com/oauth2/authorize?client_id={Uri.EscapeDataString(clientId)}&response_type=code&scope=identify&redirect_uri={redirect}&state={state}"
				: $"https://slack.com/oauth/v2/authorize?client_id={Uri.EscapeDataString(clientId)}&user_scope=users:read&redirect_uri={redirect}&state={state}";

			return OAuthStartResult.Ok(url, state);
		}

		/// <summary>
		/// Completes an OAuth link: validates the CSRF state belongs to the calling user, exchanges
		/// the authorization code server-side, derives the verified external user id, and links it.
		/// </summary>
		public async Task<LinkResult> ExchangeAndLinkAsync(string resgridUserId, ChatbotPlatform platform, string code, string state)
		{
			if (string.IsNullOrWhiteSpace(resgridUserId) || string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
				return LinkResult.Fail("Missing required parameters.");

			// Validate and consume the CSRF state.
			if (!_states.TryRemove(state, out var stored))
				return LinkResult.Fail("Invalid or expired linking request. Please start again.");

			if (DateTime.UtcNow > stored.CreatedAt.AddMinutes(10))
				return LinkResult.Fail("Linking request expired. Please start again.");

			// The state must belong to the authenticated caller and the platform they're completing.
			if (stored.UserId != resgridUserId || stored.Platform != platform)
				return LinkResult.Fail("Linking request does not match the current user.");

			try
			{
				var (externalUserId, externalUsername) = platform == ChatbotPlatform.Discord
					? await ExchangeDiscordAsync(code)
					: await ExchangeSlackAsync(code);

				if (string.IsNullOrWhiteSpace(externalUserId))
					return LinkResult.Fail("Could not verify your account with the platform.");

				// If this platform identity is already linked to a different user, refuse.
				var existing = await _userIdentityService.GetIdentityAsync(platform, externalUserId);
				if (existing != null && existing.UserId != resgridUserId)
					return LinkResult.Fail("This account is already linked to a different user.");

				var identity = await _userIdentityService.LinkUserAsync(
					resgridUserId, platform, externalUserId, externalUsername ?? string.Empty, "oauth2");

				return LinkResult.Ok("Account linked successfully.", identity);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return LinkResult.Fail("Failed to complete account linking. Please try again.");
			}
		}

		/// <summary>
		/// Unlinks a platform account from a Resgrid user.
		/// </summary>
		public async Task<LinkResult> UnlinkAsync(string resgridUserId, ChatbotPlatform platform, string externalUserId)
		{
			var identity = await _userIdentityService.GetIdentityAsync(platform, externalUserId);
			if (identity == null)
				return LinkResult.Fail("No linked account found.");

			if (identity.UserId != resgridUserId)
				return LinkResult.Fail("This account is linked to a different user.");

			await _userIdentityService.RemoveLinkAsync(resgridUserId, platform);
			return LinkResult.Ok("Account unlinked successfully.", null);
		}

		private async Task<(string id, string username)> ExchangeDiscordAsync(string code)
		{
			var (clientId, clientSecret) = GetCredentials(ChatbotPlatform.Discord);

			using var tokenResponse = await PostFormAsync("https://discord.com/api/oauth2/token", new Dictionary<string, string>
			{
				["client_id"] = clientId,
				["client_secret"] = clientSecret,
				["grant_type"] = "authorization_code",
				["code"] = code,
				["redirect_uri"] = ChatbotConfig.OAuthRedirectUri
			});

			if (tokenResponse == null || !tokenResponse.RootElement.TryGetProperty("access_token", out var tokenEl))
				return (null, null);

			var accessToken = tokenEl.GetString();

			using var request = new HttpRequestMessage(HttpMethod.Get, "https://discord.com/api/users/@me");
			request.Headers.Add("Authorization", $"Bearer {accessToken}");
			using var meResponse = await _http.SendAsync(request);
			if (!meResponse.IsSuccessStatusCode)
				return (null, null);

			using var doc = JsonDocument.Parse(await meResponse.Content.ReadAsStringAsync());
			var root = doc.RootElement;
			var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
			var username = root.TryGetProperty("global_name", out var gnEl) && gnEl.ValueKind == JsonValueKind.String
				? gnEl.GetString()
				: (root.TryGetProperty("username", out var unEl) ? unEl.GetString() : null);
			return (id, username);
		}

		private async Task<(string id, string username)> ExchangeSlackAsync(string code)
		{
			var (clientId, clientSecret) = GetCredentials(ChatbotPlatform.Slack);

			using var tokenResponse = await PostFormAsync("https://slack.com/api/oauth.v2.access", new Dictionary<string, string>
			{
				["client_id"] = clientId,
				["client_secret"] = clientSecret,
				["code"] = code,
				["redirect_uri"] = ChatbotConfig.OAuthRedirectUri
			});

			if (tokenResponse == null)
				return (null, null);

			var root = tokenResponse.RootElement;
			if (!root.TryGetProperty("ok", out var okEl) || !okEl.GetBoolean())
				return (null, null);

			if (!root.TryGetProperty("authed_user", out var authedUser) || authedUser.ValueKind != JsonValueKind.Object)
				return (null, null);

			var id = authedUser.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
			return (id, id);
		}

		private static async Task<JsonDocument> PostFormAsync(string url, Dictionary<string, string> form)
		{
			using var content = new FormUrlEncodedContent(form);
			using var response = await _http.PostAsync(url, content);
			if (!response.IsSuccessStatusCode)
				return null;

			return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
		}

		private static (string clientId, string clientSecret) GetCredentials(ChatbotPlatform platform)
			=> platform == ChatbotPlatform.Discord
				? (ChatbotConfig.DiscordClientId, ChatbotConfig.DiscordClientSecret)
				: (ChatbotConfig.SlackClientId, ChatbotConfig.SlackClientSecret);

		private static string GenerateState()
		{
			var bytes = new byte[32];
			using var rng = RandomNumberGenerator.Create();
			rng.GetBytes(bytes);
			return Convert.ToHexString(bytes).ToLowerInvariant();
		}

		private static void PurgeExpiredStates()
		{
			var cutoff = DateTime.UtcNow.AddMinutes(-10);
			foreach (var kvp in _states)
			{
				if (kvp.Value.CreatedAt < cutoff)
					_states.TryRemove(kvp.Key, out _);
			}
		}

		private class OAuthState
		{
			public string UserId { get; set; }
			public ChatbotPlatform Platform { get; set; }
			public DateTime CreatedAt { get; set; }
		}
	}

	public class OAuthStartResult
	{
		public bool Success { get; set; }
		public string Message { get; set; }
		public string AuthorizeUrl { get; set; }
		public string State { get; set; }

		public static OAuthStartResult Ok(string authorizeUrl, string state) =>
			new OAuthStartResult { Success = true, AuthorizeUrl = authorizeUrl, State = state };

		public static OAuthStartResult Fail(string message) =>
			new OAuthStartResult { Success = false, Message = message };
	}

	public class LinkResult
	{
		public bool Success { get; set; }
		public string Message { get; set; }
		public ChatbotUserIdentity Identity { get; set; }

		public static LinkResult Ok(string message, ChatbotUserIdentity identity) =>
			new LinkResult { Success = true, Message = message, Identity = identity };

		public static LinkResult Fail(string message) =>
			new LinkResult { Success = false, Message = message };
	}
}
