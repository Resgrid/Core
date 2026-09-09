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
using Resgrid.Model.Services;

namespace Resgrid.Tests.Services
{
	public sealed partial class InventoryModernizationTests
	{
		[TestCase(6, 4)]
		[TestCase(4, 6)]
		[TestCase(-2, 0)]
		public async Task Counts_post_signed_bulk_variance_with_matching_snapshot_and_ledger_backlinks(int opening, int observed)
		{
			var item = Item(); var location = Location(); SeedStock(item, location, opening);
			var detail = await M5ObservedCount(location, observed);
			detail.Lines.Should().ContainSingle().Which.ExpectedQuantity.Should().Be(opening);
			var input = M5Completion(detail); var result = await _service.CompleteCountAsync(_actor, input);
			Stock(item, location).Should().Be(observed);
			var ledger = _store.All<InventoryTransaction>().Should().ContainSingle().Which;
			ledger.Quantity.Should().Be(Math.Abs(observed - opening)); ledger.ItemId.Should().Be(item.Id);
			ledger.TransactionType.Should().Be((int)InventoryTransactionType.Count);
			ledger.ReferenceType.Should().Be((int)InventoryReferenceType.Count); ledger.ReferenceId.Should().Be(detail.Count.Id);
			ledger.CountItemId.Should().Be(detail.Lines.Single().Id); ledger.OperationId.Should().Be(result.OperationId);
			ledger.FromLocationId.Should().Be(observed < opening ? location.Id : null);
			ledger.ToLocationId.Should().Be(observed > opening ? location.Id : null);
			var saved = await _service.GetCountAsync(_actor, detail.Count.Id);
			saved.Count.Status.Should().Be((int)InventoryCountStatus.Completed); saved.Count.CompletedOn.Should().Be(_clock.Utc);
			saved.Lines.Single().TransactionId.Should().Be(ledger.Id); saved.Lines.Single().CountedQuantity.Should().Be(observed);
			var summary = _events.Should().ContainSingle(e => e.Trigger == WorkflowTriggerEventType.InventoryCountCompleted).Which;
			summary.AggregateId.Should().Be(detail.Count.Id); summary.CorrelationId.Should().Be(result.OperationId);
			var payload = JObject.FromObject(summary.Payload); payload.Value<int>("VarianceLineCount").Should().Be(1);
			payload.Value<string>("VarianceValue").Should().Be(ProtectedDataEnvelope.RedactionValue);
			JsonConvert.SerializeObject(_events).Should().NotContain(Canary).And.NotContain("UnitCost");
			_dispatches.Last().Should().Equal(result.OutboxIds);
		}

		[Test]
		public async Task Count_without_variance_completes_once_and_replays_its_summary_without_a_stock_movement()
		{
			var item = Item(); var location = Location(); SeedStock(item, location, 1.123456m);
			var detail = await M5ObservedCount(location, 1.123456m); var input = M5Completion(detail);
			var result = await _service.CompleteCountAsync(_actor, input);
			result.TransactionIds.Should().BeEmpty(); _store.All<InventoryTransaction>().Should().BeEmpty(); Stock(item, location).Should().Be(1.123456m);
			var summary = _events.Should().ContainSingle().Which; summary.Trigger.Should().Be(WorkflowTriggerEventType.InventoryCountCompleted);
			JObject.FromObject(summary.Payload).Value<int>("VarianceLineCount").Should().Be(0);
			(await _service.GetCountAsync(_actor, detail.Count.Id)).Count.Status.Should().Be((int)InventoryCountStatus.Completed);
			var replay = await _service.CompleteCountAsync(_actor, input);
			replay.OperationId.Should().Be(result.OperationId); replay.OutboxIds.Should().Equal(result.OutboxIds);
			_events.Should().ContainSingle(); _store.All<InventoryOperation>().Should().ContainSingle();
			_dispatches.Last().Should().Equal(result.OutboxIds);
			await Fails(() => _service.CompleteCountAsync(_actor, new InventoryCountComplete { CountId = input.CountId, RequestId = input.RequestId, Revision = input.Revision + 1 }), "RequestConflict", 409);
			_deniedLocations.Add(location.Id);
			await Fails(() => _service.CompleteCountAsync(_actor, input), "LocationUnavailable", 404);
		}

