using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model.Inventories;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	public sealed partial class InventoryModernizationTests
	{
		[Test]
		public async Task Historical_departure_follows_a_bag_entered_after_the_call_on_the_same_unit()
		{
			var item = Item(InventoryTrackingMode.Serialized); var bagItem = Item(InventoryTrackingMode.Serialized, kit: true);
			var first = Location(InventoryLocationType.Unit, 101); var second = Location(InventoryLocationType.Unit, 102);
			var asset = await CreateAsset(item, first); var bag = await CreateAsset(bagItem, first, "bag");
			var container = _store.All<InventoryLocation>().Single(l => l.ContainerAssetId == bag.Id);
			_clock.Advance(TimeSpan.FromMinutes(10)); var call = _clock.Utc;
			_clock.Advance(TimeSpan.FromMinutes(10));
			await _service.CreateAndCompleteTransferAsync(_actor, Command(AssetMove(item, asset, first, container)));
			_clock.Advance(TimeSpan.FromMinutes(10));
			await _service.CreateAndCompleteTransferAsync(_actor, Command(AssetMove(bagItem, bag, first, second)));
			var snapshot = (await _service.AtCallAsync(ChecklistActor(), 25, call, new[] { 101 }, false)).Single(a => a.AssetId == asset.Id);
			snapshot.ReturnedUtc.Should().Be(_clock.Utc);
		}

		[Test]
		public async Task Transfer_item_listing_rechecks_both_parent_holders_before_returning_items()
		{
			var item = Item(); var first = Location(); var second = Location();
			await _service.PostTransactionAsync(_actor, Receive(item, first, 5));
			await _service.CreateAndCompleteTransferAsync(_actor, Command(new InventoryPosting { ItemId = item.Id, FromLocationId = first.Id, ToLocationId = second.Id, Quantity = 1, Type = InventoryTransactionType.Transfer }));
			(await _service.ListAsync<InventoryTransferItem>(_actor)).Items.Should().ContainSingle();
			_deniedLocations.Add(second.Id);
			(await _service.ListAsync<InventoryTransferItem>(_actor)).Items.Should().BeEmpty();
		}

		[Test]
		public async Task Visible_totals_preserve_decimals_and_exclude_deleted_foreign_and_inaccessible_stock()
		{
			var item = Item(); var empty = Item(); var location = Location(); var denied = Location();
			_deniedLocations.Add(denied.Id);
			_store.Seed(new InventoryStock { DepartmentId = Department, ItemId = item.Id, LocationId = location.Id, Quantity = 1.125m });
			_store.Seed(new InventoryStock { DepartmentId = Department, ItemId = item.Id, LocationId = location.Id, LotId = Guid.NewGuid().ToString("D"), Quantity = 2m });
			_store.Seed(new InventoryStock { DepartmentId = Department, ItemId = item.Id, LocationId = denied.Id, Quantity = 100m });
			_store.Seed(new InventoryStock { DepartmentId = Department, ItemId = item.Id, LocationId = location.Id, Quantity = 100m, IsDeleted = true });
			_store.Seed(new InventoryStock { DepartmentId = 999, ItemId = item.Id, LocationId = location.Id, Quantity = 100m });
			_store.Seed(new InventoryStock { DepartmentId = Department, ItemId = Item().Id, LocationId = location.Id, Quantity = 100m });
			var totals = await _service.GetVisibleQuantitiesAsync(_actor, new[] { item.Id, empty.Id });
			totals.Should().HaveCount(2); totals[item.Id].Should().Be(3.125m); totals[empty.Id].Should().Be(0m);
			_auth.Verify(a => a.CanLocationAsync(_actor, It.Is<InventoryLocation>(l => l.Id == location.Id)), Times.Once);
		}

		[Test]
		public async Task Visible_totals_exclude_stock_inside_a_terminal_container()
		{
			var item = Item(); var bagItem = Item(InventoryTrackingMode.Serialized, kit: true); var location = Location();
			var bag = await CreateAsset(bagItem, location, "bag");
			var container = _store.All<InventoryLocation>().Single(l => l.ContainerAssetId == bag.Id);
			await _service.PostTransactionAsync(_actor, Receive(item, container, 5));
			(await _service.GetVisibleQuantitiesAsync(_actor, new[] { item.Id }))[item.Id].Should().Be(5);
			await _service.ChangeAssetStatusAsync(_actor, Status(bagItem, bag, location, InventoryAssetStatus.Lost));
			(await _service.GetVisibleQuantitiesAsync(_actor, new[] { item.Id }))[item.Id].Should().Be(0);
		}

		[Test]
		public async Task Legacy_lookup_preserves_location_authorization_and_attended_protection()
		{
			var item = Item(); var location = Location();
			var row = _store.Seed(new InventoryTransaction { DepartmentId = Department, ItemId = item.Id, ToLocationId = location.Id, LegacyInventoryId = 42, Content = "{}" });
			(await _service.GetLegacyTransactionAsync(_actor, 42)).Id.Should().Be(row.Id);
			_read.Invocations.Count.Should().BeGreaterThan(0);
			(await _service.GetLegacyTransactionAsync(_actor, 43)).Should().BeNull();
			_deniedLocations.Add(location.Id);
			(await _service.GetLegacyTransactionAsync(_actor, 42)).Should().BeNull();
		}
	}

	public partial class InventoryDatabaseTests
	{
		[Test]
		public async Task Stock_aggregation_scopes_items_and_department_and_sums_lots_before_holder_authorization()
		{
			var seed = await SeedAsync(); var foreign = await SeedAsync(88);
			var lot = NewRow<InventoryLot>(); lot.ItemId = seed.Item.Id; lot.ReceivedOn = DateTime.UtcNow;
			await WriteAsync(async store =>
			{
				await store.InsertAsync(lot);
				await store.ApplyStockDeltaAsync(77, seed.Item.Id, seed.Location.Id, null, 1.125m, "inventory-test-author");
				await store.ApplyStockDeltaAsync(77, seed.Item.Id, seed.Location.Id, lot.Id, 2m, "inventory-test-author");
			});
			await WriteAsync(async store => { await store.ApplyStockDeltaAsync(88, foreign.Item.Id, foreign.Location.Id, null, 100m, "inventory-test-author"); }, 88);
			using var uow = new Resgrid.Repositories.DataRepository.Transactions.UnitOfWork(Connections()); var repository = Store(uow);
			var totals = await repository.StockQuantitiesAsync(77, new[] { seed.Item.Id, foreign.Item.Id });
			totals.Should().ContainSingle(); totals[0].Quantity.Should().Be(3.125m); totals[0].LocationId.Should().Be(seed.Location.Id);
			(await repository.StockQuantitiesAsync(77, Array.Empty<string>())).Should().BeEmpty();
		}

		[Test]
		public async Task Asset_history_queries_preserve_the_call_boundary_and_filter_assets_before_paging()
		{
			var seed = await SeedAsync(); var asset = NewRow<InventoryAsset>(); asset.ItemId = seed.Item.Id; asset.CurrentLocationId = seed.Location.Id;
			var at = new DateTime(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc);
			InventoryTransaction Movement(DateTime when, string id) { var row = NewRow<InventoryTransaction>(); row.ItemId = seed.Item.Id; row.AssetId = id; row.OccurredOn = when; row.Quantity = 1; row.TransactionType = 1; return row; }
			var before = Movement(at.AddDays(-1), asset.Id); var boundary = Movement(at, asset.Id); var after = Movement(at.AddDays(1), asset.Id);
			await WriteAsync(async store => { await store.InsertAsync(asset); await store.InsertAsync(before); await store.InsertAsync(boundary); await store.InsertAsync(after); await store.InsertAsync(Movement(at, null)); });
			using var uow = new Resgrid.Repositories.DataRepository.Transactions.UnitOfWork(Connections()); var repository = Store(uow);
			(await repository.AssetHistoryAsync(77, new[] { asset.Id }, at, false)).Select(t => t.Id).Should().Equal(before.Id, boundary.Id);
			(await repository.AssetHistoryAsync(77, new[] { asset.Id }, at, true)).Select(t => t.Id).Should().Equal(after.Id);
			(await repository.AssetHistoryAsync(88, new[] { asset.Id }, at, false)).Should().BeEmpty();
		}
	}

	[TestFixture]
	public class InventoryDependencyTests
	{
		[Test]
		public void Every_registered_inventory_trigger_has_a_label_in_each_supported_locale()
		{
			var strings = new System.Resources.ResourceManager("Resgrid.Localization.Areas.User.Inventory.Inventory", typeof(Resgrid.Localization.SupportedLocales).Assembly);
			foreach (var language in Resgrid.Localization.SupportedLocales.GetSupportedCultures())
			{
				var resources = strings.GetResourceSet(System.Globalization.CultureInfo.GetCultureInfo(language), true, false);
				resources.Should().NotBeNull(language);
				foreach (var key in InventoryWorkflowPayload.Triggers.Select(t => ((Resgrid.Model.WorkflowTriggerEventType)t).ToString()).Append("HolderHistoryRetained"))
					resources.GetString(key).Should().NotBeNullOrWhiteSpace(key + " in " + language).And.NotBe(key);
			}
		}

		[TestCase(false, false), TestCase(true, true), TestCase(true, false), TestCase(false, true)]
		public void Unit_retention_dependencies_are_accepted_only_as_a_pair(bool hasStore, bool hasUnit)
		{
			Action construct = () => new UnitsService(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null,
				hasStore ? Mock.Of<IInventoryStore>() : null, hasUnit ? Mock.Of<IUnitOfWork>() : null);
			if (hasStore == hasUnit) construct.Should().NotThrow();
			else construct.Should().Throw<ArgumentException>();
		}
	}
}
