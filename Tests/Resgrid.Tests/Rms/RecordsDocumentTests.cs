using System;
using System.Collections.Generic;
using System.IO;
using File = System.IO.File;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Providers.PdfProvider;
using Resgrid.Services.Records;
using Resgrid.Web.Areas.User.Controllers;

namespace Resgrid.Tests.Rms
{
	[TestFixture]
	public class RecordsDocumentTests
	{
		private FakeIncidentStore _store;
		private Mock<IRecordsAuthorizationService> _auth;
		private Mock<IIncidentReportsService> _incidents;
		private Mock<IPdfProvider> _pdf;
		private RecordsDocumentService _service;
		private NerisIncidentSnapshot _snapshot;
		private Mock<IDepartmentGroupsService> _groups;
		[SetUp]
		public void Setup()
		{
			_store = new FakeIncidentStore(); _auth = new Mock<IRecordsAuthorizationService>(); _incidents = new Mock<IIncidentReportsService>(); _pdf = new Mock<IPdfProvider>();
			_auth.Setup(a => a.CanUserViewRecordAsync("officer", "report", 1)).ReturnsAsync(true);
			_auth.Setup(a => a.HasPermissionAsync("officer", 1, It.IsAny<PermissionTypes>())).ReturnsAsync(true);
			_auth.Setup(a => a.IsActiveMemberAsync("officer", 1)).ReturnsAsync(true);
			_auth.Setup(a => a.IsDepartmentAdminAsync("officer", 1)).ReturnsAsync(true);
			_groups = new Mock<IDepartmentGroupsService>();
			var report = new RmsIncidentReport { DepartmentId = 1, RmsIncidentReportId = "report", CurrentRevisionId = "r1", RecordNumber = "INC-2026-0023", IncidentNumber = "2026-00023", RowVersion = 2, State = (int)RmsRecordState.Finalized };
			_snapshot = new NerisIncidentSnapshot { Report = report, Narrative = new RmsNarrative { Narrative = "Officer verified all occupants accounted for. <script>alert('unsafe')</script>" },
				Casualties = new List<RmsCasualtyRescue> { new RmsCasualtyRescue { PersonnelUserId = "restricted-person", BirthMonthYear = "1990-04", DetailJson = "{\"private\":\"restricted-body\"}" } },
				Evidence = new List<RmsEvidenceArtifact> { new RmsEvidenceArtifact { Title = "Dispatch decision", Classification = 0, ManifestJson = "{\"decision\":\"Engine 2 selected\"}", Checksum = "fixture" }, new RmsEvidenceArtifact { Title = "Restricted source title", Classification = 1, ManifestJson = "restricted-evidence" } },
				Attachments = new List<RmsRecordAttachment> { new RmsRecordAttachment { RmsRecordAttachmentId = "photo", Classification = 0, FileName = "scene.jpg", Checksum = "fixture-attachment", ByteSize = 12 } } };
			_store.Reports.Add(JsonConvert.DeserializeObject<RmsIncidentReport>(JsonConvert.SerializeObject(report)));
			var json = JsonConvert.SerializeObject(_snapshot);
			_store.Revisions.Add(new RmsRevision { DepartmentId = 1, RecordId = "report", RecordKind = 2, RmsRevisionId = "r1", RevisionNumber = 1, SnapshotJson = json, Checksum = RecordSnapshotSerializer.Checksum(json), ActorUserId = "Officer Jones", CreatedOn = new DateTime(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc), AttestationStatementVersion = "1" });
			_incidents.Setup(s => s.BuildSnapshotAsync(1, "report", "r1")).ReturnsAsync(() => JsonConvert.DeserializeObject<NerisIncidentSnapshot>(json));
			var brand = new Mock<IDepartmentProfileMediaService>(); brand.Setup(b => b.GetBrandingAsync(1)).ReturnsAsync(new DepartmentBranding { DisplayName = "Example Fire Department", AddressText = "100 Example Street", PhoneNumber = "555-0100", Website = "example.invalid" });
			var layouts = new Mock<IRecordsPrintLayoutService>(); layouts.Setup(l => l.GetDepartmentDefaultAsync(1)).ReturnsAsync(new RmsRecordPrintLayout { Version = 3, Scope = 1, Config = new RecordsPrintLayoutConfig { LetterheadLine1 = "Fire Prevention and Emergency Response", FooterText = "Departmental record copy", WatermarkLabel = "TRAINING FIXTURE" } });
			var udf = new RecordsUdfService(Mock.Of<IRmsUdfDefinitionsRepository>(), Mock.Of<IUdfFieldRepository>(), Mock.Of<IUdfFieldValueRepository>(), _auth.Object, _groups.Object, Mock.Of<IUnitOfWork>(), Mock.Of<IDepartmentDataProtectionService>());
			_service = new RecordsDocumentService(_auth.Object, _store.Shared.RecordsRepo.Object, _store.ReportsRepo.Object, _store.AnalysesRepo.Object, _store.Shared.RevisionsRepo.Object, _incidents.Object, brand.Object, layouts.Object, _pdf.Object, Mock.Of<IRecordsEvidenceService>(), udf);
		}

