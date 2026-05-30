using System.Threading.Tasks;
using Resgrid.Chatbot.Models;

namespace Resgrid.Providers.Chatbot.Interfaces
{
	public interface IChatbotPlatformAdapter
	{
		ChatbotPlatform Platform { get; }
		Task<ChatbotMessage> ParseInboundMessageAsync(object rawRequest);
		Task<string> FormatOutboundResponseAsync(ChatbotResponse response);

		// Phase 3 (P3.0): proactively send a (potentially rich) response to a specific platform user.
		// Used both for multi-turn replies and for outbound delivery (call alerts, etc.) via the
		// IChatbotOutboundService / IChatbotAdapterRegistry fan-out (§15).
		Task SendRichResponseAsync(string platformUserId, ChatbotResponse response);

		// Phase 3 (P3.0): per-platform capability flags so handlers/renderers can adapt output.
		ChatbotPlatformCapabilities GetCapabilities();

		// Phase 3 (P3.0): send a typing indicator where the platform supports it (no-op otherwise).
		Task SendTypingIndicatorAsync(string platformUserId);
	}
}
