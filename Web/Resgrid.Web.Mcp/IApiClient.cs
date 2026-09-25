using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Web.Mcp
{
	/// <summary>
	/// Interface for API client that handles authenticated requests to Resgrid API
	/// </summary>
	public interface IApiClient
	{
		/// <summary>
		/// Authenticates a user and returns an access token and a refresh token
		/// </summary>
		Task<AuthenticationResult> AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default);

		/// <summary>
		/// Exchanges a refresh token for a new access token and a new refresh token. The refresh token presented is
		/// single use: the API rejects it once its short reuse window has passed.
		/// </summary>
		Task<AuthenticationResult> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);

		/// <summary>
		/// Makes an authenticated GET request to the API
		/// </summary>
		Task<TResponse> GetAsync<TResponse>(string endpoint, string accessToken, CancellationToken cancellationToken = default);

		/// <summary>
		/// Makes an authenticated POST request to the API
		/// </summary>
		Task<TResponse> PostAsync<TRequest, TResponse>(string endpoint, TRequest request, string accessToken, CancellationToken cancellationToken = default);

		/// <summary>
		/// Makes an authenticated PUT request to the API
		/// </summary>
		Task<TResponse> PutAsync<TRequest, TResponse>(string endpoint, TRequest request, string accessToken, CancellationToken cancellationToken = default);

		/// <summary>
		/// Makes an authenticated DELETE request to the API
		/// </summary>
		Task<bool> DeleteAsync(string endpoint, string accessToken, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// Result of authentication attempt
	/// </summary>
	public sealed record AuthenticationResult
	{
		public bool IsSuccess { get; init; }
		public string AccessToken { get; init; }
		public string TokenType { get; init; }
		public int ExpiresIn { get; init; }
		public string RefreshToken { get; init; }
		public string ErrorMessage { get; init; }
	}
}

