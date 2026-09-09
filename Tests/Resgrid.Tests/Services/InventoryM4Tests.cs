using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Inventories;

namespace Resgrid.Tests.Services
{
	public sealed partial class InventoryModernizationTests
	{
		[Test]
		public async Task Receipts_weight_the_average_before_moving_stock_and_replay_preserves_cost_and_ledger()
		{
			var item = M4Item(); var location = Location();
			var first = await _service.PostTransactionAsync(_actor, M4Receive(item, location, 10, 2.125001m));
			var command = M4Receive(item, location, 30, 4.125001m);
			var second = await _service.PostTransactionAsync(_actor, command);
			M4Average(item).Should().Be(3.625001m); Stock(item, location).Should().Be(40);
			M4Cost(first).Value<decimal>("UnitCost").Should().Be(2.125001m);
			M4Cost(first).Value<decimal>("TotalCost").Should().Be(21.250010m);
			M4Cost(second).Value<decimal>("TotalCost").Should().Be(123.750030m);
			M4Cost(second).Value<string>("CurrencyCode").Should().Be("USD");
			var version = _store.All<InventoryItem>().Single().Revision;
			(await _service.PostTransactionAsync(_actor, Copy(command))).TransactionIds.Should().Equal(second.TransactionIds);
			M4Average(item).Should().Be(3.625001m); _store.All<InventoryItem>().Single().Revision.Should().Be(version);
			_store.All<InventoryTransaction>().Should().HaveCount(2); _events.Should().HaveCount(2);
		}

