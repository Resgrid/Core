using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Chatbot.Config;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;

namespace Resgrid.Chatbot.NLU.Providers
{
	/// <summary>
	/// Cloud NLU provider compatible with OpenAI API format.
	/// Supports: OpenAI, Azure OpenAI, DeepSeek, and any OpenAI-compatible API.
	///
	/// DeepSeek configuration:
	///   ChatbotConfig.CloudNluProvider = CloudNluProviderType.DeepSeek
	///   ChatbotConfig.CloudNluApiEndpoint = "https://api.deepseek.com"
	///   ChatbotConfig.CloudNluApiKey = "sk-..."
	///   ChatbotConfig.CloudNluModelName = "deepseek-chat" (V3) or "deepseek-reasoner" (R1)
	///
	/// OpenAI configuration:
	///   ChatbotConfig.CloudNluApiEndpoint = "https://api.openai.com/v1"
	///   ChatbotConfig.CloudNluModelName = "gpt-4o"
	///
	/// Azure OpenAI configuration:
	///   ChatbotConfig.CloudNluApiEndpoint = "https://{resource}.openai.azure.com/openai/deployments/{deployment}/chat/completions?api-version=2024-02-15-preview"
	///   
	/// When Endpoint is empty, the provider auto-resolves it from CloudNluProviderType.
	/// </summary>
	public class OpenAiCompatibleNluProvider : INLUProvider
	{
		public string ProviderName => "CloudLLM";
		public int Priority => 100;

		private readonly HttpClient _httpClient;

		private static readonly string IntentSystemPrompt = @"You are a classification engine for emergency service chatbot commands.
Classify the user's message into exactly one of these intent categories:

- set_status: User wants to change their personnel status (responding, on scene, available, not responding, standing by, en route)
- set_staffing: User wants to change their staffing level (available, delayed, unavailable, committed, on shift)
- my_status: User asks about their current status or staffing level
- list_calls: User wants to see active/open calls/incidents
- call_detail: User asks about a specific call by number/name
- respond_to_call: User says they are responding/going/en route to a specific call
- close_call: User wants to close/cancel/end a call
- dispatch_call: User wants to create/draft a new call or incident (dispatch role)
- list_units: User wants to see unit statuses
- set_unit_status: User wants to change the status of a specific unit
- list_messages: User wants to check/read their inbox messages
- message_detail: User asks about a specific message by number
- send_message: User wants to send a message to someone
- respond_to_message: User wants to reply/respond to a specific message
- delete_message: User wants to delete/remove a message
- list_calendar: User wants to see upcoming calendar events
- calendar_detail: User asks about a specific calendar event
- rsvp_calendar: User wants to RSVP (yes/no/maybe) to an event
- list_shifts: User wants to see shifts
- shift_signup: User wants to sign up for a shift
- shift_drop: User wants to drop a shift
- personnel_lookup: User asks about personnel (who is X, where is X, list personnel)
- weather_alert: User asks about weather alerts or warnings
- emergency_mayday: User signals emergency/distress (mayday, officer down, firefighter down)
- link_account: User wants to link/authenticate their account
- unlink_account: User wants to unlink/logout
- list_departments: User wants to see all departments they belong to
- get_active_department: User asks which department is currently active / in use
- switch_department: User wants to switch/change their active department to a different one
- help: User asks for help/commands/menu
- stop: User wants to stop/end/cancel/unsubscribe
- unknown: Cannot determine the intent

Respond with ONLY a JSON object. No explanation, no markdown, no additional text.

{""intent"": ""<intent_name>"", ""confidence"": 0.95, ""parameters"": {""key"": ""value""}}

Extract any parameters mentioned:
- For set_status: ""statusName"" (the status name), or ""actionType"" (numeric ID 1-4)
- For set_staffing: ""staffingName"" (the staffing name), or ""staffingType"" (numeric S1-S5)
- For call_detail/close_call/respond_to_call: ""callId"" (the call number)
- For set_unit_status: ""unitName"", ""status""
- For send_message: ""recipient"", ""body""
- For respond_to_message: ""messageId"", ""response"" (yes/no)
- For message_detail/delete_message: ""messageId""
- For rsvp_calendar: ""response"" (yes/no/maybe), ""eventId"" or ""eventName""
- For shift_signup/shift_drop: ""shiftId"" or ""shiftName""
- For personnel_lookup: ""query"" (the name/description)
- For dispatch_call: ""description"" (the call description)
- For switch_department: ""departmentIdentifier"" (the department name, code, or number to switch to)
- For list_departments: no parameters needed
- For get_active_department: no parameters needed

Confidence should be between 0.0 and 1.0 based on how certain you are.
If the user's message doesn't clearly match any intent, set intent to ""unknown"" with confidence 0.0.";

