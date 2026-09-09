using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Resgrid.Model.Checklists;
using Resgrid.Model.Inventories;

namespace Resgrid.Services
{
	public sealed partial class InventoryModernizationService
	{
		public async Task<bool> IsAvailableAsync(int departmentId) => await _auth.IsEnabledAsync(departmentId) && await _store.HasLegacyMigrationAsync(departmentId);

		public async Task<List<ChecklistAssetTarget>> ListAsync(ChecklistActor actor)
		{
			var inventoryActor = ChecklistInventoryActor(actor); await RequireChecklistInventoryActorAsync(inventoryActor);
			var result = new List<ChecklistAssetTarget>();
			if (!await IsAvailableAsync(actor.DepartmentId)) return result;
			foreach (var asset in await AllAsync<InventoryAsset>(actor.DepartmentId))
			{
				var target = await CurrentChecklistAssetAsync(inventoryActor, asset.Id, true);
				if (target != null) result.Add(target);
			}
			return result;
		}

		public async Task<ChecklistAssetTarget> GetAsync(ChecklistActor actor, string id)
		{
			var inventoryActor = ChecklistInventoryActor(actor); await RequireChecklistInventoryActorAsync(inventoryActor);
			return await IsAvailableAsync(actor.DepartmentId) ? await CurrentChecklistAssetAsync(inventoryActor, id, true) : null;
		}

		public async Task<ChecklistAssetTarget> RoutingAsync(int departmentId, string id)
		{
			if (!await IsAvailableAsync(departmentId)) return null;
			return await CurrentChecklistAssetAsync(new InventoryActor { DepartmentId = departmentId }, id, false);
		}

		public async Task<bool> CanReceiveReminderAsync(int departmentId, string userId, string id)
		{
			var actor = new InventoryActor { DepartmentId = departmentId, UserId = userId };
			try
			{
				await _auth.RequireAsync(actor);
				if (!await IsAvailableAsync(departmentId)) return false;
				var asset = await CurrentChecklistMetadataAsync(departmentId, id);
				if (asset == null) return false;
				var location = await EffectiveLocationAsync(departmentId, await _store.GetAsync<InventoryLocation>(departmentId, asset.CurrentLocationId));
				// This is authorization for a generic notice only: no grant, protected-read call or display content.
				await _auth.RequireAsync(actor);
				return !location.IsDeleted && await _auth.CanLocationAsync(actor, location);
			}
			catch (InventoryException) { return false; }
		}

		private async Task<InventoryAsset> CurrentChecklistMetadataAsync(int departmentId, string id)
		{
			if (!Guid.TryParseExact(id, "D", out _)) return null;
			var asset = await _store.GetAsync<InventoryAsset>(departmentId, id);
			if (asset?.DepartmentId != departmentId || asset.IsDeleted || !ChecklistAssetPresent(asset.Status) || asset.CurrentLocationId == null) return null;
			var item = await _store.GetAsync<InventoryItem>(departmentId, asset.ItemId);
			if (item?.DepartmentId != departmentId || item.IsDeleted || !item.IsActive || item.TrackingMode != (int)InventoryTrackingMode.Serialized) return null;
			var location = await _store.GetAsync<InventoryLocation>(departmentId, asset.CurrentLocationId);
			return location?.DepartmentId == departmentId && !location.IsDeleted ? asset : null;
		}

