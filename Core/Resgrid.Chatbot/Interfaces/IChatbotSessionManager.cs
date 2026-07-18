using System.Threading.Tasks;
using Resgrid.Chatbot.Models;

namespace Resgrid.Chatbot.Interfaces
{
	public interface IChatbotSessionManager
	{
		Task<ChatbotSession> GetOrCreateSessionAsync(string userId, int departmentId, ChatbotPlatform platform,
			string fromIdentifier, int ttlMinutes = 0);
		Task<ChatbotSession> GetSessionAsync(string sessionId);
		Task SaveSessionAsync(ChatbotSession session);
		Task EndSessionAsync(string sessionId);
		Task PruneExpiredSessionsAsync();
	}
}
