using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Repositories.DataRepository
{
    public sealed partial class WorkOrderRepository
    {
        // GUIDs are opaque cursors. Resolve their immutable sort keys inside the same tenant.
        private string AfterCursor(string table, params string[] columns)
        {
            var later = columns.Select((column, index) => "(" + string.Join(" AND ", columns.Take(index).Select(c => $"page.{Col(c)}=anchor.{Col(c)}").Append($"page.{Col(column)}>anchor.{Col(column)}")) + ")");
            return $"({P}AfterId IS NULL OR EXISTS (SELECT 1 FROM {Tbl(table)} anchor WHERE anchor.{Col("DepartmentId")}={P}DepartmentId AND anchor.{Col("Id")}={P}AfterId AND ({string.Join(" OR ", later)})))";
        }
        private static string[] EvidenceOrder<T>() where T : WorkOrderRow => typeof(T) == typeof(WorkOrderActivity) ? new[] { "CreatedOn", "Revision", "Id" } : new[] { "CreatedOn", "Id" };
        private string ReportScope(WorkOrderReadScope scope, DynamicParameters parameters)
        {
            if (scope == null || string.IsNullOrWhiteSpace(scope.UserId)) throw new ArgumentException("Invalid work-order scope.");
            parameters.Add("UserId", scope.UserId); parameters.Add("AllowedGroup", scope.GroupId);
            parameters.Add("Roles", InListValue(scope.RoleIds == null || scope.RoleIds.Length == 0 ? new[] { -1 } : scope.RoleIds));
            return scope.All ? "1=1" : $"({Col("CreatedBy")}={P}UserId OR {AssignmentScope()} OR {Col("TargetGroupId")}={P}AllowedGroup)";
        }
        public async Task<List<WorkOrder>> ReportOrdersAsync(int departmentId, WorkOrderReadScope scope, WorkOrderReportQuery query, int take, string[] ids = null)
        {
            if (query == null || (query.AfterId != null && !Guid.TryParseExact(query.AfterId, "D", out _)) || take < 1 || take > 500) throw new ArgumentException("Invalid report page.");
            if (ids?.Length > 500 || ids?.Length == 0) throw new ArgumentException("Invalid report identities.");
            var parameters = new DynamicParameters(new { DepartmentId = departmentId, query.FromUtc, query.UntilUtc, query.AfterId, Status = (int?)query.Status, Priority = (int?)query.Priority, query.UnitId, query.GroupId, query.AssetId, Skip = 0, Take = take });
            var conditions = new List<string> { $"{Col("DepartmentId")}={P}DepartmentId", $"{Col("IsDeleted")}={(IsPostgres ? "false" : "0")}", AfterCursor("WorkOrders", "CreatedOn", "Id"), ReportScope(scope, parameters) };
            if (ids != null) { parameters.Add("Ids", InListValue(ids)); conditions.Add(InList("Id", "Ids")); }
            var cohort = query.PreventiveDueCohort ? $"COALESCE({Col("OriginalDueOn")},{Col("DueOn")})" : Col("CreatedOn");
            if (query.PreventiveDueCohort) conditions.Add($"{Col("Type")}=1");
            if (query.FromUtc.HasValue) conditions.Add($"{cohort}>={P}FromUtc");
            if (query.UntilUtc.HasValue) conditions.Add($"{cohort}<{P}UntilUtc");
            foreach (var item in new[] { (query.Status.HasValue, "Status", "Status"), (query.Priority.HasValue, "Priority", "Priority"), (query.UnitId.HasValue, "TargetUnitId", "UnitId"), (query.GroupId.HasValue, "TargetGroupId", "GroupId"), (query.AssetId != null, "InventoryAssetId", "AssetId") })
                if (item.Item1) conditions.Add(Col(item.Item2) + "=" + P + item.Item3);
            return (await QueryAsync<WorkOrder>($"SELECT {Cols(Columns<WorkOrder>())} FROM {Tbl("WorkOrders")} page WHERE {string.Join(" AND ", conditions)} ORDER BY {Cols("CreatedOn", "Id")} {Paging()}", parameters, default)).ToList();
        }
        public async Task<List<T>> ReportChildrenAsync<T>(int departmentId, string[] orderIds, string afterId, int take) where T : WorkOrderRow
        {
            if (orderIds == null || orderIds.Length == 0 || orderIds.Length > 500 || (afterId != null && !Guid.TryParseExact(afterId, "D", out _)) || take < 1 || take > 500 || typeof(T) == typeof(WorkOrder)) throw new ArgumentException("Invalid report child page.");
            var parameters = new DynamicParameters(new { DepartmentId = departmentId, AfterId = afterId, Skip = 0, Take = take });
            parameters.Add("OrderIds", InListValue(orderIds));
            return (await QueryAsync<T>($"SELECT {Cols(Columns<T>(false))} FROM {Tbl(Table<T>())} page WHERE {Col("DepartmentId")}={P}DepartmentId AND {InList("WorkOrderId", "OrderIds")} AND {AfterCursor(Table<T>(), EvidenceOrder<T>())} ORDER BY {Cols(EvidenceOrder<T>())} {Paging()}", parameters, default)).ToList();
        }
        public async Task CaptureReportSnapshotAsync(WorkOrder row, DateTime recordedOn, string activityId)
        {
            Transaction();
            var snapshots = await ReportSnapshotsAsync(row.DepartmentId, new[] { row.Id }, DateTime.MaxValue);
            var previous = snapshots.SingleOrDefault();
            if (previous?.Revision >= row.Revision) return;
            // Background updates inherit a protected immutable source reference, never its plaintext.
            var snapshot = new WorkOrderReportSnapshot { DepartmentId = row.DepartmentId, WorkOrderId = row.Id, Revision = row.Revision, RecordedOn = recordedOn,
                SourceActivityId = activityId ?? previous?.SourceActivityId, RecurrenceVersionId = row.RecurrenceVersionId,
                SourceType = activityId != null ? row.SourceType : previous?.SourceType ?? (row.Content == null ? row.SourceType : 0),
                Status = row.Status, Priority = row.Priority, TargetUnitId = row.TargetUnitId, TargetGroupId = row.TargetGroupId, InventoryAssetId = row.InventoryAssetId,
                DueOn = row.DueOn, StartedOn = row.StartedOn, CompletedOn = row.CompletedOn, ClosedOn = row.ClosedOn, ResponseDueOn = row.ResponseDueOn, RepairDueOn = row.RepairDueOn, ResponseOn = row.ResponseOn, ResponseBreachedOn = row.ResponseBreachedOn, RepairBreachedOn = row.RepairBreachedOn, SlaPolicyRevision = row.SlaPolicyRevision };
            var columns = typeof(WorkOrderReportSnapshot).GetProperties().Select(p => p.Name).ToArray();
            await ExecuteAsync($"INSERT INTO {Tbl("WorkOrderReportSnapshots")} ({Cols(columns)}) VALUES ({string.Join(",", columns.Select(c => P + c))})", snapshot, default);
        }
        public async Task<List<WorkOrderReportSnapshot>> ReportSnapshotsAsync(int departmentId, string[] orderIds, DateTime asOf)
        {
            if (orderIds == null || orderIds.Length == 0 || orderIds.Length > 500) throw new ArgumentException("Invalid snapshot page.");
            var parameters = new DynamicParameters(new { DepartmentId = departmentId, AsOf = asOf }); parameters.Add("OrderIds", InListValue(orderIds));
            var columns = typeof(WorkOrderReportSnapshot).GetProperties().Select(p => p.Name).ToArray();
            return (await QueryAsync<WorkOrderReportSnapshot>($"SELECT {Cols(columns)} FROM (SELECT *, ROW_NUMBER() OVER (PARTITION BY {Col("WorkOrderId")} ORDER BY {Col("RecordedOn")} DESC,{Col("Revision")} DESC) AS rn FROM {Tbl("WorkOrderReportSnapshots")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {InList("WorkOrderId", "OrderIds")} AND {Col("RecordedOn")}<={P}AsOf) s WHERE rn=1", parameters, default)).ToList();
        }
        public async Task<List<WorkOrderReportSnapshot>> ReportSnapshotHistoryAsync(int departmentId, string orderId, string afterId)
        {
            if (!Guid.TryParseExact(orderId, "D", out _) || (afterId != null && !Guid.TryParseExact(afterId, "D", out _))) throw new ArgumentException("Invalid snapshot history page.");
            return (await QueryAsync<WorkOrderReportSnapshot>($"SELECT * FROM {Tbl("WorkOrderReportSnapshots")} page WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("WorkOrderId")}={P}OrderId AND {AfterCursor("WorkOrderReportSnapshots", "Revision", "Id")} ORDER BY {Cols("Revision", "Id")} {Paging()}", new { DepartmentId = departmentId, OrderId = orderId, AfterId = afterId, Skip = 0, Take = 500 }, default)).ToList();
        }
        public async Task<List<WorkOrder>> ReportPacketOrdersAsync(int departmentId, WorkOrderReadScope scope, DateTime asOf, int[] unitIds, string[] assetIds, string afterId)
        {
            if (unitIds == null || assetIds == null || unitIds.Length > 250 || assetIds.Length > 1000 || (afterId != null && !Guid.TryParseExact(afterId, "D", out _))) throw new ArgumentException("Invalid packet target page.");
            if (unitIds.Length == 0 && assetIds.Length == 0) return new();
            var parameters = new DynamicParameters(new { DepartmentId = departmentId, AsOf = asOf, AfterId = afterId, Skip = 0, Take = 500 });
            parameters.Add("Units", InListValue(unitIds.Length == 0 ? new[] { -1 } : unitIds));
            parameters.Add("Assets", InListValue(assetIds.Length == 0 ? new[] { "" } : assetIds));
            var targets = $"({InList("TargetUnitId", "Units")} OR {InList("InventoryAssetId", "Assets")})";
            var candidates = $"SELECT {Col("Id")} FROM {Tbl("WorkOrders")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {targets} UNION SELECT {Col("WorkOrderId")} FROM {Tbl("WorkOrderReportSnapshots")} WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("RecordedOn")}<={P}AsOf AND {targets}";
            return (await QueryAsync<WorkOrder>($"SELECT {Cols(Columns<WorkOrder>())} FROM {Tbl("WorkOrders")} page WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("IsDeleted")}={(IsPostgres ? "false" : "0")} AND {Col("CreatedOn")}<={P}AsOf AND {AfterCursor("WorkOrders", "CreatedOn", "Id")} AND {Col("Id")} IN ({candidates}) AND {ReportScope(scope, parameters)} ORDER BY {Cols("CreatedOn", "Id")} {Paging()}", parameters, default)).ToList();
        }
        public async Task<bool> HasUnrecoverableReportHistoryAsync(int departmentId, WorkOrderReadScope scope, DateTime asOf)
        {
            var parameters = new DynamicParameters(new { DepartmentId = departmentId, AsOf = asOf });
            return await ScalarAsync<int>($"SELECT CASE WHEN EXISTS(SELECT 1 FROM {Tbl("WorkOrders")} w WHERE {Col("DepartmentId")}={P}DepartmentId AND {Col("IsDeleted")}={(IsPostgres ? "false" : "0")} AND {Col("CreatedOn")}<={P}AsOf AND {Col("UpdatedOn")}>{P}AsOf AND {ReportScope(scope, parameters)} AND NOT EXISTS (SELECT 1 FROM {Tbl("WorkOrderReportSnapshots")} s WHERE s.{Col("DepartmentId")}=w.{Col("DepartmentId")} AND s.{Col("WorkOrderId")}=w.{Col("Id")} AND s.{Col("RecordedOn")}<={P}AsOf)) THEN 1 ELSE 0 END", parameters, default) == 1;
        }
    }
}
