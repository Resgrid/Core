using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using FluentMigrator.Runner;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Model.WorkOrders;
using Resgrid.Repositories.DataRepository.Transactions;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
    public partial class WorkOrderDatabaseTests
    {
        private WorkOrdersService ReportService(Resgrid.Repositories.DataRepository.WorkOrderRepository repository, Resgrid.Model.Repositories.Queries.IUnitOfWork uow)
        {
            var auth = new Mock<IWorkOrderAuthorizationService>();
            auth.Setup(a => a.ScopeAsync(It.IsAny<ChecklistActor>())).ReturnsAsync((ChecklistActor a) => new WorkOrderReadScope { All = true, UserId = a.UserId });
            var read = new Mock<IProtectedReadService>(); read.SetReturnsDefault(Task.FromResult(new ProtectedReadResult()));
            return new WorkOrdersService(repository, auth.Object, Mock.Of<IReadinessAccessService>(), uow, Mock.Of<IAuditLogsRepository>(), Mock.Of<IDomainEventOutboxService>(),
                new Lazy<IProtectedReadService>(() => read.Object), new Lazy<IProtectedWriteService>(() => Mock.Of<IProtectedWriteService>()), Mock.Of<IRecordAttachmentScanner>());
        }
        [Test, Order(12)]
        public async Task P2M4_twelve_month_report_matches_independent_SQL_and_filters_pages_before_projection()
        {
            var start = new DateTime(2011, 1, 1, 0, 0, 0, DateTimeKind.Utc); var asset = Guid.NewGuid().ToString("D");
            using var uow = new UnitOfWork(Connections()); var store = Orders(uow);
            await uow.CreateOrGetConnectionAsync(); await store.LockDepartmentAsync(77);
            for (var month = 0; month < 12; month++)
            {
                var row = await Insert(store); row.CreatedOn = start.AddMonths(month); row.UpdatedOn = row.CreatedOn; row.TargetUnitId = 10; row.InventoryAssetId = asset;
                row.Status = month % 2 == 0 ? 5 : 6; row.StartedOn = row.CreatedOn.AddHours(1); row.CompletedOn = row.StartedOn.Value.AddHours(month + 1);
                row.Content = JsonConvert.SerializeObject(new { Fields = new WorkOrderContent { Title = "Synthetic yearly repair", Currency = "USD" } }); await store.WriteAsync(row);
                var labor = new WorkOrderLabor { DepartmentId = 77, WorkOrderId = row.Id, CreatedBy = "author", UserId = "author", CreatedOn = row.CreatedOn, UpdatedOn = row.CreatedOn, WorkDate = row.CreatedOn };
                await store.AllocateAsync(labor); labor.Content = "{\"Hours\":2,\"RatePerHour\":10,\"Currency\":\"USD\"}"; await store.WriteAsync(labor);
                var part = new WorkOrderPart { DepartmentId = 77, WorkOrderId = row.Id, CreatedBy = "author", CreatedOn = row.CreatedOn, UpdatedOn = row.CreatedOn };
                await store.AllocateAsync(part); part.Content = "{\"Quantity\":3,\"UnitCost\":4,\"Currency\":\"EUR\"}"; await store.WriteAsync(part);
            }
            uow.CommitChanges();
            var service = ReportService(store, uow); var actor = new ChecklistActor { DepartmentId = 77, UserId = "author" };
            var query = new WorkOrderReportQuery { FromUtc = start, UntilUtc = start.AddYears(1), AssetId = asset };
            var stats = await service.GetWorkOrderStatsAsync(actor, query);
            await using var db = Connect(_connection);
            var duration = _type == DatabaseTypes.Postgres ? "EXTRACT(EPOCH FROM (completedon-startedon))/3600.0" : "DATEDIFF_BIG(second,StartedOn,CompletedOn)/3600.0";
            var mttr = await db.ExecuteScalarAsync<decimal>($"SELECT AVG({duration}) FROM {Q("WorkOrders")} WHERE {Q("DepartmentId")}=77 AND {Q("InventoryAssetId")}=@Asset", new { Asset = asset });
            string Value(string name) => _type == DatabaseTypes.Postgres ? $"CAST(l.content::jsonb->>'{name}' AS numeric)" : $"CAST(JSON_VALUE(l.Content,'$.{name}') AS decimal(18,2))";
            async Task<decimal> Cost(string table, string quantity, string price) => await db.ExecuteScalarAsync<decimal>($"SELECT SUM({Value(quantity)}*{Value(price)}) FROM {Q(table)} l JOIN {Q("WorkOrders")} o ON o.{Q("DepartmentId")}=l.{Q("DepartmentId")} AND o.{Q("Id")}=l.{Q("WorkOrderId")} WHERE o.{Q("DepartmentId")}=77 AND o.{Q("InventoryAssetId")}=@Asset", new { Asset = asset });
            stats.Total.Should().Be(12); stats.Open.Should().Be(6); stats.MeanTimeToRepairHours.Should().Be(mttr).And.Be(6.5m);
            stats.Costs.Single(c => c.Currency == "USD").Labor.Should().Be(await Cost("WorkOrderLabors", "Hours", "RatePerHour")).And.Be(240);
            stats.Costs.Single(c => c.Currency == "EUR").Parts.Should().Be(await Cost("WorkOrderParts", "Quantity", "UnitCost")).And.Be(144);
            var scope = new WorkOrderReadScope { UserId = "author", All = true }; var first = await store.ReportOrdersAsync(77, scope, query, 7);
            query.AfterId = first.Last().Id; var last = await store.ReportOrdersAsync(77, scope, query, 7);
            first.Should().HaveCount(7); last.Should().HaveCount(5); first.Concat(last).Select(r => r.Id).Should().OnlyHaveUniqueItems();
            (await store.ReportOrdersAsync(88, scope, query, 7)).Should().BeEmpty();
            var csv = await service.ExportWorkOrdersAsync(actor, new WorkOrderReportQuery { AssetId = asset }); System.Text.Encoding.UTF8.GetString(csv).Split('\n').Length.Should().Be(26);
        }
        [Test, Order(13)]
        public async Task P2M4_snapshot_history_is_tenant_bound_atomic_immutable_and_survives_upgrade_replay()
        {
            using var uow = new UnitOfWork(Connections()); var store = Orders(uow); var at = new DateTime(2012, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            await uow.CreateOrGetConnectionAsync(); await store.LockDepartmentAsync(77); var row = await Insert(store);
            var activity = new WorkOrderActivity { DepartmentId = 77, WorkOrderId = row.Id, CreatedBy = "author", CreatedOn = at, UpdatedOn = at };
            await store.AllocateAsync(activity); activity.Content = "{\"Snapshot\":{\"Title\":\"Historical title\"}}"; await store.WriteAsync(activity);
            row.SlaPolicyRevision = 1; row.ResponseDueOn = at.AddHours(1); row.RepairDueOn = at.AddDays(1);
            row.TargetUnitId = 10; await store.CaptureReportSnapshotAsync(row, at, activity.Id); await store.CaptureReportSnapshotAsync(row, at, activity.Id);
            row.Revision++; row.TargetUnitId = 20; await store.WriteAsync(row); await store.CaptureReportSnapshotAsync(row, at.AddDays(1), null); uow.CommitChanges();
            var first = (await store.ReportSnapshotsAsync(77, new[] { row.Id }, at)).Single(); first.Revision.Should().Be(1); first.TargetUnitId.Should().Be(10); first.SourceActivityId.Should().Be(activity.Id);
            var latest = (await store.ReportSnapshotsAsync(77, new[] { row.Id }, at.AddDays(2))).Single(); latest.Revision.Should().Be(2); latest.SourceActivityId.Should().Be(activity.Id);
            (await store.ReportSnapshotsAsync(88, new[] { row.Id }, at)).Should().BeEmpty();
            await uow.CreateOrGetConnectionAsync(); row.Revision++; await store.CaptureReportSnapshotAsync(row, at.AddDays(2), null); uow.DiscardChanges();
            (await store.ReportSnapshotsAsync(77, new[] { row.Id }, at.AddDays(3))).Single().Revision.Should().Be(2);
            _runner.GetRequiredService<IMigrationRunner>().MigrateUp();
            (await store.ReportSnapshotsAsync(77, new[] { row.Id }, at)).Single().Should().BeEquivalentTo(first);
            await using var db = Connect(_connection);
            await FluentActions.Awaiting(() => db.ExecuteAsync($"INSERT INTO {Q("WorkOrderReportSnapshots")} ({Q("DepartmentId")},{Q("WorkOrderId")},{Q("Revision")},{Q("RecordedOn")},{Q("SourceType")},{Q("Status")},{Q("Priority")}) VALUES(88,@Id,1,@At,0,0,0)", new { Id = row.Id, At = at })).Should().ThrowAsync<DbException>();
            first.ResponseDueOn.Should().Be(at.AddHours(1)); first.SlaPolicyRevision.Should().Be(1);
            // The historical clock must prevent rollback even after the current policy target is disabled.
            await uow.CreateOrGetConnectionAsync(); row.SlaPolicyRevision = null; row.ResponseDueOn = null; row.RepairDueOn = null; await store.WriteAsync(row); uow.CommitChanges();
            FluentActions.Invoking(() => _runner.GetRequiredService<IMigrationRunner>().MigrateDown(205)).Should().Throw<Exception>();
            (await store.ReportSnapshotsAsync(77, new[] { row.Id }, at)).Single().Should().BeEquivalentTo(first);
        }
        [Test, Order(14)]
        public async Task P2M4_large_organization_selective_history_and_date_queries_use_indexes()
        {
            await using var db = Connect(_connection); var asset = Guid.NewGuid().ToString("D");
            var source = _type == DatabaseTypes.Postgres ? "SELECT generate_series(1,50000) AS n" : "SELECT TOP (50000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_objects a CROSS JOIN sys.all_objects b";
            var guid = _type == DatabaseTypes.Postgres ? "md5(n::text || 'readiness-p2m4')::uuid::text" : "CONVERT(varchar(36),NEWID())";
            var date = _type == DatabaseTypes.Postgres ? "timestamp '2010-01-01' + (n % 365) * interval '1 day'" : "DATEADD(day,CAST(n % 365 AS int),CAST('2010-01-01' AS datetime2))";
            await db.ExecuteAsync($"INSERT INTO {Q("WorkOrders")} ({Q("DepartmentId")},{Q("CreatedBy")},{Q("RequestId")},{Q("NumberYear")},{Q("NumberSequence")},{Q("CreatedOn")},{Q("UpdatedOn")},{Q("Content")},{Q("InventoryAssetId")},{Q("TargetUnitId")},{Q("Type")},{Q("Status")},{Q("Priority")},{Q("SourceType")}) SELECT 77,'scale-author',{guid},2010,n,{date},{date},'{{\"Fields\":{{\"Title\":\"synthetic scale row\"}}}}',CASE WHEN n<=12 THEN @Asset ELSE NULL END,CASE WHEN n<=12 THEN 999 ELSE NULL END,0,0,1,0 FROM ({source}) seed", new { Asset = asset }, commandTimeout: 120);
            await db.ExecuteAsync(_type == DatabaseTypes.Postgres ? "ANALYZE workorders" : "UPDATE STATISTICS WorkOrders WITH FULLSCAN");
            using (var uow = new UnitOfWork(Connections()))
            {
                var store = Orders(uow); var scope = new WorkOrderReadScope { UserId = "scale-author", All = true };
                var page = await store.ReportPacketOrdersAsync(77, scope, new DateTime(2011, 1, 1), new[] { 999 }, Array.Empty<string>(), 0);
                page.Should().HaveCount(12, "a small call must not load the full department history");
                (await store.ReportPacketOrdersAsync(88, scope, new DateTime(2011, 1, 1), new[] { 999 }, Array.Empty<string>(), 0)).Should().BeEmpty();
                (await store.HasUnrecoverableReportHistoryAsync(77, scope, new DateTime(2011, 1, 1))).Should().BeFalse();
                var packet = await ReportService(store, uow).ReadinessEvidenceAsync(new ChecklistActor { DepartmentId = 77, UserId = "scale-author" }, new DateTime(2010, 12, 1), new DateTime(2011, 1, 1), new[] { 999 }, Array.Empty<string>());
                packet.Items.Should().HaveCount(12); packet.HistoryUnavailable.Should().BeFalse();
            }
            var predicates = new[] { ($"{Q("InventoryAssetId")}='{asset}'", "ix_workorders_asset"), ($"{Q("TargetUnitId")}=999", "ix_workorders_unit"), ($"{Q("CreatedOn")}>='2011-01-01' AND {Q("CreatedOn")}<'2012-01-01'", "ix_workorders_reportcreated") };
            foreach (var (predicate, index) in predicates)
            {
                var sql = $"SELECT * FROM {Q("WorkOrders")} WHERE {Q("DepartmentId")}=77 AND {Q("IsDeleted")}={(_type == DatabaseTypes.Postgres ? "false" : "0")} AND {Q("Id")}>0 AND {predicate} ORDER BY {Q("Id")} " + (_type == DatabaseTypes.Postgres ? "LIMIT 51 OFFSET 0" : "OFFSET 0 ROWS FETCH NEXT 51 ROWS ONLY");
                string plan;
                if (_type == DatabaseTypes.Postgres) plan = await db.ExecuteScalarAsync<string>("EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + sql);
                else
                {
                    await db.OpenAsync();
                    try { await db.ExecuteAsync("SET SHOWPLAN_XML ON"); plan = await db.ExecuteScalarAsync<string>(sql); }
                    finally { await db.ExecuteAsync("SET SHOWPLAN_XML OFF"); await db.CloseAsync(); }
                }
                plan.ToLowerInvariant().Should().Contain(index);
                TestContext.Out.WriteLine(_type + ": 50,000-row selective report uses " + index);
            }
        }
        [Test, Order(99)]
        public async Task P2M4_retained_snapshots_follow_legal_hold_transaction_rollback_and_department_purge()
        {
            using (var uow = new UnitOfWork(Connections()))
            {
                var store = Orders(uow); await uow.CreateOrGetConnectionAsync(); await store.LockDepartmentAsync(77);
                foreach (var department in new[] { 77, 88 }) { var row = await Insert(store, department); await store.CaptureReportSnapshotAsync(row, DateTime.UtcNow, null); }
                uow.CommitChanges();
            }
            await using var db = Connect(_connection); await db.OpenAsync();
            var date = _type == DatabaseTypes.Postgres ? "timestamp" : "datetime2";
            await db.ExecuteAsync($"CREATE TABLE {Q("ChecklistDefinitions")} ({Q("DepartmentId")} int); CREATE TABLE {Q("RmsRecordLegalHolds")} ({Q("DepartmentId")} int,{Q("ReleasedOn")} {date}); INSERT INTO {Q("RmsRecordLegalHolds")} VALUES(77,NULL);");
            async Task<int> Count(int department) => await db.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {Q("WorkOrderReportSnapshots")} WHERE {Q("DepartmentId")}=@Department", new { Department = department });
            var before = await Count(77); var foreignBefore = await Count(88); before.Should().BePositive(); foreignBefore.Should().BePositive();
            await using (var hold = await db.BeginTransactionAsync())
            {
                await FluentActions.Awaiting(() => Resgrid.Repositories.DataRepository.ChecklistDepartmentCleanup.DeleteWithinTransactionAsync(db, hold, 77, _type)).Should().ThrowAsync<InvalidOperationException>().WithMessage("*legal hold*");
                await hold.CommitAsync();
            }
            (await Count(77)).Should().Be(before);
            await db.ExecuteAsync($"UPDATE {Q("RmsRecordLegalHolds")} SET {Q("ReleasedOn")}=@Now WHERE {Q("DepartmentId")}=77", new { Now = DateTime.UtcNow });
            await using (var rollback = await db.BeginTransactionAsync())
            {
                await Resgrid.Repositories.DataRepository.ChecklistDepartmentCleanup.DeleteWithinTransactionAsync(db, rollback, 77, _type);
                await rollback.RollbackAsync();
            }
            (await Count(77)).Should().Be(before);
            await using (var commit = await db.BeginTransactionAsync())
            {
                await Resgrid.Repositories.DataRepository.ChecklistDepartmentCleanup.DeleteWithinTransactionAsync(db, commit, 77, _type); await commit.CommitAsync();
            }
            (await Count(77)).Should().Be(0); (await Count(88)).Should().Be(foreignBefore);
        }
    }
}
