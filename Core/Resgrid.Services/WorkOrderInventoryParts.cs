using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Inventories;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Services
{
    public sealed partial class WorkOrdersService
    {
        public async Task CancelPartWitnessAsync(ChecklistActor actor, int orderId, int partId, int revision, string reason)
        {
            Text(reason, 4000, true);
            await TransactionAsync(actor, async events =>
            {
                var order = await ContributionAsync(actor, orderId, revision);
                var part = await _store.GetAsync<WorkOrderPart>(actor.DepartmentId, partId);
                if (part?.WorkOrderId != orderId || part.InventoryOperationId == null || part.VoidedOn.HasValue || _inventoryMaintenance == null || _inventoryCatalog == null)
                    throw new WorkOrderException(409, "Unavailable");
                await RevealAsync(actor, part); var content = Decode<WorkOrderPartContent>(part.Content);
                if (part.InventoryTransactionId != null && (content.VoidReason == null || part.InventoryReversalId != null)) throw new WorkOrderException(409, "Conflict");
                await _inventoryMaintenance.Value.CancelPendingPartAsync(InventoryActor(actor), part.Id, part.InventoryOperationId);
                if (part.InventoryTransactionId == null) { part.VoidedOn = Now; content.VoidReason = reason; }
                else { content.VoidReason = null; part.InventoryOperationId = (await _inventoryCatalog.Value.GetAsync<InventoryTransaction>(InventoryActor(actor), part.InventoryTransactionId)).OperationId; }
                part.Content = JsonConvert.SerializeObject(content); part.Revision++; await SaveAsync(actor, part);
                await ChangedAsync(actor, order, WorkOrderActivityType.PartVoided, events, reason, trigger: WorkflowTriggerEventType.WorkOrderPartChanged); return true;
            });
        }
        private async Task RequireNoPendingPartsAsync(ChecklistActor actor, int id)
        {
            if ((await ChildrenAsync<WorkOrderPart>(actor, id)).Any(p => p.Staged && (p.ReservedQuantity > 0 || p.IssuedQuantity > 0))) throw new WorkOrderException(409, "PartBalanceOutstanding");
            foreach (var part in await ChildrenAsync<WorkOrderPart>(actor, id))
                if (part.InventoryOperationId != null && !part.VoidedOn.HasValue && (part.InventoryTransactionId == null || Decode<WorkOrderPartContent>(part.Content).VoidReason != null && part.InventoryReversalId == null))
                    throw new WorkOrderException(409, "InventoryWitnessPending");
        }
        public async Task AddPartAsync(ChecklistActor actor, int id, WorkOrderPartInput input)
        {
            if (input?.Content == null || input.Content.Quantity <= 0 || input.Content.Quantity > 100000 || decimal.Round(input.Content.Quantity, 6) != input.Content.Quantity) throw new WorkOrderException(400, "InvalidInput");
            Text(input.Content.Description, 2000, true); Money(input.Content.UnitCost); input.Content.VoidReason = null;
            var linked = !string.IsNullOrWhiteSpace(input.InventoryItemId);
            var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { input.Content.Description, input.Content.Quantity, input.Content.UnitCost, input.InventoryItemId, input.InventoryAssetId, input.InventoryLotId, input.InventoryLocationId }))));
            if (linked && (!Guid.TryParseExact(input.RequestId, "D", out _) || !Guid.TryParseExact(input.InventoryItemId, "D", out _) || !Guid.TryParseExact(input.InventoryLocationId, "D", out _) || _inventoryMaintenance == null)) throw new WorkOrderException(400, "InventoryPartInvalid");
            await TransactionAsync(actor, async events =>
            {
                if (linked)
                {
                    RequireMaintenanceStore();
                    var prior = (await _maintenance.QueryMaintenanceAsync<WorkOrderPart>(actor.DepartmentId, "InventoryRequestId", input.RequestId)).SingleOrDefault();
                    if (prior != null)
                    {
                        await ReadOrderAsync(actor, id); await RevealAsync(actor, prior);
                        var content = Decode<WorkOrderPartContent>(prior.Content);
                        if (prior.Staged || prior.WorkOrderId != id || prior.CreatedBy != actor.UserId || prior.InventoryItemId != input.InventoryItemId || content.Quantity != input.Content.Quantity || content.RequestFingerprint != fingerprint) throw new WorkOrderException(409, "Conflict");
                        return true;
                    }
                }
                var row = await ContributionAsync(actor, id, input.Revision);
                var part = New<WorkOrderPart>(actor, id); part.InventoryItemId = linked ? input.InventoryItemId : null; part.InventoryRequestId = linked ? input.RequestId : null;
                input.Content.Currency = Decode<StoredContent>(row.Content).Fields.Currency; input.Content.RequestFingerprint = linked ? fingerprint : null;
                if (!linked) await RequireSpendingAsync(actor, row, input.Content.UnitCost.HasValue ? decimal.Round(input.Content.Quantity * input.Content.UnitCost.Value, 2, MidpointRounding.AwayFromZero) : null, input.Content.Currency);
                part.Content = JsonConvert.SerializeObject(input.Content); await SaveAsync(actor, part, true);
                if (linked)
                {
                    var command = new InventoryCommand { RequestId = input.RequestId, Lines = new() { new InventoryPosting { ItemId = input.InventoryItemId, AssetId = input.InventoryAssetId, LotId = input.InventoryLotId, FromLocationId = input.InventoryLocationId,
                        Quantity = input.Content.Quantity, Type = InventoryTransactionType.Consume, ReferenceType = InventoryReferenceType.WorkOrder, ReferenceId = id.ToString(CultureInfo.InvariantCulture), WorkOrderPartId = part.Id } } };
                    var result = await _inventoryMaintenance.Value.PostPartAsync(InventoryActor(actor), part.Id, command);
                    events.AddRange(result.OutboxIds); part.InventoryOperationId = result.OperationId; part.Revision++;
                    await RevealAsync(actor, part); await SaveAsync(actor, part);
                    if (!result.AwaitingWitness) { await CompleteInventoryPartAsync(InventoryActor(actor), part.Id, result.TransactionIds.Single(), false, events); return true; }
                }
                await ChangedAsync(actor, row, WorkOrderActivityType.PartAdded, events, trigger: WorkflowTriggerEventType.WorkOrderPartChanged); return true;
            });
        }
        public async Task VoidPartAsync(ChecklistActor actor, int id, int partId, int revision, string reason)
        {
            Text(reason, 4000, true);
            await TransactionAsync(actor, async events =>
            {
                var row = await ContributionAsync(actor, id, revision); var part = await _store.GetAsync<WorkOrderPart>(actor.DepartmentId, partId);
                if (part?.WorkOrderId != id || part.VoidedOn.HasValue || part.Staged) throw new WorkOrderException(409, "Unavailable");
                await RevealAsync(actor, part); var content = Decode<WorkOrderPartContent>(part.Content);
                if (content.VoidReason != null) throw new WorkOrderException(409, "InventoryWitnessPending");
                content.VoidReason = reason; part.Content = JsonConvert.SerializeObject(content); part.Revision++;
                if (part.InventoryItemId != null)
                {
                    if (part.InventoryTransactionId == null || _inventoryCatalog == null || _inventoryMaintenance == null) throw new WorkOrderException(409, "InventoryWitnessPending");
                    var original = await _inventoryCatalog.Value.GetAsync<InventoryTransaction>(InventoryActor(actor), part.InventoryTransactionId);
                    if (original.WorkOrderPartId != partId || original.ReversesTransactionId != null) throw new WorkOrderException(409, "InventoryPartInvalid");
                    await SaveAsync(actor, part);
                    var command = new InventoryCommand { RequestId = MaintenanceIdentity("part-reversal:" + actor.DepartmentId + ":" + part.Id + ":" + part.Revision), Lines = new() { new InventoryPosting {
                        ItemId = original.ItemId, AssetId = original.AssetId, LotId = original.LotId, ToLocationId = original.FromLocationId, Quantity = original.Quantity, Type = InventoryTransactionType.Adjust,
                        ReferenceType = InventoryReferenceType.WorkOrder, ReferenceId = id.ToString(CultureInfo.InvariantCulture), WorkOrderPartId = partId, ReversesTransactionId = original.Id, Note = reason } } };
                    var result = await _inventoryMaintenance.Value.PostPartAsync(InventoryActor(actor), partId, command); events.AddRange(result.OutboxIds);
                    if (!result.AwaitingWitness) { await CompleteInventoryPartAsync(InventoryActor(actor), partId, result.TransactionIds.Single(), true, events); return true; }
                    part.InventoryOperationId = result.OperationId; await RevealAsync(actor, part); await SaveAsync(actor, part);
                }
                else { part.VoidedOn = Now; await SaveAsync(actor, part); }
                await ChangedAsync(actor, row, WorkOrderActivityType.PartVoided, events, reason, trigger: WorkflowTriggerEventType.WorkOrderPartChanged); return true;
            });
        }
        public async Task CompleteInventoryPartAsync(InventoryActor actor, int partId, string transactionId, bool reversal, List<long> events)
        {
            if (_uow.Transaction == null || _inventoryCatalog == null) throw new InvalidOperationException("The inventory posting transaction owns completion.");
            var principal = new ChecklistActor { DepartmentId = actor.DepartmentId, UserId = actor.UserId, GrantToken = actor.GrantToken };
            await RequireWriteAsync(principal);
            var part = await _store.GetAsync<WorkOrderPart>(actor.DepartmentId, partId);
            if (part?.WorkOrderId == null) throw new WorkOrderException(404, "Unavailable");
            var row = await ReadOrderAsync(principal, part.WorkOrderId.Value);
            if (row.Status >= 5 || !await _authorization.CanContributeAsync(principal, row)) throw new WorkOrderException(403, "PermissionRequired");
            await RevealAsync(principal, part);
            var ledger = await _inventoryCatalog.Value.GetAsync<InventoryTransaction>(actor, transactionId);
            if (ledger.WorkOrderPartId != partId || ledger.ReferenceType != (int)InventoryReferenceType.WorkOrder || ledger.ReferenceId != row.Id.ToString(CultureInfo.InvariantCulture) || ledger.ItemId != part.InventoryItemId) throw new WorkOrderException(409, "InventoryPartInvalid");
            if (part.Staged && ledger.WorkOrderPartMovementId.HasValue) { await CompletePartMovementAsync(principal, row, part, ledger, events); return; }
            if (part.Staged || ledger.WorkOrderPartMovementId.HasValue) throw new WorkOrderException(409, "InventoryPartInvalid");
            var content = Decode<WorkOrderPartContent>(part.Content);
            if (ledger.Quantity != content.Quantity || reversal && ledger.ReversesTransactionId != part.InventoryTransactionId || !reversal && ledger.ReversesTransactionId != null) throw new WorkOrderException(409, "InventoryPartInvalid");
            if (reversal) { if (part.InventoryReversalId != null) throw new WorkOrderException(409, "Conflict"); part.InventoryReversalId = ledger.Id; part.VoidedOn = Now; }
            else
            {
                if (part.InventoryTransactionId != null) throw new WorkOrderException(409, "Conflict");
                part.InventoryTransactionId = ledger.Id;
                var details = JObject.Parse(ledger.Content ?? "{}"); content.UnitCost = details["UnitCost"]?.Value<decimal?>(); content.Currency = details["CurrencyCode"]?.Value<string>();
                part.Content = JsonConvert.SerializeObject(content);
            }
            part.Revision++; await SaveAsync(principal, part);
            if (!reversal) await RequireSpendingAsync(principal, row);
            await ChangedAsync(principal, row, reversal ? WorkOrderActivityType.PartVoided : WorkOrderActivityType.PartAdded, events, trigger: WorkflowTriggerEventType.WorkOrderPartChanged);
        }
    }
}
