using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model.Inventories;

namespace Resgrid.Tests.Services
{
	public sealed partial class InventoryModernizationTests
	{
		[TestCase(InventoryAssetStatus.Retired)]
		[TestCase(InventoryAssetStatus.Lost)]
		public async Task Valuation_excludes_stock_and_assets_in_terminal_containers_without_failing_the_report(InventoryAssetStatus status)
		{
			var location = Location(); var bagItem = M4Item(InventoryTrackingMode.Serialized);
			var bag = _store.Seed(new InventoryAsset { DepartmentId = Department, ItemId = bagItem.Id, CurrentLocationId = location.Id,
				Status = (int)status, Content = JsonConvert.SerializeObject(new InventoryAssetContent { AcquisitionCost = 100 }) });
			var container = _store.Seed(new InventoryLocation { DepartmentId = Department, ContainerAssetId = bag.Id, LocationType = (int)InventoryLocationType.Container,
				Content = "{}" });
			var bulk = M4Item(); SeedStock(bulk, container, 5); SeedStock(bulk, location, 2);
			_store.Seed(new InventoryAsset { DepartmentId = Department, ItemId = bagItem.Id, CurrentLocationId = container.Id, Status = 0,
				Content = JsonConvert.SerializeObject(new InventoryAssetContent { AcquisitionCost = 50 }) });
			var valuation = await _service.GetValuationAsync(_actor);
			valuation.Lines.Should().ContainSingle().Which.LocationId.Should().Be(location.Id);
			valuation.Totals.Should().ContainSingle().Which.KnownValue.Should().Be(4);
		}

		[TestCase(InventoryTrackingMode.Bulk)]
		[TestCase(InventoryTrackingMode.Serialized)]
		public async Task Generic_receipt_cannot_override_a_fixed_lot_price(InventoryTrackingMode tracking)
		{
			var item = M4Item(tracking); var location = Location();
			var lot = _store.Seed(new InventoryLot { DepartmentId = Department, ItemId = item.Id,
				Content = JsonConvert.SerializeObject(new InventoryLotContent { LotNumber = "Synthetic fixed-price lot", UnitCost = 10 }) });
			var command = M4Receive(item, location, tracking == InventoryTrackingMode.Bulk ? 10 : 1, 20);
			command.Lines[0].LotId = lot.Id;
			InventoryAsset asset = null;
			if (tracking == InventoryTrackingMode.Serialized)
			{
				asset = _store.Seed(new InventoryAsset { DepartmentId = Department, ItemId = item.Id, LotId = lot.Id,
					Content = JsonConvert.SerializeObject(new InventoryAssetContent { SerialNumber = "Synthetic unreceived lot asset" }) });
				command.Lines[0].AssetId = asset.Id;
			}
			var beforeItem = JsonConvert.SerializeObject(_store.All<InventoryItem>().Single());
			var beforeAssets = JsonConvert.SerializeObject(_store.All<InventoryAsset>());

			await Fails(() => _service.PostTransactionAsync(_actor, command), "LotCostMismatch", 409);

			_store.All<InventoryStock>().Should().BeEmpty(); _store.All<InventoryTransaction>().Should().BeEmpty();
			_store.All<InventoryOperation>().Should().BeEmpty(); _events.Should().BeEmpty(); _audits.Should().BeEmpty();
			JsonConvert.SerializeObject(_store.All<InventoryItem>().Single()).Should().Be(beforeItem);
			JsonConvert.SerializeObject(_store.All<InventoryAsset>()).Should().Be(beforeAssets);
		}

