using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Resgrid.Web.Mcp.ModelContextProtocol;
using Newtonsoft.Json;
using Sentry;

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
					var transaction = SentrySdk.StartTransaction("mcp.tool.get_active_calls", "mcp.tool");

					try
					{
						var args = JsonConvert.DeserializeObject<TokenArgs>(arguments.ToString());

						if (string.IsNullOrWhiteSpace(args?.AccessToken))
						{
							transaction.Status = SpanStatus.InvalidArgument;
							transaction.Finish();
							return CreateErrorResponse("Access token is required");
						}

						_logger.LogInformation("Retrieving active calls");
						SentrySdk.AddBreadcrumb("Retrieving active calls", "mcp.tool", level: BreadcrumbLevel.Info);

						var result = await _apiClient.GetAsync<object>(
							V4Routes.Get.ActiveCalls,
							args.AccessToken
						);

						transaction.Status = SpanStatus.Ok;
						SentrySdk.AddBreadcrumb("Active calls retrieved successfully", "mcp.tool", level: BreadcrumbLevel.Info);

						return new
						{
							success = true,
							data = result
						};
					}
					catch (Exception ex) when (ex is not McpToolErrorException)
					{
						_logger.LogError(ex, "Error retrieving active calls");
						transaction.Status = SpanStatus.InternalError;
						SentrySdk.CaptureException(ex, scope =>
						{
							scope.SetTag("tool", "get_active_calls");
							scope.SetTag("operation", "retrieve_active_calls");
						});

						return CreateErrorResponse("Failed to retrieve active calls. Please try again later.");
					}
					finally
					{
						transaction.Finish();
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
					var transaction = SentrySdk.StartTransaction("mcp.tool.get_call_details", "mcp.tool");

					try
					{
						var args = JsonConvert.DeserializeObject<CallIdArgs>(arguments.ToString());

						if (string.IsNullOrWhiteSpace(args?.AccessToken))
						{
							transaction.Status = SpanStatus.InvalidArgument;
							transaction.Finish();
							return CreateErrorResponse("Access token is required");
						}

						if (args.CallId <= 0)
						{
							transaction.Status = SpanStatus.InvalidArgument;
							transaction.Finish();
							return CreateErrorResponse("Valid call ID is required");
						}

						_logger.LogInformation("Retrieving call details for call {CallId}", args.CallId);
						SentrySdk.AddBreadcrumb($"Retrieving call details for call {args.CallId}", "mcp.tool",
							data: new Dictionary<string, string> { { "call_id", args.CallId.ToString() } },
							level: BreadcrumbLevel.Info);

						var result = await _apiClient.GetAsync<object>(
							$"{V4Routes.Get.Call}?callId={args.CallId}",
							args.AccessToken
						);

						transaction.Status = SpanStatus.Ok;
						SentrySdk.AddBreadcrumb("Call details retrieved successfully", "mcp.tool", level: BreadcrumbLevel.Info);

						return new
						{
							success = true,
							data = result
						};
					}
					catch (Exception ex) when (ex is not McpToolErrorException)
					{
						_logger.LogError(ex, "Error retrieving call details");
						transaction.Status = SpanStatus.InternalError;
						SentrySdk.CaptureException(ex, scope =>
						{
							scope.SetTag("tool", "get_call_details");
							scope.SetTag("operation", "retrieve_call_details");
						});

						return CreateErrorResponse("Failed to retrieve call details. Please try again later.");
					}
					finally
					{
						transaction.Finish();
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
					["priority"] = new SchemaBuilder.PropertySchema { Type = "integer", Description = "Call priority (0=Low, 1=Medium, 2=High, 3=Emergency, or the id of one of the department's call priorities). Defaults to 0." },
					["groupIds"] = new SchemaBuilder.PropertySchema { Type = "array", Items = "integer", Description = "Group (station) IDs to dispatch" },
					["unitIds"] = new SchemaBuilder.PropertySchema { Type = "array", Items = "integer", Description = "Unit IDs to dispatch" },
					["roleIds"] = new SchemaBuilder.PropertySchema { Type = "array", Items = "integer", Description = "Personnel role IDs to dispatch" },
					["personnelIds"] = new SchemaBuilder.PropertySchema { Type = "array", Items = "string", Description = "Personnel user IDs to dispatch" },
					["dispatchToEveryone"] = new SchemaBuilder.PropertySchema { Type = "boolean", Description = "Set to true to dispatch (page) every member of the department. Required when no dispatch targets are given." }
				},
				new[] { "accessToken", "name", "nature" }
			);

			server.AddTool(
				toolName,
				"Creates a new call in the Resgrid CAD system and dispatches it. Give the groups, units, roles or personnel to dispatch, or set dispatchToEveryone to page the whole department.",
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

						// v4 SaveCall treats an empty dispatch list as "dispatch everyone", so an omitted target list must
						// never reach it by accident: paging the whole department has to be asked for explicitly.
						var dispatchList = BuildDispatchList(args);

						if (dispatchList.Count == 0 && !args.DispatchToEveryone)
						{
							return CreateErrorResponse("Specify groupIds, unitIds, roleIds or personnelIds to dispatch, or set dispatchToEveryone to true");
						}

						if (dispatchList.Count > 0 && args.DispatchToEveryone)
						{
							return CreateErrorResponse("Specify either dispatch targets or dispatchToEveryone, not both");
						}

						_logger.LogInformation("Creating new call: {CallName}", args.Name);

						var callData = new
						{
							Name = args.Name,
							Nature = args.Nature,
							Address = args.Address,
							Note = args.Notes,
							Priority = args.Priority,
							DispatchList = args.DispatchToEveryone ? "0" : string.Join("|", dispatchList)
						};

						var result = await _apiClient.PostAsync<object, object>(
							V4Routes.Post.SaveCall,
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
					catch (Exception ex) when (ex is not McpToolErrorException)
					{
						_logger.LogError(ex, "Error creating call");
						return CreateErrorResponse("Failed to create call. Please try again later.");
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
				["note"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "Optional closing note or comment" },
				["closeType"] = new SchemaBuilder.PropertySchema { Type = "integer", Description = "How the call was closed: 1=Closed (default), 2=Cancelled, 3=Unfounded, 4=Founded, 5=Minor" }
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

						var closeType = args.CloseType ?? 1;

						if (closeType < 1 || closeType > 5)
						{
							return CreateErrorResponse("closeType must be between 1 and 5 (1=Closed, 2=Cancelled, 3=Unfounded, 4=Founded, 5=Minor)");
						}

						_logger.LogInformation("Closing call {CallId}", args.CallId);

						// CloseCall writes Type straight into Call.State, where 0 is Active: always send a closed state.
						var closeData = new
						{
							Id = args.CallId.ToString(),
							Notes = args.Note,
							Type = closeType
						};

						var result = await _apiClient.PutAsync<object, object>(
							V4Routes.Put.CloseCall,
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
				catch (Exception ex) when (ex is not McpToolErrorException)
				{
					_logger.LogError(ex, "Error closing call");
					return CreateErrorResponse("Failed to close call. Please try again later.");
				}
				}
			);
		}

		private static object CreateErrorResponse(string errorMessage) =>
			new { success = false, error = errorMessage };

		/// <summary>
		/// Builds v4 dispatch list entries: "P:" user, "G:" group, "R:" role and "U:" unit, joined with "|".
		/// </summary>
		private static List<string> BuildDispatchList(CreateCallArgs args)
		{
			var entries = new List<string>();

			foreach (var userId in args.PersonnelIds ?? Array.Empty<string>())
				if (!string.IsNullOrWhiteSpace(userId))
					entries.Add($"P:{userId.Trim()}");

			foreach (var groupId in args.GroupIds ?? Array.Empty<int>())
				entries.Add($"G:{groupId}");

			foreach (var roleId in args.RoleIds ?? Array.Empty<int>())
				entries.Add($"R:{roleId}");

			foreach (var unitId in args.UnitIds ?? Array.Empty<int>())
				entries.Add($"U:{unitId}");

			return entries;
		}

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

			[JsonProperty("groupIds")]
			public int[] GroupIds { get; set; }

			[JsonProperty("unitIds")]
			public int[] UnitIds { get; set; }

			[JsonProperty("roleIds")]
			public int[] RoleIds { get; set; }

			[JsonProperty("personnelIds")]
			public string[] PersonnelIds { get; set; }

			[JsonProperty("dispatchToEveryone")]
			public bool DispatchToEveryone { get; set; }
		}

		private sealed class CloseCallArgs
		{
			[JsonProperty("accessToken")]
			public string AccessToken { get; set; }

			[JsonProperty("callId")]
			public int CallId { get; set; }

			[JsonProperty("note")]
			public string Note { get; set; }

			[JsonProperty("closeType")]
			public int? CloseType { get; set; }
		}
	}
}









