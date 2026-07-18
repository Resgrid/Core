using System.Threading.Tasks;
using Resgrid.Chatbot.Models;

namespace Resgrid.Chatbot.Interfaces
{
	public interface IConversationEngine
	{
		Task<ConversationResult> ProcessAsync(ChatbotMessage message, ChatbotSession session, ChatbotIntent intent);
		
		// Phase 2: Multi-turn dialog methods called by IngressService
		Task<ChatbotResponse> HandleContinuationAsync(ChatbotMessage message, ChatbotSession session);
		bool NeedsParameterCollection(ChatbotIntent intent);
		Task<ChatbotResponse> BeginDialogAsync(ChatbotIntent intent, ChatbotSession session);
	}

	public class ConversationResult
	{
		public ChatbotResponse Response { get; set; }
		public bool IsComplete { get; set; }
		public bool NeedsFollowUp { get; set; }
		public ChatbotDialogState NextState { get; set; }
	}
}
