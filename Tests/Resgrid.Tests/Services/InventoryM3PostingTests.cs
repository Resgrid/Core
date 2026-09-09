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
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	public sealed partial class InventoryModernizationTests
	{
		private RecordInventoryUsage UsageFor(InventoryTransaction transaction, string sourceId = null) => new()
		{
			DepartmentId = Department, SourceType = (int)InventoryUsageSourceType.RmsRecord, SourceId = sourceId ?? Guid.NewGuid().ToString("D"),
			RecordKind = (int)RmsRecordKind.Operational, ItemId = transaction.ItemId, AssetId = transaction.AssetId, LotId = transaction.LotId,
			SourceLocationId = transaction.FromLocationId ?? transaction.ToLocationId, Quantity = transaction.Quantity,
			TransactionId = transaction.Id, UsageType = (int)InventoryUsageType.Used
		};

		private async Task AttachUsage(RecordInventoryUsage usage)
		{
			Begin();
			try { await _service.RecordUsageWithinTransactionAsync(_actor, usage); _uow.Object.CommitChanges(); }
			catch { _uow.Object.DiscardChanges(); throw; }
		}

		[Test]
		public async Task Usage_attachment_checks_transaction_provenance_and_keeps_stock_unchanged()
		{
			var item = Item(); var other = Item(); var location = Location(); var otherLocation = Location(); SeedStock(item, location, 5);
			var result = await _service.PostTransactionAsync(_actor, Command(Move(item, location, null, 1.125001m, InventoryTransactionType.Consume)));
			var transaction = _store.All<InventoryTransaction>().Single(); var usage = UsageFor(transaction);
			await FluentActions.Awaiting(() => _service.RecordUsageWithinTransactionAsync(_actor, usage)).Should().ThrowAsync<InvalidOperationException>();
			await Fails(() => AttachUsage(new RecordInventoryUsage { DepartmentId = Department + 1 }), "InvalidUsageSource", 400);
			foreach (var mismatch in new Action<RecordInventoryUsage>[]
			{
				r => r.Quantity = 1, r => r.ItemId = other.Id, r => r.SourceLocationId = otherLocation.Id,
				r => r.AssetId = Guid.NewGuid().ToString("D"), r => r.LotId = Guid.NewGuid().ToString("D")
			})
			{
				var invalid = Copy(usage); mismatch(invalid); await Fails(() => AttachUsage(invalid), "UsageTransactionMismatch", 409);
			}
			var foreignTransaction = Copy(transaction); foreignTransaction.Id = Guid.NewGuid().ToString("D"); foreignTransaction.DepartmentId++;
			_store.Seed(foreignTransaction); var foreignUsage = Copy(usage); foreignUsage.TransactionId = foreignTransaction.Id;
			await Fails(() => AttachUsage(foreignUsage), "Unavailable", 404);
			await AttachUsage(usage);
			var saved = _store.All<RecordInventoryUsage>().Single(); saved.TransactionId.Should().Be(result.TransactionIds.Single());
			saved.SourceId.Should().Be(usage.SourceId); saved.Quantity.Should().Be(1.125001m); saved.CreatedOn.Should().Be(transaction.OccurredOn); saved.CreatedBy.Should().Be(_actor.UserId);
			Stock(item, location).Should().Be(3.874999m); _events.Should().ContainSingle();
			await Fails(() => AttachUsage(Copy(usage)), "UsageAlreadyRecorded", 409); Stock(item, location).Should().Be(3.874999m);
			_recordsAuth.Setup(x => x.CanUserViewRecordAsync(_actor.UserId, usage.SourceId, Department)).ReturnsAsync(false);
			await Fails(() => _service.GetAsync<RecordInventoryUsage>(_actor, usage.Id), "RecordSourceAuthorizationRequired", 403);
		}

		[Test]
		public async Task Controlled_usage_rejects_unwitnessed_ledger_evidence_and_attaches_a_completed_independent_witness_once()
		{
			var item = Item(controlled: true); var location = Location(); SeedStock(item, location, 5);
			var unaudited = _store.Seed(new InventoryTransaction { DepartmentId = Department, CreatedBy = _actor.UserId, ItemId = item.Id,
				FromLocationId = location.Id, Quantity = 1, TransactionType = (int)InventoryTransactionType.Consume, OccurredOn = _clock.Utc });
			await Fails(() => AttachUsage(UsageFor(unaudited)), "IndependentWitnessRequired", 409);
			var command = Command(Move(item, location, null, 1, InventoryTransactionType.Consume));
			(await _service.PostTransactionAsync(_actor, command)).AwaitingWitness.Should().BeTrue(); Stock(item, location).Should().Be(5);
			var completed = await Witness(command.RequestId); var transaction = _store.All<InventoryTransaction>().Single(t => t.Id == completed.TransactionIds.Single());
			var usage = UsageFor(transaction); await AttachUsage(usage);
			_store.All<RecordInventoryUsage>().Should().ContainSingle().Which.TransactionId.Should().Be(transaction.Id); Stock(item, location).Should().Be(4);
			_auth.Verify(x => x.RequireAsync(_actor, true, PermissionTypes.ManageControlledSubstances, null), Times.AtLeastOnce);
		}

		[TestCase(false)]
		[TestCase(true)]
		public async Task Usage_content_is_protected_and_failed_protection_rolls_back_joined_stock_ledger_and_events(bool failProtection)
		{
			var item = Item(); var location = Location(); SeedStock(item, location, 5); var sourceId = Guid.NewGuid().ToString("D");
			var key = RandomNumberGenerator.GetBytes(32); var crypto = new ProtectedFieldCryptoService();
			try
			{
				if (failProtection)
					_write.Setup(x => x.PrepareRecordsEntityWriteAsync(Department, It.IsAny<RecordInventoryUsage>(), It.IsAny<RecordInventoryUsage>(), It.IsAny<string>(),
						It.IsAny<IReadOnlyDictionary<string, (Func<RecordInventoryUsage, string> Get, Action<RecordInventoryUsage, string> Set)>>(), It.IsAny<Action>(),
						It.IsAny<string>(), It.IsAny<string>(), false, It.IsAny<CancellationToken>())).ReturnsAsync(ProtectedWriteResult.Blocked("synthetic_usage_protection_failure"));
				else EncryptWrites<RecordInventoryUsage>(key, crypto);
				async Task PostAndAttach()
				{
					Begin();
					try
					{
						var posting = Move(item, location, null, 1, InventoryTransactionType.Consume); posting.ReferenceType = InventoryReferenceType.RmsRecord; posting.ReferenceId = sourceId;
						var result = await _service.PostWithinTransactionAsync(_actor, Command(posting));
						var usage = UsageFor(_store.All<InventoryTransaction>().Single(t => t.Id == result.TransactionIds.Single()), sourceId);
						usage.Content = JsonConvert.SerializeObject(new InventoryLabel { Note = Canary }); await _service.RecordUsageWithinTransactionAsync(_actor, usage);
						_uow.Object.CommitChanges();
					}
					catch { _uow.Object.DiscardChanges(); throw; }
				}
				if (failProtection)
				{
					await Fails(PostAndAttach, "ProtectedDataRequired", 403); Stock(item, location).Should().Be(5);
					_store.All<RecordInventoryUsage>().Should().BeEmpty(); _store.All<InventoryTransaction>().Should().BeEmpty(); _store.All<InventoryOperation>().Should().BeEmpty();
					_events.Should().BeEmpty(); _audits.Should().BeEmpty(); _dispatches.Should().BeEmpty();
				}
				else
				{
					await PostAndAttach(); var saved = _store.All<RecordInventoryUsage>().Single(); saved.IsProtected.Should().BeTrue(); saved.Content.Should().StartWith("rgdp:").And.NotContain(Canary);
					JObject.Parse(crypto.DecryptText(key, saved.Content, Department, "recordinventoryusages.content", saved.Id)).Value<string>("Note").Should().Be(Canary);
					Stock(item, location).Should().Be(4); _events.Should().ContainSingle(); _dispatches.Should().BeEmpty("the source adapter dispatches only after its own commit");
				}
			}
			finally { CryptographicOperations.ZeroMemory(key); }
		}

		[TestCase(InventoryAssetStatus.InService)]
		[TestCase(InventoryAssetStatus.OutForRepair)]
		[TestCase(InventoryAssetStatus.Damaged)]
		public async Task Exact_serialized_consumption_reversal_restores_the_original_status(InventoryAssetStatus originalStatus)
		{
			var item = Item(InventoryTrackingMode.Serialized); var location = Location(); var asset = await CreateAsset(item, location);
			if (originalStatus != InventoryAssetStatus.InService) await _service.ChangeAssetStatusAsync(_actor, Status(item, asset, location, originalStatus));
			asset = _store.All<InventoryAsset>().Single(); var consume = Move(item, location, null, 1, InventoryTransactionType.Consume); consume.AssetId = asset.Id; consume.ExpectedAssetRevision = asset.Revision;
			var consumed = await _service.PostTransactionAsync(_actor, Command(consume)); var transaction = _store.All<InventoryTransaction>().Single(t => t.Id == consumed.TransactionIds.Single());
			var reversal = Move(item, null, location, 1, InventoryTransactionType.Adjust); reversal.AssetId = asset.Id; reversal.ReversesTransactionId = transaction.Id;
			var reversed = await _service.PostTransactionAsync(_actor, Command(reversal)); var restored = _store.All<InventoryAsset>().Single();
			restored.Status.Should().Be((int)originalStatus); restored.CurrentLocationId.Should().Be(location.Id);
			var correction = _store.All<InventoryTransaction>().Single(t => t.Id == reversed.TransactionIds.Single());
			correction.ReversesTransactionId.Should().Be(transaction.Id); correction.OldStatus.Should().Be((int)InventoryAssetStatus.Consumed); correction.NewStatus.Should().Be((int)originalStatus);
		}

		[Test]
		public async Task Serialized_reversal_rejects_a_later_lifecycle_change_even_if_status_and_location_match_again()
		{
			var item = Item(InventoryTrackingMode.Serialized); var location = Location(); var asset = await CreateAsset(item, location);
			var consume = Move(item, location, null, 1, InventoryTransactionType.Consume); consume.AssetId = asset.Id;
			var consumed = await _service.PostTransactionAsync(_actor, Command(consume)); var originalId = consumed.TransactionIds.Single();
			await _service.ChangeAssetStatusAsync(_actor, Status(item, _store.All<InventoryAsset>().Single(), location, InventoryAssetStatus.Lost));
			await _service.ChangeAssetStatusAsync(_actor, Status(item, _store.All<InventoryAsset>().Single(), location, InventoryAssetStatus.Consumed));
			var before = JsonConvert.SerializeObject(_store.All<InventoryAsset>()); var count = _store.All<InventoryTransaction>().Count();
			var reversal = Move(item, null, location, 1, InventoryTransactionType.Adjust); reversal.AssetId = asset.Id; reversal.ReversesTransactionId = originalId;
			await Fails(() => _service.PostTransactionAsync(_actor, Command(reversal)), "AssetReversalConflict", 409);
			JsonConvert.SerializeObject(_store.All<InventoryAsset>()).Should().Be(before); _store.All<InventoryTransaction>().Should().HaveCount(count);
		}

		[Test]
		public async Task Generic_reversal_of_attached_consumption_requires_the_record_correction_path()
		{
			var item = Item(); var location = Location(); SeedStock(item, location, 5);
			var consumed = await _service.PostTransactionAsync(_actor, Command(Move(item, location, null, 1, InventoryTransactionType.Consume)));
			var transaction = _store.All<InventoryTransaction>().Single(); await AttachUsage(UsageFor(transaction));
			var reversal = Move(item, null, location, 1, InventoryTransactionType.Adjust); reversal.ReversesTransactionId = consumed.TransactionIds.Single();
			await Fails(() => _service.PostTransactionAsync(_actor, Command(reversal)), "RecordUsageCorrectionRequired", 409);
			Stock(item, location).Should().Be(4); _store.All<InventoryTransaction>().Should().ContainSingle();
			_recordUsageAdapter.Verify(x => x.RequireUsageCorrectionAccessAsync(It.IsAny<InventoryActor>(), It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task Controlled_attached_reversal_requires_current_record_access_before_pending_and_for_both_witness_principals()
		{
			var item = Item(controlled: true); var location = Location(); SeedStock(item, location, 5);
			var consume = Command(Move(item, location, null, 1, InventoryTransactionType.Consume)); await _service.PostTransactionAsync(_actor, consume); await Witness(consume.RequestId);
			var transaction = _store.All<InventoryTransaction>().Single(); var usage = UsageFor(transaction); await AttachUsage(usage);
			var reversal = Move(item, null, location, 1, InventoryTransactionType.Adjust); reversal.ReversesTransactionId = transaction.Id; var command = Command(reversal);
			_recordUsageAdapter.Setup(x => x.RequireUsageCorrectionAccessAsync(It.IsAny<InventoryActor>(), usage.Id)).ThrowsAsync(new InventoryException(403, "RecordSourceAuthorizationRequired"));
			await Fails(() => _service.PostTransactionAsync(_actor, command), "RecordSourceAuthorizationRequired", 403);
			_store.All<InventoryOperation>().Should().ContainSingle(); Stock(item, location).Should().Be(4);
			_recordUsageAdapter.Setup(x => x.RequireUsageCorrectionAccessAsync(It.IsAny<InventoryActor>(), usage.Id)).Returns(Task.CompletedTask);
			(await _service.PostTransactionAsync(_actor, command)).AwaitingWitness.Should().BeTrue(); Stock(item, location).Should().Be(4);
			_recordUsageAdapter.Setup(x => x.RequireUsageCorrectionAccessAsync(It.Is<InventoryActor>(a => a.UserId == "witness"), usage.Id)).ThrowsAsync(new InventoryException(403, "RecordSourceAuthorizationRequired"));
			await Fails(() => Witness(command.RequestId), "RecordSourceAuthorizationRequired", 403);
			_store.All<InventoryTransaction>().Should().ContainSingle(); Stock(item, location).Should().Be(4);
			_recordUsageAdapter.Setup(x => x.RequireUsageCorrectionAccessAsync(It.IsAny<InventoryActor>(), usage.Id)).Returns(Task.CompletedTask);
			(await Witness(command.RequestId)).AwaitingWitness.Should().BeFalse(); Stock(item, location).Should().Be(5);
			_recordUsageAdapter.Verify(x => x.RequireUsageCorrectionAccessAsync(It.Is<InventoryActor>(a => a.UserId == _actor.UserId && a.GrantToken == null), usage.Id), Times.AtLeastOnce);
			_recordUsageAdapter.Verify(x => x.RequireUsageCorrectionAccessAsync(It.Is<InventoryActor>(a => a.UserId == "witness"), usage.Id), Times.AtLeastOnce);
		}

		[Test]
		public async Task Old_public_operation_fingerprint_and_replay_survive_absent_M3_usage_fields()
		{
			var item = Item(); var location = Location(); var command = Receive(item, location, 3);
			var oldShape = JObject.FromObject(new { Kind = "Post", command.Lines });
			foreach (var line in oldShape["Lines"].Children<JObject>()) { line.Remove("UsageId"); line.Remove("UsageType"); }
			var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(oldShape.ToString(Formatting.None))));
			var completed = await _service.PostTransactionAsync(_actor, command); var receipt = JsonConvert.DeserializeObject<InventoryOperationContent>(_store.All<InventoryOperation>().Single().Content);
			receipt.Fingerprint.Should().Be(expected); JsonConvert.SerializeObject(command.Lines).Should().NotContain("UsageId").And.NotContain("UsageType");
			(await _service.PostTransactionAsync(_actor, Copy(command))).TransactionIds.Should().Equal(completed.TransactionIds);
			Stock(item, location).Should().Be(3); _store.All<InventoryTransaction>().Should().ContainSingle();
		}

		[Test]
		public async Task Joined_usage_workflow_exposes_safe_usage_identifiers_and_reversal_backlink_without_narrative()
		{
			var item = Item(); var location = Location(); SeedStock(item, location, 5); var sourceId = Guid.NewGuid().ToString("D");
			var usageId = Guid.NewGuid().ToString("D"); var correctionId = Guid.NewGuid().ToString("D");
			var line = Move(item, location, null, 1, InventoryTransactionType.Consume); line.ReferenceType = InventoryReferenceType.RmsRecord; line.ReferenceId = sourceId;
			line.UsageId = usageId; line.UsageType = InventoryUsageType.ConsumedOnPatient;
			Begin(); var original = await _service.PostWithinTransactionAsync(_actor, Command(line)); _uow.Object.CommitChanges();
			var reverse = Move(item, null, location, 1, InventoryTransactionType.Adjust); reverse.ReferenceType = InventoryReferenceType.RmsRecord; reverse.ReferenceId = sourceId;
			reverse.ReversesTransactionId = original.TransactionIds.Single(); reverse.UsageId = correctionId; reverse.UsageType = line.UsageType;
			Begin(); await _service.PostWithinTransactionAsync(_actor, Command(reverse)); _uow.Object.CommitChanges();
			_events.Should().HaveCount(2); var safe = InventoryWorkflowPayload.Parse(InventoryWorkflowPayload.Routing(JObject.FromObject(_events.Last().Payload)));
			safe.Value<string>("UsageId").Should().Be(correctionId); safe.Value<int>("UsageType").Should().Be((int)InventoryUsageType.ConsumedOnPatient);
			safe.Value<string>("ReversesTransactionId").Should().Be(original.TransactionIds.Single()); safe.Value<string>("ReferenceId").Should().Be(sourceId);
			InventoryWorkflowPayload.Variables.Should().Contain(("usage_id", "UsageId")).And.Contain(("usage_type", "UsageType"));
			JsonConvert.SerializeObject(_events).Should().NotContain(Canary).And.NotContain("synthetic-manager-grant"); safe.Value<string>("Note").Should().Be(ProtectedDataEnvelope.RedactionValue);
			Stock(item, location).Should().Be(5); _dispatches.Should().BeEmpty();
		}
	}
}
