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
		public Task<InventoryItem> SaveItemAsync(InventoryActor actor, InventoryItemInput input) => TransactionAsync(actor, async events =>
		{
			await _auth.RequireAsync(actor, true);
			if (input?.Details == null || !Enum.IsDefined(input.TrackingMode)) throw new InventoryException(400, "InvalidItem");
			Text(input.Details.Name); Text(input.Details.UnitOfMeasure, 80);
			if (input.Details.Description?.Length > 16000 || input.Details.Code?.Length > 100 || input.Details.Barcode?.Length > 250
				|| input.Details.DefaultUnitCost < 0 || input.Details.MinLevel < 0 || input.Details.ReorderPoint < 0 || input.Details.DefaultExpirationDays < 0)
				throw new InventoryException(400, "InvalidItem");
			if (input.IsKit && input.TrackingMode != InventoryTrackingMode.Serialized) throw new InventoryException(400, "KitMustBeSerialized");
			if (input.RequiresExpiration && input.TrackingMode == InventoryTrackingMode.Bulk && !input.RequiresLotTracking) throw new InventoryException(400, "ExpiryRequiresLotTracking");
			if (input.CategoryId != null && (await GetAsync<InventoryCategory>(actor, input.CategoryId)).IsDeleted) throw new InventoryException(404, "Unavailable");
			if (input.IsControlledSubstance) await _auth.RequireAsync(actor, true, PermissionTypes.ManageControlledSubstances);
			var row = input.Id == null ? New<InventoryItem>(actor) : await GetAsync<InventoryItem>(actor, input.Id);
			Cost(input.Details.DefaultUnitCost);
			var existingDetails = Decode<InventoryItemContent>(row);
			input.Details.CurrencyCode = Currency(input.Details.CurrencyCode) ?? existingDetails.CurrencyCode;
			if (existingDetails.CurrencyCode != null && existingDetails.CurrencyCode != input.Details.CurrencyCode && ((await _store.RelatedAsync<InventoryTransaction>(actor.DepartmentId, "ItemId", row.Id)).Count != 0 || await HasOpenPurchaseLineAsync(actor, row.Id)))
				throw new InventoryException(409, "ItemCurrencyLocked");
			input.Details.AverageUnitCost = existingDetails.AverageUnitCost;
			if (input.Details.PreferredVendorId != null && input.Details.PreferredVendorId != existingDetails.PreferredVendorId && (await GetAsync<InventoryVendor>(actor, input.Details.PreferredVendorId)).IsDeleted) throw new InventoryException(409, "VendorUnavailable");
			if (input.Id != null && row.Revision != input.Revision) throw new InventoryException(409, "RevisionConflict");
			if (input.Id != null && (row.TrackingMode != (int)input.TrackingMode || row.IsKit != input.IsKit || row.RequiresLotTracking != input.RequiresLotTracking || row.RequiresExpiration != input.RequiresExpiration || row.IsControlledSubstance != input.IsControlledSubstance)
				&& ((await _store.RelatedAsync<InventoryTransaction>(actor.DepartmentId, "ItemId", row.Id)).Count > 0 || await HasOpenPurchaseLineAsync(actor, row.Id))) throw new InventoryException(409, "ItemTrackingLocked");
			foreach (var other in (await AllAsync<InventoryItem>(actor.DepartmentId)).Where(x => !x.IsDeleted && x.Id != row.Id))
			{
				var details = Decode<InventoryItemContent>(await RevealAsync(actor, other));
				if (string.Equals(details.Name?.Trim(), input.Details.Name.Trim(), StringComparison.OrdinalIgnoreCase)
					|| !string.IsNullOrWhiteSpace(input.Details.Barcode) && string.Equals(details.Barcode, input.Details.Barcode, StringComparison.OrdinalIgnoreCase)) throw new InventoryException(409, "DuplicateItem");
			}
			row.CategoryId = input.CategoryId; row.TrackingMode = (int)input.TrackingMode; row.IsKit = input.IsKit; row.RequiresLotTracking = input.RequiresLotTracking;
			row.RequiresExpiration = input.RequiresExpiration; row.IsControlledSubstance = input.IsControlledSubstance; row.IsActive = input.IsActive;
			input.Details.Name = input.Details.Name.Trim(); row.Content = JsonConvert.SerializeObject(input.Details);
			await SaveAsync(actor, row, input.Id == null); await AuditAsync(actor, row, "InventoryItemSaved");
			if (input.ParLevels != null) await SaveParLevelsAsync(actor, row, input.ParLevels);
			await RefreshLowStockAsync(actor, row.Id, events); return await RevealAsync(actor, row);
		});
		/// <summary>
		/// Sets or removes the listed locations' minimums for the item; locations not listed keep theirs, so a form showing only
		/// some locations never wipes the rest. Setting or removing needs write access to that location. Removing soft deletes;
		/// setting again revives the same row.
		/// </summary>
		private async Task SaveParLevelsAsync(InventoryActor actor, InventoryItem item, System.Collections.Generic.List<InventoryParLevelInput> input)
		{
			if (input.Count > 1000) throw new InventoryException(400, "InvalidParLevel");
			var changes = new System.Collections.Generic.Dictionary<string, decimal?>(StringComparer.Ordinal);
			foreach (var level in input.Where(x => x != null))
			{
				Id(level.LocationId);
				var minimum = level.MinimumQuantity;
				if (minimum.HasValue && (minimum.Value < 0 || minimum.Value > 1_000_000_000m || decimal.Round(minimum.Value, 6) != minimum.Value) || !changes.TryAdd(level.LocationId, minimum))
					throw new InventoryException(400, "InvalidParLevel");
			}
			var existing = (await _store.RelatedAsync<InventoryParLevel>(actor.DepartmentId, "ItemId", item.Id)).ToDictionary(x => x.LocationId, StringComparer.Ordinal);
			foreach (var change in changes)
			{
				var current = existing.GetValueOrDefault(change.Key);
				var live = current != null && !current.IsDeleted;
				if (!change.Value.HasValue ? !live : live && current.MinimumQuantity == change.Value.Value) continue;
				await LocationAsync(actor, change.Key, true);
				if (!change.Value.HasValue)
				{
					current.IsDeleted = true; await SaveAsync(actor, current, false); await AuditAsync(actor, current, "InventoryParLevelRemoved");
					continue;
				}
				var row = current ?? New<InventoryParLevel>(actor);
				row.ItemId = item.Id; row.LocationId = change.Key; row.MinimumQuantity = change.Value.Value; row.IsDeleted = false;
				await SaveAsync(actor, row, current == null); await AuditAsync(actor, row, "InventoryParLevelSaved");
			}
		}
		public async Task<System.Collections.Generic.List<InventoryParLevel>> GetParLevelsAsync(InventoryActor actor, System.Collections.Generic.IReadOnlyCollection<string> itemIds)
		{
			await _auth.RequireAsync(actor);
			if (itemIds == null || itemIds.Count > 500) throw new InventoryException(400, "InvalidPage");
			foreach (var id in itemIds) Id(id);
			var result = new System.Collections.Generic.List<InventoryParLevel>();
			if (itemIds.Count == 0) return result;
			foreach (var row in await _store.RelatedManyAsync<InventoryParLevel>(actor.DepartmentId, "ItemId", itemIds.Distinct().ToArray()))
			{
				if (row.IsDeleted) continue;
				try { await AuthorizeRowAsync(actor, row); }
				catch (InventoryException ex) when (ex.StatusCode is 403 or 404 || ex.Code == "LocationUnavailable") { continue; }
				result.Add(row);
			}
			return result;
		}
		public Task<InventoryCategory> SaveCategoryAsync(InventoryActor actor, string id, int revision, string name, string parentId) => TransactionAsync(actor, async events =>
		{
			await _auth.RequireAsync(actor, true); Text(name);
			var row = id == null ? New<InventoryCategory>(actor) : await GetAsync<InventoryCategory>(actor, id);
			if (id != null && row.Revision != revision) throw new InventoryException(409, "RevisionConflict");
			var parent = parentId; var depth = 0;
			while (parent != null) { if (parent == row.Id || ++depth > 32) throw new InventoryException(400, "InvalidCategoryHierarchy"); var p = await GetAsync<InventoryCategory>(actor, parent); if (p.IsDeleted) throw new InventoryException(404, "Unavailable"); parent = p.ParentCategoryId; }
			row.ParentCategoryId = parentId; row.Content = JsonConvert.SerializeObject(new InventoryLabel { Name = name.Trim() });
			await SaveAsync(actor, row, id == null); await AuditAsync(actor, row, "InventoryCategorySaved"); return await RevealAsync(actor, row);
		});
		public Task<InventoryLocation> SaveLocationAsync(InventoryActor actor, InventoryLocationInput input) => TransactionAsync(actor, async events =>
		{
			await _auth.RequireAsync(actor, true); if (input == null) throw new InventoryException(400, "LocationRequired"); Text(input.Name);
			var row = input.Id == null ? New<InventoryLocation>(actor) : await GetAsync<InventoryLocation>(actor, input.Id);
			if (input.Id != null && row.Revision != input.Revision) throw new InventoryException(409, "RevisionConflict");
			if (input.Id != null && (row.LocationType != (int)input.Type || row.GroupId != input.GroupId || row.UnitId != input.UnitId || row.UserId != input.UserId || row.ContainerAssetId != input.ContainerAssetId || row.ParentLocationId != input.ParentLocationId))
				throw new InventoryException(409, "LocationHolderImmutable");
			row.LocationType = (int)input.Type; row.GroupId = input.GroupId; row.UnitId = input.UnitId; row.UserId = input.UserId; row.ContainerAssetId = input.ContainerAssetId; row.ParentLocationId = input.ParentLocationId;
			await _auth.ValidateHolderAsync(actor, row);
			if (row.ParentLocationId != null)
			{
				if (input.Type is not (InventoryLocationType.Facility or InventoryLocationType.External) || row.ParentLocationId == row.Id) throw new InventoryException(400, "InvalidLocationHierarchy");
				await LocationAsync(actor, row.ParentLocationId, true);
			}
			if (row.ContainerAssetId != null)
			{
				var asset = await GetAsync<InventoryAsset>(actor, row.ContainerAssetId); var item = await GetAsync<InventoryItem>(actor, asset.ItemId);
				if (!item.IsKit || asset.IsDeleted) throw new InventoryException(400, "KitRequired");
				await LocationAsync(actor, asset.CurrentLocationId, true);
			}
			if (input.IsDefault && (input.Type != InventoryLocationType.Facility || input.ParentLocationId != null)) throw new InventoryException(400, "InvalidDefaultLocation");
			if (input.IsDefault) foreach (var old in (await AllAsync<InventoryLocation>(actor.DepartmentId)).Where(x => x.Id != row.Id && x.IsDefault)) { old.IsDefault = false; await SaveAsync(actor, old, false); }
			row.IsDefault = input.IsDefault; row.Content = JsonConvert.SerializeObject(new InventoryLabel { Name = input.Name.Trim() });
			await SaveAsync(actor, row, input.Id == null); await AuditAsync(actor, row, "InventoryLocationSaved"); return await RevealAsync(actor, row);
		});
		private async Task<InventoryLocation> HolderLocationAsync(InventoryActor actor, int? unitId, string userId)
		{
			var type = unitId.HasValue ? InventoryLocationType.Unit : InventoryLocationType.Personnel;
			var row = (await AllAsync<InventoryLocation>(actor.DepartmentId)).SingleOrDefault(l => !l.IsDeleted && l.LocationType == (int)type && l.UnitId == unitId && l.UserId == userId);
			if (row != null) { await LocationAsync(actor, row.Id, true, PermissionTypes.IssueInventory); return row; }
			row = New<InventoryLocation>(actor); row.LocationType = (int)type; row.UnitId = unitId; row.UserId = userId;
			await _auth.ValidateHolderAsync(actor, row); row.Content = JsonConvert.SerializeObject(new InventoryLabel { Name = type.ToString() }); await SaveAsync(actor, row); return row;
		}
		public Task<InventoryLot> SaveLotAsync(InventoryActor actor, InventoryLot lot, InventoryLotContent details) => TransactionAsync(actor, async events =>
		{
			await _auth.RequireAsync(actor, true); if (lot == null || details == null) throw new InventoryException(400, "LotRequired"); Text(details.LotNumber);
			var item = await GetAsync<InventoryItem>(actor, lot.ItemId);
			if (item.IsDeleted || !item.IsActive || item.RequiresExpiration && !lot.ExpiresOn.HasValue || details.UnitCost < 0) throw new InventoryException(400, "InvalidLot");
			Cost(details.UnitCost);
			if (details.VendorId != null && (await GetAsync<InventoryVendor>(actor, details.VendorId)).IsDeleted) throw new InventoryException(409, "VendorUnavailable");
			foreach (var other in await _store.RelatedAsync<InventoryLot>(actor.DepartmentId, "ItemId", item.Id))
				if (Decode<InventoryLotContent>(await RevealAsync(actor, other)).LotNumber == details.LotNumber) throw new InventoryException(409, "DuplicateLot");
			var row = New<InventoryLot>(actor); row.ItemId = item.Id; row.ExpiresOn = lot.ExpiresOn?.ToUniversalTime(); row.ReceivedOn = Now; row.Content = JsonConvert.SerializeObject(details);
			await SaveAsync(actor, row); await AuditAsync(actor, row, "InventoryLotCreated"); return await RevealAsync(actor, row);
		});
		public Task ArchiveAsync<T>(InventoryActor actor, string id, int revision) where T : InventoryMutableRow => TransactionAsync(actor, async events =>
		{
			await _auth.RequireAsync(actor, true); var row = await GetAsync<T>(actor, id); if (row.Revision != revision) throw new InventoryException(409, "RevisionConflict");
			if (row is not (InventoryItem or InventoryCategory or InventoryLocation or InventoryKit or InventoryVendor)) throw new InventoryException(400, "ArchiveNotSupported");
			if (row is InventoryVendor && (await _store.RelatedAsync<InventoryPurchaseOrder>(actor.DepartmentId, "VendorId", id)).Any(p => !p.IsDeleted && p.Status is 0 or 1 or 2)) throw new InventoryException(409, "VendorHasOpenOrders");
			if (row is InventoryItem item && ((await _store.RelatedAsync<InventoryStock>(actor.DepartmentId, "ItemId", id)).Any(x => x.Quantity != 0) || (await _store.RelatedAsync<InventoryAsset>(actor.DepartmentId, "ItemId", id)).Any(x => !x.IsDeleted && x.Status is not (4 or 5 or 6)))) throw new InventoryException(409, "StockRemains");
			if (row is InventoryLocation location && (location.IsDefault || (await _store.RelatedAsync<InventoryStock>(actor.DepartmentId, "LocationId", id)).Any(x => x.Quantity != 0) || (await _store.RelatedAsync<InventoryAsset>(actor.DepartmentId, "CurrentLocationId", id)).Any() || (await _store.RelatedAsync<InventoryLocation>(actor.DepartmentId, "ParentLocationId", id)).Any(x => !x.IsDeleted))) throw new InventoryException(409, "LocationInUse");
			if (row is InventoryCategory && ((await _store.RelatedAsync<InventoryItem>(actor.DepartmentId, "CategoryId", id)).Any(x => !x.IsDeleted) || (await _store.RelatedAsync<InventoryCategory>(actor.DepartmentId, "ParentCategoryId", id)).Any(x => !x.IsDeleted))) throw new InventoryException(409, "CategoryInUse");
			row.IsDeleted = true; await SaveAsync(actor, row, false); await AuditAsync(actor, row, "InventoryArchived");
			// An archived location holds no stock, so its par level alerts resolve.
			if (row is InventoryLocation)
				foreach (var itemId in (await _store.RelatedAsync<InventoryParLevel>(actor.DepartmentId, "LocationId", id)).Select(p => p.ItemId).Distinct())
					await RefreshLowStockAsync(actor, itemId, events);
			return true;
		});
		public Task RebuildStocksAsync(InventoryActor actor) => TransactionAsync(actor, async events => {
			await _auth.RequireAsync(actor, true); await _store.RebuildStocksAsync(actor.DepartmentId);
			await RefreshAllLowStockAsync(actor, events);
			return true;
		});
		public async Task<System.Collections.Generic.List<InventoryTransaction>> GetByReferenceAsync(InventoryActor actor, InventoryReferenceType type, string id)
		{
			await _auth.RequireAsync(actor); var result = new System.Collections.Generic.List<InventoryTransaction>();
			foreach (var row in (await _store.RelatedAsync<InventoryTransaction>(actor.DepartmentId, "ReferenceId", id)).Where(x => x.ReferenceType == (int)type)) { await AuthorizeRowAsync(actor, row); result.Add(await RevealAsync(actor, row)); } return result;
		}
	}
}
