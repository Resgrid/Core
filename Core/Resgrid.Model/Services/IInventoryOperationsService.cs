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
		Task RefreshAlertsAsync(InventoryActor actor);
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
