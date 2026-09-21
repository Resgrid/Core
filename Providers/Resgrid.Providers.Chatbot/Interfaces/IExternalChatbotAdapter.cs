using System.Threading.Tasks;
using Resgrid.Chatbot.Models;

namespace Resgrid.Providers.Chatbot.Interfaces
{
	/// <summary>A native transport, with an explicit distinction between replies and unsolicited sends.</summary>
	public interface IExternalChatbotAdapter : IChatbotPlatformAdapter
	{
		bool IsConfigured { get; }
		bool IsInboundConfigured { get; }
		bool CanInitiateProactively { get; }
		Task SendReplyAsync(ChatbotMessage message, ChatbotResponse response);
	}
}
