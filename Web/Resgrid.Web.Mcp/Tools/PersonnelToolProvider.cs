using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Resgrid.Web.Mcp.ModelContextProtocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
							V4Routes.Get.AllPersonnelInfos,
							args.AccessToken
						);

						return new
						{
							success = true,
							data = result
						};
					}
					catch (Exception ex) when (ex is not McpToolErrorException)
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

						var result = await _apiClient.GetAsync<JObject>(
							V4Routes.Get.AllPersonnelInfos,
							args.AccessToken
						);

						return new
						{
							success = true,
							data = V4ResponseReader.Project(V4ResponseReader.GetDataArray(result), V4ResponseReader.PersonnelStatusFields)
						};
					}
					catch (Exception ex) when (ex is not McpToolErrorException)
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
					["statusType"] = new SchemaBuilder.PropertySchema { Type = "integer", Description = "Status id. Defaults: 0=Available, 1=Not Responding, 2=Responding, 3=On Scene, 4=Available Station, 5=Responding To Station, 6=Responding To Scene, 7=On Unit. Departments with custom statuses use their own status ids." },
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

						if (args.StatusType < 0)
						{
							return CreateErrorResponse("StatusType must be a status id of 0 or greater");
						}

						_logger.LogInformation("Setting status for personnel {UserId}", args.UserId);

						// SavePersonStatus takes the status id as a string Type. Only department admins may set another member's status.
						var statusData = new
						{
							UserId = args.UserId,
							Type = args.StatusType.ToString(),
							Note = args.Note
						};

						var result = await _apiClient.PostAsync<object, object>(
							V4Routes.Post.SavePersonStatus,
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
					catch (Exception ex) when (ex is not McpToolErrorException)
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
				"Retrieves the current GPS locations of personnel in the department. Only people with a current location that the caller may view are returned.",
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

						// v4 has no bulk latest-location endpoint for personnel; the map markers carry them, with the
						// department's location TTL and the location-view permissions already applied.
						var result = await _apiClient.GetAsync<JObject>(
							V4Routes.Get.MapDataAndMarkers,
							args.AccessToken
						);

						return new
						{
							success = true,
							data = V4ResponseReader.GetMapMarkers(result, V4ResponseReader.PersonnelMarkerType, "UserId")
						};
					}
				catch (Exception ex) when (ex is not McpToolErrorException)
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







