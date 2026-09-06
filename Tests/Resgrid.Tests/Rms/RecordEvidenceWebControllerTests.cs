using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Services.Records;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.Records;

namespace Resgrid.Tests.Rms
{
	[TestFixture, NonParallelizable]
	public class RecordEvidenceWebControllerTests
	{
		private Mock<IRecordEvidenceSelectionService> _selection;
		private Mock<IRecordsEvidenceService> _evidence;
		private Mock<IRecordsService> _records;
		private RecordEvidenceContext _context;
		private RecordEvidenceController _controller;
		[SetUp]
		public void Setup()
		{
			_selection = new(); _evidence = new(); _records = new();
			_context = new RecordEvidenceContext { RecordId = "record", RecordKind = RmsRecordKind.Operational, RowVersion = 7, CallId = 501, CanCapture = true, CanViewRestricted = true, CanExport = true };
			_selection.Setup(s => s.GetContextAsync(9, "officer", "record", RmsRecordKind.Operational)).ReturnsAsync(() => _context);
			var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] {
				new Claim(ClaimTypes.PrimarySid, "officer"), new Claim(ClaimTypes.PrimaryGroupSid, "9") }, "test")) };
			http.Connection.RemoteIpAddress = IPAddress.Loopback;
			Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			_controller = new RecordEvidenceController(_selection.Object, _evidence.Object, _records.Object) {
				ControllerContext = new ControllerContext { HttpContext = http }, TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>()) };
		}
		[TearDown] public void Cleanup() => Resgrid.Web.Helpers.ClaimsAuthorizationHelper._httpContextAccessor = null;
		private static RecordEvidenceForm Input() => new() { RecordId = "record", RecordKind = RmsRecordKind.Operational,
			RowVersion = 7, SourceKind = RmsEvidenceKind.ChatPromotion, SourceIds = new() { "one", "two" }, CaptureReason = "Selected incident messages" };
		private static RmsEvidenceArtifact Artifact() => new() { DepartmentId = 9, RecordId = "record", RecordKind = (int)RmsRecordKind.Operational,
			RmsEvidenceArtifactId = "artifact", RevisionId = "signed", Classification = (int)RmsEvidenceClassification.Restricted,
			Title = "Sensitive title", CaptureReason = "Sensitive reason", SourceVersion = "secret-source-version", ManifestJson = "{\"body\":\"Selected message\"}", Checksum = RecordSnapshotSerializer.Checksum("{\"body\":\"Selected message\"}") };

		[Test]
		public async Task Missing_and_stale_form_versions_cannot_capture_or_silently_adopt_the_current_draft()
		{
			var input = Input(); input.RowVersion = null;
			(await _controller.Capture(input, default)).Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(428);
			input.RowVersion = 6; (await _controller.Capture(input, default)).Should().BeOfType<RedirectToActionResult>();
			_controller.TempData["EvidenceMessage"].ToString().Should().Contain("draft changed");
			_evidence.VerifyNoOtherCalls();
		}

		[TestCase(RmsEvidenceKind.CertificationSnapshot)]
		[TestCase(RmsEvidenceKind.ChatPromotion)]
		[TestCase(RmsEvidenceKind.TrackingFix)]
		public async Task No_selection_cannot_fall_back_to_all_record_participants(RmsEvidenceKind kind)
		{
			var input = Input(); input.SourceKind = kind; input.SourceIds.Clear();
			(await _controller.Capture(input, default)).Should().BeOfType<BadRequestObjectResult>(); _evidence.VerifyNoOtherCalls();
		}

		[Test]
		public async Task Capture_uses_posted_selection_and_version_with_server_actor_tenant_call_and_utc()
		{
			var input = Input(); input.StartUtc = new DateTime(2026, 9, 1, 8, 0, 0); input.EndUtc = input.StartUtc.Value.AddHours(1);
			(await _controller.Capture(input, default)).Should().BeOfType<RedirectToActionResult>();
			_evidence.Verify(e => e.CaptureAsync(It.Is<RecordEvidenceCaptureRequest>(r => r.DepartmentId == 9 && r.CapturedByUserId == "officer" && r.CallId == 501
				&& r.ExpectedRowVersion == 7 && r.SourceIds.Count == 2 && r.CoverageStart.Value.Kind == DateTimeKind.Utc && r.OriginClient == RmsOriginClient.Web), true, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Signed_evidence_history_uses_all_revision_query_and_restricts_the_entire_metadata_entry_after_audit()
		{
			_evidence.Setup(e => e.GetHistoryAsync(9, "record", 0, 51)).ReturnsAsync(new List<RmsEvidenceArtifact> { Artifact() });
			_records.Setup(r => r.RecordAccessAsync(9, "officer", "record", null, RmsAccessAuditAction.Read, It.IsAny<string>(), It.IsAny<string>(), RmsOriginClient.Web))
				.Callback(() => _context.CanViewRestricted = false).Returns(Task.CompletedTask);
			var result = (ViewResult)await _controller.Index("record", RmsRecordKind.Operational);
			var entry = ((RecordEvidenceView)result.Model).Artifacts.Should().ContainSingle().Which;
			entry.Withheld.Should().BeTrue(); entry.Id.Should().BeNull(); entry.Title.Should().BeNull(); entry.Reason.Should().BeNull(); entry.Checksum.Should().BeNull(); entry.RevisionId.Should().BeNull(); entry.SourceVersion.Should().BeNull();
		}

		[TestCase("restricted")]
		[TestCase("export")]
		[TestCase("record")]
		[TestCase("purged")]
		[TestCase("checksum")]
		[TestCase("tenant")]
		public async Task Manifest_rechecks_grants_parent_and_integrity_after_audit_before_returning_bytes(string boundary)
		{
			var artifact = Artifact(); _evidence.Setup(e => e.GetAsync(9, "artifact")).ReturnsAsync(() => artifact);
			_records.Setup(r => r.RecordAccessAsync(9, "officer", "record", "signed", RmsAccessAuditAction.Export, It.IsAny<string>(), It.IsAny<string>(), RmsOriginClient.Web))
				.Callback(() => {
					if (boundary == "restricted") _context.CanViewRestricted = false;
					if (boundary == "export") _context.CanExport = false;
					if (boundary == "record") _selection.Setup(s => s.GetContextAsync(9, "officer", "record", RmsRecordKind.Operational)).ThrowsAsync(new UnauthorizedAccessException());
					if (boundary == "purged") artifact = null;
					if (boundary == "checksum") artifact.ManifestJson = "{}";
					if (boundary == "tenant") artifact.DepartmentId = 10;
				}).Returns(Task.CompletedTask);
			var result = await _controller.Manifest("record", RmsRecordKind.Operational, "artifact");
			if (boundary == "checksum") result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(409);
			else result.Should().BeOfType<ForbidResult>();
		}

		[Test]
		public async Task A_verified_signed_manifest_can_be_retrieved_and_is_not_cached()
		{
			var artifact = Artifact(); _evidence.Setup(e => e.GetAsync(9, "artifact")).ReturnsAsync(artifact);
			var file = (FileContentResult)await _controller.Manifest("record", RmsRecordKind.Operational, "artifact");
			System.Text.Encoding.UTF8.GetString(file.FileContents).Should().Be(artifact.ManifestJson);
			_controller.Response.Headers.CacheControl.ToString().Should().Be("no-store");
		}
	}
}
