using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Repositories.DataRepository
{
    public sealed partial class WorkOrderRepository
    {
        public async Task<List<T>> QueryMaintenanceAsync<T>(int departmentId, string field = null, object value = null, int skip = 0, bool pendingOnly = false) where T : WorkOrderRow
        {
            if (skip < 0 || field != null && field is not ("RecurrenceId" or "WorkOrderId" or "RequestId" or "InventoryRequestId")) throw new ArgumentException("Invalid maintenance query.");
            var where = $"{Col("DepartmentId")}={P}DepartmentId";
            if (field != null) where += $" AND {Col(field)}={P}Value";
            if (pendingOnly)
                where += typeof(T) == typeof(WorkOrderFailureIntent) ? $" AND {Col("ProcessedOn")} IS NULL" : typeof(T) == typeof(WorkOrderRecurrence) ? $" AND {Col("IsActive")}={Bool(true)}" : throw new ArgumentException("Invalid maintenance queue.");
            return (await QueryAsync<T>($"SELECT {Cols(Columns<T>(false))} FROM {Tbl(Table<T>())} WHERE {where} ORDER BY {Col("Id")} {Paging()}", new { DepartmentId = departmentId, Value = value, Skip = skip, Take = 500 }, default)).ToList();
        }
        public async Task<List<int>> MaintenanceDepartmentsAsync(int afterDepartmentId) => (await QueryAsync<int>($"SELECT DISTINCT {Col("DepartmentId")} FROM {Tbl("Departments")} WHERE {Col("DepartmentId")}>{P}After ORDER BY {Col("DepartmentId")} {Paging()}", new { After = afterDepartmentId, Skip = 0, Take = 200 }, default)).ToList();
        public Task<WorkOrderFailureIntent> FailureAsync(int departmentId, string completionId, string itemId) => QueryFirstOrDefaultAsync<WorkOrderFailureIntent>($"SELECT * FROM {Tbl("WorkOrderFailureIntents")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("CompletionId")}={P}CompletionId AND {Col("ItemId")}={P}ItemId", new { DepartmentId = departmentId, CompletionId = completionId, ItemId = itemId }, default);
        public async Task<List<WorkOrderSafetyHold>> ActiveHoldsAsync(int departmentId, int? unitId, string assetId) => (await QueryAsync<WorkOrderSafetyHold>($"SELECT * FROM {Tbl("WorkOrderSafetyHolds")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("ReleasedOn")} IS NULL AND ({Col("UnitId")}={P}UnitId OR {Col("AssetId")}={P}AssetId) ORDER BY {Col("Id")}", new { DepartmentId = departmentId, UnitId = unitId, AssetId = assetId }, default)).ToList();
        public async Task<List<WorkOrder>> OverdueAsync(int departmentId, DateTime now) => (await QueryAsync<WorkOrder>($"SELECT {Cols(Columns<WorkOrder>())} FROM {Tbl("WorkOrders")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("EscalatedOn")} IS NULL AND {(IsPostgres ? $"{Col("DueOn")} + {Col("EscalateAfterMinutes")} * INTERVAL '1 minute'" : $"DATEADD(minute,{Col("EscalateAfterMinutes")},{Col("DueOn")})")}<{P}Now AND {Col("Status")}<5 AND {Col("IsDeleted")}={Bool(false)} ORDER BY {Col("DueOn")},{Col("Id")} {Paging()}", new { DepartmentId = departmentId, Now = now, Skip = 0, Take = 500 }, default)).ToList();
        private string Bool(bool value) => IsPostgres ? (value ? "true" : "false") : (value ? "1" : "0");
        public async Task LockUnitAsync(int departmentId, int unitId)
        {
            Transaction();
            var sql = IsPostgres ? "SELECT unitid FROM units WHERE departmentid=@DepartmentId AND unitid=@UnitId FOR UPDATE" : "SELECT UnitId FROM Units WITH (UPDLOCK,HOLDLOCK) WHERE DepartmentId=@DepartmentId AND UnitId=@UnitId";
            if (await ScalarAsync<int>(sql, new { DepartmentId = departmentId, UnitId = unitId }, default) != unitId) throw new InvalidOperationException("Maintenance unit is unavailable.");
        }
        public async Task<UnitState> LatestUnitStateAsync(int departmentId, int unitId)
        {
            Transaction();
            var unit = await ScalarAsync<int>(IsPostgres ? "SELECT unitid FROM units WHERE departmentid=@DepartmentId AND unitid=@UnitId FOR UPDATE" : "SELECT UnitId FROM Units WITH (UPDLOCK,HOLDLOCK) WHERE DepartmentId=@DepartmentId AND UnitId=@UnitId", new { DepartmentId = departmentId, UnitId = unitId }, default);
            if (unit != unitId) return null;
            return await QueryFirstOrDefaultAsync<UnitState>($"SELECT {Cols("UnitStateId", "UnitId", "State", "Timestamp")} FROM {Tbl("UnitStates")} {(IsPostgres ? "" : "WITH (UPDLOCK,HOLDLOCK)")} WHERE {Col("UnitId")}={P}UnitId ORDER BY {Col("Timestamp")} DESC,{Col("UnitStateId")} DESC {Paging()}", new { UnitId = unitId, Skip = 0, Take = 1 }, default);
        }
        public async Task<int> AppendUnitStateAsync(int departmentId, int unitId, int state, DateTime now)
        {
            await LockUnitAsync(departmentId, unitId);
            var current = await LatestUnitStateAsync(departmentId, unitId);
            if (current != null && current.Timestamp >= now) now = current.Timestamp.AddMilliseconds(1);
            // Routing only: never copy notes, coordinates or another row's protected envelope.
            return await ScalarAsync<int>($"INSERT INTO {Tbl("UnitStates")} ({Cols("UnitId", "State", "Timestamp", "IsProtected")}) {(IsPostgres ? "" : "OUTPUT INSERTED.[UnitStateId]")} VALUES ({P}UnitId,{P}State,{P}Now,{Bool(false)}) {(IsPostgres ? "RETURNING unitstateid" : "")}", new { UnitId = unitId, State = state, Now = now }, default);
        }
        public async Task<int> LastAppendedUnitStateIdAsync(int departmentId, int unitId)
        {
            await LockUnitAsync(departmentId, unitId);
            return await ScalarAsync<int>($"SELECT COALESCE(MAX({Col("UnitStateId")}),0) FROM {Tbl("UnitStates")} WHERE {Col("UnitId")}={P}UnitId", new { UnitId = unitId }, default);
        }
    }
}