		private void CaptureCustomFields()
		{
			_snapshot.CustomFields = new RecordUdfSection { DefinitionId = "extension-v1", ExtensionVersion = 1, Fields = new()
			{
				new() { Field = new() { UdfFieldId = "public", Label = "Department score", RmsClassification = 0, Visibility = 0, IsEnabled = true }, Value = "23" },
				new() { Field = new() { UdfFieldId = "admin", Label = "Confidential admin label", RmsClassification = 0, Visibility = 2, IsEnabled = true }, Value = "admin-only-value" },
				new() { Field = new() { UdfFieldId = "restricted", Label = "Restricted custom label", RmsClassification = 1, Visibility = 0, IsEnabled = true }, Value = "restricted-custom-value" }
			} };
			var json = JsonConvert.SerializeObject(_snapshot); _store.Revisions[0].SnapshotJson = json; _store.Revisions[0].Checksum = RecordSnapshotSerializer.Checksum(json);
			_incidents.Setup(s => s.BuildSnapshotAsync(1, "report", "r1")).ReturnsAsync(() => JsonConvert.DeserializeObject<NerisIncidentSnapshot>(json));
		}

		[Test]
		public async Task Address_state_is_printed_and_compared_while_header_lifecycle_state_and_storage_ids_are_omitted()
		{
			_snapshot.Location = new RmsLocation { Street = "Border Road", State = "CA", Country = "US", SourceKind = (int)RmsSourceKind.Dispatch };
			_snapshot.Report.UdfDefinitionId = "storage-form-identity";
			var json = JsonConvert.SerializeObject(_snapshot); _store.Revisions[0].SnapshotJson = json; _store.Revisions[0].Checksum = RecordSnapshotSerializer.Checksum(json);
			_incidents.Setup(s => s.BuildSnapshotAsync(1, "report", "r1")).ReturnsAsync(() => JsonConvert.DeserializeObject<NerisIncidentSnapshot>(json));
			var changed = JsonConvert.DeserializeObject<NerisIncidentSnapshot>(json); changed.Location.State = "NV"; changed.Report.State = (int)RmsRecordState.Accepted;
			var nextJson = JsonConvert.SerializeObject(changed);
			_store.Revisions.Add(new RmsRevision { DepartmentId = 1, RecordId = "report", RecordKind = (int)RmsRecordKind.IncidentReport, RmsRevisionId = "r2", RevisionNumber = 2, SnapshotJson = nextJson, Checksum = RecordSnapshotSerializer.Checksum(nextJson) });
			_incidents.Setup(s => s.BuildSnapshotAsync(1, "report", "r2")).ReturnsAsync(() => JsonConvert.DeserializeObject<NerisIncidentSnapshot>(nextJson));
			var original = await _service.GetAsync(1, "officer", "report", RmsRecordKind.IncidentReport, "r1");
			var next = await _service.GetAsync(1, "officer", "report", RmsRecordKind.IncidentReport, "r2");
			var html = await _service.RenderHtmlAsync(1, "officer", original);
			html.Should().Contain(">CA<").And.Contain(">Dispatch<").And.NotContain("storage-form-identity");
			var differences = await _service.DiffAsync(1, "officer", original, next);
			differences.Should().Contain(d => d.FieldKey == "Location.State" && d.OldValue == "CA" && d.NewValue == "NV");
			differences.Should().NotContain(d => d.FieldKey == "Report.State");
		}