		private async Task<ChecklistAssetTarget> CurrentChecklistAssetAsync(InventoryActor actor, string id, bool attended)
		{
			try
			{
				var asset = await CurrentChecklistMetadataAsync(actor.DepartmentId, id); if (asset == null) return null;
				var location = await EffectiveLocationAsync(actor.DepartmentId, await _store.GetAsync<InventoryLocation>(actor.DepartmentId, asset.CurrentLocationId));
				if (location.IsDeleted || attended && !await _auth.CanLocationAsync(actor, location)) return null;
				var target = await ChecklistRoutingTargetAsync(actor.DepartmentId, asset.Id, location); if (target == null) return null;
				if (attended)
				{
					var item = await _store.GetAsync<InventoryItem>(actor.DepartmentId, asset.ItemId);
					var itemContent = Decode<InventoryItemContent>(await RevealAsync(actor, ChecklistReadCopy(item)));
					var assetContent = Decode<InventoryAssetContent>(await RevealAsync(actor, ChecklistReadCopy(asset)));
					target.Name = ChecklistAssetLabel(itemContent.Name, assetContent.SerialNumber);
					await _auth.RequireAsync(actor);
					if (!await _auth.CanLocationAsync(actor, location)) return null;
				}
				return target;
			}
			catch (InventoryException ex) when (ex.StatusCode == 404 || ex.StatusCode == 409) { return null; }
			catch (InventoryException ex) { throw new ChecklistException(ex.StatusCode, ex.Code); }
		}

		private async Task<ChecklistAssetTarget> ChecklistRoutingTargetAsync(int departmentId, string assetId, InventoryLocation location)
		{
			if (location?.DepartmentId != departmentId) return null;
			var target = new ChecklistAssetTarget { DepartmentId = departmentId, Id = assetId, UnitId = location.UnitId, GroupId = location.GroupId, UserId = location.UserId };
			if (location.UnitId.HasValue)
			{
				var unit = await _units.GetUnitByIdAsync(location.UnitId.Value);
				if (unit?.DepartmentId != departmentId) return null;
				target.GroupId = unit.StationGroupId;
			}
			else if (location.UserId != null) target.GroupId = (await _groups.GetGroupForUserAsync(location.UserId, departmentId))?.DepartmentGroupId;
			if (target.GroupId.HasValue && (await _groups.GetGroupByIdAsync(target.GroupId.Value, true))?.DepartmentId != departmentId) return null;
			return target;
		}