		[Test]
		public async Task Generic_serialized_receipt_cannot_replace_existing_acquisition_cost()
		{
			var item = M4Item(InventoryTrackingMode.Serialized); var location = Location();
			var asset = _store.Seed(new InventoryAsset { DepartmentId = Department, ItemId = item.Id,
				Content = JsonConvert.SerializeObject(new InventoryAssetContent { SerialNumber = "Synthetic priced asset", AcquisitionCost = 10 }) });
			var command = M4Receive(item, location, 1, 20); command.Lines[0].AssetId = asset.Id;
			var before = JsonConvert.SerializeObject(asset);

			await Fails(() => _service.PostTransactionAsync(_actor, command), "AssetCostMismatch", 409);

			JsonConvert.SerializeObject(_store.All<InventoryAsset>().Single()).Should().Be(before);
			M4Average(item).Should().BeNull(); _store.All<InventoryTransaction>().Should().BeEmpty();
			_store.All<InventoryOperation>().Should().BeEmpty(); _events.Should().BeEmpty(); _audits.Should().BeEmpty();
		}

		[Test]
		public async Task Asset_creation_rolls_back_when_acquisition_cost_conflicts_with_its_lot()
		{
			var item = M4Item(InventoryTrackingMode.Serialized); var location = Location();
			var lot = _store.Seed(new InventoryLot { DepartmentId = Department, ItemId = item.Id,
				Content = JsonConvert.SerializeObject(new InventoryLotContent { LotNumber = "Synthetic acquisition lot", UnitCost = 10 }) });
			var input = new InventoryAssetInput { RequestId = Guid.NewGuid().ToString("D"), ItemId = item.Id, LotId = lot.Id, LocationId = location.Id,
				Details = new InventoryAssetContent { SerialNumber = "Synthetic conflicting acquisition", AcquisitionCost = 20 } };

			await Fails(() => _service.CreateAssetAsync(_actor, input), "LotCostMismatch", 409);

			_store.All<InventoryAsset>().Should().BeEmpty(); _store.All<InventoryTransaction>().Should().BeEmpty();
			_store.All<InventoryOperation>().Should().BeEmpty(); M4Average(item).Should().BeNull();
			_events.Should().BeEmpty(); _audits.Should().BeEmpty();
		}

		[TestCase(true)]
		[TestCase(false)]
		public async Task Uncosted_serialized_receipt_freezes_resolved_price_before_later_average_changes(bool explicitPrice)
		{
			var item = M4Item(InventoryTrackingMode.Serialized, defaultCost: 10); var location = Location();
			var asset = _store.Seed(new InventoryAsset { DepartmentId = Department, ItemId = item.Id,
				Content = JsonConvert.SerializeObject(new InventoryAssetContent { SerialNumber = "Synthetic initially uncosted asset" }) });
			var command = M4Receive(item, location, 1, 10); command.Lines[0].AssetId = asset.Id;
			if (!explicitPrice) command.Lines[0].UnitCost = null;
			var receipt = await _service.PostTransactionAsync(_actor, command);
			JsonConvert.DeserializeObject<InventoryAssetContent>(_store.All<InventoryAsset>().Single().Content).AcquisitionCost.Should().Be(10);

			await _service.CreateAssetAsync(_actor, new InventoryAssetInput { RequestId = Guid.NewGuid().ToString("D"), ItemId = item.Id, LocationId = location.Id,
				Details = new InventoryAssetContent { SerialNumber = "Synthetic later expensive asset", AcquisitionCost = 30 } });
			M4Average(item).Should().Be(20);
			var valuation = await _service.GetValuationAsync(_actor);
			valuation.Lines.Single(l => l.AssetId == asset.Id).Value.Should().Be(10);
			valuation.Totals.Should().ContainSingle().Which.KnownValue.Should().Be(40);
			(await _service.PostTransactionAsync(_actor, Copy(command))).TransactionIds.Should().Equal(receipt.TransactionIds);
			_store.All<InventoryAsset>().Should().HaveCount(2); _store.All<InventoryTransaction>().Should().HaveCount(2);

			var consume = Move(item, location, null, 1, InventoryTransactionType.Consume); consume.AssetId = asset.Id;
			M4Cost(await _service.PostTransactionAsync(_actor, Command(consume))).Value<decimal>("UnitCost").Should().Be(10);
		}

