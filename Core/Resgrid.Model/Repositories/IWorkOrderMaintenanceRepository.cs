using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Model.Repositories
{
    public interface IWorkOrderMaintenanceRepository
    {
        Task<List<WorkOrderPart>> AllocatedPartsAsync(int departmentId, string itemId, string locationId, string lotId, string assetId);
        Task<List<WorkOrder>> SlaDueAsync(int departmentId, DateTime now);
        Task<List<T>> QueryMaintenanceAsync<T>(int departmentId, string field = null, object value = null, int skip = 0, bool pendingOnly = false) where T : WorkOrderRow;
        Task<List<int>> MaintenanceDepartmentsAsync(int afterDepartmentId);
        Task<List<WorkOrder>> OverdueAsync(int departmentId, DateTime now);
        Task<WorkOrderFailureIntent> FailureAsync(int departmentId, string completionId, string itemId);
        Task<List<WorkOrderSafetyHold>> ActiveHoldsAsync(int departmentId, int? unitId, string assetId);
        Task LockUnitAsync(int departmentId, int unitId);
        Task<UnitState> LatestUnitStateAsync(int departmentId, int unitId);
        Task<int> LastAppendedUnitStateIdAsync(int departmentId, int unitId);
        Task<int> AppendUnitStateAsync(int departmentId, int unitId, int state, DateTime now);
    }
}
