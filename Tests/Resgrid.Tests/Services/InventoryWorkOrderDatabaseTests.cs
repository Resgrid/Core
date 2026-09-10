using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Inventories;
using Resgrid.Model.Repositories;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Model.WorkOrders;
using Resgrid.Repositories.DataRepository;
using Resgrid.Repositories.DataRepository.Transactions;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
    public partial class InventoryDatabaseTests
    {
        private (WorkOrdersService Orders, InventoryModernizationService Inventory, Mock<IReadinessAccessService> Access) MaintenancePair(UnitOfWork uow)
        {
            var orders = new WorkOrderRepository(Connections(), Configuration(), uow, Mock.Of<IQueryFactory>());
            var auth = new Mock<IWorkOrderAuthorizationService>(); auth.Setup(a=>a.ScopeAsync(It.IsAny<ChecklistActor>())).ReturnsAsync(new WorkOrderReadScope { All=true });
            auth.Setup(a=>a.CanManageAsync(It.IsAny<ChecklistActor>(),It.IsAny<int?>())).ReturnsAsync(true);
            auth.Setup(a=>a.CanContributeAsync(It.IsAny<ChecklistActor>(),It.IsAny<WorkOrder>())).ReturnsAsync(true);
            auth.Setup(a=>a.RecipientsAsync(It.IsAny<int>(),It.IsAny<WorkOrder>())).ReturnsAsync(new List<string>());
            var access = new Mock<IReadinessAccessService>(); access.Setup(a=>a.CanUseMaintenanceAsync(77)).ReturnsAsync(true);
            var invAuth = new Mock<IInventoryAuthorizationService>(); invAuth.Setup(a=>a.IsEnabledAsync(77)).ReturnsAsync(true); invAuth.Setup(a=>a.CanLocationAsync(It.IsAny<InventoryActor>(),It.IsAny<InventoryLocation>())).ReturnsAsync(true);
            var read=new Mock<IProtectedReadService>(); read.SetReturnsDefault(Task.FromResult(new ProtectedReadResult()));
            var write=new Mock<IProtectedWriteService>(); write.SetReturnsDefault(Task.FromResult(ProtectedWriteResult.Allowed()));
            var outbox=new Mock<IDomainEventOutboxService>(); long sequence=0;
            outbox.Setup(o=>o.EnqueueAsync(77,It.IsAny<string>(),It.IsAny<DomainEventEnvelope>(),It.IsAny<CancellationToken>())).ReturnsAsync(()=> { uow.Transaction.Should().NotBeNull(); return new DomainEventOutboxEntry {DomainEventOutboxId=++sequence}; });
            outbox.Setup(o=>o.DispatchAfterCommitAsync(It.IsAny<IEnumerable<long>>(),It.IsAny<CancellationToken>())).ReturnsAsync(0);
            var audit=new Mock<IAuditLogsRepository>(); audit.Setup(a=>a.InsertAsync(It.IsAny<AuditLog>(),It.IsAny<CancellationToken>(),It.IsAny<bool>())).ReturnsAsync((AuditLog row,CancellationToken c,bool b)=> {row.AuditLogId=1;return row;});
            WorkOrdersService work=null;
            var inventory = new InventoryModernizationService(Store(uow),invAuth.Object,uow,read.Object,write.Object,outbox.Object,audit.Object,Mock.Of<IUnitsService>(),Mock.Of<IDepartmentGroupsService>(),
                workOrders:orders,workOrderAuthorization:new(()=>auth.Object),maintenanceOrders:orders,workOrderMaintenance:new(()=>work),readinessAccess:access.Object);
            work=new WorkOrdersService(orders,auth.Object,access.Object,uow,audit.Object,outbox.Object,new(()=>read.Object),new(()=>write.Object),Mock.Of<IRecordAttachmentScanner>(),
                maintenance:orders,inventoryMaintenance:new(()=>inventory),inventoryCatalog:new(()=>inventory));
            return (work,inventory,access);
        }
        [TestCase(false), TestCase(true)]
        public async Task Asset_holds_require_separate_release_preserve_other_holds_and_never_revive_retirement(bool retire)
        {
            await MarkMigratedAsync(); using var uow=new UnitOfWork(Connections()); var pair=MaintenancePair(uow);
            var actor=new InventoryActor {DepartmentId=77,UserId="inventory-test-author"};
            var principal=new ChecklistActor {DepartmentId=77,UserId=actor.UserId};
            var reviewer=new ChecklistActor {DepartmentId=77,UserId="inventory-test-other"};
            var item=await pair.Inventory.SaveItemAsync(actor,new() {TrackingMode=InventoryTrackingMode.Serialized,Details=new() {Name="Synthetic apparatus",UnitOfMeasure="each"}});
            var location=await pair.Inventory.SaveLocationAsync(actor,new() {Name="Synthetic station",Type=InventoryLocationType.Facility});
            var asset=await pair.Inventory.CreateAssetAsync(actor,new() {RequestId=Guid.NewGuid().ToString("D"),ItemId=item.Id,LocationId=location.Id,Details=new() {SerialNumber="SYNTHETIC-001"}});
            var holds=new List<WorkOrderHoldView>();
            for (var i=0;i<2;i++)
            {
                var order=await pair.Orders.CreateAsync(principal,new() {RequestId=Guid.NewGuid().ToString("D"),InventoryAssetId=asset.Id,Content=new() {Title="Independent defect "+i}});
                await pair.Orders.AddHoldAsync(principal,order.Order.Id,new() {Revision=1,Asset=true,Reason="Inspection defect"});
                holds.Add((await pair.Orders.HoldsAsync(principal,order.Order.Id)).Single());
            }
            var release=new WorkOrderReleaseInput {Revision=1,RestoreState=true,Evidence="Independent function test",Qualification="Qualified maintenance reviewer"};
            await FluentActions.Awaiting(()=>pair.Orders.ReleaseHoldAsync(principal,holds[0].Hold.Id,release)).Should().ThrowAsync<WorkOrderException>();
            await pair.Orders.ReleaseHoldAsync(reviewer,holds[0].Hold.Id,release);
            asset=await pair.Inventory.GetAsync<InventoryAsset>(actor,asset.Id); asset.Status.Should().Be((int)InventoryAssetStatus.OutForRepair);
            var status=new InventoryCommand {RequestId=Guid.NewGuid().ToString("D"),Lines=new() {new() {ItemId=item.Id,AssetId=asset.Id,FromLocationId=location.Id,ExpectedAssetRevision=asset.Revision,Type=InventoryTransactionType.StatusChange,Status=InventoryAssetStatus.InService,Note="Unauthorized bypass"}}};
            await FluentActions.Awaiting(()=>pair.Inventory.ChangeAssetStatusAsync(actor,status)).Should().ThrowAsync<InventoryException>();
            if(retire) { status.RequestId=Guid.NewGuid().ToString("D");status.Lines[0].Status=InventoryAssetStatus.Retired;status.Lines[0].Note="Asset decommissioned";await pair.Inventory.ChangeAssetStatusAsync(actor,status); }
            pair.Access.Setup(a=>a.CanUseMaintenanceAsync(77)).ReturnsAsync(false);
            await pair.Orders.ReleaseHoldAsync(reviewer,holds[1].Hold.Id,release);
            (await pair.Inventory.GetAsync<InventoryAsset>(actor,asset.Id)).Status.Should().Be((int)(retire ? InventoryAssetStatus.Retired : InventoryAssetStatus.InService));
            (await pair.Orders.HoldsAsync(reviewer,holds[1].Hold.WorkOrderId.Value)).Single().Hold.StateRestored.Should().Be(!retire);
        }
        [Test]
        public async Task Cancelling_an_unposted_controlled_part_retains_evidence_and_cannot_move_stock()
        {
            await MarkMigratedAsync(); using var uow = new UnitOfWork(Connections()); var pair = MaintenancePair(uow);
            var actor = new InventoryActor { DepartmentId=77, UserId="inventory-test-author" };
            var witness = new InventoryActor { DepartmentId=77, UserId="inventory-test-other" };
            var principal = new ChecklistActor { DepartmentId=77, UserId=actor.UserId };
            var item = await pair.Inventory.SaveItemAsync(actor, new() { IsControlledSubstance=true, Details=new() { Name="Synthetic controlled part", UnitOfMeasure="each" } });
            var location = await pair.Inventory.SaveLocationAsync(actor, new() { Name="Synthetic store", Type=InventoryLocationType.Facility });
            var receive = new InventoryCommand { RequestId=Guid.NewGuid().ToString("D"), Lines=new() { new() { ItemId=item.Id, ToLocationId=location.Id, Quantity=10, Type=InventoryTransactionType.Receive } } };
            await pair.Inventory.PostTransactionAsync(actor, receive); await pair.Inventory.WitnessAsync(witness, receive.RequestId, "Independent receipt");
            var order = await pair.Orders.CreateAsync(principal, new() { RequestId=Guid.NewGuid().ToString("D"), Content=new() { Title="Synthetic repair" } });
            await pair.Orders.TransitionAsync(principal, order.Order.Id, new() { Revision=1, Status=WorkOrderStatus.Accepted });
            order = await pair.Orders.GetAsync(principal, order.Order.Id);
            var input = new WorkOrderPartInput { Revision=order.Order.Revision, RequestId=Guid.NewGuid().ToString("D"), InventoryItemId=item.Id, InventoryLocationId=location.Id, Content=new() { Description="Unused proposal", Quantity=2 } };
            await pair.Orders.AddPartAsync(principal, order.Order.Id, input); order = await pair.Orders.GetAsync(principal, order.Order.Id);
            var part = order.Parts.Single(); part.InventoryWitnessRequestId.Should().Be(input.RequestId);
            await pair.Orders.CancelPartWitnessAsync(principal, order.Order.Id, part.Id, order.Order.Revision, "Part no longer needed");
            await FluentActions.Awaiting(()=>pair.Inventory.WitnessAsync(witness,input.RequestId,"Stale witness")).Should().ThrowAsync<InventoryException>();
            await pair.Orders.AddPartAsync(principal, order.Order.Id, input);
            part=(await pair.Orders.GetAsync(principal, order.Order.Id)).Parts.Single(); part.VoidedOn.Should().NotBeNull(); part.InventoryTransactionId.Should().BeNull(); part.AwaitingWitness.Should().BeFalse();
            (await Store(uow).ListAsync<InventoryStock>(77,0)).Single().Quantity.Should().Be(10);
            (await Store(uow).ListAsync<InventoryTransaction>(77,0)).Should().HaveCount(1);
        }
        [TestCase(false, false), TestCase(true, false), TestCase(true, true)]
        public async Task Linked_parts_and_reversals_post_once_with_authoritative_cost_and_independent_witness(bool controlled, bool cancel)
        {
            await MarkMigratedAsync(); using var uow=new UnitOfWork(Connections()); var pair=MaintenancePair(uow);
            var actor=new InventoryActor {DepartmentId=77,UserId="inventory-test-author"};
            var witness=new InventoryActor {DepartmentId=77,UserId="inventory-test-other"};
            var principal=new ChecklistActor {DepartmentId=77,UserId=actor.UserId};
            var item=await pair.Inventory.SaveItemAsync(actor,new() { IsControlledSubstance=controlled, Details=new() {Name="Synthetic parts",UnitOfMeasure="each",CurrencyCode="EUR"} });
            var location=await pair.Inventory.SaveLocationAsync(actor,new() {Name="Synthetic store",Type=InventoryLocationType.Facility});
            var receive=new InventoryCommand {RequestId=Guid.NewGuid().ToString("D"),Lines=new() {new() {ItemId=item.Id,ToLocationId=location.Id,Quantity=10,UnitCost=4.25m,Type=InventoryTransactionType.Receive}}};
            var receipt=await pair.Inventory.PostTransactionAsync(actor,receive);
            if(receipt.AwaitingWitness) await pair.Inventory.WitnessAsync(witness,receive.RequestId,"Independent receipt");
            var order=await pair.Orders.CreateAsync(principal,new() {RequestId=Guid.NewGuid().ToString("D"),Content=new() {Title="Synthetic repair"}});
            await pair.Orders.TransitionAsync(principal,order.Order.Id,new() {Revision=1,Status=WorkOrderStatus.Accepted});
            order=await pair.Orders.GetAsync(principal,order.Order.Id);
            var input=new WorkOrderPartInput {Revision=order.Order.Revision,RequestId=Guid.NewGuid().ToString("D"),InventoryItemId=item.Id,InventoryLocationId=location.Id,Content=new() {Description="Synthetic installed part",Quantity=2.123456m,UnitCost=1m}};
            if (!controlled)
            {
                input.Content.Quantity = 11;
                await FluentActions.Awaiting(()=>pair.Orders.AddPartAsync(principal,order.Order.Id,input)).Should().ThrowAsync<WorkOrderException>();
                (await pair.Orders.GetAsync(principal,order.Order.Id)).Parts.Should().BeEmpty("stock failure rolls back the source row too");
                (await Store(uow).ListAsync<InventoryTransaction>(77,0)).Should().HaveCount(1);
                input.Content.Quantity = 2.123456m;
            }
            await pair.Orders.AddPartAsync(principal,order.Order.Id,input);
            await pair.Orders.AddPartAsync(principal,order.Order.Id,input);
            var part=(await pair.Orders.GetAsync(principal,order.Order.Id)).Parts.Single();
            if(controlled)
            {
                part.AwaitingWitness.Should().BeTrue();
                await FluentActions.Awaiting(()=>pair.Inventory.WitnessAsync(actor,input.RequestId,"Same actor denied")).Should().ThrowAsync<InventoryException>();
                pair.Access.Setup(a=>a.CanUseMaintenanceAsync(77)).ReturnsAsync(false);
                await FluentActions.Awaiting(()=>pair.Inventory.WitnessAsync(witness,input.RequestId,"Expired addon denied")).Should().ThrowAsync<InventoryException>();
                pair.Access.Setup(a=>a.CanUseMaintenanceAsync(77)).ReturnsAsync(true);
                await pair.Inventory.WitnessAsync(witness,input.RequestId,"Independent part consumption");
            }
            order=await pair.Orders.GetAsync(principal,order.Order.Id); part=order.Parts.Single(); part.Content.UnitCost.Should().Be(4.25m); part.Content.Currency.Should().Be("EUR"); part.AwaitingWitness.Should().BeFalse();
            var ledger=await pair.Inventory.GetAsync<InventoryTransaction>(actor,part.InventoryTransactionId); ledger.WorkOrderPartId.Should().Be(part.Id); ledger.ReferenceId.Should().Be(order.Order.Id.ToString());
            var stock=(await Store(uow).ListAsync<InventoryStock>(77,0)).Single(); stock.Quantity.Should().Be(7.876544m);
            await pair.Orders.VoidPartAsync(principal,order.Order.Id,part.Id,order.Order.Revision,"Unused");
            var operation= (await pair.Orders.GetAsync(principal,order.Order.Id)).Parts.Single();
            if(controlled)
            {
                operation.AwaitingWitness.Should().BeTrue();
                var pending=await pair.Inventory.GetAsync<InventoryOperation>(actor,operation.InventoryOperationId);
                operation.InventoryWitnessRequestId.Should().Be(pending.RequestId);
                if (cancel)
                {
                    order=await pair.Orders.GetAsync(principal,order.Order.Id);
                    await pair.Orders.CancelPartWitnessAsync(principal,order.Order.Id,part.Id,order.Order.Revision,"Keep installed");
                    await FluentActions.Awaiting(()=>pair.Inventory.WitnessAsync(witness,pending.RequestId,"Cancelled reversal")).Should().ThrowAsync<InventoryException>();
                    order=await pair.Orders.GetAsync(principal,order.Order.Id);
                    order.Parts.Single().AwaitingWitness.Should().BeFalse();
                    (await Store(uow).ListAsync<InventoryStock>(77,0)).Single().Quantity.Should().Be(7.876544m);
                    await pair.Orders.VoidPartAsync(principal,order.Order.Id,part.Id,order.Order.Revision,"New independent reversal");
                    operation=(await pair.Orders.GetAsync(principal,order.Order.Id)).Parts.Single();
                    var retried=await pair.Inventory.GetAsync<InventoryOperation>(actor,operation.InventoryOperationId);
                    retried.RequestId.Should().NotBe(pending.RequestId);
                    pending=retried;
                }
                await pair.Inventory.WitnessAsync(witness,pending.RequestId,"Independent reversal");
            }
            (await pair.Orders.GetAsync(principal,order.Order.Id)).Parts.Single().VoidedOn.Should().NotBeNull();
            (await Store(uow).ListAsync<InventoryStock>(77,0)).Single().Quantity.Should().Be(10m);
            (await Store(uow).ListAsync<InventoryTransaction>(77,0)).Count(t=>t.WorkOrderPartId==part.Id).Should().Be(2);
        }
    }
}