		public async Task<List<ReadinessAssetSnapshot>> AtCallAsync(ChecklistActor actor, int callId, DateTime callUtc, IReadOnlyCollection<int> unitIds, bool contractorEquipment)
		{
			var inventoryActor = ChecklistInventoryActor(actor); await RequireChecklistInventoryActorAsync(inventoryActor);
			if (contractorEquipment || !await _store.HasLegacyMigrationAsync(actor.DepartmentId)) return null;
			if (callId <= 0 || unitIds == null || unitIds.Any(id => id <= 0)) throw new ChecklistException(400, "ReadinessCallUnavailable");
			// Historical reads remain available after module suspension. The caller authorizes the call and dispatch list.
			var units = unitIds.ToHashSet(); var result = new List<ReadinessAssetSnapshot>(); if (units.Count == 0) return result;
			var assets = (await AllAsync<InventoryAsset>(actor.DepartmentId)).Where(a => a.DepartmentId == actor.DepartmentId).ToDictionary(a => a.Id, StringComparer.Ordinal);
			var locations = (await AllAsync<InventoryLocation>(actor.DepartmentId)).Where(l => l.DepartmentId == actor.DepartmentId).ToDictionary(l => l.Id, StringComparer.Ordinal);
			var ledger = await ChecklistAssetHistoryAsync(actor.DepartmentId, assets.Keys, callUtc, false);
			var histories = ledger.GroupBy(t => t.AssetId).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
			var candidates = assets.Values.Select(asset => (Asset: asset, State: HistoricalChecklistPosition(asset.Id, callUtc, long.MaxValue, assets, locations, histories)))
				.Where(candidate => candidate.State?.Location?.UnitId != null && units.Contains(candidate.State.Location.UnitId.Value)).ToList();
			// Later container changes are needed to prove the first departure, including moves into a different bag on the same unit.
			var departureAssets = candidates.Select(c => c.Asset.Id).Concat(locations.Values.Select(l => l.ContainerAssetId).Where(id => id != null)).Distinct();
			if (candidates.Count > 0) ledger.AddRange(await ChecklistAssetHistoryAsync(actor.DepartmentId, departureAssets, callUtc, true));
			histories = ledger.GroupBy(t => t.AssetId).ToDictionary(g => g.Key, g => g.OrderBy(t => t.OccurredOn).ThenBy(t => t.EntryId).ToList(), StringComparer.Ordinal);
			foreach (var candidate in candidates)
			{
				var asset = candidate.Asset; var state = candidate.State;
				if (!await _auth.CanLocationAsync(inventoryActor, state.Location)) continue;
				var unit = await _units.GetUnitByIdAsync(state.Location.UnitId.Value); if (unit?.DepartmentId != actor.DepartmentId) continue;
				string name;
				try
				{
					// Use the asset's own immutable label snapshot, even when its holder provenance belongs to a moving bag.
					var source = await RevealAsync(inventoryActor, ChecklistReadCopy(state.AssetSource));
					var content = string.IsNullOrEmpty(source.Content) ? new JObject() : JObject.Parse(source.Content);
					name = ChecklistAssetLabel(content["ItemName"]?.Type == JTokenType.String ? content["ItemName"].Value<string>() : null,
						content["SerialNumber"]?.Type == JTokenType.String ? content["SerialNumber"].Value<string>() : null);
				}
				catch (InventoryException ex) { throw new ChecklistException(ex.StatusCode, ex.Code); }
				var next = HistoricalChecklistDeparture(asset.Id, callUtc, state, ledger, assets, locations, histories);
				await RequireChecklistInventoryActorAsync(inventoryActor);
				// The unit's current station/resource policy authorizes this historical read; it is not historical ownership evidence.
				if (!await _auth.CanLocationAsync(inventoryActor, state.Location)) continue;
				result.Add(new ReadinessAssetSnapshot { DepartmentId = actor.DepartmentId, AssetId = asset.Id, UnitId = state.Location.UnitId,
					SourceSubsystem = "Inventory", SourceId = state.HolderSource.Id, SourceVersion = state.HolderSource.EntryId.ToString(CultureInfo.InvariantCulture),
					IssuedUtc = DateTime.SpecifyKind(state.HolderSource.OccurredOn, DateTimeKind.Utc), ReturnedUtc = next, Name = name });
				if (result.Count > 1000) throw new ChecklistException(400, "ReportTooLarge");
			}
			return result;
		}

		private async Task<List<InventoryTransaction>> ChecklistAssetHistoryAsync(int departmentId, IEnumerable<string> assetIds, DateTime at, bool after)
		{
			var result = new List<InventoryTransaction>();
			foreach (var batch in assetIds.Distinct().Chunk(500))
				for (var skip = 0; ; skip += 500)
				{
					var rows = await _store.AssetHistoryAsync(departmentId, batch, at, after, skip);
					result.AddRange(rows.Take(500));
					if (result.Count > 100000) throw new ChecklistException(400, "ReportTooLarge");
					if (rows.Count <= 500) break;
				}
			return result.OrderBy(t => t.OccurredOn).ThenBy(t => t.EntryId).ToList();
		}

