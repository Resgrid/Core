using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Tokens;
using Resgrid.Chatbot.Models;
using Resgrid.Config;
using Resgrid.Model.Providers;
using Resgrid.Providers.Chatbot.Services;

namespace Resgrid.Providers.Chatbot.Adapters
{
	public class GoogleChatBotAdapter : HttpChatbotAdapter
	{
		private readonly ICacheProvider _cache;
		public GoogleChatBotAdapter(ChatbotHttpClient http, ICacheProvider cache) : base(http) { _cache = cache; }
		public override ChatbotPlatform Platform => ChatbotPlatform.GoogleChat;
		public override bool IsConfigured => !string.IsNullOrWhiteSpace(ChatbotConfig.GoogleChatServiceAccountEmail)
			&& !string.IsNullOrWhiteSpace(ChatbotConfig.GoogleChatPrivateKey) && !string.IsNullOrWhiteSpace(ChatbotConfig.GoogleChatAudience);
		protected override int MessageLength => 3500;
		protected override async Task SendTextAsync(string recipient, string text, ChatbotMessage inbound)
		{
			if (!Regex.IsMatch(recipient, @"^users/[A-Za-z0-9_-]+$")) throw new InvalidOperationException("Invalid Google Chat recipient.");
			var key = "chatbot:google:route:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ChatbotConfig.GoogleChatServiceAccountEmail + ":" + recipient)));
			var space = inbound?.GetMetaString("space") ?? await _cache.GetStringAsync(key);
			if (space == null || !Regex.IsMatch(space, @"^spaces/[A-Za-z0-9_-]+$"))
				throw new InvalidOperationException("Send the Google Chat app a private message first.");
			if (inbound != null && !await _cache.SetStringAsync(key, space, TimeSpan.FromDays(90)))
				throw new InvalidOperationException("Unable to save the Google Chat conversation.");
			string assertion;
			try
			{
				using var rsa = RSA.Create();
				rsa.ImportFromPem(ChatbotConfig.GoogleChatPrivateKey);
				var now = DateTime.UtcNow;
				var jwt = new JwtSecurityToken(ChatbotConfig.GoogleChatServiceAccountEmail, "https://oauth2.googleapis.com/token",
					new[] { new System.Security.Claims.Claim("scope", "https://www.googleapis.com/auth/chat.bot") },
					now, now.AddMinutes(5), new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256));
				jwt.Payload["iat"] = EpochTime.GetIntDate(now);
				assertion = new JwtSecurityTokenHandler().WriteToken(jwt);
			}
			catch (Exception) { throw new InvalidOperationException("Google Chat credentials are invalid."); }
			var token = await Http.SendAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
			{ ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer", ["assertion"] = assertion }));
			var accessToken = (string)token["access_token"];
			if (string.IsNullOrWhiteSpace(accessToken)) throw new InvalidOperationException("Google Chat authentication failed.");
			var sent = await Http.PostAsync("https://chat.googleapis.com/v1/" + space + "/messages", new { text }, "Bearer " + accessToken);
			if (string.IsNullOrWhiteSpace((string)sent["name"])) throw new InvalidOperationException("Google Chat rejected the message.");
		}
	}
}
