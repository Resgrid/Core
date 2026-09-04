using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Providers.Neris;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// The NERIS incident-analysis filing (RMS-3). The properties under test are the ones that make it a separate
	/// artifact rather than a section of the incident: it never blocks the incident report, it waits for the
	/// incident's destination id instead of failing, and it carries its own idempotency key.
	/// </summary>
	[TestFixture]
	public class IncidentAnalysisServiceTests
	{
		private const int Dept = 4;

		private FakeIncidentStore _store;
		private Mock<INerisProfileService> _neris;
		private RmsNerisProfile _profile;
		private bool _submissionEnabled;
		private IncidentAnalysisService _service;
		private RmsIncidentReport _report;

		[SetUp]
		public void SetUp()
		{
			_store = new FakeIncidentStore();

			_profile = new RmsNerisProfile { DepartmentId = Dept, NerisEntityId = "FD24027000", ContractVersion = "1.4.78", IsEnabled = true };
			_submissionEnabled = true;
			_neris = new Mock<INerisProfileService>();
			_neris.SetupGet(n => n.ContractVersion).Returns("1.4.78");
			_neris.Setup(n => n.GetProfileAsync(Dept)).ReturnsAsync(() => _profile);
			_neris.Setup(n => n.IsSubmissionEnabledAsync(Dept)).ReturnsAsync(() => _submissionEnabled);

			_report = new RmsIncidentReport
			{
				RmsIncidentReportId = Guid.NewGuid().ToString(),
				DepartmentId = Dept,
				ProtectionId = Guid.NewGuid().ToString(),
				CallId = 88,
				ReportingEntityId = "FD24027000",
				DefinitionKey = RmsDefinitionKeys.NerisIncidentReport,
				DefinitionVersion = 1,
				ProfileVersion = "1.4.78",
				State = (int)RmsRecordState.Finalized,
				IncidentNumber = "2026-000200",
				AuthorUserId = "author",
				CallCreatedOn = DateTime.UtcNow.AddDays(-2),
				CreatedOn = DateTime.UtcNow.AddDays(-2),
				ModifiedOn = DateTime.UtcNow.AddDays(-2),
				RowVersion = 1
			};
			_store.Reports.Add(_report);

			_service = new IncidentAnalysisService(_store.AnalysesRepo.Object, _store.ReportsRepo.Object,
				_store.ModulesRepo.Object, _store.PropertiesRepo.Object, _store.VehiclesRepo.Object,
				_store.IssuesRepo.Object, _store.SubmissionsRepo.Object, _store.Shared.RevisionsRepo.Object,
				_store.Shared.AuditsRepo.Object, _store.UnitOfWork.Object, _neris.Object,
				new NerisMappingService(), new NerisValidationService(Mock.Of<INerisApiClient>(), _neris.Object));
		}

		private static IncidentAnalysisDraftInput CompleteDraft()
		{
			return new IncidentAnalysisDraftInput
			{
				GeneralCause = "ACCIDENTAL",
				InvestigationTypes = new List<string> { "INVESTIGATED_BY_ARSON_FIRE_INVESTIGATOR" },
				CurrencyCode = "USD",
				Properties = new List<IncidentPropertyInput>
				{
					new IncidentPropertyInput
					{
						LocationUse = "RESIDENTIAL||DETATCHED_SINGLE_FAMILY_DWELLING", ConstructionType = "TYPE_VB", DamageType = "MAJOR_DAMAGE",
						FireSpread = "BUILDING", EstimatedValue = 300000m, EstimatedLoss = 120000m, ContentsValue = 50000m, ContentsLoss = 20000m
					}
				},
				Vehicles = new List<IncidentVehicleInput>
				{
					new IncidentVehicleInput { VehicleKind = "AUTOMOBILE", Make = "FORD", Model = "F-150", ModelYear = 2019, DamageType = "DAMAGED_NOT_DRIVABLE", Vin = "1FTFW1E85KFA00000", LicensePlate = "ABC1234", LicenseState = "IL", EstimatedValue = 20000m, EstimatedLoss = 20000m }
				},
				Modules = new List<IncidentModuleInput>
				{
					new IncidentModuleInput { Kind = RmsIncidentModuleKind.StructureFireOrigin, PrimaryCode = "KITCHEN", DetailJson = "{\"room_of_origin\":\"KITCHEN\"}" }
				}
			};
		}

		private async Task<IncidentAnalysisAggregate> StartAndFillAsync(bool canWriteRestricted = true)
		{
			var aggregate = await _service.StartForReportAsync(Dept, "investigator", _report.RmsIncidentReportId);
			return await _service.SaveDraftAsync(Dept, "investigator", aggregate.Analysis.RmsIncidentAnalysisId,
				aggregate.Analysis.RowVersion, CompleteDraft(), canWriteRestricted);
		}

		[Test]
		public async Task Starting_twice_returns_the_same_analysis()
		{
			var first = await _service.StartForReportAsync(Dept, "investigator", _report.RmsIncidentReportId);
			var second = await _service.StartForReportAsync(Dept, "someone_else", _report.RmsIncidentReportId);

			second.Analysis.RmsIncidentAnalysisId.Should().Be(first.Analysis.RmsIncidentAnalysisId);
			_store.Analyses.Should().ContainSingle("one analysis per incident report");
		}

		[Test]
		public async Task Totals_are_summed_from_what_the_analysis_enumerates()
		{
			var saved = await StartAndFillAsync();

			saved.Analysis.EstimatedValueTotal.Should().Be(370000m, "property value plus contents value plus vehicle value");
			saved.Analysis.EstimatedLossTotal.Should().Be(160000m);
		}

		[Test]
		public async Task A_caller_without_the_restricted_grant_neither_writes_nor_erases_vin_and_plate()
		{
			var saved = await StartAndFillAsync();
			saved.Vehicles.Single().Vin.Should().Be("1FTFW1E85KFA00000");

			var input = CompleteDraft();
			input.Vehicles[0].Vin = "SOMETHINGELSE";
			input.Vehicles[0].LicensePlate = "ZZZ9999";
			input.Vehicles[0].Model = "F-250";

			var again = await _service.SaveDraftAsync(Dept, "reviewer", saved.Analysis.RmsIncidentAnalysisId,
				saved.Analysis.RowVersion, input, canWriteRestricted: false);

			again.Vehicles.Single().Model.Should().Be("F-250", "the unrestricted half is still editable");
			again.Vehicles.Single().Vin.Should().Be("1FTFW1E85KFA00000", "the stored restricted value is carried forward, not overwritten");
			again.Vehicles.Single().LicensePlate.Should().Be("ABC1234");
		}

		[Test]
		public async Task Finalizing_before_the_incident_is_filed_succeeds_and_waits()
		{
			var saved = await StartAndFillAsync();

			var finalized = await _service.FinalizeAsync(Dept, "investigator", saved.Analysis.RmsIncidentAnalysisId, saved.Analysis.RowVersion);

			finalized.State.Should().Be(RmsIncidentAnalysisState.Finalized);
			finalized.Analysis.CurrentRevisionId.Should().NotBeNullOrWhiteSpace();
			_store.Submissions.Should().BeEmpty("the incident has no NERIS id yet, so there is nothing to file against");
		}

		[Test]
		public async Task Finalizing_after_the_incident_is_filed_queues_the_analysis_with_its_own_key()
		{
			_report.NerisIncidentId = "FD24027000I2026000200";
			var saved = await StartAndFillAsync();

			var finalized = await _service.FinalizeAsync(Dept, "investigator", saved.Analysis.RmsIncidentAnalysisId, saved.Analysis.RowVersion);

			finalized.State.Should().Be(RmsIncidentAnalysisState.Submitted);
			var submission = _store.Submissions.Should().ContainSingle().Subject;
			submission.Destination.Should().Be(RmsSubmissionDestinations.NerisIncidentAnalysis);
			submission.RecordKind.Should().Be((int)RmsRecordKind.IncidentAnalysis);
			submission.IdempotencyKey.Should().StartWith("neris-analysis:", "an incident and its analysis must never collide on a key");
			submission.PayloadChecksum.Should().NotBeNullOrWhiteSpace();
		}

		[Test]
		public async Task The_queued_payload_carries_the_incident_id_and_the_analysis_sections()
		{
			_report.NerisIncidentId = "FD24027000I2026000200";
			var saved = await StartAndFillAsync();
			await _service.FinalizeAsync(Dept, "investigator", saved.Analysis.RmsIncidentAnalysisId, saved.Analysis.RowVersion);

			var payload = JObject.Parse(_store.Submissions.Single().PayloadJson);
			payload["base"].Value<string>("incident_neris_id").Should().Be("FD24027000I2026000200");
			payload["base"].Value<string>("general_cause").Should().Be("ACCIDENTAL");
			payload["properties"].Should().HaveCount(1);
			payload["vehicles"].Should().HaveCount(1);
			payload["structure_fire_origin"].Should().NotBeNull("the module lands at the payload path its catalog descriptor names");
		}

		[Test]
		public async Task An_analysis_that_will_not_validate_never_touches_the_incident_report()
		{
			var saved = await StartAndFillAsync();
			var input = CompleteDraft();
			input.GeneralCause = "NOT_A_NERIS_CAUSE";
			var bad = await _service.SaveDraftAsync(Dept, "investigator", saved.Analysis.RmsIncidentAnalysisId, saved.Analysis.RowVersion, input);

			Func<Task> act = () => _service.FinalizeAsync(Dept, "investigator", bad.Analysis.RmsIncidentAnalysisId, bad.Analysis.RowVersion);

			await act.Should().ThrowAsync<IncidentReportValidationException>();
			_report.State.Should().Be((int)RmsRecordState.Finalized, "the incident report is untouched by its analysis failing");
			_store.Issues.Should().OnlyContain(i => i.RecordId == bad.Analysis.RmsIncidentAnalysisId);
		}

		[Test]
		public async Task Analyses_waiting_on_their_incident_are_queued_once_it_is_filed()
		{
			var saved = await StartAndFillAsync();
			await _service.FinalizeAsync(Dept, "investigator", saved.Analysis.RmsIncidentAnalysisId, saved.Analysis.RowVersion);
			_store.Submissions.Should().BeEmpty();

			// The incident's own submission landed; the analysis can now be filed against it.
			_report.NerisIncidentId = "FD24027000I2026000200";
			var queued = await _service.QueueAwaitingIncidentAsync(Dept);

			queued.Should().Be(1);
			_store.Submissions.Should().ContainSingle().Which.Destination.Should().Be(RmsSubmissionDestinations.NerisIncidentAnalysis);

			// A second pass must not re-queue what is already in flight.
			(await _service.QueueAwaitingIncidentAsync(Dept)).Should().Be(0);
			_store.Submissions.Should().ContainSingle();
		}

		[Test]
		public async Task An_analysis_in_flight_cannot_be_voided_until_it_settles()
		{
			_report.NerisIncidentId = "FD24027000I2026000200";
			var saved = await StartAndFillAsync();
			var finalized = await _service.FinalizeAsync(Dept, "investigator", saved.Analysis.RmsIncidentAnalysisId, saved.Analysis.RowVersion);
			finalized.State.Should().Be(RmsIncidentAnalysisState.Submitted);

			Func<Task> act = () => _service.VoidAsync(Dept, "chief", finalized.Analysis.RmsIncidentAnalysisId, "Superseded", "Reopened the investigation.");

			await act.Should().ThrowAsync<InvalidOperationException>("voiding what the destination may already hold would leave the two out of step");
		}

		[Test]
		public async Task Voiding_a_rejected_analysis_supersedes_its_open_submission()
		{
			_report.NerisIncidentId = "FD24027000I2026000200";
			var saved = await StartAndFillAsync();
			var finalized = await _service.FinalizeAsync(Dept, "investigator", saved.Analysis.RmsIncidentAnalysisId, saved.Analysis.RowVersion);

			// The destination rejected it; the department decides not to correct and resubmit.
			var analysis = _store.Analyses.Single();
			analysis.State = (int)RmsIncidentAnalysisState.Rejected;

			var voided = await _service.VoidAsync(Dept, "chief", finalized.Analysis.RmsIncidentAnalysisId, "Superseded", "Reopened the investigation.");

			voided.State.Should().Be(RmsIncidentAnalysisState.Voided);
			_store.Submissions.Single().State.Should().Be((int)RmsSubmissionState.Superseded);
		}

		[Test]
		public async Task A_stale_row_version_is_refused()
		{
			var saved = await StartAndFillAsync();

			Func<Task> act = () => _service.SaveDraftAsync(Dept, "investigator", saved.Analysis.RmsIncidentAnalysisId, saved.Analysis.RowVersion - 1, CompleteDraft());

			await act.Should().ThrowAsync<RecordConcurrencyException>();
		}
	}
}
