using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// No-op outbound chat delivery. Registered as the default (with PreserveExistingDefaults) so
	/// <see cref="CommunicationService"/> always resolves an <see cref="IChatbotOutboundService"/> — even
	/// in hosts (e.g. workers) that don't load the chatbot provider module. The real
	/// ChatbotOutboundService overrides it when ChatbotProviderModule is present.
	/// </summary>
	public class NullChatbotOutboundService : IChatbotOutboundService
	{
		public Task<ChatbotOutboundResult> SendToUserAsync(string userId, int departmentId, ChatbotOutboundMessage message)
			=> Task.FromResult(new ChatbotOutboundResult());
	}
}
