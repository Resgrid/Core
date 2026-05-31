using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Framework;
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

		// A single shared HttpClient avoids socket exhaustion. This provider is registered
		// InstancePerLifetimeScope, so a per-instance client would leak sockets under load.
		// Per-request timeouts are enforced via a CancellationToken (see ClassifyAsync) rather
		// than the shared client's Timeout, which cannot be varied safely across concurrent callers.
		private static readonly HttpClient _httpClient = new HttpClient();
		private readonly IChatbotDepartmentConfigService _configService;

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

		public OpenAiCompatibleNluProvider(IChatbotDepartmentConfigService configService)
		{
			_configService = configService;
		}

		public async Task<NLUResult> ClassifyAsync(string text, string context = null, int departmentId = 0)
		{
			if (string.IsNullOrWhiteSpace(text))
				return new NLUResult { IntentName = "unknown", Confidence = 0, ProviderName = ProviderName };

			var sw = Stopwatch.StartNew();

			try
			{
				// A department may supply its own LLM provider so its processing stays with that
				// provider; otherwise fall back to the Resgrid system-level configuration.
				DepartmentLlmOverride departmentLlm = null;
				if (departmentId > 0 && _configService != null)
					departmentLlm = await _configService.GetLlmOverrideAsync(departmentId);

				var endpoint = departmentLlm != null ? departmentLlm.Endpoint : ResolveEndpoint();
				var apiKey = departmentLlm != null ? departmentLlm.ApiKey : ResolveApiKey();
				var model = departmentLlm != null && !string.IsNullOrWhiteSpace(departmentLlm.Model)
					? departmentLlm.Model
					: ResolveModel();

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

				// Anthropic uses a different request/response schema and auth header than the OpenAI
				// chat-completions API. Department overrides carry no provider type, so detect Anthropic
				// from the endpoint URL; otherwise honour the system-level provider setting.
				var isAnthropic = departmentLlm != null
					? (!string.IsNullOrWhiteSpace(endpoint) && endpoint.IndexOf("anthropic", StringComparison.OrdinalIgnoreCase) >= 0)
					: ChatbotConfig.CloudNluProvider == CloudNluProviderType.Anthropic;

				var systemPrompt = !string.IsNullOrWhiteSpace(ChatbotConfig.CloudNluSystemPrompt)
					? ChatbotConfig.CloudNluSystemPrompt
					: IntentSystemPrompt;

				var maxTokens = ChatbotConfig.CloudNluMaxTokens > 0 ? ChatbotConfig.CloudNluMaxTokens : 256;

				object requestBody;
				if (isAnthropic)
				{
					// Anthropic /v1/messages: the system prompt is a top-level field, messages contain
					// only user/assistant turns, and there is no response_format option.
					var anthropicSystem = string.IsNullOrWhiteSpace(context)
						? systemPrompt
						: $"{systemPrompt}\n\nConversation context: {context}";

					requestBody = new
					{
						model,
						max_tokens = maxTokens,
						temperature = ChatbotConfig.CloudNluTemperature,
						system = anthropicSystem,
						messages = new[] { new { role = "user", content = text } }
					};
				}
				else
				{
					var messages = new List<object>
					{
						new { role = "system", content = systemPrompt }
					};

					if (!string.IsNullOrWhiteSpace(context))
					{
						messages.Add(new { role = "system", content = $"Conversation context: {context}" });
					}

					messages.Add(new { role = "user", content = text });

					requestBody = new
					{
						model,
						messages,
						temperature = ChatbotConfig.CloudNluTemperature,
						max_tokens = maxTokens,
						response_format = new { type = "json_object" }
					};
				}

				var json = JsonConvert.SerializeObject(requestBody);
				var content = new StringContent(json, Encoding.UTF8, "application/json");

				var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
				{
					Content = content
				};

				if (isAnthropic)
				{
					// Anthropic authenticates with x-api-key and requires an API version header.
					request.Headers.Add("x-api-key", apiKey);
					request.Headers.Add("anthropic-version", "2023-06-01");
				}
				else if (departmentLlm == null && ChatbotConfig.CloudNluProvider == CloudNluProviderType.AzureOpenAI)
				{
					// Azure OpenAI uses an api-key header instead of Bearer auth (system config only;
					// a department override is assumed OpenAI-compatible with Bearer auth).
					request.Headers.Add("api-key", apiKey);
				}
				else
				{
					request.Headers.Add("Authorization", $"Bearer {apiKey}");
				}

				// Enforce the configured timeout per request via a CancellationToken rather than the
				// shared client's Timeout.
				using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(
					ChatbotConfig.CloudNluTimeoutSeconds > 0 ? ChatbotConfig.CloudNluTimeoutSeconds : 10));

				var response = await _httpClient.SendAsync(request, cts.Token);
				var responseBody = await response.Content.ReadAsStringAsync();

				sw.Stop();

				if (!response.IsSuccessStatusCode)
				{
					Logging.LogError($"Cloud NLU error from {ProviderName} (HTTP {(int)response.StatusCode}): {responseBody?.Truncate(500)}");
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

				var parsed = ParseOpenAiResponse(responseBody, model, sw.ElapsedMilliseconds, isAnthropic);
				return parsed;
			}
			catch (TaskCanceledException ex)
			{
				sw.Stop();
				Logging.LogError($"Cloud NLU ({ProviderName}) timed out after {sw.ElapsedMilliseconds}ms: {ex.Message}");
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
				Logging.LogException(ex, "Cloud NLU classification failed.");
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
				CloudNluProviderType.Anthropic => "claude-3-5-sonnet-latest",
				_ => "gpt-4o"
			};
		}

		private NLUResult ParseOpenAiResponse(string responseBody, string model, long latencyMs, bool isAnthropic = false)
		{
			try
			{
				var root = JObject.Parse(responseBody);

				string contentText;
				int? totalTokens;

				if (isAnthropic)
				{
					// Anthropic returns content as an array of blocks and reports input/output tokens
					// separately rather than a single total_tokens value.
					var contentBlocks = root["content"] as JArray;
					contentText = contentBlocks != null && contentBlocks.Count > 0
						? contentBlocks[0]?["text"]?.ToString()
						: null;

					var usage = root["usage"];
					totalTokens = usage != null
						? (usage["input_tokens"]?.Value<int>() ?? 0) + (usage["output_tokens"]?.Value<int>() ?? 0)
						: (int?)null;
				}
				else
				{
					var choices = root["choices"] as JArray;
					if (choices == null || choices.Count == 0)
					{
						Logging.LogError($"Cloud NLU ({ProviderName}) returned no choices.");
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
					contentText = message?["content"]?.ToString();

					var usage = root["usage"];
					totalTokens = usage != null ? usage["total_tokens"]?.Value<int>() : null;
				}

				if (string.IsNullOrWhiteSpace(contentText))
				{
					Logging.LogError($"Cloud NLU ({ProviderName}) returned empty content.");
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
			catch (JsonException ex)
			{
				Logging.LogException(ex, "Cloud NLU returned unparseable JSON.");
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
