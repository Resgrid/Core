using System.Threading.Tasks;
using Resgrid.Chatbot.Models;

namespace Resgrid.Providers.Chatbot.Interfaces
{
	public interface IChatbotPlatformAdapter
	{
		ChatbotPlatform Platform { get; }
		Task<ChatbotMessage> ParseInboundMessageAsync(object rawRequest);
		Task<string> FormatOutboundResponseAsync(ChatbotResponse response);
	}
}
