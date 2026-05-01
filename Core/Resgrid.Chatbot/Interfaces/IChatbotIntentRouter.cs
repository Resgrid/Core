using System.Threading.Tasks;
using Resgrid.Chatbot.Models;

namespace Resgrid.Chatbot.Interfaces
{
	public interface IChatbotIntentRouter
	{
		Task<ChatbotIntent> ClassifyIntentAsync(ChatbotMessage message, ChatbotSession session);
	}
}