		[Test]
		public async Task Count_observations_require_current_revision_distinct_owned_lines_and_nonnegative_quantities()
		{
			var item = Item(); var location = Location(); SeedStock(item, location, 5);
			var start = M5CountInput(location); var detail = await _service.StartCountAsync(_actor, start);
			await Fails(() => _service.StartCountAsync(_actor, start), "CountAlreadyExists", 409);
			await Fails(() => _service.GetCountAsync(new InventoryActor { DepartmentId = 88, UserId = _actor.UserId }, detail.Count.Id), "Unavailable", 404);
			var update = M5Observations(detail, 4);
			update.Lines.Add(Copy(update.Lines[0])); await Fails(() => _service.SaveCountAsync(_actor, update), "InvalidCount", 400);
			update.Lines.RemoveAt(1); update.Lines[0].Id = Guid.NewGuid().ToString("D");
			await Fails(() => _service.SaveCountAsync(_actor, update), "Unavailable", 404);
			update = M5Observations(detail, -0.000001m); await Fails(() => _service.SaveCountAsync(_actor, update), "InvalidQuantity", 400);
			update = M5Observations(detail, 0.0000001m); await Fails(() => _service.SaveCountAsync(_actor, update), "InvalidQuantity", 400);
			await Fails(() => _service.CompleteCountAsync(_actor, M5Completion(detail)), "CountStateConflict", 409);
			update = M5Observations(detail, 4); var saved = await _service.SaveCountAsync(_actor, update);
			await Fails(() => _service.SaveCountAsync(_actor, update), "CountStateConflict", 409);
			saved.Count.Revision.Should().Be(detail.Count.Revision + 1); saved.Lines.Single().CountedQuantity.Should().Be(4);
			_store.All<InventoryCount>().Should().ContainSingle(); _store.All<InventoryTransaction>().Should().BeEmpty();
		}

		[TestCase("quantity")]
		[TestCase("stock-revision")]
		[TestCase("new-position")]
		[TestCase("item-revision")]
		[TestCase("location-revision")]
		public async Task Counts_reject_changed_department_snapshots_without_overwriting_newer_stock(string change)
		{
			var item = Item(); var location = Location(); SeedStock(item, location, 5);
			var detail = await M5ObservedCount(location, 4);
			switch (change)
			{
				case "quantity": SeedStock(item, location, 6); break;
				case "stock-revision": var stock = _store.All<InventoryStock>().Single(); stock.Revision++; _store.Seed(stock); break;
				case "new-position": SeedStock(item, Location(), 1); break;
				case "item-revision": item.Revision++; _store.Seed(item); break;
				case "location-revision": location.Revision++; _store.Seed(location); break;
			}
			var before = JsonConvert.SerializeObject(_store.All<InventoryStock>());
			await Fails(() => _service.CompleteCountAsync(_actor, M5Completion(detail)), "CountSnapshotChanged", 409);
			JsonConvert.SerializeObject(_store.All<InventoryStock>()).Should().Be(before);
			_store.All<InventoryTransaction>().Should().BeEmpty(); _store.All<InventoryOperation>().Should().BeEmpty(); _events.Should().BeEmpty();
			(await _service.GetCountAsync(_actor, detail.Count.Id)).Count.Status.Should().Be((int)InventoryCountStatus.Draft);
		}

		[Test]
		public async Task Controlled_count_variance_waits_for_independent_witness_and_replays_only_the_completed_receipt()
		{
			var item = Item(controlled: true); var location = Location(); SeedStock(item, location, 5);
			var detail = await M5ObservedCount(location, 4); var input = M5Completion(detail);
			var pending = await _service.CompleteCountAsync(_actor, input);
			pending.AwaitingWitness.Should().BeTrue(); Stock(item, location).Should().Be(5);
			_store.All<InventoryTransaction>().Should().BeEmpty(); _events.Should().BeEmpty();
			(await _service.GetCountAsync(_actor, detail.Count.Id)).Count.Status.Should().Be((int)InventoryCountStatus.AwaitingWitness);
			await Fails(() => _service.WitnessAsync(_actor, input.RequestId, "Synthetic count verified"), "IndependentWitnessRequired", 409);
			var completed = await Witness(input.RequestId); completed.AwaitingWitness.Should().BeFalse(); Stock(item, location).Should().Be(4);
			var ledger = _store.All<InventoryTransaction>().Should().ContainSingle().Which;
			ledger.CountItemId.Should().Be(detail.Lines.Single().Id); ledger.CreatedBy.Should().Be(_actor.UserId);
			var content = JObject.Parse(ledger.Content); content.Value<string>("PerformerId").Should().Be(_actor.UserId);
			content.Value<string>("WitnessUserId").Should().Be("witness");
			_events.Should().ContainSingle(e => e.Trigger == WorkflowTriggerEventType.ControlledSubstanceRecorded);
			_events.Should().ContainSingle(e => e.Trigger == WorkflowTriggerEventType.InventoryCountCompleted);
			var eventCount = _events.Count; (await Witness(input.RequestId)).TransactionIds.Should().Equal(completed.TransactionIds);
			(await _service.CompleteCountAsync(_actor, input)).TransactionIds.Should().Equal(completed.TransactionIds);
			_events.Should().HaveCount(eventCount); _store.All<InventoryTransaction>().Should().ContainSingle();
		}

