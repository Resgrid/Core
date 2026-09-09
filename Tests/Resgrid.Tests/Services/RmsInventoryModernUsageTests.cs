using System;
using System.Collections.Generic;
using System.Data.Common;
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
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public sealed partial class RmsInventoryModernUsageTests
	{
		private const int Department = 77;
		private const string RecordId = "11111111-1111-1111-1111-111111111111";
		private const string ItemId = "22222222-2222-2222-2222-222222222222";
		private const string LocationId = "33333333-3333-3333-3333-333333333333";
		private const string Canary = "SYNTHETIC-RECORD-INVENTORY-PHI-CANARY";
		private readonly InventoryActor _actor = new() { DepartmentId = Department, UserId = "author", GrantToken = "synthetic-current-grant" };
		private Mock<IRmsExternalReferencesRepository> _referencesRepo;
		private Mock<IRmsOperationalRecordsRepository> _records;
		private Mock<IRmsIncidentReportsRepository> _incidents;
		private Mock<IRmsAccessAuditsRepository> _auditRepo;
		private Mock<IInventoryStore> _modernStore;
		private Mock<IInventoryStockService> _stock;
		private Mock<IInventoryCatalogService> _catalog;
		private Mock<IInventoryService> _legacy;
		private Mock<IRecordsAuthorizationService> _authorization;
		private Mock<IUnitOfWork> _uow;
		private Mock<IDomainEventOutboxService> _outbox;
		private RmsInventoryUsageAdapter _adapter;
		private RmsOperationalRecord _record;
		private RmsIncidentReport _incident;
		private List<RmsExternalReference> _references;
		private List<RmsAccessAudit> _audits;
		private List<InventoryTransaction> _ledger;
		private List<RecordInventoryUsage> _usageRows;
		private List<(InventoryActor Actor, InventoryCommand Command)> _posts;
		private List<List<long>> _dispatches;
		private DbTransaction _transaction;
		private Action _rollback;
		private decimal _balance;
		private bool _migrated;

		[SetUp]
		public void SetUp()
		{
			_references = new(); _audits = new(); _ledger = new(); _usageRows = new(); _posts = new(); _dispatches = new(); _balance = 10; _transaction = null; _migrated = true;
			_record = new RmsOperationalRecord { RmsOperationalRecordId = RecordId, DepartmentId = Department, AuthorUserId = "author", State = (int)RmsRecordState.Draft, RowVersion = 1 };
			_incident = new RmsIncidentReport { RmsIncidentReportId = RecordId, DepartmentId = Department, AuthorUserId = "author", State = (int)RmsRecordState.Draft, RowVersion = 1 };
			_records = new(); _incidents = new(); _referencesRepo = new(); _auditRepo = new(); _authorization = new(); _modernStore = new(); _stock = new(); _catalog = new(); _legacy = new(); _uow = new(); _outbox = new();
			_records.Setup(r => r.GetByIdForDepartmentAsync(Department, RecordId)).ReturnsAsync(() => Copy(_record));
			_incidents.Setup(r => r.GetByIdForDepartmentAsync(Department, RecordId)).ReturnsAsync(() => Copy(_incident));
			_records.Setup(r => r.TryBumpRowVersionAsync(Department, RecordId, It.IsAny<long>(), It.IsAny<CancellationToken>())).ReturnsAsync((int d, string id, long version, CancellationToken ct) =>
			{ _transaction.Should().NotBeNull(); if (_record.RowVersion != version) return false; _record.RowVersion++; return true; });
			_incidents.Setup(r => r.TryBumpRowVersionAsync(Department, RecordId, It.IsAny<long>(), It.IsAny<CancellationToken>())).ReturnsAsync((int d, string id, long version, CancellationToken ct) =>
			{ _transaction.Should().NotBeNull(); if (_incident.RowVersion != version) return false; _incident.RowVersion++; return true; });
			_authorization.Setup(a => a.CanUserViewRecordAsync("author", RecordId, Department)).ReturnsAsync(true);
			_authorization.Setup(a => a.HasPermissionAsync("author", Department, It.IsAny<PermissionTypes>())).ReturnsAsync(true);
			_authorization.Setup(a => a.CanUseSourceInventoryAsync("author", Department, It.IsAny<int?>())).ReturnsAsync(true);
			_referencesRepo.Setup(r => r.GetByIdAsync(It.IsAny<object>())).ReturnsAsync((object id) => Copy(_references.SingleOrDefault(r => r.RmsExternalReferenceId == id.ToString())));
			_referencesRepo.Setup(r => r.GetForRecordAsync(Department, RecordId)).ReturnsAsync(() => _references.Select(Copy).ToList());
			_referencesRepo.Setup(r => r.InsertAsync(It.IsAny<RmsExternalReference>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((RmsExternalReference r, CancellationToken c, bool f) =>
			{ _transaction.Should().NotBeNull(); _references.Add(Copy(r)); return r; });
			_auditRepo.Setup(a => a.InsertAsync(It.IsAny<RmsAccessAudit>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((RmsAccessAudit a, CancellationToken c, bool f) =>
			{ _transaction.Should().NotBeNull(); _audits.Add(Copy(a)); return a; });
			var item = new InventoryItem { DepartmentId = Department, Id = ItemId, LegacyInventoryTypeId = 1,
				Content = JsonConvert.SerializeObject(new InventoryItemContent { Name = Canary, UnitOfMeasure = Canary }) };
			var location = new InventoryLocation { Id = LocationId, DepartmentId = Department, LocationType = (int)InventoryLocationType.Station, GroupId = 7 };
			_catalog.Setup(c => c.GetAsync<InventoryItem>(It.IsAny<InventoryActor>(), ItemId)).ReturnsAsync(() => Copy(item));
			_catalog.Setup(c => c.GetAsync<InventoryLocation>(It.IsAny<InventoryActor>(), LocationId)).ReturnsAsync(() => Copy(location));
			_catalog.Setup(c => c.GetAsync<InventoryTransaction>(It.IsAny<InventoryActor>(), It.IsAny<string>())).ReturnsAsync((InventoryActor a, string id) => Copy(_ledger.SingleOrDefault(t => t.DepartmentId == a.DepartmentId && t.Id == id)));
			_catalog.Setup(c => c.ListAsync<InventoryLocation>(It.IsAny<InventoryActor>(), 0)).ReturnsAsync(new InventoryPage<InventoryLocation> { Items = new() { location } });
			_modernStore.Setup(s => s.HasLegacyMigrationAsync(Department)).ReturnsAsync(() => _migrated);
			_modernStore.Setup(s => s.LockDepartmentAsync(Department)).Returns(() => { _transaction.Should().NotBeNull(); return Task.CompletedTask; });
			_modernStore.Setup(s => s.LegacyItemAsync(Department, 1)).ReturnsAsync(() => Copy(item));
			_modernStore.Setup(s => s.GetAsync<InventoryTransaction>(Department, It.IsAny<string>())).ReturnsAsync((int d, string id) => Copy(_ledger.SingleOrDefault(t => t.Id == id && t.DepartmentId == d)));
			_modernStore.Setup(s => s.GetAsync<InventoryLocation>(Department, LocationId)).ReturnsAsync(() => Copy(location));
			_modernStore.Setup(s => s.GetAsync<RecordInventoryUsage>(Department, It.IsAny<string>())).ReturnsAsync((int d, string id) => Copy(_usageRows.SingleOrDefault(t => t.Id == id)));
			_modernStore.Setup(s => s.RelatedAsync<RecordInventoryUsage>(Department, It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync((int d, string col, string id) => _usageRows.Where(x => (string)typeof(RecordInventoryUsage).GetProperty(col).GetValue(x) == id).Select(Copy).ToList());
			_modernStore.Setup(s => s.RelatedAsync<InventoryTransaction>(Department, It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync((int d, string col, string id) => _ledger.Where(x => (string)typeof(InventoryTransaction).GetProperty(col).GetValue(x) == id).Select(Copy).ToList());
			_modernStore.Setup(s => s.LegacyTransactionAsync(Department, It.IsAny<int>())).ReturnsAsync((int d, int id) => Copy(_ledger.SingleOrDefault(t => t.LegacyInventoryId == id)));
			_catalog.Setup(c => c.GetAsync<RecordInventoryUsage>(It.IsAny<InventoryActor>(), It.IsAny<string>())).ReturnsAsync((InventoryActor a, string id) => Copy(_usageRows.SingleOrDefault(t => t.Id == id)));
			_stock.Setup(s => s.RecordUsageWithinTransactionAsync(It.IsAny<InventoryActor>(), It.IsAny<RecordInventoryUsage>())).Returns((InventoryActor a, RecordInventoryUsage usage) =>
			{ _transaction.Should().NotBeNull(); if (_usageRows.Any(x => x.TransactionId == usage.TransactionId)) throw new InventoryException(409, "UsageAlreadyRecorded"); _usageRows.Add(Copy(usage)); return Task.CompletedTask; });
			_stock.Setup(s => s.PostWithinTransactionAsync(It.IsAny<InventoryActor>(), It.IsAny<InventoryCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync((InventoryActor actor, InventoryCommand command, CancellationToken ct) =>
			{
				_transaction.Should().NotBeNull("Records must own the stock and reference transaction"); _posts.Add((Copy(actor), Copy(command)));
				var result = new InventoryResult();
				foreach (var line in command.Lines)
				{
				var before = _balance; _balance += line.FromLocationId != null ? -line.Quantity : line.Quantity;
				var entry = new InventoryTransaction { DepartmentId = actor.DepartmentId, EntryId = _ledger.Count + 1, OperationId = Guid.NewGuid().ToString("D"),
					ItemId = line.ItemId, AssetId = line.AssetId, LotId = line.LotId, FromLocationId = line.FromLocationId, ToLocationId = line.ToLocationId,
					Quantity = line.Quantity, FromQuantityBefore = before, FromQuantityAfter = _balance, ReferenceType = (int)line.ReferenceType, ReferenceId = line.ReferenceId,
					TransactionType = (int)line.Type, ReversesTransactionId = line.ReversesTransactionId, OccurredOn = DateTime.UtcNow, Content = JsonConvert.SerializeObject(new { line.Note, ItemName = Canary, UnitOfMeasure = "each", UnitCost = 4.25m }) };
				_ledger.Add(entry); result.TransactionIds.Add(entry.Id); result.OutboxIds.Add(100 + _ledger.Count);
				}
				return result;
			});
			_uow.SetupGet(u => u.Transaction).Returns(() => _transaction);
			_uow.Setup(u => u.CreateOrGetConnectionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => { Begin(); return (DbConnection)null; });
			_uow.Setup(u => u.CreateOrGetConnection()).Returns(() => { Begin(); return (DbConnection)null; });
			_uow.Setup(u => u.CommitChanges()).Callback(() => { _transaction.Should().NotBeNull(); _transaction = null; _rollback = null; });
			_uow.Setup(u => u.DiscardChanges()).Callback(() => { _rollback?.Invoke(); _transaction = null; _rollback = null; });
			_outbox.Setup(o => o.DispatchAfterCommitAsync(It.IsAny<IEnumerable<long>>(), It.IsAny<CancellationToken>())).ReturnsAsync((IEnumerable<long> ids, CancellationToken ct) =>
			{ _transaction.Should().BeNull(); var values = ids.ToList(); _dispatches.Add(values); return values.Count; });
			var groups = new Mock<IDepartmentGroupsService>(); groups.Setup(g => g.GetGroupByIdAsync(7, true)).ReturnsAsync(new DepartmentGroup { DepartmentId = Department, DepartmentGroupId = 7 });
			_legacy.Setup(i => i.GetTypeByIdAsync(1)).ReturnsAsync(new InventoryType { InventoryTypeId = 1, DepartmentId = Department, Type = "Synthetic foam", UnitOfMesasure = "litres" });
			_legacy.Setup(i => i.SaveInventoryAsync(It.IsAny<Inventory>(), It.IsAny<CancellationToken>())).ReturnsAsync((Inventory row, CancellationToken ct) => { row.InventoryId = 901; return row; });
			_adapter = new RmsInventoryUsageAdapter(_referencesRepo.Object, _records.Object, _incidents.Object, _legacy.Object, _authorization.Object,
				groups.Object, Mock.Of<IUnitsService>(), _uow.Object, _auditRepo.Object, _modernStore.Object, _stock.Object, _catalog.Object, _outbox.Object);
		}

		[TestCase(RmsRecordKind.Operational), TestCase(RmsRecordKind.IncidentReport)]
		public async Task Modern_consumption_joins_stock_to_parent_version_clamps_provenance_and_dispatches_after_commit(RmsRecordKind kind)
		{
			var command = Command(); command.Lines[0].ReferenceType = InventoryReferenceType.WorkOrder; command.Lines[0].ReferenceId = "999";
			var result = await _adapter.ConsumeModernAsync(_actor, RecordId, kind, 1, command);
			var captured = _posts.Single(); captured.Actor.DepartmentId.Should().Be(Department); captured.Actor.UserId.Should().Be("author"); captured.Actor.GrantToken.Should().Be(_actor.GrantToken);
			captured.Command.RequestId.Should().Be(command.RequestId); var posting = captured.Command.Lines.Single(); posting.ReferenceType.Should().Be(InventoryReferenceType.RmsRecord); posting.ReferenceId.Should().Be(RecordId);
			command.Lines[0].ReferenceType.Should().Be(InventoryReferenceType.WorkOrder, "the adapter must copy the caller's mutable command"); command.Lines[0].ReferenceId.Should().Be("999");
			_balance.Should().Be(7.874999m); (kind == RmsRecordKind.Operational ? _record.RowVersion : _incident.RowVersion).Should().Be(2);
			result.TransactionId.Should().Be(_ledger.Single().Id); result.ItemId.Should().Be(ItemId); result.InventoryId.Should().Be(0);
			var reference = _references.Single(); reference.RmsExternalReferenceId.Should().Be(command.RequestId); reference.SourceEntityType.Should().Be("RecordInventoryUsage"); reference.SourceEntityId.Should().Be(result.UsageId);
			JObject.Parse(reference.SnapshotJson).Value<int>("SchemaVersion").Should().Be(3); reference.SnapshotJson.Should().NotContain(Canary).And.NotContain(_actor.GrantToken);
			result.ItemName.Should().Be("REDACTED"); result.Note.Should().Be("REDACTED"); result.SourceChecksum.Should().HaveLength(64); _audits.Single().DetailJson.Should().NotContain(Canary);
			_dispatches.Single().Should().Equal(101); _stock.Verify(s => s.PostTransactionAsync(It.IsAny<InventoryActor>(), It.IsAny<InventoryCommand>(), It.IsAny<CancellationToken>()), Times.Never);
			_legacy.Verify(i => i.SaveInventoryAsync(It.IsAny<Inventory>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task Receipt_retry_rechecks_current_grant_and_source_without_rebumping_parent_or_consuming_twice()
		{
			var command = Command(); var first = await _adapter.ConsumeModernAsync(_actor, RecordId, RmsRecordKind.Operational, 1, command);
			var renewed = new InventoryActor { DepartmentId = Department, UserId = "author", GrantToken = "synthetic-renewed-grant" };
			var retry = await _adapter.ConsumeModernAsync(renewed, RecordId, RmsRecordKind.Operational, 1, Copy(command));
			retry.TransactionId.Should().Be(first.TransactionId); _record.RowVersion.Should().Be(2); _balance.Should().Be(7.874999m);
			_posts.Should().HaveCount(1); _references.Should().HaveCount(1); _audits.Should().HaveCount(1); _dispatches.Should().HaveCount(2, "retry recovers the original outbox dispatch without another movement");
			_records.Verify(r => r.TryBumpRowVersionAsync(Department, RecordId, 1, It.IsAny<CancellationToken>()), Times.Once);
			_catalog.Verify(c => c.GetAsync<InventoryTransaction>(It.Is<InventoryActor>(a => a.GrantToken == "synthetic-renewed-grant"), first.TransactionId), Times.Once);
			command.Lines[0].Quantity = 3;
			var error = (await ((Func<Task>)(() => _adapter.ConsumeModernAsync(_actor, RecordId, RmsRecordKind.Operational, 1, command))).Should().ThrowAsync<InventoryException>()).Which;
			error.Code.Should().Be("RequestConflict"); _balance.Should().Be(7.874999m); _record.RowVersion.Should().Be(2);
		}

		[Test]
		public async Task Audit_failure_rolls_back_parent_stock_ledger_and_every_new_reference()
		{
			_auditRepo.Setup(a => a.InsertAsync(It.IsAny<RmsAccessAudit>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ThrowsAsync(new InvalidOperationException("Synthetic audit unavailable"));
			await ((Func<Task>)(() => _adapter.ConsumeModernAsync(_actor, RecordId, RmsRecordKind.Operational, 1, Command()))).Should().ThrowAsync<InvalidOperationException>();
			_record.RowVersion.Should().Be(1); _balance.Should().Be(10); _ledger.Should().BeEmpty(); _references.Should().BeEmpty(); _audits.Should().BeEmpty(); _dispatches.Should().BeEmpty();
			_uow.Verify(u => u.CommitChanges(), Times.Never); _uow.Verify(u => u.DiscardChanges(), Times.Once);
		}

		[Test]
		public async Task Source_authorization_and_closed_parent_block_consumption_and_replayed_receipts()
		{
			var command = Command(); _authorization.Setup(a => a.CanUseSourceInventoryAsync("author", Department, 7)).ReturnsAsync(false);
			await ((Func<Task>)(() => _adapter.ConsumeModernAsync(_actor, RecordId, RmsRecordKind.Operational, 1, command))).Should().ThrowAsync<UnauthorizedAccessException>();
			_record.RowVersion.Should().Be(1); _posts.Should().BeEmpty();
			_authorization.Setup(a => a.CanUseSourceInventoryAsync("author", Department, 7)).ReturnsAsync(true);
			await _adapter.ConsumeModernAsync(_actor, RecordId, RmsRecordKind.Operational, 1, command); _record.State = (int)RmsRecordState.Finalized;
			await ((Func<Task>)(() => _adapter.ConsumeModernAsync(_actor, RecordId, RmsRecordKind.Operational, 1, command))).Should().ThrowAsync<InvalidOperationException>();
			_posts.Should().HaveCount(1); _balance.Should().Be(7.874999m);
		}

		[Test]
		public async Task Legacy_input_translates_after_cutover_with_deterministic_request_and_preserves_pre_cutover_behavior()
		{
			_migrated = false;
			var legacy = await _adapter.ConsumeAsync(Department, "author", RecordId, RmsRecordKind.Operational, 1, 1, 7, null, 2.5m, "Synthetic usage");
			legacy.InventoryId.Should().Be(901); legacy.ItemName.Should().Be("Synthetic foam"); _posts.Should().BeEmpty();
			_legacy.Verify(i => i.SaveInventoryAsync(It.Is<Inventory>(r => r.Amount == -2.5 && r.DepartmentId == Department), It.IsAny<CancellationToken>()), Times.Once);
			_migrated = true;
			var modern = await _adapter.ConsumeAsync(Department, "author", RecordId, RmsRecordKind.Operational, 2, 1, 7, null, 1.125001m, Canary, grantToken: _actor.GrantToken);
			var retry = await _adapter.ConsumeAsync(Department, "author", RecordId, RmsRecordKind.Operational, 2, 1, 7, null, 1.125001m, Canary, grantToken: "new-grant");
			modern.TransactionId.Should().NotBeNullOrEmpty(); retry.TransactionId.Should().Be(modern.TransactionId); _posts.Should().HaveCount(1);
			_posts.Single().Actor.GrantToken.Should().Be(_actor.GrantToken); _record.RowVersion.Should().Be(3);
			(await _adapter.GetUsageForRecordAsync(Department, RecordId)).Should().HaveCount(2);
		}

		[Test]
		public async Task Protected_source_denial_or_incomplete_witness_result_rolls_back_parent_and_dispatches_nothing()
		{
			_catalog.Setup(c => c.GetAsync<InventoryItem>(It.IsAny<InventoryActor>(), ItemId)).ThrowsAsync(new InventoryException(403, "ProtectedDataRequired"));
			await ((Func<Task>)(() => _adapter.ConsumeModernAsync(_actor, RecordId, RmsRecordKind.Operational, 1, Command()))).Should().ThrowAsync<InventoryException>();
			_record.RowVersion.Should().Be(1); _posts.Should().BeEmpty();
			_catalog.Setup(c => c.GetAsync<InventoryItem>(It.IsAny<InventoryActor>(), ItemId)).ReturnsAsync(new InventoryItem { Id = ItemId, DepartmentId = Department });
			_stock.Setup(s => s.PostWithinTransactionAsync(It.IsAny<InventoryActor>(), It.IsAny<InventoryCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(new InventoryResult { AwaitingWitness = true });
			await ((Func<Task>)(() => _adapter.ConsumeModernAsync(_actor, RecordId, RmsRecordKind.Operational, 1, Command()))).Should().ThrowAsync<InvalidOperationException>();
			_record.RowVersion.Should().Be(1); _references.Should().BeEmpty(); _dispatches.Should().BeEmpty();
		}

		private static T Copy<T>(T value) => JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value));
		private static InventoryCommand Command() => new() { RequestId = Guid.NewGuid().ToString("D"), Lines = new() { new InventoryPosting {
			ItemId = ItemId, FromLocationId = LocationId, Type = InventoryTransactionType.Consume, Quantity = 2.125001m, Note = Canary } } };
		private void Begin()
		{
			_transaction.Should().BeNull(); _transaction = new Mock<DbTransaction>().Object;
			var references = _references.Select(Copy).ToList(); var audits = _audits.Select(Copy).ToList(); var ledger = _ledger.Select(Copy).ToList();
			var record = Copy(_record); var incident = Copy(_incident); var balance = _balance; var usages = _usageRows.Select(Copy).ToList();
			_rollback = () => { _references = references; _audits = audits; _ledger = ledger; _usageRows = usages; _record = record; _incident = incident; _balance = balance; };
		}
	}
}
