using System;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model.Inventories;
using Resgrid.Repositories.DataRepository.Transactions;

namespace Resgrid.Tests.Services
{
	/// <summary>M0329 InventoryParLevels: one minimum per item and location, in the item's and location's own department.</summary>
	public partial class InventoryDatabaseTests
	{
		private static InventoryParLevel DatabaseParLevel(InventoryItem item, InventoryLocation location, decimal minimum = 2) => new InventoryParLevel
		{
			DepartmentId = item.DepartmentId, CreatedBy = "inventory-test-author", CreatedOn = DateTime.UtcNow, ModifiedOn = DateTime.UtcNow,
			ItemId = item.Id, LocationId = location.Id, MinimumQuantity = minimum
		};

		[Test]
		public async Task Par_levels_are_unique_per_item_and_location_tenant_scoped_and_hold_no_content()
		{
			var seed = await SeedAsync(); var foreign = await SeedAsync(88);
			var par = DatabaseParLevel(seed.Item, seed.Location, 1.234567m);
			await WriteAsync(store => store.InsertAsync(par));

			foreach (var corrupt in new Action<InventoryParLevel>[]
			{
				row => { }, // a second row for the same item and location
				row => row.ItemId = foreign.Item.Id, row => row.LocationId = foreign.Location.Id, row => row.DepartmentId = 88,
				row => row.MinimumQuantity = -1, row => row.Content = "{}", row => row.Revision = 0
			})
				await RejectAsync(store => { var row = DatabaseParLevel(seed.Item, seed.Location); corrupt(row); return store.InsertAsync(row); });

			using var uow = new UnitOfWork(Connections());
			var saved = await Store(uow).GetAsync<InventoryParLevel>(77, par.Id);
			saved.MinimumQuantity.Should().Be(1.234567m); saved.Content.Should().BeNull(); saved.IsDeleted.Should().BeFalse();
		}
	}
}
