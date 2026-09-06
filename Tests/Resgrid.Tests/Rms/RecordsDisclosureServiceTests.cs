using System;
using System.Collections.Generic;
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
using Resgrid.Model.Services;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// Public-records workflow (RMS plan section 4.7, RMS-3d). The three properties the plan actually cares about
	/// are the ones under test: a production never mutates a source revision, the produced set is frozen so a
	/// later amendment cannot change what was released, and redaction is logged rather than silent.
	/// </summary>
	[TestFixture]
	public class RecordsDisclosureServiceTests
	{
		private const int Dept = 21;

		private FakeRmsStore _store;
		private FakeIncidentStore _incidentStore;
		private Mock<Resgrid.Model.Providers.IPdfProvider> _pdf;
        private Mock<IRecordAttachmentScanner> _scanner;
		private Mock<IRecordsAuthorizationService> _authorization;
		private Mock<IDepartmentSettingsService> _settings;
		private RecordsDisclosureService _service;
		private RmsOperationalRecord _finalized;
		private RmsRevision _revision;
		private bool _udfAdmin;

		[SetUp]
		public void SetUp()
		{
			_incidentStore = new FakeIncidentStore(); _store = _incidentStore.Shared;

			_authorization = new Mock<IRecordsAuthorizationService>();
			_authorization.Setup(a => a.HasPermissionAsync(It.IsAny<string>(), Dept, It.IsAny<PermissionTypes>())).ReturnsAsync(true);
			_authorization.Setup(a => a.GetVisibleGroupIdsAsync(It.IsAny<string>(), Dept)).ReturnsAsync((List<int>)null);
			_authorization.Setup(a => a.CanUserViewRecordAsync(It.IsAny<string>(), It.IsAny<string>(), Dept)).ReturnsAsync(true);
			_udfAdmin = true;
			_authorization.Setup(a => a.IsActiveMemberAsync(It.IsAny<string>(), Dept)).ReturnsAsync(true);
			_authorization.Setup(a => a.IsDepartmentAdminAsync(It.IsAny<string>(), Dept)).ReturnsAsync(() => _udfAdmin);

			_settings = new Mock<IDepartmentSettingsService>();
			_settings.Setup(s => s.GetRecordsDisclosureConfigAsync(Dept, It.IsAny<bool>()))
				.ReturnsAsync(new RecordsDisclosureConfig { StatutoryClockDays = 5, DefaultRedactionProfile = RmsRedactionProfiles.Standard });

			_finalized = SeedRecord(RmsRecordState.Finalized);
			_revision = SeedRevision(_finalized);

			_scanner = new Mock<IRecordAttachmentScanner>();
            _scanner.Setup(s => s.ScanAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>())).ReturnsAsync(new RecordAttachmentScanResult { State = RmsAttachmentScanState.Clean });
            _pdf = new Mock<Resgrid.Model.Providers.IPdfProvider>();
			_pdf.Setup(p => p.ConvertHtmlToPdf(It.IsAny<string>(), "Letter")).Returns(System.Text.Encoding.ASCII.GetBytes("%PDF-fixture"));
			var incidents = new Mock<IIncidentReportsService>();
			incidents.Setup(s => s.BuildSnapshotAsync(Dept, It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync((int d, string id, string revision) => JsonConvert.DeserializeObject<NerisIncidentSnapshot>(_store.Revisions.Single(r => r.RmsRevisionId == revision).SnapshotJson));
			var udf = new RecordsUdfService(Mock.Of<IRmsUdfDefinitionsRepository>(), Mock.Of<IUdfFieldRepository>(), Mock.Of<IUdfFieldValueRepository>(), _authorization.Object, Mock.Of<IDepartmentGroupsService>(), _store.UnitOfWork.Object, Mock.Of<IDepartmentDataProtectionService>());
			var documents = new RecordsDocumentService(_authorization.Object, _store.RecordsRepo.Object, _incidentStore.ReportsRepo.Object, _incidentStore.AnalysesRepo.Object, _store.RevisionsRepo.Object,
				incidents.Object, Mock.Of<IDepartmentProfileMediaService>(), Mock.Of<IRecordsPrintLayoutService>(), _pdf.Object, Mock.Of<IRecordsEvidenceService>(), udf);
			_service = new RecordsDisclosureService(_store.DisclosureRequestsRepo.Object, _store.DisclosureProductionsRepo.Object,
				_store.RecordsRepo.Object, _store.RevisionsRepo.Object, _store.AuditsRepo.Object,
				_authorization.Object, _settings.Object, _store.UnitOfWork.Object, _incidentStore.ReportsRepo.Object, documents, _store.AttachmentsRepo.Object, _pdf.Object, _incidentStore.AnalysesRepo.Object, _scanner.Object, udf);
		}

		private async Task<RmsDisclosureProduction> ReviewedProduceAsync(int departmentId, string userId, string requestId)
		{
			var review = await _service.GetReviewAsync(departmentId, userId, requestId);
			review.Reviewed = true; review.Authority = "Test jurisdiction rule 4"; review.Basis = "Fixture custodian review"; review.UnresolvedScopeHandling = "Separate review tracked by fixture";
			return await _service.ProduceAsync(departmentId, userId, requestId, review: review);
		}

		private static void Approve(RmsDisclosureReview review)
		{ review.Reviewed = true; review.Authority = "Fixture rule 4"; review.Basis = "Reviewed for this test request"; }
		private void AddAdminCustomField()
		{
			var snapshot = JsonConvert.DeserializeObject<RecordSnapshot>(_revision.SnapshotJson);
			snapshot.CustomFields = new RecordUdfSection { DefinitionId = "captured-form", ExtensionVersion = 1, Fields = new()
			{ new() { Field = new() { UdfFieldId = "admin-only", Label = "Admin review label", RmsClassification = 0, Visibility = 2, IsEnabled = true }, Value = "Admin review value" } } };
			_revision.SnapshotJson = RecordSnapshotSerializer.Serialize(snapshot); _revision.Checksum = RecordSnapshotSerializer.Checksum(_revision.SnapshotJson);
		}
		[Test]
		public async Task Production_list_drops_earlier_packets_when_record_access_is_revoked_during_later_packet_loading()
		{
			var request = await OpenRequestAsync();
			var first = await ReviewedProduceAsync(Dept, "clerk", request.RmsDisclosureRequestId);
			var second = await ReviewedProduceAsync(Dept, "clerk", request.RmsDisclosureRequestId);
			_store.DisclosureProductionsRepo.Setup(p => p.GetForRequestAsync(Dept, request.RmsDisclosureRequestId)).ReturnsAsync(new[] { first, second });
			var allowed = true;
			_authorization.Setup(a => a.CanUserViewRecordAsync("clerk", It.IsAny<string>(), Dept)).ReturnsAsync(() => allowed);
			_store.DisclosureProductionsRepo.Setup(p => p.GetByIdForDepartmentAsync(Dept, second.RmsDisclosureProductionId))
				.Callback(() => allowed = false).ReturnsAsync(second);
			(await _service.GetProductionsAsync(Dept, "clerk", request.RmsDisclosureRequestId)).Should().BeEmpty();
			first.ArtifactJson.Should().NotBeNullOrEmpty("revocation must not destroy a previously produced immutable packet");
		}

		[Test]
		public async Task Production_read_rechecks_custodian_permission_after_record_authorization()
		{
			var request = await OpenRequestAsync(); var production = await ReviewedProduceAsync(Dept, "clerk", request.RmsDisclosureRequestId);
			var allowed = true;
			_authorization.Setup(a => a.HasPermissionAsync("clerk", Dept, PermissionTypes.ManageRecordDisclosures)).ReturnsAsync(() => allowed);
			_authorization.Setup(a => a.CanUserViewRecordAsync("clerk", It.IsAny<string>(), Dept)).Callback(() => allowed = false).ReturnsAsync(true);
			(await _service.GetAuthorizedProductionAsync(Dept, "clerk", production.RmsDisclosureProductionId)).Should().BeNull();
		}

		[Test]
		public async Task Standard_packet_keeps_custom_field_role_requirement_after_custodian_role_revocation()
		{
			AddAdminCustomField(); var request = await OpenRequestAsync(); var original = _revision.SnapshotJson;
			var production = await ReviewedProduceAsync(Dept, "clerk", request.RmsDisclosureRequestId);
			production.ArtifactJson.Should().Contain("Admin review value");
			JObject.Parse(production.ArtifactJson)["udf_visibility_required"].Value<int>().Should().Be(2);
			(await _service.GetAuthorizedProductionAsync(Dept, "clerk", production.RmsDisclosureProductionId)).Should().NotBeNull();
			_udfAdmin = false;
			(await _service.GetAuthorizedProductionAsync(Dept, "clerk", production.RmsDisclosureProductionId)).Should().BeNull();
			foreach (var format in new[] { "pdf", "json", "zip" })
			{
				Func<Task> download = () => _service.DownloadAsync(Dept, "clerk", production.RmsDisclosureProductionId, format);
				await download.Should().ThrowAsync<UnauthorizedAccessException>();
			}
			Func<Task> release = () => _service.ReleaseAsync(Dept, "clerk", production.RmsDisclosureProductionId, deliveryMethod: "Counter", deliveryReference: "Denied test");
			await release.Should().ThrowAsync<UnauthorizedAccessException>(); _revision.SnapshotJson.Should().Be(original);
		}
		[Test]
		public async Task Review_fails_closed_when_admin_role_is_revoked_after_the_document_was_projected()
		{
			AddAdminCustomField(); var request = await OpenRequestAsync(); var calls = 0;
			_authorization.Setup(a => a.IsDepartmentAdminAsync("clerk", Dept)).ReturnsAsync(() => ++calls <= 2);
			Func<Task> review = () => _service.GetReviewAsync(Dept, "clerk", request.RmsDisclosureRequestId);
			await review.Should().ThrowAsync<UnauthorizedAccessException>(); _store.DisclosureProductions.Should().BeEmpty();
		}
		[Test]
		public async Task Redacted_attachment_replacement_is_scanned_traced_and_does_not_release_original_bytes_or_metadata()
		{
			SeedIncidentWithFile(); var original = _store.Attachments.Single().Data.ToArray(); var request = await OpenRequestAsync();
			await _service.SaveScopeAsync(Dept, "clerk", request.RmsDisclosureRequestId, "Incident", new RmsRecordQuery { DefinitionKey = RmsDefinitionKeys.NerisIncidentReport }, RmsRedactionProfiles.Standard);
			var review = await _service.GetReviewAsync(Dept, "clerk", request.RmsDisclosureRequestId); Approve(review); var file = review.Records.Single().Attachments.Single();
			file.Reviewed = true; file.Include = true; var bytes = System.Text.Encoding.UTF8.GetBytes("Public scene information only");
			file.Derivative = new RmsDisclosureAttachmentDerivative { FileName = "released.txt", ContentType = "text/plain", Data = bytes, Checksum = RecordSnapshotSerializer.Checksum(bytes) };
			Func<Task> produce = () => _service.ProduceAsync(Dept, "clerk", request.RmsDisclosureRequestId, review: review);
			await produce.Should().ThrowAsync<ArgumentException>(); file.Authority = "Fixture privacy provision"; file.Basis = "Personal contact information removed";
			_scanner.Setup(s => s.ScanAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>())).ReturnsAsync(new RecordAttachmentScanResult { State = RmsAttachmentScanState.Skipped });
			await produce.Should().ThrowAsync<ArgumentException>(); _store.DisclosureProductions.Should().BeEmpty();
			_scanner.Setup(s => s.ScanAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>())).ReturnsAsync(new RecordAttachmentScanResult { State = RmsAttachmentScanState.Clean });
			var packet = await _service.ProduceAsync(Dept, "clerk", request.RmsDisclosureRequestId, review: review);
			packet.ArtifactJson.Should().Contain(file.Checksum).And.Contain(file.Derivative.Checksum).And.Contain("Attachment replacement").And.NotContain("scene.txt").And.NotContain(Convert.ToBase64String(original));
			_store.Attachments.Single().Data.Should().Equal(original);
			using var zip = new System.IO.Compression.ZipArchive(new System.IO.MemoryStream((await _service.DownloadAsync(Dept, "clerk", packet.RmsDisclosureProductionId, "zip")).Data));
			using var reader = new System.IO.StreamReader(zip.GetEntry("attachments/0001-released.txt").Open()); (await reader.ReadToEndAsync()).Should().Be("Public scene information only");
		}
		[Test]
		public async Task A_stale_release_cannot_replace_another_officers_partial_delivery()
		{
			SeedRecord(RmsRecordState.Draft); var request = await OpenRequestAsync();
			await _service.SaveScopeAsync(Dept, "clerk", request.RmsDisclosureRequestId, "All", new RmsRecordQuery(), RmsRedactionProfiles.Standard);
			var production = await ReviewedProduceAsync(Dept, "clerk", request.RmsDisclosureRequestId);
			var stale = JsonConvert.DeserializeObject<RmsDisclosureProduction>(JsonConvert.SerializeObject(production));
			await _service.ReleaseAsync(Dept, "first", production.RmsDisclosureProductionId, deliveryMethod: "Collection", deliveryReference: "First receipt");
			var released = _store.DisclosureProductions.Single(); var version = released.RowVersion;
			// Second officer already read the unreleased production, then reads the newly advanced, still-open request.
			_store.DisclosureProductionsRepo.SetupSequence(r => r.GetByIdForDepartmentAsync(Dept, production.RmsDisclosureProductionId)).ReturnsAsync(stale).ReturnsAsync(released);
			Func<Task> second = () => _service.ReleaseAsync(Dept, "second", production.RmsDisclosureProductionId, deliveryMethod: "Collection", deliveryReference: "Second receipt");
			await second.Should().ThrowAsync<InvalidOperationException>();
			_store.DisclosureProductions.Single().ReleasedByUserId.Should().Be("first"); _store.DisclosureProductions.Single().RowVersion.Should().Be(version);
			_store.Audits.Count(a => a.Purpose == "Disclosure released").Should().Be(1);
		}
		[Test]
		public async Task Request_reads_use_live_permissions_and_never_mutate_stored_requester_identity()
		{
			var request = await OpenRequestAsync();
			_authorization.Setup(a => a.HasPermissionAsync("clerk", Dept, PermissionTypes.ViewRestrictedRecords)).ReturnsAsync(false);
			(await _service.GetAsync(Dept, "clerk", request.RmsDisclosureRequestId)).RequesterName.Should().BeNull();
			(await _service.QueryAsync(Dept, "clerk", null)).Single().RequesterName.Should().BeNull();
			_store.DisclosureRequests.Single().RequesterName.Should().NotBeNull();
			_authorization.Setup(a => a.HasPermissionAsync("clerk", Dept, PermissionTypes.ManageRecordDisclosures)).ReturnsAsync(false);
			Func<Task> get = () => _service.GetAsync(Dept, "clerk", request.RmsDisclosureRequestId); await get.Should().ThrowAsync<UnauthorizedAccessException>();
			Func<Task> query = () => _service.QueryAsync(Dept, "clerk", null); await query.Should().ThrowAsync<UnauthorizedAccessException>();
		}
		[TestCase(PermissionTypes.ViewRestrictedRecords)]
		[TestCase(PermissionTypes.ManageRecordDisclosures)]
		public async Task Review_does_not_return_earlier_content_after_revocation_during_a_later_record(PermissionTypes permission)
		{
			SeedIncidentWithFile(); var request = await OpenRequestAsync(RmsRedactionProfiles.FullDisclosure);
			await _service.SaveScopeAsync(Dept, "clerk", request.RmsDisclosureRequestId, "All", new RmsRecordQuery(), RmsRedactionProfiles.FullDisclosure);
			// Scope checks use QueryAsync. This lookup occurs only when the later incident document is loaded.
			_incidentStore.ReportsRepo.Setup(r => r.GetByIdForDepartmentAsync(Dept, "incident")).ReturnsAsync(() =>
			{ _authorization.Setup(a => a.HasPermissionAsync("clerk", Dept, permission)).ReturnsAsync(false); return _incidentStore.Reports.Single(); });
			Func<Task> review = () => _service.GetReviewAsync(Dept, "clerk", request.RmsDisclosureRequestId); await review.Should().ThrowAsync<UnauthorizedAccessException>();
		}
		[Test]
		public async Task Incident_scope_includes_its_analysis_and_binds_all_emitted_attachment_metadata()
		{
			var report = SeedIncidentWithFile();
			var source = _store.Revisions.Single(r => r.RecordId == "incident"); var snapshot = JObject.Parse(source.SnapshotJson);
			snapshot["Attachments"][0]["Description"] = "Sensitive scene description"; snapshot["Attachments"][0]["UploadedByUserId"] = "photographer";
			source.SnapshotJson = snapshot.ToString(Formatting.None); source.Checksum = RecordSnapshotSerializer.Checksum(source.SnapshotJson);
			var analysis = new RmsIncidentAnalysis { DepartmentId = Dept, RmsIncidentAnalysisId = "analysis", IncidentReportId = report.RmsIncidentReportId, CurrentRevisionId = "analysis-r1" };
			_incidentStore.Analyses.Add(analysis); var json = JsonConvert.SerializeObject(new { Analysis = analysis, Report = report, Fire = new { GeneralCause = "Cooking" } });
			_store.Revisions.Add(new RmsRevision { DepartmentId = Dept, RecordId = "analysis", RecordKind = (int)RmsRecordKind.IncidentAnalysis, RmsRevisionId = "analysis-r1", RevisionNumber = 1, SnapshotJson = json, Checksum = RecordSnapshotSerializer.Checksum(json) });
			var request = await OpenRequestAsync(); await _service.SaveScopeAsync(Dept, "clerk", request.RmsDisclosureRequestId, "Incident and analysis", new RmsRecordQuery { DefinitionKey = RmsDefinitionKeys.NerisIncidentReport }, RmsRedactionProfiles.Standard);
			var review = await _service.GetReviewAsync(Dept, "clerk", request.RmsDisclosureRequestId); Approve(review);
			review.Records.Should().HaveCount(2); review.Records.Should().Contain(r => r.RecordKind == RmsRecordKind.IncidentAnalysis && r.Fields.Any(f => f.Value == "Cooking"));
			var file = review.Records.Single(r => r.RecordKind == RmsRecordKind.IncidentReport).Attachments.Single(); file.Metadata.Should().Contain(f => f.Value == "Sensitive scene description").And.Contain(f => f.Value == "photographer"); file.Include = true; file.Reviewed = true;
			_store.Attachments.Single().FileName = "changed-live-name.txt";
			var production = await _service.ProduceAsync(Dept, "clerk", request.RmsDisclosureRequestId, review: review);
			production.RecordCount.Should().Be(2); production.ArtifactJson.Should().Contain("Cooking").And.NotContain("changed-live-name");
			(await _service.GetAuthorizedProductionAsync(Dept, "clerk", production.RmsDisclosureProductionId)).Should().NotBeNull();
			_authorization.Setup(a => a.CanUserViewRecordAsync("clerk", "incident", Dept)).ReturnsAsync(false);
			(await _service.GetAuthorizedProductionAsync(Dept, "clerk", production.RmsDisclosureProductionId)).Should().BeNull();
		}
		[Test]
		public async Task A_partial_scope_release_records_delivery_but_keeps_the_request_clock_open()
		{
			SeedRecord(RmsRecordState.Draft, "Unfinished responsive report"); var request = await OpenRequestAsync();
			await _service.SaveScopeAsync(Dept, "clerk", request.RmsDisclosureRequestId, "All responsive reports", new RmsRecordQuery(), RmsRedactionProfiles.Standard);
			var review = await _service.GetReviewAsync(Dept, "clerk", request.RmsDisclosureRequestId); Approve(review); review.UnresolvedScopeHandling = "Custodian reviewing unfinished report separately";
			var production = await _service.ProduceAsync(Dept, "clerk", request.RmsDisclosureRequestId, review: review);
			Func<Task> missingReceipt = () => _service.ReleaseAsync(Dept, "clerk", production.RmsDisclosureProductionId); await missingReceipt.Should().ThrowAsync<ArgumentException>();
			await _service.ReleaseAsync(Dept, "clerk", production.RmsDisclosureProductionId, deliveryMethod: "Counter collection", deliveryReference: "Fixture receipt 7");
			var open = await _service.GetAsync(Dept, "clerk", request.RmsDisclosureRequestId); open.ClosedOn.Should().BeNull(); open.State.Should().Be((int)RmsDisclosureState.InReview);
			_store.Audits.Should().Contain(a => a.Purpose == "Disclosure released" && a.DetailJson.Contains("Fixture receipt 7"));
		}
		[Test]
		public async Task Concurrent_request_change_prevents_production_from_overwriting_scope_or_status()
		{
			var request = await OpenRequestAsync(); var review = await _service.GetReviewAsync(Dept, "clerk", request.RmsDisclosureRequestId); Approve(review);
			_store.DisclosureRequestsRepo.Setup(r => r.TryBumpRowVersionAsync(Dept, request.RmsDisclosureRequestId, It.IsAny<long>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
			Func<Task> produce = () => _service.ProduceAsync(Dept, "clerk", request.RmsDisclosureRequestId, review: review); await produce.Should().ThrowAsync<InvalidOperationException>();
			_store.DisclosureProductions.Should().BeEmpty();
		}
		private RmsIncidentReport SeedIncidentWithFile()
		{
			var report = new RmsIncidentReport { DepartmentId = Dept, RmsIncidentReportId = "incident", DefinitionKey = RmsDefinitionKeys.NerisIncidentReport, State = (int)RmsRecordState.Accepted, CallId = 77, RecordNumber = "INC-2026-77", CurrentRevisionId = "incident-r1" };
			var file = new RmsRecordAttachment { DepartmentId = Dept, RecordId = "incident", RmsRecordAttachmentId = "scene", Classification = 0, FileName = "scene.txt", Data = System.Text.Encoding.UTF8.GetBytes("Reviewed scene attachment"), ScanState = (int)RmsAttachmentScanState.Clean };
			file.Checksum = RecordSnapshotSerializer.Checksum(file.Data); file.ByteSize = file.Data.Length; _store.Attachments.Add(file);
			var snapshot = new NerisIncidentSnapshot { Report = report, Narrative = new RmsNarrative { Narrative = "Crew completed incident operations" }, Attachments = new List<RmsRecordAttachment> { new RmsRecordAttachment { RmsRecordAttachmentId = "scene", Classification = 0, FileName = file.FileName, Checksum = file.Checksum } },
				Evidence = new List<RmsEvidenceArtifact> { new RmsEvidenceArtifact { Classification = 0, ManifestJson = "{\"decision\":\"Engine 2 selected\",\"caller\":\"private caller\"}", Checksum = "fixture" } } };
			var json = JsonConvert.SerializeObject(snapshot); _incidentStore.Reports.Add(report);
			_store.Revisions.Add(new RmsRevision { DepartmentId = Dept, RecordId = "incident", RecordKind = (int)RmsRecordKind.IncidentReport, RmsRevisionId = "incident-r1", RevisionNumber = 1, SnapshotJson = json, Checksum = RecordSnapshotSerializer.Checksum(json) }); return report;
		}

		[Test]
		public async Task Production_requires_an_explicit_review_and_rejects_a_stale_review()
		{
			var request = await OpenRequestAsync();
			Func<Task> missing = () => _service.ProduceAsync(Dept, "clerk", request.RmsDisclosureRequestId); await missing.Should().ThrowAsync<ArgumentException>();
			var review = await _service.GetReviewAsync(Dept, "clerk", request.RmsDisclosureRequestId); Approve(review);
			SeedRevision(_finalized);
			Func<Task> stale = () => _service.ProduceAsync(Dept, "clerk", request.RmsDisclosureRequestId, review: review); await stale.Should().ThrowAsync<InvalidOperationException>();
			_store.DisclosureProductions.Should().BeEmpty();
		}
		[Test]
		public async Task Both_record_kinds_field_redactions_evidence_files_and_frozen_packet_survive_later_changes()
		{
			SeedIncidentWithFile(); var request = await OpenRequestAsync();
			await _service.SaveScopeAsync(Dept, "clerk", request.RmsDisclosureRequestId, "Operational and incident", new RmsRecordQuery(), RmsRedactionProfiles.Standard);
			var review = await _service.GetReviewAsync(Dept, "clerk", request.RmsDisclosureRequestId); review.Records.Should().HaveCount(2); Approve(review);
			var incident = review.Records.Single(r => r.RecordKind == RmsRecordKind.IncidentReport);
			incident.Decisions.Add(new RmsDisclosureFieldDecision { Path = "/Evidence/0/ManifestJson/caller", Withhold = true, Authority = "Fixture privacy rule", Basis = "Caller identity excluded from this request" });
			incident.Attachments.Single().Reviewed = true; incident.Attachments.Single().Include = true;
			var original = _store.Revisions.Single(r => r.RmsRevisionId == "incident-r1").SnapshotJson;
			string rendered = null;
			_pdf.Setup(p => p.ConvertHtmlToPdf(It.IsAny<string>(), "Letter")).Returns((string html, string paper) => { rendered = html; return System.Text.Encoding.ASCII.GetBytes("%PDF-fixture"); });
			var production = await _service.ProduceAsync(Dept, "clerk", request.RmsDisclosureRequestId, review: review);
			production.ArtifactJson.Should().NotContain("private caller").And.Contain("Engine 2 selected").And.Contain("WITHHELD");
			rendered.Should().Contain("Contents").And.Contain("Fixture privacy rule").And.NotContain("private caller");
			_store.Revisions.Single(r => r.RmsRevisionId == "incident-r1").SnapshotJson.Should().Be(original);
			_store.Attachments.Single().Data = new byte[] { 9 }; // The release keeps the reviewed bytes, not a live file link.
			var download = await _service.DownloadAsync(Dept, "clerk", production.RmsDisclosureProductionId, "zip");
			using var zip = new System.IO.Compression.ZipArchive(new System.IO.MemoryStream(download.Data));
			zip.GetEntry("packet.pdf").Should().NotBeNull();
			using var reader = new System.IO.StreamReader(zip.GetEntry("attachments/0001-scene.txt").Open()); (await reader.ReadToEndAsync()).Should().Be("Reviewed scene attachment");
			var qa = Environment.GetEnvironmentVariable("RESGRID_RMS_DOCUMENT_QA_DIR"); if (!string.IsNullOrWhiteSpace(qa)) await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(qa, "disclosure-packet.html"), rendered);
		}
		[Test]
		public async Task Every_file_needs_a_decision_and_a_withheld_file_cannot_remain_in_the_packet()
		{
			SeedIncidentWithFile(); var request = await OpenRequestAsync(); await _service.SaveScopeAsync(Dept, "clerk", request.RmsDisclosureRequestId, "Incident", new RmsRecordQuery { DefinitionKey = RmsDefinitionKeys.NerisIncidentReport }, RmsRedactionProfiles.Standard);
			var review = await _service.GetReviewAsync(Dept, "clerk", request.RmsDisclosureRequestId); Approve(review);
			Func<Task> produce = () => _service.ProduceAsync(Dept, "clerk", request.RmsDisclosureRequestId, review: review);
			await produce.Should().ThrowAsync<ArgumentException>();
			var file = review.Records.Single().Attachments.Single(); file.Reviewed = true;
			await produce.Should().ThrowAsync<ArgumentException>();
			file.Authority = "Fixture rule"; file.Basis = "Entire file withheld";
			var production = await _service.ProduceAsync(Dept, "clerk", request.RmsDisclosureRequestId, review: review);
			((JArray)JObject.Parse(production.ArtifactJson)["attachments"]).Should().BeEmpty();
			production.ArtifactJson.Should().NotContain("scene.txt").And.NotContain(Convert.ToBase64String(_store.Attachments.Single().Data));
		}
		[Test]
		public async Task Permission_revoked_while_PDF_is_rendered_prevents_storing_or_releasing_a_packet()
		{
			var request = await OpenRequestAsync(); var review = await _service.GetReviewAsync(Dept, "clerk", request.RmsDisclosureRequestId); Approve(review);
			_pdf.Setup(p => p.ConvertHtmlToPdf(It.IsAny<string>(), "Letter")).Returns(() => { _authorization.Setup(a => a.HasPermissionAsync("clerk", Dept, PermissionTypes.ManageRecordDisclosures)).ReturnsAsync(false); return System.Text.Encoding.ASCII.GetBytes("%PDF-fixture"); });
			Func<Task> produce = () => _service.ProduceAsync(Dept, "clerk", request.RmsDisclosureRequestId, review: review); await produce.Should().ThrowAsync<UnauthorizedAccessException>(); _store.DisclosureProductions.Should().BeEmpty();
		}
		[Test]
		public async Task Call_and_station_scope_filters_are_applied_to_the_saved_revision()
		{
			var request = await OpenRequestAsync(); var saved = RecordSnapshotSerializer.Deserialize(_revision.SnapshotJson); saved.CallId = 77; saved.StationGroupId = 5; saved.StartedOn = new DateTime(2024, 2, 3);
			_revision.SnapshotJson = RecordSnapshotSerializer.Serialize(saved); _revision.Checksum = RecordSnapshotSerializer.Checksum(_revision.SnapshotJson);
			_finalized.CallId = 99; _finalized.StationGroupId = 9; _finalized.StartedOn = DateTime.UtcNow;
			await _service.SaveScopeAsync(Dept, "clerk", request.RmsDisclosureRequestId, "Original call", new RmsRecordQuery { CallId = 77, StationGroupId = 5, Year = 2024 }, RmsRedactionProfiles.Standard);
			(await _service.PreviewScopeAsync(Dept, "clerk", request.RmsDisclosureRequestId)).Items.Should().ContainSingle();
			await _service.SaveScopeAsync(Dept, "clerk", request.RmsDisclosureRequestId, "Other call", new RmsRecordQuery { CallId = 99 }, RmsRedactionProfiles.Standard);
			(await _service.PreviewScopeAsync(Dept, "clerk", request.RmsDisclosureRequestId)).Items.Should().BeEmpty();
		}

		[Test]
		public async Task Full_disclosure_cannot_grant_restricted_permission_and_source_bytes_are_unchanged()
		{
			var request = await OpenRequestAsync(RmsRedactionProfiles.FullDisclosure);
			var before = _revision.SnapshotJson;
			_authorization.Setup(a => a.HasPermissionAsync("clerk", Dept, PermissionTypes.ViewRestrictedRecords)).ReturnsAsync(false);
			var production = await ReviewedProduceAsync(Dept, "clerk", request.RmsDisclosureRequestId);
			JObject.Parse(production.ArtifactJson)["documents"][0]["content"]["Details"]["CaseNumber"].Should().BeNull();
			production.WithheldFieldsJson.Should().Contain("Details.CaseNumber");
			_revision.SnapshotJson.Should().Be(before);
			(await _service.GetAuthorizedProductionAsync(Dept, "clerk", production.RmsDisclosureProductionId)).Should().NotBeNull();
		}

		[Test]
		public async Task Losing_restricted_access_prevents_download_and_release_of_an_existing_full_packet()
		{
			var request = await OpenRequestAsync(RmsRedactionProfiles.FullDisclosure);
			var production = await ReviewedProduceAsync(Dept, "clerk", request.RmsDisclosureRequestId);
			var bytes = production.ArtifactJson;
			_authorization.Setup(a => a.HasPermissionAsync("clerk", Dept, PermissionTypes.ViewRestrictedRecords)).ReturnsAsync(false);
			(await _service.GetAuthorizedProductionAsync(Dept, "clerk", production.RmsDisclosureProductionId)).Should().BeNull();
			(await _service.GetProductionsAsync(Dept, "clerk", request.RmsDisclosureRequestId)).Should().BeEmpty();
			Func<Task> release = () => _service.ReleaseAsync(Dept, "clerk", production.RmsDisclosureProductionId);
			await release.Should().ThrowAsync<UnauthorizedAccessException>();
			production.ReleasedOn.Should().BeNull();
			production.ArtifactJson.Should().Be(bytes);
		}

		[Test]
		public async Task Cross_group_or_malformed_production_manifests_are_not_retrievable()
		{
			var request = await OpenRequestAsync();
			var production = await ReviewedProduceAsync(Dept, "clerk", request.RmsDisclosureRequestId);
			_authorization.Setup(a => a.CanUserViewRecordAsync("other-group", _finalized.RmsOperationalRecordId, Dept)).ReturnsAsync(false);
			(await _service.GetAuthorizedProductionAsync(Dept, "other-group", production.RmsDisclosureProductionId)).Should().BeNull();
			production.ProducedSetJson = "[]";
			(await _service.GetAuthorizedProductionAsync(Dept, "clerk", production.RmsDisclosureProductionId)).Should().BeNull();
		}

		private RmsOperationalRecord SeedRecord(RmsRecordState state, string summary = "Structure fire response")
		{
			var record = new RmsOperationalRecord
			{
				RmsOperationalRecordId = Guid.NewGuid().ToString(),
				DepartmentId = Dept,
				ProtectionId = Guid.NewGuid().ToString(),
				DefinitionKey = RmsDefinitionKeys.Run,
				DefinitionVersion = 1,
				RecordType = (int)RmsOperationalRecordType.Run,
				State = (int)state,
				RecordNumber = "RUN-2026-0007",
				DisplaySummary = summary,
				AuthorUserId = "author",
				StartedOn = DateTime.UtcNow.AddDays(-30),
				FinalizedOn = DateTime.UtcNow.AddDays(-29),
				CreatedOn = DateTime.UtcNow.AddDays(-30),
				ModifiedOn = DateTime.UtcNow.AddDays(-29),
				RowVersion = 2
			};
			_store.Records.Add(record);
			return record;
		}

		private RmsRevision SeedRevision(RmsOperationalRecord record)
		{
			var snapshot = new RecordSnapshot
			{
				RecordId = record.RmsOperationalRecordId,
				DepartmentId = Dept,
				DefinitionKey = record.DefinitionKey,
				RecordNumber = record.RecordNumber,
				Details = new RmsOperationalRecordDetail
				{
					RecordId = record.RmsOperationalRecordId,
					Narrative = "Crew made entry and knocked the fire down.",
					ContactName = "Jane Public",
					// A restricted-class field: the standard profile must withhold it and say so.
					CaseNumber = "CASE-2026-014",
					Location = "100 Main St"
				},
				Participants = new List<RmsRecordParticipant>
				{
					new RmsRecordParticipant { UserId = "member-1", DisplayNameSnapshot = "A. Firefighter", Role = "Attended", GroupNameSnapshot = "Station 1" }
				}
			};

			var json = RecordSnapshotSerializer.Serialize(snapshot);
			var revision = new RmsRevision
			{
				RmsRevisionId = Guid.NewGuid().ToString(),
				DepartmentId = Dept,
				ProtectionId = Guid.NewGuid().ToString(),
				RecordId = record.RmsOperationalRecordId,
				RecordKind = (int)RmsRecordKind.Operational,
				RevisionNumber = 1,
				Transition = (int)RmsRevisionTransition.Finalized,
				DefinitionKey = record.DefinitionKey,
				DefinitionVersion = 1,
				SnapshotJson = json,
				Checksum = RecordSnapshotSerializer.Checksum(json),
				ActorUserId = "author",
				CreatedOn = DateTime.UtcNow.AddDays(-29)
			};
			_store.Revisions.Add(revision);
			record.CurrentRevisionId = revision.RmsRevisionId;
			return revision;
		}

		private async Task<RmsDisclosureRequest> OpenRequestAsync(string profile = RmsRedactionProfiles.Standard)
		{
			var request = await _service.CreateRequestAsync(Dept, "clerk", new RmsDisclosureRequest
			{
				RequesterName = "A. Reporter",
				RequesterOrganization = "Local Paper",
				JurisdictionProfile = "US-IL",
				ReceivedOn = DateTime.UtcNow.AddDays(-1)
			});

			return await _service.SaveScopeAsync(Dept, "clerk", request.RmsDisclosureRequestId, "All run reports from last month",
				new RmsRecordQuery { States = new List<int> { (int)RmsRecordState.Finalized }, DefinitionKey = RmsDefinitionKeys.Run }, profile);
		}

		[Test]
		public async Task A_new_request_gets_a_number_and_a_statutory_clock()
		{
			var request = await _service.CreateRequestAsync(Dept, "clerk", new RmsDisclosureRequest
			{
				RequesterName = "A. Reporter",
				ReceivedOn = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc)
			});

			request.RequestNumber.Should().Be("PRR-2026-0001");
			request.State.Should().Be((int)RmsDisclosureState.Received);
			request.StatutoryDueOn.Should().Be(new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc),
				"the clock runs from when the department received it, not from when it was logged");
			request.RedactionProfile.Should().Be(RmsRedactionProfiles.Standard);
		}

		[Test]
		public async Task A_request_without_a_requester_is_refused()
		{
			Func<Task> act = () => _service.CreateRequestAsync(Dept, "clerk", new RmsDisclosureRequest());

			await act.Should().ThrowAsync<ArgumentException>();
		}

		[Test]
		public async Task The_scope_preview_runs_through_the_same_authorization_path_as_the_queue()
		{
			var request = await OpenRequestAsync();
			_authorization.Setup(a => a.GetVisibleGroupIdsAsync("clerk", Dept)).ReturnsAsync(new List<int> { 5 });
			_authorization.Setup(a => a.CanUserViewRecordAsync("clerk", It.IsAny<string>(), Dept)).ReturnsAsync(false);

			var preview = await _service.PreviewScopeAsync(Dept, "clerk", request.RmsDisclosureRequestId);

			preview.MatchedCount.Should().Be(0, "inaccessible candidates are counted separately and their scope facts are not exposed");
			preview.WithheldWholeRecordCount.Should().Be(1, "a disclosure officer sees no more of the department than their queue shows them");
			preview.Items.Should().BeEmpty();
		}

		[Test]
		public async Task A_draft_is_listed_but_never_producible()
		{
			SeedRecord(RmsRecordState.Draft, "Half-written report");
			var request = await _service.CreateRequestAsync(Dept, "clerk", new RmsDisclosureRequest { RequesterName = "A. Reporter" });
			await _service.SaveScopeAsync(Dept, "clerk", request.RmsDisclosureRequestId, "Everything",
				new RmsRecordQuery { DefinitionKey = RmsDefinitionKeys.Run }, RmsRedactionProfiles.Standard);

			var preview = await _service.PreviewScopeAsync(Dept, "clerk", request.RmsDisclosureRequestId);

			preview.Items.Should().HaveCount(2);
			var draft = preview.Items.Single(i => i.Summary == "Half-written report");
			draft.Producible.Should().BeFalse();
			draft.NotProducibleReason.Should().Contain("saved revision");
		}

		[Test]
		public async Task A_production_redacts_restricted_fields_and_logs_what_it_withheld()
		{
			var request = await OpenRequestAsync();

			var production = await ReviewedProduceAsync(Dept, "clerk", request.RmsDisclosureRequestId);

			production.RecordCount.Should().Be(1);
			production.WithheldFieldCount.Should().BeGreaterThan(0);

			var artifact = JObject.Parse(production.ArtifactJson);
			var details = artifact["documents"][0]["content"]["Details"];
			details["Narrative"].Value<string>().Should().Contain("knocked the fire down");
			details["ContactName"].Value<string>().Should().Be("Jane Public", "an unrestricted field is released");
			details["CaseNumber"].Should().BeNull("a restricted-class field is not released under the standard profile");

			var withheld = JArray.Parse(production.WithheldFieldsJson);
			withheld.Should().Contain(w => w["Field"].Value<string>() == "Details.CaseNumber",
				"a requester is entitled to know something was withheld even when they cannot have it");
		}

		[Test]
		public async Task The_no_identifiers_profile_withholds_participant_identity()
		{
			var request = await OpenRequestAsync(RmsRedactionProfiles.NoPersonalIdentifiers);

			var production = await ReviewedProduceAsync(Dept, "clerk", request.RmsDisclosureRequestId);

			var artifact = JObject.Parse(production.ArtifactJson);
			artifact["documents"][0]["content"]["Participants"].Should().BeNull();
			JArray.Parse(production.WithheldFieldsJson).Should().Contain(w => w["Field"].Value<string>() == "Participants");
		}

		[Test]
		public async Task A_production_never_mutates_the_source_revision()
		{
			var request = await OpenRequestAsync();
			var before = _revision.SnapshotJson;
			var beforeChecksum = _revision.Checksum;
			var beforeRowVersion = _finalized.RowVersion;

			await ReviewedProduceAsync(Dept, "clerk", request.RmsDisclosureRequestId);

			_revision.SnapshotJson.Should().Be(before);
			_revision.Checksum.Should().Be(beforeChecksum);
			_finalized.RowVersion.Should().Be(beforeRowVersion, "answering a request must never damage the record it answers from");
		}

		[Test]
		public async Task The_produced_set_freezes_the_revision_and_its_checksum()
		{
			var request = await OpenRequestAsync();
			var production = await ReviewedProduceAsync(Dept, "clerk", request.RmsDisclosureRequestId);

			var produced = JArray.Parse(production.ProducedSetJson);
			produced.Should().ContainSingle();
			produced[0]["revision_id"].Value<string>().Should().Be(_revision.RmsRevisionId);
			produced[0]["revision_checksum"].Value<string>().Should().Be(_revision.Checksum);

			// The record is amended after release. What was produced must still describe revision 1.
			var amended = SeedRevision(_finalized);
			amended.RevisionNumber = 2;

			var reread = _store.DisclosureProductions.Single();
			JArray.Parse(reread.ProducedSetJson)[0]["revision_id"].Value<string>().Should().Be(_revision.RmsRevisionId,
				"a later amendment cannot silently change what the department released");
		}

		[Test]
		public async Task A_production_is_checksummed_and_verifiable()
		{
			var request = await OpenRequestAsync();
			var production = await ReviewedProduceAsync(Dept, "clerk", request.RmsDisclosureRequestId);

			(await _service.VerifyProductionAsync(Dept, production.RmsDisclosureProductionId)).Should().BeTrue();

			_store.DisclosureProductions.Single().ArtifactJson = "{\"documents\":[]}";
			(await _service.VerifyProductionAsync(Dept, production.RmsDisclosureProductionId)).Should().BeFalse();
		}

		[Test]
		public async Task Releasing_closes_the_statutory_clock_and_audits_the_handover()
		{
			var request = await OpenRequestAsync();
			var production = await ReviewedProduceAsync(Dept, "clerk", request.RmsDisclosureRequestId);

			var released = await _service.ReleaseAsync(Dept, "chief", production.RmsDisclosureProductionId, deliveryMethod: "Custodian handover", deliveryReference: "Fixture receipt 4");

			released.ReleasedOn.Should().NotBeNull();
			released.ReleasedByUserId.Should().Be("chief");
			_store.DisclosureRequests.Single().State.Should().Be((int)RmsDisclosureState.Released);
			_store.DisclosureRequests.Single().ClosedOn.Should().NotBeNull();
			_store.Audits.Should().Contain(a => a.Purpose == "Disclosure released");
		}

		[Test]
		public async Task Releasing_twice_is_refused()
		{
			var request = await OpenRequestAsync();
			var production = await ReviewedProduceAsync(Dept, "clerk", request.RmsDisclosureRequestId);
			await _service.ReleaseAsync(Dept, "chief", production.RmsDisclosureProductionId, deliveryMethod: "Custodian handover", deliveryReference: "Fixture receipt 4");

			Func<Task> act = () => _service.ReleaseAsync(Dept, "chief", production.RmsDisclosureProductionId, deliveryMethod: "Custodian handover", deliveryReference: "Fixture receipt 4");

			await act.Should().ThrowAsync<InvalidOperationException>();
		}

		[Test]
		public async Task The_scope_cannot_change_once_something_has_been_produced()
		{
			var request = await OpenRequestAsync();
			await ReviewedProduceAsync(Dept, "clerk", request.RmsDisclosureRequestId);

			Func<Task> act = () => _service.SaveScopeAsync(Dept, "clerk", request.RmsDisclosureRequestId, "Actually, everything",
				new RmsRecordQuery(), RmsRedactionProfiles.FullDisclosure);

			await act.Should().ThrowAsync<InvalidOperationException>();
		}

		[Test]
		public async Task Closing_without_a_reason_is_refused()
		{
			var request = await OpenRequestAsync();

			Func<Task> act = () => _service.CloseAsync(Dept, "chief", request.RmsDisclosureRequestId, RmsDisclosureState.Denied, "   ");

			await act.Should().ThrowAsync<ArgumentException>("a refusal without a recorded basis is not defensible");
		}

		[Test]
		public async Task Denying_records_the_exemption_relied_on()
		{
			var request = await OpenRequestAsync();

			var denied = await _service.CloseAsync(Dept, "chief", request.RmsDisclosureRequestId, RmsDisclosureState.Denied, "Active investigation exemption");

			denied.State.Should().Be((int)RmsDisclosureState.Denied);
			denied.DispositionReason.Should().Be("Active investigation exemption");
			denied.ClosedOn.Should().NotBeNull();
		}

		[Test]
		public async Task Every_produced_record_is_audited_against_the_record_itself()
		{
			var request = await OpenRequestAsync();
			await ReviewedProduceAsync(Dept, "clerk", request.RmsDisclosureRequestId);

			// "What did we hand out about this record" has to be answerable from the record, not only the request.
			_store.Audits.Should().Contain(a => a.RecordId == _finalized.RmsOperationalRecordId && a.Purpose.StartsWith("Disclosure production"));
		}

		[Test]
		public async Task A_scope_that_resolves_to_nothing_producible_is_refused()
		{
			_finalized.State = (int)RmsRecordState.Draft;
			var request = await OpenRequestAsync();

			Func<Task> act = () => ReviewedProduceAsync(Dept, "clerk", request.RmsDisclosureRequestId);

			await act.Should().ThrowAsync<InvalidOperationException>();
		}

		[Test]
		public async Task A_client_supplied_scope_cannot_widen_the_viewer()
		{
			var request = await _service.CreateRequestAsync(Dept, "clerk", new RmsDisclosureRequest { RequesterName = "A. Reporter" });

			// A caller tries to scope the request to somebody else's groups.
			await _service.SaveScopeAsync(Dept, "clerk", request.RmsDisclosureRequestId, "Everything",
				new RmsRecordQuery { VisibleGroupIds = new List<int> { 99 }, ViewerUserId = "someone-else" }, RmsRedactionProfiles.Standard);

			var stored = JsonConvert.DeserializeObject<RmsRecordQuery>(_store.DisclosureRequests.Single().ScopeQueryJson);
			stored.VisibleGroupIds.Should().BeNull("the viewer fields come from the caller's own authorization, never the request body");
			stored.ViewerUserId.Should().BeNull();
		}
	}
}
