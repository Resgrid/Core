using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Services
{
    public sealed partial class WorkOrdersService
    {
        private const int ReportLimit = 50000;
        private async Task RevealReportBatchAsync<T>(ChecklistActor actor, IReadOnlyCollection<T> rows) where T : WorkOrderRow
        {
            if (rows.Count == 0) return;
            if (rows.Any(r => r == null || r.DepartmentId != actor.DepartmentId)) throw new WorkOrderException(404, "Unavailable");
            var hadPlaintext = rows.Any(r => !string.IsNullOrEmpty(r.Content) && !ProtectedDataEnvelope.HasEnvelopePrefix(r.Content));
            var result = await _read.Value.ResolveRecordsEntitiesForReadAsync(actor.DepartmentId, rows.Select(r => (r, Key(r))).ToList(), WorkOrderTables.Fields<T>(), actor.GrantToken, actor.UserId);
            if (result == null || result.RedactedFields.Count > 0 || result.IsProtected && hadPlaintext) throw new WorkOrderException(403, "ProtectedDataRequired");
        }
        private async Task<WorkOrderEvidencePage<V>> EvidencePageAsync<T, V>(ChecklistActor actor, int id, int afterId, Func<T, V> project) where T : WorkOrderRow
        {
            if (afterId < 0) throw new WorkOrderException(400, "InvalidInput");
            var order = await ReadOrderAsync(actor, id);
            var rows = await _store.ReportChildrenAsync<T>(actor.DepartmentId, new[] { id }, afterId, 51);
            var page = new WorkOrderEvidencePage<V> { NextAfterId = rows.Count > 50 ? rows[49].Id : null };
            foreach (var row in rows.Take(50)) page.Items.Add(project(await RevealAsync(actor, row)));
            await VerifyReportAccessAsync(actor, new[] { order }); return page;
        }
        public Task<WorkOrderEvidencePage<WorkOrderActivityView>> GetWorkOrderActivityAsync(ChecklistActor actor, int id, int afterId = 0) => EvidencePageAsync<WorkOrderActivity, WorkOrderActivityView>(actor, id, afterId, a =>
        {
            var view = Decode<WorkOrderActivityView>(a.Content); view.Id = a.Id; view.Type = (WorkOrderActivityType)a.ActivityType; view.UserId = a.CreatedBy; view.CreatedOn = a.CreatedOn; view.OldStatus = a.OldStatus; view.NewStatus = a.NewStatus; return view;
        });
        public Task<WorkOrderEvidencePage<WorkOrderHoldView>> GetWorkOrderHoldsAsync(ChecklistActor actor, int id, int afterId = 0) => EvidencePageAsync<WorkOrderSafetyHold, WorkOrderHoldView>(actor, id, afterId, h => new WorkOrderHoldView { Hold = h, Content = Decode<WorkOrderHoldContent>(h.Content) });
        private WorkOrderReportQuery ReportQuery(WorkOrderReportQuery input, bool statistics)
        {
            if (input == null || input.AfterId < 0 || input.UnitId <= 0 || input.GroupId <= 0 || input.FromUtc?.Kind == DateTimeKind.Local || input.UntilUtc?.Kind == DateTimeKind.Local
                || input.Status.HasValue && !Enum.IsDefined(input.Status.Value) || input.Priority.HasValue && !Enum.IsDefined(input.Priority.Value)
                || input.AssetId != null && !Guid.TryParseExact(input.AssetId, "D", out _)) throw new WorkOrderException(400, "InvalidInput");
            var query = new WorkOrderReportQuery { FromUtc = input.FromUtc, UntilUtc = input.UntilUtc, AfterId = input.AfterId, UnitId = input.UnitId, GroupId = input.GroupId, AssetId = input.AssetId, Status = input.Status, Priority = input.Priority };
            query.UntilUtc = query.UntilUtc.HasValue ? DateTime.SpecifyKind(query.UntilUtc.Value, DateTimeKind.Utc) : Now.AddTicks(1);
            query.FromUtc = query.FromUtc.HasValue ? DateTime.SpecifyKind(query.FromUtc.Value, DateTimeKind.Utc) : statistics ? query.UntilUtc.Value.AddDays(-365) : null;
            if (query.FromUtc >= query.UntilUtc || query.FromUtc?.Year < 2000 || query.UntilUtc.Value.Year > 2200
                || statistics && query.UntilUtc - query.FromUtc > TimeSpan.FromDays(366)) throw new WorkOrderException(400, "ReportRangeInvalid");
            return query;
        }
        private async Task<List<T>> ReportChildrenAsync<T>(ChecklistActor actor, int[] ids) where T : WorkOrderRow
        {
            var result = new List<T>(); var after = 0;
            while (true)
            {
                var rows = await _store.ReportChildrenAsync<T>(actor.DepartmentId, ids, after, 500);
                await RevealReportBatchAsync(actor, rows);
                foreach (var row in rows)
                {
                    if (!row.WorkOrderId.HasValue || !ids.Contains(row.WorkOrderId.Value)) throw new WorkOrderException(404, "Unavailable");
                    result.Add(row);
                }
                if (result.Count > ReportLimit) throw new WorkOrderException(400, "ReportTooLarge");
                if (rows.Count < 500) return result;
                after = rows.Last().Id;
            }
        }
        private async Task VerifyReportAccessAsync(ChecklistActor actor, IReadOnlyCollection<WorkOrder> captured, bool checkRevision = true)
        {
            await _authorization.RequireMemberAsync(actor);
            var scope = await _authorization.ScopeAsync(actor);
            foreach (var group in captured.Chunk(500))
            {
                var fresh = await _store.ReportOrdersAsync(actor.DepartmentId, scope, new WorkOrderReportQuery(), 500, group.Select(x => x.Id).ToArray());
                if (fresh.Count != group.Length) throw new WorkOrderException(403, "PermissionRequired");
                await RevealReportBatchAsync(actor, fresh);
                foreach (var row in fresh)
                {
                    if (checkRevision && group.Single(x => x.Id == row.Id).Revision != row.Revision) throw new WorkOrderException(409, "Conflict");
                    await PopulateOrderContentAsync(actor, row); // Generated orders additionally require access to their immutable protected template.
                }
            }
        }
        private async Task<List<WorkOrderReportEntry>> ReportEntriesAsync(ChecklistActor actor, List<WorkOrder> rows)
        {
            if (rows.Count == 0) return new();
            var ids = rows.Select(x => x.Id).ToArray();
            var labor = (await ReportChildrenAsync<WorkOrderLabor>(actor, ids)).ToLookup(x => x.WorkOrderId.Value);
            var parts = (await ReportChildrenAsync<WorkOrderPart>(actor, ids)).ToLookup(x => x.WorkOrderId.Value);
            var vendor = (await ReportChildrenAsync<WorkOrderVendorCharge>(actor, ids)).ToLookup(x => x.WorkOrderId.Value);
            var activity = (await ReportChildrenAsync<WorkOrderActivity>(actor, ids)).ToLookup(x => x.WorkOrderId.Value);
            var holds = (await ReportChildrenAsync<WorkOrderSafetyHold>(actor, ids)).ToLookup(x => x.WorkOrderId.Value);
            var entries = new List<WorkOrderReportEntry>();
            await RevealReportBatchAsync(actor, rows);
            foreach (var row in rows)
            {
                await PopulateOrderContentAsync(actor, row);
                var entry = new WorkOrderReportEntry { Order = Summary(row, Decode<StoredContent>(row.Content).Fields), StartedOn = row.StartedOn, CompletedOn = row.CompletedOn, ClosedOn = row.ClosedOn };
                entry.ResponseDueOn = row.ResponseDueOn; entry.RepairDueOn = row.RepairDueOn; entry.ResponseOn = row.ResponseOn;
                PopulateTimeAnalytics(entry, row, activity[row.Id], holds[row.Id], Now);
                if (row.Status is 5 or 6 && row.StartedOn.HasValue && row.CompletedOn >= row.StartedOn)
                    entry.RepairHours = (decimal)(row.CompletedOn.Value - row.StartedOn.Value).TotalHours;
                foreach (var item in labor[row.Id])
                {
                    var content = Decode<WorkOrderLaborContent>(item.Content);
                    var total = CurrencyTotal(entry.Costs, content.Currency);
                    // Old labor rows did not pin currency. Never assign today's currency to a historical rate.
                    if (content.RatePerHour.HasValue && total.Currency != null) total.Labor += decimal.Round(content.Hours * content.RatePerHour.Value, 2, MidpointRounding.AwayFromZero);
                    else total.UnknownLabor++;
                }
                foreach (var item in parts[row.Id].Where(p => !p.VoidedOn.HasValue && (p.Staged ? p.ConsumedQuantity > 0 : p.InventoryItemId == null || p.InventoryTransactionId != null)))
                {
                    var content = Decode<WorkOrderPartContent>(item.Content); var total = CurrencyTotal(entry.Costs, content.Currency);
                    if (item.Staged && content.ConsumedCost.HasValue && total.Currency != null) total.Parts += content.ConsumedCost.Value;
                    else if (!item.Staged && content.UnitCost.HasValue && total.Currency != null) total.Parts += decimal.Round(content.Quantity * content.UnitCost.Value, 2, MidpointRounding.AwayFromZero);
                    else total.UnknownParts++;
                }
                foreach (var item in vendor[row.Id].Where(c => !c.VoidedOn.HasValue)) { var content = Decode<WorkOrderVendorChargeContent>(item.Content); CurrencyTotal(entry.Costs, content.Currency).Vendor += content.Amount; }
                entries.Add(entry);
            }
            return entries;
        }
        private static WorkOrderCostTotal CurrencyTotal(List<WorkOrderCostTotal> totals, string currency)
        {
            currency = currency?.Length == 3 && currency.All(c => c is >= 'A' and <= 'Z') ? currency : null;
            var total = totals.SingleOrDefault(c => c.Currency == currency);
            if (total == null) totals.Add(total = new WorkOrderCostTotal { Currency = currency });
            return total;
        }
        public async Task<WorkOrderHistoryPage> GetWorkOrderHistoryAsync(ChecklistActor actor, WorkOrderReportQuery query)
        {
            query = ReportQuery(query, false); await _authorization.RequireMemberAsync(actor);
            var rows = await _store.ReportOrdersAsync(actor.DepartmentId, await _authorization.ScopeAsync(actor), query, 51);
            var page = new WorkOrderHistoryPage { NextAfterId = rows.Count > 50 ? rows[49].Id : null };
            rows = rows.Take(50).ToList(); page.Items = await ReportEntriesAsync(actor, rows);
            await VerifyReportAccessAsync(actor, rows); return page;
        }
        private async Task<(List<WorkOrder> Rows, List<WorkOrderReportEntry> Entries)> AllReportEntriesAsync(ChecklistActor actor, WorkOrderReportQuery query)
        {
            await _authorization.RequireMemberAsync(actor); var scope = await _authorization.ScopeAsync(actor);
            var all = new List<WorkOrder>(); var entries = new List<WorkOrderReportEntry>(); query.AfterId = 0;
            while (true)
            {
                var rows = await _store.ReportOrdersAsync(actor.DepartmentId, scope, query, 500);
                all.AddRange(rows); if (all.Count > ReportLimit) throw new WorkOrderException(400, "ReportTooLarge");
                entries.AddRange(await ReportEntriesAsync(actor, rows));
                if (rows.Count < 500) break;
                query.AfterId = rows.Last().Id;
            }
            return (all, entries);
        }
        public async Task<WorkOrderStats> GetWorkOrderStatsAsync(ChecklistActor actor, WorkOrderReportQuery query)
        {
            query = ReportQuery(query, true); var asOf = Now;
            var (rows, entries) = await AllReportEntriesAsync(actor, query);
            var stats = new WorkOrderStats { FromUtc = query.FromUtc.Value, UntilUtc = query.UntilUtc.Value, AsOfUtc = asOf, Total = rows.Count };
            decimal repair = 0;
            foreach (var entry in entries)
            {
                var order = entry.Order;
                if ((int)order.Status <= 5)
                {
                    stats.Open++; stats.OpenByPriority[(int)order.Priority]++;
                    var days = Math.Max(0, (asOf - order.CreatedOn).TotalDays); stats.OpenByAge[days < 7 ? 0 : days < 30 ? 1 : days < 90 ? 2 : 3]++;
                    if ((int)order.Status < 5 && order.DueOn < asOf) stats.Overdue++;
                }
                if (entry.RepairHours.HasValue) { repair += entry.RepairHours.Value; stats.RepairSamples++; }
                else if (order.Status is WorkOrderStatus.Completed or WorkOrderStatus.Closed) stats.MissingRepairTimes++;
                foreach (var cost in entry.Costs)
                {
                    var total = CurrencyTotal(stats.Costs, cost.Currency); total.Labor += cost.Labor; total.Parts += cost.Parts; total.Vendor += cost.Vendor;
                    total.UnknownLabor += cost.UnknownLabor; total.UnknownParts += cost.UnknownParts;
                }
            }
            stats.MeanTimeToRepairHours = stats.RepairSamples > 0 ? repair / stats.RepairSamples : null;
            await PopulateAdditionalStatsAsync(actor, query, rows, entries, stats);
            await VerifyReportAccessAsync(actor, rows); return stats;
        }
        public async Task<byte[]> ExportWorkOrdersAsync(ChecklistActor actor, WorkOrderReportQuery query)
        {
            var (rows, entries) = await AllReportEntriesAsync(actor, ReportQuery(query, false));
            var bytes = WorkOrderReportDocuments.Csv(entries);
            await VerifyReportAccessAsync(actor, rows); return bytes;
        }
        public async Task<ReadinessWorkOrderSection> ReadinessEvidenceAsync(ChecklistActor actor, DateTime fromUtc, DateTime callUtc, int[] unitIds, string[] assetIds)
        {
            if (unitIds == null || assetIds == null || unitIds.Length > 250 || assetIds.Length > 1000 || fromUtc > callUtc) throw new WorkOrderException(400, "InvalidInput");
            await _authorization.RequireMemberAsync(actor); var scope = await _authorization.ScopeAsync(actor);
            var section = new ReadinessWorkOrderSection { RestrictedScope = !scope.All }; if (unitIds.Length == 0 && assetIds.Length == 0) return section;
            // Include older still-open maintenance as well as completed work in the packet lookback.
            section.HistoryUnavailable = await _store.HasUnrecoverableReportHistoryAsync(actor.DepartmentId, scope, callUtc);
            var captured = new List<WorkOrder>(); var scanned = 0; var afterId = 0;
            while (true)
            {
                var rows = await _store.ReportPacketOrdersAsync(actor.DepartmentId, scope, callUtc, unitIds, assetIds, afterId);
                scanned += rows.Count; if (scanned > ReportLimit) throw new WorkOrderException(400, "ReportTooLarge");
                if (rows.Count == 0) break;
                var snapshots = (await _store.ReportSnapshotsAsync(actor.DepartmentId, rows.Select(r => r.Id).ToArray(), callUtc)).ToDictionary(s => s.WorkOrderId);
                var included = new List<ReadinessWorkOrderEvidence>();
                foreach (var row in rows)
                {
                    snapshots.TryGetValue(row.Id, out var snapshot);
                    if (snapshot == null && row.UpdatedOn > callUtc) { section.HistoryUnavailable = true; continue; }
                    // A restored/older writer may have changed the root without emitting a revision reference.
                    // A known newer state before the call cannot be represented by an older snapshot.
                    if (snapshot != null && row.UpdatedOn <= callUtc && row.Revision != snapshot.Revision) { section.HistoryUnavailable = true; continue; }
                    var unit = snapshot?.TargetUnitId ?? (snapshot == null ? row.TargetUnitId : null);
                    var asset = snapshot == null ? row.InventoryAssetId : snapshot.InventoryAssetId;
                    if (!(unit.HasValue && unitIds.Contains(unit.Value)) && !(asset != null && assetIds.Contains(asset))) continue;
                    var status = snapshot?.Status ?? row.Status; var completed = snapshot == null ? row.CompletedOn : snapshot.CompletedOn;
                    var lastChange = snapshot?.RecordedOn ?? row.UpdatedOn;
                    if (status >= 5 && (completed ?? row.CreatedOn) < fromUtc && lastChange < fromUtc) continue;
                    WorkOrderContent fields;
                    if (snapshot == null) { await RevealOrderAsync(actor, row); fields = Decode<StoredContent>(row.Content).Fields; }
                    else if (snapshot.SourceActivityId.HasValue)
                    {
                        var activity = await RevealAsync(actor, await _store.GetAsync<WorkOrderActivity>(actor.DepartmentId, snapshot.SourceActivityId.Value));
                        if (activity.WorkOrderId != row.Id) throw new WorkOrderException(409, "Unavailable");
                        fields = JObject.Parse(activity.Content ?? "{}")["Snapshot"]?.ToObject<WorkOrderContent>();
                    }
                    else if (snapshot.SourceType == 2 && snapshot.RecurrenceVersionId.HasValue)
                    {
                        var version = await RevealAsync(actor, await _store.GetAsync<WorkOrderRecurrenceVersion>(actor.DepartmentId, snapshot.RecurrenceVersionId.Value));
                        fields = Decode<WorkOrderRecurrenceInput>(version.Content)?.Template?.Content;
                    }
                    else if (snapshot.SourceType == 1) { await RevealAsync(actor, await _store.GetAsync<WorkOrder>(actor.DepartmentId, row.Id)); fields = new WorkOrderContent { Title = MaintenanceText("GeneratedFailureTitle") }; }
                    else fields = null;
                    if (fields == null) { section.HistoryUnavailable = true; continue; }
                    var evidence = new ReadinessWorkOrderEvidence { WorkOrderId = row.Id, Revision = snapshot?.Revision ?? row.Revision, SnapshotId = snapshot?.Id, SourceActivityId = snapshot?.SourceActivityId,
                        RecurrenceVersionId = snapshot?.RecurrenceVersionId, RecordedOn = snapshot?.RecordedOn ?? row.UpdatedOn, Title = fields.Title, Status = status, Priority = snapshot?.Priority ?? row.Priority,
                        UnitId = unit, AssetId = asset, DueOn = snapshot == null ? row.DueOn : snapshot.DueOn, ChecklistCompletionId = row.SourceChecklistCompletionId, ChecklistItemId = row.SourceChecklistItemId };
                    section.Items.Add(evidence); included.Add(evidence);
                    captured.Add(row);
                    if (section.Items.Count > 1000) throw new WorkOrderException(400, "ReportTooLarge");
                }
                if (included.Count > 0)
                {
                    var holds = await ReportChildrenAsync<WorkOrderSafetyHold>(actor, included.Select(e => e.WorkOrderId).ToArray());
                    foreach (var evidence in included) evidence.ActiveSafetyHoldIds = holds.Where(h => h.WorkOrderId == evidence.WorkOrderId && h.CreatedOn <= callUtc && (!h.ReleasedOn.HasValue || h.ReleasedOn > callUtc)).Select(h => h.Id).ToList();
                }
                if (rows.Count < 500) break; afterId = rows.Last().Id;
            }
            await VerifyReportAccessAsync(actor, captured, false); return section;
        }
    }
}
