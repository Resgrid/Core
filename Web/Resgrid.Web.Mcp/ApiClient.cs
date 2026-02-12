using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

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

		public async Task<AuthenticationResult> AuthenticateAsync(
			string username,
			string password,
			CancellationToken cancellationToken = default)
		{
			try
			{
				var client = _httpClientFactory.CreateClient("ResgridApi");

				var formContent = new FormUrlEncodedContent(new[]
				{
					new KeyValuePair<string, string>("grant_type", "password"),
					new KeyValuePair<string, string>("username", username),
					new KeyValuePair<string, string>("password", password),
					new KeyValuePair<string, string>("scope", "openid profile email")
				});

				var response = await client.PostAsync("/api/v4/connect/token", formContent, cancellationToken);

				if (response.IsSuccessStatusCode)
				{
					var content = await response.Content.ReadAsStringAsync(cancellationToken);
					var tokenResponse = JsonConvert.DeserializeObject<TokenResponse>(content);

					if (tokenResponse is null)
					{
						_logger.LogError("Failed to deserialize token response. Content: {Content}", content);
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
						ExpiresIn = tokenResponse.ExpiresIn
					};
				}
				else
				{
					var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
					_logger.LogWarning("Authentication failed: {StatusCode} - {Error}", response.StatusCode, errorContent);

					return new AuthenticationResult
					{
						IsSuccess = false,
						ErrorMessage = $"Authentication failed: {response.StatusCode}"
					};
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error during authentication");
				return new AuthenticationResult
				{
					IsSuccess = false,
					ErrorMessage = ex.Message
				};
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
				response.EnsureSuccessStatusCode();

				var content = await response.Content.ReadAsStringAsync(cancellationToken);
				var result = JsonConvert.DeserializeObject<TResponse>(content);

				if (result is null)
				{
					_logger.LogError("Failed to deserialize response from {Endpoint}. Content: {Content}", endpoint, content);
					throw new InvalidOperationException($"Failed to deserialize response from {endpoint}");
				}

				return result;
			}
			catch (Exception ex)
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
				response.EnsureSuccessStatusCode();

				var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
				var result = JsonConvert.DeserializeObject<TResponse>(responseContent);

				if (result is null)
				{
					_logger.LogError("Failed to deserialize response from {Endpoint}. Content: {Content}", endpoint, responseContent);
					throw new InvalidOperationException($"Failed to deserialize response from {endpoint}");
				}

				return result;
			}
			catch (Exception ex)
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
				response.EnsureSuccessStatusCode();

				var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
				var result = JsonConvert.DeserializeObject<TResponse>(responseContent);

				if (result is null)
				{
					_logger.LogError("Failed to deserialize response from {Endpoint}. Content: {Content}", endpoint, responseContent);
					throw new InvalidOperationException($"Failed to deserialize response from {endpoint}");
				}

				return result;
			}
			catch (Exception ex)
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
				return response.IsSuccessStatusCode;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error making DELETE request to {Endpoint}", endpoint);
				throw;
			}
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
		}
	}
}
