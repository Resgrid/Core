using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Newtonsoft.Json;

namespace Resgrid.Web.Mcp.Tools
{
	/// <summary>
	/// Provides MCP tools for shift management in the Resgrid system
	/// </summary>
	public sealed class ShiftsToolProvider
	{
		private readonly IApiClient _apiClient;
		private readonly ILogger<ShiftsToolProvider> _logger;
		private readonly List<string> _toolNames;

		public ShiftsToolProvider(IApiClient apiClient, ILogger<ShiftsToolProvider> logger)
		{
			_apiClient = apiClient;
			_logger = logger;
			_toolNames = new List<string>();
		}

		public void RegisterTools(McpServer server)
		{
			RegisterGetShiftsTool(server);
			RegisterGetShiftDetailsTool(server);
			RegisterGetCurrentShiftTool(server);
			RegisterSignupForShiftTool(server);
		}

		public IEnumerable<string> GetToolNames() => _toolNames;

		private void RegisterGetShiftsTool(McpServer server)
		{
			const string toolName = "get_shifts";
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
				"Retrieves all shifts for the department",
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

						_logger.LogInformation("Retrieving shifts");

						var result = await _apiClient.GetAsync<object>(
							"/api/v4/Shifts/GetShifts",
							args.AccessToken
						);

						return new { success = true, data = result };
					}
					catch (Exception ex)
					{
						_logger.LogError(ex, "Error retrieving shifts");
						return CreateErrorResponse(ex.Message);
					}
				}
			);
		}

		private void RegisterGetShiftDetailsTool(McpServer server)
		{
			const string toolName = "get_shift_details";
			_toolNames.Add(toolName);

			var schema = SchemaBuilder.BuildObjectSchema(
				new Dictionary<string, SchemaBuilder.PropertySchema>
				{
					["accessToken"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "OAuth2 access token obtained from authentication" },
					["shiftId"] = new SchemaBuilder.PropertySchema { Type = "integer", Description = "Shift ID" }
				},
				new[] { "accessToken", "shiftId" }
			);

			server.AddTool(
				toolName,
				"Retrieves detailed information about a specific shift",
				schema,
				async (arguments) =>
				{
					try
					{
						var args = JsonConvert.DeserializeObject<ShiftIdArgs>(arguments.ToString());

						if (string.IsNullOrWhiteSpace(args?.AccessToken))
						{
							return CreateErrorResponse("Access token is required");
						}

						_logger.LogInformation("Retrieving shift details for {ShiftId}", args.ShiftId);

						var result = await _apiClient.GetAsync<object>(
							$"/api/v4/Shifts/GetShift?shiftId={args.ShiftId}",
							args.AccessToken
						);

						return new { success = true, data = result };
					}
					catch (Exception ex)
					{
						_logger.LogError(ex, "Error retrieving shift details");
						return CreateErrorResponse(ex.Message);
					}
				}
			);
		}

		private void RegisterGetCurrentShiftTool(McpServer server)
		{
			const string toolName = "get_current_shift";
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
				"Retrieves the current active shift for the user",
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

						_logger.LogInformation("Retrieving current shift");

						var result = await _apiClient.GetAsync<object>(
							"/api/v4/Shifts/GetCurrentShift",
							args.AccessToken
						);

						return new { success = true, data = result };
					}
					catch (Exception ex)
					{
						_logger.LogError(ex, "Error retrieving current shift");
						return CreateErrorResponse(ex.Message);
					}
				}
			);
		}

		private void RegisterSignupForShiftTool(McpServer server)
		{
			const string toolName = "signup_for_shift";
			_toolNames.Add(toolName);

			var schema = SchemaBuilder.BuildObjectSchema(
				new Dictionary<string, SchemaBuilder.PropertySchema>
				{
					["accessToken"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "OAuth2 access token obtained from authentication" },
					["shiftId"] = new SchemaBuilder.PropertySchema { Type = "integer", Description = "Shift ID" },
					["shiftDayId"] = new SchemaBuilder.PropertySchema { Type = "integer", Description = "Shift day ID" }
				},
				new[] { "accessToken", "shiftId", "shiftDayId" }
			);

			server.AddTool(
				toolName,
				"Signs up the user for a specific shift",
				schema,
				async (arguments) =>
				{
					try
					{
						var args = JsonConvert.DeserializeObject<SignupShiftArgs>(arguments.ToString());

						if (string.IsNullOrWhiteSpace(args?.AccessToken))
						{
							return CreateErrorResponse("Access token is required");
						}

						_logger.LogInformation("Signing up for shift {ShiftId}", args.ShiftId);

						var signupData = new
						{
							shiftId = args.ShiftId,
							shiftDayId = args.ShiftDayId
						};

						var result = await _apiClient.PostAsync<object, object>(
							"/api/v4/Shifts/SignupForShift",
							signupData,
							args.AccessToken
						);

						return new { success = true, data = result, message = "Successfully signed up for shift" };
					}
					catch (Exception ex)
					{
						_logger.LogError(ex, "Error signing up for shift");
						return CreateErrorResponse(ex.Message);
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

		private sealed class ShiftIdArgs
		{
			[JsonProperty("accessToken")]
			public string AccessToken { get; set; }

			[JsonProperty("shiftId")]
			public int ShiftId { get; set; }
		}

		private sealed class SignupShiftArgs
		{
			[JsonProperty("accessToken")]
			public string AccessToken { get; set; }

			[JsonProperty("shiftId")]
			public int ShiftId { get; set; }

			[JsonProperty("shiftDayId")]
			public int ShiftDayId { get; set; }
		}
	}
}

