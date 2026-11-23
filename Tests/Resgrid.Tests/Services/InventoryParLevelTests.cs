using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Inventories;

namespace Resgrid.Tests.Services
{
	/// <summary>Per-location minimums (par levels, M0329): a station can run low while the department total is fine.</summary>
	public sealed partial class InventoryModernizationTests
	{
		private InventoryParLevel ParLevel(InventoryItem item, InventoryLocation location, decimal minimum, bool removed = false) => _store.Seed(new InventoryParLevel {
			DepartmentId = Department, ItemId = item.Id, LocationId = location.Id, MinimumQuantity = minimum, IsDeleted = removed, CreatedBy = _actor.UserId, CreatedOn = _clock.Utc });

		private InventoryItemInput ParInput(InventoryItem item, params InventoryParLevelInput[] levels)
		{
			var current = _store.All<InventoryItem>().Single(i => i.Id == item.Id);
			return new InventoryItemInput { Id = item.Id, Revision = current.Revision, TrackingMode = (InventoryTrackingMode)current.TrackingMode, IsActive = current.IsActive,
				Details = JsonConvert.DeserializeObject<InventoryItemContent>(current.Content), ParLevels = levels.ToList() };
		}

		[Test]
		public async Task An_empty_station_raises_its_own_low_stock_alert_while_the_warehouse_is_full()
		{
			var item = M5ThresholdItem(3); var warehouse = Location(); var station = Location(InventoryLocationType.Station);
			SeedStock(item, warehouse, 40); ParLevel(item, station, 2);

			await _service.RefreshAlertsAsync(_actor);

			var alert = _store.All<InventoryAlert>().Should().ContainSingle().Which;
			alert.AlertType.Should().Be((int)InventoryAlertType.LowStock); alert.LocationId.Should().Be(station.Id); alert.Quantity.Should().Be(0); alert.Status.Should().Be(0);
			_events.Should().ContainSingle().Which.Trigger.Should().Be(WorkflowTriggerEventType.InventoryLowStock);
		}

		[Test]
		public async Task Restocking_the_station_resolves_its_alert_and_the_department_check_still_runs()
		{
			var item = M5ThresholdItem(50); var warehouse = Location(); var station = Location(InventoryLocationType.Station);
			SeedStock(item, warehouse, 40); ParLevel(item, station, 2);
			await _service.RefreshAlertsAsync(_actor);
			_store.All<InventoryAlert>().Where(a => a.Status == 0).Select(a => a.LocationId).Should().BeEquivalentTo(new[] { null, station.Id });

			await _service.PostTransactionAsync(_actor, Receive(item, station, 3));

			var open = _store.All<InventoryAlert>().Where(a => a.Status == 0).ToList();
			open.Should().ContainSingle().Which.LocationId.Should().BeNull("43 in all is still under the department reorder point of 50");
			_store.All<InventoryAlert>().Single(a => a.LocationId == station.Id).Status.Should().Be(1);
		}

		[Test]
		public async Task Saving_an_item_sets_replaces_and_removes_its_par_levels()
		{
			var item = Item(); var station = Location(InventoryLocationType.Station); var engine = Location(InventoryLocationType.Unit, 101);
			SeedStock(item, station, 5);

			await _service.SaveItemAsync(_actor, ParInput(item, new InventoryParLevelInput { LocationId = station.Id, MinimumQuantity = 6 }, new InventoryParLevelInput { LocationId = engine.Id, MinimumQuantity = 1 }));

			_store.All<InventoryParLevel>().Where(p => !p.IsDeleted).Select(p => (p.LocationId, p.MinimumQuantity)).Should().BeEquivalentTo(new[] { (station.Id, 6m), (engine.Id, 1m) });
			_store.All<InventoryParLevel>().Should().OnlyContain(p => p.Content == null);
			_store.All<InventoryAlert>().Where(a => a.Status == 0).Select(a => a.LocationId).Should().BeEquivalentTo(new[] { station.Id, engine.Id });

			// Clearing the station removes its level and resolves its alert; the engine, not listed, keeps its own.
			await _service.SaveItemAsync(_actor, ParInput(item, new InventoryParLevelInput { LocationId = station.Id, MinimumQuantity = null }));

			_store.All<InventoryParLevel>().Single(p => p.LocationId == station.Id).IsDeleted.Should().BeTrue();
			_store.All<InventoryParLevel>().Single(p => p.LocationId == engine.Id).IsDeleted.Should().BeFalse();
			_store.All<InventoryAlert>().Where(a => a.Status == 0).Select(a => a.LocationId).Should().BeEquivalentTo(new[] { engine.Id });

			// Setting it again revives the same row.
			var stationRow = _store.All<InventoryParLevel>().Single(p => p.LocationId == station.Id).Id;
			await _service.SaveItemAsync(_actor, ParInput(item, new InventoryParLevelInput { LocationId = station.Id, MinimumQuantity = 4 }));
			_store.All<InventoryParLevel>().Single(p => p.LocationId == station.Id).Should().Match<InventoryParLevel>(p => p.Id == stationRow && !p.IsDeleted && p.MinimumQuantity == 4);
		}

