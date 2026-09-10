using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Inventories;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Model.WorkOrders;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public sealed partial class InventoryModernizationTests
	{
		private const int Department = 77;
		private const string Canary = "PII-PHI-CANARY inventory narrative";
		private readonly InventoryActor _actor = new() { DepartmentId = Department, UserId = "manager", GrantToken = "synthetic-manager-grant" };
		private Store _store;
		private InventoryModernizationService _service;
		private Mock<IInventoryAuthorizationService> _auth;
		private Mock<IProtectedReadService> _read;
		private Mock<IProtectedWriteService> _write;
		private Mock<IUnitOfWork> _uow;
		private Mock<IDomainEventOutboxService> _outbox;
		private List<DomainEventEnvelope> _events;
		private List<List<long>> _dispatches;
		private List<AuditLog> _audits;
		private HashSet<string> _deniedLocations;
		private DbTransaction _transaction;
		private int _eventsBefore;
		private int _auditsBefore;
		private long _outboxSequence;
		private TestClock _clock;
		private Mock<IInventoryRepository> _legacyInventory;
		private Mock<IInventoryTypesRepository> _legacyTypes;
		private Mock<IWorkOrderRepository> _workOrders;
		private Mock<IWorkOrderAuthorizationService> _workOrderAuth;
		private Mock<IRecordsAuthorizationService> _recordsAuth;
		private Mock<IRmsInventoryUsageAdapter> _recordUsageAdapter;
		private Mock<IContactsService> _contacts;
		private Dictionary<string, Contact> _companyContacts;

		[SetUp]
		public void SetUp()
		{
			_store = new Store(); _events = new(); _dispatches = new(); _audits = new(); _deniedLocations = new();
			_transaction = null; _outboxSequence = 0; _clock = new TestClock();
			_auth = new Mock<IInventoryAuthorizationService>();
			_auth.Setup(x => x.RequireAsync(It.IsAny<InventoryActor>(), It.IsAny<bool>(), It.IsAny<PermissionTypes?>(), It.IsAny<int?>()))
				.Returns(Task.CompletedTask);
			_auth.Setup(x => x.CanLocationAsync(It.IsAny<InventoryActor>(), It.IsAny<InventoryLocation>()))
				.ReturnsAsync((InventoryActor a, InventoryLocation l) => l.DepartmentId == a.DepartmentId && !_deniedLocations.Contains(l.Id));
			_auth.Setup(x => x.ValidateHolderAsync(It.IsAny<InventoryActor>(), It.IsAny<InventoryLocation>())).Returns(Task.CompletedTask);
			_auth.Setup(x => x.IsEnabledAsync(Department)).ReturnsAsync(true);
			_read = new Mock<IProtectedReadService>(); _read.SetReturnsDefault(Task.FromResult(new ProtectedReadResult()));
			_write = new Mock<IProtectedWriteService>(); _write.SetReturnsDefault(Task.FromResult(ProtectedWriteResult.Allowed()));
			var units = new Mock<IUnitsService>();
			units.Setup(x => x.GetUnitByIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => new Unit { UnitId = id, DepartmentId = Department, StationGroupId = 10 });
			var groups = new Mock<IDepartmentGroupsService>();
			groups.Setup(x => x.GetGroupByIdAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync((int id, bool b) => new DepartmentGroup { DepartmentGroupId = id, DepartmentId = Department });
			groups.Setup(x => x.GetGroupForUserAsync(It.IsAny<string>(), Department)).ReturnsAsync(new DepartmentGroup { DepartmentGroupId = 10, DepartmentId = Department });
			var audit = new Mock<IAuditLogsRepository>();
			audit.Setup(x => x.InsertAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((AuditLog a, CancellationToken c, bool f) => { _transaction.Should().NotBeNull(); a.AuditLogId = _audits.Count + 1; _audits.Add(a); return a; });
			_outbox = new Mock<IDomainEventOutboxService>();
			_outbox.Setup(x => x.EnqueueAsync(Department, "Inventory", It.IsAny<DomainEventEnvelope>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string p, DomainEventEnvelope e, CancellationToken c) =>
				{
					_transaction.Should().NotBeNull("the event must be persisted alongside its inventory movement");
					_events.Add(Copy(e)); return new DomainEventOutboxEntry { DomainEventOutboxId = ++_outboxSequence };
				});
			_outbox.Setup(x => x.DispatchAfterCommitAsync(It.IsAny<IEnumerable<long>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((IEnumerable<long> ids, CancellationToken c) => { _transaction.Should().BeNull("dispatch is allowed only after the owner commits"); var batch = ids.ToList(); _dispatches.Add(batch); return batch.Count; });
			_uow = new Mock<IUnitOfWork>(); _uow.SetupGet(x => x.Transaction).Returns(() => _transaction);
			_uow.Setup(x => x.CreateOrGetConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => { Begin(); return (DbConnection)null; });
			_uow.Setup(x => x.CommitChanges()).Callback(() => { _store.Commit(); _transaction = null; });
			_uow.Setup(x => x.DiscardChanges()).Callback(Rollback);
			_legacyInventory = new(); _legacyTypes = new();
			_legacyInventory.Setup(x => x.GetAllInventoriesByDepartmentIdAsync(Department)).ReturnsAsync(Array.Empty<Inventory>());
			_legacyTypes.Setup(x => x.GetAllByDepartmentIdAsync(Department)).ReturnsAsync(Array.Empty<InventoryType>());
			_workOrders = new(); _workOrderAuth = new();
			_recordsAuth = new(); _recordUsageAdapter = new();
			_recordsAuth.Setup(x => x.CanUserViewRecordAsync(It.IsAny<string>(), It.IsAny<string>(), Department)).ReturnsAsync(true);
			_recordsAuth.Setup(x => x.HasPermissionAsync(It.IsAny<string>(), Department, PermissionTypes.ViewRestrictedRecords)).ReturnsAsync(true);
			_recordUsageAdapter.Setup(x => x.RequireUsageCorrectionAccessAsync(It.IsAny<InventoryActor>(), It.IsAny<string>())).Returns(Task.CompletedTask);
			_contacts = new(); _companyContacts = new();
			_contacts.Setup(x => x.GetContactByIdAsync(It.IsAny<string>())).ReturnsAsync((string id) => _companyContacts.TryGetValue(id, out var contact) ? Copy(contact) : null);
			_contacts.Setup(x => x.GetAllContactsForDepartmentAsync(It.IsAny<int>())).ReturnsAsync((int departmentId) => _companyContacts.Values.Where(c => c.DepartmentId == departmentId).Select(Copy).ToList());
			_service = new InventoryModernizationService(_store, _auth.Object, _uow.Object, _read.Object, _write.Object,
				_outbox.Object, audit.Object, units.Object, groups.Object, _clock, _legacyInventory.Object, _legacyTypes.Object,
				_workOrders.Object, new Lazy<IWorkOrderAuthorizationService>(() => _workOrderAuth.Object),
				new Lazy<IRecordsAuthorizationService>(() => _recordsAuth.Object), new Lazy<IRmsInventoryUsageAdapter>(() => _recordUsageAdapter.Object), _contacts.Object);
		}

		[Test]
		public async Task Expiring_bulk_items_require_lot_tracking_and_a_dated_lot_for_receipts()
		{
			var input = new InventoryItemInput { TrackingMode = InventoryTrackingMode.Bulk, RequiresExpiration = true,
				Details = new InventoryItemContent { Name = "Synthetic expiring supplies", UnitOfMeasure = "each" } };
			await Fails(() => _service.SaveItemAsync(_actor, input), "ExpiryRequiresLotTracking", 400);
			_store.All<InventoryItem>().Should().BeEmpty();
			input.RequiresLotTracking = true; var item = await _service.SaveItemAsync(_actor, input); var location = Location();
			await Fails(() => _service.PostTransactionAsync(_actor, Receive(item, location, 1)), "LotRequired", 400);
			await Fails(() => _service.SaveLotAsync(_actor, new InventoryLot { ItemId = item.Id }, new InventoryLotContent { LotNumber = "synthetic lot" }), "InvalidLot", 400);
			var lot = await _service.SaveLotAsync(_actor, new InventoryLot { ItemId = item.Id, ExpiresOn = _clock.Utc.AddDays(10) }, new InventoryLotContent { LotNumber = "synthetic lot" });
			var receipt = Receive(item, location, 1); receipt.Lines[0].LotId = lot.Id;
			await _service.PostTransactionAsync(_actor, receipt);
			_store.All<InventoryTransaction>().Should().ContainSingle(t => t.LotId == lot.Id);
		}

		[TestCase(false)]
		[TestCase(true)]
		public async Task Expiration_tracking_cannot_change_after_an_item_has_ledger_history(bool initiallyRequired)
		{
			var input = new InventoryItemInput { TrackingMode = InventoryTrackingMode.Bulk, RequiresExpiration = initiallyRequired, RequiresLotTracking = true,
				Details = new InventoryItemContent { Name = "Synthetic lot supplies", UnitOfMeasure = "each" } };
			var item = await _service.SaveItemAsync(_actor, input); var location = Location();
			var lot = await _service.SaveLotAsync(_actor, new InventoryLot { ItemId = item.Id, ExpiresOn = _clock.Utc.AddDays(10) }, new InventoryLotContent { LotNumber = "synthetic lot" });
			var receipt = Receive(item, location, 1); receipt.Lines[0].LotId = lot.Id; await _service.PostTransactionAsync(_actor, receipt);
			input.Id = item.Id; input.Revision = (await _service.GetAsync<InventoryItem>(_actor, item.Id)).Revision; input.RequiresExpiration = !initiallyRequired;
			await Fails(() => _service.SaveItemAsync(_actor, input), "ItemTrackingLocked", 409);
			(await _service.GetAsync<InventoryItem>(_actor, item.Id)).RequiresExpiration.Should().Be(initiallyRequired);
		}

		[TestCase(InventoryAssetStatus.Retired)]
		[TestCase(InventoryAssetStatus.Lost)]
		public async Task Terminal_bag_containers_do_not_block_inventory_locations_or_asset_lists(InventoryAssetStatus status)
		{
			var bagItem = Item(InventoryTrackingMode.Serialized, kit: true); var equipmentItem = Item(InventoryTrackingMode.Serialized); var location = Location();
			var bag = await CreateAsset(bagItem, location, "synthetic-bag");
			var container = _store.All<InventoryLocation>().Single(l => l.ContainerAssetId == bag.Id);
			var containedAsset = await CreateAsset(equipmentItem, container, "synthetic-contained");
			var availableAsset = await CreateAsset(equipmentItem, location, "synthetic-available");
			await _service.ChangeAssetStatusAsync(_actor, Status(bagItem, bag, location, status));

			var locations = await _service.ListAsync<InventoryLocation>(_actor);
			locations.Items.Should().Contain(l => l.Id == location.Id).And.NotContain(l => l.Id == container.Id);
			var assets = await _service.ListAsync<InventoryAsset>(_actor);
			assets.Items.Should().Contain(a => a.Id == availableAsset.Id).And.NotContain(a => a.Id == containedAsset.Id);
			(await _service.QueryAsync<InventoryAsset>(_actor, new InventoryQuery { ItemId = equipmentItem.Id })).Items.Select(a => a.Id).Should().Equal(availableAsset.Id);
			_store.All<InventoryLocation>().Should().Contain(l => l.Id == container.Id, "the container identity remains as historical evidence");
		}

		[Test]
		public async Task Structural_queries_filter_before_paging_and_authorize_each_returned_holder()
		{
			var item = Item(); var unrelated = Item(); var visible = Location(); var denied = Location(); _deniedLocations.Add(denied.Id);
			for (var i = 1; i <= 502; i++) _store.Seed(new InventoryTransaction { DepartmentId = Department, ItemId = item.Id,
				ToLocationId = visible.Id, EntryId = i, Quantity = 1, Content = "{}", OccurredOn = _clock.Utc });
			for (var i = 0; i < 501; i++) _store.Seed(new InventoryTransaction { DepartmentId = Department, ItemId = unrelated.Id,
				ToLocationId = visible.Id, EntryId = 2000 + i, Quantity = 1, Content = "{}", OccurredOn = _clock.Utc });
			var hidden = _store.Seed(new InventoryTransaction { DepartmentId = Department, ItemId = item.Id, ToLocationId = denied.Id,
				EntryId = 503, Quantity = 1, Content = "{\"Note\":\"denied-content-canary\"}", OccurredOn = _clock.Utc });
			_store.Seed(new InventoryTransaction { DepartmentId = 88, ItemId = item.Id, ToLocationId = visible.Id, EntryId = 9000, Quantity = 1, Content = "{}" });
			_read.Invocations.Clear();
			var first = await _service.QueryAsync<InventoryTransaction>(_actor, new InventoryQuery { ItemId = item.Id });
			var second = await _service.QueryAsync<InventoryTransaction>(_actor, new InventoryQuery { ItemId = item.Id }, 1);
			first.HasMore.Should().BeTrue(); second.HasMore.Should().BeFalse();
			var returned = first.Items.Concat(second.Items).ToList();
			returned.Should().HaveCount(502).And.OnlyContain(t => t.DepartmentId == Department && t.ItemId == item.Id && t.ToLocationId == visible.Id);
			returned.Select(t => t.EntryId).Should().Equal(Enumerable.Range(1, 502).Reverse().Select(i => (long)i));
			returned.Should().NotContain(t => t.Id == hidden.Id);
			_read.Invocations.Count(i => i.Method.IsGenericMethod && i.Method.GetGenericArguments().Contains(typeof(InventoryTransaction))).Should().Be(502,
				"a holder-denied or foreign row must not reach protected-content resolution");
			await Fails(() => _service.QueryAsync<InventoryTransaction>(_actor, new InventoryQuery { ItemId = "invalid-filter" }), "InvalidIdentifier", 400);
		}

		[Test]
		public async Task Serialized_outbound_adjustments_cannot_leave_current_stock_divergent_from_the_ledger()
		{
			var item = Item(InventoryTrackingMode.Serialized); var location = Location(); var asset = await CreateAsset(item, location);
			var adjustment = Move(item, location, null, 1, InventoryTransactionType.Adjust); adjustment.AssetId = asset.Id;
			var eventsBefore = _events.Count; var operationsBefore = _store.All<InventoryOperation>().Count();
			await Fails(() => _service.PostTransactionAsync(_actor, Command(adjustment)), "SerializedAdjustmentUnsupported", 400);
			_store.All<InventoryTransaction>().Should().HaveCount(1); _store.All<InventoryOperation>().Should().HaveCount(operationsBefore); _events.Should().HaveCount(eventsBefore);
			var unchanged = _store.All<InventoryAsset>().Single(); unchanged.CurrentLocationId.Should().Be(location.Id); unchanged.Status.Should().Be((int)InventoryAssetStatus.InService); unchanged.Revision.Should().Be(asset.Revision);
		}

		[TestCase(InventoryTransactionType.Consume)]
		[TestCase(InventoryTransactionType.WriteOff)]
		public async Task Serialized_disposal_retries_return_the_receipt_but_new_requests_cannot_dispose_the_same_asset_again(InventoryTransactionType type)
		{
			var item = Item(InventoryTrackingMode.Serialized); var location = Location(); var asset = await CreateAsset(item, location);
			var disposal = Move(item, location, null, 1, type); disposal.AssetId = asset.Id; var command = Command(disposal);
			var first = await _service.PostTransactionAsync(_actor, command); var eventsBefore = _events.Count;
			(await _service.PostTransactionAsync(_actor, Copy(command))).TransactionIds.Should().Equal(first.TransactionIds);
			command.RequestId = Guid.NewGuid().ToString("D");
			await Fails(() => _service.PostTransactionAsync(_actor, command), "AssetNotAvailable", 409);
			_store.All<InventoryTransaction>().Should().HaveCount(2); _store.All<InventoryOperation>().Should().HaveCount(2); _events.Should().HaveCount(eventsBefore);
			_store.All<InventoryAsset>().Single().Status.Should().Be((int)(type == InventoryTransactionType.Consume ? InventoryAssetStatus.Consumed : InventoryAssetStatus.Lost));
		}

		[TestCase(InventoryAssetStatus.Lost)]
		[TestCase(InventoryAssetStatus.Consumed)]
		[TestCase(InventoryAssetStatus.Retired)]
		public async Task Terminal_asset_status_cannot_implicitly_restore_stock_or_allow_new_consumption(InventoryAssetStatus terminal)
		{
			var item = Item(InventoryTrackingMode.Serialized); var location = Location(); var asset = await CreateAsset(item, location);
			await _service.ChangeAssetStatusAsync(_actor, Status(item, asset, location, terminal)); asset = _store.All<InventoryAsset>().Single();
			var eventsBefore = _events.Count;
			foreach (var status in new[] { InventoryAssetStatus.InService, InventoryAssetStatus.Issued, InventoryAssetStatus.OutForRepair, InventoryAssetStatus.Damaged })
				await Fails(() => _service.ChangeAssetStatusAsync(_actor, Status(item, asset, location, status)), "AssetNotAvailable", 409);
			var consumption = Move(item, location, null, 1, InventoryTransactionType.Consume); consumption.AssetId = asset.Id;
			await Fails(() => _service.PostTransactionAsync(_actor, Command(consumption)), "AssetNotAvailable", 409);
			_store.All<InventoryAsset>().Single().Status.Should().Be((int)terminal); _store.All<InventoryTransaction>().Should().HaveCount(2);
			_store.All<InventoryOperation>().Should().HaveCount(2); _events.Should().HaveCount(eventsBefore);
		}

		[Test]
		public async Task Expired_serialized_assets_cannot_be_consumed_but_can_be_written_off()
		{
			var item = Item(InventoryTrackingMode.Serialized); var location = Location();
			var asset = await _service.CreateAssetAsync(_actor, new InventoryAssetInput { RequestId = Guid.NewGuid().ToString("D"), ItemId = item.Id,
				LocationId = location.Id, ExpiresOn = _clock.Utc.AddDays(-1), Details = new InventoryAssetContent { SerialNumber = "synthetic-expired-asset" } });
			var consumption = Move(item, location, null, 1, InventoryTransactionType.Consume); consumption.AssetId = asset.Id;
			await Fails(() => _service.PostTransactionAsync(_actor, Command(consumption)), "AssetNotAvailable", 409);
			_store.All<InventoryTransaction>().Should().HaveCount(1); _store.All<InventoryAsset>().Single().Status.Should().Be((int)InventoryAssetStatus.InService);
			consumption.Type = InventoryTransactionType.WriteOff; await _service.PostTransactionAsync(_actor, Command(consumption));
			_store.All<InventoryAsset>().Single().Status.Should().Be((int)InventoryAssetStatus.Lost);
		}

		[Test]
		public async Task Public_posting_cannot_attach_a_caller_supplied_issuance_backlink()
		{
			var item = Item(); var other = Item(); var location = Location();
			var issuance = _store.Seed(new InventoryIssuance { DepartmentId = Department, ItemId = other.Id, LocationId = location.Id, Quantity = 1, IssuedToUserId = "member" });
			var command = Receive(item, location, 2); command.Lines[0].IssuanceId = issuance.Id;
			await Fails(() => _service.PostTransactionAsync(_actor, command), "InvalidIssuance", 400);
			_store.All<InventoryTransaction>().Should().BeEmpty(); _store.All<InventoryStock>().Should().BeEmpty(); _store.All<InventoryOperation>().Should().BeEmpty(); _events.Should().BeEmpty();
			_store.All<InventoryIssuance>().Single().ItemId.Should().Be(other.Id);
		}

		[Test]
		public async Task Retry_returns_original_receipt_without_stock_or_event_duplication_and_changed_retry_conflicts()
		{
			var item = Item(); var location = Location(); var command = Receive(item, location, 2.125001m);
			var first = await _service.PostTransactionAsync(_actor, command);
			var retry = await _service.PostTransactionAsync(_actor, Copy(command));
			retry.OperationId.Should().Be(first.OperationId); retry.TransactionIds.Should().Equal(first.TransactionIds);
			Stock(item, location).Should().Be(2.125001m); _store.All<InventoryTransaction>().Should().HaveCount(1);
			_store.All<InventoryOperation>().Should().HaveCount(1); _events.Should().HaveCount(1);
			command.Lines[0].Quantity = 3;
			await Fails(() => _service.PostTransactionAsync(_actor, command), "RequestConflict", 409);
			Stock(item, location).Should().Be(2.125001m); _events.Should().HaveCount(1);
		}

		[Test]
		public async Task Missing_or_reserved_request_identifier_fails_before_mutation()
		{
			var item = Item(); var location = Location(); var command = Receive(item, location, 1);
			command.RequestId = null; await Fails(() => _service.PostTransactionAsync(_actor, command), "InvalidIdentifier", 400);
			command.RequestId = Guid.Empty.ToString("D"); await Fails(() => _service.PostTransactionAsync(_actor, command), "InvalidIdentifier", 400);
			command.RequestId = "00000000-0000-0000-0000-000000000001";
			await Fails(() => _service.PostTransactionAsync(_actor, command), "ReservedRequestId", 400);
			_store.All<InventoryStock>().Should().BeEmpty(); _store.All<InventoryOperation>().Should().BeEmpty(); _events.Should().BeEmpty();
		}

		[Test]
		public async Task Insufficient_second_line_rolls_back_prior_stock_ledger_audit_and_outbox()
		{
			var first = Item(); var second = Item(); var location = Location(); SeedStock(first, location, 5); SeedStock(second, location, 1);
			var command = Command(Move(first, location, null, 2, InventoryTransactionType.Consume), Move(second, location, null, 2, InventoryTransactionType.Consume));
			await Fails(() => _service.PostTransactionAsync(_actor, command), "InsufficientStock", 409);
			Stock(first, location).Should().Be(5); Stock(second, location).Should().Be(1);
			_store.All<InventoryTransaction>().Should().BeEmpty(); _store.All<InventoryOperation>().Should().BeEmpty();
			_events.Should().BeEmpty(); _audits.Should().BeEmpty(); _dispatches.Should().BeEmpty();
			_uow.Verify(x => x.CommitChanges(), Times.Never);
		}

		[Test]
		public async Task Transfer_commits_both_legs_and_one_completion_event_and_retries_atomically()
		{
			var first = Item(); var second = Item(); var from = Location(); var to = Location(InventoryLocationType.Unit, 101);
			SeedStock(first, from, 5); SeedStock(second, from, 7);
			var command = Command(Move(first, from, to, 2.25m, InventoryTransactionType.Transfer), Move(second, from, to, 3, InventoryTransactionType.Transfer));
			var result = await _service.CreateAndCompleteTransferAsync(_actor, command);
			(await _service.CreateAndCompleteTransferAsync(_actor, Copy(command))).TransferId.Should().Be(result.TransferId);
			Stock(first, from).Should().Be(2.75m); Stock(first, to).Should().Be(2.25m); Stock(second, from).Should().Be(4); Stock(second, to).Should().Be(3);
			_store.All<InventoryTransfer>().Single().Status.Should().Be(2); _store.All<InventoryTransferItem>().Should().HaveCount(2);
			_events.Count(e => e.Trigger == WorkflowTriggerEventType.InventoryAdjusted).Should().Be(2);
			var completion = _events.Single(e => e.Trigger == WorkflowTriggerEventType.InventoryTransferCompleted);
			completion.AggregateId.Should().Be(result.TransferId); completion.SchemaVersion.Should().Be(1);
			JObject.FromObject(completion.Payload).Value<bool>("InventoryEvent").Should().BeTrue();
			JsonConvert.SerializeObject(_events).Should().NotContain(Canary);
			_dispatches.SelectMany(x => x).Should().HaveCount(3);
		}

		[Test]
		public async Task Transfer_with_unavailable_later_item_creates_no_partial_transfer()
		{
			var first = Item(); var second = Item(); var from = Location(); var to = Location();
			SeedStock(first, from, 5); SeedStock(second, from, 1);
			await Fails(() => _service.CreateAndCompleteTransferAsync(_actor,
				Command(Move(first, from, to, 2, InventoryTransactionType.Transfer), Move(second, from, to, 3, InventoryTransactionType.Transfer))), "InsufficientStock", 409);
			Stock(first, from).Should().Be(5); Stock(first, to).Should().Be(0);
			_store.All<InventoryTransfer>().Should().BeEmpty(); _store.All<InventoryTransferItem>().Should().BeEmpty();
			_store.All<InventoryTransaction>().Should().BeEmpty(); _events.Should().BeEmpty();
		}

		[Test]
		public async Task Serialized_asset_can_be_issued_to_person_returned_damaged_and_then_repaired()
		{
			var item = Item(InventoryTrackingMode.Serialized); var location = Location(); var asset = await CreateAsset(item, location);
			var issue = Issue(item, location, 1, userId: "member", assetId: asset.Id);
			var result = await _service.IssueAsync(_actor, issue);
			(await _service.IssueAsync(_actor, Copy(issue))).IssuanceId.Should().Be(result.IssuanceId);
			var issuance = _store.All<InventoryIssuance>().Single(); issuance.IssuedToUserId.Should().Be("member");
			_store.All<InventoryAsset>().Single().Status.Should().Be((int)InventoryAssetStatus.Issued);
			(await _service.GetIssuableAsync(_actor)).Should().BeEmpty();
			await Fails(() => _service.IssueAsync(_actor, Issue(item, location, 1, unitId: 101, assetId: asset.Id)), "ReturnAssetFirst", 409);
			var returned = await _service.ReturnAsync(_actor, new InventoryReturnInput { RequestId = Guid.NewGuid().ToString("D"), IssuanceId = issuance.Id,
				Revision = issuance.Revision, ToLocationId = location.Id, Quantity = 1, Condition = InventoryAssetStatus.Damaged, Note = Canary });
			returned.IssuanceId.Should().Be(issuance.Id); _store.All<InventoryIssuance>().Single().Status.Should().Be((int)InventoryIssuanceStatus.Returned);
			asset = _store.All<InventoryAsset>().Single(); asset.Status.Should().Be((int)InventoryAssetStatus.Damaged); asset.CurrentLocationId.Should().Be(location.Id);
			await _service.ChangeAssetStatusAsync(_actor, Status(item, asset, location, InventoryAssetStatus.InService));
			(await _service.GetIssuableAsync(_actor)).Single().Asset.Id.Should().Be(asset.Id);
			_events.Count(e => e.Trigger == WorkflowTriggerEventType.InventoryIssued).Should().Be(1);
			_events.Count(e => e.Trigger == WorkflowTriggerEventType.InventoryReturned).Should().Be(1);
			_events.Count(e => e.Trigger == WorkflowTriggerEventType.InventoryAssetStatusChanged).Should().Be(3);
			JsonConvert.SerializeObject(_events).Should().NotContain(Canary).And.NotContain("serial-canary");
		}

		[Test]
		public async Task Bulk_unit_issue_supports_partial_returns_and_preserves_original_quantity()
		{
			var item = Item(); var source = Location(); SeedStock(item, source, 10);
			var issued = await _service.IssueAsync(_actor, Issue(item, source, 6.125001m, unitId: 101));
			var issuance = _store.All<InventoryIssuance>().Single(); var target = _store.All<InventoryLocation>().Single(l => l.UnitId == 101);
			(await _service.GetUnitEquipmentAsync(_actor, 101)).Single().Issuances.Single().Id.Should().Be(issued.IssuanceId);
			var input = new InventoryReturnInput { RequestId = Guid.NewGuid().ToString("D"), IssuanceId = issuance.Id, Revision = issuance.Revision,
				Quantity = 2.125001m, ToLocationId = source.Id, Condition = InventoryAssetStatus.InService };
			await _service.ReturnAsync(_actor, input); await _service.ReturnAsync(_actor, Copy(input));
			issuance = _store.All<InventoryIssuance>().Single(); issuance.Quantity.Should().Be(6.125001m); issuance.ReturnedQuantity.Should().Be(2.125001m);
			issuance.Status.Should().Be((int)InventoryIssuanceStatus.PartiallyReturned); issuance.ReturnedOn.Should().BeNull();
			Stock(item, source).Should().Be(6); Stock(item, target).Should().Be(4);
			input.RequestId = Guid.NewGuid().ToString("D"); input.Revision = issuance.Revision; input.Quantity = 4.000001m;
			await Fails(() => _service.ReturnAsync(_actor, input), "ReturnConflict", 409);
			input.Quantity = 4; await _service.ReturnAsync(_actor, input);
			_store.All<InventoryIssuance>().Single().ReturnedOn.Should().NotBeNull(); Stock(item, source).Should().Be(10); Stock(item, target).Should().Be(0);
			_events.Count(e => e.Trigger == WorkflowTriggerEventType.InventoryReturned).Should().Be(2);
		}

		[Test]
		public async Task Serialized_status_uses_optimistic_revision_and_terminal_status_closes_issuance()
		{
			var item = Item(InventoryTrackingMode.Serialized); var source = Location(); var asset = await CreateAsset(item, source);
			await _service.IssueAsync(_actor, Issue(item, source, 1, unitId: 101, assetId: asset.Id));
			var issued = _store.All<InventoryAsset>().Single(); var location = _store.All<InventoryLocation>().Single(l => l.Id == issued.CurrentLocationId);
			await Fails(() => _service.ChangeAssetStatusAsync(_actor, Status(item, asset, location, InventoryAssetStatus.Lost)), "AssetConflict", 409);
			await _service.ChangeAssetStatusAsync(_actor, Status(item, issued, location, InventoryAssetStatus.Lost));
			_store.All<InventoryIssuance>().Single().Status.Should().Be((int)InventoryIssuanceStatus.Lost);
			(await _service.GetUnitEquipmentAsync(_actor, 101)).Should().BeEmpty();
			(await _service.RoutingAsync(Department, asset.Id)).Should().BeNull();
		}

		[Test]
		public async Task Joined_posting_enqueues_inside_owner_transaction_and_dispatches_only_after_owner_commit()
		{
			var item = Item(); var location = Location(); SeedStock(item, location, 3);
			var command = Command(Move(item, location, null, 1.125m, InventoryTransactionType.Consume));
			command.Lines[0].ReferenceType = InventoryReferenceType.RmsRecord; command.Lines[0].ReferenceId = Guid.NewGuid().ToString("D");
			await _uow.Object.CreateOrGetConnectionAsync();
			var result = await _service.PostWithinTransactionAsync(_actor, command);
			Stock(item, location).Should().Be(1.875m); result.OutboxIds.Should().HaveCount(1); _dispatches.Should().BeEmpty();
			_uow.Verify(x => x.CommitChanges(), Times.Never); _uow.Verify(x => x.CreateOrGetConnectionAsync(It.IsAny<CancellationToken>()), Times.Once);
			_store.All<InventoryTransaction>().Single().ReferenceId.Should().Be(command.Lines[0].ReferenceId);
			_uow.Object.CommitChanges(); await _outbox.Object.DispatchAfterCommitAsync(result.OutboxIds);
			_dispatches.Single().Should().Equal(result.OutboxIds);
		}

		[Test]
		public async Task Joined_retry_preserves_original_dispatch_ids_after_item_lifecycle_changes()
		{
			var item = Item(); var location = Location(); var command = Receive(item, location, 2);
			command.Lines[0].ReferenceType = InventoryReferenceType.RmsRecord; command.Lines[0].ReferenceId = Guid.NewGuid().ToString("D");
			await _uow.Object.CreateOrGetConnectionAsync(); var first = await _service.PostWithinTransactionAsync(_actor, command); _uow.Object.CommitChanges();
			item.IsActive = false; _store.Seed(item);
			await _uow.Object.CreateOrGetConnectionAsync(); var replay = await _service.PostWithinTransactionAsync(_actor, Copy(command)); _uow.Object.CommitChanges();
			replay.TransactionIds.Should().Equal(first.TransactionIds); replay.OutboxIds.Should().Equal(first.OutboxIds).And.NotBeEmpty();
			_store.All<InventoryTransaction>().Should().HaveCount(1); _store.All<InventoryOperation>().Should().HaveCount(1); Stock(item, location).Should().Be(2);
			await _outbox.Object.DispatchAfterCommitAsync(replay.OutboxIds); _dispatches.Single().Should().Equal(first.OutboxIds);
			command.RequestId = Guid.NewGuid().ToString("D"); await _uow.Object.CreateOrGetConnectionAsync();
			await Fails(() => _service.PostWithinTransactionAsync(_actor, command), "ItemUnavailable", 409); _uow.Object.DiscardChanges();
			_store.All<InventoryTransaction>().Should().HaveCount(1); _store.All<InventoryOperation>().Should().HaveCount(1); Stock(item, location).Should().Be(2);
		}

		[Test]
		public async Task Joined_new_request_validates_every_line_before_mutating_stock_or_ledger()
		{
			var item = Item(); var location = Location();
			var command = Command(Move(item, null, location, 2, InventoryTransactionType.Receive), Move(item, location, null, 1, InventoryTransactionType.Receive));
			await _uow.Object.CreateOrGetConnectionAsync();
			await Fails(() => _service.PostWithinTransactionAsync(_actor, command), "InvalidMovement", 400);
			_store.All<InventoryStock>().Should().BeEmpty(); _store.All<InventoryTransaction>().Should().BeEmpty(); _events.Should().BeEmpty();
			_uow.Object.DiscardChanges(); _store.All<InventoryOperation>().Should().BeEmpty();
		}

		[Test]
		public async Task Joined_controlled_post_requires_the_independent_witness_path()
		{
			var item = Item(controlled: true); var location = Location();
			await _uow.Object.CreateOrGetConnectionAsync();
			await Fails(() => _service.PostWithinTransactionAsync(_actor, Receive(item, location, 2)), "IndependentWitnessRequired", 409);
			_store.All<InventoryStock>().Should().BeEmpty(); _store.All<InventoryTransaction>().Should().BeEmpty(); _store.All<InventoryOperation>().Should().BeEmpty(); _events.Should().BeEmpty();
			_uow.Object.DiscardChanges();
		}

		[TestCase(false)]
		[TestCase(true)]
		public async Task Work_order_closure_preserves_authorized_retries_but_blocks_new_movements(bool joined)
		{
			var order = new WorkOrder { Id = 31, DepartmentId = Department, Status = (int)WorkOrderStatus.InProgress };
			_workOrders.Setup(s => s.GetAsync<WorkOrder>(Department, order.Id, false)).ReturnsAsync(() => order);
			_workOrderAuth.Setup(a => a.CanContributeAsync(It.IsAny<ChecklistActor>(), order)).ReturnsAsync(true);
			var item = Item(); var location = Location(); var command = Receive(item, location, 2);
			command.Lines[0].ReferenceType = InventoryReferenceType.WorkOrder; command.Lines[0].ReferenceId = order.Id.ToString();
			async Task<InventoryResult> Post(InventoryCommand value)
			{
				if (!joined) return await _service.PostTransactionAsync(_actor, value);
				await _uow.Object.CreateOrGetConnectionAsync();
				try { var result = await _service.PostWithinTransactionAsync(_actor, value); _uow.Object.CommitChanges(); return result; }
				catch { _uow.Object.DiscardChanges(); throw; }
			}
			var first = await Post(command); order.Status = (int)WorkOrderStatus.Closed;
			var replay = await Post(Copy(command)); replay.TransactionIds.Should().Equal(first.TransactionIds); replay.OutboxIds.Should().Equal(first.OutboxIds);
			var newRequest = Copy(command); newRequest.RequestId = Guid.NewGuid().ToString("D"); await Fails(() => Post(newRequest), "ReferenceClosed", 409);
			_workOrderAuth.Setup(a => a.CanContributeAsync(It.IsAny<ChecklistActor>(), order)).ReturnsAsync(false);
			await Fails(() => Post(Copy(command)), "ReferenceUnavailable", 404);
			Stock(item, location).Should().Be(2); _store.All<InventoryTransaction>().Should().HaveCount(1); _store.All<InventoryOperation>().Should().HaveCount(1); _events.Should().HaveCount(1);
		}

		[Test]
		public async Task Owner_rollback_removes_joined_posting_and_outer_commands_refuse_nested_ownership()
		{
			var item = Item(); var location = Location(); SeedStock(item, location, 3); var command = Command(Move(item, location, null, 1, InventoryTransactionType.Consume));
			await ((Func<Task>)(() => _service.PostWithinTransactionAsync(_actor, command))).Should().ThrowAsync<InvalidOperationException>();
			await _uow.Object.CreateOrGetConnectionAsync();
			await ((Func<Task>)(() => _service.PostTransactionAsync(_actor, command))).Should().ThrowAsync<InvalidOperationException>();
			_transaction.Should().NotBeNull(); await _service.PostWithinTransactionAsync(_actor, command); _uow.Object.DiscardChanges();
			Stock(item, location).Should().Be(3); _store.All<InventoryTransaction>().Should().BeEmpty(); _store.All<InventoryOperation>().Should().BeEmpty();
			_events.Should().BeEmpty(); _dispatches.Should().BeEmpty();
		}

		[Test]
		public async Task Controlled_post_waits_for_independent_attestation_and_retry_never_duplicates_movement()
		{
			var item = Item(controlled: true); var location = Location(); var command = Receive(item, location, 5);
			var pending = await _service.PostTransactionAsync(_actor, command); pending.AwaitingWitness.Should().BeTrue();
			Stock(item, location).Should().Be(0); _events.Should().BeEmpty(); _store.All<InventoryTransaction>().Should().BeEmpty();
			await Fails(() => _service.WitnessAsync(_actor, command.RequestId, "Count independently verified"), "IndependentWitnessRequired", 409);
			var witness = new InventoryActor { DepartmentId = Department, UserId = "witness", GrantToken = "synthetic-witness-grant" };
			var result = await _service.WitnessAsync(witness, command.RequestId, "Count independently verified");
			(await _service.WitnessAsync(witness, command.RequestId, "Count independently verified")).TransactionIds.Should().Equal(result.TransactionIds);
			result.AwaitingWitness.Should().BeFalse(); Stock(item, location).Should().Be(5);
			var transaction = _store.All<InventoryTransaction>().Single(); transaction.CreatedBy.Should().Be(_actor.UserId);
			var content = JObject.Parse(transaction.Content); content.Value<string>("WitnessUserId").Should().Be(witness.UserId);
			content.Value<string>("PerformerId").Should().Be(_actor.UserId);
			_events.Count(e => e.Trigger == WorkflowTriggerEventType.ControlledSubstanceRecorded).Should().Be(1);
			_auth.Verify(x => x.RequireAsync(It.Is<InventoryActor>(a => a.UserId == _actor.UserId && a.GrantToken == null), true, PermissionTypes.ManageControlledSubstances, null), Times.AtLeastOnce);
			JsonConvert.SerializeObject(_events).Should().NotContain(Canary).And.NotContain("synthetic-witness-grant").And.NotContain("Count independently verified");
		}

		[Test]
		public async Task Revoked_performer_permission_blocks_pending_controlled_movement()
		{
			var item = Item(controlled: true); var location = Location(); var command = Receive(item, location, 2);
			await _service.PostTransactionAsync(_actor, command);
			_auth.Setup(x => x.RequireAsync(It.Is<InventoryActor>(a => a.UserId == "manager" && a.GrantToken == null), true, PermissionTypes.ManageControlledSubstances, null))
				.ThrowsAsync(new InventoryException(403, "PermissionDenied"));
			await Fails(() => _service.WitnessAsync(new InventoryActor { DepartmentId = Department, UserId = "witness" }, command.RequestId, "Verified"), "PermissionDenied", 403);
			_store.All<InventoryOperation>().Single().State.Should().Be(1); Stock(item, location).Should().Be(0); _events.Should().BeEmpty();
		}

		[Test]
		public async Task Denied_protection_preflight_read_or_ledger_encryption_never_commits_partial_data()
		{
			var item = Item(); var location = Location(); SeedStock(item, location, 4); var command = Command(Move(item, location, null, 1, InventoryTransactionType.Consume));
			_write.SetReturnsDefault(Task.FromResult(new ProtectedWriteResult { Success = false }));
			await Fails(() => _service.PostTransactionAsync(_actor, command), "ProtectedDataRequired", 403);
			_write.SetReturnsDefault(Task.FromResult(ProtectedWriteResult.Allowed()));
			_read.SetReturnsDefault(Task.FromResult(new ProtectedReadResult { IsProtected = true, RedactedFields = new() { "inventoryitems.content" } }));
			await Fails(() => _service.PostTransactionAsync(_actor, command), "ProtectedDataRequired", 403);
			_read.SetReturnsDefault(Task.FromResult(new ProtectedReadResult()));
			_write.Setup(x => x.PrepareRecordsEntityWriteAsync(Department, It.IsAny<InventoryTransaction>(), It.IsAny<InventoryTransaction>(), It.IsAny<string>(),
				It.IsAny<IReadOnlyDictionary<string, (Func<InventoryTransaction, string> Get, Action<InventoryTransaction, string> Set)>>(), It.IsAny<Action>(),
				It.IsAny<string>(), It.IsAny<string>(), false, It.IsAny<CancellationToken>())).ReturnsAsync(new ProtectedWriteResult { Success = false });
			await Fails(() => _service.PostTransactionAsync(_actor, command), "ProtectedDataRequired", 403);
			Stock(item, location).Should().Be(4); _store.All<InventoryTransaction>().Should().BeEmpty(); _store.All<InventoryOperation>().Should().BeEmpty();
			_events.Should().BeEmpty(); _audits.Should().BeEmpty(); _dispatches.Should().BeEmpty(); _uow.Verify(x => x.CommitChanges(), Times.Never);
		}

		[Test]
		public async Task Cross_department_and_holder_scope_are_rechecked_for_reads_writes_and_receipt_retries()
		{
			var item = Item(); var location = Location(); var command = Receive(item, location, 2); await _service.PostTransactionAsync(_actor, command);
			await Fails(() => _service.GetAsync<InventoryItem>(new InventoryActor { DepartmentId = 78, UserId = "manager" }, item.Id), "Unavailable", 404);
			_deniedLocations.Add(location.Id);
			await Fails(() => _service.PostTransactionAsync(_actor, command), "LocationUnavailable", 404);
			(await _service.ListAsync<InventoryStock>(_actor)).Items.Should().BeEmpty();
			Stock(item, location).Should().Be(2); _events.Should().HaveCount(1);
		}

		[Test]
		public async Task Migration_module_and_foreign_reference_guards_block_new_mutations()
		{
			var item = Item(); var location = Location(); var command = Receive(item, location, 2);
			_store.Migrated = false; await Fails(() => _service.PostTransactionAsync(_actor, command), "MigrationRequired", 409);
			_store.Migrated = true; _auth.Setup(x => x.IsEnabledAsync(Department)).ReturnsAsync(false);
			await Fails(() => _service.PostTransactionAsync(_actor, command), "InventoryDisabled", 409);
			_auth.Setup(x => x.IsEnabledAsync(Department)).ReturnsAsync(true);
			command.Lines[0].ReferenceType = InventoryReferenceType.RmsRecord; command.Lines[0].ReferenceId = Guid.NewGuid().ToString("D");
			await Fails(() => _service.PostTransactionAsync(_actor, command), "ReferenceUnsupported", 400);
			command.Lines[0].ReferenceType = InventoryReferenceType.WorkOrder; command.Lines[0].ReferenceId = "25";
			await Fails(() => _service.PostTransactionAsync(_actor, command), "ReferenceUnavailable", 404);
			_store.All<InventoryStock>().Should().BeEmpty(); _store.All<InventoryOperation>().Should().BeEmpty(); _events.Should().BeEmpty();
		}

		[Test]
		public async Task Catalog_cannot_change_tracking_after_posting_or_move_existing_location_to_another_holder()
		{
			var input = new InventoryItemInput { Details = new InventoryItemContent { Name = "Synthetic medical consumable", UnitOfMeasure = "each" } };
			var item = await _service.SaveItemAsync(_actor, input); var location = Location(); await _service.PostTransactionAsync(_actor, Receive(item, location, 1));
			input.Id = item.Id; input.Revision = (await _service.GetAsync<InventoryItem>(_actor, item.Id)).Revision; input.TrackingMode = InventoryTrackingMode.Serialized;
			await Fails(() => _service.SaveItemAsync(_actor, input), "ItemTrackingLocked", 409);
			await Fails(() => _service.SaveLocationAsync(_actor, new InventoryLocationInput { Id = location.Id, Revision = location.Revision,
				Name = "Attempted reassignment", Type = InventoryLocationType.Unit, UnitId = 101 }), "LocationHolderImmutable", 409);
			_store.All<InventoryItem>().Single().TrackingMode.Should().Be((int)InventoryTrackingMode.Bulk);
		}

		[Test]
		public async Task Lot_mismatch_expired_issue_and_precision_overflow_are_rejected_without_rewriting_stock()
		{
			var item = Item(); item.RequiresLotTracking = true; _store.Seed(item); var other = Item(); var location = Location();
			var lot = _store.Seed(new InventoryLot { DepartmentId = Department, ItemId = other.Id, ExpiresOn = _clock.Utc.AddDays(-1), Content = "{}" });
			var command = Receive(item, location, 1); command.Lines[0].LotId = lot.Id;
			await Fails(() => _service.PostTransactionAsync(_actor, command), "LotMismatch", 400);
			lot.ItemId = item.Id; _store.Seed(lot); command = Command(Move(item, location, null, 1, InventoryTransactionType.Consume)); command.Lines[0].LotId = lot.Id;
			await Fails(() => _service.PostTransactionAsync(_actor, command), "LotExpired", 409);
			command = Receive(item, location, 0.0000001m); command.Lines[0].LotId = lot.Id;
			await Fails(() => _service.PostTransactionAsync(_actor, command), "InvalidQuantity", 400);
			_store.All<InventoryStock>().Should().BeEmpty(); _store.All<InventoryTransaction>().Should().BeEmpty();
		}

		[Test]
		public async Task Kit_shortage_rolls_back_every_component_and_success_issues_exact_bill_of_materials()
		{
			var first = Item(); var second = Item(); var location = Location(); SeedStock(first, location, 5); SeedStock(second, location, 1);
			var kit = await _service.SaveKitAsync(_actor, new InventoryKitInput { Name = "Synthetic medical kit", Lines = new() {
				new InventoryKitLine { ItemId = first.Id, Quantity = 2 }, new InventoryKitLine { ItemId = second.Id, Quantity = 2 } } });
			var input = new InventoryKitIssueInput { RequestId = Guid.NewGuid().ToString("D"), KitId = kit.Id,
				Lines = new() { Issue(first, location, 2, unitId: 101), Issue(second, location, 2, unitId: 101) } };
			await Fails(() => _service.IssueKitAsync(_actor, input), "InsufficientStock", 409);
			Stock(first, location).Should().Be(5); _store.All<InventoryIssuance>().Should().BeEmpty(); _store.All<InventoryTransaction>().Should().BeEmpty();
			SeedStock(second, location, 4); var result = await _service.IssueKitAsync(_actor, input);
			result.IssuanceIds.Should().HaveCount(2); Stock(first, location).Should().Be(3); Stock(second, location).Should().Be(2);
			(await _service.IssueKitAsync(_actor, Copy(input))).IssuanceIds.Should().Equal(result.IssuanceIds);
		}

		[Test]
		public async Task Checklist_routing_and_reminder_delivery_follow_current_holder_without_protected_reads()
		{
			var item = Item(InventoryTrackingMode.Serialized); var location = Location(InventoryLocationType.Unit, 101); var asset = await CreateAsset(item, location);
			_read.Invocations.Clear(); var routing = await _service.RoutingAsync(Department, asset.Id);
			routing.UnitId.Should().Be(101); routing.GroupId.Should().Be(10); routing.Name.Should().BeNull();
			(await _service.CanReceiveReminderAsync(Department, "member", asset.Id)).Should().BeTrue(); _read.Invocations.Should().BeEmpty();
			var attended = await _service.GetAsync(ChecklistActor(), asset.Id); attended.Name.Should().Contain("serial-canary");
			_deniedLocations.Add(location.Id); (await _service.GetAsync(ChecklistActor(), asset.Id)).Should().BeNull();
			(await _service.CanReceiveReminderAsync(Department, "member", asset.Id)).Should().BeFalse();
			(await _service.RoutingAsync(78, asset.Id)).Should().BeNull();
		}

		[Test]
		public async Task Checklist_history_uses_ledger_at_call_and_preserves_label_when_current_asset_moves_or_is_retired()
		{
			var item = Item(InventoryTrackingMode.Serialized); var first = Location(InventoryLocationType.Unit, 101); var second = Location(InventoryLocationType.Unit, 102);
			var asset = await CreateAsset(item, first); var received = _store.All<InventoryTransaction>().Single();
			_clock.Advance(TimeSpan.FromMinutes(10)); var call = _clock.Utc; _clock.Advance(TimeSpan.FromMinutes(10));
			await _service.CreateAndCompleteTransferAsync(_actor, Command(AssetMove(item, asset, first, second))); var departure = _clock.Utc;
			asset = _store.All<InventoryAsset>().Single(); _clock.Advance(TimeSpan.FromMinutes(10));
			await _service.ChangeAssetStatusAsync(_actor, Status(item, asset, second, InventoryAssetStatus.Retired));
			item.Content = JsonConvert.SerializeObject(new InventoryItemContent { Name = "Current label must not rewrite history", UnitOfMeasure = "each" }); _store.Seed(item);
			_auth.Setup(x => x.IsEnabledAsync(Department)).ReturnsAsync(false);
			var snapshots = await _service.AtCallAsync(ChecklistActor(), 25, call, new[] { 101 }, false);
			var snapshot = snapshots.Single(); snapshot.AssetId.Should().Be(asset.Id); snapshot.UnitId.Should().Be(101); snapshot.SourceId.Should().Be(received.Id);
			snapshot.SourceVersion.Should().Be(received.EntryId.ToString()); snapshot.IssuedUtc.Should().Be(received.OccurredOn); snapshot.ReturnedUtc.Should().Be(departure);
			snapshot.Name.Should().Contain("serial-canary").And.NotContain("Current label");
			(await _service.AtCallAsync(ChecklistActor(), 25, call, new[] { 102 }, false)).Should().BeEmpty();
			(await _service.AtCallAsync(ChecklistActor(), 25, call, new[] { 101 }, true)).Should().BeNull();
		}

		[Test]
		public async Task Checklist_container_history_tracks_bag_movement_while_child_provenance_keeps_its_own_label()
		{
			var bagItem = Item(InventoryTrackingMode.Serialized, kit: true); var childItem = Item(InventoryTrackingMode.Serialized);
			var first = Location(InventoryLocationType.Unit, 101); var second = Location(InventoryLocationType.Unit, 102);
			var bag = await CreateAsset(bagItem, first, "bag-serial"); var bagLocation = _store.All<InventoryLocation>().Single(l => l.ContainerAssetId == bag.Id);
			var child = await CreateAsset(childItem, bagLocation, "child-serial");
			_clock.Advance(TimeSpan.FromMinutes(10)); await _service.CreateAndCompleteTransferAsync(_actor, Command(AssetMove(bagItem, bag, first, second)));
			var transfer = _store.All<InventoryTransaction>().Last(); _clock.Advance(TimeSpan.FromMinutes(10)); var call = _clock.Utc;
			var snapshot = (await _service.AtCallAsync(ChecklistActor(), 25, call, new[] { 102 }, false)).Single(s => s.AssetId == child.Id);
			snapshot.SourceId.Should().Be(transfer.Id); snapshot.SourceVersion.Should().Be(transfer.EntryId.ToString()); snapshot.Name.Should().Contain("child-serial").And.NotContain("bag-serial");
			(await _service.RoutingAsync(Department, child.Id)).UnitId.Should().Be(102);
		}

		[Test]
		public async Task Checklist_historical_snapshot_stops_at_later_terminal_status()
		{
			var item = Item(InventoryTrackingMode.Serialized); var location = Location(InventoryLocationType.Unit, 101); var asset = await CreateAsset(item, location);
			_clock.Advance(TimeSpan.FromMinutes(5)); var call = _clock.Utc; _clock.Advance(TimeSpan.FromMinutes(5));
			await _service.ChangeAssetStatusAsync(_actor, Status(item, asset, location, InventoryAssetStatus.Lost));
			var snapshot = (await _service.AtCallAsync(ChecklistActor(), 25, call, new[] { 101 }, false)).Single();
			snapshot.ReturnedUtc.Should().Be(_clock.Utc, "the asset ceased to be present when it was marked lost");
		}

		[Test]
		public async Task Controlled_transfer_waits_for_independent_witness_before_committing_both_legs_and_transfer_event()
		{
			var item = Item(controlled: true); var from = Location(); var to = Location(InventoryLocationType.Unit, 101); SeedStock(item, from, 5);
			var command = Command(Move(item, from, to, 2.125001m, InventoryTransactionType.Transfer));
			var pending = await _service.CreateAndCompleteTransferAsync(_actor, command); pending.AwaitingWitness.Should().BeTrue();
			(await _service.CreateAndCompleteTransferAsync(_actor, Copy(command))).OperationId.Should().Be(pending.OperationId);
			Stock(item, from).Should().Be(5); Stock(item, to).Should().Be(0); _store.All<InventoryTransfer>().Should().BeEmpty(); _events.Should().BeEmpty();
			var completed = await Witness(command.RequestId); completed.AwaitingWitness.Should().BeFalse(); completed.TransferId.Should().NotBeNullOrEmpty();
			Stock(item, from).Should().Be(2.874999m); Stock(item, to).Should().Be(2.125001m);
			_store.All<InventoryTransfer>().Should().HaveCount(1); _store.All<InventoryTransferItem>().Should().HaveCount(1);
			var eventCount = _events.Count; (await Witness(command.RequestId)).TransferId.Should().Be(completed.TransferId); _events.Should().HaveCount(eventCount);
			_events.Count(e => e.Trigger == WorkflowTriggerEventType.InventoryTransferCompleted).Should().Be(1);
			_events.Count(e => e.Trigger == WorkflowTriggerEventType.ControlledSubstanceRecorded).Should().Be(1);
			_store.All<InventoryTransaction>().Single().CreatedBy.Should().Be(_actor.UserId);
		}

		[TestCase(true), TestCase(false)]
		public async Task Controlled_issue_and_partial_return_each_require_witness_without_premature_issuance_changes(bool toUnit)
		{
			var item = Item(controlled: true); var source = Location(); SeedStock(item, source, 10);
			var issue = Issue(item, source, 5, unitId: toUnit ? 101 : null, userId: toUnit ? null : "member");
			(await _service.IssueAsync(_actor, issue)).AwaitingWitness.Should().BeTrue(); _store.All<InventoryIssuance>().Should().BeEmpty(); Stock(item, source).Should().Be(10); _events.Should().BeEmpty();
			var completed = await Witness(issue.RequestId); var issuance = _store.All<InventoryIssuance>().Single(); completed.IssuanceId.Should().Be(issuance.Id);
			issuance.CreatedBy.Should().Be(_actor.UserId); issuance.IssuedToUnitId.Should().Be(issue.UnitId); issuance.IssuedToUserId.Should().Be(issue.UserId);
			Stock(item, source).Should().Be(5); var returned = new InventoryReturnInput { RequestId = Guid.NewGuid().ToString("D"), IssuanceId = issuance.Id,
				Revision = issuance.Revision, ToLocationId = source.Id, Quantity = 2.125001m, Condition = InventoryAssetStatus.InService, Note = Canary };
			var priorEvents = _events.Count; (await _service.ReturnAsync(_actor, returned)).AwaitingWitness.Should().BeTrue();
			_store.All<InventoryIssuance>().Single().ReturnedQuantity.Should().Be(0); Stock(item, source).Should().Be(5); _events.Should().HaveCount(priorEvents);
			await Witness(returned.RequestId); await Witness(returned.RequestId);
			issuance = _store.All<InventoryIssuance>().Single(); issuance.ReturnedQuantity.Should().Be(2.125001m); issuance.Status.Should().Be((int)InventoryIssuanceStatus.PartiallyReturned);
			Stock(item, source).Should().Be(7.125001m); _events.Count(e => e.Trigger == WorkflowTriggerEventType.InventoryIssued).Should().Be(1);
			_events.Count(e => e.Trigger == WorkflowTriggerEventType.InventoryReturned).Should().Be(1); _events.Count(e => e.Trigger == WorkflowTriggerEventType.ControlledSubstanceRecorded).Should().Be(2);
			JsonConvert.SerializeObject(_events).Should().NotContain(Canary).And.NotContain("synthetic-witness-grant");
		}

		[Test]
		public async Task Controlled_kit_witness_commits_controlled_and_ordinary_components_as_one_operation()
		{
			var medicine = Item(controlled: true); var supplies = Item(); var source = Location(); SeedStock(medicine, source, 5); SeedStock(supplies, source, 10);
			var kit = await _service.SaveKitAsync(_actor, new InventoryKitInput { Name = "Synthetic controlled kit", Lines = new() {
				new InventoryKitLine { ItemId = medicine.Id, Quantity = 2 }, new InventoryKitLine { ItemId = supplies.Id, Quantity = 3 } } });
			var input = new InventoryKitIssueInput { RequestId = Guid.NewGuid().ToString("D"), KitId = kit.Id,
				Lines = new() { Issue(medicine, source, 2, unitId: 101), Issue(supplies, source, 3, unitId: 101) } };
			var pending = await _service.IssueKitAsync(_actor, input); pending.AwaitingWitness.Should().BeTrue(); _store.All<InventoryIssuance>().Should().BeEmpty(); _events.Should().BeEmpty();
			(await _service.IssueKitAsync(_actor, Copy(input))).OperationId.Should().Be(pending.OperationId);
			var completed = await Witness(input.RequestId); completed.IssuanceIds.Should().HaveCount(2); _store.All<InventoryTransaction>().Select(t => t.OperationId).Distinct().Should().ContainSingle();
			Stock(medicine, source).Should().Be(3); Stock(supplies, source).Should().Be(7); _store.All<InventoryIssuance>().Should().OnlyContain(i => i.CreatedBy == _actor.UserId);
			_events.Count(e => e.Trigger == WorkflowTriggerEventType.InventoryIssued).Should().Be(2); _events.Count(e => e.Trigger == WorkflowTriggerEventType.ControlledSubstanceRecorded).Should().Be(1);
		}

		[Test]
		public async Task Controlled_serialized_receipt_allocates_identity_but_is_not_available_until_witness_completes_receipt()
		{
			var item = Item(InventoryTrackingMode.Serialized, controlled: true); var location = Location();
			var input = new InventoryAssetInput { RequestId = Guid.NewGuid().ToString("D"), ItemId = item.Id, LocationId = location.Id, Details = new InventoryAssetContent { SerialNumber = "Synthetic controlled serial" } };
			var pending = await _service.CreateAssetAsync(_actor, input); pending.CurrentLocationId.Should().BeNull();
			(await _service.CreateAssetAsync(_actor, Copy(input))).Id.Should().Be(pending.Id); _store.All<InventoryAsset>().Should().HaveCount(1);
			_store.All<InventoryTransaction>().Should().BeEmpty(); _events.Should().BeEmpty(); (await _service.GetIssuableAsync(_actor)).Should().BeEmpty(); (await _service.RoutingAsync(Department, pending.Id)).Should().BeNull();
			var completed = await Witness(input.RequestId); completed.AssetId.Should().Be(pending.Id); (await Witness(input.RequestId)).TransactionIds.Should().Equal(completed.TransactionIds);
			(await _service.CreateAssetAsync(_actor, Copy(input))).CurrentLocationId.Should().Be(location.Id); _store.All<InventoryTransaction>().Should().HaveCount(1);
			(await _service.GetIssuableAsync(_actor)).Single().Asset.Id.Should().Be(pending.Id); _events.Count(e => e.Trigger == WorkflowTriggerEventType.ControlledSubstanceRecorded).Should().Be(1);
		}

		[Test]
		public async Task Controlled_transfer_witness_rechecks_available_stock_and_preserves_pending_receipt_on_failure()
		{
			var item = Item(controlled: true); var from = Location(); var to = Location(); SeedStock(item, from, 5);
			var command = Command(Move(item, from, to, 4, InventoryTransactionType.Transfer)); await _service.CreateAndCompleteTransferAsync(_actor, command);
			SeedStock(item, from, 3); await Fails(() => Witness(command.RequestId), "InsufficientStock", 409);
			Stock(item, from).Should().Be(3); Stock(item, to).Should().Be(0); _store.All<InventoryTransfer>().Should().BeEmpty(); _store.All<InventoryTransaction>().Should().BeEmpty(); _events.Should().BeEmpty();
			_store.All<InventoryOperation>().Single().State.Should().Be(1);
			SeedStock(item, from, 5); (await Witness(command.RequestId)).TransferId.Should().NotBeNullOrEmpty(); Stock(item, from).Should().Be(1);
		}

		[Test]
		public async Task Legacy_migration_preserves_signed_six_decimal_balances_unit_precedence_and_attended_encrypted_source_copies()
		{
			_store.Migrated = false;
			var type = new InventoryType { InventoryTypeId = 1, DepartmentId = Department, Type = "Synthetic old stock", Description = Canary, UnitOfMesasure = "each" };
			var sources = new[] {
				Legacy(1, 5.125001, 10), Legacy(2, -2.000001, 10), Legacy(3, 3.125001, 11, 101), Legacy(4, -0.125001, 11, 101), Legacy(5, -1.25, 0) };
			var original = JsonConvert.SerializeObject(sources);
			_legacyTypes.Setup(x => x.GetAllByDepartmentIdAsync(Department)).ReturnsAsync(new[] { type });
			_legacyInventory.Setup(x => x.GetAllInventoriesByDepartmentIdAsync(Department)).ReturnsAsync(sources);
			var key = RandomNumberGenerator.GetBytes(32); var crypto = new ProtectedFieldCryptoService();
			try
			{
				EncryptWrites<InventoryItem>(key, crypto); EncryptWrites<InventoryLocation>(key, crypto); EncryptWrites<InventoryTransaction>(key, crypto); EncryptWrites<InventoryOperation>(key, crypto);
				var migrated = await _service.MigrateLegacyAsync(_actor); migrated.Items.Should().Be(1); migrated.Transactions.Should().Be(5);
				migrated.Warnings.Should().Contain("LegacyUnitLocationTakesPrecedence:3").And.Contain("LegacyNegativeBalancesPreserved");
				var locations = _store.All<InventoryLocation>().ToList(); var item = _store.All<InventoryItem>().Single(); item.LegacyInventoryTypeId.Should().Be(1);
				Stock(item, locations.Single(l => l.GroupId == 10)).Should().Be(3.125000m); Stock(item, locations.Single(l => l.UnitId == 101)).Should().Be(3.000000m);
				Stock(item, locations.Single(l => l.IsDefault)).Should().Be(-1.25m); locations.Single(l => l.UnitId == 101).GroupId.Should().BeNull();
				_store.All<InventoryTransaction>().Should().OnlyContain(t => t.IsProtected && t.Content.StartsWith("rgdp:") && !t.Content.Contains(Canary));
				var consumption = _store.All<InventoryTransaction>().Single(t => t.LegacyInventoryId == 2); consumption.Quantity.Should().Be(2.000001m); consumption.ToLocationId.Should().BeNull();
				var decrypted = JObject.Parse(crypto.DecryptText(key, consumption.Content, Department, "inventorytransactions.content", consumption.Id));
				decrypted["LegacySource"].Value<double>("Amount").Should().Be(-2.000001); decrypted["LegacySource"].Value<string>("Note").Should().Be(Canary);
				JsonConvert.SerializeObject(sources).Should().Be(original); _events.Should().BeEmpty();
				(await _service.MigrateLegacyAsync(_actor)).AlreadyMigrated.Should().BeTrue(); _store.All<InventoryTransaction>().Should().HaveCount(5);
				_store.All<InventoryOperation>().Single().RequestId.Should().Be("00000000-0000-0000-0000-000000000001");
			}
			finally { CryptographicOperations.ZeroMemory(key); }
		}

		[Test]
		public async Task Legacy_migration_refuses_unrepresentable_quantities_and_rolls_back_when_protected_copy_fails()
		{
			_store.Migrated = false; _legacyTypes.Setup(x => x.GetAllByDepartmentIdAsync(Department)).ReturnsAsync(new[] { new InventoryType { InventoryTypeId = 1, DepartmentId = Department, Type = "Synthetic legacy", UnitOfMesasure = "each" } });
			_legacyInventory.Setup(x => x.GetAllInventoriesByDepartmentIdAsync(Department)).ReturnsAsync(new[] { Legacy(1, 0.1234567, 10) });
			await Fails(() => _service.MigrateLegacyAsync(_actor), "LegacyInventoryQuantityRequiresReview:1", 409);
			_store.All<InventoryItem>().Should().BeEmpty();
			_legacyInventory.Setup(x => x.GetAllInventoriesByDepartmentIdAsync(Department)).ReturnsAsync(new[] { Legacy(1, 1.125001, 10) });
			_write.Setup(x => x.PrepareRecordsEntityWriteAsync(Department, It.IsAny<InventoryTransaction>(), It.IsAny<InventoryTransaction>(), It.IsAny<string>(),
				It.IsAny<IReadOnlyDictionary<string, (Func<InventoryTransaction, string> Get, Action<InventoryTransaction, string> Set)>>(), It.IsAny<Action>(),
				It.IsAny<string>(), It.IsAny<string>(), false, It.IsAny<CancellationToken>())).ReturnsAsync(ProtectedWriteResult.Blocked("synthetic_broker_unavailable"));
			await Fails(() => _service.MigrateLegacyAsync(_actor), "ProtectedDataRequired", 403);
			_store.All<InventoryItem>().Should().BeEmpty(); _store.All<InventoryLocation>().Should().BeEmpty(); _store.All<InventoryTransaction>().Should().BeEmpty(); _store.All<InventoryOperation>().Should().BeEmpty();
			(await _service.IsMigratedAsync(Department)).Should().BeFalse(); _events.Should().BeEmpty(); _dispatches.Should().BeEmpty();
		}

		private Task<InventoryResult> Witness(string requestId) => _service.WitnessAsync(new InventoryActor { DepartmentId = Department, UserId = "witness", GrantToken = "synthetic-witness-grant" }, requestId, "Synthetic independent count verified");
		private Inventory Legacy(int id, double quantity, int group, int? unit = null) => new() { InventoryId = id, DepartmentId = Department, TypeId = 1, GroupId = group, UnitId = unit, Amount = quantity,
			AddedByUserId = "legacy-author", TimeStamp = _clock.Utc.AddDays(-5).AddMinutes(id), Note = Canary, Batch = "Synthetic legacy batch", Location = "Synthetic shelf" };
		private void EncryptWrites<T>(byte[] key, ProtectedFieldCryptoService crypto) where T : InventoryRow
		{
			_write.Setup(x => x.PrepareRecordsEntityWriteAsync(Department, It.IsAny<T>(), It.IsAny<T>(), It.IsAny<string>(),
				It.IsAny<IReadOnlyDictionary<string, (Func<T, string> Get, Action<T, string> Set)>>(), It.IsAny<Action>(), It.IsAny<string>(), It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, T row, T previous, string id, IReadOnlyDictionary<string, (Func<T, string> Get, Action<T, string> Set)> fields, Action mark, string grant, string user, bool workload, CancellationToken ct) =>
				{
					grant.Should().Be(_actor.GrantToken); user.Should().Be(_actor.UserId); workload.Should().BeFalse();
					foreach (var field in fields) field.Value.Set(row, crypto.EncryptText(key, 1, field.Value.Get(row), d, field.Key, id));
					mark?.Invoke(); return ProtectedWriteResult.Allowed(true, true);
				});
		}
		private static T Copy<T>(T value) => JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value));
		private static async Task Fails(Func<Task> action, string code, int status)
		{
			var error = (await action.Should().ThrowAsync<InventoryException>()).Which; error.Code.Should().Be(code); error.StatusCode.Should().Be(status);
		}
		private ChecklistActor ChecklistActor() => new() { DepartmentId = _actor.DepartmentId, UserId = _actor.UserId, GrantToken = _actor.GrantToken };
		private InventoryItem Item(InventoryTrackingMode tracking = InventoryTrackingMode.Bulk, bool controlled = false, bool kit = false) => _store.Seed(new InventoryItem {
			DepartmentId = Department, CreatedBy = _actor.UserId, CreatedOn = _clock.Utc, TrackingMode = (int)tracking, IsControlledSubstance = controlled, IsKit = kit,
			Content = JsonConvert.SerializeObject(new InventoryItemContent { Name = "Synthetic equipment " + Guid.NewGuid().ToString("N"), UnitOfMeasure = "each", Description = Canary, DefaultUnitCost = 2.25m }) });
		private InventoryLocation Location(InventoryLocationType type = InventoryLocationType.Facility, int? unitId = null) => _store.Seed(new InventoryLocation {
			DepartmentId = Department, LocationType = (int)type, UnitId = unitId, CreatedBy = _actor.UserId, CreatedOn = _clock.Utc, Content = JsonConvert.SerializeObject(new InventoryLabel { Name = "Synthetic storage" }) });
		private void SeedStock(InventoryItem item, InventoryLocation location, decimal quantity)
		{
			var stock = _store.All<InventoryStock>().SingleOrDefault(s => s.ItemId == item.Id && s.LocationId == location.Id && s.LotId == null)
				?? new InventoryStock { DepartmentId = Department, ItemId = item.Id, LocationId = location.Id };
			stock.Quantity = quantity; _store.Seed(stock);
		}
		private decimal Stock(InventoryItem item, InventoryLocation location) => _store.All<InventoryStock>().SingleOrDefault(s => s.ItemId == item.Id && s.LocationId == location.Id && s.LotId == null)?.Quantity ?? 0;
		private static InventoryCommand Command(params InventoryPosting[] lines) => new() { RequestId = Guid.NewGuid().ToString("D"), Lines = lines.ToList() };
		private static InventoryPosting Move(InventoryItem item, InventoryLocation from, InventoryLocation to, decimal quantity, InventoryTransactionType type) => new() {
			ItemId = item.Id, FromLocationId = from?.Id, ToLocationId = to?.Id, Quantity = quantity, Type = type, Note = Canary };
		private static InventoryCommand Receive(InventoryItem item, InventoryLocation location, decimal quantity) => Command(Move(item, null, location, quantity, InventoryTransactionType.Receive));
		private static InventoryPosting AssetMove(InventoryItem item, InventoryAsset asset, InventoryLocation from, InventoryLocation to) => new() {
			ItemId = item.Id, AssetId = asset.Id, FromLocationId = from.Id, ToLocationId = to.Id, Quantity = 1, Type = InventoryTransactionType.Transfer };
		private static InventoryCommand Status(InventoryItem item, InventoryAsset asset, InventoryLocation location, InventoryAssetStatus status) => Command(new InventoryPosting {
			ItemId = item.Id, AssetId = asset.Id, FromLocationId = location.Id, Quantity = 0, Type = InventoryTransactionType.StatusChange, Status = status, ExpectedAssetRevision = asset.Revision });
		private static InventoryIssueInput Issue(InventoryItem item, InventoryLocation location, decimal quantity, int? unitId = null, string userId = null, string assetId = null) => new() {
			RequestId = Guid.NewGuid().ToString("D"), ItemId = item.Id, FromLocationId = location.Id, Quantity = quantity, UnitId = unitId, UserId = userId, AssetId = assetId, Note = Canary };
		private Task<InventoryAsset> CreateAsset(InventoryItem item, InventoryLocation location, string serial = "serial-canary") => _service.CreateAssetAsync(_actor, new InventoryAssetInput {
			RequestId = Guid.NewGuid().ToString("D"), ItemId = item.Id, LocationId = location.Id, Details = new InventoryAssetContent { SerialNumber = serial, AcquisitionCost = 25m } });
		private void Begin()
		{
			_transaction.Should().BeNull(); _store.Begin(); _transaction = new Mock<DbTransaction>().Object; _eventsBefore = _events.Count; _auditsBefore = _audits.Count;
		}
		private void Rollback()
		{
			_store.Rollback(); _events.RemoveRange(_eventsBefore, _events.Count - _eventsBefore); _audits.RemoveRange(_auditsBefore, _audits.Count - _auditsBefore); _transaction = null;
		}
		private sealed class TestClock : TimeProvider
		{
			public DateTime Utc { get; private set; } = new(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc);
			public override DateTimeOffset GetUtcNow() => new(Utc);
			public void Advance(TimeSpan by) => Utc += by;
		}

		/// <summary>Detached reads and rollback snapshots exercise transaction ownership; this is not a substitute for dialect database tests.</summary>
		private sealed class Store : IInventoryStore
		{
			private Dictionary<Type, List<InventoryRow>> _rows = new();
			private Dictionary<Type, List<InventoryRow>> _before;
			private long _entrySequence;
			public bool Migrated { get; set; } = true;
			public IEnumerable<T> All<T>() where T : InventoryRow => _rows.TryGetValue(typeof(T), out var rows) ? rows.Cast<T>().Select(Copy).ToList() : Enumerable.Empty<T>();
			public T Seed<T>(T row) where T : InventoryRow
			{
				if (!_rows.TryGetValue(typeof(T), out var rows)) _rows[typeof(T)] = rows = new();
				var index = rows.FindIndex(r => r.DepartmentId == row.DepartmentId && r.Id == row.Id);
				if (index < 0) rows.Add(Copy(row)); else rows[index] = Copy(row); return Copy(row);
			}
			public void Begin() => _before = _rows.ToDictionary(p => p.Key, p => p.Value.Select(v => (InventoryRow)JsonConvert.DeserializeObject(JsonConvert.SerializeObject(v), p.Key)).ToList());
			public void Commit() => _before = null;
			public void Rollback() { if (_before != null) _rows = _before; _before = null; }
			private void RequireTransaction() { if (_before == null) throw new InvalidOperationException("Mutation requires an owning transaction."); }
			public Task LockDepartmentAsync(int departmentId) { RequireTransaction(); return Task.CompletedTask; }
			public Task<List<int>> AlertDepartmentsAsync(int afterDepartmentId) => Task.FromResult(All<InventoryItem>().Select(i => i.DepartmentId).Distinct().Where(i => i > afterDepartmentId).OrderBy(i => i).Take(100).ToList());
			public Task<long> LastEntryAsync(int departmentId) => Task.FromResult(All<InventoryTransaction>().Where(t => t.DepartmentId == departmentId).Select(t => t.EntryId).DefaultIfEmpty().Max());
			public Task<InventoryAlert> OpenAlertAsync(int departmentId, string dedupKey) => Task.FromResult(All<InventoryAlert>().SingleOrDefault(a => a.DepartmentId == departmentId && a.DedupKey == dedupKey && a.Status == 0));
			public Task<InventoryAlertDelivery> AlertDeliveryAsync(int departmentId, string alertId, string userId) => Task.FromResult(All<InventoryAlertDelivery>().SingleOrDefault(d => d.DepartmentId == departmentId && d.AlertId == alertId && d.UserId == userId));
			public Task<List<InventoryAlert>> OpenAlertsAsync(int departmentId, int skip = 0) => Task.FromResult(All<InventoryAlert>().Where(a => a.DepartmentId == departmentId && a.Status == 0).OrderBy(a => a.OpenedOn).ThenBy(a => a.Id).Skip(skip).Take(501).ToList());
			public Task<List<InventoryAlert>> ClaimableAlertsAsync(int departmentId, string userId, DateTime now, int skip = 0) => Task.FromResult(All<InventoryAlert>()
				.Where(a => a.DepartmentId == departmentId && a.Status == 0 && !All<InventoryAlertDelivery>().Any(d => d.DepartmentId == departmentId && d.AlertId == a.Id && d.UserId == userId
					&& (d.State is 2 or 3 || d.NextAttemptOn > now || d.LeaseUntil > now))).OrderBy(a => a.OpenedOn).ThenBy(a => a.Id).Skip(skip).Take(501).ToList());
			public Task<List<T>> RelatedManyAsync<T>(int departmentId, string column, IReadOnlyCollection<string> ids) where T : InventoryRow
				=> Task.FromResult(All<T>().Where(r => r.DepartmentId == departmentId && ids.Contains((string)typeof(T).GetProperty(column).GetValue(r))).OrderBy(r => r.Id).ToList());
			public Task<T> GetAsync<T>(int departmentId, string id) where T : InventoryRow => Task.FromResult(All<T>().SingleOrDefault(r => r.DepartmentId == departmentId && r.Id == id));
			public Task<List<T>> ListAsync<T>(int departmentId, int skip = 0) where T : InventoryRow => Task.FromResult(All<T>().Where(r => r.DepartmentId == departmentId).OrderBy(r => r.CreatedOn).ThenBy(r => r.Id).Skip(skip).Take(501).ToList());
			public Task<List<InventoryStockQuantity>> StockQuantitiesAsync(int departmentId, IReadOnlyCollection<string> itemIds) => Task.FromResult(All<InventoryStock>()
				.Where(s => s.DepartmentId == departmentId && !s.IsDeleted && itemIds.Contains(s.ItemId)).GroupBy(s => new { s.ItemId, s.LocationId })
				.Select(g => new InventoryStockQuantity { ItemId = g.Key.ItemId, LocationId = g.Key.LocationId, Quantity = g.Sum(s => s.Quantity) }).ToList());
			public Task<List<InventoryTransaction>> AssetHistoryAsync(int departmentId, IReadOnlyCollection<string> assetIds, DateTime at, bool after, int skip = 0) => Task.FromResult(All<InventoryTransaction>()
				.Where(t => t.DepartmentId == departmentId && assetIds.Contains(t.AssetId) && (after ? t.OccurredOn > at : t.OccurredOn <= at))
				.OrderBy(t => t.OccurredOn).ThenBy(t => t.EntryId).Skip(skip).Take(501).ToList());
			public Task<List<T>> QueryAsync<T>(int departmentId, InventoryQuery filter, int skip = 0) where T : InventoryRow
			{
				var rows = All<T>().Where(r => r.DepartmentId == departmentId && (r is not InventoryMutableRow mutable || !mutable.IsDeleted));
				foreach (var field in new[] { ("ItemId", filter.ItemId), ("AssetId", filter.AssetId), ("IssuedToUserId", filter.IssuedToUserId), ("KitId", filter.KitId) }.Where(x => x.Item2 != null))
					rows = rows.Where(r => (string)typeof(T).GetProperty(field.Item1).GetValue(r) == field.Item2);
				if (filter.LocationId != null) rows = rows.Where(r => r is InventoryTransaction t ? t.FromLocationId == filter.LocationId || t.ToLocationId == filter.LocationId : (string)typeof(T).GetProperty("LocationId").GetValue(r) == filter.LocationId);
				return Task.FromResult((typeof(T) == typeof(InventoryTransaction) ? rows.OrderByDescending(r => ((InventoryTransaction)(InventoryRow)r).EntryId) : rows.OrderBy(r => r.Id)).Skip(skip).Take(501).ToList());
			}
			public Task<List<T>> RelatedAsync<T>(int departmentId, string column, string id) where T : InventoryRow => Task.FromResult(All<T>().Where(r => r.DepartmentId == departmentId && (string)typeof(T).GetProperty(column).GetValue(r) == id).ToList());
			public Task InsertAsync<T>(T row) where T : InventoryRow
			{
				RequireTransaction(); if (All<T>().Any(r => r.DepartmentId == row.DepartmentId && r.Id == row.Id)) throw new InvalidOperationException("Duplicate inventory identity.");
				if (row is InventoryOperation op && All<InventoryOperation>().Any(r => r.DepartmentId == op.DepartmentId && r.RequestId == op.RequestId)) throw new InventoryException(409, "RequestConflict");
				if (row is InventoryTransaction transaction) transaction.EntryId = ++_entrySequence;
				Seed(row); return Task.CompletedTask;
			}
			public Task UpdateAsync<T>(T row, int expectedRevision) where T : InventoryRow
			{
				RequireTransaction(); if (row is InventoryTransaction or InventoryTransferItem or RecordInventoryUsage) throw new InvalidOperationException("Ledger identities are immutable.");
				var stored = All<T>().SingleOrDefault(r => r.DepartmentId == row.DepartmentId && r.Id == row.Id);
				if (stored == null || stored.Revision != expectedRevision || row.Revision != expectedRevision + 1) throw new InventoryException(409, "RevisionConflict");
				Seed(row); return Task.CompletedTask;
			}
			public Task<InventoryOperation> RequestAsync(int departmentId, string requestId) => Task.FromResult(All<InventoryOperation>().SingleOrDefault(r => r.DepartmentId == departmentId && r.RequestId == requestId));
			public Task<InventoryStock> ApplyStockDeltaAsync(int departmentId, string itemId, string locationId, string lotId, decimal delta, string userId)
			{
				RequireTransaction(); var stock = All<InventoryStock>().SingleOrDefault(r => r.DepartmentId == departmentId && r.ItemId == itemId && r.LocationId == locationId && r.LotId == lotId)
					?? new InventoryStock { DepartmentId = departmentId, ItemId = itemId, LocationId = locationId, LotId = lotId, CreatedBy = userId };
				stock.Quantity += delta; stock.Revision++; return Task.FromResult(Seed(stock));
			}
			public Task<InventoryItem> LegacyItemAsync(int departmentId, int typeId) => Task.FromResult(All<InventoryItem>().SingleOrDefault(r => r.DepartmentId == departmentId && r.LegacyInventoryTypeId == typeId));
			public Task<InventoryTransaction> LegacyTransactionAsync(int departmentId, int inventoryId) => Task.FromResult(All<InventoryTransaction>().SingleOrDefault(r => r.DepartmentId == departmentId && r.LegacyInventoryId == inventoryId));
			public async Task RebuildStocksAsync(int departmentId)
			{
				RequireTransaction(); if (_rows.TryGetValue(typeof(InventoryStock), out var stocks)) stocks.RemoveAll(r => r.DepartmentId == departmentId);
				foreach (var transaction in All<InventoryTransaction>().Where(t => t.DepartmentId == departmentId && t.AssetId == null).OrderBy(t => t.EntryId))
				{
					if (transaction.FromLocationId != null) await ApplyStockDeltaAsync(departmentId, transaction.ItemId, transaction.FromLocationId, transaction.LotId, -transaction.Quantity, transaction.CreatedBy);
					if (transaction.ToLocationId != null) await ApplyStockDeltaAsync(departmentId, transaction.ItemId, transaction.ToLocationId, transaction.LotId, transaction.Quantity, transaction.CreatedBy);
				}
			}
			public Task<bool> HasLegacyMigrationAsync(int departmentId) => Task.FromResult(departmentId == Department && (Migrated || All<InventoryOperation>().Any(o => o.DepartmentId == departmentId && o.RequestId == "00000000-0000-0000-0000-000000000001" && o.State == 2)));
		}
	}
}
