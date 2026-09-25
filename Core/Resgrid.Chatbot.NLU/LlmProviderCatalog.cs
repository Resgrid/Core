using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using Resgrid.Config;

namespace Resgrid.Chatbot.NLU
{
	public enum LlmWireFormat
	{
		/// <summary>OpenAI chat-completions request and response shape, which almost every provider now accepts.</summary>
		OpenAiChat,

		/// <summary>Anthropic Messages API (/v1/messages).</summary>
		AnthropicMessages
	}

	public enum LlmAuthStyle
	{
		Bearer,
		AzureApiKey,
		AnthropicApiKey
	}

	/// <summary>Request adjustments a provider or model needs. Learned flags are added after a 400 names a parameter this client sent.</summary>
	[Flags]
	public enum LlmCompat
	{
		None = 0,

		/// <summary>Current Claude models and OpenAI reasoning models reject a non-default temperature.</summary>
		OmitTemperature = 1,

		/// <summary>The provider has no response_format json_object mode; the prompt already asks for JSON.</summary>
		OmitJsonMode = 2,

		/// <summary>OpenAI reasoning models reject max_tokens and take max_completion_tokens instead.</summary>
		MaxCompletionTokens = 4
	}

	/// <summary>A provider a department can connect with its own subscription (bring your own key). Endpoint is the full request URL.</summary>
	public sealed record LlmProviderPreset(string Id, string Name, string Endpoint, string ExampleModel, LlmWireFormat Format, LlmAuthStyle Auth, LlmCompat Compat, params string[] Hosts)
	{
		/// <summary>The endpoint has placeholders ({resource}, {deployment}) the admin replaces with their own values.</summary>
		public bool IsTemplate => Endpoint.Contains('{');
	}

	/// <summary>
	/// Bring-your-own-key LLM providers (enhanced-ai-addon-plan.md §8). Departments store only an endpoint, key and model, so
	/// the provider is inferred from the endpoint host: that picks the auth header, the wire format and known request quirks.
	/// Unknown hosts are treated as OpenAI-compatible with Bearer auth, which covers gateways and self-hosted servers
	/// (vLLM, Ollama, LiteLLM) published on a public https address.
	/// </summary>
	public static class LlmProviderCatalog
	{
		public const string CustomId = "custom";

		private const LlmCompat AnthropicCompat = LlmCompat.OmitTemperature | LlmCompat.OmitJsonMode;

		public static readonly LlmProviderPreset Custom = new(CustomId, "Other OpenAI-compatible endpoint", "", "", LlmWireFormat.OpenAiChat, LlmAuthStyle.Bearer, LlmCompat.None);

