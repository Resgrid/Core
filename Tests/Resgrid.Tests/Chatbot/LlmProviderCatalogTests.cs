using System;
using System.Linq;
using System.Net.Http;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Chatbot.NLU;
using Resgrid.Config;

namespace Resgrid.Tests.Chatbot
{
	/// <summary>Bring-your-own-key provider presets and the request shapes the chatbot sends to them (enhanced-ai-addon-plan.md §8).</summary>
	[TestFixture]
	public class LlmProviderCatalogTests
	{
		[TestCase("https://api.openai.com/v1/chat/completions", "openai", LlmAuthStyle.Bearer)]
		[TestCase("https://api.anthropic.com/v1/messages", "anthropic", LlmAuthStyle.AnthropicApiKey)]
		[TestCase("https://generativelanguage.googleapis.com/v1beta/openai/chat/completions", "google-gemini", LlmAuthStyle.Bearer)]
		[TestCase("https://county.openai.azure.com/openai/deployments/chat/chat/completions?api-version=2024-10-21", "azure-openai", LlmAuthStyle.AzureApiKey)]
		[TestCase("https://county.cognitiveservices.azure.com/openai/deployments/chat/chat/completions?api-version=2024-10-21", "azure-openai", LlmAuthStyle.AzureApiKey)]
		[TestCase("https://api.mistral.ai/v1/chat/completions", "mistral", LlmAuthStyle.Bearer)]
		[TestCase("https://api.x.ai/v1/chat/completions", "xai", LlmAuthStyle.Bearer)]
		[TestCase("https://api.deepseek.com/chat/completions", "deepseek", LlmAuthStyle.Bearer)]
		[TestCase("https://api.groq.com/openai/v1/chat/completions", "groq", LlmAuthStyle.Bearer)]
		[TestCase("https://api.together.xyz/v1/chat/completions", "together", LlmAuthStyle.Bearer)]
		[TestCase("https://api.fireworks.ai/inference/v1/chat/completions", "fireworks", LlmAuthStyle.Bearer)]
		[TestCase("https://openrouter.ai/api/v1/chat/completions", "openrouter", LlmAuthStyle.Bearer)]
		[TestCase("https://api.perplexity.ai/chat/completions", "perplexity", LlmAuthStyle.Bearer)]
		[TestCase("https://api.cohere.ai/compatibility/v1/chat/completions", "cohere", LlmAuthStyle.Bearer)]
		[TestCase("https://llm.county.gov/v1/chat/completions", LlmProviderCatalog.CustomId, LlmAuthStyle.Bearer)]
		[TestCase("not a url", LlmProviderCatalog.CustomId, LlmAuthStyle.Bearer)]
		public void Provider_is_inferred_from_the_endpoint_host(string endpoint, string id, LlmAuthStyle auth)
		{
			var provider = LlmProviderCatalog.Infer(endpoint);
			provider.Id.Should().Be(id);
			provider.Auth.Should().Be(auth);
		}

		[Test]
		public void A_lookalike_host_is_not_a_known_provider()
		{
			LlmProviderCatalog.Infer("https://api.openai.com.evil.example/v1/chat/completions").Id.Should().Be(LlmProviderCatalog.CustomId);
			LlmProviderCatalog.Infer("https://openai.azure.com.evil.example/v1/chat/completions").Auth.Should().Be(LlmAuthStyle.Bearer);
		}

		[Test]
		public void Anthropic_format_follows_the_path_and_the_earlier_url_rule_still_works()
		{
			LlmProviderCatalog.Infer("https://api.anthropic.com/v1/chat/completions").Format.Should().Be(LlmWireFormat.OpenAiChat, "Anthropic's OpenAI-compatible endpoint takes Bearer auth");
			var gateway = LlmProviderCatalog.Infer("https://gateway.example.com/v1/acct/anthropic/v1/messages");
			gateway.Format.Should().Be(LlmWireFormat.AnthropicMessages);
			gateway.Auth.Should().Be(LlmAuthStyle.AnthropicApiKey);
			LlmProviderCatalog.Infer("https://llm.example.com/anthropic-proxy/v1/chat/completions").Format.Should().Be(LlmWireFormat.OpenAiChat);
		}