		[Test]
		public async Task Controlled_count_witness_rechecks_snapshot_and_original_performer_access_before_posting()
		{
			var item = Item(controlled: true); var location = Location(); SeedStock(item, location, 5);
			var detail = await M5ObservedCount(location, 4); var input = M5Completion(detail);
			await _service.CompleteCountAsync(_actor, input);
			_auth.Setup(x => x.RequireAsync(It.Is<InventoryActor>(a => a.UserId == _actor.UserId && a.GrantToken == null), true, PermissionTypes.ManageControlledSubstances, null))
				.ThrowsAsync(new InventoryException(403, "PermissionRequired"));
			await Fails(() => Witness(input.RequestId), "PermissionRequired", 403);
			_auth.Setup(x => x.RequireAsync(It.Is<InventoryActor>(a => a.UserId == _actor.UserId && a.GrantToken == null), true, PermissionTypes.ManageControlledSubstances, null)).Returns(Task.CompletedTask);
			SeedStock(item, location, 6);
			await Fails(() => Witness(input.RequestId), "CountSnapshotChanged", 409);
			Stock(item, location).Should().Be(6); _store.All<InventoryTransaction>().Should().BeEmpty(); _events.Should().BeEmpty();
			_store.All<InventoryOperation>().Should().ContainSingle().Which.State.Should().Be(1);
			(await _service.GetCountAsync(_actor, detail.Count.Id)).Count.Status.Should().Be((int)InventoryCountStatus.AwaitingWitness);
		}

		[TestCase(false)]
		[TestCase(true)]
		public async Task Cancelled_counts_cannot_post_or_complete_a_pending_witness_request(bool controlled)
		{
			var item = Item(controlled: controlled); var location = Location(); SeedStock(item, location, 5);
			var detail = await M5ObservedCount(location, 4); var input = M5Completion(detail);
			if (controlled) { await _service.CompleteCountAsync(_actor, input); detail = await _service.GetCountAsync(_actor, detail.Count.Id); }
			await _service.CancelCountAsync(_actor, detail.Count.Id, detail.Count.Revision);
			if (controlled) await Fails(() => Witness(input.RequestId), "CountStateConflict", 409);
			else await Fails(() => _service.CompleteCountAsync(_actor, input), "CountStateConflict", 409);
			Stock(item, location).Should().Be(5); _store.All<InventoryTransaction>().Should().BeEmpty(); _events.Should().BeEmpty();
			(await _service.GetCountAsync(_actor, detail.Count.Id)).Count.Status.Should().Be((int)InventoryCountStatus.Cancelled);
		}

		[Test]
		public async Task Assigned_serialized_assets_cannot_be_closed_by_a_count_variance()
		{
			var item = Item(InventoryTrackingMode.Serialized); var location = Location(); var asset = await CreateAsset(item, location);
			await _service.IssueAsync(_actor, Issue(item, location, 1, unitId: 101, assetId: asset.Id));
			var issuance = _store.All<InventoryIssuance>().Single(); var holder = _store.All<InventoryLocation>().Single(l => l.Id == issuance.LocationId);
			var detail = await M5ObservedCount(holder, 0); var before = _store.All<InventoryTransaction>().Count();
			await Fails(() => _service.CompleteCountAsync(_actor, M5Completion(detail)), "ReturnAssetFirst", 409);
			_store.All<InventoryTransaction>().Should().HaveCount(before);
			_store.All<InventoryIssuance>().Single().Status.Should().Be((int)InventoryIssuanceStatus.Outstanding);
			_store.All<InventoryAsset>().Single().Status.Should().Be((int)InventoryAssetStatus.Issued);
			await Fails(() => _service.SaveCountAsync(_actor, M5Observations(detail, 2)), "SerializedQuantity", 400);
		}

		[TestCase(false)]
		[TestCase(true)]
		public async Task Finished_count_evidence_and_completed_replays_survive_container_loss_with_current_holder_authorization(bool completed)
		{
			var bagItem = Item(InventoryTrackingMode.Serialized, kit: true); var location = Location(); var bag = await CreateAsset(bagItem, location);
			var container = _store.All<InventoryLocation>().Single(l => l.ContainerAssetId == bag.Id); var item = Item();
			await _service.PostTransactionAsync(_actor, Receive(item, container, 5));
			var detail = await M5ObservedCount(container, 4); var input = M5Completion(detail); InventoryResult receipt = null;
			if (completed) receipt = await _service.CompleteCountAsync(_actor, input);
			else await _service.CancelCountAsync(_actor, detail.Count.Id, detail.Count.Revision);
			await _service.ChangeAssetStatusAsync(_actor, Status(bagItem, bag, location, InventoryAssetStatus.Lost));
			var events = _events.Count; var saved = await _service.GetCountAsync(_actor, detail.Count.Id);
			saved.Count.Status.Should().Be(completed ? (int)InventoryCountStatus.Completed : (int)InventoryCountStatus.Cancelled);
			saved.Lines.Should().ContainSingle().Which.LocationId.Should().Be(container.Id);
			if (completed)
			{
				var replay = await _service.CompleteCountAsync(_actor, input); replay.TransactionIds.Should().Equal(receipt.TransactionIds);
				replay.OutboxIds.Should().Equal(receipt.OutboxIds); _events.Should().HaveCount(events);
			}
			_deniedLocations.Add(location.Id);
			await Fails(() => _service.GetCountAsync(_actor, detail.Count.Id), "LocationUnavailable", 404);
			if (completed) await Fails(() => _service.CompleteCountAsync(_actor, input), "LocationUnavailable", 404);
		}

