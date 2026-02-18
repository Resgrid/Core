﻿﻿﻿using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Resgrid.Web.Mcp.ModelContextProtocol;
using Newtonsoft.Json;

namespace Resgrid.Web.Mcp.Tools
{
	/// <summary>
	/// Provides authentication tools for MCP clients
	/// </summary>
	public sealed class AuthenticationToolProvider
	{
		private readonly IApiClient _apiClient;
		private readonly ILogger<AuthenticationToolProvider> _logger;
		private const string TOOL_NAME = "authenticate";

		public AuthenticationToolProvider(IApiClient apiClient, ILogger<AuthenticationToolProvider> logger)
		{
			_apiClient = apiClient;
			_logger = logger;
		}

		public void RegisterTools(McpServer server)
		{
			server.AddTool(
				TOOL_NAME,
				"Authenticates a user with the Resgrid CAD system and returns an access token for subsequent operations",
				new Dictionary<string, object>
				{
					["type"] = "object",
					["properties"] = new Dictionary<string, object>
					{
						["username"] = new Dictionary<string, object>
						{
							["type"] = "string",
							["description"] = "The username or email address of the user"
						},
						["password"] = new Dictionary<string, object>
						{
							["type"] = "string",
							["description"] = "The user's password"
						}
					},
					["required"] = new[] { "username", "password" }
				},
				async (arguments) =>
				{
					try
					{
						var args = JsonConvert.DeserializeObject<AuthenticateArgs>(arguments.ToString());

						if (string.IsNullOrWhiteSpace(args?.Username) || string.IsNullOrWhiteSpace(args?.Password))
						{
							return new
							{
								success = false,
								error = "Username and password are required"
							};
						}

						var userIdentifier = ComputeUserIdentifier(args.Username);
						_logger.LogInformation("Authentication attempt for user identifier: {UserIdentifier}", userIdentifier);

						var result = await _apiClient.AuthenticateAsync(args.Username, args.Password);

						if (result.IsSuccess)
						{
							_logger.LogInformation("Authentication successful for user identifier: {UserIdentifier}", userIdentifier);
							return new
							{
								success = true,
								accessToken = result.AccessToken,
								tokenType = result.TokenType,
								expiresIn = result.ExpiresIn,
								message = "Authentication successful. Use this access token in subsequent API calls."
							};
						}
						else
						{
							_logger.LogWarning("Authentication failed for user identifier: {UserIdentifier}", userIdentifier);
							return new
							{
								success = false,
								error = result.ErrorMessage ?? "Authentication failed"
							};
						}
					}
					catch (Exception ex)
					{
						_logger.LogError(ex, "Error in authentication tool");
						return new
						{
							success = false,
							error = "Authentication failed. Please check your credentials and try again."
						};
					}
				}
			);
		}

		public IEnumerable<string> GetToolNames()
		{
			yield return TOOL_NAME;
		}

		/// <summary>
		/// Computes a deterministic, non-reversible hash of the username for logging without exposing PII.
		/// Uses HMAC-SHA256 with a fixed key and truncates to 12 characters for readability.
		/// </summary>
		/// <param name="username">The username to hash</param>
		/// <returns>A truncated hash that can be used for correlation in logs</returns>
		private static string ComputeUserIdentifier(string username)
		{
			if (string.IsNullOrWhiteSpace(username))
			{
				return "unknown";
			}

			// Fixed key for deterministic hashing - this allows correlation across logs
			// while preventing reversibility
			var key = Encoding.UTF8.GetBytes("Resgrid-MCP-Auth-Log-Salt-2026");
			var data = Encoding.UTF8.GetBytes(username.ToLowerInvariant());

			using (var hmac = new HMACSHA256(key))
			{
				var hash = hmac.ComputeHash(data);
				var hashString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
				// Truncate to 12 characters for readability while maintaining uniqueness
				return hashString.Substring(0, 12);
			}
		}

		private sealed class AuthenticateArgs
		{
			[JsonProperty("username")]
			public string Username { get; set; }

			[JsonProperty("password")]
			public string Password { get; set; }
		}
	}
}