		[Test]
		public void Every_preset_is_a_public_https_endpoint_or_a_template_to_fill_in()
		{
			LlmProviderCatalog.All.Select(p => p.Id).Should().OnlyHaveUniqueItems();
			foreach (var preset in LlmProviderCatalog.All.Where(p => p.Id != LlmProviderCatalog.CustomId))
			{
				preset.Endpoint.Should().StartWith("https://");
				if (!preset.IsTemplate)
					LlmProviderCatalog.Infer(preset.Endpoint).Id.Should().Be(preset.Id, "a preset's own endpoint maps back to it");
			}
			LlmProviderCatalog.Find("azure-openai").IsTemplate.Should().BeTrue();
		}

		[Test]
		public void System_config_keeps_its_provider_type()
		{
			LlmProviderCatalog.ForSystem(CloudNluProviderType.Anthropic, "https://proxy.example.com/claude").Format.Should().Be(LlmWireFormat.AnthropicMessages);
			LlmProviderCatalog.ForSystem(CloudNluProviderType.AzureOpenAI, "https://aoai.example.com/chat/completions").Auth.Should().Be(LlmAuthStyle.AzureApiKey);
			var onPrem = LlmProviderCatalog.ForSystem(CloudNluProviderType.OpenAiCompatible, "http://10.0.0.5:8000/v1/chat/completions");
			onPrem.Format.Should().Be(LlmWireFormat.OpenAiChat);
			onPrem.Auth.Should().Be(LlmAuthStyle.Bearer);
			LlmProviderCatalog.ForSystem(CloudNluProviderType.OpenAI, "https://api.anthropic.com/v1/messages").Format.Should().Be(LlmWireFormat.OpenAiChat);
		}

		[TestCase(null, null, null, false)]
		[TestCase("https://api.openai.com/v1/chat/completions", "gpt-4.1-mini", null, false)]
		[TestCase("", "", "", false)]
		[TestCase("https://api.mistral.ai/v1/chat/completions", null, null, true)]
		[TestCase(null, "other-model", null, true)]
		[TestCase(null, null, "sk-new", true)]
		public void Only_a_new_key_endpoint_or_model_sets_a_new_provider(string endpoint, string model, string key, bool setsNew)
		{
			LlmProviderCatalog.SetsNewProvider("https://api.openai.com/v1/chat/completions", "gpt-4.1-mini", endpoint, model, key).Should().Be(setsNew);
		}

		[Test]
		public void Anthropic_body_has_a_top_level_system_prompt_no_temperature_and_room_to_think()
		{
			var body = JObject.Parse(LlmWire.Body(LlmWireFormat.AnthropicMessages, LlmProviderCatalog.Find("anthropic").Compat, "claude-opus-5", "classify",
				new[] { ("user", "status responding") }, 256, 0, jsonMode: true));

			body["system"].ToString().Should().Be("classify");
			body["max_tokens"].Value<int>().Should().Be(LlmWire.ReasoningOutputFloor);
			body.ContainsKey("temperature").Should().BeFalse("current Claude models reject sampling parameters");
			body.ContainsKey("response_format").Should().BeFalse();
			((JArray)body["messages"]).Should().ContainSingle().Which["role"].ToString().Should().Be("user");
		}

		[Test]
		public void OpenAi_body_uses_max_completion_tokens_and_json_mode()
		{
			var body = JObject.Parse(LlmWire.Body(LlmWireFormat.OpenAiChat, LlmProviderCatalog.Find("openai").Compat, "gpt-4.1-mini", "classify",
				new[] { ("user", "status responding") }, 256, 0.1, jsonMode: true));

			body.ContainsKey("max_tokens").Should().BeFalse();
			body["max_completion_tokens"].Value<int>().Should().Be(LlmWire.ReasoningOutputFloor);
			body["response_format"]["type"].ToString().Should().Be("json_object");
			body["temperature"].Value<double>().Should().Be(0.1);
			body["messages"][0]["role"].ToString().Should().Be("system");

			var other = JObject.Parse(LlmWire.Body(LlmWireFormat.OpenAiChat, LlmCompat.None, "m", "s", new[] { ("user", "u") }, 256, 0, jsonMode: false));
			other["max_tokens"].Value<int>().Should().Be(256, "other OpenAI-compatible providers keep the configured limit");
			other.ContainsKey("response_format").Should().BeFalse();
		}