		[Test]
		public async Task Department_counts_skip_retained_stock_and_assets_inside_terminal_containers()
		{
			var location = Location(); var bagItem = Item(InventoryTrackingMode.Serialized, kit: true); var bag = await CreateAsset(bagItem, location);
			var container = _store.All<InventoryLocation>().Single(l => l.ContainerAssetId == bag.Id);
			var hiddenSupplies = Item(); var available = Item(); var hiddenEquipment = Item(InventoryTrackingMode.Serialized);
			await _service.PostTransactionAsync(_actor, Receive(hiddenSupplies, container, 2));
			await _service.PostTransactionAsync(_actor, Receive(available, location, 3));
			var containedAsset = await CreateAsset(hiddenEquipment, container, "contained-equipment");
			await _service.ChangeAssetStatusAsync(_actor, Status(bagItem, bag, location, InventoryAssetStatus.Retired));
			var detail = await _service.StartCountAsync(_actor, new InventoryCountInput { Id = Guid.NewGuid().ToString("D"), Name = "Synthetic department count" });
			detail.Count.LocationId.Should().BeNull(); detail.Lines.Should().ContainSingle().Which.ItemId.Should().Be(available.Id);
			detail.Lines.Single().ExpectedQuantity.Should().Be(3);
			Stock(hiddenSupplies, container).Should().Be(2, "terminal-holder evidence remains stored");
			_store.All<InventoryAsset>().Single(a => a.Id == containedAsset.Id).Status.Should().Be((int)InventoryAssetStatus.InService);
		}

		[TestCase(false)]
		[TestCase(true)]
		public async Task A_stale_draft_or_pending_count_can_be_cancelled_after_its_container_is_lost(bool awaitingWitness)
		{
			var location = Location(); var bagItem = Item(InventoryTrackingMode.Serialized, kit: true); var bag = await CreateAsset(bagItem, location);
			var container = _store.All<InventoryLocation>().Single(l => l.ContainerAssetId == bag.Id); var item = Item(controlled: awaitingWitness);
			SeedStock(item, container, 5); var detail = await M5ObservedCount(container, 4); var input = M5Completion(detail);
			if (awaitingWitness) (await _service.CompleteCountAsync(_actor, input)).AwaitingWitness.Should().BeTrue();
			await _service.ChangeAssetStatusAsync(_actor, Status(bagItem, bag, location, InventoryAssetStatus.Lost));
			var current = await _service.GetCountAsync(_actor, detail.Count.Id);
			if (!awaitingWitness) await Fails(() => _service.CompleteCountAsync(_actor, input), "CountSnapshotChanged", 409);
			await _service.CancelCountAsync(_actor, current.Count.Id, current.Count.Revision);
			(await _service.GetCountAsync(_actor, detail.Count.Id)).Count.Status.Should().Be((int)InventoryCountStatus.Cancelled);
			if (awaitingWitness) await Fails(() => Witness(input.RequestId), "CountStateConflict", 409);
			Stock(item, container).Should().Be(5); _store.All<InventoryTransaction>().Should().OnlyContain(t => t.CountItemId == null);
		}

		[Test]
		public async Task Losing_an_outer_container_refreshes_low_stock_for_nested_bulk_and_serialized_contents()
		{
			var location = Location(); var outerItem = Item(InventoryTrackingMode.Serialized, kit: true); var outer = await CreateAsset(outerItem, location, "outer-container");
			var outerLocation = _store.All<InventoryLocation>().Single(l => l.ContainerAssetId == outer.Id);
			var innerItem = Item(InventoryTrackingMode.Serialized, kit: true); var inner = await CreateAsset(innerItem, outerLocation, "inner-container");
			var innerLocation = _store.All<InventoryLocation>().Single(l => l.ContainerAssetId == inner.Id);
			var supplies = M5ThresholdItem(3); var equipment = M5ThresholdItem(0, InventoryTrackingMode.Serialized);
			await _service.PostTransactionAsync(_actor, Receive(supplies, innerLocation, 5));
			var asset = await CreateAsset(equipment, innerLocation, "nested-equipment");
			_store.All<InventoryAlert>().Should().BeEmpty();
			await _service.ChangeAssetStatusAsync(_actor, Status(outerItem, outer, location, InventoryAssetStatus.Lost));
			var alerts = _store.All<InventoryAlert>().ToList(); alerts.Should().HaveCount(2).And.OnlyContain(a => a.Status == 0 && a.AlertType == (int)InventoryAlertType.LowStock && a.Quantity == 0);
			alerts.Select(a => a.ItemId).Should().BeEquivalentTo(new[] { supplies.Id, equipment.Id });
			_events.Count(e => e.Trigger == WorkflowTriggerEventType.InventoryLowStock).Should().Be(2);
			Stock(supplies, innerLocation).Should().Be(5); _store.All<InventoryAsset>().Single(a => a.Id == asset.Id).CurrentLocationId.Should().Be(innerLocation.Id);
		}

