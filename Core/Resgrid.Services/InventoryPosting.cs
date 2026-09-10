using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Inventories;

namespace Resgrid.Services
{
	public sealed partial class InventoryModernizationService
	{
		private async Task<InventoryResult> OperationAsync(InventoryActor actor, string requestId, object input, Func<InventoryOperation, List<long>, Task<InventoryResult>> work, List<long> events)
		{
			Id(requestId); if (requestId == "00000000-0000-0000-0000-000000000001") throw new InventoryException(400, "ReservedRequestId");
			var fingerprint = Fingerprint(input); var existing = await _store.RequestAsync(actor.DepartmentId, requestId);
			if (existing != null)
			{
				if (existing.CreatedBy != actor.UserId) throw new InventoryException(409, "RequestConflict");
				var receipt = Decode<InventoryOperationContent>(await RevealAsync(actor, existing));
				if (receipt.Fingerprint != fingerprint) throw new InventoryException(409, "RequestConflict");
				return receipt.Result ?? throw new InventoryException(409, "RequestInProgress");
			}
			var operation = New<InventoryOperation>(actor); operation.RequestId = requestId; operation.State = 1;
			// Insert the operation first so ledger FK constraints can join it. Content has no unsaved caller payload.
			operation.Content = JsonConvert.SerializeObject(new InventoryOperationContent { Fingerprint = fingerprint }); await SaveAsync(actor, operation);
			var result = await work(operation, events); result.OperationId = operation.Id; result.OutboxIds = events.ToList();
			operation.State = result.AwaitingWitness ? 1 : 2;
			var content = Decode<InventoryOperationContent>(await RevealAsync(actor, operation)); content.Fingerprint = fingerprint; content.Result = result;
			operation.Content = JsonConvert.SerializeObject(content); await SaveAsync(actor, operation, false); return result;
		}
		private async Task ValidateCommandAsync(InventoryActor actor, InventoryCommand command, bool issuance = false, bool joined = false, bool purchasing = false, bool counting = false)
		{
			await _auth.RequireAsync(actor);
			if (command?.Lines == null || command.Lines.Count is < 1 or > 100) throw new InventoryException(400, "InvalidLines"); Id(command.RequestId);
			foreach (var line in command.Lines)
			{
				if (line?.WorkOrderPartMovementId != null && (!joined || line.WorkOrderPartId == null)) throw new InventoryException(400, "WorkOrderPostingRequired");
				if (line?.WorkOrderPartId != null && (!joined || line.ReferenceType != InventoryReferenceType.WorkOrder)) throw new InventoryException(400, "WorkOrderPostingRequired");
				if (line?.WorkOrderPartId != null) await RequireWorkOrderPartAsync(actor, line);
				if (line?.CountItemId != null && !counting) throw new InventoryException(400, "CountCompletionRequired");
				if (line?.PurchaseOrderItemId != null && !purchasing) throw new InventoryException(400, "PurchaseReceiptRequired");
				if (line != null && (line.UsageId != null || line.UsageType.HasValue))
				{
					if (!joined || line.ReferenceType != InventoryReferenceType.RmsRecord || line.UsageId == null || !line.UsageType.HasValue || !Enum.IsDefined(line.UsageType.Value)) throw new InventoryException(400, "InvalidUsageSource");
					Id(line.UsageId);
				}
				if (line == null || !Enum.IsDefined(line.Type) || line.Type == InventoryTransactionType.Migrated || !counting && line.Type == InventoryTransactionType.Count || !issuance && line.Type is InventoryTransactionType.Issue or InventoryTransactionType.Return)
					throw new InventoryException(400, "InvalidTransactionType");
				Quantity(line.Quantity, line.Type == InventoryTransactionType.StatusChange);
				if (line.Type == InventoryTransactionType.StatusChange && (line.Quantity != 0 || line.AssetId == null || !line.Status.HasValue || !Enum.IsDefined(line.Status.Value))) throw new InventoryException(400, "InvalidAssetStatus");
				if (line.Type != InventoryTransactionType.StatusChange && line.Status.HasValue && !issuance) throw new InventoryException(400, "InvalidAssetStatus");
				if (!issuance && line.IssuanceId != null) throw new InventoryException(400, "InvalidIssuance");
				if (line.Note?.Length > 16000 || line.UnitCost < 0) throw new InventoryException(400, "InvalidText");
				if (line.FromLocationId == line.ToLocationId && line.Type != InventoryTransactionType.StatusChange) throw new InventoryException(400, "DistinctLocationsRequired");
				var from = line.FromLocationId != null; var to = line.ToLocationId != null;
				if (line.Type == InventoryTransactionType.Receive && (from || !to)
					|| line.Type is InventoryTransactionType.Consume or InventoryTransactionType.WriteOff && (!from || to)
					|| line.Type is InventoryTransactionType.Transfer or InventoryTransactionType.Issue or InventoryTransactionType.Return && (!from || !to)
					|| line.Type is InventoryTransactionType.Adjust or InventoryTransactionType.Count && from == to) throw new InventoryException(400, "InvalidMovement");
				var permission = line.Type == InventoryTransactionType.Transfer ? PermissionTypes.TransferInventory : line.Type is InventoryTransactionType.Issue or InventoryTransactionType.Return ? PermissionTypes.IssueInventory : PermissionTypes.AdjustInventory;
				if (from) await LocationAsync(actor, line.FromLocationId, true, permission, historical: line.ReversesTransactionId != null);
				if (to) await LocationAsync(actor, line.ToLocationId, true, permission, historical: line.ReversesTransactionId != null);
				if (!from && !to) await _auth.RequireAsync(actor, true, permission);
				await ValidatePostingItemAsync(actor, line, joined, purchasing, counting);
			}
		}
		private async Task ValidatePostingItemAsync(InventoryActor actor, InventoryPosting line, bool joined = false, bool purchasing = false, bool counting = false)
		{
			var item = await GetAsync<InventoryItem>(actor, line.ItemId);
			if ((item.IsDeleted || !item.IsActive) && line.ReversesTransactionId == null) throw new InventoryException(409, "ItemUnavailable");
			if (item.IsControlledSubstance) await _auth.RequireAsync(actor, true, PermissionTypes.ManageControlledSubstances);
			if (!Enum.IsDefined(line.ReferenceType) || line.ReferenceId?.Length > 128 || (line.ReferenceType == InventoryReferenceType.None) != (line.ReferenceId == null)) throw new InventoryException(400, "InvalidReference");
			if (line.ReferenceId != null && !Guid.TryParseExact(line.ReferenceId, "D", out _) && (!long.TryParse(line.ReferenceId, out var numeric) || numeric <= 0)) throw new InventoryException(400, "InvalidReference");
			await ValidateReferenceAsync(actor, line, joined, purchasing: purchasing, counting: counting);
			if (line.LotId != null)
			{
				var lot = await GetAsync<InventoryLot>(actor, line.LotId);
				if (lot.ItemId != item.Id || lot.IsDeleted && line.ReversesTransactionId == null || item.RequiresExpiration && !lot.ExpiresOn.HasValue) throw new InventoryException(400, "LotMismatch");
				if (line.Type is InventoryTransactionType.Consume or InventoryTransactionType.Issue && lot.ExpiresOn <= Now) throw new InventoryException(409, "LotExpired");
			}
			else if (item.RequiresLotTracking || item.RequiresExpiration && item.TrackingMode == (int)InventoryTrackingMode.Bulk) throw new InventoryException(400, "LotRequired");
			if (item.TrackingMode == (int)InventoryTrackingMode.Serialized)
			{
				if (line.Type == InventoryTransactionType.Adjust && line.ReversesTransactionId == null) throw new InventoryException(400, "SerializedAdjustmentUnsupported");
				if (line.AssetId == null || line.Type != InventoryTransactionType.StatusChange && line.Quantity != 1) throw new InventoryException(400, "SerializedQuantity");
			}
			else if (line.AssetId != null || line.Type == InventoryTransactionType.StatusChange) throw new InventoryException(400, "AssetMismatch");
		}
		// Recheck the original principal without using another person's protected-data grant.
		private async Task RequireCommandAccessAsync(InventoryActor actor, InventoryCommand command, bool joined = false)
		{
			await _auth.RequireAsync(actor);
			if (command?.Lines == null || command.Lines.Count is < 1 or > 100 || command.Lines.Any(l => l == null)) throw new InventoryException(400, "InvalidLines");
			foreach (var line in command.Lines)
			{
				var permission = line.Type == InventoryTransactionType.Transfer ? PermissionTypes.TransferInventory : line.Type is InventoryTransactionType.Issue or InventoryTransactionType.Return ? PermissionTypes.IssueInventory : PermissionTypes.AdjustInventory;
				if (line.FromLocationId != null) await LocationAsync(actor, line.FromLocationId, true, permission, historical: line.ReversesTransactionId != null);
				if (line.ToLocationId != null) await LocationAsync(actor, line.ToLocationId, true, permission, historical: line.ReversesTransactionId != null);
				if (line.FromLocationId == null && line.ToLocationId == null) await _auth.RequireAsync(actor, true, permission);
				if (line.AssetId != null)
				{
					Id(line.AssetId); var asset = await _store.GetAsync<InventoryAsset>(actor.DepartmentId, line.AssetId);
					if (asset == null) throw new InventoryException(404, "Unavailable");
					if (asset.CurrentLocationId != null) await LocationAsync(actor, asset.CurrentLocationId, true, permission);
				}
				Id(line.ItemId); var item = await _store.GetAsync<InventoryItem>(actor.DepartmentId, line.ItemId);
				if (item == null) throw new InventoryException(404, "Unavailable");
				if (item.IsControlledSubstance) await _auth.RequireAsync(actor, true, PermissionTypes.ManageControlledSubstances);
				if (!joined && line.ReversesTransactionId != null)
				{
					if ((await _store.GetAsync<InventoryTransaction>(actor.DepartmentId, line.ReversesTransactionId))?.PurchaseOrderItemId != null) throw new InventoryException(409, "PurchaseReceiptImmutable");
					var linked = await _store.RelatedAsync<RecordInventoryUsage>(actor.DepartmentId, "TransactionId", line.ReversesTransactionId);
					foreach (var usage in linked)
					{
						if (!item.IsControlledSubstance || _recordUsage == null) throw new InventoryException(409, "RecordUsageCorrectionRequired");
						await _recordUsage.Value.RequireUsageCorrectionAccessAsync(actor, usage.Id);
					}
				}
				await ValidateReferenceAsync(actor, line, joined, requireOpen: false);
			}
		}
		private async Task<bool> RequiresWitnessAsync(InventoryActor actor, IEnumerable<string> itemIds)
		{
			var controlled = false;
			foreach (var id in itemIds.Distinct())
			{
				Id(id); var item = await _store.GetAsync<InventoryItem>(actor.DepartmentId, id);
				if (item == null) throw new InventoryException(404, "Unavailable");
				controlled |= item.IsControlledSubstance;
			}
			return controlled;
		}
		private async Task<InventoryResult> AwaitWitnessAsync(InventoryActor actor, InventoryOperation operation, string kind, InventoryCommand command = null,
			List<InventoryIssueInput> issues = null, InventoryReturnInput returned = null, string assetId = null)
		{
			operation.Content = JsonConvert.SerializeObject(new InventoryOperationContent { PendingKind = kind, PendingCommand = command, PendingIssues = issues,
				PendingReturn = returned, PerformerId = actor.UserId });
			await SaveAsync(actor, operation, false); await AuditAsync(actor, operation, "InventoryWitnessRequested");
			return new InventoryResult { OperationId = operation.Id, AwaitingWitness = true, AssetId = assetId };
		}
		public Task<InventoryResult> PostTransactionAsync(InventoryActor actor, InventoryCommand command, CancellationToken ct = default) => TransactionAsync(actor, async events =>
		{
			ct.ThrowIfCancellationRequested(); await RequireCommandAccessAsync(actor, command);
			if (command.Lines.Any(l => l.Type == InventoryTransactionType.Transfer)) throw new InventoryException(400, "TransferCommandRequired");
			return await OperationAsync(actor, command.RequestId, new { Kind = "Post", command.Lines }, async (operation, pending) =>
			{
				await ValidateCommandAsync(actor, command);
				if (await RequiresWitnessAsync(actor, command.Lines.Select(l => l.ItemId))) return await AwaitWitnessAsync(actor, operation, "Post", command);
				return await PostLinesAsync(actor, operation, command, pending);
			}, events);
		});
		public async Task<InventoryResult> PostWithinTransactionAsync(InventoryActor actor, InventoryCommand command, CancellationToken ct = default)
		{
			if (_uow.Transaction == null) throw new InvalidOperationException("An owning transaction is required.");
			if (!await _store.HasLegacyMigrationAsync(actor.DepartmentId)) throw new InventoryException(409, "MigrationRequired");
			ct.ThrowIfCancellationRequested(); await _store.LockDepartmentAsync(actor.DepartmentId);
			if (!await _auth.IsEnabledAsync(actor.DepartmentId)) throw new InventoryException(409, "InventoryDisabled");
			var preflight = await _write.PreflightWriteAsync(actor.DepartmentId, actor.GrantToken, actor.UserId, false);
			if (preflight?.Success != true) throw new InventoryException(403, "ProtectedDataRequired");
			await RequireCommandAccessAsync(actor, command, joined: true);
			if (command.Lines.Any(l => l.Type == InventoryTransactionType.Transfer)) throw new InventoryException(400, "TransferCommandRequired");
			foreach (var line in command.Lines) if ((await _store.GetAsync<InventoryItem>(actor.DepartmentId, line.ItemId)).IsControlledSubstance) throw new InventoryException(409, "IndependentWitnessRequired");
			var events = new List<long>();
			return await OperationAsync(actor, command.RequestId, new { Kind = "Post", command.Lines }, async (op, pending) =>
			{
				await ValidateCommandAsync(actor, command, joined: true);
				return await PostLinesAsync(actor, op, command, pending);
			}, events);
		}
		public Task<InventoryResult> WitnessAsync(InventoryActor actor, string requestId, string attestation) => TransactionAsync(actor, async events =>
		{
			await _auth.RequireAsync(actor, true, PermissionTypes.ManageControlledSubstances); Text(attestation, 4000); Id(requestId);
			var operation = await _store.RequestAsync(actor.DepartmentId, requestId); if (operation == null) throw new InventoryException(404, "Unavailable");
			var receipt = Decode<InventoryOperationContent>(await RevealAsync(actor, operation));
			if (operation.State == 2 && receipt.WitnessId == actor.UserId && receipt.Attestation == attestation)
			{
				if (receipt.Result == null) throw new InventoryException(409, "RequestInProgress");
				if (receipt.PendingCount != null)
				{
					await RequireCountAccessAsync(actor, await _store.GetAsync<InventoryCount>(actor.DepartmentId, receipt.PendingCount.CountId));
					foreach (var id in receipt.Result.OutboxIds ?? new()) if (!events.Contains(id)) events.Add(id);
				}
				if (receipt.PendingPurchaseReceipt != null)
				{
					await RequirePurchaseReceiptAccessAsync(actor, receipt.PendingPurchaseReceipt);
					foreach (var id in receipt.Result.OutboxIds ?? new()) if (!events.Contains(id)) events.Add(id);
				}
				foreach (var id in receipt.Result.TransactionIds)
				{
					var transaction = await _store.GetAsync<InventoryTransaction>(actor.DepartmentId, id);
					if (transaction == null || transaction.OperationId != operation.Id) throw new InventoryException(404, "Unavailable");
					await AuthorizeRowAsync(actor, transaction);
				}
				return receipt.Result;
			}
			if (operation.State != 1 || string.IsNullOrWhiteSpace(receipt.PerformerId) || receipt.PerformerId == actor.UserId || receipt.PerformerId != operation.CreatedBy) throw new InventoryException(409, "IndependentWitnessRequired");
			var performer = new InventoryActor { DepartmentId = actor.DepartmentId, UserId = receipt.PerformerId };
			await _auth.RequireAsync(performer, true, PermissionTypes.ManageControlledSubstances);
			InventoryResult result;
			switch (receipt.PendingKind ?? "Post")
			{
				case "Count":
					if (receipt.PendingCount == null) throw new InventoryException(409, "CountStateConflict");
					await RequireCountAccessAsync(performer, await _store.GetAsync<InventoryCount>(actor.DepartmentId, receipt.PendingCount.CountId));
					result = await CompleteCountLinesAsync(actor, operation, receipt.PendingCount, events, performer.UserId, actor.UserId, attestation);
					break;
				case "PurchaseReceive":
					await RequirePurchaseReceiptAccessAsync(performer, receipt.PendingPurchaseReceipt);
					result = await CompletePurchaseReceiptAsync(actor, operation, receipt.PendingPurchaseReceipt, events, performer.UserId, actor.UserId, attestation);
					break;
				case "WorkOrder":
                        if (_workOrderMaintenance == null || receipt.PendingCommand?.Lines?.Count != 1) throw new InventoryException(409, "ReferenceUnavailable");
                        await RequireCommandAccessAsync(performer, receipt.PendingCommand, joined: true);
                        await RequireWorkOrderPartAsync(performer, receipt.PendingCommand.Lines[0]);
                        await RequireCommandAccessAsync(actor, receipt.PendingCommand, joined: true);
                        await ValidateCommandAsync(actor, receipt.PendingCommand, joined: true);
                        result = await PostLinesAsync(actor, operation, receipt.PendingCommand, events, performer.UserId, actor.UserId, attestation);
                        await _workOrderMaintenance.Value.CompleteInventoryPartAsync(actor, receipt.PendingCommand.Lines[0].WorkOrderPartId.Value, result.TransactionIds.Single(), receipt.PendingCommand.Lines[0].ReversesTransactionId != null, events);
                        break;
                    case "Post":
				case "Transfer":
				case "Receive":
					await RequireCommandAccessAsync(performer, receipt.PendingCommand);
					await RequireCommandAccessAsync(actor, receipt.PendingCommand);
					await ValidateCommandAsync(actor, receipt.PendingCommand);
					if (receipt.PendingKind == "Transfer") result = await CompleteTransferAsync(actor, operation, receipt.PendingCommand, events, performer.UserId, actor.UserId, attestation);
					else if (receipt.PendingKind == "Receive") result = await ReceiveAssetAsync(actor, operation, receipt.PendingCommand, events, performer.UserId, actor.UserId, attestation);
					else result = await PostLinesAsync(actor, operation, receipt.PendingCommand, events, performer.UserId, actor.UserId, attestation);
					break;
				case "Issue":
				case "KitIssue":
					if (receipt.PendingIssues == null || receipt.PendingIssues.Count is < 1 or > 100) throw new InventoryException(409, "IndependentWitnessRequired");
					foreach (var input in receipt.PendingIssues) await ValidateIssueAccessAsync(performer, input);
					result = await IssueLinesAsync(actor, operation, receipt.PendingIssues, events, performer.UserId, actor.UserId, attestation);
					break;
				case "Return":
					if (receipt.PendingReturn == null) throw new InventoryException(409, "IndependentWitnessRequired");
					await RequireReturnAccessAsync(performer, receipt.PendingReturn);
					result = await ReturnLinesAsync(actor, operation, receipt.PendingReturn, events, performer.UserId, actor.UserId, attestation);
					break;
				default: throw new InventoryException(409, "IndependentWitnessRequired");
			}
			result.OutboxIds = events.ToList();
			// Clearing the protected pending request and all movement effects commit atomically.
			receipt.PendingCommand = null; receipt.PendingIssues = null; receipt.PendingReturn = null; receipt.PendingKind = null;
			receipt.WitnessId = actor.UserId; receipt.WitnessedOn = Now; receipt.Attestation = attestation; receipt.Result = result;
			operation.State = 2; operation.WitnessUserId = actor.UserId; operation.Content = JsonConvert.SerializeObject(receipt); await SaveAsync(actor, operation, false); return result;
		});
		private async Task<InventoryResult> PostLinesAsync(InventoryActor actor, InventoryOperation operation, InventoryCommand command, List<long> events, string performer = null, string witness = null, string attestation = null)
		{
			var result = new InventoryResult { OperationId = operation.Id };
			for (var index = 0; index < command.Lines.Count; index++)
			{
				var line = command.Lines[index]; var item = await GetAsync<InventoryItem>(actor, line.ItemId);
				var transaction = New<InventoryTransaction>(actor); transaction.OperationId = operation.Id; transaction.LineNumber = index;
				transaction.ItemId = item.Id; transaction.AssetId = line.AssetId; transaction.LotId = line.LotId; transaction.Quantity = line.Quantity;
				transaction.FromLocationId = line.FromLocationId; transaction.ToLocationId = line.ToLocationId; transaction.TransactionType = (int)line.Type;
				transaction.ReferenceType = (int)line.ReferenceType; transaction.ReferenceId = line.ReferenceId; transaction.OccurredOn = Now;
				transaction.ReversesTransactionId = line.ReversesTransactionId; transaction.IssuanceId = line.IssuanceId; transaction.PurchaseOrderItemId = line.PurchaseOrderItemId;
				transaction.CountItemId = line.CountItemId; transaction.WorkOrderPartId = line.WorkOrderPartId;
				transaction.WorkOrderPartMovementId = line.WorkOrderPartMovementId;
				if (line.ReversesTransactionId != null)
				{
					var original = await GetAsync<InventoryTransaction>(actor, line.ReversesTransactionId);
					if (original.PurchaseOrderItemId != null) throw new InventoryException(409, "PurchaseReceiptImmutable");
					if (original.WorkOrderPartId.HasValue && original.WorkOrderPartId != line.WorkOrderPartId) throw new InventoryException(409, "WorkOrderPostingRequired");
					if (original.CountItemId != null) throw new InventoryException(409, "CountReceiptImmutable");
					if (original.ReferenceType == (int)InventoryReferenceType.RmsRecord && (line.ReferenceType != InventoryReferenceType.RmsRecord || line.ReferenceId != original.ReferenceId)) throw new InventoryException(409, "RecordUsageCorrectionRequired");
					if (original.ReversesTransactionId != null || original.ItemId != item.Id || original.AssetId != line.AssetId || original.LotId != line.LotId || original.Quantity != line.Quantity
						|| original.FromLocationId != line.ToLocationId || original.ToLocationId != line.FromLocationId || original.TransactionType is 4 or 5 or 9
						|| original.TransactionType == 0 && (line.UsageId == null || line.ReferenceType != InventoryReferenceType.RmsRecord || original.FromLocationId == null || original.ToLocationId != null)
						|| (await _store.RelatedAsync<InventoryTransaction>(actor.DepartmentId, "ReversesTransactionId", original.Id)).Count > 0) throw new InventoryException(409, "InvalidReversal");
				}
				var postingCost = await CostForPostingAsync(actor, item, line);
				if (item.TrackingMode == (int)InventoryTrackingMode.Bulk)
				{
					if (line.FromLocationId != null)
					{
						var stock = await _store.ApplyStockDeltaAsync(actor.DepartmentId, item.Id, line.FromLocationId, line.LotId, -line.Quantity, actor.UserId);
						if (stock.Quantity < await AllocatedQuantityAsync(actor.DepartmentId, line)) throw new InventoryException(409, "InsufficientStock");
						transaction.FromQuantityAfter = stock.Quantity; transaction.FromQuantityBefore = stock.Quantity + line.Quantity;
					}
					if (line.ToLocationId != null)
					{
						var stock = await _store.ApplyStockDeltaAsync(actor.DepartmentId, item.Id, line.ToLocationId, line.LotId, line.Quantity, actor.UserId);
						transaction.ToQuantityAfter = stock.Quantity; transaction.ToQuantityBefore = stock.Quantity - line.Quantity;
					}
				}
				else await MoveAssetAsync(actor, line, transaction, postingCost.UnitCost);
				var cost = postingCost.UnitCost;
				transaction.CreatedBy = performer ?? actor.UserId;
				var serial = line.AssetId == null ? null : Decode<InventoryAssetContent>(await GetAsync<InventoryAsset>(actor, line.AssetId)).SerialNumber;
				transaction.Content = JsonConvert.SerializeObject(new { line.Note, ItemName = Decode<InventoryItemContent>(item).Name, UnitOfMeasure = Decode<InventoryItemContent>(item).UnitOfMeasure, SerialNumber = serial, UnitCost = cost, TotalCost = cost.HasValue ? (decimal?)Money(cost.Value * line.Quantity) : null, postingCost.CurrencyCode, PerformerId = performer ?? actor.UserId, WitnessUserId = witness, WitnessedOn = witness == null ? (DateTime?)null : Now, Attestation = attestation });
				await ApplyAverageCostAsync(actor, item, postingCost);
				await SaveAsync(actor, transaction); await AuditAsync(actor, transaction, "InventoryTransactionPosted"); result.TransactionIds.Add(transaction.Id);
				await EventAsync(transaction, WorkflowTriggerEventType.InventoryAdjusted, events, usageId: line.UsageId, usageType: line.UsageType);
				if (transaction.OldStatus != transaction.NewStatus) await EventAsync(transaction, WorkflowTriggerEventType.InventoryAssetStatusChanged, events);
				if (item.IsControlledSubstance) { if (witness == null) throw new InventoryException(409, "IndependentWitnessRequired"); await EventAsync(transaction, WorkflowTriggerEventType.ControlledSubstanceRecorded, events); }
			}
			await RefreshPostingAlertsAsync(actor, command, events);
			result.OutboxIds = events.ToList(); return result;
		}
		private async Task MoveAssetAsync(InventoryActor actor, InventoryPosting line, InventoryTransaction transaction, decimal? receiptCost)
		{
			var asset = await GetAsync<InventoryAsset>(actor, line.AssetId);
			if (line.FromLocationId != null && await AllocatedQuantityAsync(actor.DepartmentId, line) > 0) throw new InventoryException(409, "AssetNotAvailable");
			if (asset.ItemId != line.ItemId || asset.LotId != line.LotId || asset.IsDeleted || line.ExpectedAssetRevision.HasValue && asset.Revision != line.ExpectedAssetRevision) throw new InventoryException(409, "AssetConflict");
			if (line.Type == InventoryTransactionType.Adjust && line.ReversesTransactionId != null)
			{
				var original = await GetAsync<InventoryTransaction>(actor, line.ReversesTransactionId);
				var latest = (await _store.RelatedAsync<InventoryTransaction>(actor.DepartmentId, "AssetId", asset.Id)).OrderByDescending(x => x.EntryId).FirstOrDefault();
				if (original.TransactionType != (int)InventoryTransactionType.Consume || original.NewStatus != (int)InventoryAssetStatus.Consumed || original.OldStatus is not (0 or 2 or 3)
					|| asset.Status != original.NewStatus || asset.CurrentLocationId != original.FromLocationId || latest?.Id != original.Id || line.FromLocationId != null || line.ToLocationId != original.FromLocationId)
					throw new InventoryException(409, "AssetReversalConflict");
				transaction.OldStatus = asset.Status; transaction.NewStatus = original.OldStatus; asset.Status = original.OldStatus.Value;
				await SaveAsync(actor, asset, false); return;
			}
			// Terminal holders remain for audit access, never as stock available for another disposal or implicit recovery.
			if (asset.Status is 4 or 5 or 6 && (line.Type != InventoryTransactionType.StatusChange || line.Status is InventoryAssetStatus.InService or InventoryAssetStatus.Issued or InventoryAssetStatus.OutForRepair or InventoryAssetStatus.Damaged))
				throw new InventoryException(409, "AssetNotAvailable");
			if (asset.CurrentLocationId == null && line.Type != InventoryTransactionType.Receive) throw new InventoryException(409, "AssetNotAvailable");
			if (line.Type != InventoryTransactionType.Receive && asset.CurrentLocationId != line.FromLocationId) throw new InventoryException(409, "AssetLocationConflict");
			if (line.Type == InventoryTransactionType.Receive && asset.CurrentLocationId != null) throw new InventoryException(409, "AssetAlreadyReceived");
			if (line.Type == InventoryTransactionType.Receive)
			{
				var details = Decode<InventoryAssetContent>(asset);
				details.AcquisitionCost ??= receiptCost; asset.Content = JsonConvert.SerializeObject(details);
			}
			if (line.Type is InventoryTransactionType.Issue or InventoryTransactionType.Transfer && (asset.Status != (int)InventoryAssetStatus.InService || asset.ExpiresOn <= Now)) throw new InventoryException(409, "AssetNotAvailable");
			if (line.Type == InventoryTransactionType.Consume && asset.ExpiresOn <= Now) throw new InventoryException(409, "AssetNotAvailable");
			var issuance = (await _store.RelatedAsync<InventoryIssuance>(actor.DepartmentId, "AssetId", asset.Id)).SingleOrDefault(x => x.Status is 0 or 2);
			if (issuance != null && line.Type is not (InventoryTransactionType.Return or InventoryTransactionType.StatusChange) && !(line.Type == InventoryTransactionType.Issue && issuance.Id == line.IssuanceId)) throw new InventoryException(409, "ReturnAssetFirst");
			var status = line.Type switch { InventoryTransactionType.Issue => InventoryAssetStatus.Issued, InventoryTransactionType.Consume => InventoryAssetStatus.Consumed,
				InventoryTransactionType.WriteOff or InventoryTransactionType.Count => InventoryAssetStatus.Lost, InventoryTransactionType.Return or InventoryTransactionType.StatusChange => line.Status ?? InventoryAssetStatus.InService, _ => (InventoryAssetStatus)asset.Status };
			if (status is InventoryAssetStatus.InService or InventoryAssetStatus.Issued && _maintenanceOrders != null && (await _maintenanceOrders.ActiveHoldsAsync(actor.DepartmentId, null, asset.Id)).Count > 0) throw new InventoryException(409, "AssetSafetyHold");
            if (line.Type == InventoryTransactionType.StatusChange)
			{
				if (asset.Status == (int)status) throw new InventoryException(409, "StatusUnchanged");
				if (status == InventoryAssetStatus.Issued && issuance == null || status == InventoryAssetStatus.InService && issuance != null) throw new InventoryException(409, "IssuanceStatusConflict");
				transaction.ToLocationId = asset.CurrentLocationId;
				if (issuance != null && status is InventoryAssetStatus.Lost or InventoryAssetStatus.Consumed or InventoryAssetStatus.Retired)
				{ issuance.Status = (int)(status == InventoryAssetStatus.Consumed ? InventoryIssuanceStatus.Consumed : InventoryIssuanceStatus.Lost); issuance.ReturnedOn = Now; await SaveAsync(actor, issuance, false); }
			}
			else
			{
				if (line.ToLocationId != null)
				{
					var target = await _store.GetAsync<InventoryLocation>(actor.DepartmentId, line.ToLocationId); var visited = new HashSet<string>();
					while (target != null) { if (!visited.Add(target.Id) || target.ContainerAssetId == asset.Id || visited.Count > 32) throw new InventoryException(400, "InvalidLocationHierarchy"); target = target.ContainerAssetId != null ? await _store.GetAsync<InventoryLocation>(actor.DepartmentId, (await _store.GetAsync<InventoryAsset>(actor.DepartmentId, target.ContainerAssetId))?.CurrentLocationId) : target.ParentLocationId == null ? null : await _store.GetAsync<InventoryLocation>(actor.DepartmentId, target.ParentLocationId); }
				}
				// Retain the last holder for access control and audit after terminal disposal.
				asset.CurrentLocationId = line.ToLocationId ?? asset.CurrentLocationId;
			}
			transaction.OldStatus = asset.Status; transaction.NewStatus = (int)status; asset.Status = (int)status; await SaveAsync(actor, asset, false);
		}
		private async Task EventAsync(InventoryTransaction transaction, WorkflowTriggerEventType trigger, List<long> events, string transferId = null, string usageId = null, InventoryUsageType? usageType = null)
		{
			var entry = await _outbox.EnqueueAsync(transaction.DepartmentId, "Inventory", new DomainEventEnvelope
			{
				EventName = trigger.ToString(), SchemaVersion = 1, AggregateType = transferId != null ? "InventoryTransfer" : transaction.AssetId != null ? "InventoryAsset" : "InventoryItem",
				AggregateId = transferId ?? transaction.AssetId ?? transaction.ItemId, Trigger = trigger, CorrelationId = transaction.OperationId, OccurredOn = transaction.OccurredOn,
				Payload = new { InventoryEvent = true, TransactionId = transaction.Id, transaction.ItemId, transaction.AssetId, transaction.LotId, UsageId = usageId, UsageType = usageType, TransferId = transferId, transaction.IssuanceId, transaction.TransactionType, transaction.Quantity,
					transaction.FromLocationId, transaction.ToLocationId, transaction.FromQuantityBefore, transaction.FromQuantityAfter, transaction.ToQuantityBefore, transaction.ToQuantityAfter,
					transaction.OldStatus, transaction.NewStatus, transaction.ReferenceType, transaction.ReferenceId, transaction.OccurredOn, transaction.ReversesTransactionId, transaction.PurchaseOrderItemId, transaction.CountItemId,
					CountId = transaction.ReferenceType == (int)InventoryReferenceType.Count ? transaction.ReferenceId : null }
			}); events.Add(entry.DomainEventOutboxId);
		}
		public Task<InventoryResult> CreateAndCompleteTransferAsync(InventoryActor actor, InventoryCommand command) => TransactionAsync(actor, async events =>
		{
			await RequireCommandAccessAsync(actor, command); if (command.Lines.Any(l => l.Type != InventoryTransactionType.Transfer)) throw new InventoryException(400, "TransferLinesRequired");
			if (command.Lines.Select(l => l.FromLocationId).Distinct().Count() != 1 || command.Lines.Select(l => l.ToLocationId).Distinct().Count() != 1) throw new InventoryException(400, "TransferLocationsRequired");
			return await OperationAsync(actor, command.RequestId, new { Kind = "Transfer", command.Lines }, async (op, pending) =>
			{
				await ValidateCommandAsync(actor, command);
				if (await RequiresWitnessAsync(actor, command.Lines.Select(l => l.ItemId))) return await AwaitWitnessAsync(actor, op, "Transfer", command);
				return await CompleteTransferAsync(actor, op, command, pending);
			}, events);
		});
		private async Task<InventoryResult> CompleteTransferAsync(InventoryActor actor, InventoryOperation operation, InventoryCommand command, List<long> events,
			string performer = null, string witness = null, string attestation = null)
		{
			if (command.Lines.Any(l => l.Type != InventoryTransactionType.Transfer) || command.Lines.Select(l => l.FromLocationId).Distinct().Count() != 1 || command.Lines.Select(l => l.ToLocationId).Distinct().Count() != 1) throw new InventoryException(400, "TransferLinesRequired");
			var transfer = New<InventoryTransfer>(actor); transfer.CreatedBy = performer ?? actor.UserId; transfer.FromLocationId = command.Lines[0].FromLocationId;
			transfer.ToLocationId = command.Lines[0].ToLocationId; transfer.Status = 2; transfer.OperationId = operation.Id; await SaveAsync(actor, transfer);
			var result = await PostLinesAsync(actor, operation, command, events, performer, witness, attestation); result.TransferId = transfer.Id;
			for (var i = 0; i < command.Lines.Count; i++)
			{
				var line = command.Lines[i]; var detail = New<InventoryTransferItem>(actor); detail.CreatedBy = performer ?? actor.UserId;
				detail.TransferId = transfer.Id; detail.TransactionId = result.TransactionIds[i]; detail.ItemId = line.ItemId; detail.AssetId = line.AssetId; detail.LotId = line.LotId; detail.Quantity = line.Quantity; await SaveAsync(actor, detail);
			}
			await EventAsync(await _store.GetAsync<InventoryTransaction>(actor.DepartmentId, result.TransactionIds[0]), WorkflowTriggerEventType.InventoryTransferCompleted, events, transfer.Id);
			result.OutboxIds = events.ToList(); return result;
		}
		public Task<InventoryResult> ChangeAssetStatusAsync(InventoryActor actor, InventoryCommand command)
		{
			if (command?.Lines == null || command.Lines.Any(x => x.Type != InventoryTransactionType.StatusChange)) throw new InventoryException(400, "StatusLinesRequired");
			return PostTransactionAsync(actor, command);
		}
	}
}
