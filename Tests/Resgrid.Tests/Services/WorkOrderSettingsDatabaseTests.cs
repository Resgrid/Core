using System;
using System.Threading.Tasks;
using FluentAssertions;
using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Resgrid.Model.WorkOrders;
using Resgrid.Repositories.DataRepository.Transactions;

namespace Resgrid.Tests.Services
{
    public partial class WorkOrderDatabaseTests
    {
        [Test, Order(10)]
        public async Task Department_currency_and_order_snapshot_round_trip_and_prevent_destructive_rollback()
        {
            using var uow = new UnitOfWork(Connections()); var store = Orders(uow);
            await uow.CreateOrGetConnectionAsync();
            var order = await Insert(store); var originalContent = order.Content;
            order.CurrencyCode = "CAD"; await store.WriteAsync(order);
            var policy = new WorkOrderPolicy { DepartmentId = 77, CreatedBy = "manager", CreatedOn = DateTime.UtcNow,
                UpdatedOn = DateTime.UtcNow, CurrencyCode = "EUR", Content = "{\"SpendingThreshold\":125}", CalendarJson = "{}" };
            await store.AllocateAsync(policy); await store.WriteAsync(policy); uow.CommitChanges();
            var loaded = await store.GetAsync<WorkOrder>(77, order.Id);
            loaded.CurrencyCode.Should().Be("CAD"); loaded.Content.Should().Be(originalContent);
            (await store.GetAsync<WorkOrderPolicy>(77, policy.Id)).CurrencyCode.Should().Be("EUR");
            (await store.GetAsync<WorkOrderPolicy>(88, policy.Id)).Should().BeNull();
            var runner = _runner.GetRequiredService<IMigrationRunner>();
            FluentActions.Invoking(() => runner.MigrateDown(225)).Should().Throw<Exception>();
            _runner.GetRequiredService<IVersionLoader>().LoadVersionInfo(); runner.MigrateUp();
            (await store.GetAsync<WorkOrder>(77, order.Id)).CurrencyCode.Should().Be("CAD");
            // Leave the shared fixture's other rollback checks independent of this test's metadata.
            await uow.CreateOrGetConnectionAsync(); order.CurrencyCode = null; policy.CurrencyCode = null;
            await store.WriteAsync(order); await store.WriteAsync(policy); uow.CommitChanges();
        }
    }
}
