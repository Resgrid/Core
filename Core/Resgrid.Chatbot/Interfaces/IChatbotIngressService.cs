using System.Threading.Tasks;
using Resgrid.Chatbot.Models;

namespace Resgrid.Chatbot.Interfaces
{
	public interface IChatbotIngressService
	{
		Task<ChatbotResponse> ProcessMessageAsync(ChatbotMessage message);
	}
}
