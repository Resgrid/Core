using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Resgrid.Web.Mcp.ModelContextProtocol;
using Newtonsoft.Json;

namespace Resgrid.Web.Mcp.Tools
{
	/// <summary>
	/// Provides MCP tools for dispatch operations in the Resgrid system
	/// </summary>
	public sealed class DispatchToolProvider
	{
		private readonly IApiClient _apiClient;
		private readonly ILogger<DispatchToolProvider> _logger;
		private readonly List<string> _toolNames;

		public DispatchToolProvider(IApiClient apiClient, ILogger<DispatchToolProvider> logger)
		{
			_apiClient = apiClient;
			_logger = logger;
			_toolNames = new List<string>();
		}

		public void RegisterTools(McpServer server)
		{
			RegisterDispatchCallTool(server);
			RegisterGetDispatchStatusTool(server);
		}

		public IEnumerable<string> GetToolNames() => _toolNames;

		private void RegisterDispatchCallTool(McpServer server)
		{
			const string toolName = "dispatch_call";
			_toolNames.Add(toolName);

			var schema = SchemaBuilder.BuildObjectSchema(
				new Dictionary<string, SchemaBuilder.PropertySchema>
				{
					["accessToken"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "OAuth2 access token obtained from authentication" },
					["callId"] = new SchemaBuilder.PropertySchema { Type = "integer", Description = "The unique identifier of the call to dispatch" },
					["groupIds"] = new SchemaBuilder.PropertySchema { Type = "array", Items = "integer", Description = "Optional array of group IDs to dispatch" },
					["unitIds"] = new SchemaBuilder.PropertySchema { Type = "array", Items = "integer", Description = "Optional array of unit IDs to dispatch" },
					["personnelIds"] = new SchemaBuilder.PropertySchema { Type = "array", Items = "string", Description = "Optional array of personnel user IDs to dispatch" }
				},
				new[] { "accessToken", "callId" }
			);

			server.AddTool(
				toolName,
				"Dispatches personnel and units to an active call",
				schema,
				async (arguments) =>
				{
					try
					{
						var args = JsonConvert.DeserializeObject<DispatchCallArgs>(arguments.ToString());

						if (string.IsNullOrWhiteSpace(args?.AccessToken))
						{
							return CreateErrorResponse("Access token is required");
						}

						if (args.CallId <= 0)
						{
							return CreateErrorResponse("Valid call ID is required");
						}

						_logger.LogInformation("Dispatching to call {CallId}", args.CallId);

						var dispatchData = new
						{
							callId = args.CallId,
							groupIds = args.GroupIds,
							unitIds = args.UnitIds,
							personnelIds = args.PersonnelIds
						};

						var result = await _apiClient.PostAsync<object, object>(
							"/api/v4/Dispatch/DispatchCall",
							dispatchData,
							args.AccessToken
						);

						return new
						{
							success = true,
							data = result,
							message = "Dispatch successful"
						};
					}
					catch (Exception ex)
					{
						_logger.LogError(ex, "Error dispatching call");
						return CreateErrorResponse("Failed to dispatch call. Please try again later.");
					}
				}
			);
		}

		private void RegisterGetDispatchStatusTool(McpServer server)
		{
			const string toolName = "get_dispatch_status";
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
				"Gets the current dispatch status for personnel and units in the department",
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

						_logger.LogInformation("Retrieving dispatch status");

						var result = await _apiClient.GetAsync<object>(
							"/api/v4/Personnel/GetPersonnelStatuses",
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
						_logger.LogError(ex, "Error retrieving dispatch status");
						return CreateErrorResponse("Failed to retrieve dispatch status. Please try again later.");
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

		private sealed class DispatchCallArgs
		{
			[JsonProperty("accessToken")]
			public string AccessToken { get; set; }

			[JsonProperty("callId")]
			public int CallId { get; set; }

			[JsonProperty("groupIds")]
			public int[] GroupIds { get; set; }

			[JsonProperty("unitIds")]
			public int[] UnitIds { get; set; }

			[JsonProperty("personnelIds")]
			public string[] PersonnelIds { get; set; }
		}
	}
}