		[Test]
		public async Task Protected_count_summary_failure_rolls_back_ledger_stock_backlinks_and_outbox()
		{
			var item = Item(); var location = Location(); SeedStock(item, location, 5);
			var detail = await M5ObservedCount(location, 4); var audits = _audits.Count;
			_write.Setup(x => x.PrepareRecordsEntityWriteAsync(Department, It.Is<InventoryCount>(c => c.Status == 2), It.IsAny<InventoryCount>(), It.IsAny<string>(),
				It.IsAny<IReadOnlyDictionary<string, (Func<InventoryCount, string> Get, Action<InventoryCount, string> Set)>>(), It.IsAny<Action>(),
				It.IsAny<string>(), It.IsAny<string>(), false, It.IsAny<CancellationToken>())).ReturnsAsync(ProtectedWriteResult.Blocked("synthetic_broker_unavailable"));
			await Fails(() => _service.CompleteCountAsync(_actor, M5Completion(detail)), "ProtectedDataRequired", 403);
			Stock(item, location).Should().Be(5); _store.All<InventoryTransaction>().Should().BeEmpty(); _store.All<InventoryOperation>().Should().BeEmpty();
			_store.All<InventoryCountItem>().Single().TransactionId.Should().BeNull(); _events.Should().BeEmpty(); _audits.Should().HaveCount(audits);
			var saved = await _service.GetCountAsync(_actor, detail.Count.Id); saved.Count.Revision.Should().Be(detail.Count.Revision); saved.Count.Status.Should().Be(0);
		}

		[Test]
		public async Task Count_ledger_entries_cannot_be_forged_or_reversed_through_generic_posting()
		{
			var item = Item(); var location = Location(); SeedStock(item, location, 5);
			var detail = await M5ObservedCount(location, 4);
			var forged = Move(item, location, null, 1, InventoryTransactionType.Count); forged.CountItemId = detail.Lines.Single().Id;
			await Fails(() => _service.PostTransactionAsync(_actor, Command(forged)), "CountCompletionRequired", 400);
			var result = await _service.CompleteCountAsync(_actor, M5Completion(detail));
			var reversal = Move(item, null, location, 1, InventoryTransactionType.Adjust); reversal.ReversesTransactionId = result.TransactionIds.Single();
			await Fails(() => _service.PostTransactionAsync(_actor, Command(reversal)), "CountReceiptImmutable", 409);
			Stock(item, location).Should().Be(4); _store.All<InventoryTransaction>().Should().ContainSingle();
		}

		[Test]
		public async Task Low_stock_alerts_deduplicate_update_resolve_and_reopen_with_new_history()
		{
			var item = M5ThresholdItem(3); var location = Location(); SeedStock(item, location, 3);
			await _service.RefreshAlertsAsync(_actor); await _service.RefreshAlertsAsync(_actor);
			var first = _store.All<InventoryAlert>().Should().ContainSingle().Which;
			first.AlertType.Should().Be((int)InventoryAlertType.LowStock); first.Quantity.Should().Be(3); first.Status.Should().Be(0);
			SeedStock(item, location, 2); await _service.RefreshAlertsAsync(_actor);
			_store.All<InventoryAlert>().Should().ContainSingle().Which.Quantity.Should().Be(2); _events.Should().ContainSingle();
			SeedStock(item, location, 4); await _service.RefreshAlertsAsync(_actor);
			_store.All<InventoryAlert>().Single().Status.Should().Be(1); _store.All<InventoryAlert>().Single().ResolvedOn.Should().Be(_clock.Utc);
			SeedStock(item, location, 1); await _service.RefreshAlertsAsync(_actor);
			var alerts = _store.All<InventoryAlert>().ToList(); alerts.Should().HaveCount(2).And.OnlyContain(a => a.Content == null);
			var reopened = alerts.Single(a => a.Status == 0); reopened.Id.Should().NotBe(first.Id); reopened.DedupKey.Should().Be(first.DedupKey);
			_events.Should().HaveCount(2).And.OnlyContain(e => e.Trigger == WorkflowTriggerEventType.InventoryLowStock);
			JsonConvert.SerializeObject(_events).Should().NotContain(Canary).And.NotContain("ReorderPoint").And.NotContain("UnitCost");
		}

