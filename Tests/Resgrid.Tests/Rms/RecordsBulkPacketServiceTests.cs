using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// Bulk packets and bulk assign-for-review (RMS plan section 4.7): authorized-selection semantics with per-record
	/// skips, one stored ADP-sealed export run per packet, per-record Export audits, optional email delivery, and the
	/// review assignment that touches only Records awaiting review.
	/// </summary>
	[TestFixture]
	public class RecordsBulkPacketServiceTests
	{
		private const int Dept = 9;
		private const string Exporter = "exporter";
		private Mock<IRecordsDocumentService> _documents;
		private Mock<IRecordsService> _records;
		private Mock<IRmsOperationalRecordsRepository> _rows;
		private Mock<IRmsExportRunsRepository> _runs;
		private Mock<IRecordsAuthorizationService> _authorization;
		private Mock<IRmsAccessAuditsRepository> _audits;
		private Mock<IEmailService> _email;
		private Mock<IPdfProvider> _pdf;
		private Mock<IUnitOfWork> _unitOfWork;
		private PassthroughRecordsProtection _protection;
		private List<RmsExportRun> _storedRuns;
		private List<RmsAccessAudit> _storedAudits;
		private RecordsBulkPacketService _service;

		[SetUp]
		public void SetUp()
		{
			_documents = new Mock<IRecordsDocumentService>();
			_records = new Mock<IRecordsService>();
			_rows = new Mock<IRmsOperationalRecordsRepository>();
			_runs = new Mock<IRmsExportRunsRepository>();
			_authorization = new Mock<IRecordsAuthorizationService>();
			_audits = new Mock<IRmsAccessAuditsRepository>();
			_email = new Mock<IEmailService>();
			_pdf = new Mock<IPdfProvider>();
			_unitOfWork = new Mock<IUnitOfWork>();
			_protection = new PassthroughRecordsProtection();
			_storedRuns = new List<RmsExportRun>();
			_storedAudits = new List<RmsAccessAudit>();

			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = Dept, Name = "Pine Valley Fire" });
			_authorization.Setup(a => a.HasPermissionAsync(Exporter, Dept, PermissionTypes.ExportRecords)).ReturnsAsync(true);
			_authorization.Setup(a => a.HasPermissionAsync(Exporter, Dept, PermissionTypes.ReviewRecords)).ReturnsAsync(true);
			_authorization.Setup(a => a.IsActiveMemberAsync(It.IsAny<string>(), Dept)).ReturnsAsync(true);
			_authorization.Setup(a => a.CanUserViewRecordAsync(Exporter, It.IsAny<string>(), Dept)).ReturnsAsync((string u, string id, int d) => id != "hidden");
			_pdf.Setup(p => p.ConvertHtmlToPdf(It.IsAny<string>(), It.IsAny<string>())).Returns((string html, string size) => System.Text.Encoding.UTF8.GetBytes("%PDF-" + html));
			_runs.Setup(r => r.InsertAsync(It.IsAny<RmsExportRun>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((RmsExportRun run, CancellationToken c, bool b) => { _storedRuns.Add(run); return run; });
			_runs.Setup(r => r.GetWithDataAsync(Dept, It.IsAny<string>())).ReturnsAsync((int d, string id) => _storedRuns.FirstOrDefault(r => r.RmsExportRunId == id));
			_audits.Setup(a => a.InsertAsync(It.IsAny<RmsAccessAudit>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((RmsAccessAudit audit, CancellationToken c, bool b) => { _storedAudits.Add(audit); return audit; });
			_email.Setup(e => e.SendReportDeliveryEmail(It.IsAny<EmailNotification>())).ReturnsAsync(true);

			var rows = new[] { Row("r1", "RG-1"), Row("r2", "RG-2"), Row("hidden", "RG-3"), Row("draft", null, revision: null) };
			_rows.Setup(r => r.GetByIdsAsync(Dept, It.IsAny<IEnumerable<string>>())).ReturnsAsync((int d, IEnumerable<string> ids) => rows.Where(r => ids.Contains(r.RmsOperationalRecordId)).ToList());
			_documents.Setup(d => d.GetAsync(Dept, Exporter, It.IsAny<string>(), RmsRecordKind.Operational, It.IsAny<string>(), true))
				.ReturnsAsync((int d, string u, string id, RmsRecordKind k, string rev, bool e) => new RecordDocument { RecordId = id, RecordNumber = rows.First(r => r.RmsOperationalRecordId == id).RecordNumber, RevisionId = rev, RevisionNumber = 1, FinalizedOn = new DateTime(2026, 9, 1), OriginalChecksum = "chk-" + id, ContentChecksum = "chk-" + id, ContentJson = "{}" });
			_documents.Setup(d => d.RenderHtmlAsync(Dept, Exporter, It.IsAny<RecordDocument>())).ReturnsAsync((int d, string u, RecordDocument doc) => "<html><body><p>Record " + doc.RecordNumber + "</p></body></html>");

			_service = new RecordsBulkPacketService(_documents.Object, _records.Object, _rows.Object, _runs.Object, _authorization.Object, _protection, _audits.Object, departments.Object, _email.Object, _pdf.Object, _unitOfWork.Object);
		}

		private static RmsOperationalRecord Row(string id, string number, string revision = "rev")
			=> new RmsOperationalRecord { RmsOperationalRecordId = id, DepartmentId = Dept, RecordNumber = number, DefinitionKey = "shift-log", DefinitionVersion = 1, CurrentRevisionId = revision == null ? null : revision + "-" + id, State = (int)RmsRecordState.Finalized };

		[Test]
		public async Task Compiled_pdf_packet_skips_unauthorized_or_unfinalized_records_stores_one_sealed_run_and_audits_each_record()
		{
			var result = await _service.BuildPacketAsync(Dept, Exporter, new RecordsBulkPacketRequest { RecordIds = new List<string> { "r1", "r2", "hidden", "draft", "missing", "r1" }, Title = "Board packet", Purpose = "Quarterly board review" });

			result.Processed.Should().Be(2);
			result.Skips.Select(s => s.RecordId + ":" + s.Reason).Should().BeEquivalentTo("hidden:not_visible", "draft:no_revision", "missing:not_found");
			result.Run.TemplateKey.Should().Be(RecordsBulkPacketService.PacketTemplateKey);
			result.Run.Trigger.Should().Be((int)RmsExportTrigger.Bulk);
			result.Run.ContentType.Should().Be("application/pdf");
			result.Run.FileName.Should().StartWith("Board-packet-").And.EndWith(".pdf");
			result.Run.RecordCount.Should().Be(2);
			result.Run.ExpiresOn.Should().BeCloseTo(DateTime.UtcNow.AddDays(RecordsBulkPacketService.RunRetentionDays), TimeSpan.FromMinutes(1));
			System.Text.Encoding.UTF8.GetString(result.Run.Data).Should().Contain("Manifest").And.Contain("RG-1").And.Contain("RG-2").And.Contain("Record RG-2").And.Contain("Packet item 2 of 2");
			result.Delivered.Should().BeFalse();

			_storedRuns.Should().ContainSingle();
			_protection.Writes.Should().Contain("export-run", "the stored bytes go through the ADP seam");
			_storedAudits.Should().HaveCount(2);
			_storedAudits.Select(a => a.RecordId).Should().BeEquivalentTo("r1", "r2");
			_storedAudits.Should().OnlyContain(a => a.Action == (int)RmsAccessAuditAction.Export && a.Purpose == "Quarterly board review");
			_unitOfWork.Verify(u => u.CommitChanges(), Times.Once);
			_email.Verify(e => e.SendReportDeliveryEmail(It.IsAny<EmailNotification>()), Times.Never);
		}

		[Test]
		public async Task Bundle_packet_zips_one_pdf_per_record_with_a_manifest_and_optionally_rides_the_report_email_path()
		{
			var result = await _service.BuildPacketAsync(Dept, Exporter, new RecordsBulkPacketRequest { RecordIds = new List<string> { "r2", "r1" }, Mode = RecordsBulkPacketMode.Bundle, Title = "Insurance", DeliverToEmail = "claims@example.org" });

			result.Run.ContentType.Should().Be("application/zip");
			using var archive = new ZipArchive(new MemoryStream(result.Run.Data), ZipArchiveMode.Read);
			archive.Entries.Select(e => e.Name).Should().BeEquivalentTo("manifest.json", "001-RG-2.pdf", "002-RG-1.pdf");
			using (var reader = new StreamReader(archive.GetEntry("manifest.json").Open()))
				reader.ReadToEnd().Should().Contain("RG-2").And.Contain("chk-r1");
			result.Delivered.Should().BeTrue();
			_email.Verify(e => e.SendReportDeliveryEmail(It.Is<EmailNotification>(n => n.To == "claims@example.org" && n.AttachmentName == result.Run.FileName && n.AttachmentData.Length == result.Run.Data.Length)), Times.Once);

			var download = await _service.GetPacketAsync(Dept, Exporter, result.Run.RmsExportRunId);
			download.Should().NotBeNull();
			download.FileName.Should().Be(result.Run.FileName);
			(await _service.GetPacketAsync(Dept, Exporter, "nope")).Should().BeNull();
		}

		[Test]
		public async Task Packets_fail_closed_on_permission_size_email_and_empty_selections()
		{
			_authorization.Setup(a => a.HasPermissionAsync("viewer", Dept, PermissionTypes.ExportRecords)).ReturnsAsync(false);
			Func<Task> denied = () => _service.BuildPacketAsync(Dept, "viewer", new RecordsBulkPacketRequest { RecordIds = new List<string> { "r1" } });
			await denied.Should().ThrowAsync<UnauthorizedAccessException>();

			Func<Task> tooMany = () => _service.BuildPacketAsync(Dept, Exporter, new RecordsBulkPacketRequest { RecordIds = Enumerable.Range(0, RecordsBulkPacketRequest.MaxRecords + 1).Select(i => "id" + i).ToList() });
			await tooMany.Should().ThrowAsync<ArgumentException>();

			Func<Task> badEmail = () => _service.BuildPacketAsync(Dept, Exporter, new RecordsBulkPacketRequest { RecordIds = new List<string> { "r1" }, DeliverToEmail = "not an address" });
			await badEmail.Should().ThrowAsync<ArgumentException>();

			Func<Task> nothing = () => _service.BuildPacketAsync(Dept, Exporter, new RecordsBulkPacketRequest { RecordIds = new List<string> { "hidden", "missing" } });
			(await nothing.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("hidden (not_visible)");
			_storedRuns.Should().BeEmpty();
			_unitOfWork.Verify(u => u.CommitChanges(), Times.Never);
		}

		[Test]
		public async Task Assign_for_review_touches_only_records_awaiting_review_and_reports_the_rest_as_skips()
		{
			_records.Setup(r => r.AssignReviewerAsync(Dept, Exporter, "r1", "reviewer", "rotation", It.IsAny<CancellationToken>())).ReturnsAsync(new RecordAggregate());
			_records.Setup(r => r.AssignReviewerAsync(Dept, Exporter, "draft", "reviewer", "rotation", It.IsAny<CancellationToken>())).ThrowsAsync(new RecordTransitionException("draft", RmsRecordState.Draft, RmsRecordState.Draft, "only a Record awaiting review can be assigned a reviewer"));
			_records.Setup(r => r.AssignReviewerAsync(Dept, Exporter, "hidden", "reviewer", "rotation", It.IsAny<CancellationToken>())).ThrowsAsync(new UnauthorizedAccessException());

			var result = await _service.AssignForReviewAsync(Dept, Exporter, new RecordsBulkAssignRequest { RecordIds = new List<string> { "r1", "draft", "hidden" }, ReviewerUserId = "reviewer", Reason = "rotation" });

			result.Processed.Should().Be(1);
			result.Skips.Select(s => s.RecordId + ":" + s.Reason).Should().BeEquivalentTo("draft:not_awaiting_review", "hidden:not_visible");
			result.Run.Should().BeNull();

			_authorization.Setup(a => a.IsActiveMemberAsync("gone", Dept)).ReturnsAsync(false);
			Func<Task> inactive = () => _service.AssignForReviewAsync(Dept, Exporter, new RecordsBulkAssignRequest { RecordIds = new List<string> { "r1" }, ReviewerUserId = "gone" });
			await inactive.Should().ThrowAsync<ArgumentException>();
		}
	}
}
