using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Chatbot.Models;

namespace Resgrid.Chatbot.Interfaces
{
	public interface IEntityExtractor
	{
		Task<List<ChatbotEntity>> ExtractAsync(string text, string intentName, int departmentId);
	}
}
