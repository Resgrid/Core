using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;
using Resgrid.Config;
using Resgrid.Framework;

namespace Resgrid.Chatbot.NLU.Providers
{
	/// <summary>
	/// Free-form chat completion sharing the cloud NLU classifier's provider resolution: system-level
	/// ChatbotConfig (OpenAI / Azure OpenAI / DeepSeek / Anthropic) with per-department LLM overrides
	/// honored. Used by the chatbot's conversational fallback; failures return null, never throw.
	/// </summary>
	public class OpenAiCompatibleChatCompletionClient : IChatCompletionClient
	{
		// Shared client to avoid socket exhaustion; per-request timeout via CancellationToken
		// (same rationale as OpenAiCompatibleNluProvider).
		private static readonly HttpClient _httpClient = new HttpClient();
		private readonly IChatbotDepartmentConfigService _configService;

		public OpenAiCompatibleChatCompletionClient(IChatbotDepartmentConfigService configService)
		{
			_configService = configService;
		}

		public async Task<bool> IsAvailableAsync(int departmentId)
		{
			var (_, apiKey, _, _, _) = await ResolveAsync(departmentId);
			return !string.IsNullOrWhiteSpace(apiKey);
		}

		public async Task<string> CompleteAsync(int departmentId, string systemPrompt, List<ChatCompletionTurn> turns, int? maxTokens = null)
		{
			try
			{
				if (turns == null || turns.Count == 0)
					return null;

				var (endpoint, apiKey, model, isAnthropic, isDepartmentOverride) = await ResolveAsync(departmentId);
				if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(endpoint))
					return null;

				// SSRF guard: the effective endpoint (system config or department override) must be an
				// absolute https URI resolving only to public addresses.
				if (!LlmEndpointValidator.IsValid(endpoint, out var endpointError))
				{
					Logging.LogError($"Chat completion rejected for department {departmentId}: invalid LLM endpoint ({endpointError})");
					return null;
				}

				var effectiveMaxTokens = maxTokens ?? (ChatbotConfig.CloudNluMaxTokens > 0 ? ChatbotConfig.CloudNluMaxTokens : 512);

				object requestBody;
				if (isAnthropic)
				{
					requestBody = new
					{
						model,
						max_tokens = effectiveMaxTokens,
						temperature = ChatbotConfig.CloudNluTemperature,
						system = systemPrompt,
						messages = turns.Select(t => new { role = NormalizeRole(t.Role), content = t.Content }).ToArray()
					};
				}
				else
				{
					var messages = new List<object> { new { role = "system", content = systemPrompt } };
					messages.AddRange(turns.Select(t => new { role = NormalizeRole(t.Role), content = t.Content }));

					requestBody = new
					{
						model,
						messages,
						temperature = ChatbotConfig.CloudNluTemperature,
						max_tokens = effectiveMaxTokens
					};
				}

				var bodyJson = JsonConvert.SerializeObject(requestBody);
				var maxRetries = ChatbotConfig.CloudNluMaxRetries >= 0 ? ChatbotConfig.CloudNluMaxRetries : 0;

				for (var attempt = 0; attempt <= maxRetries; attempt++)
				{
					if (attempt > 0)
						await Task.Delay(TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt - 1)));

					using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
					{
						Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
					};

					if (isAnthropic)
					{
						request.Headers.Add("x-api-key", apiKey);
						request.Headers.Add("anthropic-version", "2023-06-01");
					}
					else if (!isDepartmentOverride && ChatbotConfig.CloudNluProvider == CloudNluProviderType.AzureOpenAI)
					{
						request.Headers.Add("api-key", apiKey);
					}
					else if (isDepartmentOverride && IsAzureOpenAiHost(endpoint))
					{
						request.Headers.Add("api-key", apiKey);
					}
					else
					{
						request.Headers.Add("Authorization", $"Bearer {apiKey}");
					}

					using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(
						ChatbotConfig.CloudNluTimeoutSeconds > 0 ? ChatbotConfig.CloudNluTimeoutSeconds : 15));

