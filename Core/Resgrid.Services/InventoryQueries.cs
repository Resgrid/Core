using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model.Inventories;

namespace Resgrid.Services
{
	public sealed partial class InventoryModernizationService
	{
		public async Task<System.Collections.Generic.Dictionary<string, T>> GetManyAsync<T>(InventoryActor actor, System.Collections.Generic.IReadOnlyCollection<string> ids) where T : InventoryRow
		{
			await _auth.RequireAsync(actor);
			if (ids == null || ids.Count > 5000) throw new InventoryException(400, "InvalidPage");
			foreach (var id in ids) Id(id);
			var result = new System.Collections.Generic.Dictionary<string, T>();
			foreach (var batch in ids.Distinct().Chunk(500))
			{
				var rows = await _store.RelatedManyAsync<T>(actor.DepartmentId, "Id", batch);
				if (rows.Count != batch.Length) throw new InventoryException(404, "Unavailable");
				foreach (var row in rows)
				{
					await AuthorizeRowAsync(actor, row);
					result.Add(row.Id, await RevealAsync(actor, row));
				}
			}
			return result;
		}
		public async Task<InventoryTransaction> GetLegacyTransactionAsync(InventoryActor actor, int inventoryId)
		{
			await _auth.RequireAsync(actor);
			if (inventoryId <= 0) throw new InventoryException(400, "InvalidIdentifier");
			var row = await _store.LegacyTransactionAsync(actor.DepartmentId, inventoryId);
			if (row == null) return null;
			try { await AuthorizeRowAsync(actor, row); }
			catch (InventoryException ex) when (ex.StatusCode is 403 or 404 || ex.Code == "LocationUnavailable") { return null; }
			return await RevealAsync(actor, row);
		}

		public async Task<System.Collections.Generic.Dictionary<string, decimal>> GetVisibleQuantitiesAsync(InventoryActor actor, System.Collections.Generic.IReadOnlyCollection<string> itemIds)
		{
			await _auth.RequireAsync(actor);
			if (itemIds == null || itemIds.Count > 500) throw new InventoryException(400, "InvalidPage");
			foreach (var id in itemIds) Id(id);
			var totals = itemIds.Distinct().ToDictionary(id => id, _ => 0m);
			if (totals.Count == 0) return totals;
			var quantities = await _store.StockQuantitiesAsync(actor.DepartmentId, totals.Keys.ToArray());
			foreach (var location in quantities.GroupBy(q => q.LocationId))
			{
				try { await LocationAsync(actor, location.Key); }
				catch (InventoryException ex) when (ex.StatusCode is 403 or 404 || ex.Code == "LocationUnavailable") { continue; }
				foreach (var quantity in location) if (totals.ContainsKey(quantity.ItemId)) totals[quantity.ItemId] += quantity.Quantity;
			}
			return totals;
		}

		public async Task<InventoryPage<T>> QueryAsync<T>(InventoryActor actor, InventoryQuery filter, int page = 0) where T : InventoryRow
		{
			await _auth.RequireAsync(actor);
			if (page < 0 || page > 10000) throw new InventoryException(400, "InvalidPage");
			filter ??= new InventoryQuery();
			foreach (var id in new[] { filter.ItemId, filter.LocationId, filter.AssetId, filter.KitId }.Where(x => x != null)) Id(id);
			if (filter.SourceId?.Length > 128 || filter.SourceType is < 0 or > 1 || filter.RecordKind is < 1 or > 2) throw new InventoryException(400, "InvalidUsageSource");
			if (filter.IssuedToUserId?.Length > 128) throw new InventoryException(400, "InvalidIdentifier");
			var rows = await _store.QueryAsync<T>(actor.DepartmentId, filter, page * 500);
			var result = new InventoryPage<T> { HasMore = rows.Count > 500 };
			foreach (var row in rows.Take(500))
			{
				try { await AuthorizeRowAsync(actor, row); }
				catch (InventoryException ex) when (ex.StatusCode is 403 or 404 || ex.Code == "LocationUnavailable") { continue; }
				result.Items.Add(await RevealAsync(actor, row));
			}
			return result;
		}
	}
}