		[TestCase("record")]
		[TestCase("export")]
		[TestCase("restricted")]
		public async Task Permission_revoked_during_PDF_generation_prevents_returning_the_rendered_bytes(string permission)
		{
			CaptureCustomFields(); var document = await _service.GetAsync(1, "officer", "report", RmsRecordKind.IncidentReport); var original = _store.Revisions[0].SnapshotJson;
			_pdf.Setup(p => p.ConvertHtmlToPdf(It.IsAny<string>(), "Letter")).Callback(() =>
			{
				if (permission == "record") _auth.Setup(a => a.CanUserViewRecordAsync("officer", "report", 1)).ReturnsAsync(false);
				else _auth.Setup(a => a.HasPermissionAsync("officer", 1, permission == "export" ? PermissionTypes.ExportRecords : PermissionTypes.ViewRestrictedRecords)).ReturnsAsync(false);
			}).Returns(new byte[] { 1, 2, 3 });
			Func<Task> print = () => _service.RenderPdfAsync(1, "officer", document);
			await print.Should().ThrowAsync<UnauthorizedAccessException>(); _store.Revisions[0].SnapshotJson.Should().Be(original);
			_pdf.Verify(p => p.ConvertHtmlToPdf(It.IsAny<string>(), "Letter"), Times.Once);
		}

		[Test]
		public async Task Comparison_PDF_includes_captured_custom_field_labels_values_and_provenance_with_safe_HTML()
		{
			CaptureCustomFields();
			var changed = JsonConvert.DeserializeObject<NerisIncidentSnapshot>(_store.Revisions[0].SnapshotJson); changed.CustomFields.Fields[0].Value = "<script>unsafe</script>";
			var json = JsonConvert.SerializeObject(changed);
			_store.Revisions.Add(new RmsRevision { DepartmentId = 1, RecordId = "report", RecordKind = (int)RmsRecordKind.IncidentReport, RmsRevisionId = "r2", RevisionNumber = 2, SnapshotJson = json, Checksum = RecordSnapshotSerializer.Checksum(json) });
			_incidents.Setup(s => s.BuildSnapshotAsync(1, "report", "r2")).ReturnsAsync(() => JsonConvert.DeserializeObject<NerisIncidentSnapshot>(json));
			_auth.Setup(a => a.IsDepartmentAdminAsync("officer", 1)).ReturnsAsync(false);
			_auth.Setup(a => a.HasPermissionAsync("officer", 1, PermissionTypes.ViewRestrictedRecords)).ReturnsAsync(false);
			string html = null;
			_pdf.Setup(p => p.ConvertHtmlToPdf(It.IsAny<string>(), "Letter")).Callback<string, string>((text, size) => html = text).Returns(new byte[] { 1, 2, 3 });
			(await _service.RenderDiffPdfAsync(1, "officer", "report", RmsRecordKind.IncidentReport, "r1", "r2")).Should().Equal(1, 2, 3);
			html.Should().Contain("Example Fire Department").And.Contain("revision 1 to 2").And.Contain("Department score").And.Contain("23").And.Contain("&lt;script&gt;unsafe&lt;/script&gt;").And.NotContain("<script>").And.NotContain("restricted-custom-value").And.NotContain("Confidential admin label");
			html.Should().Contain(_store.Revisions[0].Checksum).And.Contain(_store.Revisions[1].Checksum);
			var qa = Environment.GetEnvironmentVariable("RESGRID_RMS_DOCUMENT_QA_DIR");
			if (!string.IsNullOrWhiteSpace(qa)) { Directory.CreateDirectory(qa); File.WriteAllText(Path.Combine(qa, "revision-diff.html"), html); }
		}

