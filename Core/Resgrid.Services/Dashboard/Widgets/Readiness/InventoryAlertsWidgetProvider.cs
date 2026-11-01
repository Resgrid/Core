using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Readiness;
using Resgrid.Model.Inventories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Readiness
{
	/// <summary>
	/// inventory.alerts (plan §7.7): IInventoryOperationsService.GetAlertSummaryAsync — open alerts by type under the Alerts tab's
	/// row rule (one grouped count query, authorization once per item and holder). Departments that have not initialized the
	/// modern inventory fall back to the legacy consolidated inventory: entries at or below zero count as low stock.
	/// </summary>
	[DashboardWidget("inventory.alerts")]
	public class InventoryAlertsWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IInventoryOperationsService _inventoryOperationsService;
		private readonly IInventoryMigrationService _inventoryMigrationService;
		private readonly IInventoryAuthorizationService _inventoryAuthorizationService;
		private readonly IInventoryService _inventoryService;

		public InventoryAlertsWidgetProvider(IInventoryOperationsService inventoryOperationsService, IInventoryMigrationService inventoryMigrationService,
			IInventoryAuthorizationService inventoryAuthorizationService, IInventoryService inventoryService)
		{
			_inventoryOperationsService = inventoryOperationsService;
			_inventoryMigrationService = inventoryMigrationService;
			_inventoryAuthorizationService = inventoryAuthorizationService;
			_inventoryService = inventoryService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var maxRows = context.GetInt("maxRows", 5);
			var actor = ReadinessWidgetSupport.InventoryActor(context);

			if (!await _inventoryMigrationService.IsMigratedAsync(context.DepartmentId))
				return await LegacyAsync(context, actor, maxRows);

			var summary = await ReadinessWidgetSupport.InventoryAsync(() => _inventoryOperationsService.GetAlertSummaryAsync(actor, maxRows)) ?? new InventoryAlertSummary();
			int Count(InventoryAlertType type) => summary.CountsByType != null && summary.CountsByType.TryGetValue((int)type, out var count) ? count : 0;

			return new InventoryAlertsWidgetData
			{
				Migrated = true,
				LowStock = Count(InventoryAlertType.LowStock),
				ExpiringSoon = Count(InventoryAlertType.ExpiringSoon),
				Expired = Count(InventoryAlertType.Expired),
				OverdueReturn = Count(InventoryAlertType.OverdueReturn),
				DepartedHolder = Count(InventoryAlertType.DepartedHolder),
				Total = summary.Total,
				Truncated = summary.Truncated,
				Items = (summary.Top ?? new List<InventoryAlertSummaryItem>()).Take(maxRows).Select(t => new InventoryAlertsItem
				{
					AlertType = t.AlertType,
					ItemId = t.ItemId,
					ItemName = ReadinessWidgetSupport.SafeText(t.ItemName),
					LocationId = t.LocationId,
					Count = t.Count,
					Quantity = t.Quantity,
					DueOn = t.DueOn,
					OpenedOn = t.OpenedOn,
					Tone = Tone(t.AlertType)
				}).ToList()
			};
		}

		private async Task<InventoryAlertsWidgetData> LegacyAsync(DashboardWidgetContext context, InventoryActor actor, int maxRows)
		{
			// The inventory pages admit members only (InventoryController.OnActionExecutionAsync).
			await ReadinessWidgetSupport.InventoryAsync(async () => { await _inventoryAuthorizationService.RequireAsync(actor); return true; });

			var consolidated = (await _inventoryService.GetConsolidatedInventoryForDepartment(context.DepartmentId) ?? new List<Inventory>())
				.Where(i => i != null && i.DepartmentId == context.DepartmentId && i.Amount <= 0).ToList();
			var data = new InventoryAlertsWidgetData { Migrated = false, LowStock = consolidated.Count, Total = consolidated.Count };
			if (consolidated.Count == 0 || maxRows == 0)
				return data;

			var types = (await _inventoryService.GetAllTypesForDepartmentAsync(context.DepartmentId) ?? new List<InventoryType>())
				.Where(t => t.DepartmentId == context.DepartmentId).GroupBy(t => t.InventoryTypeId).ToDictionary(g => g.Key, g => g.First().Type);

			data.Items = consolidated.OrderBy(i => i.Amount).ThenBy(i => types.TryGetValue(i.TypeId, out var name) ? name : null, System.StringComparer.OrdinalIgnoreCase)
				.Take(maxRows).Select(i => new InventoryAlertsItem
				{
					AlertType = (int)InventoryAlertType.LowStock,
					ItemName = types.TryGetValue(i.TypeId, out var name) ? ReadinessWidgetSupport.SafeText(name) : null,
					Count = 1,
					Quantity = double.IsFinite(i.Amount) && System.Math.Abs(i.Amount) < 1e15 ? (decimal)i.Amount : null,
					Tone = Tone((int)InventoryAlertType.LowStock)
				}).ToList();
			return data;
		}

		private static string Tone(int alertType) => (InventoryAlertType)alertType switch
		{
			InventoryAlertType.Expired or InventoryAlertType.OverdueReturn or InventoryAlertType.DepartedHolder => "danger",
			InventoryAlertType.LowStock => "warning",
			_ => "info"
		};
	}
}
