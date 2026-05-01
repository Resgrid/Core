using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;

namespace Resgrid.Chatbot.Services
{
	public class ChatbotSessionManager : IChatbotSessionManager
	{
		// Phase 2: Primary store via IChatbotSessionStore (Redis or in-memory).
		// In-memory fallback for single-instance deployments.
		private readonly IChatbotSessionStore _sessionStore;

		// Fallback in-memory store when Redis is unavailable
		private static readonly ConcurrentDictionary<string, ChatbotSession> _fallbackSessions = new(StringComparer.OrdinalIgnoreCase);
		private static bool _redisAvailable = false;

		public ChatbotSessionManager(IChatbotSessionStore sessionStore)
		{
			_sessionStore = sessionStore;
		}

		public async Task<ChatbotSession> GetOrCreateSessionAsync(string userId, int departmentId, ChatbotPlatform platform, string fromIdentifier)
		{
			// Try to use the primary store (Redis) first
			if (_redisAvailable && _sessionStore != null)
			{
				try
				{
					var existing = await _sessionStore.GetActiveSessionAsync(userId, platform);
					if (existing != null)
					{
						existing.LastActivity = DateTime.UtcNow;
						return existing;
					}

					var session = CreateNewSession(userId, departmentId, platform);
					await _sessionStore.SaveSessionAsync(session);
					return session;
				}
				catch
				{
					// Redis failed, fall through to in-memory
					_redisAvailable = false;
				}
			}

			// In-memory fallback
			var fallbackExisting = _fallbackSessions.Values.FirstOrDefault(s =>
				s.UserId == userId &&
				s.DepartmentId == departmentId &&
				s.Platform == platform &&
				s.State != ChatbotDialogState.Idle);

			if (fallbackExisting != null)
			{
				fallbackExisting.LastActivity = DateTime.UtcNow;
				return fallbackExisting;
			}

			var fallbackSession = CreateNewSession(userId, departmentId, platform);
			_fallbackSessions[fallbackSession.SessionId] = fallbackSession;
			return fallbackSession;
		}

		public async Task<ChatbotSession> GetSessionAsync(string sessionId)
		{
			if (_redisAvailable && _sessionStore != null)
			{
				try
				{
					var session = await _sessionStore.GetSessionAsync(sessionId);
					if (session != null)
						return session;
				}
				catch
				{
					_redisAvailable = false;
				}
			}

			_fallbackSessions.TryGetValue(sessionId, out var fallbackSession);
			return fallbackSession;
		}

		public async Task SaveSessionAsync(ChatbotSession session)
		{
			if (session == null || string.IsNullOrWhiteSpace(session.SessionId))
				return;

			session.LastActivity = DateTime.UtcNow;

			if (_redisAvailable && _sessionStore != null)
			{
				try
				{
					await _sessionStore.SaveSessionAsync(session);
					return;
				}
				catch
				{
					_redisAvailable = false;
				}
			}

			_fallbackSessions[session.SessionId] = session;
		}

		public async Task EndSessionAsync(string sessionId)
		{
			if (_redisAvailable && _sessionStore != null)
			{
				try
				{
					await _sessionStore.DeleteSessionAsync(sessionId);
					return;
				}
				catch
				{
					_redisAvailable = false;
				}
			}

			_fallbackSessions.TryRemove(sessionId, out _);
		}

		public async Task PruneExpiredSessionsAsync()
		{
			var cutoff = DateTime.UtcNow.AddMinutes(-60);

			if (_redisAvailable && _sessionStore != null)
			{
				try
				{
					await _sessionStore.PruneExpiredAsync(cutoff);
				}
				catch
				{
					_redisAvailable = false;
				}
			}

			var expired = _fallbackSessions.Values
				.Where(s => s.LastActivity < cutoff)
				.Select(s => s.SessionId)
				.ToList();

			foreach (var id in expired)
				_fallbackSessions.TryRemove(id, out _);
		}

		private static ChatbotSession CreateNewSession(string userId, int departmentId, ChatbotPlatform platform)
		{
			return new ChatbotSession
			{
				SessionId = Guid.NewGuid().ToString("N"),
				UserId = userId,
				DepartmentId = departmentId,
				Platform = platform,
				State = ChatbotDialogState.Idle,
				CreatedAt = DateTime.UtcNow,
				LastActivity = DateTime.UtcNow
			};
		}
	}
}
