using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json.Linq;
using Resgrid.Config;
using Resgrid.Providers.Chatbot.Adapters;

namespace Resgrid.Providers.Chatbot.Services
{
	/// <summary>Validates provider JWTs against fixed authorities; token failures never enter application logs.</summary>
	public class ChatbotJwtValidator
	{
		private static readonly ConfigurationManager<OpenIdConnectConfiguration> TeamsMetadata = CreateManager(
			"https://login.botframework.com/v1/.well-known/openidconfiguration");
		private static readonly ConfigurationManager<OpenIdConnectConfiguration> GoogleMetadata = CreateManager(
			"https://accounts.google.com/.well-known/openid-configuration");
		private readonly IConfigurationManager<OpenIdConnectConfiguration> _teams;
		private readonly IConfigurationManager<OpenIdConnectConfiguration> _google;

		public ChatbotJwtValidator() : this(TeamsMetadata, GoogleMetadata) { }
		public ChatbotJwtValidator(IConfigurationManager<OpenIdConnectConfiguration> teams,
			IConfigurationManager<OpenIdConnectConfiguration> google)
		{ _teams = teams; _google = google; }

		public async Task<bool> ValidateTeamsAsync(string bearer, string serviceUrl)
		{
			if (!Guid.TryParse(ChatbotConfig.TeamsAppId, out _) || !Guid.TryParse(ChatbotConfig.TeamsTenantId, out _)
				|| string.IsNullOrWhiteSpace(ChatbotConfig.TeamsAppPassword) || !TeamsBotAdapter.IsServiceUrlAllowed(serviceUrl))
				return false;
			try
			{
				var validated = await ValidateAsync(bearer, ChatbotConfig.TeamsAppId, _teams,
					new[] { "https://api.botframework.com" });
				if (validated.Token == null || !string.Equals(validated.Token.Claims.FirstOrDefault(c => c.Type == "serviceurl"
					|| c.Type == "serviceUrl")?.Value, serviceUrl, StringComparison.Ordinal)) return false;

				var key = validated.Token.SigningKey as JsonWebKey ?? validated.Configuration.JsonWebKeySet?.Keys.FirstOrDefault(k =>
					string.Equals(k.Kid, validated.Token.SigningKey?.KeyId, StringComparison.Ordinal));
				if (key == null) return validated.Configuration.JsonWebKeySet == null;
				if (!key.AdditionalData.TryGetValue("endorsements", out var endorsements)) return true;
				var values = endorsements is System.Text.Json.JsonElement element
					? JToken.Parse(element.GetRawText()) : JToken.FromObject(endorsements);
				return values is JArray array && array.Any(v => v.Type == JTokenType.String
					&& string.Equals((string)v, "msteams", StringComparison.Ordinal));
			}
			catch { return false; }
		}

		public async Task<bool> ValidateGoogleChatAsync(string bearer, string audience)
		{
			if (string.IsNullOrWhiteSpace(audience)) return false;
			try
			{
				var validated = await ValidateAsync(bearer, audience, _google,
					new[] { "https://accounts.google.com", "accounts.google.com" });
				return validated.Token != null
					&& validated.Token.Claims.Any(c => c.Type == "email" && c.Value == "chat@system.gserviceaccount.com")
					&& validated.Token.Claims.Any(c => c.Type == "email_verified"
						&& string.Equals(c.Value, "true", StringComparison.OrdinalIgnoreCase));
			}
			catch { return false; }
		}

		private static async Task<(JwtSecurityToken Token, OpenIdConnectConfiguration Configuration)> ValidateAsync(
			string bearer, string audience, IConfigurationManager<OpenIdConnectConfiguration> manager, string[] issuers)
		{
			if (!AuthenticationHeaderValue.TryParse(bearer, out var header)
				|| !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
				|| string.IsNullOrWhiteSpace(header.Parameter) || header.Parameter.Length > 32768) return (null, null);

			var handler = new JwtSecurityTokenHandler { MapInboundClaims = false, MaximumTokenSizeInBytes = 32768 };
			if (!handler.CanReadToken(header.Parameter)) return (null, null);
			for (var attempt = 0; attempt < 2; attempt++)
			{
				var configuration = await manager.GetConfigurationAsync(CancellationToken.None);
				try
				{
					handler.ValidateToken(header.Parameter, new TokenValidationParameters
					{
						RequireSignedTokens = true, RequireExpirationTime = true,
						ValidateIssuerSigningKey = true, IssuerSigningKeys = configuration.SigningKeys,
						ValidateIssuer = true, ValidIssuers = issuers,
						ValidateAudience = true, ValidAudience = audience, IgnoreTrailingSlashWhenValidatingAudience = false,
						ValidateLifetime = true, ClockSkew = TimeSpan.FromMinutes(1),
						ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 }
					}, out var token);
					return (token as JwtSecurityToken, configuration);
				}
				catch (SecurityTokenSignatureKeyNotFoundException) when (attempt == 0) { manager.RequestRefresh(); }
			}
			return (null, null);
		}

		private static ConfigurationManager<OpenIdConnectConfiguration> CreateManager(string address) => new(
			address, new OpenIdConnectConfigurationRetriever(), new HttpDocumentRetriever { RequireHttps = true })
		{ AutomaticRefreshInterval = TimeSpan.FromHours(12) };
	}
}
