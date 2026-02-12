using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Newtonsoft.Json;

namespace Resgrid.Web.Mcp.Tools
{
	/// <summary>
	/// Provides MCP tools for managing calls (dispatches) in the Resgrid system
	/// </summary>
	public sealed class CallsToolProvider
	{
		private readonly IApiClient _apiClient;
		private readonly ILogger<CallsToolProvider> _logger;
		private readonly List<string> _toolNames;

		public CallsToolProvider(IApiClient apiClient, ILogger<CallsToolProvider> logger)
		{
			_apiClient = apiClient;
			_logger = logger;
			_toolNames = new List<string>();
		}

		public void RegisterTools(McpServer server)
		{
			RegisterGetActiveCallsTool(server);
			RegisterGetCallDetailsTool(server);
			RegisterCreateCallTool(server);
			RegisterCloseCallTool(server);
		}

		public IEnumerable<string> GetToolNames() => _toolNames;

		private void RegisterGetActiveCallsTool(McpServer server)
		{
			const string toolName = "get_active_calls";
			_toolNames.Add(toolName);

			var schema = SchemaBuilder.BuildObjectSchema(
				new Dictionary<string, SchemaBuilder.PropertySchema>
				{
					["accessToken"] = new SchemaBuilder.PropertySchema
					{
						Type = "string",
						Description = "OAuth2 access token obtained from authentication"
					}
				},
				new[] { "accessToken" }
			);

			server.AddTool(
				toolName,
				"Retrieves all active calls (dispatches) for the user's department in the Resgrid CAD system",
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

						_logger.LogInformation("Retrieving active calls");

						var result = await _apiClient.GetAsync<object>(
							"/api/v4/Calls/GetActiveCalls",
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
						_logger.LogError(ex, "Error retrieving active calls");
						return CreateErrorResponse(ex.Message);
					}
				}
			);
		}