		public static readonly IReadOnlyList<LlmProviderPreset> All = new[]
		{
			new LlmProviderPreset("openai", "OpenAI", "https://api.openai.com/v1/chat/completions", "gpt-4.1-mini",
				LlmWireFormat.OpenAiChat, LlmAuthStyle.Bearer, LlmCompat.MaxCompletionTokens, "api.openai.com"),
			new LlmProviderPreset("anthropic", "Anthropic (Claude)", "https://api.anthropic.com/v1/messages", "claude-opus-5",
				LlmWireFormat.AnthropicMessages, LlmAuthStyle.AnthropicApiKey, AnthropicCompat, "api.anthropic.com"),
			new LlmProviderPreset("google-gemini", "Google Gemini", "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions", "gemini-2.5-flash",
				LlmWireFormat.OpenAiChat, LlmAuthStyle.Bearer, LlmCompat.None, "generativelanguage.googleapis.com"),
			new LlmProviderPreset("azure-openai", "Microsoft Azure OpenAI", "https://{resource}.openai.azure.com/openai/deployments/{deployment}/chat/completions?api-version=2024-10-21", "",
				LlmWireFormat.OpenAiChat, LlmAuthStyle.AzureApiKey, LlmCompat.None, ".openai.azure.com", ".cognitiveservices.azure.com", ".services.ai.azure.com"),
			new LlmProviderPreset("mistral", "Mistral AI", "https://api.mistral.ai/v1/chat/completions", "mistral-small-latest",
				LlmWireFormat.OpenAiChat, LlmAuthStyle.Bearer, LlmCompat.None, "api.mistral.ai"),
			new LlmProviderPreset("xai", "xAI (Grok)", "https://api.x.ai/v1/chat/completions", "grok-3-mini",
				LlmWireFormat.OpenAiChat, LlmAuthStyle.Bearer, LlmCompat.None, "api.x.ai"),
			new LlmProviderPreset("deepseek", "DeepSeek", "https://api.deepseek.com/chat/completions", "deepseek-chat",
				LlmWireFormat.OpenAiChat, LlmAuthStyle.Bearer, LlmCompat.None, "api.deepseek.com"),
			new LlmProviderPreset("groq", "Groq", "https://api.groq.com/openai/v1/chat/completions", "llama-3.3-70b-versatile",
				LlmWireFormat.OpenAiChat, LlmAuthStyle.Bearer, LlmCompat.None, "api.groq.com"),
			new LlmProviderPreset("together", "Together AI", "https://api.together.xyz/v1/chat/completions", "meta-llama/Llama-3.3-70B-Instruct-Turbo",
				LlmWireFormat.OpenAiChat, LlmAuthStyle.Bearer, LlmCompat.None, "api.together.xyz", "api.together.ai"),
			new LlmProviderPreset("fireworks", "Fireworks AI", "https://api.fireworks.ai/inference/v1/chat/completions", "accounts/fireworks/models/llama-v3p3-70b-instruct",
				LlmWireFormat.OpenAiChat, LlmAuthStyle.Bearer, LlmCompat.None, "api.fireworks.ai"),
			new LlmProviderPreset("openrouter", "OpenRouter", "https://openrouter.ai/api/v1/chat/completions", "openai/gpt-4.1-mini",
				LlmWireFormat.OpenAiChat, LlmAuthStyle.Bearer, LlmCompat.None, "openrouter.ai"),
			new LlmProviderPreset("perplexity", "Perplexity", "https://api.perplexity.ai/chat/completions", "sonar",
				LlmWireFormat.OpenAiChat, LlmAuthStyle.Bearer, LlmCompat.OmitJsonMode, "api.perplexity.ai"),
			new LlmProviderPreset("cohere", "Cohere", "https://api.cohere.ai/compatibility/v1/chat/completions", "command-a-03-2025",
				LlmWireFormat.OpenAiChat, LlmAuthStyle.Bearer, LlmCompat.None, "api.cohere.ai", "api.cohere.com"),
			Custom
		};

		public static LlmProviderPreset Find(string id) =>
			All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

		/// <summary>The provider a department endpoint belongs to; never null (unknown hosts are <see cref="Custom"/>).</summary>
		public static LlmProviderPreset Infer(string endpoint)
		{
			if (string.IsNullOrWhiteSpace(endpoint) || !Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri))
				return Custom;

