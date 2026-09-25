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
	/// ChatbotConfig (the operator's own or on-prem model, or OpenAI / Azure OpenAI / DeepSeek / Anthropic) with a
	/// department's own provider (bring your own key, LlmProviderCatalog) honored when its Enhanced AI entitlement allows.
	/// Used by the chatbot's conversational fallback; failures return null, never throw.
	/// </summary>
	public class OpenAiCompatibleChatCompletionClient : IChatCompletionClient
	{
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

				var (endpoint, apiKey, model, provider, isDepartmentOverride) = await ResolveAsync(departmentId);
				if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(endpoint))
					return null;

				// SSRF guard: a department endpoint must be an absolute https URI resolving only to public addresses. The
				// operator may opt in to a private or on-prem system endpoint (ChatbotConfig.CloudNluAllowPrivateEndpoint).
				var allowPrivate = !isDepartmentOverride && ChatbotConfig.CloudNluAllowPrivateEndpoint;
				if (!allowPrivate && !LlmEndpointValidator.IsValid(endpoint, out var endpointError))
				{
					Logging.LogError($"Chat completion rejected for department {departmentId}: invalid LLM endpoint ({endpointError})");
					return null;
				}

				var httpClient = Resgrid.Llm.OperatorEndpointPolicy.GetSharedClient(new Uri(endpoint), allowPrivate);
				var effectiveMaxTokens = maxTokens ?? (ChatbotConfig.CloudNluMaxTokens > 0 ? ChatbotConfig.CloudNluMaxTokens : 512);
				var chatTurns = turns.Select(t => (NormalizeRole(t.Role), t.Content)).ToList();
				var compat = LlmWire.CompatFor(provider, endpoint, model);
				var compatRetried = false;
				var maxRetries = ChatbotConfig.CloudNluMaxRetries >= 0 ? ChatbotConfig.CloudNluMaxRetries : 0;

				for (var attempt = 0; attempt <= maxRetries; attempt++)
				{
					if (attempt > 0)
						await Task.Delay(TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt - 1)));

					using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
					{
						Content = new StringContent(LlmWire.Body(provider.Format, compat, model, systemPrompt, chatTurns, effectiveMaxTokens,
							Math.Round(ChatbotConfig.CloudNluTemperature, 3), jsonMode: false), Encoding.UTF8, "application/json")
					};
					LlmWire.Authorize(request, provider.Auth, apiKey);

					using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(
						ChatbotConfig.CloudNluTimeoutSeconds > 0 ? ChatbotConfig.CloudNluTimeoutSeconds : 15));

					using var response = await httpClient.SendAsync(request, cts.Token);

					if (response.IsSuccessStatusCode)
					{
						if (compatRetried)
							LlmWire.Remember(endpoint, model, compat);
						return LlmWire.Text(JObject.Parse(await response.Content.ReadAsStringAsync()), provider.Format);
					}

					// A 400 naming a parameter this model does not take (temperature, max_tokens, response_format) is retried once without it.
					if ((int)response.StatusCode == 400 && !compatRetried &&
						LlmWire.Adjust(compat, provider.Format, await response.Content.ReadAsStringAsync()) is { } adjusted)
					{
						compat = adjusted;
						compatRetried = true;
						attempt--;
						continue;
					}

					if (attempt < maxRetries && IsRetryable(response.StatusCode))
						continue;

					Logging.LogError($"Chat completion error from {provider.Name} (HTTP {(int)response.StatusCode}){FormatRequestId(response)}.");
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

		private async Task<(string endpoint, string apiKey, string model, LlmProviderPreset provider, bool isDepartmentOverride)> ResolveAsync(int departmentId)
		{
			// A department's own provider (bring your own key) applies only while GetLlmOverrideAsync allows it.
			DepartmentLlmOverride departmentLlm = null;
			if (departmentId > 0 && _configService != null)
				departmentLlm = await _configService.GetLlmOverrideAsync(departmentId);

			if (departmentLlm != null)
				return (departmentLlm.Endpoint, departmentLlm.ApiKey,
					!string.IsNullOrWhiteSpace(departmentLlm.Model) ? departmentLlm.Model : ResolveModel(), LlmProviderCatalog.Infer(departmentLlm.Endpoint), true);

			var endpoint = ResolveEndpoint();
			return (endpoint, ResolveApiKey(), ResolveModel(), LlmProviderCatalog.ForSystem(ChatbotConfig.CloudNluProvider, endpoint), false);
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
				CloudNluProviderType.Anthropic => "claude-opus-5",
				_ => "gpt-4o"
			};
		}

		private static string NormalizeRole(string role)
		{
			return string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase) ? "assistant" : "user";
		}
	}
}