					using var response = await _httpClient.SendAsync(request, cts.Token);

					if (response.IsSuccessStatusCode)
					{
						var responseBody = await response.Content.ReadAsStringAsync();
						var root = JObject.Parse(responseBody);

						if (isAnthropic)
						{
							var blocks = root["content"] as JArray;
							return blocks != null && blocks.Count > 0 ? blocks[0]?["text"]?.ToString() : null;
						}

						var choices = root["choices"] as JArray;
						return choices != null && choices.Count > 0 ? choices[0]?["message"]?["content"]?.ToString() : null;
					}

					if (attempt < maxRetries && IsRetryable(response.StatusCode))
						continue;

					Logging.LogError($"Chat completion error (HTTP {(int)response.StatusCode}){FormatRequestId(response)}.");
					return null;
				}

				return null;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Chat completion failed.");
				return null;
			}
		}

		private async Task<(string endpoint, string apiKey, string model, bool isAnthropic, bool isDepartmentOverride)> ResolveAsync(int departmentId)
		{
			DepartmentLlmOverride departmentLlm = null;
			if (departmentId > 0 && _configService != null)
				departmentLlm = await _configService.GetLlmOverrideAsync(departmentId);

			string endpoint;
			string apiKey;
			string model;
			bool isAnthropic;

			if (departmentLlm != null)
			{
				endpoint = departmentLlm.Endpoint;
				apiKey = departmentLlm.ApiKey;
				model = !string.IsNullOrWhiteSpace(departmentLlm.Model) ? departmentLlm.Model : ResolveModel();
				isAnthropic = !string.IsNullOrWhiteSpace(endpoint) && endpoint.IndexOf("anthropic", StringComparison.OrdinalIgnoreCase) >= 0;
			}
			else
			{
				endpoint = ResolveEndpoint();
				apiKey = ResolveApiKey();
				model = ResolveModel();
				isAnthropic = ChatbotConfig.CloudNluProvider == CloudNluProviderType.Anthropic;
			}

			return (endpoint, apiKey, model, isAnthropic, departmentLlm != null);
		}

		private static bool IsRetryable(System.Net.HttpStatusCode statusCode)
		{
			var code = (int)statusCode;
			return code == 429 || code >= 500;
		}

		private static string FormatRequestId(HttpResponseMessage response)
		{
			string[] headers = { "x-request-id", "request-id", "apim-request-id" };
			foreach (var header in headers)
			{
				if (response.Headers.TryGetValues(header, out var values))
				{
					var value = values.FirstOrDefault();
					if (!string.IsNullOrWhiteSpace(value))
						return $" request-id: {value}";
				}
			}

			return string.Empty;
		}

		private static bool IsAzureOpenAiHost(string endpoint)
		{
			if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
				return false;

			return uri.Host.IndexOf(".openai.azure.com", StringComparison.OrdinalIgnoreCase) >= 0
				|| uri.Host.IndexOf(".cognitiveservices.azure.com", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private static string ResolveEndpoint()
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

		private static string ResolveApiKey()
		{
			if (!string.IsNullOrWhiteSpace(ChatbotConfig.CloudNluApiKey))
				return ChatbotConfig.CloudNluApiKey;

			return ChatbotConfig.CloudNluProvider switch
			{
				CloudNluProviderType.DeepSeek => Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY"),
				CloudNluProviderType.OpenAI => Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
				CloudNluProviderType.OpenAiCompatible => Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? Environment.GetEnvironmentVariable("CLOUD_NLU_API_KEY"),
				CloudNluProviderType.AzureOpenAI => Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY"),
				CloudNluProviderType.Anthropic => Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"),
				_ => Environment.GetEnvironmentVariable("CLOUD_NLU_API_KEY")
			};
		}

		private static string ResolveModel()
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

		private static string NormalizeRole(string role)
		{
			return string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase) ? "assistant" : "user";
		}
	}
}
