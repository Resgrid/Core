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
		public async Task<InventoryAsset> CreateAssetAsync(InventoryActor actor, InventoryAssetInput input)
		{
			var result = await TransactionAsync(actor, async events =>
			{
				if (input?.Details == null) throw new InventoryException(400, "AssetRequired"); Text(input.Details.SerialNumber);
				await LocationAsync(actor, input.LocationId, true);
				if (await RequiresWitnessAsync(actor, new[] { input.ItemId })) await _auth.RequireAsync(actor, true, PermissionTypes.ManageControlledSubstances);
				return await OperationAsync(actor, input.RequestId, new { Kind = "Asset", input.Id, input.ItemId, input.LocationId, input.LotId, input.ExpiresOn, input.Details }, async (operation, pending) =>
				{
					var item = await GetAsync<InventoryItem>(actor, input.ItemId);
					if (item.TrackingMode != (int)InventoryTrackingMode.Serialized || item.IsDeleted || !item.IsActive || input.Details.AcquisitionCost < 0) throw new InventoryException(400, "InvalidAsset");
					if (item.RequiresExpiration && !input.ExpiresOn.HasValue) throw new InventoryException(400, "ExpiryRequired");
					var asset = await CreateUnreceivedAssetAsync(actor, item, input.Id, input.LotId, input.ExpiresOn, input.Details);
					var command = new InventoryCommand { RequestId = input.RequestId, Lines = new() { new InventoryPosting { ItemId = item.Id, AssetId = asset.Id, LotId = input.LotId, ToLocationId = input.LocationId, Quantity = 1, Type = InventoryTransactionType.Receive, UnitCost = input.Details.AcquisitionCost } } };
					await ValidateCommandAsync(actor, command);
					if (item.IsControlledSubstance) return await AwaitWitnessAsync(actor, operation, "Receive", command, assetId: asset.Id);
					return await ReceiveAssetAsync(actor, operation, command, pending);
				}, events);
			});
			return await GetAsync<InventoryAsset>(actor, result.AssetId);
		}
		private async Task<InventoryAsset> CreateUnreceivedAssetAsync(InventoryActor actor, InventoryItem item, string id, string lotId, DateTime? expiresOn, InventoryAssetContent details, string performer = null)
		{
			Text(details.SerialNumber); Cost(details.AcquisitionCost);
			if (details.AssetTag?.Length > 250 || details.Barcode?.Length > 250) throw new InventoryException(400, "InvalidAsset");
			foreach (var other in await _store.RelatedAsync<InventoryAsset>(actor.DepartmentId, "ItemId", item.Id))
				if (string.Equals(Decode<InventoryAssetContent>(await RevealAsync(actor, other)).SerialNumber, details.SerialNumber, StringComparison.OrdinalIgnoreCase)) throw new InventoryException(409, "DuplicateSerial");
			var asset = New<InventoryAsset>(actor); if (id != null) { Id(id); asset.Id = id; }
			asset.ItemId = item.Id; asset.LotId = lotId; asset.ExpiresOn = expiresOn?.ToUniversalTime(); asset.AcquiredOn = Now; asset.Status = (int)InventoryAssetStatus.InService;
			asset.CreatedBy = performer ?? actor.UserId; asset.Content = JsonConvert.SerializeObject(details); await SaveAsync(actor, asset); return asset;
		}
		private async Task CreateKitContainerAsync(InventoryActor actor, string assetId, string performer)
		{
			var location = New<InventoryLocation>(actor); location.CreatedBy = performer ?? actor.UserId;
			location.LocationType = (int)InventoryLocationType.Container; location.ContainerAssetId = assetId;
			location.Content = JsonConvert.SerializeObject(new InventoryLabel { Name = "Container" }); await SaveAsync(actor, location);
		}
		private async Task<InventoryResult> ReceiveAssetAsync(InventoryActor actor, InventoryOperation operation, InventoryCommand command, List<long> events,
			string performer = null, string witness = null, string attestation = null)
		{
			if (command.Lines.Count != 1 || command.Lines[0].Type != InventoryTransactionType.Receive || command.Lines[0].AssetId == null) throw new InventoryException(400, "InvalidAsset");
			var line = command.Lines[0]; var result = await PostLinesAsync(actor, operation, command, events, performer, witness, attestation); result.AssetId = line.AssetId;
			if ((await _store.GetAsync<InventoryItem>(actor.DepartmentId, line.ItemId)).IsKit)
				await CreateKitContainerAsync(actor, line.AssetId, performer);
			return result;
		}
		public Task<InventoryResult> IssueAsync(InventoryActor actor, InventoryIssueInput input) => TransactionAsync(actor, async events =>
		{
			if (input == null) throw new InventoryException(400, "IssuanceRequired"); await ValidateIssueAccessAsync(actor, input);
			return await OperationAsync(actor, input.RequestId, new { Kind = "Issue", Input = input }, async (op, pending) =>
			{
				await ValidateIssueAsync(actor, input);
				if (await RequiresWitnessAsync(actor, new[] { input.ItemId })) return await AwaitWitnessAsync(actor, op, "Issue", issues: new() { input });
				return await IssueLinesAsync(actor, op, new[] { input }, pending);
			}, events);
		});
		private async Task ValidateIssueAccessAsync(InventoryActor actor, InventoryIssueInput input)
		{
			if (input == null) throw new InventoryException(400, "IssuanceRequired");
			Quantity(input.Quantity); if ((input.UnitId.HasValue ? 1 : 0) + (input.UserId != null ? 1 : 0) != 1) throw new InventoryException(400, "HolderRequired");
			if (input.FromLocationId == null) throw new InventoryException(400, "InvalidMovement");
			await RequireCommandAccessAsync(actor, new InventoryCommand { RequestId = input.RequestId, Lines = new() { IssuePosting(input) } });
			var holder = New<InventoryLocation>(actor); holder.LocationType = input.UnitId.HasValue ? (int)InventoryLocationType.Unit : (int)InventoryLocationType.Personnel; holder.UnitId = input.UnitId; holder.UserId = input.UserId;
			await _auth.ValidateHolderAsync(actor, holder);
			var groupId = input.UnitId.HasValue ? (await _units.GetUnitByIdAsync(input.UnitId.Value))?.StationGroupId : (await _groups.GetGroupForUserAsync(input.UserId, actor.DepartmentId))?.DepartmentGroupId;
			await _auth.RequireAsync(actor, true, PermissionTypes.IssueInventory, groupId);
			var existing = (await AllAsync<InventoryLocation>(actor.DepartmentId)).SingleOrDefault(l => !l.IsDeleted && l.LocationType == holder.LocationType && l.UnitId == input.UnitId && l.UserId == input.UserId);
			if (existing != null) await LocationAsync(actor, existing.Id, true, PermissionTypes.IssueInventory);
		}
		private async Task ValidateIssueAsync(InventoryActor actor, InventoryIssueInput input)
		{
			await ValidateIssueAccessAsync(actor, input);
			if (input.Note?.Length > 16000) throw new InventoryException(400, "InvalidText");
			if (input.ExpectedReturnOn.HasValue && input.ExpectedReturnOn.Value.ToUniversalTime() <= Now) throw new InventoryException(400, "ReturnDateInPast");
			await ValidatePostingItemAsync(actor, IssuePosting(input));
			if (input.AssetId != null)
			{
				var asset = await GetAsync<InventoryAsset>(actor, input.AssetId);
				if (asset.IsDeleted || asset.ItemId != input.ItemId || asset.LotId != input.LotId) throw new InventoryException(409, "AssetConflict");
				if ((await _store.RelatedAsync<InventoryIssuance>(actor.DepartmentId, "AssetId", input.AssetId)).Any(x => x.Status is 0 or 2)) throw new InventoryException(409, "ReturnAssetFirst");
				if (asset.CurrentLocationId != input.FromLocationId) throw new InventoryException(409, "AssetLocationConflict");
				if (asset.Status != (int)InventoryAssetStatus.InService || asset.ExpiresOn <= Now) throw new InventoryException(409, "AssetNotAvailable");
			}
		}
		private static InventoryPosting IssuePosting(InventoryIssueInput input, string locationId = null, string issuanceId = null) => new InventoryPosting
		{
			ItemId = input.ItemId, AssetId = input.AssetId, LotId = input.LotId, FromLocationId = input.FromLocationId, ToLocationId = locationId,
			Quantity = input.Quantity, Type = InventoryTransactionType.Issue, IssuanceId = issuanceId, ReferenceType = input.ReferenceType, ReferenceId = input.ReferenceId, Note = input.Note
		};
		private async Task<InventoryResult> IssueLinesAsync(InventoryActor actor, InventoryOperation operation, IEnumerable<InventoryIssueInput> inputs, List<long> events,
			string performer = null, string witness = null, string attestation = null)
		{
			var command = new InventoryCommand { RequestId = operation.RequestId }; var issuances = new List<InventoryIssuance>();
			foreach (var input in inputs)
			{
				await ValidateIssueAsync(actor, input); var location = await HolderLocationAsync(actor, input.UnitId, input.UserId);
				var issuance = New<InventoryIssuance>(actor); issuance.CreatedBy = performer ?? actor.UserId; issuance.ItemId = input.ItemId; issuance.AssetId = input.AssetId; issuance.LotId = input.LotId;
				issuance.Quantity = input.Quantity; issuance.IssuedToUserId = input.UserId; issuance.IssuedToUnitId = input.UnitId; issuance.LocationId = location.Id;
				issuance.IssuedOn = Now; issuance.ExpectedReturnOn = input.ExpectedReturnOn?.ToUniversalTime(); issuance.Status = 0;
				issuance.ReferenceType = (int)input.ReferenceType; issuance.ReferenceId = input.ReferenceId; issuance.Content = JsonConvert.SerializeObject(new { input.Note, IssuedByUserId = performer ?? actor.UserId });
				// Keep these identities in memory until every line has validated.
				issuances.Add(issuance);
				command.Lines.Add(IssuePosting(input, location.Id, issuance.Id));
			}
			await ValidateCommandAsync(actor, command, true);
			// The ledger has an optional issuance FK, so allocate the issuance before posting and allow this exact new identity in MoveAssetAsync.
			foreach (var issuance in issuances) await SaveAsync(actor, issuance);
			var result = await PostLinesAsync(actor, operation, command, events, performer, witness, attestation);
			result.IssuanceIds = issuances.Select(i => i.Id).ToList(); result.IssuanceId = result.IssuanceIds.First();
			foreach (var id in result.TransactionIds) await EventAsync(await _store.GetAsync<InventoryTransaction>(actor.DepartmentId, id), WorkflowTriggerEventType.InventoryIssued, events);
			result.OutboxIds = events.ToList(); return result;
		}
		public Task<InventoryResult> ReturnAsync(InventoryActor actor, InventoryReturnInput input) => TransactionAsync(actor, async events =>
		{
			await RequireReturnAccessAsync(actor, input);
			return await OperationAsync(actor, input.RequestId, new { Kind = "Return", Input = input }, (operation, pending) => ReturnLinesAsync(actor, operation, input, pending), events);
		});
		private static InventoryCommand ReturnCommand(InventoryReturnInput input, InventoryIssuance issuance) => new InventoryCommand
		{
			RequestId = input.RequestId, Lines = new() { new InventoryPosting { ItemId = issuance.ItemId, AssetId = issuance.AssetId, LotId = issuance.LotId, FromLocationId = issuance.LocationId,
				ToLocationId = input.ToLocationId, Type = InventoryTransactionType.Return, Quantity = input.Quantity, Status = input.Condition, IssuanceId = issuance.Id,
				ReferenceType = (InventoryReferenceType)issuance.ReferenceType, ReferenceId = issuance.ReferenceId, Note = input.Note } }
		};
		private async Task RequireReturnAccessAsync(InventoryActor actor, InventoryReturnInput input)
		{
			if (input == null) throw new InventoryException(400, "IssuanceRequired"); Quantity(input.Quantity); Id(input.IssuanceId);
			if (input.ToLocationId == null) throw new InventoryException(400, "InvalidMovement");
			var issuance = await _store.GetAsync<InventoryIssuance>(actor.DepartmentId, input.IssuanceId); if (issuance == null) throw new InventoryException(404, "Unavailable");
			await RequireCommandAccessAsync(actor, ReturnCommand(input, issuance));
		}
		private async Task<InventoryResult> ReturnLinesAsync(InventoryActor actor, InventoryOperation operation, InventoryReturnInput input, List<long> events,
			string performer = null, string witness = null, string attestation = null)
		{
			await RequireReturnAccessAsync(actor, input); var issuance = await GetAsync<InventoryIssuance>(actor, input.IssuanceId);
			if (issuance.Revision != input.Revision || issuance.Status is not (0 or 2) || input.Quantity > issuance.Quantity - issuance.ReturnedQuantity) throw new InventoryException(409, "ReturnConflict");
			if (input.Condition is not (InventoryAssetStatus.InService or InventoryAssetStatus.Damaged or InventoryAssetStatus.OutForRepair)) throw new InventoryException(400, "InvalidReturnCondition");
			var command = ReturnCommand(input, issuance); await ValidateCommandAsync(actor, command, true);
			if (witness == null && await RequiresWitnessAsync(actor, new[] { issuance.ItemId }))
			{
				var pending = await AwaitWitnessAsync(actor, operation, "Return", returned: input); pending.IssuanceId = issuance.Id; return pending;
			}
			var result = await PostLinesAsync(actor, operation, command, events, performer, witness, attestation);
			issuance.ReturnedQuantity += input.Quantity; issuance.ReturnedToLocationId = input.ToLocationId;
			issuance.Status = (int)(issuance.ReturnedQuantity == issuance.Quantity ? InventoryIssuanceStatus.Returned : InventoryIssuanceStatus.PartiallyReturned);
			if (issuance.Status == (int)InventoryIssuanceStatus.Returned) issuance.ReturnedOn = Now;
			await SaveAsync(actor, issuance, false); result.IssuanceId = issuance.Id;
			await EventAsync(await _store.GetAsync<InventoryTransaction>(actor.DepartmentId, result.TransactionIds[0]), WorkflowTriggerEventType.InventoryReturned, events);
			result.OutboxIds = events.ToList(); return result;
		}
		public Task<InventoryKit> SaveKitAsync(InventoryActor actor, InventoryKitInput input) => TransactionAsync(actor, async events =>
		{
			await _auth.RequireAsync(actor, true); if (input?.Lines == null || input.Lines.Count is < 1 or > 100) throw new InventoryException(400, "InvalidKit"); Text(input.Name);
			if (input.Lines.Select(l => l.ItemId).Distinct().Count() != input.Lines.Count) throw new InventoryException(400, "DuplicateKitItem");
			var kit = input.Id == null ? New<InventoryKit>(actor) : await GetAsync<InventoryKit>(actor, input.Id);
			if (input.Id != null && kit.Revision != input.Revision) throw new InventoryException(409, "RevisionConflict");
			kit.Content = JsonConvert.SerializeObject(new InventoryLabel { Name = input.Name }); await SaveAsync(actor, kit, input.Id == null);
			foreach (var old in await _store.RelatedAsync<InventoryKitItem>(actor.DepartmentId, "KitId", kit.Id)) { old.IsDeleted = true; await SaveAsync(actor, old, false); }
			foreach (var line in input.Lines)
			{
				Quantity(line.Quantity); var item = await GetAsync<InventoryItem>(actor, line.ItemId); if (item.IsDeleted || !item.IsActive || item.TrackingMode == 1 && decimal.Truncate(line.Quantity) != line.Quantity) throw new InventoryException(400, "InvalidKitItem");
				var row = New<InventoryKitItem>(actor); row.KitId = kit.Id; row.ItemId = item.Id; row.Quantity = line.Quantity; await SaveAsync(actor, row);
			}
			await AuditAsync(actor, kit, "InventoryKitSaved"); return await RevealAsync(actor, kit);
		});
		public Task<InventoryResult> IssueKitAsync(InventoryActor actor, InventoryKitIssueInput input) => TransactionAsync(actor, async events =>
		{
			if (input?.Lines == null || input.Lines.Count is < 1 or > 100) throw new InventoryException(400, "InvalidKit");
			foreach (var line in input.Lines) await ValidateIssueAccessAsync(actor, line);
			return await OperationAsync(actor, input.RequestId, new { Kind = "KitIssue", input.KitId, input.Lines }, async (op, pending) =>
			{
				var kit = await GetAsync<InventoryKit>(actor, input.KitId); if (kit.IsDeleted) throw new InventoryException(404, "Unavailable");
				var expected = (await _store.RelatedAsync<InventoryKitItem>(actor.DepartmentId, "KitId", kit.Id)).Where(x => !x.IsDeleted).ToDictionary(x => x.ItemId, x => x.Quantity);
				var actual = input.Lines.GroupBy(x => x.ItemId).ToDictionary(x => x.Key, x => x.Sum(l => l.Quantity));
				if (expected.Count != actual.Count || expected.Any(x => !actual.TryGetValue(x.Key, out var quantity) || quantity != x.Value) || input.Lines.Select(l => (l.UnitId, l.UserId)).Distinct().Count() != 1) throw new InventoryException(400, "KitContentsMismatch");
				foreach (var line in input.Lines) await ValidateIssueAsync(actor, line);
				if (await RequiresWitnessAsync(actor, input.Lines.Select(l => l.ItemId))) return await AwaitWitnessAsync(actor, op, "KitIssue", issues: input.Lines);
				return await IssueLinesAsync(actor, op, input.Lines, pending);
			}, events);
		});
		private async Task<InventoryEquipment> EquipmentAsync(InventoryActor actor, InventoryAsset asset, InventoryStock stock = null)
		{
			var locationId = asset?.CurrentLocationId ?? stock.LocationId; var location = await LocationAsync(actor, locationId); var holder = await EffectiveLocationAsync(actor.DepartmentId, location);
			var item = await GetAsync<InventoryItem>(actor, asset?.ItemId ?? stock.ItemId); var result = new InventoryEquipment { Asset = asset == null ? null : await RevealAsync(actor, asset), Stock = stock, ItemName = Decode<InventoryItemContent>(item).Name, UnitId = holder.UnitId, GroupId = holder.GroupId, UserId = holder.UserId };
			if (holder.UnitId.HasValue) result.GroupId = (await _units.GetUnitByIdAsync(holder.UnitId.Value))?.StationGroupId;
			foreach (var issuance in await _store.RelatedAsync<InventoryIssuance>(actor.DepartmentId, asset != null ? "AssetId" : "ItemId", asset?.Id ?? stock.ItemId))
				if (issuance.Status is 0 or 2 && (asset != null || issuance.LocationId == stock.LocationId && issuance.LotId == stock.LotId)) result.Issuances.Add(await RevealAsync(actor, issuance));
			return result;
		}
		public async Task<List<InventoryEquipment>> GetUnitEquipmentAsync(InventoryActor actor, int unitId)
		{
			await _auth.RequireAsync(actor); var result = new List<InventoryEquipment>();
			foreach (var asset in (await AllAsync<InventoryAsset>(actor.DepartmentId)).Where(a => !a.IsDeleted && a.CurrentLocationId != null && a.Status is not (4 or 5 or 6)))
			{
				try { var equipment = await EquipmentAsync(actor, asset); if (equipment.UnitId == unitId) result.Add(equipment); } catch (InventoryException ex) when (ex.StatusCode is 403 or 404 || ex.Code == "LocationUnavailable") { }
			}
			foreach (var stock in (await AllAsync<InventoryStock>(actor.DepartmentId)).Where(s => s.Quantity > 0))
			{
				try { var equipment = await EquipmentAsync(actor, null, stock); if (equipment.UnitId == unitId) result.Add(equipment); } catch (InventoryException ex) when (ex.StatusCode is 403 or 404 || ex.Code == "LocationUnavailable") { }
			}
			return result;
		}
		public async Task<List<InventoryEquipment>> GetIssuableAsync(InventoryActor actor, string itemId = null, string locationId = null)
		{
			await _auth.RequireAsync(actor); var result = new List<InventoryEquipment>();
			foreach (var asset in (await AllAsync<InventoryAsset>(actor.DepartmentId)).Where(a => !a.IsDeleted && a.Status == 0 && a.CurrentLocationId != null && (!a.ExpiresOn.HasValue || a.ExpiresOn > Now) && (itemId == null || itemId == a.ItemId) && (locationId == null || locationId == a.CurrentLocationId)))
			{
				try { var item = await GetAsync<InventoryItem>(actor, asset.ItemId); if (item.IsActive && !item.IsDeleted) result.Add(await EquipmentAsync(actor, asset)); } catch (InventoryException ex) when (ex.StatusCode is 403 or 404 || ex.Code == "LocationUnavailable") { }
			}
			foreach (var stock in (await AllAsync<InventoryStock>(actor.DepartmentId)).Where(s => s.Quantity > 0 && (itemId == null || itemId == s.ItemId) && (locationId == null || locationId == s.LocationId)))
			{
				try { var item = await GetAsync<InventoryItem>(actor, stock.ItemId); var lot = stock.LotId == null ? null : await GetAsync<InventoryLot>(actor, stock.LotId); if (item.IsActive && !item.IsDeleted && (lot == null || !lot.IsDeleted && (!lot.ExpiresOn.HasValue || lot.ExpiresOn > Now))) result.Add(await EquipmentAsync(actor, null, stock)); } catch (InventoryException ex) when (ex.StatusCode is 403 or 404 || ex.Code == "LocationUnavailable") { }
			}
			return result;
		}
	}
}
