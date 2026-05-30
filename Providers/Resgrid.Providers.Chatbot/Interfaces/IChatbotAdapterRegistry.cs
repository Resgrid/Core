using Resgrid.Chatbot.Models;

namespace Resgrid.Providers.Chatbot.Interfaces
{
	/// <summary>
	/// Resolves the platform adapter for a given <see cref="ChatbotPlatform"/> and answers whether the
	/// bot can send a proactive (un-prompted) message on that platform. Used by the outbound delivery
	/// channel; platforms that require a prior conversation/template are reported as not-yet-initiable
	/// so callers can fall back to SMS/push (see Phase 3 §15.3).
	/// </summary>
	public interface IChatbotAdapterRegistry
	{
		IChatbotPlatformAdapter GetAdapter(ChatbotPlatform platform);
		bool CanInitiateProactively(ChatbotPlatform platform);
	}
}