		[TestCase(true, true, true, "7.5")]
		[TestCase(true, false, true, "6.5")]
		[TestCase(false, true, true, "7.5")]
		[TestCase(false, false, true, "5.5")]
		[TestCase(false, false, false, "4.5")]
		public async Task Outbound_cost_precedence_is_lot_then_asset_then_average_then_default(bool serialized, bool withLot, bool withAverage, string expected)
		{
			var item = M4Item(serialized ? InventoryTrackingMode.Serialized : InventoryTrackingMode.Bulk, defaultCost: 4.5m, average: withAverage ? 5.5m : null);
			var location = Location();
			var lot = withLot ? _store.Seed(new InventoryLot { DepartmentId = Department, ItemId = item.Id,
				Content = JsonConvert.SerializeObject(new InventoryLotContent { LotNumber = "Synthetic lot", UnitCost = 7.5m }) }) : null;
			var movement = Move(item, location, null, 1, InventoryTransactionType.Consume); movement.LotId = lot?.Id;
			if (serialized) movement.AssetId = _store.Seed(new InventoryAsset { DepartmentId = Department, ItemId = item.Id, LotId = lot?.Id,
				CurrentLocationId = location.Id, Status = 0, Content = JsonConvert.SerializeObject(new InventoryAssetContent { SerialNumber = "costed-asset", AcquisitionCost = 6.5m }) }).Id;
			else _store.Seed(new InventoryStock { DepartmentId = Department, ItemId = item.Id, LocationId = location.Id, LotId = lot?.Id, Quantity = 2 });
			var result = await _service.PostTransactionAsync(_actor, Command(movement));
			M4Cost(result).Value<decimal>("UnitCost").Should().Be(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
		}

		[Test]
		public async Task Same_item_receipt_lines_update_the_average_sequentially_with_six_decimal_rounding()
		{
			var item = M4Item(defaultCost: null); var location = Location();
			var first = Move(item, null, location, 1, InventoryTransactionType.Receive); first.UnitCost = 1;
			var second = Move(item, null, location, 2, InventoryTransactionType.Receive); second.UnitCost = 2;
			await _service.PostTransactionAsync(_actor, Command(first, second));
			M4Average(item).Should().Be(1.666667m); Stock(item, location).Should().Be(3);
		}

		[Test]
		public async Task Unknown_opening_value_stays_unknown_when_receiving_priced_stock()
		{
			var item = M4Item(defaultCost: null); var location = Location(); SeedStock(item, location, 2);
			await _service.PostTransactionAsync(_actor, M4Receive(item, location, 3, 5));
			M4Average(item).Should().BeNull();
			var valuation = await _service.GetValuationAsync(_actor);
			valuation.Lines.Should().ContainSingle().Which.Value.Should().BeNull();
			valuation.Totals.Should().ContainSingle().Which.UncostedRows.Should().Be(1);
		}

		[Test]
		public async Task Consumption_correction_freezes_original_cost_after_later_receipts_and_rejects_repricing()
		{
			var item = M4Item(); var location = Location();
			await _service.PostTransactionAsync(_actor, M4Receive(item, location, 10, 2));
			var used = await _service.PostTransactionAsync(_actor, Command(Move(item, location, null, 3, InventoryTransactionType.Consume)));
			var original = _store.All<InventoryTransaction>().Single(x => x.Id == used.TransactionIds.Single());
			var originalContent = original.Content;
			await _service.PostTransactionAsync(_actor, M4Receive(item, location, 5, 8)); M4Average(item).Should().Be(4.5m);
			var reversal = Move(item, null, location, 3, InventoryTransactionType.Adjust); reversal.ReversesTransactionId = original.Id; reversal.UnitCost = 9;
			await Fails(() => _service.PostTransactionAsync(_actor, Command(reversal)), "ReversalCostMismatch", 409);
			reversal.UnitCost = null; var corrected = await _service.PostTransactionAsync(_actor, Command(reversal));
			M4Cost(corrected).Value<decimal>("UnitCost").Should().Be(2); M4Cost(corrected).Value<decimal>("TotalCost").Should().Be(6);
			M4Average(item).Should().Be(4m); _store.All<InventoryTransaction>().Single(x => x.Id == original.Id).Content.Should().Be(originalContent);
			(await _service.GetValuationAsync(_actor)).Totals.Single().KnownValue.Should().Be(60m);
		}

		[Test]
		public async Task Receipt_reversal_removes_its_original_value_from_the_weighted_average()
		{
			var item = M4Item(); var location = Location();
			await _service.PostTransactionAsync(_actor, M4Receive(item, location, 10, 2));
			var receipt = await _service.PostTransactionAsync(_actor, M4Receive(item, location, 10, 4)); M4Average(item).Should().Be(3);
			var reversal = Move(item, location, null, 10, InventoryTransactionType.Adjust); reversal.ReversesTransactionId = receipt.TransactionIds.Single();
			var result = await _service.PostTransactionAsync(_actor, Command(reversal));
			M4Average(item).Should().Be(2); Stock(item, location).Should().Be(10); M4Cost(result).Value<decimal>("UnitCost").Should().Be(4);
		}

		[Test]
		public async Task Issuance_return_uses_the_frozen_issue_cost_without_repricing_the_current_average()
		{
			var item = M4Item(); var location = Location(); var returnedTo = Location();
			await _service.PostTransactionAsync(_actor, M4Receive(item, location, 10, 2));
			var issued = await _service.IssueAsync(_actor, Issue(item, location, 3, userId: "member"));
			await _service.PostTransactionAsync(_actor, M4Receive(item, location, 4, 9));
			var average = M4Average(item); average.Should().Be(4);
			var issuance = _store.All<InventoryIssuance>().Single(x => x.Id == issued.IssuanceId);
			var returned = await _service.ReturnAsync(_actor, new InventoryReturnInput { RequestId = Guid.NewGuid().ToString("D"), IssuanceId = issuance.Id,
				Revision = issuance.Revision, ToLocationId = returnedTo.Id, Quantity = 3, Condition = InventoryAssetStatus.InService });
			M4Cost(returned).Value<decimal>("UnitCost").Should().Be(2); M4Cost(returned).Value<decimal>("TotalCost").Should().Be(6); M4Average(item).Should().Be(average);
		}

		[TestCase(InventoryTransactionType.Consume)]
		[TestCase(InventoryTransactionType.Adjust)]
		[TestCase(InventoryTransactionType.WriteOff)]
		public async Task Ordinary_outbound_movements_cannot_override_the_server_cost(InventoryTransactionType type)
		{
			var item = M4Item(); var location = Location(); SeedStock(item, location, 3);
			var movement = Move(item, location, null, 1, type); movement.UnitCost = 99;
			await Fails(() => _service.PostTransactionAsync(_actor, Command(movement)), "CostOverrideUnsupported", 400);
			Stock(item, location).Should().Be(3); _store.All<InventoryTransaction>().Should().BeEmpty(); _events.Should().BeEmpty();
		}

		[Test]
		public async Task Item_updates_preserve_server_average_and_lock_currency_after_ledger_history()
		{
			var item = M4Item(); var location = Location(); await _service.PostTransactionAsync(_actor, M4Receive(item, location, 4, 3));
			var current = _store.All<InventoryItem>().Single(); var details = JsonConvert.DeserializeObject<InventoryItemContent>(current.Content);
			details.AverageUnitCost = 999; details.DefaultUnitCost = 5;
			var input = new InventoryItemInput { Id = item.Id, Revision = current.Revision, TrackingMode = InventoryTrackingMode.Bulk, Details = details };
			var saved = await _service.SaveItemAsync(_actor, input);
			M4Average(item).Should().Be(3); JsonConvert.DeserializeObject<InventoryItemContent>(saved.Content).DefaultUnitCost.Should().Be(5);
			input.Revision = saved.Revision; input.Details.CurrencyCode = "EUR";
			await Fails(() => _service.SaveItemAsync(_actor, input), "ItemCurrencyLocked", 409);
			JsonConvert.DeserializeObject<InventoryItemContent>(_store.All<InventoryItem>().Single().Content).CurrencyCode.Should().Be("USD");
			var added = await _service.SaveItemAsync(_actor, new InventoryItemInput { Details = new InventoryItemContent { Name = "Synthetic new item", UnitOfMeasure = "each", CurrencyCode = " eur ", AverageUnitCost = 999 } });
			var addedDetails = JsonConvert.DeserializeObject<InventoryItemContent>(added.Content); addedDetails.AverageUnitCost.Should().BeNull(); addedDetails.CurrencyCode.Should().Be("EUR");
		}

		[Test]
		public async Task Vendor_identity_uses_the_existing_company_contact_and_cannot_switch_or_duplicate_it()
		{
			var contact = M4CompanyContact(new string('c', 128));
			var vendor = await _service.SaveVendorAsync(_actor, new InventoryVendorInput { ContactId = contact.ContactId, Details = new() { AccountNumber = "Synthetic account", Note = Canary } });
			vendor.ContactId.Should().Be(contact.ContactId);
			(await _service.GetVendorContactsAsync(_actor)).Should().ContainSingle(x => x.Id == contact.ContactId && x.Name == contact.CompanyName);
			await Fails(() => _service.SaveVendorAsync(_actor, new InventoryVendorInput { ContactId = contact.ContactId }), "DuplicateVendor", 409);
			var other = M4CompanyContact();
			await Fails(() => _service.SaveVendorAsync(_actor, new InventoryVendorInput { Id = vendor.Id, Revision = vendor.Revision, ContactId = other.ContactId }), "VendorContactImmutable", 409);
			_store.All<InventoryVendor>().Should().ContainSingle().Which.ContactId.Should().Be(contact.ContactId);
		}

		[TestCase(88, false, 1)]
		[TestCase(Department, true, 1)]
		[TestCase(Department, false, 0)]
		public async Task Vendor_rejects_a_foreign_deleted_or_noncompany_contact(int departmentId, bool deleted, int contactType)
		{
			var contact = M4CompanyContact(); contact.DepartmentId = departmentId; contact.IsDeleted = deleted; contact.ContactType = contactType;
			await Fails(() => _service.SaveVendorAsync(_actor, new InventoryVendorInput { ContactId = contact.ContactId }), "VendorContactUnavailable", 404);
			_store.All<InventoryVendor>().Should().BeEmpty(); _audits.Should().BeEmpty();
		}

		[TestCase(PermissionTypes.AdjustInventory)]
		[TestCase(PermissionTypes.ContactView)]
		public async Task Purchasing_requires_inventory_and_contact_permissions_before_contact_resolution(PermissionTypes denied)
		{
			var contact = M4CompanyContact();
			_auth.Setup(x => x.RequireAsync(It.IsAny<InventoryActor>(), It.IsAny<bool>(), denied, It.IsAny<int?>())).ThrowsAsync(new InventoryException(403, "PermissionRequired"));
			await Fails(() => _service.SaveVendorAsync(_actor, new InventoryVendorInput { ContactId = contact.ContactId }), "PermissionRequired", 403);
			_contacts.Verify(x => x.GetContactByIdAsync(It.IsAny<string>()), Times.Never); _store.All<InventoryVendor>().Should().BeEmpty();
		}

		[Test]
		public async Task Ordered_purchase_lines_and_supplier_snapshot_cannot_be_replaced_by_a_stale_draft()
		{
			var item = M4Item(); var draft = await M4Draft((item, 5m, 3m));
			var change = new InventoryPurchaseOrderChange { Id = draft.Detail.Order.Id, Revision = draft.Detail.Order.Revision, RequestId = Guid.NewGuid().ToString("D") };
			await _service.ChangePurchaseOrderStatusAsync(_actor, change, InventoryPurchaseOrderStatus.Ordered);
			await _service.ChangePurchaseOrderStatusAsync(_actor, Copy(change), InventoryPurchaseOrderStatus.Ordered);
			var ordered = await _service.GetPurchaseOrderAsync(_actor, change.Id); var originalLine = JsonConvert.SerializeObject(ordered.Lines.Single());
			draft.Input.Revision = ordered.Order.Revision; draft.Input.Lines[0].UnitCost = 99;
			await Fails(() => _service.SavePurchaseOrderAsync(_actor, draft.Input), "RevisionConflict", 409);
			_companyContacts.Values.Single().CompanyName = "Changed company name";
			var current = await _service.GetPurchaseOrderAsync(_actor, change.Id);
			JsonConvert.SerializeObject(current.Lines.Single()).Should().Be(originalLine);
			JsonConvert.DeserializeObject<InventoryPurchaseOrderContent>(current.Order.Content).SupplierName.Should().Be(Canary);
			_store.All<InventoryOperation>().Should().ContainSingle(); _events.Should().BeEmpty();
		}

		[Test]
		public async Task Purchase_order_currency_must_match_each_item_and_a_failed_draft_leaves_no_order_or_lines()
		{
			var usd = M4Item(); var eur = M4Item(currency: "EUR");
			await Fails(async () => { await M4Draft((usd, 2m, 3m), (eur, 1m, 4m)); }, "ItemCurrencyMismatch", 409);
			_store.All<InventoryPurchaseOrder>().Should().BeEmpty(); _store.All<InventoryPurchaseOrderItem>().Should().BeEmpty();
			_store.All<InventoryTransaction>().Should().BeEmpty(); _events.Should().BeEmpty();
		}

		[Test]
		public async Task Partial_purchase_receipts_finish_the_order_and_replay_original_stock_and_workflow_identities()
		{
			var item = M4Item(); var location = Location(); var order = await M4Ordered((item, 5m, 3.125001m));
			var input = M4Receipt(order, location, 2); var first = await _service.ReceivePurchaseOrderAsync(_actor, input);
			var partial = await _service.GetPurchaseOrderAsync(_actor, order.Order.Id);
			partial.Order.Status.Should().Be((int)InventoryPurchaseOrderStatus.PartiallyReceived); partial.Lines.Single().QuantityReceived.Should().Be(2);
			Stock(item, location).Should().Be(2); M4Cost(first).Value<decimal>("UnitCost").Should().Be(3.125001m);
			var receivedEvent = _events.Single(x => x.Trigger == WorkflowTriggerEventType.InventoryPurchaseOrderReceived);
			receivedEvent.AggregateId.Should().Be(order.Order.Id); receivedEvent.AggregateType.Should().Be("InventoryPurchaseOrder"); receivedEvent.CorrelationId.Should().Be(first.OperationId);
			JObject.FromObject(receivedEvent.Payload).Value<string>("ReceiptId").Should().Be(first.OperationId);
			JsonConvert.SerializeObject(receivedEvent.Payload).Should().NotContain(Canary).And.NotContain("UnitCost").And.NotContain("TotalCost");
			await _service.ReceivePurchaseOrderAsync(_actor, M4Receipt(partial, location, 3));
			var complete = await _service.GetPurchaseOrderAsync(_actor, order.Order.Id);
			complete.Order.Status.Should().Be((int)InventoryPurchaseOrderStatus.Received); complete.Lines.Single().QuantityReceived.Should().Be(5);
			complete.Receipts.Should().HaveCount(2).And.OnlyContain(x => x.ReferenceType == (int)InventoryReferenceType.PurchaseOrder && x.ReferenceId == order.Order.Id);
			var count = _events.Count; var revision = complete.Order.Revision;
			var replay = await _service.ReceivePurchaseOrderAsync(_actor, Copy(input));
			replay.OperationId.Should().Be(first.OperationId); replay.TransactionIds.Should().Equal(first.TransactionIds); replay.OutboxIds.Should().Equal(first.OutboxIds);
			_dispatches.Last().Should().Equal(first.OutboxIds); _events.Should().HaveCount(count); Stock(item, location).Should().Be(5);
			_store.All<InventoryPurchaseOrder>().Single().Revision.Should().Be(revision);
			input.Lines[0].Quantity = 1;
			await Fails(() => _service.ReceivePurchaseOrderAsync(_actor, input), "RequestConflict", 409);
		}

		[Test]
		public async Task Repeated_receipt_rows_cannot_collectively_exceed_the_ordered_quantity()
		{
			var item = M4Item(); var location = Location(); var order = await M4Ordered((item, 5m, 3m));
			var input = M4Receipt(order, location, 3); input.Lines.Add(Copy(input.Lines[0]));
			await Fails(() => _service.ReceivePurchaseOrderAsync(_actor, input), "PurchaseOrderOverReceipt", 409);
			Stock(item, location).Should().Be(0); _store.All<InventoryTransaction>().Should().BeEmpty(); _events.Should().BeEmpty();
			_store.All<InventoryPurchaseOrderItem>().Single().QuantityReceived.Should().Be(0);
		}

		[Test]
		public async Task Cancelling_a_partial_order_preserves_receipts_and_rejects_new_receiving()
		{
			var item = M4Item(); var location = Location(); var order = await M4Ordered((item, 5m, 3m));
			await _service.ReceivePurchaseOrderAsync(_actor, M4Receipt(order, location, 2));
			var partial = await _service.GetPurchaseOrderAsync(_actor, order.Order.Id);
			var cancel = new InventoryPurchaseOrderChange { Id = order.Order.Id, Revision = partial.Order.Revision, RequestId = Guid.NewGuid().ToString("D") };
			await _service.ChangePurchaseOrderStatusAsync(_actor, cancel, InventoryPurchaseOrderStatus.Cancelled);
			await _service.ChangePurchaseOrderStatusAsync(_actor, Copy(cancel), InventoryPurchaseOrderStatus.Cancelled);
			var cancelled = await _service.GetPurchaseOrderAsync(_actor, order.Order.Id); var events = _events.Count;
			cancelled.Order.Status.Should().Be((int)InventoryPurchaseOrderStatus.Cancelled); cancelled.Lines.Single().QuantityReceived.Should().Be(2); cancelled.Receipts.Should().ContainSingle();
			await Fails(() => _service.ReceivePurchaseOrderAsync(_actor, M4Receipt(cancelled, location, 1)), "PurchaseOrderStateConflict", 409);
			Stock(item, location).Should().Be(2); _events.Should().HaveCount(events); _store.All<InventoryTransaction>().Should().ContainSingle();
		}

		[Test]
		public async Task Receipt_source_line_and_request_ownership_cannot_be_swapped()
		{
			var item = M4Item(); var location = Location(); var first = await M4Ordered((item, 2m, 3m)); var second = await M4Ordered((item, 2m, 3m));
			var wrong = M4Receipt(first, location, 1); wrong.Lines[0].PurchaseOrderItemId = second.Lines.Single().Id;
			await Fails(() => _service.ReceivePurchaseOrderAsync(_actor, wrong), "PurchaseOrderLineUnavailable", 404);
			var input = M4Receipt(first, location, 1); await _service.ReceivePurchaseOrderAsync(_actor, input);
			await Fails(() => _service.ReceivePurchaseOrderAsync(new InventoryActor { DepartmentId = Department, UserId = "other-manager", GrantToken = "other-grant" }, Copy(input)), "RequestConflict", 409);
			Stock(item, location).Should().Be(1); _store.All<InventoryTransaction>().Should().ContainSingle();
		}

		[Test]
		public async Task Purchase_receipt_cannot_add_stock_to_an_unauthorized_destination()
		{
			var item = M4Item(); var hidden = Location(); var order = await M4Ordered((item, 2m, 3m));
			_deniedLocations.Add(hidden.Id);
			await Fails(() => _service.ReceivePurchaseOrderAsync(_actor, M4Receipt(order, hidden, 1)), "LocationUnavailable", 404);
			_store.All<InventoryStock>().Should().BeEmpty(); _store.All<InventoryTransaction>().Should().BeEmpty();
			_store.All<InventoryPurchaseOrderItem>().Single().QuantityReceived.Should().Be(0); _events.Should().BeEmpty();
		}

		[Test]
		public async Task Generic_inventory_posting_cannot_manufacture_a_purchase_order_receipt()
		{
			var item = M4Item(); var location = Location(); var order = await M4Ordered((item, 2m, 3m));
			var command = M4Receive(item, location, 1, 3); command.Lines[0].ReferenceType = InventoryReferenceType.PurchaseOrder; command.Lines[0].ReferenceId = order.Order.Id;
			command.Lines[0].PurchaseOrderItemId = order.Lines.Single().Id;
			var error = (await ((Func<Task>)(() => _service.PostTransactionAsync(_actor, command))).Should().ThrowAsync<InventoryException>()).Which;
			error.StatusCode.Should().Be(400); Stock(item, location).Should().Be(0); _store.All<InventoryTransaction>().Should().BeEmpty(); _events.Should().BeEmpty();
			_store.All<InventoryPurchaseOrderItem>().Single().QuantityReceived.Should().Be(0);
		}

		[Test]
		public async Task Controlled_receipt_holds_the_whole_mixed_batch_until_an_independent_witness_and_retries_once()
		{
			var ordinary = M4Item(); var controlled = M4Item(controlled: true); var location = Location();
			var order = await M4Ordered((ordinary, 2m, 3m), (controlled, 1m, 9m));
			var input = M4Receipt(order, location, 2); input.Lines.Add(new InventoryPurchaseReceiptLine { PurchaseOrderItemId = order.Lines[1].Id, LocationId = location.Id, Quantity = 1 });
			var pending = await _service.ReceivePurchaseOrderAsync(_actor, input);
			pending.AwaitingWitness.Should().BeTrue(); pending.TransactionIds.Should().BeEmpty(); _events.Should().BeEmpty(); _store.All<InventoryStock>().Should().BeEmpty();
			_store.All<InventoryPurchaseOrderItem>().Should().OnlyContain(x => x.QuantityReceived == 0);
			(await _service.ReceivePurchaseOrderAsync(_actor, Copy(input))).OperationId.Should().Be(pending.OperationId);
			await Fails(() => _service.WitnessAsync(_actor, input.RequestId, "Synthetic witness"), "IndependentWitnessRequired", 409);
			var receipt = await Witness(input.RequestId); receipt.AwaitingWitness.Should().BeFalse(); receipt.TransactionIds.Should().HaveCount(2);
			Stock(ordinary, location).Should().Be(2); Stock(controlled, location).Should().Be(1);
			_store.All<InventoryPurchaseOrder>().Single().Status.Should().Be((int)InventoryPurchaseOrderStatus.Received);
			_store.All<InventoryTransaction>().Should().OnlyContain(x => x.CreatedBy == _actor.UserId);
			foreach (var transaction in _store.All<InventoryTransaction>()) JObject.Parse(transaction.Content).Value<string>("WitnessUserId").Should().Be("witness");
			var count = _events.Count;
			(await Witness(input.RequestId)).TransactionIds.Should().Equal(receipt.TransactionIds);
			_dispatches.Last().Should().Equal(receipt.OutboxIds);
			(await _service.ReceivePurchaseOrderAsync(_actor, Copy(input))).TransactionIds.Should().Equal(receipt.TransactionIds);
			_events.Should().HaveCount(count); _events.Count(x => x.Trigger == WorkflowTriggerEventType.InventoryPurchaseOrderReceived).Should().Be(1);
			_auth.Setup(x => x.RequireAsync(It.Is<InventoryActor>(a => a.UserId == "witness"), It.IsAny<bool>(), PermissionTypes.ContactView, It.IsAny<int?>()))
				.ThrowsAsync(new InventoryException(403, "PermissionRequired"));
			await Fails(() => Witness(input.RequestId), "PermissionRequired", 403);
		}

		[Test]
		public async Task Competing_pending_controlled_receipts_cannot_overreceive_after_the_first_witness_commits()
		{
			var item = M4Item(controlled: true); var location = Location(); var order = await M4Ordered((item, 5m, 3m));
			var first = M4Receipt(order, location, 3); var second = M4Receipt(order, location, 3);
			await _service.ReceivePurchaseOrderAsync(_actor, first); await _service.ReceivePurchaseOrderAsync(_actor, second);
			await Witness(first.RequestId); var count = _events.Count;
			await Fails(() => Witness(second.RequestId), "PurchaseOrderStateConflict", 409);
			Stock(item, location).Should().Be(3); _store.All<InventoryPurchaseOrderItem>().Single().QuantityReceived.Should().Be(3); _events.Should().HaveCount(count);
			_store.All<InventoryOperation>().Single(x => x.RequestId == second.RequestId).State.Should().Be(1);
		}

		[Test]
		public async Task A_pending_purchase_receipt_rechecks_its_original_performers_purchasing_permission()
		{
			var item = M4Item(controlled: true); var location = Location(); var order = await M4Ordered((item, 1m, 3m)); var input = M4Receipt(order, location, 1);
			await _service.ReceivePurchaseOrderAsync(_actor, input);
			_auth.Setup(x => x.RequireAsync(It.Is<InventoryActor>(a => a.UserId == _actor.UserId), It.IsAny<bool>(), PermissionTypes.ContactView, It.IsAny<int?>()))
				.ThrowsAsync(new InventoryException(403, "PermissionRequired"));
			await Fails(() => Witness(input.RequestId), "PermissionRequired", 403);
			Stock(item, location).Should().Be(0); _store.All<InventoryTransaction>().Should().BeEmpty(); _events.Should().BeEmpty();
			_store.All<InventoryOperation>().Single(x => x.RequestId == input.RequestId).State.Should().Be(1);
		}

		[Test]
		public async Task Serialized_purchase_receipts_create_one_costed_asset_per_row_and_replay_without_new_assets()
		{
			var item = M4Item(InventoryTrackingMode.Serialized); var location = Location(); var order = await M4Ordered((item, 2m, 12.5m));
			var input = M4Receipt(order, location, 1); input.Lines[0].Asset = new() { SerialNumber = "purchase-serial-one" };
			input.Lines.Add(new InventoryPurchaseReceiptLine { PurchaseOrderItemId = order.Lines.Single().Id, LocationId = location.Id, Quantity = 1, Asset = new() { SerialNumber = "purchase-serial-two" } });
			var result = await _service.ReceivePurchaseOrderAsync(_actor, input);
			var replay = await _service.ReceivePurchaseOrderAsync(_actor, Copy(input)); replay.TransactionIds.Should().Equal(result.TransactionIds);
			_store.All<InventoryAsset>().Should().HaveCount(2).And.OnlyContain(x => x.CurrentLocationId == location.Id && x.Status == 0);
			_store.All<InventoryAsset>().Select(x => JsonConvert.DeserializeObject<InventoryAssetContent>(x.Content).AcquisitionCost).Should().OnlyContain(x => x == 12.5m);
			_store.All<InventoryStock>().Should().BeEmpty(); M4Average(item).Should().Be(12.5m);
			(await _service.GetValuationAsync(_actor)).Totals.Should().ContainSingle().Which.KnownValue.Should().Be(25);
		}

		[Test]
		public async Task Purchase_receipt_rejects_an_incompatible_lot_cost_without_creating_stock()
		{
			var item = M4Item(); var location = Location(); var order = await M4Ordered((item, 2m, 3m));
			var lot = await _service.SaveLotAsync(_actor, new InventoryLot { ItemId = item.Id }, new InventoryLotContent { LotNumber = "Synthetic wrong-price lot", UnitCost = 4 });
			var input = M4Receipt(order, location, 1); input.Lines[0].LotId = lot.Id;
			await Fails(() => _service.ReceivePurchaseOrderAsync(_actor, input), "LotCostMismatch", 409);
			_store.All<InventoryStock>().Should().BeEmpty(); _store.All<InventoryTransaction>().Should().BeEmpty(); _events.Should().BeEmpty();
		}

		[Test]
		public async Task Late_protected_order_write_failure_rolls_back_stock_average_lines_ledger_audit_and_outbox()
		{
			var item = M4Item(); var location = Location(); var order = await M4Ordered((item, 2m, 8m));
			var beforeItem = JsonConvert.SerializeObject(_store.All<InventoryItem>().Single()); var beforeOrder = JsonConvert.SerializeObject(order.Order);
			var operations = _store.All<InventoryOperation>().Count(); var audits = _audits.Count; var dispatches = _dispatches.Count;
			_write.Setup(x => x.PrepareRecordsEntityWriteAsync(Department, It.Is<InventoryPurchaseOrder>(p => p.Status == 2 || p.Status == 3), It.IsAny<InventoryPurchaseOrder>(), It.IsAny<string>(),
				It.IsAny<IReadOnlyDictionary<string, (Func<InventoryPurchaseOrder, string> Get, Action<InventoryPurchaseOrder, string> Set)>>(), It.IsAny<Action>(),
				It.IsAny<string>(), It.IsAny<string>(), false, It.IsAny<CancellationToken>())).ReturnsAsync(ProtectedWriteResult.Blocked("synthetic protection unavailable"));
			await Fails(() => _service.ReceivePurchaseOrderAsync(_actor, M4Receipt(order, location, 2)), "ProtectedDataRequired", 403);
			_store.All<InventoryStock>().Should().BeEmpty(); _store.All<InventoryTransaction>().Should().BeEmpty(); _store.All<InventoryOperation>().Should().HaveCount(operations);
			JsonConvert.SerializeObject(_store.All<InventoryItem>().Single()).Should().Be(beforeItem); JsonConvert.SerializeObject(_store.All<InventoryPurchaseOrder>().Single()).Should().Be(beforeOrder);
			_store.All<InventoryPurchaseOrderItem>().Single().QuantityReceived.Should().Be(0); _audits.Should().HaveCount(audits); _dispatches.Should().HaveCount(dispatches); _events.Should().BeEmpty();
		}

		[Test]
		public async Task Valuation_partitions_currency_keeps_unknown_rows_and_excludes_terminal_assets_and_hidden_holders()
		{
			var usd = M4Item(defaultCost: 2); var eur = M4Item(defaultCost: 3, currency: "EUR"); var unknown = M4Item(defaultCost: null);
			var equipment = M4Item(InventoryTrackingMode.Serialized); var visible = Location(); var hidden = Location();
			SeedStock(usd, visible, 2); SeedStock(eur, visible, 3); SeedStock(unknown, visible, 5); SeedStock(usd, hidden, 10000);
			var asset = _store.Seed(new InventoryAsset { DepartmentId = Department, ItemId = equipment.Id, CurrentLocationId = visible.Id, Status = 0,
				Content = JsonConvert.SerializeObject(new InventoryAssetContent { SerialNumber = "visible", AcquisitionCost = 12 }) });
			_store.Seed(new InventoryAsset { DepartmentId = Department, ItemId = equipment.Id, CurrentLocationId = visible.Id, Status = (int)InventoryAssetStatus.Retired,
				Content = JsonConvert.SerializeObject(new InventoryAssetContent { SerialNumber = "retired", AcquisitionCost = 999 }) });
			_deniedLocations.Add(hidden.Id);
			var result = await _service.GetValuationAsync(_actor);
			result.Lines.Should().HaveCount(4).And.OnlyContain(x => x.LocationId == visible.Id);
			result.Lines.Where(x => x.AssetId != null).Should().ContainSingle().Which.AssetId.Should().Be(asset.Id);
			result.Totals.Single(x => x.CurrencyCode == "USD").KnownValue.Should().Be(16); result.Totals.Single(x => x.CurrencyCode == "USD").UncostedRows.Should().Be(1);
			result.Totals.Single(x => x.CurrencyCode == "EUR").KnownValue.Should().Be(9); result.AsOfUtc.Should().Be(_clock.Utc);
			await Fails(() => _service.GetValuationAsync(_actor, hidden.Id), "LocationUnavailable", 404);
		}

		private InventoryItem M4Item(InventoryTrackingMode tracking = InventoryTrackingMode.Bulk, bool controlled = false, decimal? defaultCost = 2, decimal? average = null, string currency = "USD")
		{
			var item = Item(tracking, controlled); var details = JsonConvert.DeserializeObject<InventoryItemContent>(item.Content);
			details.DefaultUnitCost = defaultCost; details.AverageUnitCost = average; details.CurrencyCode = currency; item.Content = JsonConvert.SerializeObject(details); return _store.Seed(item);
		}
		private decimal? M4Average(InventoryItem item) => JsonConvert.DeserializeObject<InventoryItemContent>(_store.All<InventoryItem>().Single(x => x.Id == item.Id).Content).AverageUnitCost;
		private JObject M4Cost(InventoryResult result) => JObject.Parse(_store.All<InventoryTransaction>().Single(x => x.Id == result.TransactionIds.Single()).Content);
		private static InventoryCommand M4Receive(InventoryItem item, InventoryLocation location, decimal quantity, decimal unitCost)
		{
			var command = Receive(item, location, quantity); command.Lines[0].UnitCost = unitCost; return command;
		}
		private Contact M4CompanyContact(string id = null)
		{
			var contact = new Contact { ContactId = id ?? Guid.NewGuid().ToString("D"), DepartmentId = Department, ContactType = 1, CompanyName = Canary };
			_companyContacts[contact.ContactId] = contact; return contact;
		}
		private async Task<(InventoryPurchaseOrderInput Input, InventoryPurchaseOrderDetail Detail)> M4Draft(params (InventoryItem Item, decimal Quantity, decimal UnitCost)[] lines)
		{
			var contact = M4CompanyContact(); var vendor = await _service.SaveVendorAsync(_actor, new InventoryVendorInput { ContactId = contact.ContactId, Details = new() { Note = Canary } });
			var input = new InventoryPurchaseOrderInput { Id = Guid.NewGuid().ToString("D"), Number = "PO-" + Guid.NewGuid().ToString("N"), VendorId = vendor.Id, CurrencyCode = "USD", Note = Canary,
				Lines = lines.Select(x => new InventoryPurchaseOrderLineInput { ItemId = x.Item.Id, QuantityOrdered = x.Quantity, UnitCost = x.UnitCost, Note = Canary }).ToList() };
			return (input, await _service.SavePurchaseOrderAsync(_actor, input));
		}
		private async Task<InventoryPurchaseOrderDetail> M4Ordered(params (InventoryItem Item, decimal Quantity, decimal UnitCost)[] lines)
		{
			var draft = await M4Draft(lines);
			await _service.ChangePurchaseOrderStatusAsync(_actor, new InventoryPurchaseOrderChange { Id = draft.Detail.Order.Id, Revision = draft.Detail.Order.Revision, RequestId = Guid.NewGuid().ToString("D") }, InventoryPurchaseOrderStatus.Ordered);
			return await _service.GetPurchaseOrderAsync(_actor, draft.Detail.Order.Id);
		}
		private static InventoryPurchaseReceiptInput M4Receipt(InventoryPurchaseOrderDetail order, InventoryLocation location, decimal quantity) => new() {
			PurchaseOrderId = order.Order.Id, Revision = order.Order.Revision, RequestId = Guid.NewGuid().ToString("D"),
			Lines = new() { new InventoryPurchaseReceiptLine { PurchaseOrderItemId = order.Lines[0].Id, LocationId = location.Id, Quantity = quantity } } };
	}
}
