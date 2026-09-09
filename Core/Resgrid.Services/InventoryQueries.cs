using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model.Inventories;

namespace Resgrid.Services
{
	public sealed partial class InventoryModernizationService
	{
		public async Task<InventoryPage<T>> QueryAsync<T>(InventoryActor actor, InventoryQuery filter, int page = 0) where T : InventoryRow
		{
			await _auth.RequireAsync(actor);
			if (page < 0 || page > 10000) throw new InventoryException(400, "InvalidPage");
			filter ??= new InventoryQuery();
			foreach (var id in new[] { filter.ItemId, filter.LocationId, filter.AssetId, filter.KitId }.Where(x => x != null)) Id(id);
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
