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
		private async Task RequireCountAccessAsync(InventoryActor actor, InventoryCount count, bool historical = true)
		{
			if (count == null || count.DepartmentId != actor.DepartmentId || count.IsDeleted) throw new InventoryException(404, "Unavailable");
			if (count.LocationId == null) await _auth.RequireAsync(actor, false, PermissionTypes.AdjustInventory);
			else await LocationAsync(actor, count.LocationId, false, PermissionTypes.AdjustInventory, historical: historical);
			foreach (var line in await _store.RelatedAsync<InventoryCountItem>(actor.DepartmentId, "CountId", count.Id))
				await LocationAsync(actor, line.LocationId, false, PermissionTypes.AdjustInventory, historical: historical);
		}
		// Conservative department fence also catches new positions, catalogue edits and rebuilds.
		// Counts and observations themselves are excluded so independent counts do not invalidate each other.
		private async Task<string> CountFingerprintAsync(int departmentId) => Fingerprint(new {
			Items = (await AllAsync<InventoryItem>(departmentId)).OrderBy(x => x.Id).Select(x => new { x.Id, x.Revision }),
			Stocks = (await AllAsync<InventoryStock>(departmentId)).OrderBy(x => x.Id).Select(x => new { x.Id, x.Revision, x.Quantity }),
			Assets = (await AllAsync<InventoryAsset>(departmentId)).OrderBy(x => x.Id).Select(x => new { x.Id, x.Revision }),
			Lots = (await AllAsync<InventoryLot>(departmentId)).OrderBy(x => x.Id).Select(x => new { x.Id, x.Revision }),
			Locations = (await AllAsync<InventoryLocation>(departmentId)).OrderBy(x => x.Id).Select(x => new { x.Id, x.Revision }),
			Ledger = await _store.LastEntryAsync(departmentId)
		});
		public Task<InventoryCountDetail> StartCountAsync(InventoryActor actor, InventoryCountInput input) => TransactionAsync(actor, async events =>
		{
			if (input == null) throw new InventoryException(400, "InvalidCount"); Id(input.Id); Text(input.Name);
			if (input.Note?.Length > 16000) throw new InventoryException(400, "InvalidText");
			var count = New<InventoryCount>(actor); count.Id = input.Id; count.LocationId = input.LocationId;
			await RequireCountAccessAsync(actor, count, false);
			if (await _store.GetAsync<InventoryCount>(actor.DepartmentId, count.Id) != null) throw new InventoryException(409, "CountAlreadyExists");
			count.SnapshotOn = Now; count.SnapshotFingerprint = await CountFingerprintAsync(actor.DepartmentId);
			count.Content = JsonConvert.SerializeObject(new InventoryCountContent { Name = input.Name.Trim(), Note = input.Note });
			var lines = new List<InventoryCountItem>();
			async Task Add(string itemId, string locationId, string lotId, string assetId, decimal quantity)
			{
				if (locationId == null || input.LocationId != null && locationId != input.LocationId) return;
				if (!await LiveAlertLocationAsync(actor.DepartmentId, locationId)) return;
				await LocationAsync(actor, locationId, false, PermissionTypes.AdjustInventory);
				var item = await GetAsync<InventoryItem>(actor, itemId);
				if (item.IsDeleted || !item.IsActive) return;
				var details = Decode<InventoryItemContent>(item);
				var row = New<InventoryCountItem>(actor); row.CountId = count.Id; row.ItemId = itemId; row.LocationId = locationId; row.LotId = lotId; row.AssetId = assetId; row.ExpectedQuantity = quantity;
				row.Content = JsonConvert.SerializeObject(new InventoryCountItemContent { ItemName = details.Name, UnitOfMeasure = details.UnitOfMeasure, CurrencyCode = details.CurrencyCode,
					UnitCost = await CurrentUnitCostAsync(actor, item, lotId, assetId),
					SerialNumber = assetId == null ? null : Decode<InventoryAssetContent>(await GetAsync<InventoryAsset>(actor, assetId)).SerialNumber,
					LotNumber = lotId == null ? null : Decode<InventoryLotContent>(await GetAsync<InventoryLot>(actor, lotId)).LotNumber });
				lines.Add(row); if (lines.Count > 100) throw new InventoryException(409, "CountScopeTooLarge");
			}
			foreach (var stock in await AllAsync<InventoryStock>(actor.DepartmentId)) if (!stock.IsDeleted) await Add(stock.ItemId, stock.LocationId, stock.LotId, null, stock.Quantity);
			foreach (var asset in await AllAsync<InventoryAsset>(actor.DepartmentId)) if (!asset.IsDeleted && asset.Status is 0 or 1 or 2 or 3) await Add(asset.ItemId, asset.CurrentLocationId, asset.LotId, asset.Id, 1);
			if (input.LocationId != null) foreach (var item in await AllAsync<InventoryItem>(actor.DepartmentId))
				if (!item.IsDeleted && item.IsActive && item.TrackingMode == 0 && !item.RequiresLotTracking && !lines.Any(l => l.ItemId == item.Id)) await Add(item.Id, input.LocationId, null, null, 0);
			if (lines.Count == 0) throw new InventoryException(409, "CountEmpty");
			await SaveAsync(actor, count); foreach (var line in lines) await SaveAsync(actor, line);
			await AuditAsync(actor, count, "InventoryCountStarted"); return await GetCountAsync(actor, count.Id);
		});
		public async Task<InventoryCountDetail> GetCountAsync(InventoryActor actor, string id)
		{
			var count = await GetAsync<InventoryCount>(actor, id);
			var result = new InventoryCountDetail { Count = count };
			foreach (var line in await _store.RelatedAsync<InventoryCountItem>(actor.DepartmentId, "CountId", id)) result.Lines.Add(await RevealAsync(actor, line));
			return result;
		}
		public Task<InventoryCountDetail> SaveCountAsync(InventoryActor actor, InventoryCountUpdate input) => TransactionAsync(actor, async events =>
		{
			if (input?.Lines == null || input.Lines.Count is < 1 or > 100 || input.Lines.Any(l => l == null) || input.Lines.Select(l => l.Id).Distinct().Count() != input.Lines.Count) throw new InventoryException(400, "InvalidCount");
			var detail = await GetCountAsync(actor, input.CountId);
			if (detail.Count.Status != 0 || detail.Count.Revision != input.Revision) throw new InventoryException(409, "CountStateConflict");
			foreach (var observation in input.Lines)
			{
				Quantity(observation.Quantity, true);
				var line = detail.Lines.SingleOrDefault(l => l.Id == observation.Id) ?? throw new InventoryException(404, "Unavailable");
				if (line.AssetId != null && observation.Quantity is not (0 or 1)) throw new InventoryException(400, "SerializedQuantity");
				line.CountedQuantity = observation.Quantity; await SaveAsync(actor, line, false);
			}
			await SaveAsync(actor, detail.Count, false); await AuditAsync(actor, detail.Count, "InventoryCountSaved"); return await GetCountAsync(actor, detail.Count.Id);
		});
		public Task CancelCountAsync(InventoryActor actor, string id, int revision) => TransactionAsync(actor, async events =>
		{
			var count = await GetAsync<InventoryCount>(actor, id);
			if (count.Revision != revision || count.Status is not (0 or 1)) throw new InventoryException(409, "CountStateConflict");
			count.Status = 3; await SaveAsync(actor, count, false); await AuditAsync(actor, count, "InventoryCountCancelled"); return true;
		});
		private async Task<InventoryCommand> CountCommandAsync(InventoryActor actor, InventoryCountDetail detail, InventoryCountComplete input, bool witnessed)
		{
			if (detail.Count.Status != (witnessed ? 1 : 0) || detail.Count.Revision != input.Revision || detail.Lines.Any(l => !l.CountedQuantity.HasValue)) throw new InventoryException(409, "CountStateConflict");
			if (detail.Count.SnapshotFingerprint != await CountFingerprintAsync(actor.DepartmentId)) throw new InventoryException(409, "CountSnapshotChanged");
			var command = new InventoryCommand { RequestId = input.RequestId };
			foreach (var row in detail.Lines.Where(l => l.CountedQuantity != l.ExpectedQuantity))
			{
				var variance = row.CountedQuantity.Value - row.ExpectedQuantity;
				if (row.AssetId != null && (variance != -1 || (await _store.RelatedAsync<InventoryIssuance>(actor.DepartmentId, "AssetId", row.AssetId)).Any(i => i.Status is 0 or 2))) throw new InventoryException(409, "ReturnAssetFirst");
				command.Lines.Add(new InventoryPosting { ItemId = row.ItemId, LotId = row.LotId, AssetId = row.AssetId, CountItemId = row.Id, Type = InventoryTransactionType.Count,
					FromLocationId = variance < 0 ? row.LocationId : null, ToLocationId = variance > 0 ? row.LocationId : null, Quantity = Math.Abs(variance), ReferenceType = InventoryReferenceType.Count, ReferenceId = detail.Count.Id });
			}
			if (command.Lines.Count > 0) await ValidateCommandAsync(actor, command, counting: true);
			return command;
		}
		public Task<InventoryResult> CompleteCountAsync(InventoryActor actor, InventoryCountComplete input) => TransactionAsync(actor, async events =>
		{
			if (input == null) throw new InventoryException(400, "InvalidCount");
			await RequireCountAccessAsync(actor, await _store.GetAsync<InventoryCount>(actor.DepartmentId, input.CountId));
			var result = await OperationAsync(actor, input.RequestId, new { Kind = "Count", input }, async (operation, pending) =>
			{
				var detail = await GetCountAsync(actor, input.CountId); var command = await CountCommandAsync(actor, detail, input, false);
				if (await RequiresWitnessAsync(actor, command.Lines.Select(l => l.ItemId)))
				{
					detail.Count.Status = 1; detail.Count.OperationId = operation.Id; await SaveAsync(actor, detail.Count, false);
					operation.Content = JsonConvert.SerializeObject(new InventoryOperationContent { PendingKind = "Count", PerformerId = actor.UserId,
						PendingCount = new InventoryCountComplete { CountId = input.CountId, RequestId = input.RequestId, Revision = detail.Count.Revision } });
					await SaveAsync(actor, operation, false); await AuditAsync(actor, detail.Count, "InventoryCountWitnessRequested");
					return new InventoryResult { AwaitingWitness = true };
				}
				return await CompleteCountLinesAsync(actor, operation, input, pending);
			}, events);
			foreach (var id in result.OutboxIds ?? new()) if (!events.Contains(id)) events.Add(id);
			return result;
		});
		private async Task<InventoryResult> CompleteCountLinesAsync(InventoryActor actor, InventoryOperation operation, InventoryCountComplete input, List<long> events, string performer = null, string witness = null, string attestation = null)
		{
			var detail = await GetCountAsync(actor, input.CountId);
			if (witness != null && detail.Count.OperationId != operation.Id) throw new InventoryException(409, "CountStateConflict");
			var command = await CountCommandAsync(actor, detail, input, witness != null);
			var snapshots = detail.Lines.ToDictionary(l => l.Id, Decode<InventoryCountItemContent>);
			var result = command.Lines.Count == 0 ? new InventoryResult { OperationId = operation.Id } : await PostLinesAsync(actor, operation, command, events, performer, witness, attestation);
			for (var i = 0; i < command.Lines.Count; i++)
			{
				var row = detail.Lines.Single(l => l.Id == command.Lines[i].CountItemId); row.TransactionId = result.TransactionIds[i]; await SaveAsync(actor, row, false);
			}
			var content = Decode<InventoryCountContent>(detail.Count); content.VarianceLineCount = command.Lines.Count;
			content.Totals = detail.Lines.Where(l => l.CountedQuantity != l.ExpectedQuantity).Select(l => new { Row = l, Data = snapshots[l.Id] })
				.GroupBy(l => l.Data.CurrencyCode).Select(g => new InventoryValuationTotal { CurrencyCode = g.Key, UncostedRows = g.Count(l => !l.Data.UnitCost.HasValue),
					KnownValue = g.Sum(l => Money((l.Row.CountedQuantity.Value - l.Row.ExpectedQuantity) * (l.Data.UnitCost ?? 0))) }).ToList();
			detail.Count.Status = 2; detail.Count.CompletedOn = Now; detail.Count.OperationId = operation.Id; detail.Count.Content = JsonConvert.SerializeObject(content);
			await SaveAsync(actor, detail.Count, false); await AuditAsync(actor, detail.Count, "InventoryCountCompleted");
			var entry = await _outbox.EnqueueAsync(actor.DepartmentId, "Inventory", new DomainEventEnvelope { EventName = "InventoryCountCompleted", Trigger = WorkflowTriggerEventType.InventoryCountCompleted,
				SchemaVersion = 1, AggregateType = "InventoryCount", AggregateId = detail.Count.Id, CorrelationId = operation.Id, OccurredOn = Now,
				Payload = new { InventoryEvent = true, CountId = detail.Count.Id, LocationId = detail.Count.LocationId, LineCount = detail.Lines.Count, VarianceLineCount = command.Lines.Count, VarianceValue = ProtectedDataEnvelope.RedactionValue, OccurredOn = Now } });
			events.Add(entry.DomainEventOutboxId); result.OutboxIds = events.ToList(); return result;
		}
	}
}
