using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Resgrid.Web.Mcp.ModelContextProtocol;
using Newtonsoft.Json;

namespace Resgrid.Web.Mcp.Tools
{
	/// <summary>
	/// Provides MCP tools for report generation in the Resgrid system
	/// </summary>
	/// <remarks>
	/// Backed by the v4 Reporting controller, which returns JSON analytics for a date window. It has no PDF/Excel
	/// output and no catalog of report types or previously generated reports.
	/// </remarks>
	public sealed class ReportsToolProvider
	{
		private readonly IApiClient _apiClient;
		private readonly ILogger<ReportsToolProvider> _logger;
		private readonly List<string> _toolNames;

		public ReportsToolProvider(IApiClient apiClient, ILogger<ReportsToolProvider> logger)
		{
			_apiClient = apiClient;
			_logger = logger;
			_toolNames = new List<string>();
		}

		public void RegisterTools(McpServer server)
		{
			RegisterReportTool(server, "generate_calls_report",
				"Generates a calls report for the specified date range: response-time analytics (alarm handling, turnout, travel and total response times). Windows longer than 5 years are shortened to the last 5 years.",
				V4Routes.Get.ResponseTimesReport);

			RegisterReportTool(server, "generate_personnel_report",
				"Generates a personnel report for the specified date range: participation and certification-compliance analytics. Windows longer than 5 years are shortened to the last 5 years.",
				V4Routes.Get.ParticipationReport);

			RegisterReportTool(server, "generate_units_report",
				"Generates a units/apparatus report for the specified date range: unit hour utilization and workload analytics. Windows longer than 5 years are shortened to the last 5 years.",
				V4Routes.Get.UtilizationReport);

			RegisterReportTool(server, "generate_activity_report",
				"Generates a department activity dashboard for the specified date range: totals, daily time series, top breakdowns and current personnel/unit availability. Windows longer than 366 days are shortened to the last 366 days.",
				V4Routes.Get.DashboardReport);
		}

		public IEnumerable<string> GetToolNames() => _toolNames;

		private void RegisterReportTool(McpServer server, string toolName, string description, string endpoint)
		{
			_toolNames.Add(toolName);

			var schema = SchemaBuilder.BuildObjectSchema(
				new Dictionary<string, SchemaBuilder.PropertySchema>
				{
					["accessToken"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "OAuth2 access token obtained from authentication" },
					["startDate"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "Start date for report (ISO 8601 format, UTC)" },
					["endDate"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "End date for report (ISO 8601 format, UTC)" }
				},
				new[] { "accessToken", "startDate", "endDate" }
			);

			server.AddTool(
				toolName,
				description,
				schema,
				async (arguments) =>
				{
					try
					{
						var args = JsonConvert.DeserializeObject<GenerateReportArgs>(arguments.ToString());

						if (string.IsNullOrWhiteSpace(args?.AccessToken))
						{
							return CreateErrorResponse("Access token is required");
						}

						if (string.IsNullOrWhiteSpace(args.StartDate))
						{
							return CreateErrorResponse("Start date is required");
						}

						if (string.IsNullOrWhiteSpace(args.EndDate))
						{
							return CreateErrorResponse("End date is required");
						}

						_logger.LogInformation("Generating {Report} from {StartDate} to {EndDate}", toolName, args.StartDate, args.EndDate);

						var result = await _apiClient.GetAsync<object>(
							$"{endpoint}?from={Uri.EscapeDataString(args.StartDate)}&to={Uri.EscapeDataString(args.EndDate)}",
							args.AccessToken
						);

						return new { success = true, data = result };
					}
					catch (Exception ex) when (ex is not McpToolErrorException)
					{
						_logger.LogError(ex, "Error generating {Report}", toolName);
						return CreateErrorResponse("Failed to generate report. Please try again later.");
					}
				}
			);
		}

		private static object CreateErrorResponse(string errorMessage) =>
			new { success = false, error = errorMessage };

		private sealed class GenerateReportArgs
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