		private void RegisterGetCallDetailsTool(McpServer server)
		{
			const string toolName = "get_call_details";
			_toolNames.Add(toolName);

			var schema = SchemaBuilder.BuildObjectSchema(
				new Dictionary<string, SchemaBuilder.PropertySchema>
				{
					["accessToken"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "OAuth2 access token obtained from authentication" },
					["callId"] = new SchemaBuilder.PropertySchema { Type = "integer", Description = "The unique identifier of the call" }
				},
				new[] { "accessToken", "callId" }
			);

			server.AddTool(
				toolName,
				"Retrieves detailed information about a specific call by its ID",
				schema,
				async (arguments) =>
				{
					try
					{
						var args = JsonConvert.DeserializeObject<CallIdArgs>(arguments.ToString());

						if (string.IsNullOrWhiteSpace(args?.AccessToken))
						{
							return CreateErrorResponse("Access token is required");
						}

						if (args.CallId <= 0)
						{
							return CreateErrorResponse("Valid call ID is required");
						}

						_logger.LogInformation("Retrieving call details for call {CallId}", args.CallId);

						var result = await _apiClient.GetAsync<object>(
							$"/api/v4/Calls/GetCall?callId={args.CallId}",
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
						_logger.LogError(ex, "Error retrieving call details");
						return CreateErrorResponse(ex.Message);
					}
				}
			);
		}

		private void RegisterCreateCallTool(McpServer server)
		{
			const string toolName = "create_call";
			_toolNames.Add(toolName);

			var schema = SchemaBuilder.BuildObjectSchema(
				new Dictionary<string, SchemaBuilder.PropertySchema>
				{
					["accessToken"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "OAuth2 access token obtained from authentication" },
					["name"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "Name or title of the call" },
					["nature"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "Nature of the call (e.g., 'Fire', 'Medical Emergency', 'Motor Vehicle Accident')" },
					["address"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "Address or location of the incident" },
					["notes"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "Additional notes or details about the call" },
					["priority"] = new SchemaBuilder.PropertySchema { Type = "integer", Description = "Priority level of the call (higher number = higher priority)" }
				},
				new[] { "accessToken", "name", "nature" }
			);

			server.AddTool(
				toolName,
				"Creates a new call (dispatch) in the Resgrid CAD system",
				schema,
				async (arguments) =>
				{
					try
					{
						var args = JsonConvert.DeserializeObject<CreateCallArgs>(arguments.ToString());

						if (string.IsNullOrWhiteSpace(args?.AccessToken))
						{
							return CreateErrorResponse("Access token is required");
						}

						if (string.IsNullOrWhiteSpace(args?.Name))
						{
							return CreateErrorResponse("Call name is required");
						}

						if (string.IsNullOrWhiteSpace(args?.Nature))
						{
							return CreateErrorResponse("Call nature is required");
						}

						_logger.LogInformation("Creating new call: {CallName}", args.Name);

						var callData = new
						{
							name = args.Name,
							nature = args.Nature,
							address = args.Address,
							notes = args.Notes,
							priority = args.Priority
						};

						var result = await _apiClient.PostAsync<object, object>(
							"/api/v4/Calls/NewCall",
							callData,
							args.AccessToken
						);

						return new
						{
							success = true,
							data = result,
							message = "Call created successfully"
						};
					}
					catch (Exception ex)
					{
						_logger.LogError(ex, "Error creating call");
						return CreateErrorResponse(ex.Message);
					}
				}
			);
		}

	private void RegisterCloseCallTool(McpServer server)
	{
		const string toolName = "close_call";
		_toolNames.Add(toolName);

		var schema = SchemaBuilder.BuildObjectSchema(
			new Dictionary<string, SchemaBuilder.PropertySchema>
			{
				["accessToken"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "OAuth2 access token obtained from authentication" },
				["callId"] = new SchemaBuilder.PropertySchema { Type = "integer", Description = "The unique identifier of the call to close" },
				["note"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "Optional closing note or comment" }
			},
			new[] { "accessToken", "callId" }
		);

		server.AddTool(
			toolName,
			"Closes an active call in the Resgrid CAD system",
			schema,
			async (arguments) =>
				{
					try
					{
						var args = JsonConvert.DeserializeObject<CloseCallArgs>(arguments.ToString());

						if (string.IsNullOrWhiteSpace(args?.AccessToken))
						{
							return CreateErrorResponse("Access token is required");
						}

						if (args.CallId <= 0)
						{
							return CreateErrorResponse("Valid call ID is required");
						}

						_logger.LogInformation("Closing call {CallId}", args.CallId);

						var closeData = new
						{
							callId = args.CallId,
							note = args.Note
						};

						var result = await _apiClient.PutAsync<object, object>(
							"/api/v4/Calls/CloseCall",
							closeData,
							args.AccessToken
						);

						return new
						{
							success = true,
							data = result,
							message = "Call closed successfully"
						};
					}
					catch (Exception ex)
					{
						_logger.LogError(ex, "Error closing call");
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

		private sealed class CallIdArgs
		{
			[JsonProperty("accessToken")]
			public string AccessToken { get; set; }

			[JsonProperty("callId")]
			public int CallId { get; set; }
		}

		private sealed class CreateCallArgs
		{
			[JsonProperty("accessToken")]
			public string AccessToken { get; set; }

			[JsonProperty("name")]
			public string Name { get; set; }

			[JsonProperty("nature")]
			public string Nature { get; set; }

			[JsonProperty("address")]
			public string Address { get; set; }

			[JsonProperty("notes")]
			public string Notes { get; set; }

			[JsonProperty("priority")]
			public int Priority { get; set; }
		}

		private sealed class CloseCallArgs
		{
			[JsonProperty("accessToken")]
			public string AccessToken { get; set; }

			[JsonProperty("callId")]
			public int CallId { get; set; }

			[JsonProperty("note")]
			public string Note { get; set; }
		}
	}
}