		public OpenAiCompatibleNluProvider()
		{
			_httpClient = new HttpClient
			{
				Timeout = TimeSpan.FromSeconds(ChatbotConfig.CloudNluTimeoutSeconds > 0
					? ChatbotConfig.CloudNluTimeoutSeconds
					: 10)
			};
		}

		public async Task<NLUResult> ClassifyAsync(string text, string context = null)
		{
			if (string.IsNullOrWhiteSpace(text))
				return new NLUResult { IntentName = "unknown", Confidence = 0, ProviderName = ProviderName };

			var sw = Stopwatch.StartNew();

			try
			{
				var endpoint = ResolveEndpoint();
				var apiKey = ResolveApiKey();
				var model = ResolveModel();

				if (string.IsNullOrWhiteSpace(apiKey))
				{
					return new NLUResult
					{
						IntentName = "unknown",
						Confidence = 0,
						ProviderName = ProviderName,
						RawResponse = "Cloud NLU not configured: missing API key.",
						ModelName = model
					};
				}

				var systemPrompt = !string.IsNullOrWhiteSpace(ChatbotConfig.CloudNluSystemPrompt)
					? ChatbotConfig.CloudNluSystemPrompt
					: IntentSystemPrompt;

				var messages = new List<object>
				{
					new { role = "system", content = systemPrompt }
				};

				if (!string.IsNullOrWhiteSpace(context))
				{
					messages.Add(new { role = "system", content = $"Conversation context: {context}" });
				}

				messages.Add(new { role = "user", content = text });

				var requestBody = new
				{
					model,
					messages,
					temperature = ChatbotConfig.CloudNluTemperature,
					max_tokens = ChatbotConfig.CloudNluMaxTokens > 0 ? ChatbotConfig.CloudNluMaxTokens : 256,
					response_format = new { type = "json_object" }
				};

				var json = JsonConvert.SerializeObject(requestBody);
				var content = new StringContent(json, Encoding.UTF8, "application/json");

				var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
				{
					Content = content
				};
				request.Headers.Add("Authorization", $"Bearer {apiKey}");

				// Azure OpenAI uses api-key header instead
				if (ChatbotConfig.CloudNluProvider == CloudNluProviderType.AzureOpenAI)
				{
					request.Headers.Remove("Authorization");
					request.Headers.Add("api-key", apiKey);
				}

				var response = await _httpClient.SendAsync(request);
				var responseBody = await response.Content.ReadAsStringAsync();

				sw.Stop();

				if (!response.IsSuccessStatusCode)
				{
					return new NLUResult
					{
						IntentName = "unknown",
						Confidence = 0,
						ProviderName = ProviderName,
						RawResponse = $"Cloud NLU error (HTTP {(int)response.StatusCode}): {responseBody?.Truncate(200)}",
						LatencyMs = sw.ElapsedMilliseconds,
						ModelName = model
					};
				}

				var parsed = ParseOpenAiResponse(responseBody, model, sw.ElapsedMilliseconds);
				return parsed;
			}
			catch (TaskCanceledException)
			{
				sw.Stop();
				return new NLUResult
				{
					IntentName = "unknown",
					Confidence = 0,
					ProviderName = ProviderName,
					RawResponse = "Cloud NLU timed out.",
					LatencyMs = sw.ElapsedMilliseconds
				};
			}
			catch (Exception ex)
			{
				sw.Stop();
				return new NLUResult
				{
					IntentName = "unknown",
					Confidence = 0,
					ProviderName = ProviderName,
					RawResponse = $"Cloud NLU error: {ex.Message.Truncate(200)}",
					LatencyMs = sw.ElapsedMilliseconds
				};
			}
		}

		public Task<bool> IsAvailableAsync()
		{
			var apiKey = ResolveApiKey();
			return Task.FromResult(!string.IsNullOrWhiteSpace(apiKey));
		}

