using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Inventories;

namespace Resgrid.Services
{
	public sealed partial class InventoryModernizationService
	{
		private async Task RequirePurchaseReceiptAccessAsync(InventoryActor actor, InventoryPurchaseReceiptInput input)
		{
			await RequirePurchasingAccessAsync(actor, true);
			if (input?.Lines == null || input.Lines.Count is < 1 or > 100 || input.Lines.Any(l => l == null) || input.Revision < 1) throw new InventoryException(400, "InvalidPurchaseReceipt");
			Id(input.PurchaseOrderId); Id(input.RequestId);
			if (await _store.GetAsync<InventoryPurchaseOrder>(actor.DepartmentId, input.PurchaseOrderId) == null) throw new InventoryException(404, "Unavailable");
			foreach (var line in input.Lines)
			{
				Id(line.PurchaseOrderItemId); Quantity(line.Quantity); await LocationAsync(actor, line.LocationId, true);
				var source = await _store.GetAsync<InventoryPurchaseOrderItem>(actor.DepartmentId, line.PurchaseOrderItemId);
				if (source == null || source.PurchaseOrderId != input.PurchaseOrderId || source.IsDeleted) throw new InventoryException(404, "PurchaseOrderLineUnavailable");
				var item = await _store.GetAsync<InventoryItem>(actor.DepartmentId, source.ItemId);
				if (item == null) throw new InventoryException(404, "Unavailable");
				if (item.IsControlledSubstance) await _auth.RequireAsync(actor, true, PermissionTypes.ManageControlledSubstances);
			}
		}
		private async Task<InventoryPurchaseOrderDetail> ValidatePurchaseReceiptAsync(InventoryActor actor, InventoryPurchaseReceiptInput input)
		{
			await RequirePurchaseReceiptAccessAsync(actor, input);
			var detail = await GetPurchaseOrderAsync(actor, input.PurchaseOrderId); var order = detail.Order;
			if (order.IsDeleted || order.Revision != input.Revision || order.Status is not (1 or 2)) throw new InventoryException(409, "PurchaseOrderStateConflict");
			var vendor = await GetAsync<InventoryVendor>(actor, order.VendorId);
			if (vendor.IsDeleted) throw new InventoryException(409, "VendorUnavailable");
			await VendorContactAsync(actor, vendor.ContactId, false);
			foreach (var group in input.Lines.GroupBy(l => l.PurchaseOrderItemId))
			{
				var source = detail.Lines.Single(l => l.Id == group.Key);
				if (group.Sum(l => l.Quantity) > source.QuantityOrdered - source.QuantityReceived) throw new InventoryException(409, "PurchaseOrderOverReceipt");
				var item = await GetAsync<InventoryItem>(actor, source.ItemId);
				if (item.IsDeleted || !item.IsActive || Decode<InventoryItemContent>(item).CurrencyCode != order.CurrencyCode) throw new InventoryException(409, "ItemCurrencyMismatch");
				foreach (var line in group)
				{
					if (item.TrackingMode == 1 && (line.Quantity != 1 || line.Asset == null) || item.TrackingMode == 0 && line.Asset != null) throw new InventoryException(400, "SerializedQuantity");
					if (line.Asset != null) { Text(line.Asset.SerialNumber); if (line.Asset.AssetTag?.Length > 250 || line.Asset.Barcode?.Length > 250) throw new InventoryException(400, "InvalidAsset"); }
					if (line.LotId != null)
					{
						var lot = await GetAsync<InventoryLot>(actor, line.LotId); var lotDetails = Decode<InventoryLotContent>(lot);
						if (lot.ItemId != item.Id || lot.IsDeleted || lot.ExpiresOn <= Now) throw new InventoryException(409, "LotMismatch");
						if (lotDetails.UnitCost.HasValue && lotDetails.UnitCost != Decode<InventoryPurchaseOrderItemContent>(source).UnitCost) throw new InventoryException(409, "LotCostMismatch");
						if (lotDetails.VendorId != null && lotDetails.VendorId != order.VendorId) throw new InventoryException(409, "LotVendorMismatch");
					}
					else if (item.RequiresLotTracking) throw new InventoryException(400, "LotRequired");
					if (line.Asset != null && (line.Asset.ExpiresOn <= Now || item.RequiresExpiration && !line.Asset.ExpiresOn.HasValue)) throw new InventoryException(409, "ExpiryRequired");
				}
			}
			return detail;
		}
		public Task<InventoryResult> ReceivePurchaseOrderAsync(InventoryActor actor, InventoryPurchaseReceiptInput input) => TransactionAsync(actor, async events =>
		{
			await RequirePurchaseReceiptAccessAsync(actor, input);
			input = JsonConvert.DeserializeObject<InventoryPurchaseReceiptInput>(JsonConvert.SerializeObject(input));
			var result = await OperationAsync(actor, input.RequestId, new { Kind = "PurchaseReceive", input }, async (operation, pending) =>
			{
				var detail = await ValidatePurchaseReceiptAsync(actor, input);
				if (await RequiresWitnessAsync(actor, input.Lines.Select(l => detail.Lines.Single(p => p.Id == l.PurchaseOrderItemId).ItemId)))
				{
					operation.Content = JsonConvert.SerializeObject(new InventoryOperationContent { PendingKind = "PurchaseReceive", PendingPurchaseReceipt = input, PerformerId = actor.UserId });
					await SaveAsync(actor, operation, false); await AuditAsync(actor, operation, "InventoryPurchaseReceiptWitnessRequested");
					return new InventoryResult { AwaitingWitness = true, PurchaseOrderId = detail.Order.Id };
				}
				return await CompletePurchaseReceiptAsync(actor, operation, input, pending);
			}, events);
			// A successful retry can redeliver only the original durable identities, never recreate a receipt.
			foreach (var id in result.OutboxIds ?? new()) if (!events.Contains(id)) events.Add(id);
			return result;
		});
		private async Task<InventoryResult> CompletePurchaseReceiptAsync(InventoryActor actor, InventoryOperation operation, InventoryPurchaseReceiptInput input, List<long> events,
			string performer = null, string witness = null, string attestation = null)
		{
			var detail = await ValidatePurchaseReceiptAsync(actor, input);
			var command = new InventoryCommand { RequestId = input.RequestId }; var kits = new List<string>();
			foreach (var line in input.Lines)
			{
				var source = detail.Lines.Single(l => l.Id == line.PurchaseOrderItemId); var cost = Decode<InventoryPurchaseOrderItemContent>(source).UnitCost;
				var item = await GetAsync<InventoryItem>(actor, source.ItemId); string assetId = null;
				if (line.Asset != null)
				{
					var asset = await CreateUnreceivedAssetAsync(actor, item, null, line.LotId, line.Asset.ExpiresOn, new InventoryAssetContent {
						SerialNumber = line.Asset.SerialNumber, AssetTag = line.Asset.AssetTag, Barcode = line.Asset.Barcode, AcquisitionCost = cost }, performer);
					assetId = asset.Id; if (item.IsKit) kits.Add(assetId);
				}
				command.Lines.Add(new InventoryPosting { ItemId = item.Id, AssetId = assetId, LotId = line.LotId, ToLocationId = line.LocationId, Quantity = line.Quantity,
					UnitCost = cost, Type = InventoryTransactionType.Receive, ReferenceType = InventoryReferenceType.PurchaseOrder, ReferenceId = detail.Order.Id, PurchaseOrderItemId = source.Id });
			}
			await ValidateCommandAsync(actor, command, purchasing: true);
			var result = await PostLinesAsync(actor, operation, command, events, performer, witness, attestation);
			foreach (var kit in kits) await CreateKitContainerAsync(actor, kit, performer);
			foreach (var group in input.Lines.GroupBy(l => l.PurchaseOrderItemId))
			{
				var source = detail.Lines.Single(l => l.Id == group.Key); source.QuantityReceived += group.Sum(l => l.Quantity); await SaveAsync(actor, source, false);
			}
			var order = detail.Order; order.Status = detail.Lines.All(l => l.QuantityReceived == l.QuantityOrdered) ? 3 : 2; order.ReceivedOn = Now;
			await SaveAsync(actor, order, false); await AuditAsync(actor, order, "InventoryPurchaseOrderReceived");
			var entry = await _outbox.EnqueueAsync(actor.DepartmentId, "Inventory", new DomainEventEnvelope {
				EventName = WorkflowTriggerEventType.InventoryPurchaseOrderReceived.ToString(), SchemaVersion = 1, AggregateType = "InventoryPurchaseOrder", AggregateId = order.Id,
				Trigger = WorkflowTriggerEventType.InventoryPurchaseOrderReceived, CorrelationId = operation.Id, OccurredOn = Now,
				Payload = new { InventoryEvent = true, PurchaseOrderId = order.Id, order.VendorId, ReceiptId = operation.Id, PurchaseOrderStatus = order.Status,
					LineCount = input.Lines.Select(l => l.PurchaseOrderItemId).Distinct().Count(), order.CurrencyCode, OccurredOn = Now } });
			events.Add(entry.DomainEventOutboxId); result.PurchaseOrderId = order.Id; result.OutboxIds = events.ToList(); return result;
		}
	}
}
