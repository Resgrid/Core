using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Chatbot.Models;
using Resgrid.Config;
using Resgrid.Model.Providers;
using Resgrid.Providers.Chatbot.Services;

namespace Resgrid.Providers.Chatbot.Adapters
{
	/// <summary>Personal Teams conversations, with connector routes learned from authenticated activities.</summary>
	public class TeamsBotAdapter : HttpChatbotAdapter
	{
		private readonly ICacheProvider _cache;
		private static readonly TimeSpan RouteLifetime = TimeSpan.FromDays(90);

		public TeamsBotAdapter(ChatbotHttpClient http, ICacheProvider cache) : base(http) { _cache = cache; }
		public override ChatbotPlatform Platform => ChatbotPlatform.MicrosoftTeams;
		public override bool IsConfigured => Guid.TryParse(ChatbotConfig.TeamsAppId, out _)
			&& Guid.TryParse(ChatbotConfig.TeamsTenantId, out _)
			&& !string.IsNullOrWhiteSpace(ChatbotConfig.TeamsAppPassword)
			&& (ChatbotConfig.TeamsServiceUrls ?? "").Split(',').Any(url => NormalizeServiceUrl(url.Trim()) != null);
		protected override int MessageLength => 3500;

		public override async Task<ChatbotMessage> ParseInboundMessageAsync(object rawRequest)
		{
			if (rawRequest is not Dictionary<string, string> parameters
				|| !parameters.TryGetValue("tenant_id", out var tenantId)
				|| !parameters.TryGetValue("teams_user_id", out var userId)
				|| !IsConfigured || !ValidTenant(tenantId) || string.IsNullOrWhiteSpace(userId))
				return null;

			var values = new Dictionary<string, string>(parameters)
			{
				["from"] = CanonicalIdentity(tenantId, userId)
			};
			return await base.ParseInboundMessageAsync(values);
		}

		public static bool IsServiceUrlAllowed(string serviceUrl)
		{
			var normalized = NormalizeServiceUrl(serviceUrl);
			return normalized != null && (ChatbotConfig.TeamsServiceUrls ?? "").Split(',')
				.Any(url => string.Equals(NormalizeServiceUrl(url.Trim()), normalized, StringComparison.Ordinal));
		}

		protected override async Task SendTextAsync(string recipient, string text, ChatbotMessage inbound)
		{
			TeamsRoute route;
			var key = RouteKey(recipient);
			if (inbound != null)
			{
				route = new TeamsRoute
				{
					AppId = ChatbotConfig.TeamsAppId,
					ServiceUrl = inbound.GetMetaString("service_url"),
					ConversationId = inbound.GetMetaString("conversation_id"),
					BotId = inbound.GetMetaString("bot_id"),
					UserId = inbound.GetMetaString("teams_user_id"),
					TenantId = inbound.GetMetaString("tenant_id")
				};
				ValidateRoute(recipient, route);
				bool saved;
				try { saved = await _cache.SetStringAsync(key, JsonConvert.SerializeObject(route), RouteLifetime); }
				catch { throw new InvalidOperationException("Teams conversation could not be saved. Please try again."); }
				if (!saved) throw new InvalidOperationException("Teams conversation could not be saved. Please try again.");
			}
			else
			{
				try
				{
					var json = await _cache.GetStringAsync(key);
					route = string.IsNullOrWhiteSpace(json) ? null : JsonConvert.DeserializeObject<TeamsRoute>(json);
				}
				catch { throw new InvalidOperationException("Teams conversation is unavailable. Send the bot a private message first."); }
				ValidateRoute(recipient, route);
			}

			var token = await Http.SendAsync(
				$"https://login.microsoftonline.com/{Guid.Parse(ChatbotConfig.TeamsTenantId):D}/oauth2/v2.0/token",
				new FormUrlEncodedContent(new Dictionary<string, string>
				{
					["grant_type"] = "client_credentials",
					["client_id"] = ChatbotConfig.TeamsAppId,
					["client_secret"] = ChatbotConfig.TeamsAppPassword,
					["scope"] = "https://api.botframework.com/.default"
				}));
			var accessToken = token["access_token"]?.Type == Newtonsoft.Json.Linq.JTokenType.String
				? (string)token["access_token"] : null;
			if (string.IsNullOrWhiteSpace(accessToken)) throw new InvalidOperationException("Teams authentication failed.");

			await Http.PostAsync(NormalizeServiceUrl(route.ServiceUrl) + "v3/conversations/"
				+ Uri.EscapeDataString(route.ConversationId) + "/activities", new
				{
					type = "message", text, textFormat = "plain", channelId = "msteams",
					from = new { id = route.BotId }, recipient = new { id = route.UserId },
					conversation = new { id = route.ConversationId },
					channelData = new { tenant = new { id = route.TenantId } }
				}, "Bearer " + accessToken);
		}

		private static void ValidateRoute(string recipient, TeamsRoute route)
		{
			if (route == null || !string.Equals(route.AppId, ChatbotConfig.TeamsAppId, StringComparison.OrdinalIgnoreCase)
				|| !ValidTenant(route.TenantId) || string.IsNullOrWhiteSpace(route.UserId)
				|| !string.Equals(recipient, CanonicalIdentity(route.TenantId, route.UserId), StringComparison.Ordinal)
				|| string.IsNullOrWhiteSpace(route.BotId) || string.IsNullOrWhiteSpace(route.ConversationId)
				|| !IsServiceUrlAllowed(route.ServiceUrl))
				throw new InvalidOperationException("Teams conversation is unavailable. Send the bot a private message first.");
		}

		private static bool ValidTenant(string tenantId) => Guid.TryParse(tenantId, out var tenant)
			&& Guid.TryParse(ChatbotConfig.TeamsTenantId, out var configured) && tenant == configured;
		private static string CanonicalIdentity(string tenantId, string userId) => $"{Guid.Parse(tenantId):D}:{userId}";
		private static string RouteKey(string recipient) => "chatbot:teams:route:"
			+ Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ChatbotConfig.TeamsAppId.ToLowerInvariant() + "\n" + recipient)));

		private static string NormalizeServiceUrl(string value)
		{
			if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
				|| !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo)
				|| !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) return null;
			return uri.AbsoluteUri.TrimEnd('/') + "/";
		}

		private sealed class TeamsRoute
		{
			public string AppId { get; set; }
			public string ServiceUrl { get; set; }
			public string ConversationId { get; set; }
			public string BotId { get; set; }
			public string UserId { get; set; }
			public string TenantId { get; set; }
		}
	}
}
