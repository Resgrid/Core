﻿﻿﻿using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Resgrid.Web.Mcp.Infrastructure
{
	/// <summary>
	/// Rate limiting service for MCP server
	/// </summary>
	public interface IRateLimiter
	{
		Task<bool> IsAllowedAsync(string clientId, string operation);
		void Reset(string clientId);
	}

	public sealed class RateLimiter : IRateLimiter, IDisposable
	{
		private readonly ILogger<RateLimiter> _logger;
		private readonly ConcurrentDictionary<string, RequestCounter> _counters;
		private readonly Timer _cleanupTimer;
		private bool _disposed;

		// Rate limits: 100 requests per minute per client
		private const int MaxRequestsPerMinute = 100;
		private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

		public RateLimiter(ILogger<RateLimiter> logger)
		{
			_logger = logger;
			_counters = new ConcurrentDictionary<string, RequestCounter>();
			_cleanupTimer = new Timer(CleanupExpired, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
		}

		public Task<bool> IsAllowedAsync(string clientId, string operation)
		{
			var key = $"{clientId}:{operation}";
			var counter = _counters.GetOrAdd(key, _ => new RequestCounter());

			var now = DateTime.UtcNow;
			counter.CleanupOldRequests(now, Window);

			if (counter.Count >= MaxRequestsPerMinute)
			{
				_logger.LogWarning("Rate limit exceeded for client {ClientId}, operation {Operation}", clientId, operation);
				return Task.FromResult(false);
			}

			counter.AddRequest(now);
			return Task.FromResult(true);
		}

		public void Reset(string clientId)
		{
			var prefix = clientId + ":";
			var keysToRemove = new System.Collections.Generic.List<string>();

			foreach (var key in _counters.Keys)
			{
				if (key.StartsWith(prefix))
				{
					keysToRemove.Add(key);
				}
			}

			foreach (var key in keysToRemove)
			{
				_counters.TryRemove(key, out _);
			}

			_logger.LogDebug("Reset rate limit for client: {ClientId}, removed {Count} operation(s)", clientId, keysToRemove.Count);
		}

		private void CleanupExpired(object state)
		{
			var now = DateTime.UtcNow;
			var keysToRemove = new System.Collections.Generic.List<string>();

			foreach (var kvp in _counters)
			{
				// First clean up old requests within the counter
				kvp.Value.CleanupOldRequests(now, Window);

				// Then check if the counter is empty (no recent requests)
				if (kvp.Value.Count == 0)
				{
					keysToRemove.Add(kvp.Key);
				}
			}

			foreach (var key in keysToRemove)
			{
				_counters.TryRemove(key, out _);
			}

			if (keysToRemove.Count > 0)
			{
				_logger.LogDebug("Cleaned up {Count} expired rate limit entries", keysToRemove.Count);
			}
		}

		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}

			_cleanupTimer?.Dispose();
			_disposed = true;
		}

		private sealed class RequestCounter
		{
			private readonly object _lock = new object();
			private System.Collections.Generic.Queue<DateTime> _requests = new System.Collections.Generic.Queue<DateTime>();

			public int Count
			{
				get
				{
					lock (_lock)
					{
						return _requests.Count;
					}
				}
			}


			public void AddRequest(DateTime timestamp)
			{
				lock (_lock)
				{
					_requests.Enqueue(timestamp);
				}
			}

			public void CleanupOldRequests(DateTime now, TimeSpan window)
			{
				lock (_lock)
				{
					var cutoff = now.Subtract(window);
					while (_requests.Count > 0 && _requests.Peek() < cutoff)
					{
						_requests.Dequeue();
					}
				}
			}
		}
	}
}

