using System;
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
		private static void Cost(decimal? value)
		{ if (value < 0 || value > 100000000m || value.HasValue && decimal.Round(value.Value, 6) != value) throw new InventoryException(400, "InvalidCost"); }
		private static decimal Money(decimal value) => decimal.Round(value, 6, MidpointRounding.ToEven);
		private static string Currency(string value)
		{
			if (value == null) return null;
			value = value.Trim().ToUpperInvariant();
			if (value.Length != 3 || value.Any(c => c < 'A' || c > 'Z')) throw new InventoryException(400, "InvalidCurrency");
			return value;
		}
		private async Task<decimal?> CurrentUnitCostAsync(InventoryActor actor, InventoryItem item, string lotId, string assetId)
		{
			var lotCost = lotId == null ? null : Decode<InventoryLotContent>(await GetAsync<InventoryLot>(actor, lotId)).UnitCost;
			var assetCost = assetId == null ? null : Decode<InventoryAssetContent>(await GetAsync<InventoryAsset>(actor, assetId)).AcquisitionCost;
			var details = Decode<InventoryItemContent>(item);
			return lotCost ?? assetCost ?? details.AverageUnitCost ?? details.DefaultUnitCost;
		}
		private sealed class PostingCost
		{
			public decimal? UnitCost; public string CurrencyCode; public decimal? AverageAfter; public bool UpdateAverage;
		}
		private async Task<PostingCost> CostForPostingAsync(InventoryActor actor, InventoryItem item, InventoryPosting line)
		{
			Cost(line.UnitCost);
			var details = Decode<InventoryItemContent>(item);
			var result = new PostingCost { CurrencyCode = details.CurrencyCode };
			InventoryTransaction original = null;
			if (line.ReversesTransactionId != null) original = await _store.GetAsync<InventoryTransaction>(actor.DepartmentId, line.ReversesTransactionId);
			else if (line.Type == InventoryTransactionType.Return && line.IssuanceId != null)
				original = (await _store.RelatedAsync<InventoryTransaction>(actor.DepartmentId, "IssuanceId", line.IssuanceId)).SingleOrDefault(t => t.TransactionType == (int)InventoryTransactionType.Issue);
			if (original != null)
			{
				original = await GetAsync<InventoryTransaction>(actor, original.Id);
				var frozen = JObject.Parse(original.Content ?? "{}");
				result.UnitCost = frozen.Value<decimal?>("UnitCost"); result.CurrencyCode = frozen.Value<string>("CurrencyCode");
				if (line.UnitCost.HasValue && line.UnitCost != result.UnitCost) throw new InventoryException(409, "ReversalCostMismatch");
			}
			else
			{
				if (line.UnitCost.HasValue && line.Type != InventoryTransactionType.Receive) throw new InventoryException(400, "CostOverrideUnsupported");
				if (line.Type == InventoryTransactionType.Receive)
				{
					var lotCost = line.LotId == null ? null : Decode<InventoryLotContent>(await GetAsync<InventoryLot>(actor, line.LotId)).UnitCost;
					var assetCost = line.AssetId == null ? null : Decode<InventoryAssetContent>(await GetAsync<InventoryAsset>(actor, line.AssetId)).AcquisitionCost;
					if (lotCost.HasValue && (line.UnitCost.HasValue && line.UnitCost != lotCost || assetCost.HasValue && assetCost != lotCost)) throw new InventoryException(409, "LotCostMismatch");
					if (assetCost.HasValue && line.UnitCost.HasValue && line.UnitCost != assetCost) throw new InventoryException(409, "AssetCostMismatch");
				}
				result.UnitCost = line.UnitCost ?? await CurrentUnitCostAsync(actor, item, line.LotId, line.AssetId);
			}
			Cost(result.UnitCost);
			var restoresStock = line.ReversesTransactionId != null && original?.FromLocationId != null && original.ToLocationId == null;
			var removesStock = line.ReversesTransactionId != null && original?.FromLocationId == null && original?.ToLocationId != null;
			if (line.Type != InventoryTransactionType.Receive && !restoresStock && !removesStock) return result;
			decimal before;
			if (item.TrackingMode == (int)InventoryTrackingMode.Bulk)
			{
				// Fixed-price lots carry their own value. Mixing them into the fallback average
				// would price untracked stock twice: once in this average and again by lot cost.
				var pricedLots = new System.Collections.Generic.HashSet<string>();
				foreach (var lot in await _store.RelatedAsync<InventoryLot>(actor.DepartmentId, "ItemId", item.Id))
					if (Decode<InventoryLotContent>(await RevealAsync(actor, lot)).UnitCost.HasValue) pricedLots.Add(lot.Id);
				if (line.LotId != null && pricedLots.Contains(line.LotId)) return result;
				before = (await _store.RelatedAsync<InventoryStock>(actor.DepartmentId, "ItemId", item.Id)).Where(s => s.LotId == null || !pricedLots.Contains(s.LotId)).Sum(s => s.Quantity);
			}
			else before = (await _store.RelatedAsync<InventoryAsset>(actor.DepartmentId, "ItemId", item.Id)).Count(a => !a.IsDeleted && a.CurrentLocationId != null && a.Status is 0 or 1 or 2 or 3);
			var incoming = line.Type == InventoryTransactionType.Receive || restoresStock;
			if (before < 0 && !incoming) throw new InventoryException(409, "CostReconciliationRequired");
			before = Math.Max(0, before);
			var oldCost = details.AverageUnitCost ?? details.DefaultUnitCost;
			var after = before + (incoming ? line.Quantity : -line.Quantity);
			if (after < 0) throw new InventoryException(409, "CostReconciliationRequired");
			result.UpdateAverage = true;
			if (!result.UnitCost.HasValue || before > 0 && !oldCost.HasValue)
				result.AverageAfter = null; // Unknown opening value is not silently priced as zero.
			else if (after == 0) result.AverageAfter = null;
			else
			{
				var value = before * (oldCost ?? 0) + (incoming ? 1 : -1) * line.Quantity * result.UnitCost.Value;
				if (value < 0) throw new InventoryException(409, "CostReconciliationRequired");
				result.AverageAfter = Money(value / after); Cost(result.AverageAfter);
			}
			return result;
		}
		private async Task ApplyAverageCostAsync(InventoryActor actor, InventoryItem item, PostingCost cost)
		{
			if (!cost.UpdateAverage) return;
			var details = Decode<InventoryItemContent>(item); details.AverageUnitCost = cost.AverageAfter;
			item.Content = JsonConvert.SerializeObject(details); await SaveAsync(actor, item, false);
		}
		public async Task<InventoryValuation> GetValuationAsync(InventoryActor actor, string locationId = null)
		{
			await _auth.RequireAsync(actor, false, PermissionTypes.AdjustInventory);
			if (_uow.Transaction != null) throw new InvalidOperationException("Inventory valuation owns its read transaction.");
			try
			{
				await _uow.CreateOrGetConnectionAsync(System.Threading.CancellationToken.None);
				// Use the posting lock so quantities and protected average costs describe the same instant.
				await _store.LockDepartmentAsync(actor.DepartmentId);
				var result = await ReadValuationAsync(actor, locationId); _uow.CommitChanges(); return result;
			}
			catch { _uow.DiscardChanges(); throw; }
		}
		private async Task<InventoryValuation> ReadValuationAsync(InventoryActor actor, string locationId)
		{
			await _auth.RequireAsync(actor, false, PermissionTypes.AdjustInventory);
			if (locationId != null) await LocationAsync(actor, locationId);
			var result = new InventoryValuation { AsOfUtc = Now };
			var items = (await AllAsync<InventoryItem>(actor.DepartmentId)).ToDictionary(i => i.Id);
			async Task Add(string itemId, string holderId, string lotId, string assetId, decimal quantity)
			{
				if (holderId == null || locationId != null && locationId != holderId || !items.TryGetValue(itemId, out var stored)) return;
				try { await LocationAsync(actor, holderId); }
				catch (InventoryException e) when (e.StatusCode is 403 or 404 || e.Code == "LocationUnavailable") { return; }
				var item = await RevealAsync(actor, JsonConvert.DeserializeObject<InventoryItem>(JsonConvert.SerializeObject(stored)));
				var details = Decode<InventoryItemContent>(item);
				// Serialized equipment is valued once, at its own acquisition cost when known.
				var cost = assetId == null ? await CurrentUnitCostAsync(actor, item, lotId, null)
					: Decode<InventoryAssetContent>(await GetAsync<InventoryAsset>(actor, assetId)).AcquisitionCost ?? await CurrentUnitCostAsync(actor, item, lotId, assetId);
				result.Lines.Add(new InventoryValuationLine { ItemId = itemId, AssetId = assetId, LotId = lotId, LocationId = holderId, ItemName = details.Name,
					CurrencyCode = details.CurrencyCode, Quantity = quantity, UnitCost = cost, Value = cost.HasValue ? Money(cost.Value * quantity) : null });
				if (result.Lines.Count > 5000) throw new InventoryException(409, "InventoryTooLarge");
			}
			foreach (var stock in await AllAsync<InventoryStock>(actor.DepartmentId)) if (stock.Quantity != 0) await Add(stock.ItemId, stock.LocationId, stock.LotId, null, stock.Quantity);
			foreach (var asset in await AllAsync<InventoryAsset>(actor.DepartmentId)) if (!asset.IsDeleted && asset.Status is 0 or 1 or 2 or 3) await Add(asset.ItemId, asset.CurrentLocationId, asset.LotId, asset.Id, 1);
			result.HasNegativeStock = result.Lines.Any(l => l.Quantity < 0);
			result.Totals = result.Lines.GroupBy(l => l.CurrencyCode).Select(g => new InventoryValuationTotal { CurrencyCode = g.Key,
				KnownValue = g.Sum(l => l.Value ?? 0), UncostedRows = g.Count(l => !l.Value.HasValue) }).OrderBy(t => t.CurrencyCode).ToList();
			await _auth.RequireAsync(actor, false, PermissionTypes.AdjustInventory); return result;
		}
	}
}
