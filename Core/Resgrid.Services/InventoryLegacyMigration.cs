using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Model;
using Resgrid.Model.Inventories;

namespace Resgrid.Services
{
	public sealed partial class InventoryModernizationService
	{
		public Task<InventoryMigrationResult> MigrateLegacyAsync(InventoryActor actor) => TransactionAsync(actor, async events =>
		{
			// No source group is supplied: a group-locked adjustment permission cannot authorize department cutover.
			await _auth.RequireAsync(actor, true);
			if (await _store.HasLegacyMigrationAsync(actor.DepartmentId)) return new InventoryMigrationResult { AlreadyMigrated = true };
			if (_legacyInventory == null || _legacyTypes == null) throw new InventoryException(409, "LegacyInventoryUnavailable");
			if ((await _store.ListAsync<InventoryItem>(actor.DepartmentId)).Count != 0
				|| (await _store.ListAsync<InventoryTransaction>(actor.DepartmentId)).Count != 0
				|| (await _store.ListAsync<InventoryOperation>(actor.DepartmentId)).Count != 0)
				throw new InventoryException(409, "LegacyMigrationHasExistingInventory");

			var result = new InventoryMigrationResult();
			var types = (await _legacyTypes.GetAllByDepartmentIdAsync(actor.DepartmentId) ?? Enumerable.Empty<InventoryType>()).ToList();
			var entries = (await _legacyInventory.GetAllInventoriesByDepartmentIdAsync(actor.DepartmentId) ?? Enumerable.Empty<Inventory>()).ToList();
			if (types.Any(t => t == null || t.DepartmentId != actor.DepartmentId || t.InventoryTypeId <= 0)
				|| types.GroupBy(t => t.InventoryTypeId).Any(g => g.Count() != 1)) throw new InventoryException(409, "LegacyInventoryTypesInvalid");
			if (entries.Any(t => t == null || t.DepartmentId != actor.DepartmentId || t.InventoryId <= 0)
				|| entries.GroupBy(t => t.InventoryId).Any(g => g.Count() != 1)) throw new InventoryException(409, "LegacyInventoryEntriesInvalid");
			var typeIds = types.Select(t => t.InventoryTypeId).ToHashSet();
			var holders = new Dictionary<string, InventoryLocation>(StringComparer.Ordinal);
			var amounts = new Dictionary<int, decimal>();
			var sourceHolders = new Dictionary<int, string>();
			var balances = new Dictionary<(int TypeId, string Holder), decimal>();

			// Validate the complete source before inserting protected rows. Invalid holders are not silently reassigned.
			foreach (var source in entries.OrderBy(t => t.TimeStamp).ThenBy(t => t.InventoryId))
			{
				if (!typeIds.Contains(source.TypeId)) throw LegacyMigrationError("LegacyInventoryTypeMissing", source.InventoryId);
				var amount = LegacyQuantity(source.Amount, source.InventoryId);
				if (source.GroupId < 0 || source.UnitId <= 0) throw LegacyMigrationError("LegacyInventoryHolderInvalid", source.InventoryId);
				if (source.GroupId > 0)
					await ValidateLegacyHolderAsync(actor, NewLegacyLocation(actor, InventoryLocationType.Station, source.GroupId, null), source.InventoryId);
				var holder = source.UnitId.HasValue ? "unit:" + source.UnitId.Value.ToString(CultureInfo.InvariantCulture)
					: source.GroupId > 0 ? "station:" + source.GroupId.ToString(CultureInfo.InvariantCulture) : "unassigned";
				if (!holders.ContainsKey(holder))
				{
					var location = source.UnitId.HasValue ? NewLegacyLocation(actor, InventoryLocationType.Unit, null, source.UnitId)
						: source.GroupId > 0 ? NewLegacyLocation(actor, InventoryLocationType.Station, source.GroupId, null)
						: NewLegacyLocation(actor, InventoryLocationType.Facility, null, null);
					await ValidateLegacyHolderAsync(actor, location, source.InventoryId);
					holders.Add(holder, location);
				}
				if (source.UnitId.HasValue && source.GroupId > 0)
				{
					var unit = await _units.GetUnitByIdAsync(source.UnitId.Value);
					if (unit?.StationGroupId != source.GroupId) result.Warnings.Add("LegacyUnitLocationTakesPrecedence:" + source.InventoryId.ToString(CultureInfo.InvariantCulture));
				}
				if (holder == "unassigned") result.Warnings.Add("LegacyUnassignedLocation:" + source.InventoryId.ToString(CultureInfo.InvariantCulture));
				amounts.Add(source.InventoryId, amount); sourceHolders.Add(source.InventoryId, holder);
				var key = (source.TypeId, holder);
				balances.TryGetValue(key, out var prior);
				var next = prior + amount;
				if (Math.Abs(next) >= 1000000000000000000m) throw LegacyMigrationError("LegacyInventoryBalanceOverflow", source.InventoryId);
				balances[key] = next;
			}
			if (entries.Any(t => !string.IsNullOrEmpty(t.Batch))) result.Warnings.Add("LegacyBatchesPreservedAsSourceMetadata");
			if (balances.Values.Any(q => q < 0)) result.Warnings.Add("LegacyNegativeBalancesPreserved");

			var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var duplicateNames = types.GroupBy(t => t.Type?.Trim() ?? "", StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
			var itemMap = new Dictionary<int, InventoryItem>();
			foreach (var source in types.OrderBy(t => t.InventoryTypeId))
			{
				var original = source.Type?.Trim();
				var name = string.IsNullOrWhiteSpace(original) ? "Legacy item " + source.InventoryTypeId.ToString(CultureInfo.InvariantCulture) : original;
				var renamed = string.IsNullOrWhiteSpace(original) || name.Length > 250 || duplicateNames.Contains(original ?? "");
				if (name.Length > 250) name = name.Substring(0, 250);
				var stem = name; var suffixNumber = 0;
				if (renamed || names.Contains(name))
				{
					do
					{
						var suffix = " [legacy " + source.InventoryTypeId.ToString(CultureInfo.InvariantCulture) + (suffixNumber == 0 ? "" : "-" + suffixNumber.ToString(CultureInfo.InvariantCulture)) + "]";
						name = stem.Substring(0, Math.Min(stem.Length, 250 - suffix.Length)) + suffix; suffixNumber++;
					} while (names.Contains(name));
					result.Warnings.Add("LegacyItemNamePreservedWithIdentifier:" + source.InventoryTypeId.ToString(CultureInfo.InvariantCulture));
				}
				names.Add(name);
				var item = New<InventoryItem>(actor); item.LegacyInventoryTypeId = source.InventoryTypeId; item.TrackingMode = (int)InventoryTrackingMode.Bulk;
				var content = JObject.FromObject(new InventoryItemContent { Name = name, Description = source.Description,
					UnitOfMeasure = string.IsNullOrWhiteSpace(source.UnitOfMesasure) ? "unit" : source.UnitOfMesasure,
					DefaultExpirationDays = source.ExpiresDays > 0 ? source.ExpiresDays : null });
				content["LegacySource"] = JObject.FromObject(new { source.InventoryTypeId, source.Type, source.Description, source.UnitOfMesasure, source.ExpiresDays });
				item.Content = content.ToString(Formatting.None); await SaveAsync(actor, item); itemMap.Add(source.InventoryTypeId, item);
				result.Items++;
			}

			// An explicit default is also created for an empty department, ready for subsequent receipts.
			var locations = await AllAsync<InventoryLocation>(actor.DepartmentId);
			if (!holders.ContainsKey("unassigned")) holders.Add("unassigned", NewLegacyLocation(actor, InventoryLocationType.Facility, null, null));
			foreach (var holder in holders.Keys.ToArray())
			{
				var proposed = holders[holder];
				var existing = locations.Where(l => !l.IsDeleted && (holder == "unassigned" ? l.IsDefault
					: l.LocationType == proposed.LocationType && l.UnitId == proposed.UnitId && l.GroupId == proposed.GroupId && l.UserId == null && l.ContainerAssetId == null)).ToList();
				if (existing.Count > 1) throw new InventoryException(409, "LegacyInventoryLocationsAmbiguous");
				if (existing.Count == 1)
				{
					var location = existing[0];
					if (location.ParentLocationId != null || holder == "unassigned" && location.LocationType != (int)InventoryLocationType.Facility) throw new InventoryException(409, "LegacyInventoryLocationsAmbiguous");
					await _auth.ValidateHolderAsync(actor, location); holders[holder] = location;
				}
				else await SaveAsync(actor, proposed);
			}

			balances.Clear();
			foreach (var source in entries.OrderBy(t => t.TimeStamp).ThenBy(t => t.InventoryId))
			{
				var amount = amounts[source.InventoryId]; var holder = sourceHolders[source.InventoryId]; var location = holders[holder];
				var key = (source.TypeId, holder); balances.TryGetValue(key, out var before); var after = before + amount; balances[key] = after;
				var row = New<InventoryTransaction>(actor); row.CreatedBy = source.AddedByUserId; row.CreatedOn = source.TimeStamp; row.OccurredOn = source.TimeStamp;
				row.TransactionType = (int)InventoryTransactionType.Migrated; row.ItemId = itemMap[source.TypeId].Id; row.LegacyInventoryId = source.InventoryId;
				row.ReferenceType = (int)InventoryReferenceType.Legacy; row.ReferenceId = source.InventoryId.ToString(CultureInfo.InvariantCulture); row.Quantity = Math.Abs(amount);
				if (amount < 0) { row.FromLocationId = location.Id; row.FromQuantityBefore = before; row.FromQuantityAfter = after; }
				else { row.ToLocationId = location.Id; row.ToQuantityBefore = before; row.ToQuantityAfter = after; }
				row.Content = JsonConvert.SerializeObject(new { SchemaVersion = 1, LegacySource = new { source.InventoryId, source.TypeId, source.DepartmentId,
					source.GroupId, source.UnitId, source.Location, source.Batch, source.Note, source.Amount, source.AddedByUserId, source.TimeStamp } });
				await SaveAsync(actor, row); result.Transactions++;
			}
			await _store.RebuildStocksAsync(actor.DepartmentId);
			var marker = New<InventoryOperation>(actor); marker.RequestId = "00000000-0000-0000-0000-000000000001"; marker.State = 2;
			marker.Content = JsonConvert.SerializeObject(result);
			await AuditAsync(actor, marker, "InventoryLegacyMigrationCompleted");
			await SaveAsync(actor, marker);
			return result;
		}, allowMigration: true);

		private InventoryLocation NewLegacyLocation(InventoryActor actor, InventoryLocationType type, int? groupId, int? unitId)
		{
			var row = New<InventoryLocation>(actor); row.LocationType = (int)type; row.GroupId = groupId; row.UnitId = unitId;
			row.IsDefault = type == InventoryLocationType.Facility;
			row.Content = JsonConvert.SerializeObject(new InventoryLabel { Name = type == InventoryLocationType.Facility ? "Unassigned" : type.ToString() });
			return row;
		}
		private async Task ValidateLegacyHolderAsync(InventoryActor actor, InventoryLocation location, int inventoryId)
		{
			try { await _auth.ValidateHolderAsync(actor, location); }
			catch (InventoryException ex) when (ex.StatusCode == 400 || ex.StatusCode == 404) { throw LegacyMigrationError("LegacyInventoryHolderInvalid", inventoryId); }
		}
		private static decimal LegacyQuantity(double value, int inventoryId)
		{
			// Round-trip text preserves the source decimal precision; casting double can silently discard significant digits.
			if (!double.IsFinite(value) || !decimal.TryParse(value.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var quantity)
				|| (double)quantity != value || quantity <= -1000000000000000000m || quantity >= 1000000000000000000m || decimal.Round(quantity, 6) != quantity)
				throw LegacyMigrationError("LegacyInventoryQuantityRequiresReview", inventoryId);
			return quantity;
		}
		private static InventoryException LegacyMigrationError(string code, int inventoryId) => new(409, code + ":" + inventoryId.ToString(CultureInfo.InvariantCulture));
	}
}
