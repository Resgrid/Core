using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Inventories;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Services
{
    public sealed partial class InventoryModernizationService
    {
        public async Task<WorkOrderPartQuote> QuotePartAsync(InventoryActor actor, int orderId, WorkOrderPartInput input)
        {
            if (_uow.Transaction == null || _maintenanceOrders == null || _workOrders == null) throw new InvalidOperationException("Reservations require their source transaction.");
            await _store.LockDepartmentAsync(actor.DepartmentId);
            if (!await _store.HasLegacyMigrationAsync(actor.DepartmentId) || !await _auth.IsEnabledAsync(actor.DepartmentId)) throw new InventoryException(409, "InventoryDisabled");
            var command = new InventoryCommand { RequestId = input.RequestId, Lines = new() { new InventoryPosting { ItemId = input.InventoryItemId, AssetId = input.InventoryAssetId, LotId = input.InventoryLotId, FromLocationId = input.InventoryLocationId, Quantity = input.Content.Quantity, Type = InventoryTransactionType.Consume, ReferenceType = InventoryReferenceType.WorkOrder, ReferenceId = orderId.ToString(CultureInfo.InvariantCulture) } } };
            await RequireCommandAccessAsync(actor, command, joined: true); await ValidateCommandAsync(actor, command, joined: true);
            var line = command.Lines[0]; var item = await GetAsync<InventoryItem>(actor, line.ItemId);
            var cost = await CostForPostingAsync(actor, item, line);
            var allocations = await AllocatedQuantityAsync(actor.DepartmentId, line);
            if (line.AssetId == null)
            {
                var stocks = await _store.RelatedAsync<InventoryStock>(actor.DepartmentId, "ItemId", item.Id);
                var available = stocks.Where(s => s.LocationId == line.FromLocationId && s.LotId == line.LotId).Sum(s => s.Quantity);
                if (available - allocations < line.Quantity) throw new InventoryException(409, "InsufficientStock");
            }
            else
            {
                var asset = await GetAsync<InventoryAsset>(actor, line.AssetId);
                if (allocations > 0 || asset.Status != (int)InventoryAssetStatus.InService || asset.CurrentLocationId != line.FromLocationId || asset.LotId != line.LotId || asset.ExpiresOn <= Now) throw new InventoryException(409, "AssetNotAvailable");
            }
            return new WorkOrderPartQuote { UnitCost = cost.UnitCost, Currency = cost.CurrencyCode };
        }
        private async Task<decimal> AllocatedQuantityAsync(int departmentId, InventoryPosting line)
        {
            if (_maintenanceOrders == null || line.FromLocationId == null) return 0;
            var allocations = await _maintenanceOrders.AllocatedPartsAsync(departmentId, line.ItemId, line.FromLocationId, line.LotId, line.AssetId);
            var total = allocations.Sum(p => (p.ReservedLocationId == line.FromLocationId ? p.ReservedQuantity : 0) + (p.IssuedLocationId == line.FromLocationId ? p.IssuedQuantity : 0));
            if (line.WorkOrderPartMovementId.HasValue)
            {
                await ValidatePartMovementAsync(departmentId, line);
                total -= line.Quantity; // Only the validated source movement may use its own allocation.
            }
            return Math.Max(0, total);
        }
        private async Task ValidatePartMovementAsync(int departmentId, InventoryPosting line)
        {
            if (!line.WorkOrderPartId.HasValue || !line.WorkOrderPartMovementId.HasValue || line.ReversesTransactionId != null) throw new InventoryException(409, "WorkOrderPostingRequired");
            var part = await _workOrders.GetAsync<WorkOrderPart>(departmentId, line.WorkOrderPartId.Value);
            var movement = await _workOrders.GetAsync<WorkOrderPartMovement>(departmentId, line.WorkOrderPartMovementId.Value);
            if (part == null || !part.Staged || part.VoidedOn.HasValue || movement?.PartId != part.Id || movement.WorkOrderId != part.WorkOrderId || movement.Cancelled || movement.InventoryTransactionId != null
                || movement.Quantity != line.Quantity || movement.FromLocationId != line.FromLocationId || movement.ToLocationId != line.ToLocationId || line.AssetId != part.ReservedAssetId || line.LotId != part.ReservedLotId) throw new InventoryException(409, "ReferenceUnavailable");
            var kind = (WorkOrderPartMovementKind)movement.Kind;
            if (kind == WorkOrderPartMovementKind.Issue)
            {
                if (line.Type != InventoryTransactionType.Transfer || line.FromLocationId != part.ReservedLocationId || line.Quantity > part.ReservedQuantity || part.IssuedQuantity > 0 && part.IssuedLocationId != line.ToLocationId) throw new InventoryException(409, "ReferenceUnavailable");
            }
            else if (kind is WorkOrderPartMovementKind.Consume or WorkOrderPartMovementKind.ReturnUnused)
            {
                if (line.FromLocationId != part.IssuedLocationId || line.Quantity > part.IssuedQuantity || line.Type != (kind == WorkOrderPartMovementKind.Consume ? InventoryTransactionType.Consume : InventoryTransactionType.Transfer)
                    || kind == WorkOrderPartMovementKind.ReturnUnused && line.ToLocationId != part.ReservedLocationId) throw new InventoryException(409, "ReferenceUnavailable");
            }
            else throw new InventoryException(409, "ReferenceUnavailable");
        }
    }
}