		[TestCase(InventoryTrackingMode.Bulk)]
		[TestCase(InventoryTrackingMode.Serialized)]
		public async Task Department_low_stock_totals_are_hidden_when_a_contributing_unit_is_not_visible(InventoryTrackingMode tracking)
		{
			var item = M5ThresholdItem(3, tracking); var visible = Location(); var hidden = Location(InventoryLocationType.Unit, 101);
			if (tracking == InventoryTrackingMode.Bulk) { SeedStock(item, visible, 1); SeedStock(item, hidden, 2); }
			else { await CreateAsset(item, visible, "visible-equipment"); await CreateAsset(item, hidden, "hidden-equipment-1"); await CreateAsset(item, hidden, "hidden-equipment-2"); }
			await _service.RefreshAlertsAsync(_actor); var alert = _store.All<InventoryAlert>().Single(); alert.Quantity.Should().Be(3);
			_deniedLocations.Add(hidden.Id); _read.Invocations.Clear();
			(await _service.QueryAsync<InventoryAlert>(_actor, new InventoryQuery { ItemId = item.Id })).Items.Should().BeEmpty();
			await Fails(() => _service.GetAsync<InventoryAlert>(_actor, alert.Id), "LocationUnavailable", 404);
			(await _service.CanReceiveAlertAsync(Department, "recipient", alert.Id)).Should().BeFalse();
			(await _service.ClaimAlertAsync(Department, "recipient")).Should().BeNull(); _store.All<InventoryAlertDelivery>().Should().BeEmpty();
			_read.Invocations.Should().NotContain(i => i.Method.IsGenericMethod && i.Method.GetGenericArguments().Contains(typeof(InventoryAlert)));
			_deniedLocations.Remove(hidden.Id);
			(await _service.QueryAsync<InventoryAlert>(_actor, new InventoryQuery { ItemId = item.Id })).Items.Should().ContainSingle().Which.Quantity.Should().Be(3);
		}

		[Test]
		public async Task Rebuilding_stock_from_ledger_resolves_a_stale_low_stock_alert_in_the_same_attended_operation()
		{
			var item = M5ThresholdItem(3); var location = Location(); await _service.PostTransactionAsync(_actor, Receive(item, location, 5));
			SeedStock(item, location, 1); await _service.RefreshAlertsAsync(_actor);
			var alert = _store.All<InventoryAlert>().Should().ContainSingle().Which; alert.Status.Should().Be(0); alert.Quantity.Should().Be(1);
			await _service.RebuildStocksAsync(_actor);
			Stock(item, location).Should().Be(5); _store.All<InventoryTransaction>().Should().ContainSingle();
			var resolved = _store.All<InventoryAlert>().Single(); resolved.Id.Should().Be(alert.Id); resolved.Status.Should().Be(1); resolved.ResolvedOn.Should().Be(_clock.Utc);
			(await _service.CanReceiveAlertAsync(Department, "recipient", alert.Id)).Should().BeFalse();
		}

		[Test]
		public async Task Alert_refresh_and_claim_are_not_limited_by_more_than_five_thousand_completed_deliveries()
		{
			var item = M5ThresholdItem(3); var location = Location(); SeedStock(item, location, 2);
			await _service.RefreshAlertsAsync(_actor); var open = _store.All<InventoryAlert>().Single();
			for (var i = 0; i < 5001; i++)
			{
				var historical = _store.Seed(new InventoryAlert { DepartmentId = Department, ItemId = item.Id, DedupKey = open.DedupKey, Status = 1, OpenedOn = _clock.Utc.AddDays(-1), ResolvedOn = _clock.Utc });
				_store.Seed(new InventoryAlertDelivery { DepartmentId = Department, AlertId = historical.Id, UserId = "recipient", State = 2, NextAttemptOn = _clock.Utc, HandedOffOn = _clock.Utc });
			}
			await _service.RefreshAlertsAsync(_actor); _events.Should().ContainSingle();
			var claim = await _service.ClaimAlertAsync(Department, "recipient"); claim.AlertId.Should().Be(open.Id); claim.State.Should().Be(1);
			await _service.FinishAlertAsync(Department, claim.Id, claim.ClaimToken, true);
			(await _service.ClaimAlertAsync(Department, "recipient")).Should().BeNull();
			_store.All<InventoryAlertDelivery>().Should().HaveCount(5002).And.OnlyContain(d => d.State == 2);
		}

