using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// Worker 43 (RMS plan RMS-3): retention, legal hold, attachment purge, and the rescan pass for attachments the
	/// scanner could not reach at upload time — the RMS-1 gap this package closes. A purge leaves a content-free
	/// tombstone (plan 4.9) and a legal hold refuses one loudly rather than skipping it in silence.
	/// </summary>
	[TestFixture]
	public class RecordsRetentionServiceTests
	{
		private const int Dept = 11;

		private FakeRmsStore _store;
		private FakeIncidentStore _incidents;
		private Mock<IDepartmentSettingsService> _settings;
		private Mock<IRecordAttachmentScanner> _scanner;
		private RecordsRetentionPolicy _policy;
		private RecordsRetentionService _service;

		[SetUp]
		public void SetUp()
		{
			_store = new FakeRmsStore();
			_incidents = new FakeIncidentStore();
			_store.SeedActiveCutover(Dept);

			_policy = new RecordsRetentionPolicy { DepartmentDefaultYears = 7 };
			_settings = new Mock<IDepartmentSettingsService>();
			_settings.Setup(s => s.GetRecordsRetentionPolicyAsync(Dept, It.IsAny<bool>())).ReturnsAsync(() => _policy);

			_scanner = new Mock<IRecordAttachmentScanner>();
			_scanner.Setup(s => s.ScanAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new RecordAttachmentScanResult { State = RmsAttachmentScanState.Clean });

			_service = new RecordsRetentionService(_store.CutoversRepo.Object, _store.RecordsRepo.Object,
				_incidents.ReportsRepo.Object, _store.DetailsRepo.Object, _store.AttachmentsRepo.Object,
				_store.LegalHoldsRepo.Object, _store.AuditsRepo.Object, _store.ProjectionsRepo.Object,
				_scanner.Object, _settings.Object);
		}

		private RmsOperationalRecord SeedFinalized(DateTime finalizedOn, string definitionKey = null)
		{
			var id = Guid.NewGuid().ToString();
			var record = new RmsOperationalRecord
			{
				RmsOperationalRecordId = id,
				DepartmentId = Dept,
				ProtectionId = Guid.NewGuid().ToString(),
				DefinitionKey = definitionKey ?? RmsDefinitionKeys.Training,
				DefinitionVersion = 1,
				RecordType = (int)RmsOperationalRecordType.Training,
				State = (int)RmsRecordState.Finalized,
				RecordNumber = "TRN-2016-0001",
				DisplaySummary = "Ladder drill",
				AuthorUserId = "author",
				StartedOn = finalizedOn,
				FinalizedOn = finalizedOn,
				CreatedOn = finalizedOn,
				ModifiedOn = finalizedOn,
				RowVersion = 1
			};
			_store.Records.Add(record);
			_store.Details.Add(new RmsOperationalRecordDetail
			{
				RmsOperationalRecordDetailId = Guid.NewGuid().ToString(),
				DepartmentId = Dept,
				ProtectionId = Guid.NewGuid().ToString(),
				RecordId = id,
				Narrative = "Crew drilled ladder throws for two hours.",
				Location = "Station 1",
				CreatedOn = finalizedOn,
				ModifiedOn = finalizedOn,
				RowVersion = 1
			});
			_store.Projections.Add(new RmsRecordSearchProjection
			{
				RmsRecordSearchProjectionId = id,
				DepartmentId = Dept,
				SourceId = id,
				RecordKind = (int)RmsRecordKind.Operational,
				DefinitionKey = record.DefinitionKey,
				State = record.State,
				DisplaySummary = "Ladder drill",
				SearchText = "Crew drilled ladder throws for two hours.",
				RecordCreatedOn = finalizedOn,
				ModifiedOn = finalizedOn
			});
			return record;
		}

		private RmsRecordAttachment SeedAttachment(string recordId, RmsAttachmentScanState scanState)
		{
			var attachment = new RmsRecordAttachment
			{
				RmsRecordAttachmentId = Guid.NewGuid().ToString(),
				DepartmentId = Dept,
				ProtectionId = Guid.NewGuid().ToString(),
				RecordId = recordId,
				FileName = "photo.jpg",
				ContentType = "image/jpeg",
				Data = Encoding.UTF8.GetBytes("bytes"),
				ByteSize = 5,
				ScanState = (int)scanState,
				UploadedOn = DateTime.UtcNow.AddDays(-1),
				CreatedOn = DateTime.UtcNow.AddDays(-1),
				ModifiedOn = DateTime.UtcNow.AddDays(-1),
				RowVersion = 1
			};
			_store.Attachments.Add(attachment);
			return attachment;
		}

		[Test]
		public async Task A_record_inside_its_retention_period_is_untouched()
		{
			var record = SeedFinalized(DateTime.UtcNow.AddYears(-3));

			var result = await _service.SweepAsync();

			result.RecordsPurged.Should().Be(0);
			record.PurgedOn.Should().BeNull();
			_store.Details.Single().Narrative.Should().NotBeNull();
		}

		[Test]
		public async Task A_record_past_retention_becomes_a_content_free_tombstone()
		{
			var record = SeedFinalized(DateTime.UtcNow.AddYears(-9));
			var attachment = SeedAttachment(record.RmsOperationalRecordId, RmsAttachmentScanState.Clean);

			var result = await _service.SweepAsync();

			result.RecordsPurged.Should().Be(1);
			result.AttachmentsPurged.Should().Be(1);

			// Identity survives so a reference still resolves; content does not.
			record.PurgedOn.Should().NotBeNull();
			record.RecordNumber.Should().Be("TRN-2016-0001");
			record.DisplaySummary.Should().Be(RecordsRetentionService.PurgedPlaceholder);
			_store.Details.Single().Narrative.Should().BeNull();
			_store.Details.Single().Location.Should().BeNull();
			_store.Details.Single().RecordId.Should().Be(record.RmsOperationalRecordId, "keys are not content");
			attachment.Data.Should().BeNull();
			attachment.DeletedOn.Should().NotBeNull();

			// The queue and the index stop showing what the department no longer holds.
			_store.Projections.Single().DisplaySummary.Should().Be(RecordsRetentionService.PurgedPlaceholder);
			_store.Projections.Single().SearchText.Should().BeNull();

			_store.Audits.Should().Contain(a => a.Purpose == RecordsRetentionService.PurgeAuditPurpose);
		}

		[Test]
		public async Task A_legal_hold_refuses_the_purge_and_says_so_in_the_audit()
		{
			var record = SeedFinalized(DateTime.UtcNow.AddYears(-9));
			_store.LegalHolds.Add(new RmsRecordLegalHold
			{
				RmsRecordLegalHoldId = Guid.NewGuid().ToString(),
				DepartmentId = Dept,
				RecordId = record.RmsOperationalRecordId,
				Reason = RmsLegalHoldReasons.Litigation,
				ReferenceNumber = "CV-2026-14",
				PlacedByUserId = "admin",
				PlacedOn = DateTime.UtcNow.AddMonths(-2),
				CreatedOn = DateTime.UtcNow.AddMonths(-2),
				ModifiedOn = DateTime.UtcNow.AddMonths(-2),
				RowVersion = 1
			});

			var result = await _service.SweepAsync();

			result.RecordsPurged.Should().Be(0);
			result.HeldByLegalHold.Should().Be(1);
			record.PurgedOn.Should().BeNull();
			_store.Details.Single().Narrative.Should().NotBeNull();
			_store.Audits.Should().Contain(a => a.Purpose == RecordsRetentionService.HeldAuditPurpose,
				"a refused purge must be visible; a silent skip looks like a sweep that never ran");
		}

		[Test]
		public async Task A_released_hold_no_longer_protects_the_record()
		{
			var record = SeedFinalized(DateTime.UtcNow.AddYears(-9));
			_store.LegalHolds.Add(new RmsRecordLegalHold
			{
				RmsRecordLegalHoldId = Guid.NewGuid().ToString(),
				DepartmentId = Dept,
				RecordId = record.RmsOperationalRecordId,
				Reason = RmsLegalHoldReasons.Investigation,
				PlacedOn = DateTime.UtcNow.AddMonths(-6),
				ReleasedOn = DateTime.UtcNow.AddDays(-1),
				ReleasedByUserId = "admin",
				CreatedOn = DateTime.UtcNow.AddMonths(-6),
				ModifiedOn = DateTime.UtcNow.AddDays(-1),
				RowVersion = 1
			});

			(await _service.SweepAsync()).RecordsPurged.Should().Be(1);
		}

		[Test]
		public async Task A_restricted_class_is_never_purged_because_it_retains_permanently()
		{
			// Coroner is a restricted class; plan 4.9 gives it permanent retention with no department default.
			var record = SeedFinalized(DateTime.UtcNow.AddYears(-20), RmsDefinitionKeys.Coroner);

			var result = await _service.SweepAsync();

			result.RecordsPurged.Should().Be(0);
			record.PurgedOn.Should().BeNull();
		}

		[Test]
		public async Task A_definition_override_of_permanent_beats_the_department_default()
		{
			_policy.Overrides.Add(new RecordsRetentionOverride { DefinitionKey = RmsDefinitionKeys.Training, RetentionYears = RecordsRetentionPolicy.Permanent });
			SeedFinalized(DateTime.UtcNow.AddYears(-9));

			(await _service.SweepAsync()).RecordsPurged.Should().Be(0);
		}

		[Test]
		public async Task A_pending_attachment_is_rescanned_and_promoted_when_it_comes_back_clean()
		{
			var record = SeedFinalized(DateTime.UtcNow.AddYears(-1));
			var attachment = SeedAttachment(record.RmsOperationalRecordId, RmsAttachmentScanState.Pending);

			var result = await _service.SweepAsync();

			result.AttachmentsRescanned.Should().Be(1);
			result.AttachmentsRejectedOnRescan.Should().Be(0);
			attachment.ScanState.Should().Be((int)RmsAttachmentScanState.Clean);
			attachment.Data.Should().NotBeNull();
		}

		[Test]
		public async Task A_pending_attachment_that_rescans_dirty_loses_its_bytes()
		{
			_scanner.Setup(s => s.ScanAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new RecordAttachmentScanResult { State = RmsAttachmentScanState.Rejected, Detail = "Eicar-Test-Signature" });

			var record = SeedFinalized(DateTime.UtcNow.AddYears(-1));
			var attachment = SeedAttachment(record.RmsOperationalRecordId, RmsAttachmentScanState.Pending);

			var result = await _service.SweepAsync();

			result.AttachmentsRejectedOnRescan.Should().Be(1);
			attachment.ScanState.Should().Be((int)RmsAttachmentScanState.Rejected);
			attachment.Data.Should().BeNull("a Pending attachment that turns out to be malware was downloadable the whole time it sat unscanned");
			attachment.DeletedOn.Should().NotBeNull();
			_store.Audits.Should().Contain(a => a.Purpose == "Attachment rejected on rescan");
		}

		[Test]
		public async Task A_scanner_that_is_still_unreachable_leaves_the_attachment_pending()
		{
			_scanner.Setup(s => s.ScanAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new RecordAttachmentScanResult { State = RmsAttachmentScanState.Pending, Detail = "clamd unreachable" });

			var record = SeedFinalized(DateTime.UtcNow.AddYears(-1));
			var attachment = SeedAttachment(record.RmsOperationalRecordId, RmsAttachmentScanState.Pending);

			var result = await _service.SweepAsync();

			result.AttachmentsRescanned.Should().Be(0);
			attachment.ScanState.Should().Be((int)RmsAttachmentScanState.Pending, "the next sweep tries again");
			attachment.Data.Should().NotBeNull();
		}

		[Test]
		public async Task A_scanner_that_throws_does_not_stop_the_sweep()
		{
			_scanner.Setup(s => s.ScanAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("socket closed"));

			// The Pending attachment hangs off a record still inside retention, so it survives to be rescanned;
			// the expired record is a separate one, which is what proves the two passes are independent.
			var current = SeedFinalized(DateTime.UtcNow.AddYears(-1));
			SeedAttachment(current.RmsOperationalRecordId, RmsAttachmentScanState.Pending);
			SeedFinalized(DateTime.UtcNow.AddYears(-9));

			var result = await _service.SweepAsync();

			result.Errors.Should().Be(1);
			result.RecordsPurged.Should().Be(1, "the retention pass runs regardless of the rescan pass");
		}
	}
}
