using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Inventories;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Inventory import: a spreadsheet seeds items, categories, locations, lots, assets, par levels, costs and the opening
	/// count. The file is a count (quantities are absolute), preview writes nothing, and apply refuses a stale preview.
	/// </summary>
	public sealed partial class InventoryModernizationTests
	{
		private const string OpeningCount =
			"Item,Category,UnitOfMeasure,UnitCost,Currency,Location,Quantity,ReorderPoint\r\n" +
			"Gauze 4x4,Medical > Dressings,box,2.50,USD,Main Warehouse,40,10\r\n" +
			"Gauze 4x4,,,,,Main Warehouse > Shelf A,12,\r\n";

		private static InventoryImportInput ImportFile(string csv, string requestId = null) => InventoryImportCsv.Parse(csv, requestId ?? Guid.NewGuid().ToString("D"));

		private static string Problems(InventoryImportPreview preview)
			=> string.Join("; ", preview.Rows.Where(r => r.ErrorCode != null).Select(r => r.RowNumber + ":" + r.ErrorCode + ":" + r.ErrorColumn));

		private async Task<InventoryImportResult> ImportAsync(string csv, string requestId = null)
		{
			var input = ImportFile(csv, requestId);
			var preview = await _service.PreviewImportAsync(_actor, input);
			preview.ErrorRows.Should().Be(0, Problems(preview));
			input.PreviewHash = preview.PreviewHash;
			return await _service.ApplyImportAsync(_actor, input);
		}

		private InventoryItem ItemNamed(string name) => _store.All<InventoryItem>().Single(i => JsonConvert.DeserializeObject<InventoryItemContent>(i.Content).Name == name);
		private InventoryLocation LocationNamed(string name) => _store.All<InventoryLocation>().Single(l => JsonConvert.DeserializeObject<InventoryLabel>(l.Content).Name == name);
		private static string NameOf(InventoryItem item) => JsonConvert.DeserializeObject<InventoryItemContent>(item.Content).Name;

		[Test]
		public async Task Preview_plans_the_opening_count_and_writes_nothing()
		{
			var preview = await _service.PreviewImportAsync(_actor, ImportFile(OpeningCount));

			preview.ErrorRows.Should().Be(0, Problems(preview)); preview.CanApply.Should().BeTrue();
			preview.Totals.ItemsCreated.Should().Be(1); preview.Totals.CategoriesCreated.Should().Be(2); preview.Totals.LocationsCreated.Should().Be(2);
			preview.Totals.Receipts.Should().Be(2);
			preview.Rows[0].ItemAction.Should().Be(InventoryImportItemAction.Create); preview.Rows[0].StockAction.Should().Be(InventoryImportStockAction.Receive);
			preview.Rows[0].Change.Should().Be(40); preview.Rows[1].LocationName.Should().Be("Main Warehouse > Shelf A");
			preview.AddedValue.Should().ContainSingle().Which.Should().BeEquivalentTo(new InventoryValuationTotal { CurrencyCode = "USD", KnownValue = 130m, UncostedRows = 0 });
			_store.All<InventoryItem>().Should().BeEmpty(); _store.All<InventoryLocation>().Should().BeEmpty(); _store.All<InventoryTransaction>().Should().BeEmpty();
			_transaction.Should().BeNull("preview never opens a write transaction");
		}

		[Test]
		public async Task Applying_seeds_items_categories_locations_costs_and_on_hand_stock()
		{
			var result = await ImportAsync(OpeningCount);

			result.AlreadyApplied.Should().BeFalse(); result.TransactionCount.Should().Be(2);
			result.Totals.ItemsCreated.Should().Be(1); result.Totals.LocationsCreated.Should().Be(2); result.Totals.CategoriesCreated.Should().Be(2);
			var item = ItemNamed("Gauze 4x4"); var details = JsonConvert.DeserializeObject<InventoryItemContent>(item.Content);
			details.UnitOfMeasure.Should().Be("box"); details.CurrencyCode.Should().Be("USD"); details.ReorderPoint.Should().Be(10);
			details.DefaultUnitCost.Should().Be(2.5m); details.AverageUnitCost.Should().Be(2.5m);
			var dressings = _store.All<InventoryCategory>().Single(c => c.Id == item.CategoryId);
			JsonConvert.DeserializeObject<InventoryLabel>(dressings.Content).Name.Should().Be("Dressings");
			JsonConvert.DeserializeObject<InventoryLabel>(_store.All<InventoryCategory>().Single(c => c.Id == dressings.ParentCategoryId).Content).Name.Should().Be("Medical");
			var warehouse = LocationNamed("Main Warehouse"); var shelf = LocationNamed("Shelf A");
			shelf.ParentLocationId.Should().Be(warehouse.Id); warehouse.LocationType.Should().Be((int)InventoryLocationType.Facility);
			Stock(item, warehouse).Should().Be(40); Stock(item, shelf).Should().Be(12);
			var receipts = _store.All<InventoryTransaction>().ToList();
			receipts.Should().OnlyContain(t => t.TransactionType == (int)InventoryTransactionType.Receive && t.FromLocationId == null);
			receipts.Should().OnlyContain(t => t.OperationId == result.OperationId);
			_audits.Select(a => a.Message).Should().Contain(new[] { "InventoryItemSaved", "InventoryLocationSaved", "InventoryCategorySaved", "InventoryTransactionPosted" });
			_dispatches.SelectMany(d => d).Should().NotBeEmpty("ledger events go out after the import commits");
		}

		[Test]
		public async Task Importing_a_corrected_count_posts_only_the_difference()
		{
			await ImportAsync(OpeningCount);
			var item = ItemNamed("Gauze 4x4"); var warehouse = LocationNamed("Main Warehouse"); var shelf = LocationNamed("Shelf A");
			var corrected = "Item,Location,Quantity\r\nGauze 4x4,Main Warehouse,35\r\nGauze 4x4,Main Warehouse > Shelf A,12\r\n";

			var preview = await _service.PreviewImportAsync(_actor, ImportFile(corrected));
			preview.Rows[0].StockAction.Should().Be(InventoryImportStockAction.Decrease); preview.Rows[0].CurrentQuantity.Should().Be(40); preview.Rows[0].Change.Should().Be(-5);
			preview.Rows[1].StockAction.Should().Be(InventoryImportStockAction.Unchanged);
			preview.Rows.Should().OnlyContain(r => r.ItemAction == InventoryImportItemAction.Existing);
			preview.Totals.LocationsCreated.Should().Be(0); preview.Totals.ItemsCreated.Should().Be(0);

			var result = await ImportAsync(corrected);
			result.TransactionCount.Should().Be(1);
			Stock(item, warehouse).Should().Be(35); Stock(item, shelf).Should().Be(12);
			var adjustment = _store.All<InventoryTransaction>().Single(t => t.OperationId == result.OperationId);
			adjustment.TransactionType.Should().Be((int)InventoryTransactionType.Adjust); adjustment.FromLocationId.Should().Be(warehouse.Id); adjustment.Quantity.Should().Be(5);
		}

		[Test]
		public async Task A_stale_preview_is_refused_and_nothing_is_written()
		{
			await ImportAsync(OpeningCount);
			var item = ItemNamed("Gauze 4x4"); var warehouse = LocationNamed("Main Warehouse");
			var input = ImportFile("Item,Location,Quantity\r\nGauze 4x4,Main Warehouse,50\r\n");
			input.PreviewHash = (await _service.PreviewImportAsync(_actor, input)).PreviewHash;
			await _service.PostTransactionAsync(_actor, Receive(item, warehouse, 3)); // Someone receives stock after the preview.
			var before = _store.All<InventoryTransaction>().Count();

			await Fails(() => _service.ApplyImportAsync(_actor, input), "ImportPreviewChanged", 409);

			_store.All<InventoryTransaction>().Should().HaveCount(before); Stock(item, warehouse).Should().Be(43);
			_store.All<InventoryOperation>().Should().NotContain(o => o.RequestId == input.RequestId);
		}

		[Test]
		public async Task Applying_the_same_request_again_returns_the_first_result()
		{
			var requestId = Guid.NewGuid().ToString("D");
			var first = await ImportAsync(OpeningCount, requestId);
			var count = _store.All<InventoryTransaction>().Count();

			var input = ImportFile(OpeningCount, requestId); input.PreviewHash = "not checked on a replay";
			var replay = await _service.ApplyImportAsync(_actor, input);

			replay.AlreadyApplied.Should().BeTrue(); replay.OperationId.Should().Be(first.OperationId);
			replay.Totals.Should().BeEquivalentTo(first.Totals); replay.TransactionCount.Should().Be(first.TransactionCount);
			_store.All<InventoryTransaction>().Should().HaveCount(count); _store.All<InventoryItem>().Should().ContainSingle();
		}

		[Test]
		public async Task Row_problems_are_reported_per_row_and_block_the_whole_file()
		{
			var controlled = Item(controlled: true);
			_unitsMock.Setup(x => x.GetUnitsForDepartmentAsync(Department)).ReturnsAsync(new List<Unit> { new() { UnitId = 5, DepartmentId = Department, Name = "Engine 1", StationGroupId = 10 } });
			var csv = "Item,Unit,Quantity,SerialNumber,Tracking,UnitCost\r\n" +
				"Good item,Engine 1,4,,,\r\n" +
				NameOf(controlled) + ",,1,,,\r\n" +
				"Bulk thing,Engine 9,2,,,\r\n" +
				"Bulk thing,Engine 1,,SN-1,Bulk,\r\n" +
				"Priced thing,Engine 1,1,,,abc\r\n";

			var input = ImportFile(csv);
			var preview = await _service.PreviewImportAsync(_actor, input);

			preview.CanApply.Should().BeFalse(); preview.ErrorRows.Should().Be(4);
			preview.Rows.Single(r => r.RowNumber == 2).ErrorCode.Should().BeNull();
			preview.Rows.Single(r => r.RowNumber == 3).ErrorCode.Should().Be("ImportControlledSubstance");
			preview.Rows.Single(r => r.RowNumber == 4).Should().BeEquivalentTo(new { ErrorCode = "ImportUnitNotFound", ErrorColumn = "Unit" }, o => o.ExcludingMissingMembers());
			preview.Rows.Single(r => r.RowNumber == 5).ErrorCode.Should().Be("ImportSerialOnBulkItem");
			preview.Rows.Single(r => r.RowNumber == 6).Should().BeEquivalentTo(new { ErrorCode = "ImportInvalidNumber", ErrorColumn = "UnitCost" }, o => o.ExcludingMissingMembers());

			input.PreviewHash = preview.PreviewHash;
			await Fails(() => _service.ApplyImportAsync(_actor, input), "ImportHasErrors", 400);
			_store.All<InventoryItem>().Should().ContainSingle("only the seeded controlled item exists"); _store.All<InventoryTransaction>().Should().BeEmpty();
		}

		[Test]
		public async Task Unit_storage_and_compartments_are_found_or_created_by_name()
		{
			_unitsMock.Setup(x => x.GetUnitsForDepartmentAsync(Department)).ReturnsAsync(new List<Unit> { new() { UnitId = 5, DepartmentId = Department, Name = "Engine 1", StationGroupId = 10 } });
			var csv = "Item,Unit,Location,Quantity\r\n" +
				"Flashlight batteries,Engine 1,,6\r\n" +
				"Trauma shears,Engine 1,Driver side compartment,2\r\n" +
				"Flashlight batteries,Engine 1,Driver side compartment,4\r\n";

			await ImportAsync(csv);

			var root = _store.All<InventoryLocation>().Single(l => l.LocationType == (int)InventoryLocationType.Unit);
			root.UnitId.Should().Be(5); JsonConvert.DeserializeObject<InventoryLabel>(root.Content).Name.Should().Be("Engine 1");
			var compartment = LocationNamed("Driver side compartment");
			compartment.ParentLocationId.Should().Be(root.Id); compartment.LocationType.Should().Be((int)InventoryLocationType.Facility);
			_store.All<InventoryLocation>().Should().HaveCount(2, "the unit's storage and its compartment are each created once");
			Stock(ItemNamed("Flashlight batteries"), root).Should().Be(6); Stock(ItemNamed("Flashlight batteries"), compartment).Should().Be(4);
			JsonConvert.DeserializeObject<InventoryItemContent>(ItemNamed("Trauma shears").Content).UnitOfMeasure.Should().Be("unit");

			var again = await _service.PreviewImportAsync(_actor, ImportFile("Item,Unit,Location,Quantity\r\nTrauma shears,Engine 1,Driver side compartment,2\r\n"));
			again.Rows.Single().StockAction.Should().Be(InventoryImportStockAction.Unchanged); again.Totals.LocationsCreated.Should().Be(0);
		}

		[Test]
		public async Task Serialized_equipment_becomes_received_assets_and_known_serials_are_left_alone()
		{
			var csv = "Item,SerialNumber,AssetTag,UnitCost,Location\r\nPortable radio,RAD-001,T-1,650,Radio cage\r\nPortable radio,RAD-002,T-2,650,Radio cage\r\n";

			await ImportAsync(csv);

			var radio = ItemNamed("Portable radio"); radio.TrackingMode.Should().Be((int)InventoryTrackingMode.Serialized);
			var cage = LocationNamed("Radio cage");
			var assets = _store.All<InventoryAsset>().ToList();
			assets.Should().HaveCount(2).And.OnlyContain(a => a.CurrentLocationId == cage.Id && a.Status == (int)InventoryAssetStatus.InService);
			assets.Select(a => JsonConvert.DeserializeObject<InventoryAssetContent>(a.Content)).Should().OnlyContain(d => d.AcquisitionCost == 650m);
			_store.All<InventoryTransaction>().Should().HaveCount(2).And.OnlyContain(t => t.AssetId != null && t.TransactionType == (int)InventoryTransactionType.Receive);

			var moved = await _service.PreviewImportAsync(_actor, ImportFile("Item,SerialNumber,Location\r\nPortable radio,RAD-001,Somewhere else\r\n"));
			var row = moved.Rows.Single();
			row.ErrorCode.Should().BeNull(); row.StockAction.Should().Be(InventoryImportStockAction.AssetExists); row.Warnings.Should().Contain("ImportAssetElsewhere");
		}

		[Test]
		public async Task Expiring_bulk_items_are_kept_by_dated_lots()
		{
			var csv = "Item,RequiresExpiration,Lot,Expires,Quantity,Location\r\n" +
				"Saline 1L,Yes,L-77,2027-03-31,20,Pharmacy\r\n" +
				"Saline 1L,,L-78,2027-06-30,5,Pharmacy\r\n" +
				"Saline 1L,,,,5,Pharmacy\r\n";

			var preview = await _service.PreviewImportAsync(_actor, ImportFile(csv));
			preview.Rows.Single(r => r.RowNumber == 4).Should().BeEquivalentTo(new { ErrorCode = "LotRequired", ErrorColumn = "Lot" }, o => o.ExcludingMissingMembers());

			await ImportAsync(string.Join("\r\n", csv.Split("\r\n").Take(3)) + "\r\n");

			var saline = ItemNamed("Saline 1L"); saline.RequiresExpiration.Should().BeTrue(); saline.RequiresLotTracking.Should().BeTrue();
			var lots = _store.All<InventoryLot>().ToList();
			lots.Should().HaveCount(2);
			lots.Single(l => JsonConvert.DeserializeObject<InventoryLotContent>(l.Content).LotNumber == "L-77").ExpiresOn.Should().Be(new DateTime(2027, 3, 31, 0, 0, 0, DateTimeKind.Utc));
			_store.All<InventoryStock>().Where(s => s.ItemId == saline.Id).Sum(s => s.Quantity).Should().Be(25);
		}

		[Test]
		public async Task Existing_items_take_file_values_but_never_change_how_they_are_tracked()
		{
			var existing = Item(); var name = NameOf(existing);
			var details = JsonConvert.DeserializeObject<InventoryItemContent>(existing.Content); details.DefaultUnitCost = null; existing.Content = JsonConvert.SerializeObject(details);
			existing = _store.Seed(existing);

			var preview = await _service.PreviewImportAsync(_actor, ImportFile("Item,Code,UnitCost\r\n" + name + ",MED-0042,3.75\r\n"));
			var row = preview.Rows.Single();
			row.ItemAction.Should().Be(InventoryImportItemAction.Update); row.ChangedFields.Should().BeEquivalentTo(new[] { "Code", "UnitCost" });

			await ImportAsync("Item,Code,UnitCost\r\n" + name + ",MED-0042,3.75\r\n");
			var saved = JsonConvert.DeserializeObject<InventoryItemContent>(_store.All<InventoryItem>().Single(i => i.Id == existing.Id).Content);
			saved.Code.Should().Be("MED-0042"); saved.DefaultUnitCost.Should().Be(3.75m); saved.Description.Should().Be(Canary, "values the file leaves blank are kept");

			var priced = await _service.PreviewImportAsync(_actor, ImportFile("Item,UnitCost\r\n" + name + ",9\r\n"));
			priced.Rows.Single().ItemAction.Should().Be(InventoryImportItemAction.Existing, "a priced item's cost is not overwritten from a row");

			var retrack = await _service.PreviewImportAsync(_actor, ImportFile("Item,Tracking\r\n" + name + ",Serialized\r\n"));
			retrack.Rows.Single().Should().BeEquivalentTo(new { ErrorCode = "ImportItemTrackingMismatch", ErrorColumn = "Tracking" }, o => o.ExcludingMissingMembers());
		}

		[Test]
		public async Task Par_levels_are_set_per_location()
		{
			await ImportAsync("Item,Location,Quantity,ParLevel\r\nNitrile gloves M,Station 1 Supply,8,10\r\n");

			var gloves = ItemNamed("Nitrile gloves M"); var supply = LocationNamed("Station 1 Supply");
			_store.All<InventoryParLevel>().Should().ContainSingle(p => p.ItemId == gloves.Id && p.LocationId == supply.Id && p.MinimumQuantity == 10 && !p.IsDeleted);
			_store.All<InventoryAlert>().Should().Contain(a => a.ItemId == gloves.Id && a.LocationId == supply.Id && a.AlertType == (int)InventoryAlertType.LowStock && a.Status == 0,
				"8 on hand is under the minimum of 10");
		}

		[Test]
		public async Task Locations_the_caller_cannot_stock_are_refused_on_their_rows()
		{
			var locked = Location(); _deniedLocations.Add(locked.Id);

			var preview = await _service.PreviewImportAsync(_actor, ImportFile("Item,Location,Quantity\r\nAnything,Synthetic storage,1\r\n"));

			preview.Rows.Single().Should().BeEquivalentTo(new { ErrorCode = "LocationUnavailable", ErrorColumn = "Location" }, o => o.ExcludingMissingMembers());
		}

		[Test]
		public async Task The_export_imports_back_unchanged()
		{
			await ImportAsync(OpeningCount);
			await ImportAsync("Item,SerialNumber,UnitCost,Location,ParLevel\r\nPortable radio,RAD-001,650,Radio cage,\r\nPortable radio,,,Main Warehouse,1\r\n");

			var file = await _service.ExportImportFileAsync(_actor);
			file.Should().StartWith(string.Join(",", InventoryImportCsv.Header.Select(h => "\"" + h + "\"")));
			var preview = await _service.PreviewImportAsync(_actor, ImportFile(file));

			preview.ErrorRows.Should().Be(0, Problems(preview));
			preview.Rows.Should().OnlyContain(r => r.ItemAction == InventoryImportItemAction.Existing);
			preview.Rows.Should().OnlyContain(r => r.StockAction == InventoryImportStockAction.Unchanged || r.StockAction == InventoryImportStockAction.AssetExists || r.StockAction == InventoryImportStockAction.None);
			preview.Totals.Should().BeEquivalentTo(new InventoryImportTotals { Unchanged = 3 });
		}

		[Test]
		public async Task Import_needs_inventory_to_be_set_up_first()
		{
			_store.Migrated = false;
			await Fails(() => _service.PreviewImportAsync(_actor, ImportFile(OpeningCount)), "MigrationRequired", 409);
		}
	}
}
