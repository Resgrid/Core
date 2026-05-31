using System.Threading.Tasks;
using Resgrid.Chatbot.Models;

namespace Resgrid.Chatbot.Services
{
	public interface IIntentMapper
	{
		ChatbotIntent MapToIntent(NLUResult nluResult);
	}
}
