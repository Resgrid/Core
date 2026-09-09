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
					["accessToken"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "OAuth2 access token obtained from authentication" },
					["page"] = new SchemaBuilder.PropertySchema { Type = "integer", Description = "Zero-based catalog page, default 0. Request the next page while HasMore is true." }
				},
				new[] { "accessToken" }
			);

			server.AddTool(
				toolName,
				"Retrieves one page of department inventory catalog items. Follow HasMore with page + 1. This client does not carry Protected Data Grants.",
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
						if (args.Page < 0 || args.Page > 10000) return CreateErrorResponse("Page must be between 0 and 10000");

						_logger.LogInformation("Retrieving inventory");

						var result = await _apiClient.GetAsync<object>(
							$"/api/v4/Inventory/GetAll?page={args.Page}",
							args.AccessToken
						);

						return new { success = true, data = result };
					}
					catch (Exception ex)
					{
						_logger.LogError("Error retrieving inventory ({ExceptionType})", ex.GetType().Name);
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
					["itemId"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "Inventory item GUID returned by get_inventory" }
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
						if (!ValidId(args.ItemId)) return CreateErrorResponse("A valid inventory item GUID is required");

						_logger.LogInformation("Retrieving inventory item {ItemId}", args.ItemId);

						var result = await _apiClient.GetAsync<object>(
							$"/api/v4/Inventory/GetItem?itemId={Uri.EscapeDataString(args.ItemId)}",
							args.AccessToken
						);

						return new { success = true, data = result };
					}
					catch (Exception ex)
					{
						_logger.LogError("Error retrieving inventory item ({ExceptionType})", ex.GetType().Name);
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
					["itemId"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "Inventory item GUID returned by get_inventory" },
					["requestId"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "Caller-supplied request GUID. Reuse this same GUID and unchanged values for every retry of this adjustment." },
					["fromLocationId"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "Source location GUID to subtract quantity. Supply exactly one of fromLocationId or toLocationId." },
					["toLocationId"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "Destination location GUID to add quantity. Supply exactly one of fromLocationId or toLocationId." },
					["lotId"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "Optional inventory lot GUID; required when the item tracks lots" },
					["quantity"] = new SchemaBuilder.PropertySchema { Type = "number", Description = "Positive quantity delta, up to 100000000 with at most six decimal places; never an absolute balance" },
					["note"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "Optional note about the update" }
				},
				new[] { "accessToken", "itemId", "requestId", "quantity" }
			);
			schema["oneOf"] = new[]
			{
				new Dictionary<string, object> { ["required"] = new[] { "fromLocationId" } },
				new Dictionary<string, object> { ["required"] = new[] { "toLocationId" } }
			};

			server.AddTool(
				toolName,
				"Records an inventory quantity adjustment at an explicit location. Supply a positive delta, one direction, and a stable request GUID. Protected writes require the Inventory app; this client cannot carry a Protected Data Grant.",
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
						if (!ValidId(args.ItemId) || !ValidId(args.RequestId)) return CreateErrorResponse("Valid item and request GUIDs are required");
						if ((args.FromLocationId == null) == (args.ToLocationId == null)
							|| args.FromLocationId != null && !ValidId(args.FromLocationId) || args.ToLocationId != null && !ValidId(args.ToLocationId)
							|| args.LotId != null && !ValidId(args.LotId)) return CreateErrorResponse("Supply exactly one valid source or destination location GUID and a valid optional lot GUID");
						if (args.Quantity <= 0 || args.Quantity > 100000000m || decimal.Round(args.Quantity, 6) != args.Quantity) return CreateErrorResponse("Quantity must be a positive delta up to 100000000 with at most six decimal places");
						if (args.Note?.Length > 16000) return CreateErrorResponse("The adjustment note cannot exceed 16000 characters");

						_logger.LogInformation("Updating inventory item {ItemId}", args.ItemId);

						var updateData = new
						{
							itemId = args.ItemId,
							requestId = args.RequestId,
							fromLocationId = args.FromLocationId,
							toLocationId = args.ToLocationId,
							lotId = args.LotId,
							quantity = args.Quantity,
							note = args.Note
						};

						var result = await _apiClient.PutAsync<object, object>(
							"/api/v4/Inventory/UpdateItem",
							updateData,
							args.AccessToken
						);

						return new { success = true, data = result, message = "Inventory adjustment request accepted" };
					}
					catch (Exception ex)
					{
						_logger.LogError("Error updating inventory ({ExceptionType})", ex.GetType().Name);
						return CreateErrorResponse("The inventory adjustment could not be confirmed. Retry with the same requestId and unchanged values. Protected data requires the Inventory app.");
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
					["accessToken"] = new SchemaBuilder.PropertySchema { Type = "string", Description = "OAuth2 access token obtained from authentication" },
					["page"] = new SchemaBuilder.PropertySchema { Type = "integer", Description = "Zero-based catalog page, default 0. Continue while HasMore is true, even when a page has no low-stock items." }
				},
				new[] { "accessToken" }
			);

			server.AddTool(
				toolName,
				"Retrieves a catalog page of bulk items at or below their reorder point using stock locations this caller can view. Follow HasMore with page + 1.",
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
						if (args.Page < 0 || args.Page > 10000) return CreateErrorResponse("Page must be between 0 and 10000");

						_logger.LogInformation("Retrieving low stock items");

						var result = await _apiClient.GetAsync<object>(
							$"/api/v4/Inventory/GetLowStockItems?page={args.Page}",
							args.AccessToken
						);

						return new { success = true, data = result };
					}
					catch (Exception ex)
					{
						_logger.LogError("Error retrieving low stock items ({ExceptionType})", ex.GetType().Name);
						return CreateErrorResponse("Failed to retrieve low stock items. Please try again later.");
					}
				}
			);
		}

		private static bool ValidId(string value) => Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty;
		private static object CreateErrorResponse(string errorMessage) =>
			new { success = false, error = errorMessage };

		private sealed class TokenArgs
		{
			[JsonProperty("accessToken")]
			public string AccessToken { get; set; }
			[JsonProperty("page")]
			public int Page { get; set; }
		}

		private sealed class ItemIdArgs
		{
			[JsonProperty("accessToken")]
			public string AccessToken { get; set; }

			[JsonProperty("itemId")]
			public string ItemId { get; set; }
		}

		private sealed class UpdateInventoryArgs
		{
			[JsonProperty("accessToken")]
			public string AccessToken { get; set; }

			[JsonProperty("itemId")]
			public string ItemId { get; set; }

			[JsonProperty("requestId")]
			public string RequestId { get; set; }
			[JsonProperty("fromLocationId")]
			public string FromLocationId { get; set; }
			[JsonProperty("toLocationId")]
			public string ToLocationId { get; set; }
			[JsonProperty("lotId")]
			public string LotId { get; set; }

			[JsonProperty("quantity")]
			public decimal Quantity { get; set; }

			[JsonProperty("note")]
			public string Note { get; set; }
		}
	}
}

