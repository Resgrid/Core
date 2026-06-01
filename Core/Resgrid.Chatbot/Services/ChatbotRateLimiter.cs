using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Model.Providers;

namespace Resgrid.Chatbot.Services
{
	/// <summary>
	/// Fixed-window (1 minute) rate limiter. Uses Redis (via <see cref="ICacheProvider"/>) for
	/// cross-instance counting when the cache is connected — so the limit is enforced across all
	/// nodes — and falls back to an in-memory counter otherwise. Registered as a single instance.
	/// </summary>
	public class ChatbotRateLimiter : IChatbotRateLimiter
	{
		private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
		private static readonly TimeSpan RedisKeyTtl = TimeSpan.FromSeconds(120);

		private readonly ICacheProvider _cacheProvider;
		private readonly ConcurrentDictionary<string, Counter> _counters = new(StringComparer.OrdinalIgnoreCase);

		public ChatbotRateLimiter(ICacheProvider cacheProvider)
		{
			_cacheProvider = cacheProvider;
		}

		public async Task<bool> TryAcquireAsync(string userId, int departmentId, int perUserPerMinute, int perDepartmentPerMinute)
		{
			if (UseRedis())
			{
				// Check the user limit first; only consume the department slot if the user is allowed.
				if (!await CheckAndConsumeRedisAsync($"u:{departmentId}:{userId}", perUserPerMinute))
					return false;

				if (!await CheckAndConsumeRedisAsync($"d:{departmentId}", perDepartmentPerMinute))
					return false;

				return true;
			}

			if (!CheckAndConsumeMemory($"u:{departmentId}:{userId}", perUserPerMinute))
				return false;

			if (!CheckAndConsumeMemory($"d:{departmentId}", perDepartmentPerMinute))
				return false;

			return true;
		}

		private bool UseRedis()
		{
			if (_cacheProvider == null)
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

		private async Task<bool> CheckAndConsumeRedisAsync(string key, int limit)
		{
			if (limit <= 0)
				return true;

			// Window is encoded into the key so each minute uses a fresh counter that expires on its own.
			var windowMinute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
			var redisKey = $"Chatbot:RateLimit:{key}:{windowMinute}";

			var count = await _cacheProvider.IncrementAsync(redisKey, RedisKeyTtl);
			if (count <= 0)
				return true; // cache hiccup — fail open rather than block legitimate traffic

			return count <= limit;
		}

		private bool CheckAndConsumeMemory(string key, int limit)
		{
			if (limit <= 0)
				return true;

			var counter = _counters.GetOrAdd(key, _ => new Counter());
			lock (counter)
			{
				var now = DateTime.UtcNow;
				if (now - counter.WindowStart >= Window)
				{
					counter.WindowStart = now;
					counter.Count = 0;
				}

				if (counter.Count >= limit)
					return false;

				counter.Count++;
				return true;
			}
		}

		private sealed class Counter
		{
			public DateTime WindowStart = DateTime.UtcNow;
			public int Count;
		}
	}
}
