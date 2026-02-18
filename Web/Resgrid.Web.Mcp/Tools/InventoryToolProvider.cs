﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Resgrid.Web.Mcp.ModelContextProtocol;
using Newtonsoft.Json;

namespace Resgrid.Web.Mcp.Tools
{
	/// <summary>
	/// Provides MCP tools for inventory management in the Resgrid system
	/// </summary>
	public sealed class InventoryToolProvider
	{
		private readonly IApiClient _apiClient;
		private readonly ILogger<InventoryToolProvider> _logger;
		private readonly List<string> _toolNames;

		public InventoryToolProvider(IApiClient apiClient, ILogger<InventoryToolProvider> logger)
		{
			_apiClient = apiClient;
			_logger = logger;
			_toolNames = new List<string>();
		}

		public void RegisterTools(McpServer server)
		{
			RegisterGetInventoryTool(server);
			RegisterGetInventoryItemTool(server);
			RegisterUpdateInventoryTool(server);
			RegisterLowStockItemsTool(server);
		}

		public IEnumerable<string> GetToolNames() => _toolNames;

		private void RegisterGetInventoryTool(McpServer server)
		{
			const string toolName = "get_inventory";
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
				"Retrieves all inventory items for the department",
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

						_logger.LogInformation("Retrieving inventory");

						var result = await _apiClient.GetAsync<object>(
							"/api/v4/Inventory/GetAll",
							args.AccessToken
						);

						return new { success = true, data = result };
					}
					catch (Exception ex)
					{
						_logger.LogError(ex, "Error retrieving inventory");
						return CreateErrorResponse("Failed to retrieve inventory. Please try again later.");
					}
				}
			);
		}

		private void RegisterGetInventoryItemTool(McpServer server)
		{
			const string toolName = "get_inventory_item";
			_toolNames.Add(toolName);

			var schema = SchemaBuilder.BuildObjectSchema(
				new Dictionary<string, SchemaBuilder.PropertySchema>
				{
					["accessToken"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "OAuth2 access token obtained from authentication" },
					["itemId"] = new SchemaBuilder.PropertySchema { Type = "integer", Description = "Inventory item ID" }
				},
				new[] { "accessToken", "itemId" }
			);

			server.AddTool(
				toolName,
				"Retrieves details of a specific inventory item",
				schema,
				async (arguments) =>
				{
					try
					{
						var args = JsonConvert.DeserializeObject<ItemIdArgs>(arguments.ToString());

						if (string.IsNullOrWhiteSpace(args?.AccessToken))
						{
							return CreateErrorResponse("Access token is required");
						}

						_logger.LogInformation("Retrieving inventory item {ItemId}", args.ItemId);

						var result = await _apiClient.GetAsync<object>(
							$"/api/v4/Inventory/GetItem?itemId={args.ItemId}",
							args.AccessToken
						);

						return new { success = true, data = result };
					}
					catch (Exception ex)
					{
						_logger.LogError(ex, "Error retrieving inventory item");
						return CreateErrorResponse("Failed to retrieve inventory item. Please try again later.");
					}
				}
			);
		}

		private void RegisterUpdateInventoryTool(McpServer server)
		{
			const string toolName = "update_inventory";
			_toolNames.Add(toolName);

			var schema = SchemaBuilder.BuildObjectSchema(
				new Dictionary<string, SchemaBuilder.PropertySchema>
				{
					["accessToken"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "OAuth2 access token obtained from authentication" },
					["itemId"] = new SchemaBuilder.PropertySchema { Type = "integer", Description = "Inventory item ID" },
					["quantity"] = new SchemaBuilder.PropertySchema { Type = "integer", Description = "New quantity" },
					["note"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "Optional note about the update" }
				},
				new[] { "accessToken", "itemId", "quantity" }
			);

			server.AddTool(
				toolName,
				"Updates the quantity of an inventory item",
				schema,
				async (arguments) =>
				{
					try
					{
						var args = JsonConvert.DeserializeObject<UpdateInventoryArgs>(arguments.ToString());

						if (string.IsNullOrWhiteSpace(args?.AccessToken))
						{
							return CreateErrorResponse("Access token is required");
						}

						_logger.LogInformation("Updating inventory item {ItemId}", args.ItemId);

						var updateData = new
						{
							itemId = args.ItemId,
							quantity = args.Quantity,
							note = args.Note
						};

						var result = await _apiClient.PutAsync<object, object>(
							"/api/v4/Inventory/UpdateItem",
							updateData,
							args.AccessToken
						);

						return new { success = true, data = result, message = "Inventory updated successfully" };
					}
					catch (Exception ex)
					{
						_logger.LogError(ex, "Error updating inventory");
						return CreateErrorResponse("Failed to update inventory. Please try again later.");
					}
				}
			);
		}

		private void RegisterLowStockItemsTool(McpServer server)
		{
			const string toolName = "get_low_stock_items";
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
				"Retrieves all inventory items that are low in stock",
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

						_logger.LogInformation("Retrieving low stock items");

						var result = await _apiClient.GetAsync<object>(
							"/api/v4/Inventory/GetLowStockItems",
							args.AccessToken
						);

						return new { success = true, data = result };
					}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Error retrieving low stock items");
					return CreateErrorResponse("Failed to retrieve low stock items. Please try again later.");
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

		private sealed class ItemIdArgs
		{
			[JsonProperty("accessToken")]
			public string AccessToken { get; set; }

			[JsonProperty("itemId")]
			public int ItemId { get; set; }
		}

		private sealed class UpdateInventoryArgs
		{
			[JsonProperty("accessToken")]
			public string AccessToken { get; set; }

			[JsonProperty("itemId")]
			public int ItemId { get; set; }

			[JsonProperty("quantity")]
			public int Quantity { get; set; }

			[JsonProperty("note")]
			public string Note { get; set; }
		}
	}
}

