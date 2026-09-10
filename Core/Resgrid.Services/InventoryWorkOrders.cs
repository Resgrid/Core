using System;
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
        public async Task CancelPendingPartAsync(InventoryActor actor, int partId, string operationId)
        {
            if (_uow.Transaction == null) throw new InvalidOperationException("The source transaction owns cancellation.");
            await _store.LockDepartmentAsync(actor.DepartmentId);
            var operation = await GetAsync<InventoryOperation>(actor, operationId);
            var receipt = Decode<InventoryOperationContent>(operation);
            if (operation.State != 1 || receipt.PendingKind != "WorkOrder" || receipt.PendingCommand?.Lines?.Count != 1 || receipt.PendingCommand.Lines[0].WorkOrderPartId != partId)
                throw new InventoryException(409, "ReferenceUnavailable");
            await RequireWorkOrderPartAsync(actor, receipt.PendingCommand.Lines[0]);
            await RequireCommandAccessAsync(actor, receipt.PendingCommand, joined: true);
            operation.State = 3; await SaveAsync(actor, operation, false);
        }
        public async Task<InventoryResult> PostPartAsync(InventoryActor actor, int partId, InventoryCommand command)
        {
            if (_uow.Transaction == null || _workOrders == null || _maintenanceOrders == null || _readinessAccess == null) throw new InvalidOperationException("Work-order inventory requires its source transaction.");
            await _store.LockDepartmentAsync(actor.DepartmentId);
            if (!await _store.HasLegacyMigrationAsync(actor.DepartmentId) || !await _auth.IsEnabledAsync(actor.DepartmentId)) throw new InventoryException(409, "InventoryDisabled");
            if (!await _readinessAccess.CanUseMaintenanceAsync(actor.DepartmentId)) throw new InventoryException(402, "ReadinessProRequired");
            if (command?.Lines?.Count != 1 || command.Lines[0].WorkOrderPartId != partId) throw new InventoryException(400, "ReferenceUnavailable");
            var preflight = await _write.PreflightWriteAsync(actor.DepartmentId, actor.GrantToken, actor.UserId, false);
            if (preflight?.Success != true) throw new InventoryException(403, "ProtectedDataRequired");
            await RequireCommandAccessAsync(actor, command, joined: true);
            var events = new System.Collections.Generic.List<long>();
            return await OperationAsync(actor, command.RequestId, new { Kind = "WorkOrder", command.Lines }, async (operation, pending) =>
            {
                await ValidateCommandAsync(actor, command, joined: true);
                if (await RequiresWitnessAsync(actor, command.Lines.Select(l => l.ItemId))) return await AwaitWitnessAsync(actor, operation, "WorkOrder", command);
                return await PostLinesAsync(actor, operation, command, pending);
            }, events);
        }
        private async Task RequireWorkOrderPartAsync(InventoryActor actor, InventoryPosting line)
        {
            if (!line.WorkOrderPartId.HasValue || _workOrders == null || _workOrderAuthorization == null || _readinessAccess == null) throw new InventoryException(400, "ReferenceUnavailable");
            if (!await _readinessAccess.CanUseMaintenanceAsync(actor.DepartmentId)) throw new InventoryException(402, "ReadinessProRequired");
            var part = await _workOrders.GetAsync<WorkOrderPart>(actor.DepartmentId, line.WorkOrderPartId.Value);
            if (part?.WorkOrderId?.ToString(System.Globalization.CultureInfo.InvariantCulture) != line.ReferenceId || part.InventoryItemId != line.ItemId || part.VoidedOn.HasValue) throw new InventoryException(409, "ReferenceUnavailable");
            if (part.Staged) await ValidatePartMovementAsync(actor.DepartmentId, line);
            else if (line.WorkOrderPartMovementId.HasValue || line.ReversesTransactionId == null && part.InventoryTransactionId != null || line.ReversesTransactionId != null && part.InventoryTransactionId != line.ReversesTransactionId) throw new InventoryException(409, "ReferenceUnavailable");
            var order = await _workOrders.GetAsync<WorkOrder>(actor.DepartmentId, part.WorkOrderId.Value);
            var principal = new ChecklistActor { DepartmentId = actor.DepartmentId, UserId = actor.UserId, GrantToken = actor.GrantToken };
            if (order == null || order.Status >= 5 || !await _workOrderAuthorization.Value.CanContributeAsync(principal, order)) throw new InventoryException(409, "ReferenceClosed");
        }
        public async Task<WorkOrderAssetState> ApplyHoldAsync(InventoryActor actor, int orderId, string assetId, int? restoreState = null, int? expectedRevision = null, bool safetyRelease = false)
        {
            if (_uow.Transaction == null || _maintenanceOrders == null || _workOrders == null || _workOrderAuthorization == null) throw new InvalidOperationException("Safety changes require the work-order transaction.");
            await _store.LockDepartmentAsync(actor.DepartmentId);
            if (!await _store.HasLegacyMigrationAsync(actor.DepartmentId)) throw new InventoryException(409, "MigrationRequired");
            var asset = await _store.GetAsync<InventoryAsset>(actor.DepartmentId, assetId);
            if (asset == null || asset.IsDeleted || asset.Status is 4 or 5 or 6 || asset.CurrentLocationId == null) { if (safetyRelease) return null; throw new InventoryException(409, "AssetNotAvailable"); }
            var location = await LocationAsync(actor, asset.CurrentLocationId, write: !safetyRelease, permission: PermissionTypes.AdjustInventory);
            var item = await _store.GetAsync<InventoryItem>(actor.DepartmentId, asset.ItemId);
            if (item?.IsControlledSubstance == true) await _auth.RequireAsync(actor, !safetyRelease, PermissionTypes.ManageControlledSubstances);
            var order = await _workOrders.GetAsync<WorkOrder>(actor.DepartmentId, orderId);
            var principal = new ChecklistActor { DepartmentId = actor.DepartmentId, UserId = actor.UserId, GrantToken = actor.GrantToken };
            if (order?.InventoryAssetId != assetId || !await _workOrderAuthorization.Value.CanManageAsync(principal, order.TargetGroupId)) throw new InventoryException(403, "PermissionRequired");
            var old = asset.Status;
            if (safetyRelease)
            {
                if (!restoreState.HasValue || restoreState is < 0 or > 3 || asset.Revision != expectedRevision || asset.Status != (int)InventoryAssetStatus.OutForRepair) return null;
                var active = await _maintenanceOrders.ActiveHoldsAsync(actor.DepartmentId, null, assetId);
                if (active.Count != 1 || active[0].WorkOrderId != orderId) return null;
                var issuance = (await _store.RelatedAsync<InventoryIssuance>(actor.DepartmentId, "AssetId", assetId)).SingleOrDefault(i => i.Status is 0 or 2);
                if (restoreState == (int)InventoryAssetStatus.InService && issuance != null || restoreState == (int)InventoryAssetStatus.Issued && issuance == null) return null;
                asset.Status = restoreState.Value;
            }
            else
            {
                if (_readinessAccess == null || !await _readinessAccess.CanUseMaintenanceAsync(actor.DepartmentId)) throw new InventoryException(402, "ReadinessProRequired");
                if (asset.Status == (int)InventoryAssetStatus.OutForRepair) return new WorkOrderAssetState { State = old, Revision = asset.Revision };
                asset.Status = (int)InventoryAssetStatus.OutForRepair;
            }
            // Only reviewed state metadata changes. Protected Content is preserved byte-for-byte; no worker read grant exists.
            var expected = asset.Revision++; asset.ModifiedOn = Now; await _store.UpdateAsync(asset, expected);
            var ledger = New<InventoryTransaction>(actor); ledger.ItemId = asset.ItemId; ledger.AssetId = asset.Id; ledger.LotId = asset.LotId;
            ledger.TransactionType = (int)InventoryTransactionType.StatusChange; ledger.OldStatus = old; ledger.NewStatus = asset.Status;
            ledger.FromLocationId = asset.CurrentLocationId; ledger.ToLocationId = asset.CurrentLocationId; ledger.OccurredOn = Now;
            ledger.ReferenceType = (int)InventoryReferenceType.WorkOrder; ledger.ReferenceId = orderId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await _store.InsertAsync(ledger);
            var events = new System.Collections.Generic.List<long>(); await EventAsync(ledger, WorkflowTriggerEventType.InventoryAssetStatusChanged, events);
            return new WorkOrderAssetState { State = old, Revision = asset.Revision, OutboxIds = events };
        }
    }
}
