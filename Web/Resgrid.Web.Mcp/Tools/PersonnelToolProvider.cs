using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Newtonsoft.Json;

namespace Resgrid.Web.Mcp.Tools
{
	/// <summary>
	/// Provides MCP tools for managing personnel in the Resgrid system
	/// </summary>
	public sealed class PersonnelToolProvider
	{
		private readonly IApiClient _apiClient;
		private readonly ILogger<PersonnelToolProvider> _logger;
		private readonly List<string> _toolNames;

		public PersonnelToolProvider(IApiClient apiClient, ILogger<PersonnelToolProvider> logger)
		{
			_apiClient = apiClient;
			_logger = logger;
			_toolNames = new List<string>();
		}

		public void RegisterTools(McpServer server)
		{
			RegisterGetPersonnelTool(server);
			RegisterGetPersonnelStatusTool(server);
			RegisterSetPersonnelStatusTool(server);
			RegisterGetPersonnelLocationTool(server);
		}

		public IEnumerable<string> GetToolNames() => _toolNames;

		private void RegisterGetPersonnelTool(McpServer server)
		{
			const string toolName = "get_personnel";
			_toolNames.Add(toolName);

			var schema = SchemaBuilder.BuildObjectSchema(
				new Dictionary<string, SchemaBuilder.PropertySchema>
				{
					["accessToken"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "OAuth2 access token obtained from authentication" }
				},
				new[] { "accessToken" }
			);

			server.AddTool(
				toolName,
				"Retrieves all personnel (members) in the user's department",
				schema,
				async (arguments) =>
				{
					try
					{
						var args = JsonConvert.DeserializeObject<TokenArgs>(arguments.ToString());

						if (string.IsNullOrWhiteSpace(args?.AccessToken))
						{
							return CreateErrorResponse("Access token is required");
						}

						_logger.LogInformation("Retrieving personnel list");

						var result = await _apiClient.GetAsync<object>(
							"/api/v4/Personnel/GetAll",
							args.AccessToken
						);

						return new
						{
							success = true,
							data = result
						};
					}
					catch (Exception ex)
					{
						_logger.LogError(ex, "Error retrieving personnel");
						return CreateErrorResponse("Failed to retrieve personnel. Please try again later.");
					}
				}
			);
		}

		private void RegisterGetPersonnelStatusTool(McpServer server)
		{
			const string toolName = "get_personnel_status";
			_toolNames.Add(toolName);

			var schema = SchemaBuilder.BuildObjectSchema(
				new Dictionary<string, SchemaBuilder.PropertySchema>
				{
					["accessToken"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "OAuth2 access token obtained from authentication" }
				},
				new[] { "accessToken" }
			);

			server.AddTool(
				toolName,
				"Retrieves the current status of all personnel in the department",
				schema,
				async (arguments) =>
				{
					try
					{
						var args = JsonConvert.DeserializeObject<TokenArgs>(arguments.ToString());

						if (string.IsNullOrWhiteSpace(args?.AccessToken))
						{
							return CreateErrorResponse("Access token is required");
						}

						_logger.LogInformation("Retrieving personnel statuses");

						var result = await _apiClient.GetAsync<object>(
							"/api/v4/PersonnelStatuses/GetAllStatuses",
							args.AccessToken
						);

						return new
						{
							success = true,
							data = result
						};
					}
					catch (Exception ex)
					{
						_logger.LogError(ex, "Error retrieving personnel statuses");
						return CreateErrorResponse("Failed to retrieve personnel statuses. Please try again later.");
					}
				}
			);
		}

		private void RegisterSetPersonnelStatusTool(McpServer server)
		{
			const string toolName = "set_personnel_status";
			_toolNames.Add(toolName);

			var schema = SchemaBuilder.BuildObjectSchema(
				new Dictionary<string, SchemaBuilder.PropertySchema>
				{
					["accessToken"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "OAuth2 access token obtained from authentication" },
					["userId"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "User ID of the personnel member" },
					["statusType"] = new SchemaBuilder.PropertySchema { Type = "integer", Description = "Status type code (0=Unavailable, 1=Available, 2=Committed, 3=OnScene, 4=Responding, 5=Standing By, 6=Not Responding)" },
					["note"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "Optional note about the status change" }
				},
				new[] { "accessToken", "userId", "statusType" }
			);

			server.AddTool(
				toolName,
				"Sets the status for personnel (e.g., Available, Responding, On Scene)",
				schema,
				async (arguments) =>
				{
					try
					{
						var args = JsonConvert.DeserializeObject<SetPersonnelStatusArgs>(arguments.ToString());

						if (string.IsNullOrWhiteSpace(args?.AccessToken))
						{
							return CreateErrorResponse("Access token is required");
						}

						if (string.IsNullOrWhiteSpace(args?.UserId))
						{
							return CreateErrorResponse("User ID is required");
						}

						if (args.StatusType < 0 || args.StatusType > 6)
						{
							return CreateErrorResponse("StatusType must be between 0 and 6 (0=Unavailable, 1=Available, 2=Committed, 3=OnScene, 4=Responding, 5=Standing By, 6=Not Responding)");
						}

						_logger.LogInformation("Setting status for personnel {UserId}", args.UserId);

						var statusData = new
						{
							userId = args.UserId,
							type = args.StatusType,
							note = args.Note
						};

						var result = await _apiClient.PostAsync<object, object>(
							"/api/v4/PersonnelStatuses/SetPersonnelStatus",
							statusData,
							args.AccessToken
						);

						return new
						{
							success = true,
							data = result,
							message = "Personnel status updated successfully"
						};
					}
					catch (Exception ex)
					{
						_logger.LogError(ex, "Error setting personnel status");
						return CreateErrorResponse("Failed to set personnel status. Please try again later.");
					}
				}
			);
		}

		private void RegisterGetPersonnelLocationTool(McpServer server)
		{
			const string toolName = "get_personnel_locations";
			_toolNames.Add(toolName);

			var schema = SchemaBuilder.BuildObjectSchema(
				new Dictionary<string, SchemaBuilder.PropertySchema>
				{
					["accessToken"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "OAuth2 access token obtained from authentication" }
				},
				new[] { "accessToken" }
			);

			server.AddTool(
				toolName,
				"Retrieves the current GPS locations of all personnel in the department",
				schema,
				async (arguments) =>
				{
					try
					{
						var args = JsonConvert.DeserializeObject<TokenArgs>(arguments.ToString());

						if (string.IsNullOrWhiteSpace(args?.AccessToken))
						{
							return CreateErrorResponse("Access token is required");
						}

						_logger.LogInformation("Retrieving personnel locations");

						var result = await _apiClient.GetAsync<object>(
							"/api/v4/PersonnelLocation/GetLatestLocations",
							args.AccessToken
						);

						return new
						{
							success = true,
							data = result
						};
					}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Error retrieving personnel locations");
					return CreateErrorResponse("Failed to retrieve personnel locations. Please try again later.");
				}
				}
			);
		}

		private static object CreateErrorResponse(string errorMessage) =>
			new { success = false, error = errorMessage };

		private sealed class TokenArgs
		{
			[JsonProperty("accessToken")]
			public string AccessToken { get; set; }
		}

		private sealed class SetPersonnelStatusArgs
		{
			[JsonProperty("accessToken")]
			public string AccessToken { get; set; }

			[JsonProperty("userId")]
			public string UserId { get; set; }

			[JsonProperty("statusType")]
			public int StatusType { get; set; }

			[JsonProperty("note")]
			public string Note { get; set; }
		}
	}
}







