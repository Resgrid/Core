using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Resgrid.Web.Mcp.Infrastructure
{
	/// <summary>
	/// Token refresh service for managing OAuth2 token lifecycle
	/// </summary>
	public interface ITokenRefreshService
	{
		Task<string> GetValidTokenAsync(string userId, string refreshToken);
		void CacheToken(string userId, string accessToken, string refreshToken, int expiresIn);
		void InvalidateToken(string userId);
	}

	public sealed class TokenRefreshService : ITokenRefreshService
	{
		private readonly IApiClient _apiClient;
		private readonly ILogger<TokenRefreshService> _logger;
		private readonly ConcurrentDictionary<string, TokenCache> _tokenCache;
		private readonly Timer _cleanupTimer;

		public TokenRefreshService(IApiClient apiClient, ILogger<TokenRefreshService> logger)
		{
			_apiClient = apiClient;
			_logger = logger;
			_tokenCache = new ConcurrentDictionary<string, TokenCache>();
			_cleanupTimer = new Timer(CleanupExpired, null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
		}

		public async Task<string> GetValidTokenAsync(string userId, string refreshToken)
		{
			if (_tokenCache.TryGetValue(userId, out var cached))
			{
				if (cached.ExpiresAt > DateTime.UtcNow.AddMinutes(5))
				{
					_logger.LogDebug("Using cached token for user {UserId}", userId);
					return cached.AccessToken;
				}

				_logger.LogInformation("Token expired for user {UserId}, refreshing...", userId);
			}

			try
			{
				// Call refresh token endpoint
				var result = await RefreshTokenAsync(refreshToken);

				if (result.IsSuccess)
				{
					CacheToken(userId, result.AccessToken, result.RefreshToken, result.ExpiresIn);
					return result.AccessToken;
				}

				_logger.LogError("Failed to refresh token for user {UserId}", userId);
				return null;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error refreshing token for user {UserId}", userId);
				return null;
			}
		}

		public void CacheToken(string userId, string accessToken, string refreshToken, int expiresIn)
		{
			var cache = new TokenCache
			{
				AccessToken = accessToken,
				RefreshToken = refreshToken,
				ExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn)
			};

			_tokenCache.AddOrUpdate(userId, cache, (_, __) => cache);
			_logger.LogDebug("Cached token for user {UserId}, expires at {ExpiresAt}", userId, cache.ExpiresAt);
		}

		public void InvalidateToken(string userId)
		{
			_tokenCache.TryRemove(userId, out _);
			_logger.LogDebug("Invalidated token for user {UserId}", userId);
		}

		private async Task<RefreshTokenResult> RefreshTokenAsync(string refreshToken)
		{
			try
			{
				// This would call the actual refresh token endpoint
				// For now, returning a placeholder
				// In real implementation, call: POST /api/v4/connect/token with grant_type=refresh_token

				_logger.LogWarning("Token refresh not fully implemented - requires refresh_token grant type support");

				return new RefreshTokenResult
				{
					IsSuccess = false
				};
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error during token refresh");
				return new RefreshTokenResult { IsSuccess = false };
			}
		}

		private void CleanupExpired(object state)
		{
			var keysToRemove = new System.Collections.Generic.List<string>();
			var now = DateTime.UtcNow;

			foreach (var kvp in _tokenCache)
			{
				if (kvp.Value.ExpiresAt < now)
				{
					keysToRemove.Add(kvp.Key);
				}
			}

			foreach (var key in keysToRemove)
			{
				_tokenCache.TryRemove(key, out _);
			}

			if (keysToRemove.Count > 0)
			{
				_logger.LogDebug("Cleaned up {Count} expired tokens", keysToRemove.Count);
			}
		}

		private sealed class TokenCache
		{
			public string AccessToken { get; set; }
			public string RefreshToken { get; set; }
			public DateTime ExpiresAt { get; set; }
		}

		private sealed class RefreshTokenResult
		{
			public bool IsSuccess { get; set; }
			public string AccessToken { get; set; }
			public string RefreshToken { get; set; }
			public int ExpiresIn { get; set; }
		}
	}
}

