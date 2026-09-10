using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Inventories;
using Resgrid.Model.Services;
using Resgrid.Model.WorkOrders;

namespace Resgrid.Services
{
    public sealed partial class WorkOrdersService
    {
        private static string OperationHash(object input) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(input))));
        public async Task<List<WorkOrderPartMovement>> PartMovementsAsync(ChecklistActor actor, int id, int afterId = 0) => (await EvidencePageAsync<WorkOrderPartMovement, WorkOrderPartMovement>(actor, id, afterId, r => r)).Items;
        public async Task ReservePartAsync(ChecklistActor actor, int id, WorkOrderPartInput input)
        {
            if (input?.Content == null || !Guid.TryParseExact(input.RequestId, "D", out _) || !Guid.TryParseExact(input.InventoryItemId, "D", out _) || !Guid.TryParseExact(input.InventoryLocationId, "D", out _)
                || input.Content.Quantity is <= 0 or > 100000 || decimal.Round(input.Content.Quantity, 6) != input.Content.Quantity || _inventoryMaintenance == null) throw new WorkOrderException(400, "InventoryPartInvalid");
            Text(input.Content.Description, 2000, true);
            var hash = OperationHash(new { input.InventoryItemId, input.InventoryAssetId, input.InventoryLotId, input.InventoryLocationId, input.Content.Description, input.Content.Quantity });
            await TransactionAsync(actor, async events =>
            {
                RequireMaintenanceStore(); await ReadOrderAsync(actor, id);
                var existing = (await _maintenance.QueryMaintenanceAsync<WorkOrderPart>(actor.DepartmentId, "InventoryRequestId", input.RequestId)).SingleOrDefault();
                if (existing != null)
                {
                    await RevealAsync(actor, existing);
                    if (!existing.Staged || existing.CreatedBy != actor.UserId || existing.WorkOrderId != id || Decode<WorkOrderPartContent>(existing.Content).RequestFingerprint != hash) throw new WorkOrderException(409, "Conflict");
                    return true;
                }
                var row = await ContributionAsync(actor, id, input.Revision); var quote = await _inventoryMaintenance.Value.QuotePartAsync(InventoryActor(actor), id, input);
                await RequireSpendingAsync(actor, row, quote.UnitCost.HasValue ? decimal.Round(quote.UnitCost.Value * input.Content.Quantity, 2, MidpointRounding.AwayFromZero) : null, quote.Currency);
                var part = New<WorkOrderPart>(actor, id); part.Staged = true; part.InventoryItemId = input.InventoryItemId; part.InventoryRequestId = input.RequestId;
                part.ReservedLocationId = input.InventoryLocationId; part.ReservedAssetId = input.InventoryAssetId; part.ReservedLotId = input.InventoryLotId; part.ReservedQuantity = input.Content.Quantity;
                part.Content = JsonConvert.SerializeObject(new WorkOrderPartContent { RequestFingerprint = hash, Description = input.Content.Description, Quantity = input.Content.Quantity, UnitCost = quote.UnitCost, Currency = quote.Currency, ConsumedCost = 0 }); await SaveAsync(actor, part, true);
                var movement = New<WorkOrderPartMovement>(actor, id); movement.PartId = part.Id; movement.Kind = (int)WorkOrderPartMovementKind.Reserve; movement.Quantity = input.Content.Quantity; movement.FromLocationId = part.ReservedLocationId;
                movement.RequestId = MaintenanceIdentity("part-reservation:" + input.RequestId); movement.Content = JsonConvert.SerializeObject(new { RequestHash = hash }); await SaveAsync(actor, movement, true);
                await ChangedAsync(actor, row, WorkOrderActivityType.PartAdded, events, trigger: WorkflowTriggerEventType.WorkOrderPartChanged); return true;
            });
        }
        public async Task MovePartAsync(ChecklistActor actor, int id, int partId, WorkOrderPartMovementInput input)
        {
            if (input == null || !Guid.TryParseExact(input.RequestId, "D", out _) || !Enum.IsDefined(input.Kind) || input.Kind == WorkOrderPartMovementKind.Reserve || input.Quantity is <= 0 or > 100000 || decimal.Round(input.Quantity, 6) != input.Quantity) throw new WorkOrderException(400, "InventoryPartInvalid");
            Text(input.Reason, 4000, input.Kind is WorkOrderPartMovementKind.ReturnUnused or WorkOrderPartMovementKind.ReleaseReservation);
            var hash = OperationHash(new { id, partId, input.Kind, input.Quantity, input.LocationId, input.Reason });
            await TransactionAsync(actor, async events =>
            {
                RequireMaintenanceStore(); await ReadOrderAsync(actor, id);
                var prior = (await _maintenance.QueryMaintenanceAsync<WorkOrderPartMovement>(actor.DepartmentId, "RequestId", input.RequestId)).SingleOrDefault();
                if (prior != null)
                {
                    await RevealAsync(actor, prior);
                    if (prior.WorkOrderId != id || prior.PartId != partId || prior.CreatedBy != actor.UserId || JObject.Parse(prior.Content)["RequestHash"]?.Value<string>() != hash) throw new WorkOrderException(409, "Conflict");
                    return true;
                }
                var row = await ContributionAsync(actor, id, input.Revision); var part = await _store.GetAsync<WorkOrderPart>(actor.DepartmentId, partId);
                if (part?.WorkOrderId != id || !part.Staged || part.VoidedOn.HasValue) throw new WorkOrderException(409, "Unavailable");
                await RevealAsync(actor, part);
                var movements = new List<WorkOrderPartMovement>();
                for (var skip = 0; ; skip += 500) { var page = await _maintenance.QueryMaintenanceAsync<WorkOrderPartMovement>(actor.DepartmentId, "PartId", partId, skip); movements.AddRange(page); if (page.Count < 500) break; if (skip >= 9500) throw new WorkOrderException(400, "HistoryLimit"); }
                if (movements.Any(m => !m.Cancelled && m.InventoryOperationId != null && m.InventoryTransactionId == null)) throw new WorkOrderException(409, "InventoryWitnessPending");
                var fromReserved = input.Kind is WorkOrderPartMovementKind.Issue or WorkOrderPartMovementKind.ReleaseReservation;
                if (input.Quantity > (fromReserved ? part.ReservedQuantity : part.IssuedQuantity)) throw new WorkOrderException(409, "PartBalanceInvalid");
                if (input.Kind == WorkOrderPartMovementKind.Issue && (!Guid.TryParseExact(input.LocationId, "D", out _) || input.LocationId == part.ReservedLocationId || part.IssuedQuantity > 0 && part.IssuedLocationId != input.LocationId)) throw new WorkOrderException(400, "InventoryPartInvalid");
                var movement = New<WorkOrderPartMovement>(actor, id); movement.PartId = partId; movement.RequestId = input.RequestId; movement.Kind = (int)input.Kind; movement.Quantity = input.Quantity;
                movement.FromLocationId = fromReserved ? part.ReservedLocationId : part.IssuedLocationId;
                movement.ToLocationId = input.Kind == WorkOrderPartMovementKind.Issue ? input.LocationId : input.Kind == WorkOrderPartMovementKind.ReturnUnused ? part.ReservedLocationId : null;
                movement.Content = JsonConvert.SerializeObject(new { RequestHash = hash, input.Reason }); await SaveAsync(actor, movement, true);
                if (input.Kind == WorkOrderPartMovementKind.ReleaseReservation)
                {
                    part.ReservedQuantity -= input.Quantity; part.Revision++; await SaveAsync(actor, part);
                    await ChangedAsync(actor, row, WorkOrderActivityType.PartVoided, events, input.Reason, trigger: WorkflowTriggerEventType.WorkOrderPartChanged); return true;
                }
                if (_inventoryMaintenance == null) throw new WorkOrderException(503, "MaintenanceUnavailable");
                if (input.Kind != WorkOrderPartMovementKind.ReturnUnused) await RequireSpendingAsync(actor, row);
                var command = new InventoryCommand { RequestId = input.RequestId, Lines = new() { new InventoryPosting { ItemId = part.InventoryItemId, AssetId = part.ReservedAssetId, LotId = part.ReservedLotId,
                    FromLocationId = movement.FromLocationId, ToLocationId = movement.ToLocationId, Quantity = input.Quantity, Type = input.Kind == WorkOrderPartMovementKind.Consume ? InventoryTransactionType.Consume : InventoryTransactionType.Transfer,
                    ReferenceType = InventoryReferenceType.WorkOrder, ReferenceId = id.ToString(CultureInfo.InvariantCulture), WorkOrderPartId = partId, WorkOrderPartMovementId = movement.Id } } };
                var result = await _inventoryMaintenance.Value.PostPartAsync(InventoryActor(actor), partId, command); events.AddRange(result.OutboxIds);
                movement.InventoryOperationId = result.OperationId; movement.Revision++; await SaveAsync(actor, movement);
                if (!result.AwaitingWitness) await CompleteInventoryPartAsync(InventoryActor(actor), partId, result.TransactionIds.Single(), false, events);
                else await ChangedAsync(actor, row, WorkOrderActivityType.PartAdded, events, trigger: WorkflowTriggerEventType.WorkOrderPartChanged);
                return true;
            });
        }
        private async Task CompletePartMovementAsync(ChecklistActor actor, WorkOrder row, WorkOrderPart part, InventoryTransaction ledger, List<long> events)
        {
            var movement = await _store.GetAsync<WorkOrderPartMovement>(actor.DepartmentId, ledger.WorkOrderPartMovementId.Value);
            if (movement?.PartId != part.Id || movement.WorkOrderId != row.Id || movement.Cancelled || movement.Quantity != ledger.Quantity || movement.InventoryTransactionId != null
                || movement.FromLocationId != ledger.FromLocationId || movement.ToLocationId != ledger.ToLocationId) throw new WorkOrderException(409, "InventoryPartInvalid");
            await RevealAsync(actor, movement); var c = Decode<WorkOrderPartContent>(part.Content);
            switch ((WorkOrderPartMovementKind)movement.Kind)
            {
                case WorkOrderPartMovementKind.Issue: part.ReservedQuantity -= ledger.Quantity; part.IssuedQuantity += ledger.Quantity; part.IssuedLocationId = ledger.ToLocationId; break;
                case WorkOrderPartMovementKind.Consume:
                    part.IssuedQuantity -= ledger.Quantity; part.ConsumedQuantity += ledger.Quantity;
                    var cost = JObject.Parse(ledger.Content); if (cost["CurrencyCode"]?.Value<string>() != c.Currency) throw new WorkOrderException(409, "PartCurrencyChanged");
                    c.ConsumedCost = c.ConsumedCost.HasValue && cost["TotalCost"]?.Value<decimal?>() is decimal amount ? c.ConsumedCost.Value + amount : null;
                    break;
                case WorkOrderPartMovementKind.ReturnUnused: part.IssuedQuantity -= ledger.Quantity; part.ReturnedQuantity += ledger.Quantity; break;
                default: throw new WorkOrderException(409, "InventoryPartInvalid");
            }
            if (part.ReservedQuantity < 0 || part.IssuedQuantity < 0) throw new WorkOrderException(409, "PartBalanceInvalid");
            part.Content = JsonConvert.SerializeObject(c); part.Revision++; await SaveAsync(actor, part);
            movement.InventoryTransactionId = ledger.Id; movement.InventoryOperationId = ledger.OperationId; movement.Revision++; await SaveAsync(actor, movement);
            if (movement.Kind != (int)WorkOrderPartMovementKind.ReturnUnused) await RequireSpendingAsync(actor, row);
            await ChangedAsync(actor, row, WorkOrderActivityType.PartAdded, events, trigger: WorkflowTriggerEventType.WorkOrderPartChanged);
        }
        public async Task CancelPartMovementAsync(ChecklistActor actor, int id, int movementId, int revision, string reason)
        {
            Text(reason, 4000, true);
            await TransactionAsync(actor, async events =>
            {
                var row = await ContributionAsync(actor, id, revision); var movement = await _store.GetAsync<WorkOrderPartMovement>(actor.DepartmentId, movementId);
                if (movement?.WorkOrderId != id || movement.Cancelled || movement.InventoryOperationId == null || movement.InventoryTransactionId != null || _inventoryMaintenance == null) throw new WorkOrderException(409, "Unavailable");
                await RevealAsync(actor, movement); await _inventoryMaintenance.Value.CancelPendingPartAsync(InventoryActor(actor), movement.PartId, movement.InventoryOperationId);
                movement.Cancelled = true; movement.Revision++; await SaveAsync(actor, movement);
                await ChangedAsync(actor, row, WorkOrderActivityType.PartVoided, events, reason, trigger: WorkflowTriggerEventType.WorkOrderPartChanged); return true;
            });
        }
    }
}
