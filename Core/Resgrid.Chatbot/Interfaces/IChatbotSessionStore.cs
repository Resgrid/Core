using System;
using System.Threading.Tasks;
using Resgrid.Chatbot.Models;

namespace Resgrid.Chatbot.Interfaces
{
	public interface IChatbotSessionStore
	{
		Task<ChatbotSession> GetOrCreateAsync(string userId, int departmentId, ChatbotPlatform platform, string identifier);
		Task<ChatbotSession> GetAsync(string sessionId);
		Task SaveAsync(ChatbotSession session);
		Task DeleteAsync(string sessionId);
		Task PruneExpiredAsync(DateTime? cutoff = null);

		// Aliases used by ChatbotSessionManager
		Task<ChatbotSession> GetActiveSessionAsync(string userId, ChatbotPlatform platform) => GetOrCreateAsync(userId, 0, platform, string.Empty);
		Task<ChatbotSession> GetSessionAsync(string sessionId) => GetAsync(sessionId);
		Task SaveSessionAsync(ChatbotSession session) => SaveAsync(session);
		Task DeleteSessionAsync(string sessionId) => DeleteAsync(sessionId);
	}
}
