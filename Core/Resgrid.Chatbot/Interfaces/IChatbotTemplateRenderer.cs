using System.Threading.Tasks;
using Resgrid.Chatbot.Models;

namespace Resgrid.Chatbot.Interfaces
{
	public interface IChatbotTemplateRenderer
	{
		string Render(string templateName, object model, ChatbotPlatform platform);
		Task<ChatbotResponse> RenderResponseAsync(string templateName, object model, ChatbotPlatform platform, ChatbotIntent intent);
	}
}
