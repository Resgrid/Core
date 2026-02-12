﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
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

						_logger.LogInformation("Authentication attempt for user: {Username}", args.Username);

						var result = await _apiClient.AuthenticateAsync(args.Username, args.Password);

						if (result.IsSuccess)
						{
							_logger.LogInformation("Authentication successful for user: {Username}", args.Username);
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
							_logger.LogWarning("Authentication failed for user: {Username}", args.Username);
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

		private sealed class AuthenticateArgs
		{
			[JsonProperty("username")]
			public string Username { get; set; }

			[JsonProperty("password")]
			public string Password { get; set; }
		}
	}
}



