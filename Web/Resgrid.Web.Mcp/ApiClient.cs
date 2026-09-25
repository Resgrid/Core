﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Resgrid.Web.Mcp.ModelContextProtocol;

namespace Resgrid.Web.Mcp
{
	/// <summary>
	/// Client for making authenticated requests to the Resgrid API
	/// </summary>
	public sealed class ApiClient : IApiClient
	{
		private readonly IHttpClientFactory _httpClientFactory;
		private readonly ILogger<ApiClient> _logger;

		public ApiClient(IHttpClientFactory httpClientFactory, ILogger<ApiClient> logger)
		{
			_httpClientFactory = httpClientFactory;
			_logger = logger;
		}

		public Task<AuthenticationResult> AuthenticateAsync(
			string username,
			string password,
			CancellationToken cancellationToken = default)
		{
			return RequestTokenAsync(new[]
			{
				new KeyValuePair<string, string>("grant_type", "password"),
				new KeyValuePair<string, string>("username", username),
				new KeyValuePair<string, string>("password", password),
				// offline_access is what makes the API issue a refresh token alongside the access token.
				new KeyValuePair<string, string>("scope", "openid profile email offline_access")
			}, "Authentication", cancellationToken);
		}

		public Task<AuthenticationResult> RefreshTokenAsync(
			string refreshToken,
			CancellationToken cancellationToken = default)
		{
			return RequestTokenAsync(new[]
			{
				new KeyValuePair<string, string>("grant_type", "refresh_token"),
				new KeyValuePair<string, string>("refresh_token", refreshToken)
			}, "Token refresh", cancellationToken);
		}

		private async Task<AuthenticationResult> RequestTokenAsync(
			IEnumerable<KeyValuePair<string, string>> form,
			string operation,
			CancellationToken cancellationToken)
		{
			try
			{
				var client = _httpClientFactory.CreateClient("ResgridApi");

				var response = await client.PostAsync(V4Routes.Post.Token, new FormUrlEncodedContent(form), cancellationToken);
				var content = await response.Content.ReadAsStringAsync(cancellationToken);

				if (response.IsSuccessStatusCode)
				{
					var tokenResponse = JsonConvert.DeserializeObject<TokenResponse>(content);

					if (tokenResponse is null)
					{
						_logger.LogError("Failed to deserialize token response. Response contained {ContentLength} characters but could not be parsed.", content.Length);
						return new AuthenticationResult
						{
							IsSuccess = false,
							ErrorMessage = "Invalid response format from authentication server"
						};
					}

					return new AuthenticationResult
					{
						IsSuccess = true,
						AccessToken = tokenResponse.AccessToken,
						TokenType = tokenResponse.TokenType,
						ExpiresIn = tokenResponse.ExpiresIn,
						RefreshToken = tokenResponse.RefreshToken
					};
				}

				_logger.LogWarning("{Operation} failed: {StatusCode} - {Error}", operation, response.StatusCode, content);

				// The token endpoint explains a refused grant in error_description (for example "The refresh token is no
				// longer valid."), which tells the caller whether to retry or sign in again.
				return new AuthenticationResult
				{
					IsSuccess = false,
					ErrorMessage = TryReadErrorDescription(content) ?? $"{operation} failed: {response.StatusCode}"
				};
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error during {Operation}", operation);
				return new AuthenticationResult
				{
					IsSuccess = false,
					ErrorMessage = $"An error occurred during {operation.ToLowerInvariant()}"
				};
			}
		}

		private static string TryReadErrorDescription(string content)
		{
			try
			{
				var error = JsonConvert.DeserializeObject<TokenErrorResponse>(content);
				return string.IsNullOrWhiteSpace(error?.ErrorDescription) ? null : error.ErrorDescription;
			}
			catch (JsonException)
			{
				return null;
			}
		}

		public async Task<TResponse> GetAsync<TResponse>(
			string endpoint,
			string accessToken,
			CancellationToken cancellationToken = default)
		{
			var client = CreateAuthenticatedClient(accessToken);

			try
			{
				var response = await client.GetAsync(endpoint, cancellationToken);
				ThrowIfNotAuthorized(response, endpoint);
				response.EnsureSuccessStatusCode();

				var content = await response.Content.ReadAsStringAsync(cancellationToken);
				var result = JsonConvert.DeserializeObject<TResponse>(content);

				if (result is null)
				{
					_logger.LogError("Failed to deserialize response from {Endpoint}. StatusCode: {StatusCode}", endpoint, response.StatusCode);
					throw new InvalidOperationException($"Failed to deserialize response from {endpoint}. StatusCode: {response.StatusCode}");
				}

				return result;
			}
			catch (Exception ex) when (ex is not McpToolErrorException)
			{
				_logger.LogError(ex, "Error making GET request to {Endpoint}", endpoint);
				throw;
			}
		}

