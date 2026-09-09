using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
	public sealed partial class RmsInventoryModernUsageTests
	{
		private static RecordInventoryUsageRequest UsageRequest(params decimal[] quantities) => new()
		{ RequestId = Guid.NewGuid().ToString("D"), Lines = quantities.Select(q => new RecordInventoryUsageLine { ItemId = ItemId, LocationId = LocationId, Quantity = q, UsageType = InventoryUsageType.LeftAtScene, Note = Canary }).ToList() };

		[TestCase(RmsRecordKind.Operational), TestCase(RmsRecordKind.IncidentReport)]
		public async Task Linked_witnessed_correction_holds_a_source_version_bump_in_the_callers_inventory_transaction(RmsRecordKind kind)
		{
			var usage = LinkedUsage(kind);
			await _uow.Object.CreateOrGetConnectionAsync(CancellationToken.None);
			try
			{
				await _adapter.RequireUsageCorrectionAccessAsync(_actor, usage.Id);
				(kind == RmsRecordKind.Operational ? _record.RowVersion : _incident.RowVersion).Should().Be(2);
				_transaction.Should().NotBeNull("the source lock must remain in the inventory caller's transaction until it commits the movement");
				if (kind == RmsRecordKind.Operational) _records.Verify(x => x.TryBumpRowVersionAsync(Department, RecordId, 1, It.IsAny<CancellationToken>()), Times.Once);
				else _incidents.Verify(x => x.TryBumpRowVersionAsync(Department, RecordId, 1, It.IsAny<CancellationToken>()), Times.Once);
				_uow.Verify(x => x.CommitChanges(), Times.Never); _uow.Verify(x => x.DiscardChanges(), Times.Never);
				_posts.Should().BeEmpty(); _references.Should().BeEmpty(); _dispatches.Should().BeEmpty();
			}
			finally { _uow.Object.DiscardChanges(); }
			(kind == RmsRecordKind.Operational ? _record.RowVersion : _incident.RowVersion).Should().Be(1, "failure later in the owning inventory command rolls the source bump back too");
		}

		[TestCase(RmsRecordKind.Operational), TestCase(RmsRecordKind.IncidentReport)]
		public async Task Competing_finalization_before_the_source_version_lock_rejects_a_witnessed_correction(RmsRecordKind kind)
		{
			var usage = LinkedUsage(kind);
			if (kind == RmsRecordKind.Operational)
				_records.Setup(x => x.TryBumpRowVersionAsync(Department, RecordId, It.IsAny<long>(), It.IsAny<CancellationToken>())).ReturnsAsync((int department, string id, long expected, CancellationToken ct) =>
				{
					expected.Should().Be(1); _record.State = (int)RmsRecordState.Finalized; _record.RowVersion = 2;
					return _record.RowVersion == expected;
				});
			else
				_incidents.Setup(x => x.TryBumpRowVersionAsync(Department, RecordId, It.IsAny<long>(), It.IsAny<CancellationToken>())).ReturnsAsync((int department, string id, long expected, CancellationToken ct) =>
				{
					expected.Should().Be(1); _incident.State = (int)RmsRecordState.Finalized; _incident.RowVersion = 2;
					return _incident.RowVersion == expected;
				});
			await _uow.Object.CreateOrGetConnectionAsync(CancellationToken.None);
			try
			{
				Func<Task> correction = () => _adapter.RequireUsageCorrectionAccessAsync(_actor, usage.Id);
				await correction.Should().ThrowAsync<RecordConcurrencyException>();
				(kind == RmsRecordKind.Operational ? _record.State : _incident.State).Should().Be((int)RmsRecordState.Finalized);
				_posts.Should().BeEmpty(); _references.Should().BeEmpty(); _dispatches.Should().BeEmpty();
				_uow.Verify(x => x.CommitChanges(), Times.Never);
			}
			finally { _uow.Object.DiscardChanges(); }
		}

		[TestCase(RmsRecordKind.Operational), TestCase(RmsRecordKind.IncidentReport)]
		public async Task Reauthorizing_an_already_posted_linked_reversal_does_not_bump_the_source_again(RmsRecordKind kind)
		{
			var usage = LinkedUsage(kind);
			_ledger.Add(new InventoryTransaction { DepartmentId = Department, ItemId = ItemId, ToLocationId = LocationId, Quantity = usage.Quantity,
				TransactionType = (int)InventoryTransactionType.Adjust, ReversesTransactionId = usage.TransactionId });
			await _uow.Object.CreateOrGetConnectionAsync(CancellationToken.None);
			try
			{
				await _adapter.RequireUsageCorrectionAccessAsync(_actor, usage.Id);
				(kind == RmsRecordKind.Operational ? _record.RowVersion : _incident.RowVersion).Should().Be(1);
				_records.Verify(x => x.TryBumpRowVersionAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
				_incidents.Verify(x => x.TryBumpRowVersionAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
				_transaction.Should().NotBeNull(); _posts.Should().BeEmpty();
			}
			finally { _uow.Object.DiscardChanges(); }
		}

		private RecordInventoryUsage LinkedUsage(RmsRecordKind kind)
		{
			var transaction = new InventoryTransaction { DepartmentId = Department, ItemId = ItemId, FromLocationId = LocationId, Quantity = 1, TransactionType = (int)InventoryTransactionType.Consume };
			_ledger.Add(transaction);
			var usage = new RecordInventoryUsage { DepartmentId = Department, SourceType = (int)InventoryUsageSourceType.RmsRecord, SourceId = RecordId,
				RecordKind = (int)kind, ItemId = ItemId, SourceLocationId = LocationId, TransactionId = transaction.Id, Quantity = transaction.Quantity };
			_usageRows.Add(usage); return usage;
		}

		[Test]
		public async Task Batch_usage_has_stable_independent_identities_and_one_atomic_version_bump()
		{
			var input = UsageRequest(1.125001m, 2.000002m);
			var result = await _adapter.RecordModernUsageAsync(_actor, RecordId, RmsRecordKind.Operational, 1, input);
			result.Should().HaveCount(2); result.Select(x => x.UsageId).Distinct().Should().HaveCount(2);
			result[0].UsageId.Should().Be(input.RequestId); _balance.Should().Be(6.874997m); _record.RowVersion.Should().Be(2);
			_usageRows.Should().HaveCount(2).And.OnlyContain(x => x.UsageType == (int)InventoryUsageType.LeftAtScene && x.SourceId == RecordId);
			var retried = await _adapter.RecordModernUsageAsync(_actor, RecordId, RmsRecordKind.Operational, 1, Copy(input));
			retried.Select(x => x.UsageId).Should().Equal(result.Select(x => x.UsageId)); _ledger.Should().HaveCount(2); _record.RowVersion.Should().Be(2);
			_dispatches.Should().HaveCount(2).And.OnlyContain(x => x.SequenceEqual(new long[] { 101, 102 }));
			input.Lines[1].UsageType = InventoryUsageType.Damaged;
			await ((Func<Task>)(() => _adapter.RecordModernUsageAsync(_actor, RecordId, RmsRecordKind.Operational, 1, input))).Should().ThrowAsync<InventoryException>().Where(x => x.Code == "RequestConflict");
		}

		[Test]
		public async Task Failure_saving_later_usage_rolls_back_whole_batch_and_outbox()
		{
			_stock.Setup(s => s.RecordUsageWithinTransactionAsync(It.IsAny<InventoryActor>(), It.IsAny<RecordInventoryUsage>())).Returns((InventoryActor a, RecordInventoryUsage row) =>
			{ if (_usageRows.Count == 1) throw new InventoryException(403, "ProtectedDataRequired"); _usageRows.Add(Copy(row)); return Task.CompletedTask; });
			await ((Func<Task>)(() => _adapter.RecordModernUsageAsync(_actor, RecordId, RmsRecordKind.Operational, 1, UsageRequest(1, 2)))).Should().ThrowAsync<InventoryException>();
			_balance.Should().Be(10); _usageRows.Should().BeEmpty(); _ledger.Should().BeEmpty(); _references.Should().BeEmpty(); _record.RowVersion.Should().Be(1); _dispatches.Should().BeEmpty();
		}

		[TestCase(RmsRecordKind.Operational), TestCase(RmsRecordKind.IncidentReport)]
		public async Task Full_correction_is_append_only_signed_and_retry_safe_with_original_cost(RmsRecordKind kind)
		{
			var usage = (await _adapter.RecordModernUsageAsync(_actor, RecordId, kind, 1, UsageRequest(2.125001m))).Single();
			var frozen = Copy(_references.Single());
			var correction = new RecordInventoryUsageCorrection { RequestId = Guid.NewGuid().ToString("D"), UsageId = usage.UsageId, Reason = "Synthetic correction reason" };
			var result = await _adapter.ReverseModernUsageAsync(_actor, RecordId, kind, 2, correction);
			result.Quantity.Should().Be(-usage.Quantity); result.ReversesUsageId.Should().Be(usage.UsageId); _balance.Should().Be(10);
			_references.First().SnapshotJson.Should().Be(frozen.SnapshotJson); _references.First().Checksum.Should().Be(frozen.Checksum);
			_posts.Last().Command.Lines.Single().UnitCost.Should().Be(4.25m); _posts.Last().Command.Lines.Single().ReversesTransactionId.Should().Be(usage.TransactionId);
			var retry = await _adapter.ReverseModernUsageAsync(_actor, RecordId, kind, 2, Copy(correction)); retry.UsageId.Should().Be(result.UsageId);
			_ledger.Should().HaveCount(2); _usageRows.Should().HaveCount(2);
			var projection = await _adapter.GetUsageForRecordAsync(Department, RecordId); projection.Sum(x => x.Quantity).Should().Be(0); projection.First().IsReversed.Should().BeTrue();
			correction.RequestId = Guid.NewGuid().ToString("D");
			await ((Func<Task>)(() => _adapter.ReverseModernUsageAsync(_actor, RecordId, kind, 3, correction))).Should().ThrowAsync<InventoryException>().Where(x => x.Code == "UsageAlreadyReversed");
		}

		[Test]
		public async Task Open_amendment_retains_old_usage_and_links_new_correction_to_base_revision()
		{
			var original = (await _adapter.RecordModernUsageAsync(_actor, RecordId, RmsRecordKind.Operational, 1, UsageRequest(1))).Single();
			_record.State = (int)RmsRecordState.Finalized; _record.AmendsRevisionId = Guid.NewGuid().ToString("D");
			var correction = new RecordInventoryUsageCorrection { RequestId = Guid.NewGuid().ToString("D"), UsageId = original.UsageId, Reason = Canary };
			await _adapter.ReverseModernUsageAsync(_actor, RecordId, RmsRecordKind.Operational, 2, correction);
			_usageRows.First().RmsRevisionId.Should().BeNull(); _usageRows.Last().RmsRevisionId.Should().Be(_record.AmendsRevisionId);
			_record.AmendsRevisionId = null; // Abandoning document edits must not create a new stock mutation.
			(await _adapter.GetUsageForRecordAsync(Department, RecordId)).Sum(x => x.Quantity).Should().Be(0); _ledger.Should().HaveCount(2); _balance.Should().Be(10);
		}

		[Test]
		public async Task Attachment_and_witnessed_reversal_never_post_stock_again_and_keep_correction_reason()
		{
			var transaction = new InventoryTransaction { DepartmentId = Department, ItemId = ItemId, FromLocationId = LocationId, Quantity = 2, TransactionType = (int)InventoryTransactionType.Consume, Content = "{}" };
			_ledger.Add(transaction);
			var request = new RecordInventoryUsageRequest { RequestId = Guid.NewGuid().ToString("D"), Lines = new() { new() { ExistingTransactionId = transaction.Id, UsageType = InventoryUsageType.ConsumedOnPatient } } };
			var usage = (await _adapter.RecordModernUsageAsync(_actor, RecordId, RmsRecordKind.Operational, 1, request)).Single();
			_posts.Should().BeEmpty(); _balance.Should().Be(10); _usageRows.Single().TransactionId.Should().Be(transaction.Id);
			var reversal = new InventoryTransaction { DepartmentId = Department, ItemId = ItemId, ToLocationId = LocationId, Quantity = 2, TransactionType = (int)InventoryTransactionType.Adjust, ReversesTransactionId = transaction.Id, Content = "{}" };
			_ledger.Add(reversal);
			var before = await _adapter.GetAuthorizedUsageAsync(_actor, RecordId, RmsRecordKind.Operational); before.Single().PendingReversalTransactionId.Should().Be(reversal.Id);
			await _adapter.ReverseModernUsageAsync(_actor, RecordId, RmsRecordKind.Operational, 2, new RecordInventoryUsageCorrection { RequestId = Guid.NewGuid().ToString("D"), UsageId = usage.UsageId, ExistingTransactionId = reversal.Id, Reason = Canary });
			_posts.Should().BeEmpty(); _balance.Should().Be(10);
			(await _adapter.GetAuthorizedUsageAsync(_actor, RecordId, RmsRecordKind.Operational)).Last().Note.Should().Be(Canary);
			_references.Should().OnlyContain(x => !x.SnapshotJson.Contains(Canary));
		}

		[Test]
		public async Task Legacy_reference_keeps_identity_and_reverses_exact_migrated_negative_entry_without_legacy_writes()
		{
			_migrated = false;
			var legacy = await _adapter.ConsumeAsync(Department, "author", RecordId, RmsRecordKind.Operational, 1, 1, 7, null, 2.5m, Canary);
			var frozen = _references.Single().Checksum; _migrated = true;
			_ledger.Add(new InventoryTransaction { DepartmentId = Department, ItemId = ItemId, FromLocationId = LocationId, Quantity = 2.5m, LegacyInventoryId = 901,
				TransactionType = (int)InventoryTransactionType.Migrated, ReferenceType = (int)InventoryReferenceType.Legacy, Content = "{}" });
			legacy.UsageId.Should().Be(legacy.ReferenceId);
			var result = await _adapter.ReverseModernUsageAsync(_actor, RecordId, RmsRecordKind.Operational, 2, new RecordInventoryUsageCorrection { RequestId = Guid.NewGuid().ToString("D"), UsageId = legacy.UsageId, Reason = Canary });
			result.Quantity.Should().Be(-2.5m); _usageRows.First().Id.Should().Be(legacy.UsageId); _references.First().Checksum.Should().Be(frozen);
			_legacy.Verify(x => x.SaveInventoryAsync(It.IsAny<Inventory>(), It.IsAny<CancellationToken>()), Times.Once);
			(await _adapter.GetUsageForLegacyLogAsync(Department, 99)).Should().BeEmpty("no Log linkage may be invented from a unit or timestamp");
		}

		[Test]
		public async Task Automatic_source_uses_unique_report_station_and_rejects_absent_source()
		{
			var input = UsageRequest(1); input.Lines[0].LocationId = null;
			await ((Func<Task>)(() => _adapter.RecordModernUsageAsync(_actor, RecordId, RmsRecordKind.Operational, 1, input))).Should().ThrowAsync<InventoryException>().Where(x => x.Code == "ExplicitSourceLocationRequired");
			_record.StationGroupId = 7;
			await _adapter.RecordModernUsageAsync(_actor, RecordId, RmsRecordKind.Operational, 1, input);
			_posts.Single().Command.Lines.Single().FromLocationId.Should().Be(LocationId); input.Lines.Single().LocationId.Should().BeNull();
		}

		[Test]
		public async Task Pre_M3_fingerprint_without_new_optional_posting_properties_still_replays()
		{
			var command = Command(); var oldPosting = Copy(command); oldPosting.Lines[0].ReferenceType = InventoryReferenceType.RmsRecord; oldPosting.Lines[0].ReferenceId = RecordId;
			var oldLines = JArray.FromObject(oldPosting.Lines); foreach (var line in oldLines.OfType<JObject>()) { line.Remove("UsageId"); line.Remove("UsageType"); }
			string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
			var fingerprint = Hash(JsonConvert.SerializeObject(new { DepartmentId = Department, UserId = "author", recordId = RecordId, kind = RmsRecordKind.Operational, expectedRowVersion = 1L, Lines = oldLines }));
			var transaction = new InventoryTransaction { DepartmentId = Department, ItemId = ItemId, FromLocationId = LocationId, Quantity = command.Lines[0].Quantity, Content = "{}" }; _ledger.Add(transaction);
			var snapshot = JsonConvert.SerializeObject(new { SchemaVersion = 2, TransactionId = transaction.Id, ItemId, Quantity = transaction.Quantity, RequestFingerprint = fingerprint });
			_references.Add(new RmsExternalReference { RmsExternalReferenceId = command.RequestId, DepartmentId = Department, RecordId = RecordId, RecordKind = 1, SourceSubsystem = "Inventory", SemanticRole = "InventoryUsage",
				SourceEntityType = "InventoryTransaction", SourceEntityId = transaction.Id, SourceVersion = "2", CapturedByUserId = "author", SnapshotJson = snapshot, Checksum = Hash(snapshot) });
			var result = await _adapter.ConsumeModernAsync(_actor, RecordId, RmsRecordKind.Operational, 1, command);
			result.TransactionId.Should().Be(transaction.Id); _posts.Should().BeEmpty(); _record.RowVersion.Should().Be(1); _references.Should().ContainSingle();
		}
	}
}