		[Test]
		public async Task Whole_custom_fields_are_projected_in_department_JSON_CSV_and_print_without_changing_the_revision()
		{
			CaptureCustomFields(); var original = _store.Revisions[0].SnapshotJson;
			_auth.Setup(a => a.IsDepartmentAdminAsync("officer", 1)).ReturnsAsync(false);
			_auth.Setup(a => a.HasPermissionAsync("officer", 1, PermissionTypes.ViewRestrictedRecords)).ReturnsAsync(false);
			var document = await _service.GetAsync(1, "officer", "report", RmsRecordKind.IncidentReport);
			foreach (var output in new[] { document.ContentJson, RecordDocumentsController.Csv(document), await _service.RenderHtmlAsync(1, "officer", document) })
				output.Should().Contain("Department score").And.Contain("23").And.NotContain("Confidential admin label").And.NotContain("admin-only-value").And.NotContain("Restricted custom label").And.NotContain("restricted-custom-value");
			_store.Revisions[0].SnapshotJson.Should().Be(original);
		}

		[Test]
		public async Task Diff_drops_fixed_and_custom_restricted_values_when_the_grant_changes_during_custom_projection()
		{
			CaptureCustomFields(); var old = await _service.GetAsync(1, "officer", "report", RmsRecordKind.IncidentReport);
			var changed = JsonConvert.DeserializeObject<NerisIncidentSnapshot>(_store.Revisions[0].SnapshotJson);
			changed.Casualties[0].PersonnelUserId = "changed-restricted-person"; changed.CustomFields.Fields[2].Value = "changed-restricted-custom";
			var json = JsonConvert.SerializeObject(changed);
			_store.Revisions.Add(new RmsRevision { DepartmentId = 1, RecordId = "report", RecordKind = 2, RmsRevisionId = "r2", RevisionNumber = 2, SnapshotJson = json, Checksum = RecordSnapshotSerializer.Checksum(json) });
			_incidents.Setup(s => s.BuildSnapshotAsync(1, "report", "r2")).ReturnsAsync(() => JsonConvert.DeserializeObject<NerisIncidentSnapshot>(json));
			var next = await _service.GetAsync(1, "officer", "report", RmsRecordKind.IncidentReport, "r2");
			var calls = 0;
			_groups.Setup(g => g.GetGroupForUserAsync("officer", 1)).ReturnsAsync(() =>
			{
				if (++calls == 3) _auth.Setup(a => a.HasPermissionAsync("officer", 1, PermissionTypes.ViewRestrictedRecords)).ReturnsAsync(false);
				return (DepartmentGroup)null;
			});
			(await _service.DiffAsync(1, "officer", old, next)).Should().BeEmpty();
			calls.Should().BeGreaterThanOrEqualTo(3);
		}

		[Test]
		public async Task Complete_output_is_pinned_and_restricted_JSON_cannot_leak_through_print_export_or_diff()
		{
			_store.Reports[0].IncidentNumber = "unapproved amendment";
			_auth.Setup(a => a.HasPermissionAsync("officer", 1, PermissionTypes.ViewRestrictedRecords)).ReturnsAsync(false);
			var doc = await _service.GetAsync(1, "officer", "report", RmsRecordKind.IncidentReport);
			doc.ContentJson.Should().Contain("2026-00023").And.Contain("scene.jpg").And.Contain("Engine 2 selected").And.NotContain("unapproved amendment").And.NotContain("restricted-person").And.NotContain("restricted-body").And.NotContain("restricted-evidence").And.NotContain("Restricted source title");
			doc.WithheldFields.Should().NotBeEmpty();
			var html = await _service.RenderHtmlAsync(1, "officer", doc);
			html.Should().Contain("Example Fire Department").And.Contain("department-default/3").And.Contain("Officer Jones").And.Contain("scene.jpg").And.Contain("&lt;script&gt;").And.NotContain("<script>");
			(await _service.DiffAsync(1, "officer", doc, doc)).Should().BeEmpty();
			// Optional QA artifact output uses the same HTML and production provider as the officer route.
			var output = Environment.GetEnvironmentVariable("RESGRID_RMS_DOCUMENT_QA_DIR");
			if (!string.IsNullOrWhiteSpace(output))
			{
				Directory.CreateDirectory(output); await File.WriteAllTextAsync(Path.Combine(output, "incident-revision.html"), html);
				if (Environment.GetEnvironmentVariable("RESGRID_RMS_NRECO_PDF_TESTS") == "1")
				{
					var bytes = new NRecoProvider().ConvertHtmlToPdf(html, "Letter"); EncodingPrefix(bytes).Should().Be("%PDF");
					await File.WriteAllBytesAsync(Path.Combine(output, "incident-revision.pdf"), bytes);
				}
			}
		}
		private static string EncodingPrefix(byte[] bytes) => System.Text.Encoding.ASCII.GetString(bytes.Take(4).ToArray());

