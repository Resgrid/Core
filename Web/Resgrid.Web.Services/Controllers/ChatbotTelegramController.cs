using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
			// Simple JSON parsing for Telegram Update objects.
			// In production, use System.Text.Json or Newtonsoft.Json for full parsing.
			// Extract message.from.id, message.text, message.chat.id
			try
			{
				if (json.Contains("\"message\""))
				{
					// Extract from.id
					var fromIdIdx = json.IndexOf("\"from\"");
					if (fromIdIdx > 0)
					{
						var idIdx = json.IndexOf("\"id\"", fromIdIdx);
						if (idIdx > 0)
						{
							var colonIdx = json.IndexOf(':', idIdx);
							if (colonIdx > 0)
							{
								var endIdx = json.IndexOfAny(new[] { ',', '}', '\n' }, colonIdx);
								if (endIdx > colonIdx)
								{
									var id = json.Substring(colonIdx + 1, endIdx - colonIdx - 1).Trim();
									parameters["from"] = id;
								}
							}
						}

						// Extract from.first_name
						var firstNameIdx = json.IndexOf("\"first_name\"", fromIdIdx);
						if (firstNameIdx > 0)
						{
							var colonIdx = json.IndexOf(':', firstNameIdx);
							if (colonIdx > 0)
							{
								var startQuote = json.IndexOf('"', colonIdx + 1);
								var endQuote = json.IndexOf('"', startQuote + 1);
								if (startQuote > 0 && endQuote > startQuote)
									parameters["first_name"] = json.Substring(startQuote + 1, endQuote - startQuote - 1);
							}
						}
					}

					// Extract message.text
					var textIdx = json.IndexOf("\"text\"");
					if (textIdx > 0)
					{
						var colonIdx = json.IndexOf(':', textIdx);
						if (colonIdx > 0)
						{
							var startQuote = json.IndexOf('"', colonIdx + 1);
							var endQuote = json.IndexOf('"', startQuote + 1);
							if (startQuote > 0 && endQuote > startQuote)
								parameters["text"] = json.Substring(startQuote + 1, endQuote - startQuote - 1);
						}
					}

					// Extract message.chat.id
					var chatIdx = json.IndexOf("\"chat\"");
					if (chatIdx > 0)
					{
						var idIdx = json.IndexOf("\"id\"", chatIdx);
						if (idIdx > 0)
						{
							var colonIdx = json.IndexOf(':', idIdx);
							if (colonIdx > 0)
							{
								var endIdx = json.IndexOfAny(new[] { ',', '}', '\n' }, colonIdx);
								if (endIdx > colonIdx)
								{
									var chatId = json.Substring(colonIdx + 1, endIdx - colonIdx - 1).Trim();
									parameters["chat_id"] = chatId;
								}
							}
						}
					}
				}
			}
			catch
			{
				// Silently ignore parse errors
			}
		}
	}
}
