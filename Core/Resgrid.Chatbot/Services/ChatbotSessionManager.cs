using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;

namespace Resgrid.Chatbot.Services
{
	/// <summary>
	/// Thin coordinator over <see cref="IChatbotSessionStore"/>. The store implementation decides
	/// whether to use Redis (shared across instances) or the in-memory fallback, so the manager
	/// simply delegates.
	/// </summary>
	public class ChatbotSessionManager : IChatbotSessionManager
	{
		private readonly IChatbotSessionStore _sessionStore;

		public ChatbotSessionManager(IChatbotSessionStore sessionStore)
		{
			_sessionStore = sessionStore;
		}

		public Task<ChatbotSession> GetOrCreateSessionAsync(string userId, int departmentId, ChatbotPlatform platform, string fromIdentifier)
			=> _sessionStore.GetOrCreateAsync(userId, departmentId, platform, fromIdentifier);

		public Task<ChatbotSession> GetSessionAsync(string sessionId)
			=> _sessionStore.GetAsync(sessionId);

		public Task SaveSessionAsync(ChatbotSession session)
			=> _sessionStore.SaveAsync(session);

		public Task EndSessionAsync(string sessionId)
			=> _sessionStore.DeleteAsync(sessionId);

		public Task PruneExpiredSessionsAsync()
			=> _sessionStore.PruneExpiredAsync(null);
	}
}
