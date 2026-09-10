using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Repositories.DataRepository
{
    public sealed partial class WorkOrderRepository
    {
        public async Task<List<WorkOrderPart>> AllocatedPartsAsync(int departmentId, string itemId, string locationId, string lotId, string assetId)
        {
            var sql = $"SELECT {Cols("Id", "WorkOrderId", "DepartmentId", "ReservedLocationId", "IssuedLocationId", "ReservedQuantity", "IssuedQuantity", "ReservedAssetId", "ReservedLotId")} FROM {Tbl("WorkOrderParts")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("InventoryItemId")}={P}ItemId AND {Col("Staged")}={Bool(true)} AND {Col("VoidedOn")} IS NULL AND ({Col("ReservedLotId")}={P}LotId OR ({Col("ReservedLotId")} IS NULL AND {P}LotId IS NULL)) AND ({Col("ReservedAssetId")}={P}AssetId OR ({Col("ReservedAssetId")} IS NULL AND {P}AssetId IS NULL)) AND (({Col("ReservedLocationId")}={P}LocationId AND {Col("ReservedQuantity")}>0) OR ({Col("IssuedLocationId")}={P}LocationId AND {Col("IssuedQuantity")}>0))";
            return (await QueryAsync<WorkOrderPart>(sql, new { DepartmentId = departmentId, ItemId = itemId, LocationId = locationId, LotId = lotId, AssetId = assetId }, default)).ToList();
        }
        public async Task<List<WorkOrder>> SlaDueAsync(int departmentId, DateTime now) => (await QueryAsync<WorkOrder>($"SELECT {Cols(Columns<WorkOrder>())} FROM {Tbl("WorkOrders")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("IsDeleted")}={Bool(false)} AND {Col("Status")}<5 AND (({Col("ResponseOn")} IS NULL AND {Col("ResponseDueOn")}<{P}Now AND {Col("ResponseBreachedOn")} IS NULL) OR ({Col("RepairDueOn")}<{P}Now AND {Col("RepairBreachedOn")} IS NULL)) ORDER BY {Col("Id")} {Paging()}", new { DepartmentId = departmentId, Now = now, Skip = 0, Take = 500 }, default)).ToList();
    }
}
