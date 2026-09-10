using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.WorkOrders;
using Resgrid.Repositories.DataRepository.Transactions;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
    public partial class WorkOrderDatabaseTests
    {
        [Test, Order(1)]
        public async Task Recurrence_rollback_refuses_to_destroy_a_manual_orders_original_due()
        {
            using var uow=new UnitOfWork(Connections()); var store=Orders(uow);
            await uow.CreateOrGetConnectionAsync(); var order=await Insert(store);
            order.OriginalDueOn=DateTime.UtcNow.Date; order.DueOn=order.OriginalDueOn.Value.AddDays(2); await store.WriteAsync(order); uow.CommitChanges();
            var runner=_runner.GetRequiredService<IMigrationRunner>();
            FluentActions.Invoking(()=>runner.MigrateDown(203)).Should().Throw<Exception>();
            _runner.GetRequiredService<IVersionLoader>().LoadVersionInfo(); runner.MigrateUp();
            var retained=await store.GetAsync<WorkOrder>(77,order.Id);
            retained.OriginalDueOn.Should().Be(order.OriginalDueOn); retained.DueOn.Should().Be(order.DueOn);
        }
        [Test, Order(10)]
        public async Task Maintenance_schema_stores_every_model_column_and_catalog_versions_are_separate()
        {
            await using var db = Connect(_connection);
            foreach (var pair in WorkOrderTables.All)
            {
                var columns = (await db.QueryAsync<string>("SELECT LOWER(column_name) FROM information_schema.columns WHERE LOWER(table_name)=@table", new { table = pair.Value.ToLowerInvariant() })).ToHashSet();
                columns.Should().Contain(pair.Key.GetProperties().Where(p => p.CanWrite && !Attribute.IsDefined(p, typeof(NotMappedAttribute))).Select(p => p.Name.ToLowerInvariant()), pair.Value);
            }
            var catalog = new ProtectedFieldCatalog();
            catalog.GetForTable("WorkOrderSafetyHolds").Single().AddedInCatalogVersion.Should().Be(23);
            foreach (var table in new[] { "WorkOrderRecurrences", "WorkOrderRecurrenceVersions", "WorkOrderMeterReadings", "WorkOrderRecurrenceChanges" })
                catalog.GetForTable(table).Single().AddedInCatalogVersion.Should().Be(24);
            catalog.GetForTable("WorkOrders").Should().OnlyContain(f => f.AddedInCatalogVersion == 18);
        }
        [Test, Order(10)]
        public async Task Maintenance_state_lock_serializes_an_ordinary_unit_state_insert_until_commit()
        {
            using var owner = new UnitOfWork(Connections()); var repository = Orders(owner);
            await owner.CreateOrGetConnectionAsync();
            await repository.AppendUnitStateAsync(77, 10, (int)UnitStateTypes.Available, DateTime.UtcNow.AddMinutes(-1));
            owner.CommitChanges();
            await owner.CreateOrGetConnectionAsync(); var before = await repository.LatestUnitStateAsync(77, 10); before.Should().NotBeNull();
            await using var dispatch = Connect(_connection); await dispatch.OpenAsync();
            // This is an ordinary dispatch INSERT: it does not call the maintenance lock.
            var writer = dispatch.ExecuteAsync($"INSERT INTO {Q("UnitStates")} ({Q("UnitId")},{Q("State")},{Q("Timestamp")},{Q("IsProtected")}) VALUES(10,2,@now,@protect)", new { now = DateTime.UtcNow.AddMinutes(1), protect = false }, commandTimeout: 10);
            await Task.Delay(200); writer.IsCompleted.Should().BeFalse("the immediate PostgreSQL FK / SQL Server range lock must protect restoration from a concurrent ordinary dispatch insert");
            await repository.AppendUnitStateAsync(77, 10, (int)UnitStateTypes.OutOfService, DateTime.UtcNow);
            owner.CommitChanges(); (await writer).Should().Be(1);
            await owner.CreateOrGetConnectionAsync();
            (await repository.LatestUnitStateAsync(77,10)).State.Should().Be((int)UnitStateTypes.Unavailable);
            owner.DiscardChanges();
        }
        [Test, Order(10)]
        public async Task Failed_source_identity_is_unique_and_inventory_part_backlink_is_tenant_bound()
        {
            using var owner = new UnitOfWork(Connections()); var repository = Orders(owner);
            await owner.CreateOrGetConnectionAsync(); var order = await Insert(repository);
            var intent = new WorkOrderFailureIntent { DepartmentId = 77, CompletionId = Guid.NewGuid().ToString("D"), ItemId = Guid.NewGuid().ToString("D"), CreatedBy = "actor", CreatedOn = DateTime.UtcNow, UpdatedOn = DateTime.UtcNow, WorkOrderId = order.Id };
            await repository.AllocateAsync(intent);
            var part = new WorkOrderPart { DepartmentId=77, WorkOrderId=order.Id, CreatedBy="actor", CreatedOn=DateTime.UtcNow, UpdatedOn=DateTime.UtcNow };
            await repository.AllocateAsync(part); owner.CommitChanges();
            await owner.CreateOrGetConnectionAsync(); intent.Id=0;
            await FluentActions.Awaiting(() => repository.AllocateAsync(intent)).Should().ThrowAsync<DbException>(); owner.DiscardChanges();
            await using var db = Connect(_connection);
            await FluentActions.Awaiting(() => db.ExecuteAsync($"INSERT INTO {Q("InventoryTransactions")} ({Q("Id")},{Q("DepartmentId")},{Q("WorkOrderPartId")}) VALUES(@id,88,@part)",new {id=Guid.NewGuid().ToString("D"),part=part.Id})).Should().ThrowAsync<DbException>();
            (await repository.QueryMaintenanceAsync<WorkOrderFailureIntent>(88)).Should().BeEmpty();
        }
        [Test, Order(10)]
        public async Task Late_escalation_candidates_are_filtered_before_the_500_row_page()
        {
            using var owner=new UnitOfWork(Connections()); var repository=Orders(owner); await owner.CreateOrGetConnectionAsync();
            for(var i=0;i<501;i++) { var order=await Insert(repository); order.DueOn=DateTime.UtcNow.AddMinutes(-10); order.EscalateAfterMinutes=60; await repository.WriteAsync(order); }
            var ready=await Insert(repository); ready.DueOn=DateTime.UtcNow.AddMinutes(-1); await repository.WriteAsync(ready); owner.CommitChanges();
            (await repository.OverdueAsync(77,DateTime.UtcNow)).Should().Contain(o=>o.Id==ready.Id);
        }
    }
}