		[Test]
		public void A_400_naming_a_parameter_adjusts_the_request_once()
		{
			LlmWire.Adjust(LlmCompat.None, LlmWireFormat.OpenAiChat, "{\"error\":{\"message\":\"Unsupported parameter: 'max_tokens' is not supported with this model. Use 'max_completion_tokens' instead.\"}}")
				.Should().Be(LlmCompat.MaxCompletionTokens);
			LlmWire.Adjust(LlmCompat.MaxCompletionTokens, LlmWireFormat.OpenAiChat, "Unrecognized request argument supplied: max_completion_tokens")
				.Should().Be(LlmCompat.None, "an older Azure api-version takes max_tokens");
			LlmWire.Adjust(LlmCompat.None, LlmWireFormat.OpenAiChat, "Unsupported value: 'temperature' does not support 0 with this model.").Should().Be(LlmCompat.OmitTemperature);
			LlmWire.Adjust(LlmCompat.None, LlmWireFormat.OpenAiChat, "response_format of type json_object is not supported").Should().Be(LlmCompat.OmitJsonMode);
			LlmWire.Adjust(LlmCompat.None, LlmWireFormat.OpenAiChat, "Invalid API key").Should().BeNull("a retry would not help");
			LlmWire.Adjust(LlmCompat.None, LlmWireFormat.AnthropicMessages, "response_format").Should().BeNull();
		}

		[Test]
		public void Learned_adjustments_are_reused_for_the_same_endpoint_and_model()
		{
			var endpoint = $"https://learn-{Guid.NewGuid():N}.example.com/v1/chat/completions";
			LlmWire.CompatFor(LlmProviderCatalog.Custom, endpoint, "m").Should().Be(LlmCompat.None);
			LlmWire.Remember(endpoint, "m", LlmCompat.OmitTemperature);
			LlmWire.CompatFor(LlmProviderCatalog.Custom, endpoint, "m").Should().Be(LlmCompat.OmitTemperature);
			LlmWire.CompatFor(LlmProviderCatalog.Custom, endpoint, "other").Should().Be(LlmCompat.None);
		}

		[Test]
		public void Anthropic_text_skips_a_leading_thinking_block()
		{
			var root = JObject.Parse("{\"content\":[{\"type\":\"thinking\",\"thinking\":\"\"},{\"type\":\"text\",\"text\":\"{\\\"intent\\\":\\\"help\\\"}\"}],\"usage\":{\"input_tokens\":10,\"output_tokens\":5}}");
			LlmWire.Text(root, LlmWireFormat.AnthropicMessages).Should().Be("{\"intent\":\"help\"}");
			LlmWire.TotalTokens(root, LlmWireFormat.AnthropicMessages).Should().Be(15);
			LlmWire.Text(JObject.Parse("{\"choices\":[{\"message\":{\"content\":\"hi\"}}],\"usage\":{\"total_tokens\":7}}"), LlmWireFormat.OpenAiChat).Should().Be("hi");
		}

		[Test]
		public void Json_is_read_out_of_a_fence_or_a_sentence()
		{
			LlmWire.JsonObject("```json\n{\"intent\":\"help\",\"confidence\":0.9}\n```").Should().Be("{\"intent\":\"help\",\"confidence\":0.9}");
			LlmWire.JsonObject("Here you go: {\"intent\":\"help\"} Thanks").Should().Be("{\"intent\":\"help\"}");
			LlmWire.JsonObject("no json").Should().Be("no json");
		}

		[TestCase(LlmAuthStyle.Bearer, "Authorization", "Bearer k")]
		[TestCase(LlmAuthStyle.AzureApiKey, "api-key", "k")]
		[TestCase(LlmAuthStyle.AnthropicApiKey, "x-api-key", "k")]
		public void Each_auth_style_sets_its_header(LlmAuthStyle auth, string header, string value)
		{
			using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com");
			LlmWire.Authorize(request, auth, "k");
			request.Headers.GetValues(header).Should().ContainSingle().Which.Should().Be(value);
			if (auth == LlmAuthStyle.AnthropicApiKey)
				request.Headers.GetValues("anthropic-version").Should().ContainSingle().Which.Should().Be("2023-06-01");
		}
	}
}
