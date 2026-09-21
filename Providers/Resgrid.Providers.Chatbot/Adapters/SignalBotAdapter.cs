using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Resgrid.Chatbot.Models;
using Resgrid.Config;
using Resgrid.Providers.Chatbot.Services;

namespace Resgrid.Providers.Chatbot.Adapters
{
	/// <summary>Private Signal text delivery through an operator-owned signal-cli REST gateway.</summary>
	public class SignalBotAdapter : HttpChatbotAdapter
	{
		public SignalBotAdapter(ChatbotHttpClient http) : base(http) { }
		public override ChatbotPlatform Platform => ChatbotPlatform.Signal;
		public override bool IsConfigured => IsValidBridgeUrl(ChatbotConfig.SignalBridgeUrl)
			&& Regex.IsMatch(ChatbotConfig.SignalAccountNumber ?? "", @"\A\+[1-9][0-9]{6,14}\z")
			&& IsValidSecret(ChatbotConfig.SignalBridgeApiToken);
		// Conservative text chunks, leaving headroom for the bridge's UTF-8 message limit.
		protected override int MessageLength => 1500;
		public static bool IsValidSecret(string value) => Regex.IsMatch(value ?? "", @"\A[A-Za-z0-9_-]{32,128}\z");
		public static bool IsValidBridgeUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
			&& (uri.Scheme == "https" || (uri.Scheme == "http" && uri.IsLoopback))
			&& string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query)
			&& string.IsNullOrEmpty(uri.Fragment) && uri.AbsolutePath == "/";

		protected override async Task SendTextAsync(string recipient, string text, ChatbotMessage inbound)
		{
			// Only verified Signal account UUIDs are linked. Never accept phone, username, or group routing.
			if (!Guid.TryParseExact(recipient, "D", out var userId) || userId == Guid.Empty)
				throw new ArgumentException("A linked Signal account is required.");
			var result = await Http.PostJsonAsync(ChatbotConfig.SignalBridgeUrl.TrimEnd('/') + "/v2/send",
				new { number = ChatbotConfig.SignalAccountNumber, recipients = new[] { userId.ToString("D") }, message = text,
					text_mode = "normal", notify_self = false }, "Bearer " + ChatbotConfig.SignalBridgeApiToken);
			// Newer REST bridges return an array by recipient type; older releases return one object.
			// This transport sends exactly one private recipient, so exactly one result is expected.
			var response = result is JArray results ? (results.Count == 1 ? results[0] as JObject : null) : result as JObject;
			var errors = response?["errors"];
			if (response == null || !long.TryParse(response["timestamp"]?.ToString(), out var timestamp) || timestamp <= 0
				|| response["error"] is { Type: not JTokenType.Null }
				|| (errors is { Type: not JTokenType.Null } && !(errors is JArray array && array.Count == 0)))
				throw new InvalidOperationException("Signal bridge did not accept the message.");
		}
	}
}
