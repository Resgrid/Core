using System.Threading.Tasks;
using Resgrid.Chatbot.Models;

namespace Resgrid.Chatbot.Interfaces
{
	public interface IChatbotActionHandler
	{
		ChatbotIntentType IntentType { get; }
		Task<ChatbotResponse> HandleAsync(ChatbotMessage message, ChatbotIntent intent, ChatbotSession session);

		/// <summary>
		/// Returns true if this handler can process the given intent type.
		/// Override in handlers that field multiple intent types (e.g. DepartmentActionHandler).
		/// Default implementation checks exact match on IntentType.
		/// </summary>
		bool CanHandle(ChatbotIntentType intentType) => intentType == IntentType;
	}
}
