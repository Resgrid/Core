using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.WorkOrders;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
    public partial class WorkOrderP2M1Tests
    {
        private async Task<WorkOrder> ReportSeed(DateTime created, int status = 0, int priority = 1, int? unit = 10, string asset = null)
        {
            var row = new WorkOrder { DepartmentId = 77, CreatedBy = "manager", RequestId = Guid.NewGuid().ToString("D"), NumberYear = created.Year, CreatedOn = created, UpdatedOn = created,
                Status = status, Priority = priority, TargetUnitId = unit, InventoryAssetId = asset };
            await _store.AllocateAsync(row); row.NumberSequence = row.Id;
            row.Content = JsonConvert.SerializeObject(new { Fields = new WorkOrderContent { Title = "Synthetic repair " + row.Id, Currency = "USD" } });
            await _store.WriteAsync(row); return row;
        }
        private async Task ReportLabor(WorkOrder row, decimal hours, decimal? rate, string currency)
        {
            var labor = new WorkOrderLabor { DepartmentId = row.DepartmentId, WorkOrderId = row.Id, CreatedBy = "manager", UserId = "manager", CreatedOn = row.CreatedOn, UpdatedOn = row.CreatedOn, WorkDate = row.CreatedOn };
            await _store.AllocateAsync(labor); labor.Content = JsonConvert.SerializeObject(new WorkOrderLaborContent { Hours = hours, RatePerHour = rate, Currency = currency }); await _store.WriteAsync(labor);
        }
        private async Task ReportPart(WorkOrder row, decimal quantity, decimal? price, string currency, bool voided = false, bool pending = false)
        {
            var part = new WorkOrderPart { DepartmentId = row.DepartmentId, WorkOrderId = row.Id, CreatedBy = "manager", CreatedOn = row.CreatedOn, UpdatedOn = row.CreatedOn,
                VoidedOn = voided ? row.CreatedOn : null, InventoryItemId = pending ? Guid.NewGuid().ToString("D") : null };
            await _store.AllocateAsync(part); part.Content = JsonConvert.SerializeObject(new WorkOrderPartContent { Quantity = quantity, UnitCost = price, Currency = currency }); await _store.WriteAsync(part);
        }
        [Test]
        public async Task P2M4_yearly_statistics_preserve_currency_unknown_costs_and_elapsed_repair_semantics()
        {
            var start = DateTime.UtcNow.Date.AddYears(-1); var end = DateTime.UtcNow.Date;
            for (var month = 0; month < 12; month++)
            {
                var row = await ReportSeed(start.AddMonths(month), month % 2 == 0 ? 5 : 6);
                row.StartedOn = row.CreatedOn.AddHours(1); row.CompletedOn = row.StartedOn.Value.AddHours(month + 1); await _store.WriteAsync(row);
                await ReportLabor(row, 2, 10, "USD"); await ReportPart(row, 3, 4, "EUR");
                await ReportPart(row, 1, 1000, "USD", voided: true); await ReportPart(row, 1, 1000, "USD", pending: true);
            }
            var unknown = await ReportSeed(start.AddDays(1), 6); await ReportLabor(unknown, 1, 999, null); await ReportLabor(unknown, 1, null, "EUR"); await ReportPart(unknown, 1, null, "USD");
            var outside = await ReportSeed(start.AddTicks(-1)); await ReportLabor(outside, 1, 9999, "USD");
            await ReportSeed(end); // Upper bound is exclusive.
            _access.Setup(a => a.CanUseMaintenanceAsync(77)).ReturnsAsync(false);
            var stats = await _service.GetWorkOrderStatsAsync(_actor, new WorkOrderReportQuery { FromUtc = start, UntilUtc = end });
            stats.Total.Should().Be(13); stats.Open.Should().Be(6); stats.RepairSamples.Should().Be(12); stats.MissingRepairTimes.Should().Be(1);
            stats.MeanTimeToRepairHours.Should().Be(6.5m);
            stats.Costs.Single(c => c.Currency == "USD").Labor.Should().Be(240);
            stats.Costs.Single(c => c.Currency == "EUR").Parts.Should().Be(144);
            stats.Costs.Single(c => c.Currency == null).UnknownLabor.Should().Be(1);
            stats.Costs.Single(c => c.Currency == "EUR").UnknownLabor.Should().Be(1);
            stats.Costs.Single(c => c.Currency == "USD").UnknownParts.Should().Be(1);
        }
        [Test]
        public async Task P2M4_history_pages_all_years_filters_before_paging_and_excludes_deleted_or_unscoped_orders()
        {
            var asset = Guid.NewGuid().ToString("D");
            for (var i = 0; i < 55; i++) await ReportSeed(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), asset: asset);
            await ReportSeed(DateTime.UtcNow.AddDays(-1), asset: Guid.NewGuid().ToString("D"));
            var deleted = await ReportSeed(DateTime.UtcNow.AddDays(-1), asset: asset); deleted.IsDeleted = true; await _store.WriteAsync(deleted);
            var page = await _service.GetWorkOrderHistoryAsync(_actor, new WorkOrderReportQuery { AssetId = asset });
            page.Items.Should().HaveCount(50); page.NextAfterId.Should().HaveValue();
            var next = await _service.GetWorkOrderHistoryAsync(_actor, new WorkOrderReportQuery { AssetId = asset, AfterId = page.NextAfterId.Value });
            next.Items.Should().HaveCount(5); next.NextAfterId.Should().BeNull(); page.Items.Concat(next.Items).Select(e => e.Order.Id).Should().OnlyHaveUniqueItems();
            (await _service.GetWorkOrderHistoryAsync(new ChecklistActor { DepartmentId = 77, UserId = "outsider" }, new WorkOrderReportQuery())).Items.Should().BeEmpty();
            (await _service.GetWorkOrderHistoryAsync(new ChecklistActor { DepartmentId = 88, UserId = "manager" }, new WorkOrderReportQuery())).Items.Should().BeEmpty();
            await FluentActions.Awaiting(() => _service.GetAsync(_actor, deleted.Id)).Should().ThrowAsync<WorkOrderException>();
        }
        [Test]
        public async Task P2M4_exports_fail_closed_on_grant_revocation_or_access_changes_during_projection()
        {
            await ReportSeed(DateTime.UtcNow.AddDays(-1));
            _read.SetReturnsDefault(Task.FromResult(new ProtectedReadResult { RedactedFields = { "workorders.content" } }));
            await FluentActions.Awaiting(() => _service.ExportWorkOrdersAsync(_actor, new WorkOrderReportQuery())).Should().ThrowAsync<WorkOrderException>();
            _read.SetReturnsDefault(Task.FromResult(new ProtectedReadResult()));
            var calls = 0;
            _auth.Setup(a => a.ScopeAsync(_actor)).ReturnsAsync(() => new WorkOrderReadScope { UserId = ++calls == 1 ? "manager" : "outsider", All = calls == 1 });
            await FluentActions.Awaiting(() => _service.ExportWorkOrdersAsync(_actor, new WorkOrderReportQuery())).Should().ThrowAsync<WorkOrderException>();
        }
        [Test]
        public async Task P2M4_call_packet_pins_title_target_status_and_revision_before_later_edits()
        {
            var input = Input(); input.TargetUnitId = 10;
            var detail = await _service.CreateAsync(_actor, input); var at = DateTime.UtcNow;
            var edit = detail.Input; edit.Content.Title = "Future title must not leak"; edit.TargetUnitId = 20;
            await _service.UpdateAsync(_actor, detail.Order.Id, edit);
            var packet = await _service.ReadinessEvidenceAsync(_actor, at.AddDays(-30), at, new[] { 10 }, Array.Empty<string>());
            packet.Items.Should().ContainSingle(); packet.Items[0].Title.Should().Be(input.Content.Title); packet.Items[0].Revision.Should().Be(1); packet.Items[0].UnitId.Should().Be(10);
            packet.Items[0].SourceActivityId.Should().HaveValue(); packet.Items[0].SnapshotId.Should().HaveValue(); packet.HistoryUnavailable.Should().BeFalse();
            var other = await _service.ReadinessEvidenceAsync(_actor, at.AddDays(-30), at, new[] { 20 }, Array.Empty<string>()); other.Items.Should().BeEmpty();
            var manifest = new ReadinessEvidenceManifestV1 { DepartmentId = 77, CallId = 5, WorkOrders = packet.Items };
            var current = new ReadinessEvidenceManifestV1 { DepartmentId = 77, CallId = 5 };
            FluentActions.Invoking(() => ChecklistReportDocuments.EnsureStillAuthorized(manifest, current)).Should().Throw<ChecklistException>();
            var html = ChecklistReportDocuments.Packet(manifest); html.Should().Contain(input.Content.Title).And.NotContain("Future title");
        }
        [Test]
        public async Task P2M4_legacy_mutations_report_unavailable_instead_of_inventing_past_state()
        {
            var at = DateTime.UtcNow.AddDays(-2); var row = await ReportSeed(at.AddDays(-1));
            row.UpdatedOn = at.AddDays(1); await _store.WriteAsync(row);
            var section = await _service.ReadinessEvidenceAsync(_actor, at.AddDays(-30), at, new[] { 10 }, Array.Empty<string>());
            section.Items.Should().BeEmpty(); section.HistoryUnavailable.Should().BeTrue();
        }
        [Test]
        public async Task P2M4_restored_writer_revision_gaps_are_unavailable_instead_of_stale_call_evidence()
        {
            var input = Input(); input.TargetUnitId = 10;
            var created = await _service.CreateAsync(_actor, input);
            var row = await _store.GetAsync<WorkOrder>(77, created.Order.Id);
            row.Revision++; row.Status = (int)WorkOrderStatus.Accepted; row.UpdatedOn = DateTime.UtcNow;
            await _store.WriteAsync(row); // Simulate an older application writing after a database restore.
            var at = DateTime.UtcNow.AddMinutes(1);
            var section = await _service.ReadinessEvidenceAsync(_actor, at.AddDays(-30), at, new[] { 10 }, Array.Empty<string>());
            section.Items.Should().BeEmpty(); section.HistoryUnavailable.Should().BeTrue();
        }
        [Test]
        public async Task P2M4_labor_pins_server_currency_and_snapshot_creation_rolls_back_with_event_failure()
        {
            var id = await Assigned(); var detail = await _service.GetAsync(_actor, id);
            await _service.AddLaborAsync(_actor, id, new WorkOrderLaborInput { Revision = detail.Order.Revision, WorkDate = DateTime.UtcNow, Content = new WorkOrderLaborContent { Hours = 2, RatePerHour = 3, Currency = "EUR" } });
            JsonConvert.DeserializeObject<WorkOrderLaborContent>(_store.All<WorkOrderLabor>().Single().Content).Currency.Should().Be("USD");
            var before = _store.Snapshots.Count;
            _outbox.Setup(o => o.EnqueueAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<DomainEventEnvelope>(), default)).ThrowsAsync(new InvalidOperationException("synthetic failure"));
            await FluentActions.Awaiting(() => _service.CreateAsync(_actor, Input())).Should().ThrowAsync<InvalidOperationException>();
            _store.Snapshots.Should().HaveCount(before);
        }
        [Test]
        public void P2M4_csv_and_packet_encode_untrusted_text_and_preserve_v4_numeric_enums()
        {
            var entry = new WorkOrderReportEntry { Order = new WorkOrderSummary { Title = "  =SUM(1,2)\"<script>", Status = WorkOrderStatus.InProgress, Priority = WorkOrderPriority.Emergency } };
            Encoding.UTF8.GetString(WorkOrderReportDocuments.Csv(new[] { entry })).Should().Contain("'  =SUM(1,2)\"\"<script>");
            WorkOrderReportDocuments.PacketSection(new[] { new ReadinessWorkOrderEvidence { Title = "<script>" } }).Should().Contain("&lt;script&gt;").And.NotContain("<script>");
            var json = Newtonsoft.Json.Linq.JObject.Parse(JsonConvert.SerializeObject(entry));
            json["Order"]["Status"].ToObject<int>().Should().Be(3); json["Order"]["Priority"].ToObject<int>().Should().Be(3);
        }
        [Test]
        public async Task P2M4_evidence_pages_keep_all_entries_and_packet_holds_exclude_later_releases()
        {
            var input = Input(); input.TargetUnitId = 10; var detail = await _service.CreateAsync(_actor, input); var at = DateTime.UtcNow;
            for (var i = 0; i < 55; i++)
            {
                var activity = new WorkOrderActivity { DepartmentId = 77, WorkOrderId = detail.Order.Id, CreatedOn = at, CreatedBy = "manager" };
                await _store.AllocateAsync(activity); activity.Content = "{\"Note\":\"Synthetic event\"}"; await _store.WriteAsync(activity);
            }
            var first = await _service.GetWorkOrderActivityAsync(_actor, detail.Order.Id); first.Items.Should().HaveCount(50);
            var next = await _service.GetWorkOrderActivityAsync(_actor, detail.Order.Id, first.NextAfterId.Value); next.Items.Should().HaveCount(6); next.NextAfterId.Should().BeNull();
            var hold = new WorkOrderSafetyHold { DepartmentId = 77, WorkOrderId = detail.Order.Id, UnitId = 10, CreatedOn = at.AddTicks(-1), ReleasedOn = at.AddDays(1), CreatedBy = "manager" };
            await _store.AllocateAsync(hold); hold.Content = "{\"Reason\":\"Synthetic sensitive hold\"}"; await _store.WriteAsync(hold);
            var section = await _service.ReadinessEvidenceAsync(_actor, at.AddDays(-30), at, new[] { 10 }, Array.Empty<string>());
            section.Items.Single().ActiveSafetyHoldIds.Should().Equal(hold.Id); JsonConvert.SerializeObject(section).Should().NotContain("sensitive hold").And.NotContain("ReleasedOn");
        }
        [Test]
        public async Task P2M4_open_age_priority_and_overdue_buckets_do_not_treat_unverified_completion_as_overdue()
        {
            var now = DateTime.UtcNow;
            foreach (var (age, priority, status) in new[] { (2, 0, 0), (10, 1, 1), (40, 2, 4), (100, 3, 3), (120, 1, 5), (125, 1, 9) })
            {
                var row = await ReportSeed(now.AddDays(-age), status, priority); row.DueOn = now.AddHours(-1); await _store.WriteAsync(row);
            }
            var stats = await _service.GetWorkOrderStatsAsync(_actor, new WorkOrderReportQuery());
            stats.Open.Should().Be(5); stats.Overdue.Should().Be(4); stats.OpenByAge.Should().Equal(1, 1, 1, 2); stats.OpenByPriority.Should().Equal(1, 2, 1, 1);
            await FluentActions.Awaiting(() => _service.GetWorkOrderStatsAsync(_actor, new WorkOrderReportQuery { FromUtc = now.AddDays(-400), UntilUtc = now })).Should().ThrowAsync<WorkOrderException>();
        }
        private sealed partial class Store
        {
            public List<WorkOrderReportSnapshot> Snapshots = new();
            private List<WorkOrderReportSnapshot> _snapshotsBefore;
            private void BeginReporting() => _snapshotsBefore = Snapshots.Select(Copy).ToList();
            private void RollbackReporting() { if (_snapshotsBefore != null) Snapshots = _snapshotsBefore; }
            public Task<List<WorkOrder>> ReportOrdersAsync(int departmentId, WorkOrderReadScope scope, WorkOrderReportQuery query, int take, int[] ids = null) => Task.FromResult(All<WorkOrder>().Where(r => r.DepartmentId == departmentId && !r.IsDeleted && scope.Allows(r)
                && r.Id > query.AfterId && (ids == null || ids.Contains(r.Id)) && (!query.PreventiveDueCohort || r.Type == 1) && (!query.FromUtc.HasValue || (query.PreventiveDueCohort ? r.OriginalDueOn ?? r.DueOn : r.CreatedOn) >= query.FromUtc) && (!query.UntilUtc.HasValue || (query.PreventiveDueCohort ? r.OriginalDueOn ?? r.DueOn : r.CreatedOn) < query.UntilUtc)
                && (!query.Status.HasValue || r.Status == (int)query.Status) && (!query.Priority.HasValue || r.Priority == (int)query.Priority)
                && (!query.UnitId.HasValue || r.TargetUnitId == query.UnitId) && (!query.GroupId.HasValue || r.TargetGroupId == query.GroupId) && (query.AssetId == null || r.InventoryAssetId == query.AssetId)).OrderBy(r => r.Id).Take(take).ToList());
            public Task<List<T>> ReportChildrenAsync<T>(int departmentId, int[] orderIds, int afterId, int take) where T : WorkOrderRow => Task.FromResult(All<T>().Where(r => r.DepartmentId == departmentId && r.Id > afterId && r.WorkOrderId.HasValue && orderIds.Contains(r.WorkOrderId.Value)).OrderBy(r => r.Id).Take(take).ToList());
            public Task CaptureReportSnapshotAsync(WorkOrder row, DateTime recordedOn, int? activityId)
            {
                var previous = Snapshots.LastOrDefault(s => s.DepartmentId == row.DepartmentId && s.WorkOrderId == row.Id);
                if (previous?.Revision >= row.Revision) return Task.CompletedTask;
                Snapshots.Add(new WorkOrderReportSnapshot { Id = Snapshots.Count + 1, DepartmentId = row.DepartmentId, WorkOrderId = row.Id, Revision = row.Revision, RecordedOn = recordedOn,
                    SourceActivityId = activityId ?? previous?.SourceActivityId, RecurrenceVersionId = row.RecurrenceVersionId, SourceType = activityId.HasValue ? row.SourceType : previous?.SourceType ?? (row.Content == null ? row.SourceType : 0),
                    Status = row.Status, Priority = row.Priority, TargetUnitId = row.TargetUnitId, TargetGroupId = row.TargetGroupId, InventoryAssetId = row.InventoryAssetId, DueOn = row.DueOn, StartedOn = row.StartedOn, CompletedOn = row.CompletedOn, ClosedOn = row.ClosedOn });
                return Task.CompletedTask;
            }
            public Task<List<WorkOrderReportSnapshot>> ReportSnapshotsAsync(int departmentId, int[] orderIds, DateTime asOf) => Task.FromResult(Snapshots.Where(s => s.DepartmentId == departmentId && orderIds.Contains(s.WorkOrderId) && s.RecordedOn <= asOf).GroupBy(s => s.WorkOrderId).Select(g => Copy(g.OrderBy(s => s.RecordedOn).ThenBy(s => s.Id).Last())).ToList());
            public Task<List<WorkOrderReportSnapshot>> ReportSnapshotHistoryAsync(int departmentId, int orderId, long afterId) => Task.FromResult(Snapshots.Where(s => s.DepartmentId == departmentId && s.WorkOrderId == orderId && s.Id > afterId).OrderBy(s => s.Id).Take(500).Select(Copy).ToList());
            public async Task<List<WorkOrder>> ReportPacketOrdersAsync(int departmentId, WorkOrderReadScope scope, DateTime asOf, int[] unitIds, string[] assetIds, int afterId)
            {
                var rows = await ReportOrdersAsync(departmentId, scope, new WorkOrderReportQuery { UntilUtc = asOf.AddTicks(1), AfterId = afterId }, int.MaxValue);
                return rows.Where(r => r.TargetUnitId.HasValue && unitIds.Contains(r.TargetUnitId.Value) || r.InventoryAssetId != null && assetIds.Contains(r.InventoryAssetId)
                    || Snapshots.Any(s => s.DepartmentId == departmentId && s.WorkOrderId == r.Id && s.RecordedOn <= asOf && (s.TargetUnitId.HasValue && unitIds.Contains(s.TargetUnitId.Value) || s.InventoryAssetId != null && assetIds.Contains(s.InventoryAssetId)))).Take(500).ToList();
            }
            public Task<bool> HasUnrecoverableReportHistoryAsync(int departmentId, WorkOrderReadScope scope, DateTime asOf) => Task.FromResult(All<WorkOrder>().Any(r => r.DepartmentId == departmentId && !r.IsDeleted && scope.Allows(r) && r.CreatedOn <= asOf && r.UpdatedOn > asOf && !Snapshots.Any(s => s.DepartmentId == departmentId && s.WorkOrderId == r.Id && s.RecordedOn <= asOf)));
        }
    }
}