		[Test]
		public async Task Unattended_alert_sweep_uses_only_dates_and_metadata_and_rolls_expiring_alerts_into_expired_history()
		{
			var item = M5ThresholdItem(100); var location = Location();
			var lot = _store.Seed(new InventoryLot { DepartmentId = Department, ItemId = item.Id, ExpiresOn = _clock.Utc.AddDays(1), ReceivedOn = _clock.Utc.AddDays(-1), IsProtected = true, Content = "SYNTHETIC-OPAQUE-LOT" });
			var stock = _store.Seed(new InventoryStock { DepartmentId = Department, ItemId = item.Id, LocationId = location.Id, LotId = lot.Id, Quantity = 2 });
			var assetItem = Item(InventoryTrackingMode.Serialized);
			_store.Seed(new InventoryAsset { DepartmentId = Department, ItemId = assetItem.Id, CurrentLocationId = location.Id, ExpiresOn = _clock.Utc.AddDays(-1), Content = "SYNTHETIC-OPAQUE-ASSET", IsProtected = true });
			var issuance = _store.Seed(new InventoryIssuance { DepartmentId = Department, ItemId = item.Id, LocationId = location.Id, Quantity = 5, ReturnedQuantity = 2, Status = 2,
				ExpectedReturnOn = _clock.Utc.AddHours(-1), Content = "SYNTHETIC-OPAQUE-ISSUANCE", IsProtected = true });
			_read.Invocations.Clear(); _write.Invocations.Clear(); _auth.Invocations.Clear();
			await _service.SweepAlertsAsync(Department); await _service.SweepAlertsAsync(Department);
			var alerts = _store.All<InventoryAlert>().ToList(); alerts.Should().HaveCount(3);
			alerts.Select(a => a.AlertType).Should().BeEquivalentTo(new[] { 1, 2, 3 });
			alerts.Single(a => a.AlertType == 3).Quantity.Should().Be(3);
			_read.Invocations.Should().BeEmpty(); _write.Invocations.Should().BeEmpty();
			_auth.Invocations.Should().OnlyContain(i => i.Method.Name == nameof(IInventoryAuthorizationService.IsEnabledAsync));
			_events.Should().HaveCount(3); var expiring = alerts.Single(a => a.LotId == lot.Id);
			_clock.Advance(TimeSpan.FromDays(2)); issuance.ReturnedQuantity = issuance.Quantity; issuance.Status = 1; _store.Seed(issuance);
			await _service.SweepAlertsAsync(Department);
			alerts = _store.All<InventoryAlert>().ToList(); alerts.Single(a => a.Id == expiring.Id).Status.Should().Be(1);
			alerts.Should().ContainSingle(a => a.LotId == lot.Id && a.AlertType == 2 && a.Status == 0);
			alerts.Single(a => a.IssuanceId == issuance.Id).Status.Should().Be(1); _events.Should().HaveCount(4);
			stock.Quantity = 0; _store.Seed(stock); await _service.SweepAlertsAsync(Department);
			_store.All<InventoryAlert>().Where(a => a.LotId == lot.Id).Should().OnlyContain(a => a.Status == 1);
			_store.All<InventoryAlert>().Should().OnlyContain(a => a.Content == null);
			JsonConvert.SerializeObject(_events).Should().NotContain("SYNTHETIC-OPAQUE").And.NotContain(Canary).And.NotContain("ReorderPoint");
		}

		[Test]
		public async Task Alert_claims_recheck_access_retry_with_backoff_and_deduplicate_each_recipient()
		{
			var item = M5ThresholdItem(3); var location = Location(); SeedStock(item, location, 2); await _service.RefreshAlertsAsync(_actor);
			var alert = _store.All<InventoryAlert>().Single(); const string recipient = "inventory-recipient";
			M5RecipientPermission(recipient, false);
			(await _service.CanReceiveAlertAsync(Department, recipient, alert.Id)).Should().BeFalse();
			(await _service.ClaimAlertAsync(Department, recipient)).Should().BeNull(); _store.All<InventoryAlertDelivery>().Should().BeEmpty();
			M5RecipientPermission(recipient, true); _read.Invocations.Clear(); _write.Invocations.Clear();
			var first = await _service.ClaimAlertAsync(Department, recipient); first.State.Should().Be(1); first.AttemptCount.Should().Be(1);
			first.LeaseUntil.Should().Be(_clock.Utc.AddMinutes(30)); first.Content.Should().BeNull();
			(await _service.ClaimAlertAsync(Department, recipient)).Should().BeNull();
			await _service.FinishAlertAsync(Department, first.Id, Guid.NewGuid().ToString("D"), true);
			_store.All<InventoryAlertDelivery>().Single().State.Should().Be(1);
			await _service.FinishAlertAsync(Department, first.Id, first.ClaimToken, false);
			var retry = _store.All<InventoryAlertDelivery>().Single(); retry.State.Should().Be(0); retry.ClaimToken.Should().BeNull(); retry.LeaseUntil.Should().BeNull();
			retry.NextAttemptOn.Should().Be(_clock.Utc.AddMinutes(10)); (await _service.ClaimAlertAsync(Department, recipient)).Should().BeNull();
			_clock.Advance(TimeSpan.FromMinutes(10)); M5RecipientPermission(recipient, false);
			(await _service.ClaimAlertAsync(Department, recipient)).Should().BeNull();
			M5RecipientPermission(recipient, true); var second = await _service.ClaimAlertAsync(Department, recipient);
			second.Id.Should().Be(first.Id); second.ClaimToken.Should().NotBe(first.ClaimToken); second.AttemptCount.Should().Be(2);
			M5RecipientPermission(recipient, false); (await _service.CanReceiveAlertAsync(Department, recipient, alert.Id)).Should().BeFalse();
			M5RecipientPermission(recipient, true); await _service.FinishAlertAsync(Department, second.Id, second.ClaimToken, true);
			var delivered = _store.All<InventoryAlertDelivery>().Single(); delivered.State.Should().Be(2); delivered.HandedOffOn.Should().Be(_clock.Utc);
			(await _service.ClaimAlertAsync(Department, recipient)).Should().BeNull();
			var other = await _service.ClaimAlertAsync(Department, "other-recipient"); other.Id.Should().NotBe(first.Id); other.AlertId.Should().Be(alert.Id);
			_read.Invocations.Should().BeEmpty(); _write.Invocations.Should().BeEmpty();
			_store.All<InventoryAlertDelivery>().Should().HaveCount(2).And.OnlyContain(d => d.Content == null);
			SeedStock(item, location, 4); await _service.RefreshAlertsAsync(_actor);
			(await _service.CanReceiveAlertAsync(Department, "other-recipient", alert.Id)).Should().BeFalse();
			(await _service.CanReceiveAlertAsync(88, recipient, alert.Id)).Should().BeFalse();
		}

