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

		[SetUp]
		public void SetUp()
		{
			_references = new List<RmsExternalReference>();
			_referencesRepo = new Mock<IRmsExternalReferencesRepository>();
			_referencesRepo.Setup(r => r.GetForRecordAsync(Dept, It.IsAny<string>())).ReturnsAsync((int d, string id) => _references.Where(x => x.RecordId == id).ToList());
			_referencesRepo.Setup(r => r.InsertAsync(It.IsAny<RmsExternalReference>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((RmsExternalReference e, CancellationToken c, bool f) => { _references.Add(e); return e; });

			_records = new Mock<IRmsOperationalRecordsRepository>();
			_records.Setup(r => r.GetByIdForDepartmentAsync(Dept, "rec-1")).ReturnsAsync(new RmsOperationalRecord { RmsOperationalRecordId = "rec-1", DepartmentId = Dept, State = (int)RmsRecordState.Draft });
			_records.Setup(r => r.GetByIdForDepartmentAsync(Dept, "rec-void")).ReturnsAsync(new RmsOperationalRecord { RmsOperationalRecordId = "rec-void", DepartmentId = Dept, State = (int)RmsRecordState.Voided });

			_adapter = new RmsInventoryUsageAdapter(_referencesRepo.Object, _records.Object);
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
