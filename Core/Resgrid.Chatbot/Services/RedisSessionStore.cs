using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Config;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;
using Resgrid.Framework;
using Resgrid.Model.Providers;

namespace Resgrid.Chatbot.Services
{
	/// <summary>
	/// Session store backed by Redis (via <see cref="ICacheProvider"/>) when
	/// <c>ChatbotConfig.UseRedisSessionStore</c> is enabled and the cache is connected; otherwise an
	/// in-memory fallback for single-instance / unconfigured deployments. Redis-backed sessions are
	/// shared across instances and expire via key TTL.
	/// </summary>
	public class RedisSessionStore : IChatbotSessionStore
	{
		private const string SessionKeyPrefix = "Chatbot:Session:";
		private const string ActiveKeyPrefix = "Chatbot:ActiveSession:";

		// In-memory fallback (single-instance / Redis unavailable).
		private static readonly ConcurrentDictionary<string, ChatbotSession> _fallback = new(StringComparer.OrdinalIgnoreCase);

		private readonly ICacheProvider _cacheProvider;

		public RedisSessionStore(ICacheProvider cacheProvider)
		{
			_cacheProvider = cacheProvider;
		}

		private static TimeSpan GetTtl(ChatbotSession session)
			=> TimeSpan.FromMinutes(session?.TtlMinutes > 0
				? session.TtlMinutes
				: (ChatbotConfig.DefaultSessionTimeoutMinutes > 0 ? ChatbotConfig.DefaultSessionTimeoutMinutes : 30));

		private bool UseRedis()
		{
			if (!ChatbotConfig.UseRedisSessionStore || _cacheProvider == null)
				return false;

			try
			{
				return _cacheProvider.IsConnected();
			}
			catch
			{
				return false;
			}
		}

		public async Task<ChatbotSession> GetOrCreateAsync(string userId, int departmentId, ChatbotPlatform platform,
			string identifier, int ttlMinutes = 0)
		{
			ttlMinutes = ttlMinutes > 0
				? ttlMinutes
				: (ChatbotConfig.DefaultSessionTimeoutMinutes > 0 ? ChatbotConfig.DefaultSessionTimeoutMinutes : 30);

			if (UseRedis())
			{
				try
				{
					var activeKey = ActiveKey(userId, departmentId, platform);
					var existingId = await _cacheProvider.GetStringAsync(activeKey);
					if (!string.IsNullOrWhiteSpace(existingId))
					{
						var existing = await GetFromRedisAsync(existingId);
						if (existing != null)
							existing.TtlMinutes = ttlMinutes;
						if (existing != null && !existing.IsExpired())
						{
							existing.LastActivity = DateTime.UtcNow;
							await SaveToRedisAsync(existing);
							return existing;
						}
					}

					var created = CreateNewSession(userId, departmentId, platform, ttlMinutes);
					await SaveToRedisAsync(created);
					return created;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
					// fall through to in-memory
				}
			}

			return GetOrCreateInMemory(userId, departmentId, platform, ttlMinutes);
		}

		public async Task<ChatbotSession> GetAsync(string sessionId)
		{
			if (string.IsNullOrWhiteSpace(sessionId))
				return null;

			if (UseRedis())
			{
				try
				{
					var session = await GetFromRedisAsync(sessionId);
					if (session != null && session.IsExpired())
						return null;
					return session;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}

			_fallback.TryGetValue(sessionId, out var fallbackSession);
			if (fallbackSession != null && fallbackSession.IsExpired())
			{
				_fallback.TryRemove(sessionId, out _);
				return null;
			}
			return fallbackSession;
		}

		public async Task SaveAsync(ChatbotSession session)
		{
			if (session == null || string.IsNullOrWhiteSpace(session.SessionId))
				return;

			session.LastActivity = DateTime.UtcNow;

			if (UseRedis())
			{
				try
				{
					await SaveToRedisAsync(session);
					return;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}

			_fallback[session.SessionId] = session;
		}

		public async Task DeleteAsync(string sessionId)
		{
			if (string.IsNullOrWhiteSpace(sessionId))
				return;

			if (UseRedis())
			{
				try
				{
					var session = await GetFromRedisAsync(sessionId);
					await _cacheProvider.RemoveAsync(SessionKey(sessionId));
					if (session != null)
						await _cacheProvider.RemoveAsync(ActiveKey(session.UserId, session.DepartmentId, session.Platform));
					return;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}

			_fallback.TryRemove(sessionId, out _);
		}

		public Task PruneExpiredAsync(DateTime? cutoff = null)
		{
			// Redis-backed sessions expire automatically via key TTL; only the in-memory fallback
			// needs explicit pruning.
			var expired = _fallback.Values
				.Where(s => cutoff.HasValue ? s.LastActivity < cutoff.Value : s.IsExpired())
				.Select(s => s.SessionId)
				.ToList();

			foreach (var id in expired)
				_fallback.TryRemove(id, out _);

			return Task.CompletedTask;
		}

		private ChatbotSession GetOrCreateInMemory(string userId, int departmentId, ChatbotPlatform platform,
			int ttlMinutes)
		{
			var existing = _fallback.Values.FirstOrDefault(s =>
				s.UserId == userId &&
				s.DepartmentId == departmentId &&
				s.Platform == platform);

			if (existing != null)
				existing.TtlMinutes = ttlMinutes;

			if (existing != null && !existing.IsExpired())
			{
				existing.LastActivity = DateTime.UtcNow;
				return existing;
			}

			if (existing != null)
				_fallback.TryRemove(existing.SessionId, out _);

			var session = CreateNewSession(userId, departmentId, platform, ttlMinutes);
			_fallback[session.SessionId] = session;
			return session;
		}

		private async Task SaveToRedisAsync(ChatbotSession session)
		{
			var json = JsonConvert.SerializeObject(session);
			var ttl = GetTtl(session);
			await _cacheProvider.SetStringAsync(SessionKey(session.SessionId), json, ttl);
			await _cacheProvider.SetStringAsync(ActiveKey(session.UserId, session.DepartmentId, session.Platform), session.SessionId, ttl);
		}

		private async Task<ChatbotSession> GetFromRedisAsync(string sessionId)
		{
			var json = await _cacheProvider.GetStringAsync(SessionKey(sessionId));
			if (string.IsNullOrWhiteSpace(json))
				return null;

			try
			{
				return JsonConvert.DeserializeObject<ChatbotSession>(json);
			}
			catch (JsonException)
			{
				return null;
			}
		}

		private static ChatbotSession CreateNewSession(string userId, int departmentId, ChatbotPlatform platform,
			int ttlMinutes)
		{
			return new ChatbotSession
			{
				SessionId = Guid.NewGuid().ToString("N"),
				UserId = userId,
				DepartmentId = departmentId,
				Platform = platform,
				State = ChatbotDialogState.Idle,
				CreatedAt = DateTime.UtcNow,
				LastActivity = DateTime.UtcNow,
				TtlMinutes = ttlMinutes
			};
		}

		private static string SessionKey(string sessionId) => $"{SessionKeyPrefix}{sessionId}";

		// The active-session pointer is keyed by department as well as user/platform so a multi-department
		// user who switches their active department gets that department's session, not a stale one. This
		// mirrors the in-memory fallback, which matches on (userId, departmentId, platform).
		private static string ActiveKey(string userId, int departmentId, ChatbotPlatform platform) => $"{ActiveKeyPrefix}{userId}:{departmentId}:{(int)platform}";
	}
}