		[Test]
		public async Task Expired_alert_claims_can_be_reclaimed_but_old_claim_tokens_cannot_finish_them()
		{
			M5ThresholdItem(1); await _service.RefreshAlertsAsync(_actor);
			var first = await _service.ClaimAlertAsync(Department, "recipient"); _clock.Advance(TimeSpan.FromMinutes(31));
			await _service.FinishAlertAsync(Department, first.Id, first.ClaimToken, true);
			_store.All<InventoryAlertDelivery>().Single().State.Should().Be(1);
			var reclaimed = await _service.ClaimAlertAsync(Department, "recipient"); reclaimed.Id.Should().Be(first.Id);
			reclaimed.ClaimToken.Should().NotBe(first.ClaimToken); reclaimed.AttemptCount.Should().Be(2);
			await _service.FinishAlertAsync(Department, first.Id, first.ClaimToken, true);
			_store.All<InventoryAlertDelivery>().Single().ClaimToken.Should().Be(reclaimed.ClaimToken);
			await _service.FinishAlertAsync(Department, reclaimed.Id, reclaimed.ClaimToken, true);
			_store.All<InventoryAlertDelivery>().Single().State.Should().Be(2);
		}

		[Test]
		public async Task Dated_alert_delivery_rechecks_the_current_location_scope_without_reading_protected_content()
		{
			var item = Item(InventoryTrackingMode.Serialized); var location = Location();
			_store.Seed(new InventoryAsset { DepartmentId = Department, ItemId = item.Id, CurrentLocationId = location.Id, ExpiresOn = _clock.Utc.AddDays(1), IsProtected = true, Content = "SYNTHETIC-OPAQUE-ASSET" });
			await _service.SweepAlertsAsync(Department); var alert = _store.All<InventoryAlert>().Single();
			_deniedLocations.Add(location.Id); _read.Invocations.Clear();
			(await _service.CanReceiveAlertAsync(Department, "recipient", alert.Id)).Should().BeFalse();
			(await _service.ClaimAlertAsync(Department, "recipient")).Should().BeNull();
			_deniedLocations.Remove(location.Id); var delivery = await _service.ClaimAlertAsync(Department, "recipient"); delivery.AlertId.Should().Be(alert.Id);
			_deniedLocations.Add(location.Id); (await _service.CanReceiveAlertAsync(Department, "recipient", alert.Id)).Should().BeFalse();
			_read.Invocations.Should().BeEmpty();
		}

		private InventoryCountInput M5CountInput(InventoryLocation location) => new() { Id = Guid.NewGuid().ToString("D"), LocationId = location.Id, Name = "Synthetic stock count", Note = Canary };
		private static InventoryCountUpdate M5Observations(InventoryCountDetail detail, decimal quantity) => new() { CountId = detail.Count.Id, Revision = detail.Count.Revision,
			Lines = detail.Lines.Select(l => new InventoryCountObservation { Id = l.Id, Quantity = quantity }).ToList() };
		private static InventoryCountComplete M5Completion(InventoryCountDetail detail) => new() { CountId = detail.Count.Id, Revision = detail.Count.Revision, RequestId = Guid.NewGuid().ToString("D") };
		private async Task<InventoryCountDetail> M5ObservedCount(InventoryLocation location, decimal quantity)
		{
			var detail = await _service.StartCountAsync(_actor, M5CountInput(location));
			return await _service.SaveCountAsync(_actor, M5Observations(detail, quantity));
		}
		private InventoryItem M5ThresholdItem(decimal threshold, InventoryTrackingMode tracking = InventoryTrackingMode.Bulk)
		{
			var item = Item(tracking); var content = JsonConvert.DeserializeObject<InventoryItemContent>(item.Content); content.ReorderPoint = threshold;
			item.Content = JsonConvert.SerializeObject(content); return _store.Seed(item);
		}
		private void M5RecipientPermission(string recipient, bool allowed)
		{
			var setup = _auth.Setup(x => x.RequireAsync(It.Is<InventoryActor>(a => a.UserId == recipient && a.GrantToken == null), false, PermissionTypes.AdjustInventory, null));
			if (allowed) setup.Returns(Task.CompletedTask); else setup.ThrowsAsync(new InventoryException(403, "PermissionRequired"));
		}
	}
}
