using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Model.Inventories;

namespace Resgrid.Model.Services
{
	public interface IInventoryOperationsService
	{
		Task<InventoryCountDetail> StartCountAsync(InventoryActor actor, InventoryCountInput input);
		Task<InventoryCountDetail> GetCountAsync(InventoryActor actor, string id);
		Task<InventoryCountDetail> SaveCountAsync(InventoryActor actor, InventoryCountUpdate input);
		Task<InventoryResult> CompleteCountAsync(InventoryActor actor, InventoryCountComplete input);
		Task CancelCountAsync(InventoryActor actor, string id, int revision);
		/// <summary>Never throws for a member: whether inventory is usable and what the caller may do, plus the unit's countable locations.</summary>
		Task<InventoryFieldAccess> GetFieldAccessAsync(InventoryActor actor, int? unitId);
		Task RefreshAlertsAsync(InventoryActor actor);
		/// <summary>
		/// Open alerts the actor may see (the Alerts tab's per-row rule, evaluated once per item and holder), counted by type,
		/// with the <paramref name="take"/> most urgent buckets. Counts and item names only: alerts carry no protected content.
		/// </summary>
		Task<InventoryAlertSummary> GetAlertSummaryAsync(InventoryActor actor, int take);
		Task<InventoryReport> BuildReportAsync(InventoryActor actor, InventoryReportInput input);
	}
	public interface IInventoryAlertService
	{
		Task<List<int>> AlertDepartmentsAsync(int afterDepartmentId);
		Task SweepAlertsAsync(int departmentId);
		Task<InventoryAlertDelivery> ClaimAlertAsync(int departmentId, string userId);
		Task<bool> CanReceiveAlertAsync(int departmentId, string userId, string alertId);
		Task FinishAlertAsync(int departmentId, string deliveryId, string claimToken, bool handedOff);
	}
}
