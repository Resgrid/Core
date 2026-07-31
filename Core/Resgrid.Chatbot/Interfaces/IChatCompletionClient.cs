using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Chatbot.Interfaces
{
	/// <summary>A single conversational turn for chat completion ("user" or "assistant").</summary>
	public class ChatCompletionTurn
	{
		public string Role { get; set; }
		public string Content { get; set; }

		public ChatCompletionTurn()
		{
		}

		public ChatCompletionTurn(string role, string content)
		{
			Role = role;
			Content = content;
		}
	}

	/// <summary>
	/// Free-form chat completion against the configured cloud LLM (OpenAI/Azure/DeepSeek/Anthropic —
	/// same provider-pluggable resolution and per-department override as the cloud NLU classifier).
	/// Returns null when the provider is unconfigured or the call fails; callers must treat that as
	/// "no answer" and fall back gracefully.
	/// </summary>
	public interface IChatCompletionClient
	{
		Task<bool> IsAvailableAsync(int departmentId);

		Task<string> CompleteAsync(int departmentId, string systemPrompt, List<ChatCompletionTurn> turns, int maxTokens = 512);
	}
}
