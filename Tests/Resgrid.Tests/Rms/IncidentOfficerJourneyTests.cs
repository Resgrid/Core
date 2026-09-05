using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
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
using Resgrid.Providers.Neris;
using Resgrid.Services;
using Resgrid.Services.Records;
using Resgrid.Services.Records.Evidence;

namespace Resgrid.Tests.Rms
{
	public partial class IncidentReportsServiceTests
	{
		/// <summary>Real lifecycle/validation/mapping/evidence/UDF/attachment/submission/document/disclosure services;
		/// in-memory persistence, scanner, PDF provider and destination transport. This is not browser or sandbox acceptance.</summary>
		[Test]
		public async Task Officer_completes_submits_corrects_and_discloses_an_incident_with_frozen_custom_fields_evidence_and_attachment()
		{
			var (udf, definition) = await JourneyCustomFieldsAsync();
			_neris.Setup(n => n.GetDestinationIdentity(_profile)).Returns("journey-destination");
			var validator = new NerisValidationService(Mock.Of<INerisApiClient>(), _neris.Object);
			_validation.Setup(v => v.ValidateLocal(It.IsAny<NerisIncidentSnapshot>(), It.IsAny<RmsNerisProfile>()))
				.Returns((NerisIncidentSnapshot snapshot, RmsNerisProfile profile) => validator.ValidateLocal(snapshot, profile));
			var activations = new Mock<IRunCardActivationsRepository>();
			activations.Setup(a => a.GetActivationsByCallIdAsync(CallId)).ReturnsAsync(new[] { new RunCardActivation {
				DepartmentId = Dept, CallId = CallId, RunCardActivationId = 14, RunCardId = 2, CreatedOn = LoggedOn,
				ResultJson = "{\"decision\":\"Engine 5 selected\",\"caller\":\"Private caller identity\"}" } });
			var evidence = new RecordsEvidenceService(_store.Shared.EvidenceRepo.Object, _store.Shared.RecordsRepo.Object, _store.ReportsRepo.Object,
				_store.Shared.AuditsRepo.Object, _store.UnitOfWork.Object, new[] { new RunCardActivationEvidenceAdapter(activations.Object) },
				_authorization.Object, _calls.Object, Mock.Of<IRmsExternalReferencesRepository>());
			_service = BuildService(udf, evidence);
			var started = await _service.StartFromCallAsync(Dept, "author", CallId); var id = started.Report.RmsIncidentReportId;
			var sample = Resgrid.Tests.Providers.NerisMappingTests.Snapshot();
			var input = DraftFrom(new IncidentReportAggregate { Report = sample.Report, Location = sample.Location, Types = sample.Types,
				Units = sample.Units.Take(1).ToList(), Aids = sample.Aids, Tactics = sample.Tactics, Narrative = sample.Narrative });
			input.Modules = sample.Modules.Select(m => new IncidentModuleInput { Kind = (RmsIncidentModuleKind)m.ModuleKind, PrimaryCode = m.PrimaryCode,
				SecondaryCode = m.SecondaryCode, Quantity = m.Quantity, DetailJson = m.DetailJson }).ToList();
			input.Resources = JsonConvert.DeserializeObject<List<IncidentResourceInput>>(JsonConvert.SerializeObject(sample.Resources));
			input.Casualties = JsonConvert.DeserializeObject<List<IncidentCasualtyRescueInput>>(JsonConvert.SerializeObject(sample.Casualties));
			input.Exposures = JsonConvert.DeserializeObject<List<IncidentExposureInput>>(JsonConvert.SerializeObject(sample.Exposures));
			input.CustomFields = new RecordUdfInput { DefinitionId = definition.UdfDefinitionId, Values = new() { [definition.Fields.Single().UdfFieldId] = "23" } };
			var saved = await _service.SaveDraftAsync(Dept, "author", id, started.Report.RowVersion, input, true);
			(await _service.ValidateAsync(Dept, id, false)).Where(i => i.Severity == (int)RmsValidationSeverity.Error).Should().BeEmpty();
			var scanner = new Mock<IRecordAttachmentScanner>(); scanner.Setup(s => s.ScanAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new RecordAttachmentScanResult { State = RmsAttachmentScanState.Clean });
			var files = new IncidentAttachmentsService(_store.ReportsRepo.Object, _store.Shared.AttachmentsRepo.Object, _store.Shared.RevisionsRepo.Object,
				_store.Shared.AuditsRepo.Object, _authorization.Object, scanner.Object, _store.UnitOfWork.Object);
			var file = await files.AddAsync(Dept, "author", id, (await _service.GetAsync(Dept, id)).Report.RowVersion, "scene.txt", "text/plain",
				Encoding.UTF8.GetBytes("Officer's reviewed scene notes"), "Scene observations", classification: 0);
			var captured = await evidence.CaptureAsync(new RecordEvidenceCaptureRequest { DepartmentId = Dept, CapturedByUserId = "author", RecordId = id,
				RecordKind = RmsRecordKind.IncidentReport, Kind = RmsEvidenceKind.RunCardActivation, CallId = CallId,
				ExpectedRowVersion = (await _service.GetAsync(Dept, id)).Report.RowVersion, CaptureReason = "Dispatch decision supporting this incident" });
			var final = await _service.FinalizeAsync(Dept, "author", id, (await _service.GetAsync(Dept, id)).Report.RowVersion, "1", "127.0.0.1", null, null);
			var firstRevision = _store.Revisions.Single(); var originalJson = firstRevision.SnapshotJson; var originalChecksum = firstRevision.Checksum;
			var firstSubmission = _store.Submissions.Single(); var originalPayload = firstSubmission.PayloadJson;
			firstSubmission.PayloadJson.Should().NotContain("Department response score").And.NotContain("scene.txt").And.NotContain("Private caller identity");
			var firstSnapshot = await _service.BuildSnapshotAsync(Dept, id, firstRevision.RmsRevisionId);
			firstSnapshot.CustomFields.Fields.Single().Value.Should().Be("23"); firstSnapshot.Evidence.Should().ContainSingle().Which.Checksum.Should().Be(captured.Checksum);
			firstSnapshot.Attachments.Should().ContainSingle().Which.Checksum.Should().Be(file.Checksum);
			_store.Signatures.Single().ArtifactChecksum.Should().Be(originalChecksum);

			var delivery = new Mock<INerisSubmissionService>();
			delivery.Setup(d => d.DeliverAsync(_profile, It.IsAny<RmsSubmission>(), null, It.IsAny<CancellationToken>()))
				.ReturnsAsync(new NerisSubmissionOutcome { Kind = NerisOutcomeKind.Rejected, StatusCode = 422, ResponseJson = "{\"detail\":\"Review outcome narrative\"}" });
			var worker = new RecordsSubmissionService(_store.SubmissionsRepo.Object, _store.ReportsRepo.Object, _store.AnalysesRepo.Object, _store.Shared.ProjectionsRepo.Object,
				_store.Shared.AuditsRepo.Object, _neris.Object, delivery.Object, new DomainEventOutboxService(_store.Shared.OutboxRepo.Object, _aggregator.Object),
				Mock.Of<IOutboundQueueProvider>(), _store.UnitOfWork.Object, _store.Shared.CutoversRepo.Object, Mock.Of<IIncidentAnalysisService>(), _store.ExchangesRepo.Object, _authorization.Object);
			void Lease(RmsSubmission submission) { submission.LeaseOwner = "journey-worker"; submission.LeaseExpiresOn = DateTime.UtcNow.AddMinutes(5); submission.RowVersion++; }
			Lease(firstSubmission); (await worker.ProcessAsync(firstSubmission)).State.Should().Be((int)RmsSubmissionState.Rejected);
			var rejected = await _service.GetAsync(Dept, id); rejected.State.Should().Be(RmsRecordState.Rejected);
			var correction = DraftFrom(rejected); correction.OutcomeNarrative = "Fire out; no extension after final inspection.";
			correction.CustomFields = new RecordUdfInput { DefinitionId = definition.UdfDefinitionId, Values = new() { [definition.Fields.Single().UdfFieldId] = "24" } };
			var edited = await _service.SaveDraftAsync(Dept, "author", id, rejected.Report.RowVersion, correction, true);
			await _service.CorrectAndResubmitAsync(Dept, "author", id, edited.Report.RowVersion, "1", "127.0.0.1", "destination-rejection", "Officer verified final inspection outcome");
			var secondRevision = _store.Revisions.Single(r => r.RevisionNumber == 2);
			secondRevision.PriorRevisionId.Should().Be(firstRevision.RmsRevisionId);
			firstRevision.SnapshotJson.Should().Be(originalJson); firstRevision.Checksum.Should().Be(originalChecksum);
			_store.Submissions.Single(s => s.RmsSubmissionId == firstSubmission.RmsSubmissionId).PayloadJson.Should().Be(originalPayload);
			var secondSubmission = _store.Submissions.Single(s => s.RevisionId == secondRevision.RmsRevisionId);
			delivery.Setup(d => d.DeliverAsync(_profile, It.IsAny<RmsSubmission>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(new NerisSubmissionOutcome {
				Kind = NerisOutcomeKind.Accepted, StatusCode = 201, ExternalId = "FD24027000I2026000123", ResponseJson = "{\"incident_status\":\"APPROVED\",\"neris_id\":\"FD24027000I2026000123\"}" });
			Lease(secondSubmission); (await worker.ProcessAsync(secondSubmission)).State.Should().Be((int)RmsSubmissionState.Accepted);
			(await _service.GetAsync(Dept, id)).State.Should().Be(RmsRecordState.Accepted);

			var pdf = new Mock<IPdfProvider>(); var rendered = new List<string>();
			pdf.Setup(p => p.ConvertHtmlToPdf(It.IsAny<string>(), "Letter")).Returns((string html, string paper) => { rendered.Add(html); return Encoding.ASCII.GetBytes("%PDF-journey-fixture"); });
			var branding = new Mock<IDepartmentProfileMediaService>(); branding.Setup(b => b.GetBrandingAsync(Dept)).ReturnsAsync(new DepartmentBranding { DisplayName = "Journey Fire Department" });
			var documents = new RecordsDocumentService(_authorization.Object, _store.Shared.RecordsRepo.Object, _store.ReportsRepo.Object, _store.AnalysesRepo.Object,
				_store.Shared.RevisionsRepo.Object, _service, branding.Object, Mock.Of<IRecordsPrintLayoutService>(), pdf.Object, evidence, udf);
			var original = await documents.GetAsync(Dept, "author", id, RmsRecordKind.IncidentReport, firstRevision.RmsRevisionId, true);
			var corrected = await documents.GetAsync(Dept, "author", id, RmsRecordKind.IncidentReport, secondRevision.RmsRevisionId, true);
			JObject.Parse(original.ContentJson)["CustomFields"]["Fields"][0]["Value"].Value<string>().Should().Be("23");
			JObject.Parse(corrected.ContentJson)["CustomFields"]["Fields"][0]["Value"].Value<string>().Should().Be("24");
			await documents.RenderPdfAsync(Dept, "author", corrected);
			rendered.Last().Should().Contain("Department response score").And.Contain("Engine 5 selected").And.Contain("scene.txt").And.Contain("final inspection");
			var differences = await documents.DiffAsync(Dept, "author", original, corrected);
			differences.Should().Contain(d => d.FieldLabel == "Department custom field: Department response score" && d.OldValue == "23" && d.NewValue == "24");
			differences.Should().NotContain(d => d.FieldKey.EndsWith("CreatedOn") || d.FieldKey.EndsWith("ModifiedOn") || d.FieldKey.EndsWith("RmsAidId"));

			var disclosures = new RecordsDisclosureService(_store.Shared.DisclosureRequestsRepo.Object, _store.Shared.DisclosureProductionsRepo.Object,
				_store.Shared.RecordsRepo.Object, _store.Shared.RevisionsRepo.Object, _store.Shared.AuditsRepo.Object, _authorization.Object, _settings.Object,
				_store.UnitOfWork.Object, _store.ReportsRepo.Object, documents, _store.Shared.AttachmentsRepo.Object, pdf.Object, _store.AnalysesRepo.Object, scanner.Object, udf);
			var request = await disclosures.CreateRequestAsync(Dept, "custodian", new RmsDisclosureRequest { RequesterName = "Training requester", JurisdictionProfile = "Fixture jurisdiction", ReceivedOn = DateTime.UtcNow });
			await disclosures.SaveScopeAsync(Dept, "custodian", request.RmsDisclosureRequestId, "Incident and supporting file", new RmsRecordQuery { CallId = CallId, DefinitionKey = RmsDefinitionKeys.NerisIncidentReport }, RmsRedactionProfiles.Standard);
			var review = await disclosures.GetReviewAsync(Dept, "custodian", request.RmsDisclosureRequestId);
			review.Reviewed = true; review.Authority = "Fixture disclosure authority"; review.Basis = "Custodian reviewed report and attachment";
			var reviewed = review.Records.Should().ContainSingle().Which;
			reviewed.Decisions.Add(new RmsDisclosureFieldDecision { Path = "/Evidence/0/ManifestJson/activations/0/result/caller", Withhold = true, Authority = "Fixture privacy rule", Basis = "Caller identity withheld" });
			reviewed.Attachments.Single().Reviewed = true; reviewed.Attachments.Single().Include = true;
			var production = await disclosures.ProduceAsync(Dept, "custodian", request.RmsDisclosureRequestId, review: review);
			production.ArtifactJson.Should().NotContain("Private caller identity").And.Contain("Engine 5 selected").And.Contain("Department response score");
			var frozenPacket = production.ArtifactJson;
			await disclosures.ReleaseAsync(Dept, "custodian", production.RmsDisclosureProductionId, deliveryMethod: "Secure collection", deliveryReference: "Fixture delivery receipt 001");
			var download = await disclosures.DownloadAsync(Dept, "custodian", production.RmsDisclosureProductionId, "zip");
			using (var zip = new ZipArchive(new MemoryStream(download.Data)))
			{
				zip.GetEntry("packet.pdf").Should().NotBeNull();
				using var reader = new StreamReader(zip.GetEntry("attachments/0001-scene.txt").Open());
				(await reader.ReadToEndAsync()).Should().Be("Officer's reviewed scene notes");
			}
			production.ArtifactJson.Should().Be(frozenPacket); firstRevision.SnapshotJson.Should().Be(originalJson);
			var qa = Environment.GetEnvironmentVariable("RESGRID_RMS_DOCUMENT_QA_DIR");
			if (!string.IsNullOrWhiteSpace(qa))
			{
				await System.IO.File.WriteAllTextAsync(Path.Combine(qa, "officer-journey-report.html"), rendered.First());
				await System.IO.File.WriteAllTextAsync(Path.Combine(qa, "officer-journey-disclosure.html"), rendered.Last());
			}
		}

		private async Task<(RecordsUdfService service, UdfDefinition definition)> JourneyCustomFieldsAsync()
		{
			var definitions = new Mock<IRmsUdfDefinitionsRepository>(); var fields = new Mock<IUdfFieldRepository>(); var values = new Mock<IUdfFieldValueRepository>();
			var definitionRows = new List<UdfDefinition>(); var fieldRows = new List<UdfField>(); var valueRows = new List<UdfFieldValue>();
			_authorization.Setup(a => a.IsDepartmentAdminAsync("author", Dept)).ReturnsAsync(true);
			definitions.Setup(d => d.GetActiveAsync(Dept, RmsDefinitionKeys.NerisIncidentReport, 1)).ReturnsAsync(() => definitionRows.SingleOrDefault());
			definitions.Setup(d => d.GetScopedAsync(Dept, It.IsAny<string>(), RmsDefinitionKeys.NerisIncidentReport, 1)).ReturnsAsync((int d, string id, string key, int version) => definitionRows.SingleOrDefault(r => r.UdfDefinitionId == id));
			definitions.Setup(d => d.InsertAsync(It.IsAny<UdfDefinition>(), It.IsAny<CancellationToken>(), true)).ReturnsAsync((UdfDefinition d, CancellationToken c, bool force) => { definitionRows.Add(d); return d; });
			fields.Setup(f => f.InsertAsync(It.IsAny<UdfField>(), It.IsAny<CancellationToken>(), true)).ReturnsAsync((UdfField f, CancellationToken c, bool force) => { fieldRows.Add(f); return f; });
			fields.Setup(f => f.GetFieldsByDefinitionIdAsync(It.IsAny<string>())).ReturnsAsync((string id) => fieldRows.Where(f => f.UdfDefinitionId == id));
			values.Setup(v => v.GetFieldValuesByEntityAsync(4, It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync((int type, string id, string definition) => valueRows.Where(v => v.EntityId == id && v.UdfDefinitionId == definition));
			values.Setup(v => v.DeleteFieldValuesByEntityAndDefinitionAsync(4, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((int type, string id, string definition, CancellationToken c) => { valueRows.RemoveAll(v => v.EntityId == id && v.UdfDefinitionId == definition); return true; });
			values.Setup(v => v.InsertAsync(It.IsAny<UdfFieldValue>(), It.IsAny<CancellationToken>(), true)).ReturnsAsync((UdfFieldValue v, CancellationToken c, bool force) => { valueRows.Add(v); return v; });
			var service = new RecordsUdfService(definitions.Object, fields.Object, values.Object, _authorization.Object, _groups.Object, _store.UnitOfWork.Object, _adp.Object);
			var definition = await service.PublishAsync(Dept, "author", RmsDefinitionKeys.NerisIncidentReport, 1, null, new List<UdfField> {
				new() { Name = "response_score", Label = "Department response score", FieldDataType = (int)UdfFieldDataType.Number, IsEnabled = true, IsRequired = true, IsVisibleOnReports = true, RmsClassification = 0, Visibility = 0 } });
			return (service, definition);
		}
	}
}