		public async Task<TResponse> PostAsync<TRequest, TResponse>(
			string endpoint,
			TRequest request,
			string accessToken,
			CancellationToken cancellationToken = default)
		{
			var client = CreateAuthenticatedClient(accessToken);

			try
			{
				var json = JsonConvert.SerializeObject(request);
				var content = new StringContent(json, Encoding.UTF8, "application/json");

				var response = await client.PostAsync(endpoint, content, cancellationToken);
				ThrowIfNotAuthorized(response, endpoint);
				response.EnsureSuccessStatusCode();

				var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
				var result = JsonConvert.DeserializeObject<TResponse>(responseContent);

				if (result is null)
				{
					_logger.LogError("Failed to deserialize response from {Endpoint}. StatusCode: {StatusCode}", endpoint, response.StatusCode);
					throw new InvalidOperationException($"Failed to deserialize response from {endpoint}. StatusCode: {response.StatusCode}");
				}

				return result;
			}
			catch (Exception ex) when (ex is not McpToolErrorException)
			{
				_logger.LogError(ex, "Error making POST request to {Endpoint}", endpoint);
				throw;
			}
		}

		public async Task<TResponse> PutAsync<TRequest, TResponse>(
			string endpoint,
			TRequest request,
			string accessToken,
			CancellationToken cancellationToken = default)
		{
			var client = CreateAuthenticatedClient(accessToken);

			try
			{
				var json = JsonConvert.SerializeObject(request);
				var content = new StringContent(json, Encoding.UTF8, "application/json");

				var response = await client.PutAsync(endpoint, content, cancellationToken);
				ThrowIfNotAuthorized(response, endpoint);
				response.EnsureSuccessStatusCode();

				var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
				var result = JsonConvert.DeserializeObject<TResponse>(responseContent);

				if (result is null)
				{
					_logger.LogError("Failed to deserialize response from {Endpoint}. StatusCode: {StatusCode}", endpoint, response.StatusCode);
					throw new InvalidOperationException($"Failed to deserialize response from {endpoint}. StatusCode: {response.StatusCode}");
				}

				return result;
			}
			catch (Exception ex) when (ex is not McpToolErrorException)
			{
				_logger.LogError(ex, "Error making PUT request to {Endpoint}", endpoint);
				throw;
			}
		}

		public async Task<bool> DeleteAsync(
			string endpoint,
			string accessToken,
			CancellationToken cancellationToken = default)
		{
			var client = CreateAuthenticatedClient(accessToken);

			try
			{
				var response = await client.DeleteAsync(endpoint, cancellationToken);
				ThrowIfNotAuthorized(response, endpoint);
				return response.IsSuccessStatusCode;
			}
			catch (Exception ex) when (ex is not McpToolErrorException)
			{
				_logger.LogError(ex, "Error making DELETE request to {Endpoint}", endpoint);
				throw;
			}
		}

		/// <summary>
		/// Turns an authorization failure into an error the MCP client can act on. The API marks a rejected access token
		/// (expired, or its session revoked) with a Bearer invalid_token challenge. Any other 401, which v4 actions return
		/// when the user may not touch a record, and any 403 mean the user is signed in but not permitted: a new token
		/// will not help.
		/// </summary>
		private void ThrowIfNotAuthorized(HttpResponseMessage response, string endpoint)
		{
			if (response.StatusCode == HttpStatusCode.Unauthorized && IsInvalidTokenChallenge(response))
			{
				_logger.LogInformation("API rejected the access token for {Endpoint}", endpoint);
				throw new McpToolErrorException(McpToolErrorException.AccessTokenExpired,
					"The access token has expired or is no longer valid. Call refresh_access_token with your refresh token, " +
					"then call this tool again with the new access token. If the refresh fails, call authenticate.");
			}

			if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
			{
				_logger.LogInformation("API refused {Endpoint} with {StatusCode}", endpoint, response.StatusCode);
				throw new McpToolErrorException(McpToolErrorException.Forbidden,
					"The signed-in user is not permitted to do this. A new access token will not change that.");
			}
		}

		private static bool IsInvalidTokenChallenge(HttpResponseMessage response)
		{
			return response.Headers.TryGetValues("WWW-Authenticate", out var challenges)
				&& challenges.Any(x => x.StartsWith("Bearer", StringComparison.OrdinalIgnoreCase)
					&& x.Contains("error=\"invalid_token\"", StringComparison.OrdinalIgnoreCase));
		}

		private HttpClient CreateAuthenticatedClient(string accessToken)
		{
			var client = _httpClientFactory.CreateClient("ResgridApi");
			client.DefaultRequestHeaders.Authorization =
				new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
			return client;
		}

		private sealed class TokenResponse
		{
			[JsonProperty("access_token")]
			public string AccessToken { get; set; }

			[JsonProperty("token_type")]
			public string TokenType { get; set; }

			[JsonProperty("expires_in")]
			public int ExpiresIn { get; set; }

			[JsonProperty("refresh_token")]
			public string RefreshToken { get; set; }
		}

		private sealed class TokenErrorResponse
		{
			[JsonProperty("error")]
			public string Error { get; set; }

			[JsonProperty("error_description")]
			public string ErrorDescription { get; set; }
		}
	}
}