		[Test]
		public async Task Saving_an_item_without_par_levels_leaves_them_alone()
		{
			var item = Item(); var station = Location(InventoryLocationType.Station); ParLevel(item, station, 2);
			var input = ParInput(item); input.ParLevels = null;

			await _service.SaveItemAsync(_actor, input);

			_store.All<InventoryParLevel>().Should().ContainSingle().Which.IsDeleted.Should().BeFalse();
		}

		[Test]
		public async Task Par_levels_at_locations_the_caller_cannot_see_cannot_be_changed_or_read()
		{
			var item = Item(); var visible = Location(); var hidden = Location(); ParLevel(item, hidden, 2); _deniedLocations.Add(hidden.Id);

			await _service.SaveItemAsync(_actor, ParInput(item, new InventoryParLevelInput { LocationId = visible.Id, MinimumQuantity = 1 }));
			_store.All<InventoryParLevel>().Single(p => p.LocationId == hidden.Id).IsDeleted.Should().BeFalse();
			(await _service.GetParLevelsAsync(_actor, new[] { item.Id })).Select(p => p.LocationId).Should().BeEquivalentTo(new[] { visible.Id });

			await Fails(() => _service.SaveItemAsync(_actor, ParInput(item, new InventoryParLevelInput { LocationId = hidden.Id, MinimumQuantity = 5 })), "LocationUnavailable", 404);
			await Fails(() => _service.SaveItemAsync(_actor, ParInput(item, new InventoryParLevelInput { LocationId = hidden.Id, MinimumQuantity = null })), "LocationUnavailable", 404);
			_store.All<InventoryParLevel>().Single(p => p.LocationId == hidden.Id).Should().Match<InventoryParLevel>(p => !p.IsDeleted && p.MinimumQuantity == 2);
		}

		[TestCase(-1)]
		[TestCase(1.0000001)]
		public async Task Invalid_par_levels_are_refused(decimal minimum)
		{
			var item = Item(); var station = Location();

			await Fails(() => _service.SaveItemAsync(_actor, ParInput(item, new InventoryParLevelInput { LocationId = station.Id, MinimumQuantity = minimum })), "InvalidParLevel", 400);
			_store.All<InventoryParLevel>().Should().BeEmpty();
		}

		[Test]
		public async Task Duplicate_locations_in_one_save_are_refused()
		{
			var item = Item(); var station = Location();

			await Fails(() => _service.SaveItemAsync(_actor, ParInput(item,
				new InventoryParLevelInput { LocationId = station.Id, MinimumQuantity = 1 }, new InventoryParLevelInput { LocationId = station.Id, MinimumQuantity = 2 })), "InvalidParLevel", 400);
		}

		[Test]
		public async Task Archiving_a_location_resolves_its_par_level_alert()
		{
			var item = Item(); var station = Location(InventoryLocationType.Station); ParLevel(item, station, 2);
			await _service.RefreshAlertsAsync(_actor);
			_store.All<InventoryAlert>().Should().ContainSingle(a => a.Status == 0 && a.LocationId == station.Id);

			await _service.ArchiveAsync<InventoryLocation>(_actor, station.Id, _store.All<InventoryLocation>().Single(l => l.Id == station.Id).Revision);

			_store.All<InventoryAlert>().Should().OnlyContain(a => a.Status == 1);
		}

		[Test]
		public async Task Low_stock_report_lists_the_department_total_and_each_short_location()
		{
			var item = M5ThresholdItem(10); var warehouse = Location(); var station = Location(InventoryLocationType.Station); var full = Location(InventoryLocationType.Station);
			SeedStock(item, warehouse, 5); SeedStock(item, station, 1); SeedStock(item, full, 3); ParLevel(item, station, 2); ParLevel(item, full, 2);

			var result = await _service.BuildReportAsync(_actor, new InventoryReportInput { Kind = InventoryReportKind.LowStock });

			result.Columns.Should().Contain("Location");
			result.Rows.Should().HaveCount(2);
			var department = result.Rows.Single(r => r["Location"] == null);
			department["OnHand"].Should().Be(9m); department["ReorderPoint"].Should().Be(10m); department["M5Shortfall"].Should().Be(1m);
			var short_ = result.Rows.Single(r => r["Location"] != null);
			short_["Location"].Should().Be("Synthetic storage"); short_["OnHand"].Should().Be(1m); short_["ReorderPoint"].Should().Be(2m); short_["M5Shortfall"].Should().Be(1m);
		}

		[Test]
		public async Task Visible_low_stock_counts_only_locations_the_caller_can_see()
		{
			var item = Item(); var station = Location(InventoryLocationType.Station); var hidden = Location();
			SeedStock(item, station, 1); ParLevel(item, station, 2); ParLevel(item, hidden, 5); _deniedLocations.Add(hidden.Id);

			var levels = (await _service.GetVisibleLowStockAsync(_actor, new[] { item.Id }))[item.Id];

			levels.Should().HaveCount(2, "the department check and the one visible par level");
			levels.Single(l => l.LocationId == station.Id).IsLow.Should().BeTrue();
			levels.Single(l => l.LocationId == null).IsLow.Should().BeFalse("the item has no department-wide level");
		}
	}
}
