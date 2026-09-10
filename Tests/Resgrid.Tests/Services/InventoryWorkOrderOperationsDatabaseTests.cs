using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Inventories;
using Resgrid.Model.WorkOrders;
using Resgrid.Repositories.DataRepository.Transactions;

namespace Resgrid.Tests.Services
{
    public partial class InventoryDatabaseTests
    {
        [TestCase(false), TestCase(true)]
        public async Task Operations_reserved_parts_issue_consume_return_and_release_exact_balances(bool controlled)
        {
            await MarkMigratedAsync(); using var uow=new UnitOfWork(Connections()); var pair=MaintenancePair(uow);
            var actor=new InventoryActor {DepartmentId=77,UserId="inventory-test-author"};
            var witness=new InventoryActor {DepartmentId=77,UserId="inventory-test-other"};
            var principal=new ChecklistActor {DepartmentId=77,UserId=actor.UserId};
            var item=await pair.Inventory.SaveItemAsync(actor,new() {IsControlledSubstance=controlled,Details=new() {Name="Synthetic reserved component",UnitOfMeasure="each",CurrencyCode="USD"}});
            var source=await pair.Inventory.SaveLocationAsync(actor,new() {Name="Synthetic store",Type=InventoryLocationType.Facility});
            var destination=await pair.Inventory.SaveLocationAsync(actor,new() {Name="Synthetic workshop",Type=InventoryLocationType.Facility});
            var receipt=new InventoryCommand {RequestId=Guid.NewGuid().ToString("D"),Lines=new() {new() {ItemId=item.Id,ToLocationId=source.Id,Quantity=10,UnitCost=4.25m,Type=InventoryTransactionType.Receive}}};
            await pair.Inventory.PostTransactionAsync(actor,receipt); if(controlled) await pair.Inventory.WitnessAsync(witness,receipt.RequestId,"Receipt verified");
            var order=await pair.Orders.CreateAsync(principal,new() {RequestId=Guid.NewGuid().ToString("D"),Content=new() {Title="Synthetic readiness repair"}});
            await pair.Orders.TransitionAsync(principal,order.Order.Id,new() {Revision=order.Order.Revision,Status=WorkOrderStatus.Accepted});
            order=await pair.Orders.GetAsync(principal,order.Order.Id);
            var reserve=new WorkOrderPartInput {Revision=order.Order.Revision,RequestId=Guid.NewGuid().ToString("D"),InventoryItemId=item.Id,InventoryLocationId=source.Id,Content=new() {Description="Synthetic component",Quantity=6,UnitCost=999}};
            await pair.Orders.ReservePartAsync(principal,order.Order.Id,reserve); await pair.Orders.ReservePartAsync(principal,order.Order.Id,reserve);
            order=await pair.Orders.GetAsync(principal,order.Order.Id); var part=order.Parts.Single(); part.Staged.Should().BeTrue(); part.ReservedQuantity.Should().Be(6); part.Content.UnitCost.Should().Be(4.25m);
            var steal=new InventoryCommand {RequestId=Guid.NewGuid().ToString("D"),Lines=new() {new() {ItemId=item.Id,FromLocationId=source.Id,Quantity=5,Type=InventoryTransactionType.Consume}}};
            if(controlled) { await pair.Inventory.PostTransactionAsync(actor,steal); await FluentActions.Awaiting(()=>pair.Inventory.WitnessAsync(witness,steal.RequestId,"Independent but reserved")).Should().ThrowAsync<InventoryException>(); }
            else await FluentActions.Awaiting(()=>pair.Inventory.PostTransactionAsync(actor,steal)).Should().ThrowAsync<InventoryException>();
            (await Store(uow).ListAsync<InventoryStock>(77)).Single().Quantity.Should().Be(10);
            await FluentActions.Awaiting(()=>pair.Orders.TransitionAsync(principal,order.Order.Id,new() {Revision=order.Order.Revision,Status=WorkOrderStatus.Cancelled,Reason="Must settle parts first"})).Should().ThrowAsync<WorkOrderException>();
            async Task Move(WorkOrderPartMovementKind kind,decimal quantity)
            {
                var current=await pair.Orders.GetAsync(principal,order.Order.Id);
                var input=new WorkOrderPartMovementInput {Revision=current.Order.Revision,RequestId=Guid.NewGuid().ToString("D"),Kind=kind,Quantity=quantity,LocationId=kind==WorkOrderPartMovementKind.Issue?destination.Id:null,Reason="Synthetic movement"};
                await pair.Orders.MovePartAsync(principal,order.Order.Id,part.Id,input);
                if(controlled && kind!=WorkOrderPartMovementKind.ReleaseReservation)
                {
                    await FluentActions.Awaiting(()=>pair.Inventory.WitnessAsync(actor,input.RequestId,"Self witness")).Should().ThrowAsync<InventoryException>();
                    await pair.Inventory.WitnessAsync(witness,input.RequestId,"Independent movement");
                }
                await pair.Orders.MovePartAsync(principal,order.Order.Id,part.Id,input);
            }
            await Move(WorkOrderPartMovementKind.Issue,4);
            part=(await pair.Orders.GetAsync(principal,order.Order.Id)).Parts.Single(); part.ReservedQuantity.Should().Be(2); part.IssuedQuantity.Should().Be(4);
            await Move(WorkOrderPartMovementKind.Consume,1.5m); await Move(WorkOrderPartMovementKind.ReturnUnused,2.5m); await Move(WorkOrderPartMovementKind.ReleaseReservation,2);
            order=await pair.Orders.GetAsync(principal,order.Order.Id); part=order.Parts.Single(); part.ReservedQuantity.Should().Be(0); part.IssuedQuantity.Should().Be(0); part.ConsumedQuantity.Should().Be(1.5m); part.ReturnedQuantity.Should().Be(2.5m); part.Content.ConsumedCost.Should().Be(6.375m);
            var stocks=await Store(uow).ListAsync<InventoryStock>(77); stocks.Single(s=>s.LocationId==source.Id).Quantity.Should().Be(8.5m); stocks.Single(s=>s.LocationId==destination.Id).Quantity.Should().Be(0);
            var ledger=await Store(uow).ListAsync<InventoryTransaction>(77); ledger.Where(t=>t.WorkOrderPartMovementId.HasValue).Should().HaveCount(3);
            (await pair.Orders.PartMovementsAsync(principal,order.Order.Id)).Should().HaveCount(5);
            await pair.Orders.TransitionAsync(principal,order.Order.Id,new() {Revision=order.Order.Revision,Status=WorkOrderStatus.Cancelled,Reason="Settled"});
        }
        [Test]
        public async Task Operations_serialized_reservation_prevents_transfer_and_unused_return_restores_source()
        {
            await MarkMigratedAsync(); using var uow=new UnitOfWork(Connections()); var pair=MaintenancePair(uow);
            var actor=new InventoryActor {DepartmentId=77,UserId="inventory-test-author"}; var principal=new ChecklistActor {DepartmentId=77,UserId=actor.UserId};
            var item=await pair.Inventory.SaveItemAsync(actor,new() {TrackingMode=InventoryTrackingMode.Serialized,Details=new() {Name="Synthetic spare",UnitOfMeasure="each"}});
            var source=await pair.Inventory.SaveLocationAsync(actor,new() {Name="Synthetic source",Type=InventoryLocationType.Facility});
            var target=await pair.Inventory.SaveLocationAsync(actor,new() {Name="Synthetic target",Type=InventoryLocationType.Facility});
            var asset=await pair.Inventory.CreateAssetAsync(actor,new() {RequestId=Guid.NewGuid().ToString("D"),ItemId=item.Id,LocationId=source.Id,Details=new() {SerialNumber="SYNTHETIC-RESERVED"}});
            var order=await pair.Orders.CreateAsync(principal,new() {RequestId=Guid.NewGuid().ToString("D"),Content=new() {Title="Synthetic repair"}});
            await pair.Orders.TransitionAsync(principal,order.Order.Id,new() {Revision=1,Status=WorkOrderStatus.Accepted}); order=await pair.Orders.GetAsync(principal,order.Order.Id);
            var input=new WorkOrderPartInput {RequestId=Guid.NewGuid().ToString("D"),Revision=order.Order.Revision,InventoryItemId=item.Id,InventoryLocationId=source.Id,InventoryAssetId=asset.Id,Content=new() {Description="Synthetic spare",Quantity=1}};
            await pair.Orders.ReservePartAsync(principal,order.Order.Id,input); order=await pair.Orders.GetAsync(principal,order.Order.Id); var part=order.Parts.Single();
            await FluentActions.Awaiting(()=>pair.Inventory.ChangeAssetStatusAsync(actor,new() {RequestId=Guid.NewGuid().ToString("D"),Lines=new() {new() {ItemId=item.Id,AssetId=asset.Id,FromLocationId=source.Id,Type=InventoryTransactionType.StatusChange,Status=InventoryAssetStatus.Retired,Note="Reserved"}}})).Should().ThrowAsync<InventoryException>();
            await pair.Orders.MovePartAsync(principal,order.Order.Id,part.Id,new() {Revision=order.Order.Revision,RequestId=Guid.NewGuid().ToString("D"),Kind=WorkOrderPartMovementKind.Issue,Quantity=1,LocationId=target.Id});
            (await pair.Inventory.GetAsync<InventoryAsset>(actor,asset.Id)).CurrentLocationId.Should().Be(target.Id);
            order=await pair.Orders.GetAsync(principal,order.Order.Id);
            await pair.Orders.MovePartAsync(principal,order.Order.Id,part.Id,new() {Revision=order.Order.Revision,RequestId=Guid.NewGuid().ToString("D"),Kind=WorkOrderPartMovementKind.ReturnUnused,Quantity=1,Reason="Unused"});
            (await pair.Inventory.GetAsync<InventoryAsset>(actor,asset.Id)).CurrentLocationId.Should().Be(source.Id);
        }
        [Test]
        public async Task Operations_cancelled_controlled_movement_keeps_reservation_and_rejects_stale_witness()
        {
            await MarkMigratedAsync(); using var uow=new UnitOfWork(Connections()); var pair=MaintenancePair(uow);
            var actor=new InventoryActor {DepartmentId=77,UserId="inventory-test-author"}; var witness=new InventoryActor {DepartmentId=77,UserId="inventory-test-other"}; var principal=new ChecklistActor {DepartmentId=77,UserId=actor.UserId};
            var item=await pair.Inventory.SaveItemAsync(actor,new() {IsControlledSubstance=true,Details=new() {Name="Synthetic controlled spare",UnitOfMeasure="each"}});
            var source=await pair.Inventory.SaveLocationAsync(actor,new() {Name="Synthetic store",Type=InventoryLocationType.Facility}); var target=await pair.Inventory.SaveLocationAsync(actor,new() {Name="Synthetic bench",Type=InventoryLocationType.Facility});
            var receive=new InventoryCommand {RequestId=Guid.NewGuid().ToString("D"),Lines=new() {new() {ItemId=item.Id,ToLocationId=source.Id,Quantity=2,Type=InventoryTransactionType.Receive}}};
            await pair.Inventory.PostTransactionAsync(actor,receive); await pair.Inventory.WitnessAsync(witness,receive.RequestId,"Receipt checked");
            var order=await pair.Orders.CreateAsync(principal,new() {RequestId=Guid.NewGuid().ToString("D"),Content=new() {Title="Synthetic order"}}); await pair.Orders.TransitionAsync(principal,order.Order.Id,new() {Revision=1,Status=WorkOrderStatus.Accepted}); order=await pair.Orders.GetAsync(principal,order.Order.Id);
            await pair.Orders.ReservePartAsync(principal,order.Order.Id,new() {Revision=order.Order.Revision,RequestId=Guid.NewGuid().ToString("D"),InventoryItemId=item.Id,InventoryLocationId=source.Id,Content=new() {Description="Synthetic reserved stock",Quantity=1}}); order=await pair.Orders.GetAsync(principal,order.Order.Id);
            var move=new WorkOrderPartMovementInput {Revision=order.Order.Revision,RequestId=Guid.NewGuid().ToString("D"),Kind=WorkOrderPartMovementKind.Issue,Quantity=1,LocationId=target.Id}; await pair.Orders.MovePartAsync(principal,order.Order.Id,order.Parts.Single().Id,move);
            var pending=(await pair.Orders.PartMovementsAsync(principal,order.Order.Id)).Single(m=>m.Kind==1); order=await pair.Orders.GetAsync(principal,order.Order.Id);
            await pair.Orders.CancelPartMovementAsync(principal,order.Order.Id,pending.Id,order.Order.Revision,"Not needed");
            await FluentActions.Awaiting(()=>pair.Inventory.WitnessAsync(witness,move.RequestId,"Stale approval")).Should().ThrowAsync<InventoryException>();
            order=await pair.Orders.GetAsync(principal,order.Order.Id); order.Parts.Single().ReservedQuantity.Should().Be(1); order.Parts.Single().IssuedQuantity.Should().Be(0);
            (await Store(uow).ListAsync<InventoryTransaction>(77)).Should().ContainSingle();
        }
    }
}