		private string ResolveEndpoint()
		{
			if (!string.IsNullOrWhiteSpace(ChatbotConfig.CloudNluApiEndpoint))
				return ChatbotConfig.CloudNluApiEndpoint;

			return ChatbotConfig.CloudNluProvider switch
			{
				CloudNluProviderType.DeepSeek => "https://api.deepseek.com/v1/chat/completions",
				CloudNluProviderType.OpenAI => "https://api.openai.com/v1/chat/completions",
				CloudNluProviderType.OpenAiCompatible => "https://api.openai.com/v1/chat/completions",
				CloudNluProviderType.AzureOpenAI => "",
				CloudNluProviderType.Anthropic => "https://api.anthropic.com/v1/messages",
				_ => "https://api.openai.com/v1/chat/completions"
			};
		}

		private string ResolveApiKey()
		{
			if (!string.IsNullOrWhiteSpace(ChatbotConfig.CloudNluApiKey))
				return ChatbotConfig.CloudNluApiKey;

			return ChatbotConfig.CloudNluProvider switch
			{
				CloudNluProviderType.DeepSeek =>
					Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY"),
				CloudNluProviderType.OpenAI =>
					Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
				CloudNluProviderType.OpenAiCompatible =>
					Environment.GetEnvironmentVariable("OPENAI_API_KEY")
					?? Environment.GetEnvironmentVariable("CLOUD_NLU_API_KEY"),
				CloudNluProviderType.AzureOpenAI =>
					Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY"),
				CloudNluProviderType.Anthropic =>
					Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"),
				_ => Environment.GetEnvironmentVariable("CLOUD_NLU_API_KEY")
			};
		}

		private string ResolveModel()
		{
			if (!string.IsNullOrWhiteSpace(ChatbotConfig.CloudNluModelName))
				return ChatbotConfig.CloudNluModelName;

			return ChatbotConfig.CloudNluProvider switch
			{
				CloudNluProviderType.DeepSeek => "deepseek-chat",
				CloudNluProviderType.OpenAI => "gpt-4o",
				CloudNluProviderType.OpenAiCompatible => "gpt-4o",
				CloudNluProviderType.AzureOpenAI => "gpt-4",
				CloudNluProviderType.Anthropic => "claude-3-5-sonnet",
				_ => "gpt-4o"
			};
		}

		private NLUResult ParseOpenAiResponse(string responseBody, string model, long latencyMs)
		{
			try
			{
				var root = JObject.Parse(responseBody);
				var choices = root["choices"] as JArray;
				if (choices == null || choices.Count == 0)
				{
					return new NLUResult
					{
						IntentName = "unknown",
						Confidence = 0,
						ProviderName = ProviderName,
						RawResponse = "Cloud NLU returned no choices.",
						LatencyMs = latencyMs,
						ModelName = model
					};
				}

				var message = choices[0]["message"];
				var contentText = message?["content"]?.ToString();

				int? totalTokens = null;
				var usage = root["usage"];
				if (usage != null)
					totalTokens = usage["total_tokens"]?.Value<int>();

				if (string.IsNullOrWhiteSpace(contentText))
				{
					return new NLUResult
					{
						IntentName = "unknown",
						Confidence = 0,
						ProviderName = ProviderName,
						RawResponse = "Cloud NLU returned empty content.",
						LatencyMs = latencyMs,
						ModelName = model,
						TotalTokens = totalTokens
					};
				}

				var intentJson = JObject.Parse(contentText);
				var intentName = intentJson["intent"]?.ToString() ?? "unknown";
				var confidence = intentJson["confidence"]?.Value<double>() ?? 0.5;

				var parameters = new Dictionary<string, string>();
				var paramsNode = intentJson["parameters"] as JObject;
				if (paramsNode != null)
				{
					foreach (var prop in paramsNode.Properties())
						parameters[prop.Name] = prop.Value?.ToString();
				}

				return new NLUResult
				{
					IntentName = intentName,
					Confidence = confidence,
					Parameters = parameters,
					ProviderName = ProviderName,
					RawResponse = contentText,
					LatencyMs = latencyMs,
					ModelName = model,
					TotalTokens = totalTokens
				};
			}
			catch (JsonException)
			{
				return new NLUResult
				{
					IntentName = "unknown",
					Confidence = 0,
					ProviderName = ProviderName,
					RawResponse = "Cloud NLU returned unparseable JSON.",
					LatencyMs = latencyMs,
					ModelName = model
				};
			}
		}
	}

	internal static class StringExtensions
	{
		public static string Truncate(this string value, int maxLength)
		{
			if (string.IsNullOrEmpty(value)) return value;
			return value.Length <= maxLength ? value : value.Substring(0, maxLength);
		}
	}
}
