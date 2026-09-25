using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Resgrid.Web.Mcp.Infrastructure
{
	/// <summary>
	/// Exchanges refresh tokens for new access tokens (the OAuth2 refresh_token grant).
	/// </summary>
	/// <remarks>
	/// The MCP server is stateless: the client holds its tokens and passes the access token to every tool. When the
	/// access token nears expiry the client calls the refresh_access_token tool with its refresh token, which lands here.
	/// </remarks>
	public interface ITokenRefreshService
	{
		Task<AuthenticationResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);
	}

	public sealed class TokenRefreshService : ITokenRefreshService
	{
		private readonly IApiClient _apiClient;
		private readonly ILogger<TokenRefreshService> _logger;
		private readonly ConcurrentDictionary<string, Lazy<Task<AuthenticationResult>>> _inFlight;

		public TokenRefreshService(IApiClient apiClient, ILogger<TokenRefreshService> logger)
		{
			_apiClient = apiClient;
			_logger = logger;
			_inFlight = new ConcurrentDictionary<string, Lazy<Task<AuthenticationResult>>>(StringComparer.Ordinal);
		}

		public Task<AuthenticationResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(refreshToken))
			{
				return Task.FromResult(new AuthenticationResult
				{
					IsSuccess = false,
					ErrorMessage = "Refresh token is required"
				});
			}

			// Refresh tokens are single use: each exchange returns a new one, and the API rejects the old one once its
			// short reuse window has passed. Callers presenting the same refresh token at the same time therefore share
			// one exchange and all receive the new pair, instead of racing to redeem the token twice.
			var exchange = _inFlight.GetOrAdd(refreshToken,
				token => new Lazy<Task<AuthenticationResult>>(() => ExchangeAsync(token)));

			// One caller giving up must not cancel the exchange the others are waiting on.
			return exchange.Value.WaitAsync(cancellationToken);
		}

		private async Task<AuthenticationResult> ExchangeAsync(string refreshToken)
		{
			try
			{
				var result = await _apiClient.RefreshTokenAsync(refreshToken, CancellationToken.None);

				if (result.IsSuccess)
					_logger.LogInformation("Access token refreshed, expires in {ExpiresIn} seconds", result.ExpiresIn);
				else
					_logger.LogWarning("Token refresh failed: {Error}", result.ErrorMessage);

				return result;
			}
			finally
			{
				// Only in-flight exchanges are shared. A later call with the same (now redeemed) token goes back to the
				// API, which decides whether it is still inside the reuse window.
				_inFlight.TryRemove(refreshToken, out _);
			}
		}
	}
}
