﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Newtonsoft.Json;

namespace Resgrid.Web.Mcp.Tools
{
	/// <summary>
	/// Provides MCP tools for report generation in the Resgrid system
	/// </summary>
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
			RegisterGenerateCallsReportTool(server);
			RegisterGeneratePersonnelReportTool(server);
			RegisterGenerateUnitsReportTool(server);
			RegisterGenerateActivityReportTool(server);
			RegisterGetAvailableReportsTool(server);
		}

		public IEnumerable<string> GetToolNames() => _toolNames;

		private void RegisterGenerateCallsReportTool(McpServer server)
		{
			const string toolName = "generate_calls_report";
			_toolNames.Add(toolName);

			var schema = SchemaBuilder.BuildObjectSchema(
				new Dictionary<string, SchemaBuilder.PropertySchema>
				{
					["accessToken"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "OAuth2 access token obtained from authentication" },
					["startDate"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "Start date for report (ISO 8601 format)" },
					["endDate"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "End date for report (ISO 8601 format)" },
					["format"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "Report format: 'pdf', 'excel', or 'json' (default: json)" }
				},
				new[] { "accessToken", "startDate", "endDate" }
			);

			server.AddTool(
				toolName,
				"Generates a calls/dispatches report for the specified date range",
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

						_logger.LogInformation("Generating calls report from {StartDate} to {EndDate}", args.StartDate, args.EndDate);

						var reportData = new
						{
							startDate = args.StartDate,
							endDate = args.EndDate,
							format = args.Format ?? "json"
						};

						var result = await _apiClient.PostAsync<object, object>(
							"/api/v4/Reports/GenerateCallsReport",
							reportData,
							args.AccessToken
						);

						return new { success = true, data = result, message = "Calls report generated successfully" };
					}
					catch (Exception ex)
					{
						_logger.LogError(ex, "Error generating calls report");
						return CreateErrorResponse("Failed to generate calls report. Please try again later.");
					}
				}
			);
		}

		private void RegisterGeneratePersonnelReportTool(McpServer server)
		{
			const string toolName = "generate_personnel_report";
			_toolNames.Add(toolName);

			var schema = SchemaBuilder.BuildObjectSchema(
				new Dictionary<string, SchemaBuilder.PropertySchema>
				{
					["accessToken"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "OAuth2 access token obtained from authentication" },
					["startDate"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "Start date for report (ISO 8601 format)" },
					["endDate"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "End date for report (ISO 8601 format)" },
					["format"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "Report format: 'pdf', 'excel', or 'json' (default: json)" }
				},
				new[] { "accessToken", "startDate", "endDate" }
			);

			server.AddTool(
				toolName,
				"Generates a personnel activity report for the specified date range",
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

						_logger.LogInformation("Generating personnel report from {StartDate} to {EndDate}", args.StartDate, args.EndDate);

						var reportData = new
						{
							startDate = args.StartDate,
							endDate = args.EndDate,
							format = args.Format ?? "json"
						};

						var result = await _apiClient.PostAsync<object, object>(
							"/api/v4/Reports/GeneratePersonnelReport",
							reportData,
							args.AccessToken
						);

						return new { success = true, data = result, message = "Personnel report generated successfully" };
					}
					catch (Exception ex)
					{
						_logger.LogError(ex, "Error generating personnel report");
						return CreateErrorResponse("Failed to generate personnel report. Please try again later.");
					}
				}
			);
		}

		private void RegisterGenerateUnitsReportTool(McpServer server)
		{
			const string toolName = "generate_units_report";
			_toolNames.Add(toolName);

			var schema = SchemaBuilder.BuildObjectSchema(
				new Dictionary<string, SchemaBuilder.PropertySchema>
				{
					["accessToken"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "OAuth2 access token obtained from authentication" },
					["startDate"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "Start date for report (ISO 8601 format)" },
					["endDate"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "End date for report (ISO 8601 format)" },
					["format"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "Report format: 'pdf', 'excel', or 'json' (default: json)" }
				},
				new[] { "accessToken", "startDate", "endDate" }
			);

			server.AddTool(
				toolName,
				"Generates a units/apparatus activity report for the specified date range",
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

						_logger.LogInformation("Generating units report from {StartDate} to {EndDate}", args.StartDate, args.EndDate);

						var reportData = new
						{
							startDate = args.StartDate,
							endDate = args.EndDate,
							format = args.Format ?? "json"
						};

						var result = await _apiClient.PostAsync<object, object>(
							"/api/v4/Reports/GenerateUnitsReport",
							reportData,
							args.AccessToken
						);

						return new { success = true, data = result, message = "Units report generated successfully" };
					}
					catch (Exception ex)
					{
						_logger.LogError(ex, "Error generating units report");
						return CreateErrorResponse("Failed to generate units report. Please try again later.");
					}
				}
			);
		}

		private void RegisterGenerateActivityReportTool(McpServer server)
		{
			const string toolName = "generate_activity_report";
			_toolNames.Add(toolName);

			var schema = SchemaBuilder.BuildObjectSchema(
				new Dictionary<string, SchemaBuilder.PropertySchema>
				{
					["accessToken"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "OAuth2 access token obtained from authentication" },
					["startDate"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "Start date for report (ISO 8601 format)" },
					["endDate"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "End date for report (ISO 8601 format)" },
					["format"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "Report format: 'pdf', 'excel', or 'json' (default: json)" }
				},
				new[] { "accessToken", "startDate", "endDate" }
			);

			server.AddTool(
				toolName,
				"Generates a comprehensive activity report covering calls, personnel, and units",
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

						_logger.LogInformation("Generating activity report from {StartDate} to {EndDate}", args.StartDate, args.EndDate);

						var reportData = new
						{
							startDate = args.StartDate,
							endDate = args.EndDate,
							format = args.Format ?? "json"
						};

						var result = await _apiClient.PostAsync<object, object>(
							"/api/v4/Reports/GenerateActivityReport",
							reportData,
							args.AccessToken
						);

						return new { success = true, data = result, message = "Activity report generated successfully" };
					}
					catch (Exception ex)
					{
						_logger.LogError(ex, "Error generating activity report");
						return CreateErrorResponse("Failed to generate activity report. Please try again later.");
					}
				}
			);
		}

		private void RegisterGetAvailableReportsTool(McpServer server)
		{
			const string toolName = "get_available_reports";
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
				"Retrieves a list of all available report types and previously generated reports",
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

						_logger.LogInformation("Retrieving available reports");

						var result = await _apiClient.GetAsync<object>(
							"/api/v4/Reports/GetAvailableReports",
							args.AccessToken
						);

						return new { success = true, data = result };
					}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Error retrieving available reports");
					return CreateErrorResponse("Failed to retrieve available reports. Please try again later.");
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

		private sealed class GenerateReportArgs
		{
			[JsonProperty("accessToken")]
			public string AccessToken { get; set; }

			[JsonProperty("startDate")]
			public string StartDate { get; set; }

			[JsonProperty("endDate")]
			public string EndDate { get; set; }

			[JsonProperty("format")]
			public string Format { get; set; }
		}
	}
}