		private sealed class HistoricalChecklistAssetPosition
		{
			public InventoryLocation Location { get; set; }
			public InventoryTransaction HolderSource { get; set; }
			public InventoryTransaction AssetSource { get; set; }
			public HashSet<string> AssetChain { get; set; }
		}
		private static HistoricalChecklistAssetPosition HistoricalChecklistPosition(string assetId, DateTime at, long throughEntry,
			Dictionary<string, InventoryAsset> assets, Dictionary<string, InventoryLocation> locations, Dictionary<string, List<InventoryTransaction>> histories, HashSet<string> visited = null)
		{
			visited ??= new HashSet<string>(StringComparer.Ordinal);
			if (!visited.Add("asset:" + assetId) || visited.Count > 64 || !assets.TryGetValue(assetId, out var asset) || !histories.TryGetValue(assetId, out var history)) return null;
			var entries = history.Where(t => t.ItemId == asset.ItemId && t.EntryId > 0 && Guid.TryParseExact(t.Id, "D", out _)
				&& (t.OccurredOn < at || t.OccurredOn == at && t.EntryId <= throughEntry)).ToList();
			var source = entries.LastOrDefault(); var status = entries.LastOrDefault(t => t.NewStatus.HasValue)?.NewStatus;
			var movement = entries.LastOrDefault(IsChecklistLocationMovement);
			if (source == null || !status.HasValue || !ChecklistAssetPresent(status.Value) || movement?.ToLocationId == null) return null;
			var result = new HistoricalChecklistAssetPosition { AssetSource = source, HolderSource = movement, AssetChain = new HashSet<string>(StringComparer.Ordinal) { assetId } };
			var locationId = movement.ToLocationId;
			while (locationId != null)
			{
				if (!visited.Add("location:" + locationId) || visited.Count > 64 || !locations.TryGetValue(locationId, out var location) || location.CreatedOn > at) return null;
				if (location.ContainerAssetId != null)
				{
					var parent = HistoricalChecklistPosition(location.ContainerAssetId, at, throughEntry, assets, locations, histories, visited);
					if (parent?.Location == null) return null;
					result.Location = parent.Location; result.AssetChain.UnionWith(parent.AssetChain);
					if (parent.HolderSource.OccurredOn > result.HolderSource.OccurredOn || parent.HolderSource.OccurredOn == result.HolderSource.OccurredOn && parent.HolderSource.EntryId > result.HolderSource.EntryId)
						result.HolderSource = parent.HolderSource;
					return result;
				}
				if (location.ParentLocationId == null) { result.Location = location; return result; }
				locationId = location.ParentLocationId;
			}
			return null;
		}
		private static DateTime? HistoricalChecklistDeparture(string assetId, DateTime callUtc, HistoricalChecklistAssetPosition initial, List<InventoryTransaction> ledger,
			Dictionary<string, InventoryAsset> assets, Dictionary<string, InventoryLocation> locations, Dictionary<string, List<InventoryTransaction>> histories)
		{
			var state = initial;
			foreach (var movement in ledger.Where(t => t.OccurredOn > callUtc && (IsChecklistLocationMovement(t) || t.NewStatus.HasValue && !ChecklistAssetPresent(t.NewStatus.Value))))
			{
				if (!state.AssetChain.Contains(movement.AssetId)) continue;
				var next = HistoricalChecklistPosition(assetId, movement.OccurredOn, movement.EntryId, assets, locations, histories);
				if (next == null || next.Location?.UnitId != initial.Location.UnitId) return DateTime.SpecifyKind(movement.OccurredOn, DateTimeKind.Utc);
				state = next;
			}
			return null;
		}
		private static bool IsChecklistLocationMovement(InventoryTransaction transaction) => transaction.TransactionType != (int)InventoryTransactionType.StatusChange && transaction.FromLocationId != transaction.ToLocationId;
		private static bool ChecklistAssetPresent(int status) => status >= (int)InventoryAssetStatus.InService && status <= (int)InventoryAssetStatus.Damaged;
		private static string ChecklistAssetLabel(string itemName, string serial) => string.IsNullOrWhiteSpace(itemName) ? "Equipment" : string.IsNullOrWhiteSpace(serial) ? itemName : itemName + " — " + serial;
		private static T ChecklistReadCopy<T>(T row) where T : InventoryRow => JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(row));
		private static InventoryActor ChecklistInventoryActor(ChecklistActor actor) => actor == null ? throw new ChecklistException(403, "MembershipRequired")
			: new InventoryActor { DepartmentId = actor.DepartmentId, UserId = actor.UserId, GrantToken = actor.GrantToken };
		private async Task RequireChecklistInventoryActorAsync(InventoryActor actor)
		{
			try { await _auth.RequireAsync(actor); } catch (InventoryException ex) { throw new ChecklistException(ex.StatusCode, ex.Code); }
		}
	}
}