			var preset = All.FirstOrDefault(p => p.Hosts.Any(h => h.StartsWith('.')
				? uri.Host.EndsWith(h, StringComparison.OrdinalIgnoreCase)
				: uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase))) ?? Custom;
			var chatCompletions = uri.AbsolutePath.TrimEnd('/').EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase);

			// Anthropic also serves an OpenAI-compatible /v1/chat/completions endpoint, which takes Bearer auth.
			if (preset.Format == LlmWireFormat.AnthropicMessages && chatCompletions)
				return preset with { Format = LlmWireFormat.OpenAiChat, Auth = LlmAuthStyle.Bearer, Compat = LlmCompat.OmitTemperature };

			// Earlier releases detected Anthropic by "anthropic" anywhere in the URL; keep that for gateways proxying the Messages API.
			if (preset.Id == CustomId && !chatCompletions && endpoint.IndexOf("anthropic", StringComparison.OrdinalIgnoreCase) >= 0)
				return preset with { Format = LlmWireFormat.AnthropicMessages, Auth = LlmAuthStyle.AnthropicApiKey, Compat = AnthropicCompat };

			return preset;
		}

		/// <summary>
		/// Whether a posted configuration sets a provider the department has not already saved: a new key, or an endpoint or
		/// model that differs from the saved one. Blank values keep or clear what is saved and never count as a new provider,
		/// so a department without the Enhanced AI add-on can still save its other settings or remove its provider.
		/// </summary>
		public static bool SetsNewProvider(string savedEndpoint, string savedModel, string endpoint, string model, string newKey) =>
			!string.IsNullOrWhiteSpace(newKey) ||
			!string.IsNullOrWhiteSpace(endpoint) && !string.Equals(endpoint.Trim(), savedEndpoint?.Trim(), StringComparison.OrdinalIgnoreCase) ||
			!string.IsNullOrWhiteSpace(model) && !string.Equals(model.Trim(), savedModel?.Trim(), StringComparison.Ordinal);

		/// <summary>The provider for the operator's system-level ChatbotConfig (open-source and on-prem installs); the configured provider type wins over the host.</summary>
		public static LlmProviderPreset ForSystem(CloudNluProviderType provider, string endpoint)
		{
			var inferred = Infer(endpoint);
			return provider switch
			{
				CloudNluProviderType.Anthropic => inferred.Format == LlmWireFormat.AnthropicMessages ? inferred
					: Find("anthropic") with { Endpoint = endpoint ?? "", Hosts = Array.Empty<string>() },
				CloudNluProviderType.AzureOpenAI => inferred.Auth == LlmAuthStyle.AzureApiKey ? inferred
					: Find("azure-openai") with { Endpoint = endpoint ?? "", Hosts = Array.Empty<string>() },
				_ => inferred.Format == LlmWireFormat.OpenAiChat ? inferred : Custom
			};
		}
	}

	/// <summary>Request building, auth and response reading shared by the chatbot's intent classifier and conversational fallback.</summary>
	public static class LlmWire
	{
		/// <summary>Reasoning models spend part of the output limit thinking before they answer, so a small classifier limit would leave no answer.</summary>
		public const int ReasoningOutputFloor = 1024;

		private const int MaxLearned = 1000;
		private static readonly ConcurrentDictionary<string, LlmCompat> Learned = new(StringComparer.OrdinalIgnoreCase);

		public static LlmCompat CompatFor(LlmProviderPreset preset, string endpoint, string model) =>
			preset.Compat | (Learned.TryGetValue(LearnKey(endpoint, model), out var learned) ? learned : LlmCompat.None);

		/// <summary>Remembers what an endpoint and model needed so later requests skip the failed first attempt.</summary>
		public static void Remember(string endpoint, string model, LlmCompat compat)
		{
			if (Learned.Count >= MaxLearned)
				Learned.Clear();
			Learned[LearnKey(endpoint, model)] = compat;
		}

		/// <summary>
		/// The adjusted flags when a 400 response names a parameter this client sent, or null when a retry would not help.
		/// Only parameter names are matched; the error text is never echoed back to the provider.
		/// </summary>
		public static LlmCompat? Adjust(LlmCompat current, LlmWireFormat format, string errorBody)
		{
			if (string.IsNullOrWhiteSpace(errorBody))
				return null;

			var next = current;
			if (!current.HasFlag(LlmCompat.OmitTemperature) && errorBody.IndexOf("temperature", StringComparison.OrdinalIgnoreCase) >= 0)
				next |= LlmCompat.OmitTemperature;
			if (format == LlmWireFormat.OpenAiChat)
			{
				if (!current.HasFlag(LlmCompat.OmitJsonMode) && (errorBody.IndexOf("response_format", StringComparison.OrdinalIgnoreCase) >= 0 ||
					errorBody.IndexOf("json_object", StringComparison.OrdinalIgnoreCase) >= 0))
					next |= LlmCompat.OmitJsonMode;
				// OpenAI reasoning models ask for max_completion_tokens; an older Azure api-version rejects it. Either way, switch.
				if (errorBody.IndexOf("max_completion_tokens", StringComparison.OrdinalIgnoreCase) >= 0)
					next ^= LlmCompat.MaxCompletionTokens;
			}

			return next == current ? null : next;
		}

		public static string Body(LlmWireFormat format, LlmCompat compat, string model, string system, IEnumerable<(string Role, string Content)> turns,
			int maxTokens, double temperature, bool jsonMode)
		{
			var body = new JObject { ["model"] = model };
			var messages = new JArray();
			if (format == LlmWireFormat.AnthropicMessages)
			{
				body["max_tokens"] = Math.Max(maxTokens, ReasoningOutputFloor);
				if (!string.IsNullOrWhiteSpace(system))
					body["system"] = system;
			}
			else
			{
				body[compat.HasFlag(LlmCompat.MaxCompletionTokens) ? "max_completion_tokens" : "max_tokens"] =
					compat.HasFlag(LlmCompat.MaxCompletionTokens) ? Math.Max(maxTokens, ReasoningOutputFloor) : maxTokens;
				if (!string.IsNullOrWhiteSpace(system))
					messages.Add(new JObject { ["role"] = "system", ["content"] = system });
				if (jsonMode && !compat.HasFlag(LlmCompat.OmitJsonMode))
					body["response_format"] = new JObject { ["type"] = "json_object" };
			}

			if (!compat.HasFlag(LlmCompat.OmitTemperature))
				body["temperature"] = temperature;
			foreach (var (role, content) in turns)
				messages.Add(new JObject { ["role"] = role, ["content"] = content });
			body["messages"] = messages;
			return body.ToString(Newtonsoft.Json.Formatting.None);
		}

		public static void Authorize(HttpRequestMessage request, LlmAuthStyle auth, string apiKey)
		{
			switch (auth)
			{
				case LlmAuthStyle.AnthropicApiKey:
					request.Headers.Add("x-api-key", apiKey);
					request.Headers.Add("anthropic-version", "2023-06-01");
					break;
				case LlmAuthStyle.AzureApiKey:
					request.Headers.Add("api-key", apiKey);
					break;
				default:
					request.Headers.Add("Authorization", $"Bearer {apiKey}");
					break;
			}
		}

		/// <summary>The answer text: the first text block for Anthropic (a thinking block may come first), else the first choice's message.</summary>
		public static string Text(JObject root, LlmWireFormat format)
		{
			if (root == null)
				return null;
			if (format == LlmWireFormat.AnthropicMessages)
				return (root["content"] as JArray)?.FirstOrDefault(b => string.Equals(b?["type"]?.ToString(), "text", StringComparison.Ordinal))?["text"]?.ToString();

			var choices = root["choices"] as JArray;
			return choices != null && choices.Count > 0 ? choices[0]?["message"]?["content"]?.ToString() : null;
		}

		/// <summary>Total tokens from either usage shape, or null when the provider reports none.</summary>
		public static int? TotalTokens(JObject root, LlmWireFormat format)
		{
			var usage = root?["usage"];
			if (usage == null || usage.Type != JTokenType.Object)
				return null;
			return format == LlmWireFormat.AnthropicMessages
				? (usage["input_tokens"]?.Value<int?>() ?? 0) + (usage["output_tokens"]?.Value<int?>() ?? 0)
				: usage["total_tokens"]?.Value<int?>();
		}

		/// <summary>The JSON object in a reply, without a markdown fence or prose around it; providers without a JSON mode often add them.</summary>
		public static string JsonObject(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
				return text;
			var start = text.IndexOf('{');
			var end = text.LastIndexOf('}');
			return start >= 0 && end > start ? text.Substring(start, end - start + 1) : text.Trim();
		}

		private static string LearnKey(string endpoint, string model) => $"{endpoint?.Trim()}|{model?.Trim()}";
	}
}
