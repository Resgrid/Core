using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Newtonsoft.Json;

namespace Resgrid.Web.Mcp.Tools
{
	/// <summary>
	/// Provides MCP tools for calendar management in the Resgrid system
	/// </summary>
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
			RegisterCreateCalendarItemTool(server);
			RegisterUpdateCalendarItemTool(server);
			RegisterDeleteCalendarItemTool(server);
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
				"Retrieves calendar items for the department within a date range",
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

						var endpoint = "/api/v4/Calendar/GetItems";
						if (!string.IsNullOrWhiteSpace(args.StartDate) && !string.IsNullOrWhiteSpace(args.EndDate))
						{
							endpoint += $"?startDate={args.StartDate}&endDate={args.EndDate}";
						}

						var result = await _apiClient.GetAsync<object>(endpoint, args.AccessToken);

						return new { success = true, data = result };
					}
					catch (Exception ex)
					{
						_logger.LogError(ex, "Error retrieving calendar items");
						return CreateErrorResponse(ex.Message);
					}
				}
			);
		}

		private void RegisterCreateCalendarItemTool(McpServer server)
		{
			const string toolName = "create_calendar_item";
			_toolNames.Add(toolName);

			var schema = SchemaBuilder.BuildObjectSchema(
				new Dictionary<string, SchemaBuilder.PropertySchema>
				{
					["accessToken"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "OAuth2 access token obtained from authentication" },
					["title"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "Calendar item title" },
					["description"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "Calendar item description" },
					["startDate"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "Start date and time (ISO 8601 format)" },
					["endDate"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "End date and time (ISO 8601 format)" },
					["location"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "Event location" }
				},
				new[] { "accessToken", "title", "startDate", "endDate" }
			);

			server.AddTool(
				toolName,
				"Creates a new calendar item",
				schema,
				async (arguments) =>
				{
					try
					{
						var args = JsonConvert.DeserializeObject<CreateCalendarArgs>(arguments.ToString());

						if (string.IsNullOrWhiteSpace(args?.AccessToken))
						{
							return CreateErrorResponse("Access token is required");
						}

						_logger.LogInformation("Creating calendar item: {Title}", args.Title);

						var itemData = new
						{
							title = args.Title,
							description = args.Description,
							startDate = args.StartDate,
							endDate = args.EndDate,
							location = args.Location
						};

						var result = await _apiClient.PostAsync<object, object>(
							"/api/v4/Calendar/CreateItem",
							itemData,
							args.AccessToken
						);

						return new { success = true, data = result, message = "Calendar item created successfully" };
					}
					catch (Exception ex)
					{
						_logger.LogError(ex, "Error creating calendar item");
						return CreateErrorResponse(ex.Message);
					}
				}
			);
		}

		private void RegisterUpdateCalendarItemTool(McpServer server)
		{
			const string toolName = "update_calendar_item";
			_toolNames.Add(toolName);

			var schema = SchemaBuilder.BuildObjectSchema(
				new Dictionary<string, SchemaBuilder.PropertySchema>
				{
					["accessToken"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "OAuth2 access token obtained from authentication" },
					["itemId"] = new SchemaBuilder.PropertySchema { Type = "integer", Description = "Calendar item ID" },
					["title"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "Calendar item title" },
					["description"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "Calendar item description" },
					["startDate"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "Start date and time (ISO 8601 format)" },
					["endDate"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "End date and time (ISO 8601 format)" }
				},
				new[] { "accessToken", "itemId" }
			);

			server.AddTool(
				toolName,
				"Updates an existing calendar item",
				schema,
				async (arguments) =>
				{
					try
					{
						var args = JsonConvert.DeserializeObject<UpdateCalendarArgs>(arguments.ToString());

						if (string.IsNullOrWhiteSpace(args?.AccessToken))
						{
							return CreateErrorResponse("Access token is required");
						}

						_logger.LogInformation("Updating calendar item {ItemId}", args.ItemId);

						var result = await _apiClient.PutAsync<object, object>(
							"/api/v4/Calendar/UpdateItem",
							args,
							args.AccessToken
						);

						return new { success = true, data = result, message = "Calendar item updated successfully" };
					}
					catch (Exception ex)
					{
						_logger.LogError(ex, "Error updating calendar item");
						return CreateErrorResponse(ex.Message);
					}
				}
			);
		}

		private void RegisterDeleteCalendarItemTool(McpServer server)
		{
			const string toolName = "delete_calendar_item";
			_toolNames.Add(toolName);

			var schema = SchemaBuilder.BuildObjectSchema(
				new Dictionary<string, SchemaBuilder.PropertySchema>
				{
					["accessToken"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "OAuth2 access token obtained from authentication" },
					["itemId"] = new SchemaBuilder.PropertySchema { Type = "integer", Description = "Calendar item ID to delete" }
				},
				new[] { "accessToken", "itemId" }
			);

			server.AddTool(
				toolName,
				"Deletes a calendar item",
				schema,
				async (arguments) =>
				{
					try
					{
						var args = JsonConvert.DeserializeObject<CalendarIdArgs>(arguments.ToString());

						if (string.IsNullOrWhiteSpace(args?.AccessToken))
						{
							return CreateErrorResponse("Access token is required");
						}

						_logger.LogInformation("Deleting calendar item {ItemId}", args.ItemId);

						var success = await _apiClient.DeleteAsync(
							$"/api/v4/Calendar/DeleteItem?itemId={args.ItemId}",
							args.AccessToken
						);

						return new { success, message = success ? "Calendar item deleted successfully" : "Failed to delete calendar item" };
					}
					catch (Exception ex)
					{
						_logger.LogError(ex, "Error deleting calendar item");
						return CreateErrorResponse(ex.Message);
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

		private sealed class CreateCalendarArgs
		{
			[JsonProperty("accessToken")]
			public string AccessToken { get; set; }

			[JsonProperty("title")]
			public string Title { get; set; }

			[JsonProperty("description")]
			public string Description { get; set; }

			[JsonProperty("startDate")]
			public string StartDate { get; set; }

			[JsonProperty("endDate")]
			public string EndDate { get; set; }

			[JsonProperty("location")]
			public string Location { get; set; }
		}

		private sealed class UpdateCalendarArgs
		{
			[JsonProperty("accessToken")]
			public string AccessToken { get; set; }

			[JsonProperty("itemId")]
			public int ItemId { get; set; }

			[JsonProperty("title")]
			public string Title { get; set; }

			[JsonProperty("description")]
			public string Description { get; set; }

			[JsonProperty("startDate")]
			public string StartDate { get; set; }

			[JsonProperty("endDate")]
			public string EndDate { get; set; }
		}

		private sealed class CalendarIdArgs
		{
			[JsonProperty("accessToken")]
			public string AccessToken { get; set; }

			[JsonProperty("itemId")]
			public int ItemId { get; set; }
		}
	}
}

