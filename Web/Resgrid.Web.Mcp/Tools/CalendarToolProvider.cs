using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Resgrid.Web.Mcp.ModelContextProtocol;
using Newtonsoft.Json;

namespace Resgrid.Web.Mcp.Tools
{
	/// <summary>
	/// Provides MCP tools for calendar management in the Resgrid system
	/// </summary>
	/// <remarks>
	/// Read-only: the v4 API exposes calendar items for reading (plus attendance and check-in), but has no endpoints
	/// to create, update or delete them.
	/// </remarks>
	public sealed class CalendarToolProvider
	{
		private readonly IApiClient _apiClient;
		private readonly ILogger<CalendarToolProvider> _logger;
		private readonly List<string> _toolNames;

		public CalendarToolProvider(IApiClient apiClient, ILogger<CalendarToolProvider> logger)
		{
			_apiClient = apiClient;
			_logger = logger;
			_toolNames = new List<string>();
		}

		public void RegisterTools(McpServer server)
		{
			RegisterGetCalendarItemsTool(server);
		}

		public IEnumerable<string> GetToolNames() => _toolNames;

		private void RegisterGetCalendarItemsTool(McpServer server)
		{
			const string toolName = "get_calendar_items";
			_toolNames.Add(toolName);

			var schema = SchemaBuilder.BuildObjectSchema(
				new Dictionary<string, SchemaBuilder.PropertySchema>
				{
					["accessToken"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "OAuth2 access token obtained from authentication" },
					["startDate"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "Start date (ISO 8601 format)" },
					["endDate"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "End date (ISO 8601 format)" }
				},
				new[] { "accessToken" }
			);

			server.AddTool(
				toolName,
				"Retrieves calendar items for the department, within a date range when both startDate and endDate are given",
				schema,
				async (arguments) =>
				{
					try
					{
						var args = JsonConvert.DeserializeObject<GetCalendarArgs>(arguments.ToString());

						if (string.IsNullOrWhiteSpace(args?.AccessToken))
						{
							return CreateErrorResponse("Access token is required");
						}

						_logger.LogInformation("Retrieving calendar items");

						var endpoint = V4Routes.Get.DepartmentCalendarItems;

						if (!string.IsNullOrWhiteSpace(args.StartDate) && !string.IsNullOrWhiteSpace(args.EndDate))
						{
							endpoint = $"{V4Routes.Get.DepartmentCalendarItemsInRange}?start={Uri.EscapeDataString(args.StartDate)}&end={Uri.EscapeDataString(args.EndDate)}";
						}

						var result = await _apiClient.GetAsync<object>(endpoint, args.AccessToken);

						return new { success = true, data = result };
					}
					catch (Exception ex) when (ex is not McpToolErrorException)
					{
						_logger.LogError(ex, "Error retrieving calendar items");
						return CreateErrorResponse("Failed to retrieve calendar items. Please try again later.");
					}
				}
			);
		}

		private static object CreateErrorResponse(string errorMessage) =>
			new { success = false, error = errorMessage };

		private sealed class GetCalendarArgs
		{
			[JsonProperty("accessToken")]
			public string AccessToken { get; set; }

			[JsonProperty("startDate")]
			public string StartDate { get; set; }

			[JsonProperty("endDate")]
			public string EndDate { get; set; }
		}
	}
}
