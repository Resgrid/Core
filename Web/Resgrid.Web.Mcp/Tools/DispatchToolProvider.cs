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
	/// Provides MCP tools for dispatch operations in the Resgrid system
	/// </summary>
	/// <remarks>
	/// Dispatching happens when a call is created (create_call). The v4 API has no endpoint that adds dispatches to an
	/// existing call: EditCall replaces the whole call and its dispatch list, so it is not exposed here.
	/// </remarks>
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
			RegisterGetDispatchStatusTool(server);
		}

		public IEnumerable<string> GetToolNames() => _toolNames;

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

						var personnel = await _apiClient.GetAsync<JObject>(
							V4Routes.Get.AllPersonnelInfos,
							args.AccessToken
						);

						var units = await _apiClient.GetAsync<JObject>(
							V4Routes.Get.AllUnitStatuses,
							args.AccessToken
						);

						return new
						{
							success = true,
							data = new
							{
								personnel = V4ResponseReader.Project(V4ResponseReader.GetDataArray(personnel), V4ResponseReader.PersonnelStatusFields),
								units = V4ResponseReader.GetDataArray(units)
							}
						};
					}
					catch (Exception ex) when (ex is not McpToolErrorException)
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
	}
}
