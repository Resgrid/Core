using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Readiness;
using Resgrid.Model.Inventories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Readiness
{
	/// <summary>
	/// inventory.valuation (plan §7.7): IInventoryPurchasingService.GetValuationAsync (Purchasing → Valuation, which requires the
	/// AdjustInventory permission and lists only holders the viewer may see), totalled per currency and per holder. Holder names
	/// come through the catalog's authorized read.
	/// </summary>
	[DashboardWidget("inventory.valuation")]
	public class InventoryValuationWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IInventoryPurchasingService _inventoryPurchasingService;
		private readonly IInventoryMigrationService _inventoryMigrationService;
		private readonly IInventoryCatalogService _inventoryCatalogService;

		public InventoryValuationWidgetProvider(IInventoryPurchasingService inventoryPurchasingService, IInventoryMigrationService inventoryMigrationService,
			IInventoryCatalogService inventoryCatalogService)
		{
			_inventoryPurchasingService = inventoryPurchasingService;
			_inventoryMigrationService = inventoryMigrationService;
			_inventoryCatalogService = inventoryCatalogService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var maxRows = context.GetInt("maxRows", 5);
			if (!await _inventoryMigrationService.IsMigratedAsync(context.DepartmentId))
				return new InventoryValuationWidgetData { Migrated = false };

			var actor = ReadinessWidgetSupport.InventoryActor(context);
			var valuation = await ReadinessWidgetSupport.InventoryAsync(() => _inventoryPurchasingService.GetValuationAsync(actor)) ?? new InventoryValuation();
			var lines = valuation.Lines ?? new List<InventoryValuationLine>();

			var data = new InventoryValuationWidgetData
			{
				Migrated = true,
				AsOfUtc = valuation.AsOfUtc == default ? (DateTime?)null : valuation.AsOfUtc,
				HasNegativeStock = valuation.HasNegativeStock,
				Totals = (valuation.Totals ?? new List<InventoryValuationTotal>()).Select(t => new InventoryValuationTotalItem
				{
					CurrencyCode = t.CurrencyCode, KnownValue = t.KnownValue, UncostedRows = t.UncostedRows
				}).ToList()
			};

			var locations = lines.Where(l => l.LocationId != null)
				.GroupBy(l => (l.LocationId, l.CurrencyCode))
				.Select(g => new InventoryValuationLocationItem { LocationId = g.Key.LocationId, CurrencyCode = g.Key.CurrencyCode, KnownValue = g.Sum(l => l.Value ?? 0), Lines = g.Count() })
				.OrderByDescending(l => l.KnownValue).ThenBy(l => l.LocationId, StringComparer.Ordinal)
				.Take(maxRows).ToList();

			if (locations.Count > 0)
			{
				try
				{
					var rows = await _inventoryCatalogService.GetManyAsync<InventoryLocation>(actor, locations.Select(l => l.LocationId).Distinct(StringComparer.Ordinal).ToList());
					foreach (var location in locations)
						if (rows != null && rows.TryGetValue(location.LocationId, out var row))
							location.Name = ReadinessWidgetSupport.SafeText(LabelName(row));
				}
				catch (InventoryException ex) when (ex.StatusCode is 403 or 404 || ex.Code == "LocationUnavailable")
				{
					// A holder removed since the valuation read keeps its id; the client shows it unnamed.
				}
			}

			data.Locations = locations;
			return data;
		}

		private static string LabelName(InventoryLocation row)
		{
			if (string.IsNullOrWhiteSpace(row?.Content))
				return null;

			try
			{
				return JsonConvert.DeserializeObject<InventoryLabel>(row.Content)?.Name;
			}
			catch (JsonException)
			{
				return null;
			}
		}
	}
}
