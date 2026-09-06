using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// Source-agnostic inventory usage (RMS-1 package): usage is written against the Record as an external
	/// reference, never as a legacy Log row, and read back the same way for either source.
	/// </summary>
	[TestFixture]
	public class RmsInventoryUsageAdapterTests
	{
		private const int Dept = 4;
		private List<RmsExternalReference> _references;
		private Mock<IRmsExternalReferencesRepository> _referencesRepo;
		private Mock<IRmsOperationalRecordsRepository> _records;
		private RmsInventoryUsageAdapter _adapter;
		private Mock<IInventoryService> _inventory;
		private Mock<IRecordsAuthorizationService> _authorization;

		[SetUp]
		public void SetUp()
		{
			_references = new List<RmsExternalReference>();
			_referencesRepo = new Mock<IRmsExternalReferencesRepository>();
			_referencesRepo.Setup(r => r.GetForRecordAsync(Dept, It.IsAny<string>())).ReturnsAsync((int d, string id) => _references.Where(x => x.RecordId == id).ToList());
			_referencesRepo.Setup(r => r.InsertAsync(It.IsAny<RmsExternalReference>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsExternalReference e, CancellationToken c, bool f) => { _references.Add(e); return e; });

			_records = new Mock<IRmsOperationalRecordsRepository>();
			_records.Setup(r => r.GetByIdForDepartmentAsync(Dept, "rec-1")).ReturnsAsync(new RmsOperationalRecord { RmsOperationalRecordId = "rec-1", DepartmentId = Dept, AuthorUserId = "u1", State = (int)RmsRecordState.Draft, RowVersion = 1 });
			_records.Setup(r => r.GetByIdForDepartmentAsync(Dept, "rec-void")).ReturnsAsync(new RmsOperationalRecord { RmsOperationalRecordId = "rec-void", DepartmentId = Dept, State = (int)RmsRecordState.Voided });

			_records.Setup(r => r.TryBumpRowVersionAsync(Dept, "rec-1", 1, It.IsAny<CancellationToken>())).ReturnsAsync(true);
			_inventory = new(); _authorization = new();
			_authorization.Setup(a => a.CanUserViewRecordAsync("u1", It.IsAny<string>(), Dept)).ReturnsAsync(true);
			_authorization.Setup(a => a.HasPermissionAsync("u1", Dept, It.IsAny<PermissionTypes>())).ReturnsAsync(true);
			_authorization.Setup(a => a.CanUseSourceInventoryAsync("u1", Dept, It.IsAny<int?>())).ReturnsAsync(true);
			_inventory.Setup(i => i.GetInventoryByIdAsync(It.IsIn(77,78))).ReturnsAsync((int id) => new Inventory { InventoryId = id, DepartmentId = Dept, TypeId = 1, GroupId = 2, Amount = -3 });
			_inventory.Setup(i => i.GetTypeByIdAsync(1)).ReturnsAsync(new InventoryType { InventoryTypeId = 1, DepartmentId = Dept, Type = "Foam", UnitOfMesasure = "litres" });
			var groups = new Mock<IDepartmentGroupsService>(); groups.Setup(g => g.GetGroupByIdAsync(2, true)).ReturnsAsync(new DepartmentGroup { DepartmentGroupId = 2, DepartmentId = Dept });
			_adapter = new RmsInventoryUsageAdapter(_referencesRepo.Object, _records.Object, Mock.Of<IRmsIncidentReportsRepository>(), _inventory.Object, _authorization.Object, groups.Object, Mock.Of<IUnitsService>(), Mock.Of<IUnitOfWork>(), Mock.Of<IRmsAccessAuditsRepository>());
		}

		[Test]
		public async Task Consumption_creates_a_negative_ledger_entry_and_frozen_named_evidence_once_under_the_parent_version()
		{
			_inventory.Setup(i => i.SaveInventoryAsync(It.IsAny<Inventory>(), It.IsAny<CancellationToken>())).ReturnsAsync((Inventory row, CancellationToken ct) => { row.InventoryId=901; return row; });
			_records.SetupSequence(r => r.TryBumpRowVersionAsync(Dept,"rec-1",1,It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false);
			Func<Task> stale = () => _adapter.ConsumeAsync(Dept,"u1","rec-1",RmsRecordKind.Operational,0,1,2,null,2.5m,"Foam used");
			await stale.Should().ThrowAsync<RecordConcurrencyException>();
			_inventory.Verify(i => i.SaveInventoryAsync(It.IsAny<Inventory>(),It.IsAny<CancellationToken>()),Times.Never);
			var usage = await _adapter.ConsumeAsync(Dept,"u1","rec-1",RmsRecordKind.Operational,1,1,2,null,2.5m,"Foam used");
			usage.InventoryId.Should().Be(901); usage.ItemName.Should().Be("Foam"); usage.UnitOfMeasure.Should().Be("litres"); usage.SourceChecksum.Should().HaveLength(64);
			_inventory.Verify(i=>i.SaveInventoryAsync(It.Is<Inventory>(r=>r.Amount==-2.5 && r.DepartmentId==Dept && r.AddedByUserId=="u1"),It.IsAny<CancellationToken>()),Times.Once);
			Func<Task> replay = () => _adapter.ConsumeAsync(Dept,"u1","rec-1",RmsRecordKind.Operational,1,1,2,null,2.5m,"Foam used"); await replay.Should().ThrowAsync<RecordConcurrencyException>();
			var evidence = new Resgrid.Services.Records.Evidence.InventoryUsageEvidenceAdapter(_adapter,_authorization.Object);
			var captured = await evidence.CaptureAsync(new RecordEvidenceCaptureRequest {DepartmentId=Dept,RecordId="rec-1",CapturedByUserId="u1"});
			captured.Classification.Should().Be(RmsEvidenceClassification.Restricted); captured.SourceItemCount.Should().Be(1);
			var frozen=RecordsEvidenceService.Serialize(captured.Manifest); frozen.Should().Contain("Foam").And.Contain("litres").And.Contain(usage.SourceChecksum);
			_references.Single().SnapshotJson="{}";
			Func<Task> tampered=()=>_adapter.GetUsageForRecordAsync(Dept,"rec-1"); await tampered.Should().ThrowAsync<InvalidOperationException>();
			RecordsEvidenceService.Serialize(captured.Manifest).Should().Be(frozen);
		}
		[Test]
		public async Task Inventory_permission_foreign_type_and_purged_parent_block_ledger_and_reference_writes()
		{
			_authorization.Setup(a=>a.CanUseSourceInventoryAsync("u1",Dept,2)).ReturnsAsync(false);
			Func<Task> denied=()=>_adapter.ConsumeAsync(Dept,"u1","rec-1",RmsRecordKind.Operational,1,1,2,null,1,"Denied"); await denied.Should().ThrowAsync<UnauthorizedAccessException>();
			_authorization.Setup(a=>a.CanUseSourceInventoryAsync("u1",Dept,2)).ReturnsAsync(true);
			_inventory.Setup(i=>i.GetTypeByIdAsync(1)).ReturnsAsync(new InventoryType {InventoryTypeId=1,DepartmentId=99}); await denied.Should().ThrowAsync<UnauthorizedAccessException>();
			_records.Setup(r=>r.GetByIdForDepartmentAsync(Dept,"rec-1")).ReturnsAsync(new RmsOperationalRecord {DepartmentId=Dept,PurgedOn=DateTime.UtcNow}); await denied.Should().ThrowAsync<InvalidOperationException>();
			_inventory.Verify(i=>i.SaveInventoryAsync(It.IsAny<Inventory>(),It.IsAny<CancellationToken>()),Times.Never); _references.Should().BeEmpty();
		}
		[Test]
		public async Task Usage_is_written_as_an_external_reference_without_a_legacy_row()
		{
			var usage = await _adapter.RecordUsageAsync(Dept, "u1", "rec-1", 77, 2.5m, "Two lengths of 1.75");

			usage.Source.Should().Be(RmsInventoryUsage.SourceRecord);
			usage.InventoryId.Should().Be(77);
			usage.Quantity.Should().Be(2.5m);

			var row = _references.Single();
			row.SemanticRole.Should().Be("InventoryUsage");
			row.SourceSubsystem.Should().Be("Inventory");
			row.SourceEntityId.Should().Be("77");
			row.RecordId.Should().Be("rec-1");
			row.Checksum.Should().HaveLength(64);
			row.SnapshotJson.Should().Contain("\"Quantity\":2.5");
		}

		[Test]
		public async Task Usage_reads_back_for_the_record_and_ignores_other_reference_roles()
		{
			await _adapter.RecordUsageAsync(Dept, "u1", "rec-1", 77, 1m, null);
			await _adapter.RecordUsageAsync(Dept, "u1", "rec-1", 78, 3m, "foam");
			_references.Add(new RmsExternalReference { RmsExternalReferenceId = "x", RecordId = "rec-1", SemanticRole = "LinkedCall", SourceEntityId = "9", CapturedOn = DateTime.UtcNow });

			var usage = await _adapter.GetUsageForRecordAsync(Dept, "rec-1");

			usage.Select(u => u.InventoryId).Should().Equal(new[] { 77, 78 });
			usage.Sum(u => u.Quantity).Should().Be(4m);
		}

		[Test]
		public async Task Legacy_logs_report_no_usage_because_the_schema_carries_none()
		{
			(await _adapter.GetUsageForLegacyLogAsync(Dept, 123)).Should().BeEmpty();
		}

		[Test]
		public async Task Terminal_records_and_bad_input_are_refused()
		{
			await _adapter.Invoking(a => a.RecordUsageAsync(Dept, "u1", "rec-void", 77, 1m, null)).Should().ThrowAsync<InvalidOperationException>();
			await _adapter.Invoking(a => a.RecordUsageAsync(Dept, "u1", "rec-1", 77, 0m, null)).Should().ThrowAsync<ArgumentException>();
			await _adapter.Invoking(a => a.RecordUsageAsync(Dept, "u1", "rec-1", 0, 1m, null)).Should().ThrowAsync<ArgumentException>();
			_references.Should().BeEmpty();
		}
	}
}