		[TestCase(true)]
		[TestCase(false)]
		public async Task Fixed_lot_receipts_and_corrections_leave_the_fallback_average_and_valuation_consistent(bool lotFirst)
		{
			var item = M4Item(defaultCost: null); var location = Location();
			var lot = _store.Seed(new InventoryLot { DepartmentId = Department, ItemId = item.Id,
				Content = JsonConvert.SerializeObject(new InventoryLotContent { LotNumber = "Synthetic independent cost pool", UnitCost = 10 }) });
			var lotCommand = M4Receive(item, location, 10, 10); lotCommand.Lines[0].LotId = lot.Id;
			var bulkCommand = M4Receive(item, location, 10, 20);
			InventoryResult lotReceipt;
			if (lotFirst)
			{
				lotReceipt = await _service.PostTransactionAsync(_actor, lotCommand); M4Average(item).Should().BeNull();
				await _service.PostTransactionAsync(_actor, bulkCommand);
			}
			else
			{
				await _service.PostTransactionAsync(_actor, bulkCommand);
				lotReceipt = await _service.PostTransactionAsync(_actor, lotCommand);
			}
			M4Average(item).Should().Be(20);
			var valuation = await _service.GetValuationAsync(_actor);
			valuation.Lines.Single(l => l.LotId == lot.Id).Value.Should().Be(100);
			valuation.Lines.Single(l => l.LotId == null).Value.Should().Be(200);
			valuation.Totals.Should().ContainSingle().Which.KnownValue.Should().Be(300);

			var consume = Move(item, location, null, 1, InventoryTransactionType.Consume); consume.LotId = lot.Id;
			var used = await _service.PostTransactionAsync(_actor, Command(consume));
			var restore = Move(item, null, location, 1, InventoryTransactionType.Adjust); restore.LotId = lot.Id; restore.ReversesTransactionId = used.TransactionIds.Single();
			await _service.PostTransactionAsync(_actor, Command(restore)); M4Average(item).Should().Be(20);
			(await _service.GetValuationAsync(_actor)).Totals.Should().ContainSingle().Which.KnownValue.Should().Be(300);

			var remove = Move(item, location, null, 10, InventoryTransactionType.Adjust); remove.LotId = lot.Id; remove.ReversesTransactionId = lotReceipt.TransactionIds.Single();
			await _service.PostTransactionAsync(_actor, Command(remove)); M4Average(item).Should().Be(20);
			(await _service.GetValuationAsync(_actor)).Totals.Should().ContainSingle().Which.KnownValue.Should().Be(200);
		}

		[TestCase(false)]
		[TestCase(true)]
		public async Task Ordered_fractional_bulk_purchase_locks_tracking_and_currency_before_any_receipt(bool changeCurrency)
		{
			var item = M4Item(); var location = Location(); var order = await M4Ordered((item, 1.5m, 3m));
			_store.All<InventoryTransaction>().Should().BeEmpty();
			var current = _store.All<InventoryItem>().Single(); var before = JsonConvert.SerializeObject(current);
			var input = new InventoryItemInput { Id = current.Id, Revision = current.Revision, TrackingMode = InventoryTrackingMode.Bulk,
				Details = JsonConvert.DeserializeObject<InventoryItemContent>(current.Content) };
			if (changeCurrency) input.Details.CurrencyCode = "EUR"; else input.TrackingMode = InventoryTrackingMode.Serialized;
			await Fails(() => _service.SaveItemAsync(_actor, input), changeCurrency ? "ItemCurrencyLocked" : "ItemTrackingLocked", 409);
			JsonConvert.SerializeObject(_store.All<InventoryItem>().Single()).Should().Be(before);
			_store.All<InventoryPurchaseOrderItem>().Single().QuantityReceived.Should().Be(0);

			await _service.ReceivePurchaseOrderAsync(_actor, M4Receipt(order, location, 1.5m));
			Stock(item, location).Should().Be(1.5m);
			_store.All<InventoryPurchaseOrder>().Single().Status.Should().Be((int)InventoryPurchaseOrderStatus.Received);
			_store.All<InventoryPurchaseOrderItem>().Single().QuantityReceived.Should().Be(1.5m);
		}
	}
}