		[Test]
		public async Task Render_rechecks_access_and_rejects_a_previously_built_full_document_after_revocation()
		{
			var doc = await _service.GetAsync(1, "officer", "report", RmsRecordKind.IncidentReport);
			_auth.Setup(a => a.HasPermissionAsync("officer", 1, PermissionTypes.ViewRestrictedRecords)).ReturnsAsync(false);
			Func<Task> render = () => _service.RenderPdfAsync(1, "officer", doc);
			await render.Should().ThrowAsync<UnauthorizedAccessException>(); _pdf.Verify(p => p.ConvertHtmlToPdf(It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task Diff_projects_both_revisions_under_the_final_scope_when_restricted_access_changes_between_reads()
		{
			var doc = await _service.GetAsync(1, "officer", "report", RmsRecordKind.IncidentReport);
			_auth.SetupSequence(a => a.HasPermissionAsync("officer", 1, PermissionTypes.ViewRestrictedRecords)).ReturnsAsync(true).ReturnsAsync(false).ReturnsAsync(false);
			(await _service.DiffAsync(1, "officer", doc, doc)).Should().BeEmpty();
		}

		[Test]
		public async Task Purge_foreign_revision_tampered_snapshot_and_export_denial_fail_closed()
		{
			(await _service.GetAsync(1, "officer", "report", RmsRecordKind.IncidentReport, "foreign")).Should().BeNull();
			_store.Revisions[0].SnapshotJson = "{\"changed\":true}";
			Func<Task> tampered = () => _service.GetAsync(1, "officer", "report", RmsRecordKind.IncidentReport); await tampered.Should().ThrowAsync<InvalidOperationException>();
			_store.Reports[0].PurgedOn = DateTime.UtcNow;
			(await _service.GetAsync(1, "officer", "report", RmsRecordKind.IncidentReport)).Should().BeNull();
			_auth.Setup(a => a.HasPermissionAsync("officer", 1, PermissionTypes.ExportRecords)).ReturnsAsync(false);
			Func<Task> denied = () => _service.GetAsync(1, "officer", "report", RmsRecordKind.IncidentReport, exporting: true); await denied.Should().ThrowAsync<UnauthorizedAccessException>();
		}

		[Test]
		public void Csv_escapes_formula_newline_and_quote_input()
		{
			var csv = RecordDocumentsController.Csv(new RecordDocument { RecordNumber = "INC-1", RevisionNumber = 2, ContentJson = "{\"Note\":\"=1+1\",\"Quote\":\"a\\\"b\\nc\"}" });
			csv.Should().Contain("\"'=1+1\"").And.Contain("\"a\"\"b\nc\"");
		}

		[Test]
		public async Task Rendering_uses_the_reloaded_revision_even_if_the_caller_mutates_document_content_or_provenance()
		{
			var doc = await _service.GetAsync(1, "officer", "report", RmsRecordKind.IncidentReport);
			doc.ContentJson = "{\"Narrative\":\"forged body\"}"; doc.AttestedBy = "forged signer";
			var html = await _service.RenderHtmlAsync(1, "officer", doc);
			html.Should().NotContain("forged body").And.NotContain("forged signer").And.Contain("Officer Jones");
		}
	}
}
