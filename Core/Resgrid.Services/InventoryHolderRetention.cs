using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Inventories;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
namespace Resgrid.Services
{
	/// <summary>Retain holder identities used by inventory evidence; an authorized department purge removes the complete subtree.</summary>
	internal static class InventoryHolderRetention
	{
		public static async Task<bool> DeleteAsync(IInventoryStore store, IUnitOfWork uow, int department, int id, bool unit, Func<Task<bool>> remove, CancellationToken ct)
		{
			if (store == null) return await remove();
			if (uow == null) throw new InvalidOperationException("Inventory holder deletion requires a transaction.");
			var owns = uow.Transaction == null;
			try
			{
				await uow.CreateOrGetConnectionAsync(ct); await store.LockDepartmentAsync(department);
				for (var skip = 0; ; skip += 500)
				{
					var rows = await store.ListAsync<InventoryLocation>(department, skip);
					if (rows.Take(500).Any(l => unit ? l.UnitId == id : l.GroupId == id)) throw new InventoryException(409, "HolderHistoryRetained");
					if (rows.Count <= 500) break;
					if (skip >= 100000) throw new InventoryException(409, "InventoryTooLarge");
				}
				var result = await remove(); if (owns) uow.CommitChanges(); return result;
			}
			catch { if (owns) uow.DiscardChanges(); throw; }
		}
	}
}
