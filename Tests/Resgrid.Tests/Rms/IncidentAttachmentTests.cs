using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	[TestFixture]
	public class IncidentAttachmentTests
	{
		private FakeIncidentStore _store;
		private Mock<IRecordsAuthorizationService> _auth;
		private Mock<IRecordAttachmentScanner> _scanner;
		private IncidentAttachmentsService _service;
		[SetUp]
		public void Setup()
		{
			_store = new FakeIncidentStore(); _auth = new Mock<IRecordsAuthorizationService>(); _scanner = new Mock<IRecordAttachmentScanner>();
			_auth.Setup(a => a.CanUserViewRecordAsync("officer", "report", 1)).ReturnsAsync(true); _auth.Setup(a => a.HasPermissionAsync("officer", 1, It.IsAny<PermissionTypes>())).ReturnsAsync(true);
			_scanner.Setup(s => s.ScanAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>())).ReturnsAsync(new RecordAttachmentScanResult { State = RmsAttachmentScanState.Clean });
			_store.Reports.Add(new RmsIncidentReport { DepartmentId = 1, RmsIncidentReportId = "report", AuthorUserId = "officer", State = (int)RmsRecordState.Draft, RowVersion = 1 });
			_service = new IncidentAttachmentsService(_store.ReportsRepo.Object, _store.Shared.AttachmentsRepo.Object, _store.Shared.RevisionsRepo.Object, _store.Shared.AuditsRepo.Object, _auth.Object, _scanner.Object, _store.UnitOfWork.Object);
		}
		private Task<RmsRecordAttachment> Add() => _service.AddAsync(1, "officer", "report", 1, "scene.txt", "text/plain", Encoding.UTF8.GetBytes("scene evidence"), "Scene notes");

		[Test]
		public async Task Upload_download_and_revision_membership_preserve_bytes_and_checksums()
		{
			var metadata = await Add(); metadata.Data.Should().BeNull();
			var file = await _service.GetAsync(1, "officer", "report", metadata.RmsRecordAttachmentId); Encoding.UTF8.GetString(file.Data).Should().Be("scene evidence");
			_store.Reports[0].RowVersion.Should().Be(2);
			var json = JsonConvert.SerializeObject(new { Attachments = new[] { metadata } });
			_store.Revisions.Add(new RmsRevision { DepartmentId = 1, RecordId = "report", RecordKind = 2, RmsRevisionId = "r1", SnapshotJson = json, Checksum = RecordSnapshotSerializer.Checksum(json) });
			(await _service.GetAsync(1, "officer", "report", metadata.RmsRecordAttachmentId, "r1")).Should().NotBeNull();
			await _service.RemoveAsync(1, "officer", "report", metadata.RmsRecordAttachmentId, 2);
			(await _service.GetAsync(1, "officer", "report", metadata.RmsRecordAttachmentId)).Should().BeNull();
			(await _service.GetAsync(1, "officer", "report", metadata.RmsRecordAttachmentId, "r1")).Data.Should().Equal(Encoding.UTF8.GetBytes("scene evidence"));
			_store.Revisions[0].SnapshotJson = "{\"Attachments\":[]}"; _store.Revisions[0].Checksum = RecordSnapshotSerializer.Checksum(_store.Revisions[0].SnapshotJson);
			(await _service.GetAsync(1, "officer", "report", metadata.RmsRecordAttachmentId, "r1")).Should().BeNull();
		}
		[Test]
		public async Task Inflight_upload_cannot_attach_to_a_newly_finalized_or_purged_report()
		{
			_scanner.Setup(s => s.ScanAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => { _store.Reports[0].State = (int)RmsRecordState.Finalized; _store.Reports[0].RowVersion++; return new RecordAttachmentScanResult { State = RmsAttachmentScanState.Clean }; });
			Func<Task> write = () => Add(); await write.Should().ThrowAsync<InvalidOperationException>(); _store.Shared.Attachments.Should().BeEmpty();
		}
		[Test]
		public async Task Retained_view_and_create_permissions_do_not_allow_a_demoted_admin_to_change_another_officers_files()
		{
			var metadata = await Add();
			_store.Reports[0].AuthorUserId = "other";
			Func<Task> add = () => Add(); await add.Should().ThrowAsync<UnauthorizedAccessException>();
			Func<Task> remove = () => _service.RemoveAsync(1, "officer", "report", metadata.RmsRecordAttachmentId, 2); await remove.Should().ThrowAsync<UnauthorizedAccessException>();
			_store.Shared.Attachments.Single().DeletedOn.Should().BeNull();
		}
		[Test]
		public async Task Revoked_access_tampered_content_and_pending_scans_prevent_download()
		{
			var metadata = await Add(); var stored = _store.Shared.Attachments.Single();
			stored.ScanState = (int)RmsAttachmentScanState.Pending;
			Func<Task> read = () => _service.GetAsync(1, "officer", "report", metadata.RmsRecordAttachmentId); await read.Should().ThrowAsync<InvalidOperationException>();
			stored.ScanState = (int)RmsAttachmentScanState.Clean; stored.Data = Encoding.UTF8.GetBytes("tampered"); await read.Should().ThrowAsync<InvalidOperationException>();
			_auth.Setup(a => a.CanUserViewRecordAsync("officer", "report", 1)).ReturnsAsync(false); await read.Should().ThrowAsync<UnauthorizedAccessException>();
		}

		[Test]
		public async Task Unclassified_or_restricted_files_require_live_restricted_permission_for_reads_and_removal()
		{
			var metadata = await Add(); _store.Shared.Attachments.Single().Classification = null;
			_auth.Setup(a => a.HasPermissionAsync("officer", 1, PermissionTypes.ViewRestrictedRecords)).ReturnsAsync(false);
			Func<Task> read = () => _service.GetAsync(1, "officer", "report", metadata.RmsRecordAttachmentId); await read.Should().ThrowAsync<UnauthorizedAccessException>();
			Func<Task> remove = () => _service.RemoveAsync(1, "officer", "report", metadata.RmsRecordAttachmentId, 2); await remove.Should().ThrowAsync<UnauthorizedAccessException>();
			_store.Shared.Attachments.Single().DeletedOn.Should().BeNull();
		}
	}
}
