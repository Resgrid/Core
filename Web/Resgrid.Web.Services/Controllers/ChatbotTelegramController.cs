using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Config;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;
using Resgrid.Framework;
using Resgrid.Providers.Chatbot.Interfaces;

namespace Resgrid.Web.Services.Controllers
{
	/// <summary>
	/// Webhook controller for Telegram Bot API.
	/// Receives messages from Telegram and routes them through the chatbot pipeline.
	/// 
	/// Phase 2 implementation: Simple webhook receiver.
	/// In production, Telegram's setWebhook should point to: POST /api/v4/ChatbotTelegram/Webhook
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class ChatbotTelegramController : ControllerBase
	{
		private readonly IChatbotIngressService _chatbotIngressService;
		private readonly IEnumerable<IChatbotPlatformAdapter> _adapters;

		public ChatbotTelegramController(
			IChatbotIngressService chatbotIngressService,
			IEnumerable<IChatbotPlatformAdapter> adapters)
		{
			_chatbotIngressService = chatbotIngressService;
			_adapters = adapters;
		}

		/// <summary>
		/// Telegram webhook endpoint.
		/// Receives Update objects from Telegram Bot API.
		/// </summary>
		[HttpPost("Webhook")]
		[Consumes("application/json")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<IActionResult> Webhook()
		{
			// Verify the secret token Telegram echoes back in the X-Telegram-Bot-Api-Secret-Token
			// header (configured via setWebhook). When a secret is configured, requests without a
			// matching token are rejected so the webhook cannot be driven by arbitrary callers.
			var configuredSecret = ChatbotConfig.TelegramWebhookSecretToken;
			if (!string.IsNullOrEmpty(configuredSecret))
			{
				var providedSecret = Request.Headers["X-Telegram-Bot-Api-Secret-Token"].ToString();
				if (!SecretMatches(providedSecret, configuredSecret))
					return Unauthorized();
			}

			try
			{
				using var reader = new StreamReader(Request.Body);
				var body = await reader.ReadToEndAsync();

				if (string.IsNullOrWhiteSpace(body))
					return Ok("ok");

				// Find the Telegram adapter
				IChatbotPlatformAdapter telegramAdapter = null;
				foreach (var adapter in _adapters)
				{
					if (adapter.Platform == ChatbotPlatform.Telegram)
					{
						telegramAdapter = adapter;
						break;
					}
				}

				if (telegramAdapter == null)
					return Ok("ok"); // Telegram adapter not configured

				// Parse the Telegram Update to extract message parameters
				var parameters = new Dictionary<string, string>();
				ParseTelegramUpdate(body, parameters);

				if (!parameters.ContainsKey("text") || string.IsNullOrWhiteSpace(parameters["text"]))
					return Ok("ok"); // Non-text update (e.g., sticker, photo) - ignore

				// Convert to ChatbotMessage
				var chatbotMessage = await telegramAdapter.ParseInboundMessageAsync(parameters);

				// Process through the chatbot pipeline
				var response = await _chatbotIngressService.ProcessMessageAsync(chatbotMessage);

				// Format and send response via Telegram adapter
				var formattedResponse = await telegramAdapter.FormatOutboundResponseAsync(response);

				return Ok("ok");
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return Ok("ok"); // Always return 200 to Telegram
			}
		}

		private static void ParseTelegramUpdate(string json, Dictionary<string, string> parameters)
		{
			// Parse the Telegram Update object with System.Text.Json. Extracts
			// message.from.id, message.from.first_name, message.text, and message.chat.id.
			try
			{
				using var doc = JsonDocument.Parse(json);
				var root = doc.RootElement;

				if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("message", out var messageEl)
					|| messageEl.ValueKind != JsonValueKind.Object)
					return;

				if (messageEl.TryGetProperty("from", out var fromEl) && fromEl.ValueKind == JsonValueKind.Object)
				{
					if (fromEl.TryGetProperty("id", out var fromIdEl) && fromIdEl.ValueKind == JsonValueKind.Number)
						parameters["from"] = fromIdEl.GetRawText();

					if (fromEl.TryGetProperty("first_name", out var firstNameEl) && firstNameEl.ValueKind == JsonValueKind.String)
						parameters["first_name"] = firstNameEl.GetString();
				}

				if (messageEl.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
					parameters["text"] = textEl.GetString();

				if (messageEl.TryGetProperty("chat", out var chatEl) && chatEl.ValueKind == JsonValueKind.Object
					&& chatEl.TryGetProperty("id", out var chatIdEl) && chatIdEl.ValueKind == JsonValueKind.Number)
					parameters["chat_id"] = chatIdEl.GetRawText();
			}
			catch (JsonException)
			{
				// Malformed update - leave parameters empty; the caller skips updates without text.
			}
		}

		private static bool SecretMatches(string provided, string expected)
		{
			if (string.IsNullOrEmpty(provided))
				return false;

			// Constant-time comparison to avoid leaking the secret via timing.
			var providedBytes = Encoding.UTF8.GetBytes(provided);
			var expectedBytes = Encoding.UTF8.GetBytes(expected);
			return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
		}
	}
}
