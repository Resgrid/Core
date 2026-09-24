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
	/// Provides MCP tools for managing units (vehicles/apparatus) in the Resgrid system
	/// </summary>
	public sealed class UnitsToolProvider
	{
		private readonly IApiClient _apiClient;
		private readonly ILogger<UnitsToolProvider> _logger;
		private readonly List<string> _toolNames;

		public UnitsToolProvider(IApiClient apiClient, ILogger<UnitsToolProvider> logger)
		{
			_apiClient = apiClient;
			_logger = logger;
			_toolNames = new List<string>();
		}

		public void RegisterTools(McpServer server)
		{
			RegisterGetUnitsTool(server);
			RegisterGetUnitStatusTool(server);
			RegisterSetUnitStatusTool(server);
			RegisterGetUnitLocationTool(server);
		}

		public IEnumerable<string> GetToolNames() => _toolNames;

		private void RegisterGetUnitsTool(McpServer server)
		{
			const string toolName = "get_units";
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
				"Retrieves all units (vehicles/apparatus) in the user's department",
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

						_logger.LogInformation("Retrieving units list");

						var result = await _apiClient.GetAsync<object>(
							V4Routes.Get.AllUnitsInfos,
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
						_logger.LogError(ex, "Error retrieving units");
						return CreateErrorResponse("Failed to retrieve units. Please try again later.");
					}
				}
			);
		}

		private void RegisterGetUnitStatusTool(McpServer server)
		{
			const string toolName = "get_unit_statuses";
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
				"Retrieves the current status of all units in the department",
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

						_logger.LogInformation("Retrieving unit statuses");

						var result = await _apiClient.GetAsync<object>(
							V4Routes.Get.AllUnitStatuses,
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
						_logger.LogError(ex, "Error retrieving unit statuses");
						return CreateErrorResponse("Failed to retrieve unit statuses. Please try again later.");
					}
				}
			);
		}

		private void RegisterSetUnitStatusTool(McpServer server)
		{
			const string toolName = "set_unit_status";
			_toolNames.Add(toolName);

			var schema = SchemaBuilder.BuildObjectSchema(
				new Dictionary<string, SchemaBuilder.PropertySchema>
				{
					["accessToken"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "OAuth2 access token obtained from authentication" },
					["unitId"] = new SchemaBuilder.PropertySchema { Type = "integer", Description = "The unique identifier of the unit" },
					["statusType"] = new SchemaBuilder.PropertySchema { Type = "integer", Description = "Status type code (1=Available, 2=Delayed, 3=Unavailable, 4=Committed, 5=Out Of Service, 6=Responding, 7=On Scene, 8=Staging, 9=Returning, 10=Cancelled, 11=Released, 12=Manual, 13=Enroute)" },
					["note"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "Optional note about the status change" },
					["callId"] = new SchemaBuilder.PropertySchema { Type = "integer", Description = "Optional id of the call the unit is working, so the status appears on that call's record" }
				},
				new[] { "accessToken", "unitId", "statusType" }
			);

			server.AddTool(
				toolName,
				"Sets the status for a unit (e.g., Available, Responding, On Scene, Out of Service)",
				schema,
				async (arguments) =>
				{
					try
					{
						var args = JsonConvert.DeserializeObject<SetUnitStatusArgs>(arguments.ToString());

						if (string.IsNullOrWhiteSpace(args?.AccessToken))
						{
							return CreateErrorResponse("Access token is required");
						}

						if (args.UnitId <= 0)
						{
							return CreateErrorResponse("Valid unit ID is required");
						}

						if (args.StatusType < 1 || args.StatusType > 13)
						{
							return CreateErrorResponse("StatusType must be between 1 and 13 (1=Available, 2=Delayed, 3=Unavailable, 4=Committed, 5=Out Of Service, 6=Responding, 7=On Scene, 8=Staging, 9=Returning, 10=Cancelled, 11=Released, 12=Manual, 13=Enroute)");
						}

						_logger.LogInformation("Setting status for unit {UnitId}", args.UnitId);

						// The tool numbers statuses from 1; UnitStateTypes starts at 0 (Available). The v4 endpoint is
						// SaveUnitStatus and takes the unit as Id, the status as a string Type and the call as RespondingTo.
						var statusData = new
						{
							Id = args.UnitId.ToString(),
							Type = (args.StatusType - 1).ToString(),
							Note = args.Note,
							RespondingTo = args.CallId.HasValue && args.CallId.Value > 0 ? args.CallId.Value.ToString() : "0",
							RespondingToType = args.CallId.HasValue && args.CallId.Value > 0 ? (int?)2 : null,
							Timestamp = DateTime.UtcNow
						};

						var result = await _apiClient.PostAsync<object, object>(
							V4Routes.Post.SaveUnitStatus,
							statusData,
							args.AccessToken
						);

						return new
						{
							success = true,
							data = result,
							message = "Unit status updated successfully"
						};
					}
					catch (Exception ex)
					{
						_logger.LogError(ex, "Error setting unit status");
						return CreateErrorResponse("Failed to set unit status. Please try again later.");
					}
				}
			);
		}

		private void RegisterGetUnitLocationTool(McpServer server)
		{
			const string toolName = "get_unit_locations";
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
				"Retrieves the current GPS locations of units in the department. Only units with a current location that the caller may view are returned.",
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

						_logger.LogInformation("Retrieving unit locations");

						// v4 only has a single-unit latest-location endpoint; the map markers carry every unit, with the
						// department's location TTL and the location-view permissions already applied.
						var result = await _apiClient.GetAsync<JObject>(
							V4Routes.Get.MapDataAndMarkers,
							args.AccessToken
						);

						return new
						{
							success = true,
							data = V4ResponseReader.GetMapMarkers(result, V4ResponseReader.UnitMarkerType, "UnitId")
						};
					}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Error retrieving unit locations");
					return CreateErrorResponse("Failed to retrieve unit locations. Please try again later.");
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

		private sealed class SetUnitStatusArgs
		{
			[JsonProperty("accessToken")]
			public string AccessToken { get; set; }

			[JsonProperty("unitId")]
			public int UnitId { get; set; }

			[JsonProperty("statusType")]
			public int StatusType { get; set; }

			[JsonProperty("note")]
			public string Note { get; set; }

			[JsonProperty("callId")]
			public int? CallId { get; set; }
		}
	}
}







