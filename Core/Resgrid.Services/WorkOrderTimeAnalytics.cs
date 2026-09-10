using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model.Checklists;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Services
{
    public sealed partial class WorkOrdersService
    {
        private async Task RecordGeneratedCreationAsync(WorkOrder row)
        {
            var activity = New<WorkOrderActivity>(new ChecklistActor { DepartmentId = row.DepartmentId, UserId = row.CreatedBy }, row.Id);
            activity.ActivityType = (int)WorkOrderActivityType.Created; activity.NewStatus = row.Status; await _store.AllocateAsync(activity);
        }
        private static void PopulateTimeAnalytics(WorkOrderReportEntry entry, WorkOrder order, IEnumerable<WorkOrderActivity> activities, IEnumerable<WorkOrderSafetyHold> holds, DateTime now)
        {
            var events = activities.Where(a => a.CreatedOn <= now).OrderBy(a => a.CreatedOn).ThenBy(a => a.Id).ToList();
            var created = events.FirstOrDefault(a => a.ActivityType == (int)WorkOrderActivityType.Created);
            if (created != null && Math.Abs((created.CreatedOn - order.CreatedOn).TotalMinutes) < 1)
            {
                var state = created.NewStatus ?? 0; var from = order.CreatedOn; decimal active = 0, waiting = 0; var valid = true;
                foreach (var transition in events.Where(a => a.OldStatus.HasValue && a.NewStatus.HasValue))
                {
                    if (transition.OldStatus != state || transition.CreatedOn < from) { valid = false; break; }
                    Add(transition.CreatedOn); state = transition.NewStatus.Value;
                }
                if (state != order.Status) valid = false;
                if (valid) { Add(now); entry.ActiveRepairHours = active; entry.WaitingHours = waiting; }
                void Add(DateTime until) { var hours = (decimal)Math.Max(0, (until - from).TotalHours); if (state == 3) active += hours; else if (state is 0 or 1 or 2 or 4) waiting += hours; from = until; }
            }
            // Union this order's explicit equipment safety holds; simultaneous holds do not double count.
            DateTime? start = null, end = null;
            foreach (var hold in holds.Where(h => h.CreatedOn <= now).OrderBy(h => h.CreatedOn))
            {
                var until = hold.ReleasedOn.HasValue && hold.ReleasedOn < now ? hold.ReleasedOn.Value : now;
                if (until < hold.CreatedOn) continue;
                if (!start.HasValue) { start = hold.CreatedOn; end = until; }
                else if (hold.CreatedOn <= end) { if (until > end) end = until; }
                else { entry.DowntimeHours += (decimal)(end.Value - start.Value).TotalHours; start = hold.CreatedOn; end = until; }
            }
            if (start.HasValue) entry.DowntimeHours += (decimal)(end.Value - start.Value).TotalHours;
        }
        private async Task PopulateAdditionalStatsAsync(ChecklistActor actor, WorkOrderReportQuery query, List<WorkOrder> rows, List<WorkOrderReportEntry> entries, WorkOrderStats stats)
        {
            foreach (var entry in entries)
            {
                stats.DowntimeHours += entry.DowntimeHours;
                if (entry.ActiveRepairHours.HasValue) { stats.ActiveRepairHours += entry.ActiveRepairHours.Value; stats.WaitingHours += entry.WaitingHours ?? 0; } else stats.MissingTimeHistory++;
                if ((int)entry.Order.Status is not (7 or 8 or 9) && entry.ResponseDueOn < (entry.ResponseOn ?? entry.CompletedOn ?? stats.AsOfUtc)) stats.ResponseSlaBreaches++;
                if ((int)entry.Order.Status is not (7 or 8 or 9) && entry.RepairDueOn < (entry.CompletedOn ?? stats.AsOfUtc)) stats.RepairSlaBreaches++;
            }
            // A repeated failure requires the same authorized asset/unit and explicitly recorded cause.
            stats.RepeatedFailures = rows.Where(r => r.Type is 0 or 2 && r.Status is not (7 or 8 or 9) && (r.InventoryAssetId != null || r.TargetUnitId.HasValue))
                .Select(r => new { Target = r.InventoryAssetId != null ? "a:" + r.InventoryAssetId : "u:" + r.TargetUnitId, Cause = Decode<StoredContent>(r.Content).Fields.Cause?.Trim().ToUpperInvariant() })
                .Where(r => !string.IsNullOrEmpty(r.Cause)).GroupBy(r => new { r.Target, r.Cause }).Sum(g => Math.Max(0, g.Count() - 1));
            var dueQuery = new WorkOrderReportQuery { PreventiveDueCohort = true, FromUtc = query.FromUtc, UntilUtc = query.UntilUtc < stats.AsOfUtc ? query.UntilUtc : stats.AsOfUtc, UnitId = query.UnitId, GroupId = query.GroupId, AssetId = query.AssetId, Priority = query.Priority };
            var captured = new List<WorkOrder>(); var scope = await _authorization.ScopeAsync(actor);
            while (true)
            {
                var batch = await _store.ReportOrdersAsync(actor.DepartmentId, scope, dueQuery, 500); captured.AddRange(batch);
                if (captured.Count > ReportLimit) throw new WorkOrderException(400, "ReportTooLarge");
                foreach (var row in batch.Where(r => r.Status is not (7 or 8 or 9)))
                {
                    stats.PreventiveDue++;
                    if (row.Status is 5 or 6 && row.CompletedOn <= (row.OriginalDueOn ?? row.DueOn)) stats.PreventiveOnTime++;
                }
                if (batch.Count < 500) break; dueQuery.AfterId = batch.Last().Id;
            }
            await VerifyReportAccessAsync(actor, captured);
        }
    }
}
