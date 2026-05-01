using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Chatbot.Config;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;
using Resgrid.Framework;

namespace Resgrid.Chatbot.Services
{
	public class RedisSessionStore : IChatbotSessionStore
	{
		private static readonly ConcurrentDictionary<string, ChatbotSession> _fallback = new(StringComparer.OrdinalIgnoreCase);

		public Task<ChatbotSession> GetOrCreateAsync(string userId, int departmentId, ChatbotPlatform platform, string identifier)
		{
			// Phase 2: In-memory store with Redis-ready interface.
			// When ChatbotConfig.UseRedisSessionStore is true and Redis is configured,
			// this will use StackExchange.Redis instead.

			var key = $"{userId}:{departmentId}:{platform}:{identifier}";
			var existing = _fallback.Values.FirstOrDefault(s =>
				s.UserId == userId &&
				s.DepartmentId == departmentId &&
				s.Platform == platform);

			if (existing != null && !existing.IsExpired())
			{
				existing.LastActivity = DateTime.UtcNow;
				return Task.FromResult(existing);
			}

			if (existing != null)
				_fallback.TryRemove(existing.SessionId, out _);

			var session = new ChatbotSession
			{
				SessionId = Guid.NewGuid().ToString("N"),
				UserId = userId,
				DepartmentId = departmentId,
				Platform = platform,
				State = ChatbotDialogState.Idle,
				CreatedAt = DateTime.UtcNow,
				LastActivity = DateTime.UtcNow
			};

			_fallback[session.SessionId] = session;
			return Task.FromResult(session);
		}

		public Task<ChatbotSession> GetAsync(string sessionId)
		{
			if (string.IsNullOrWhiteSpace(sessionId))
				return Task.FromResult<ChatbotSession>(null);

			_fallback.TryGetValue(sessionId, out var session);
			if (session != null && session.IsExpired())
			{
				_fallback.TryRemove(sessionId, out _);
				return Task.FromResult<ChatbotSession>(null);
			}

			return Task.FromResult(session);
		}

		public Task SaveAsync(ChatbotSession session)
		{
			if (session != null && !string.IsNullOrWhiteSpace(session.SessionId))
			{
				session.LastActivity = DateTime.UtcNow;
				_fallback[session.SessionId] = session;
			}
			return Task.CompletedTask;
		}

		public Task DeleteAsync(string sessionId)
		{
			if (!string.IsNullOrWhiteSpace(sessionId))
				_fallback.TryRemove(sessionId, out _);
			return Task.CompletedTask;
		}

		public Task PruneExpiredAsync(DateTime? cutoff = null)
		{
			var effectiveCutoff = cutoff ?? DateTime.UtcNow.AddMinutes(-ChatbotConfig.DefaultSessionTimeoutMinutes);
			var expired = _fallback.Values
				.Where(s => s.LastActivity < effectiveCutoff)
				.Select(s => s.SessionId)
				.ToList();

			foreach (var id in expired)
				_fallback.TryRemove(id, out _);

			return Task.CompletedTask;
		}
	}
}
