using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Neris;
using Resgrid.Services;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// RMS-2 incident report lifecycle (plan sections 4.2, 5.2.1, 5.3): source-aware prefill with provenance,
	/// SingleAuthoritative start, corrections on provenance rows, validation gating the signature, the revision +
	/// attestation + submission chain, correction after rejection, and void superseding open submissions.
	/// </summary>
	[TestFixture]
	public class IncidentReportsServiceTests
	{
		private const int Dept = 42;
		private const int CallId = 77;
		private static readonly DateTime LoggedOn = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

		private FakeIncidentStore _store;
		private Mock<IDepartmentSettingsService> _settings;
		private Mock<IDepartmentGroupsService> _groups;
		private Mock<IUserProfileService> _profiles;
		private Mock<IPersonnelRolesService> _roles;
		private Mock<IUnitsService> _units;
		private Mock<ICallsService> _calls;
		private Mock<IDepartmentDataProtectionService> _adp;
		private Mock<INerisProfileService> _neris;
		private Mock<INerisValidationService> _validation;
		private Mock<IEventAggregator> _aggregator;
		private RmsNerisProfile _profile;
		private bool _submissionEnabled;
		private List<RmsValidationIssue> _localIssues;
		private Call _call;
		private IncidentReportsService _service;

		[SetUp]
		public void SetUp()
		{
			Resgrid.Config.SystemBehaviorConfig.CacheEnabled = false;
			_store = new FakeIncidentStore();

			_settings = new Mock<IDepartmentSettingsService>();
			_settings.Setup(s => s.GetRecordsNumberingConfigAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new RecordsNumberingConfig());
			_settings.Setup(s => s.GetRecordsReviewDueHoursAsync(Dept, It.IsAny<bool>())).ReturnsAsync(72);

			_groups = new Mock<IDepartmentGroupsService>();
			_groups.Setup(g => g.GetGroupForUserAsync("author", Dept)).ReturnsAsync(new DepartmentGroup { DepartmentGroupId = 11, Name = "Station 1" });

			_profiles = new Mock<IUserProfileService>();
			_profiles.Setup(p => p.GetProfileByUserIdAsync(It.IsAny<string>(), It.IsAny<bool>())).ReturnsAsync((string id, bool b) => new UserProfile { UserId = id, FirstName = "First", LastName = id });

			_roles = new Mock<IPersonnelRolesService>();
			_roles.Setup(r => r.GetRolesForUserAsync("author", Dept)).ReturnsAsync(new List<PersonnelRole> { new PersonnelRole { Name = "Captain" } });

			_units = new Mock<IUnitsService>();
			_units.Setup(u => u.GetUnitByIdAsync(5)).ReturnsAsync(new Unit { UnitId = 5, DepartmentId = Dept, Name = "Engine 5", Type = "Engine", StationGroupId = 13 });
			_units.Setup(u => u.GetUnitStatesForCallAsync(Dept, CallId)).ReturnsAsync(new List<UnitState>
			{
				new UnitState { UnitId = 5, State = (int)UnitStateTypes.Responding, Timestamp = LoggedOn.AddMinutes(2) },
				new UnitState { UnitId = 5, State = (int)UnitStateTypes.OnScene, Timestamp = LoggedOn.AddMinutes(10) },
				new UnitState { UnitId = 5, State = (int)UnitStateTypes.Available, Timestamp = LoggedOn.AddMinutes(60) }
			});

			_call = new Call
			{
				CallId = CallId, DepartmentId = Dept, Number = "2026-000123", Name = "Structure fire", Type = "Fire", Priority = 3,
				LoggedOn = LoggedOn, Address = "1 Main St", GeoLocationData = "39.5,-104.9", NatureOfCall = "Smoke showing",
				CallNotes = new List<CallNote> { new CallNote { CallNoteId = 1, Note = "Caller reports smoke from the roof", Timestamp = LoggedOn.AddMinutes(1) } },
				UnitDispatches = new List<CallDispatchUnit> { new CallDispatchUnit { CallDispatchUnitId = 1, UnitId = 5, DispatchedOn = LoggedOn.AddMinutes(1) } }
			};
			_calls = new Mock<ICallsService>();
			_calls.Setup(c => c.GetCallByIdAsync(CallId, It.IsAny<bool>())).ReturnsAsync(() => _call);
			_calls.Setup(c => c.PopulateCallData(It.IsAny<Call>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()))
				.ReturnsAsync(() => _call);

			_adp = new Mock<IDepartmentDataProtectionService>();
			_adp.Setup(a => a.GetPinnedCatalogVersionAsync(Dept)).ReturnsAsync(0);

			_profile = new RmsNerisProfile { DepartmentId = Dept, NerisEntityId = "FD24027000", ContractVersion = "1.4.78", AutoSubmitOnFinalize = true, IsEnabled = true };
			_submissionEnabled = true;
			_neris = new Mock<INerisProfileService>();
			_neris.SetupGet(n => n.ContractVersion).Returns("1.4.78");
			_neris.Setup(n => n.GetProfileAsync(Dept)).ReturnsAsync(() => _profile);
			_neris.Setup(n => n.IsSubmissionEnabledAsync(Dept)).ReturnsAsync(() => _submissionEnabled);
			_neris.Setup(n => n.ResolveCrosswalkAsync(Dept, "incident_type", NerisCrosswalkSources.CallType, "Fire")).ReturnsAsync("FIRE||STRUCTURE_FIRE||RESIDENTIAL");

			_localIssues = new List<RmsValidationIssue>();
			_validation = new Mock<INerisValidationService>();
			_validation.Setup(v => v.ValidateLocal(It.IsAny<NerisIncidentSnapshot>(), It.IsAny<RmsNerisProfile>())).Returns(() => _localIssues.ToList());

			_aggregator = new Mock<IEventAggregator>();
			var outbox = new DomainEventOutboxService(_store.Shared.OutboxRepo.Object, _aggregator.Object);

			_service = new IncidentReportsService(_store.ReportsRepo.Object, _store.FactsRepo.Object, _store.UnitsRepo.Object, _store.TypesRepo.Object,
				_store.TacticsRepo.Object, _store.AidsRepo.Object, _store.LocationsRepo.Object, _store.NarrativesRepo.Object, _store.IssuesRepo.Object,
				_store.SubmissionsRepo.Object, _store.SignaturesRepo.Object, _store.Shared.RevisionsRepo.Object, _store.Shared.AuditsRepo.Object,
				_store.Shared.ScopesRepo.Object, _store.Shared.SharesRepo.Object, _store.Shared.ProjectionsRepo.Object, outbox, _settings.Object,
				_groups.Object, _profiles.Object, _roles.Object, _units.Object, _calls.Object, _adp.Object, _store.UnitOfWork.Object,
				_neris.Object, new NerisMappingService(), _validation.Object);
		}

		[Test]
		public async Task Start_from_call_prefills_dispatch_facts_with_provenance()
		{
			var aggregate = await _service.StartFromCallAsync(Dept, "author", CallId);

			aggregate.State.Should().Be(RmsRecordState.Draft);
			aggregate.Report.ReportingEntityId.Should().Be("FD24027000");
			aggregate.Report.DefinitionKey.Should().Be(RmsDefinitionKeys.NerisIncidentReport);
			aggregate.Report.IncidentNumber.Should().Be("2026-000123");
			aggregate.Report.CallCreatedOn.Should().Be(LoggedOn);
			aggregate.Report.CallArrivalOn.Should().Be(LoggedOn.AddMinutes(10), "first on-scene from the unit state log");
			aggregate.Report.DraftReference.Should().StartWith("I-");
			aggregate.Report.RecordNumber.Should().BeNull("numbers are allocated at finalize");

			aggregate.Location.Should().NotBeNull();
			aggregate.Location.AddressText.Should().Be("1 Main St");
			aggregate.Location.Latitude.Should().Be(39.5m);
			aggregate.Location.SourceKind.Should().Be((int)RmsSourceKind.Dispatch);

			aggregate.Units.Should().ContainSingle();
			var unit = aggregate.Units[0];
			unit.UnitId.Should().Be(5);
			unit.DispatchedOn.Should().Be(LoggedOn.AddMinutes(1));
			unit.EnrouteOn.Should().Be(LoggedOn.AddMinutes(2));
			unit.OnSceneOn.Should().Be(LoggedOn.AddMinutes(10));
			unit.ClearedOn.Should().Be(LoggedOn.AddMinutes(60));
			unit.TimesSourceKind.Should().Be((int)RmsSourceKind.App);

			aggregate.Types.Should().ContainSingle(t => t.IsPrimary && t.TypeCode == "FIRE||STRUCTURE_FIRE||RESIDENTIAL" && t.LocalCode == "Fire");

			var facts = aggregate.Facts;
			facts.Single(f => f.FactKey == NerisFactKeys.IncidentNumber).SourceKind.Should().Be((int)RmsSourceKind.Dispatch);
			facts.Single(f => f.FactKey == NerisFactKeys.CallAnswered).SourceKind.Should().Be((int)RmsSourceKind.Derived, "Resgrid holds no PSAP answered time");
			facts.Single(f => f.FactKey == NerisFactKeys.UnitTime(5, "on_scene")).SourceKind.Should().Be((int)RmsSourceKind.App);
			facts.Single(f => f.FactKey == NerisFactKeys.IncidentType).SourceKind.Should().Be((int)RmsSourceKind.Derived);
			facts.Should().ContainSingle(f => f.FactKey == IncidentReportsService.DispatchCommentFactPrefix + "0" && f.SourceValue == "Caller reports smoke from the roof");
			facts.Should().OnlyContain(f => f.CorrectedOn == null && f.RevisionId == null);

			_store.Outbox.Should().ContainSingle(o => o.TriggerEventType == (int)WorkflowTriggerEventType.RecordCreated && o.AggregateType == IncidentReportsService.IncidentAggregate);
			_store.Projections.Should().ContainSingle(p => p.RmsRecordSearchProjectionId == aggregate.Report.RmsIncidentReportId && p.RecordKind == (int)RmsRecordKind.IncidentReport && p.CallId == CallId);
			_store.Scopes.Should().Contain(s => s.DepartmentGroupId == 11 && s.AnchorType == (int)RmsGroupScopeAnchorType.Author);
			_store.Scopes.Should().Contain(s => s.DepartmentGroupId == 13 && s.AnchorType == (int)RmsGroupScopeAnchorType.Unit);
			_store.Audits.Should().Contain(a => a.RecordId == aggregate.Report.RmsIncidentReportId && a.Action == (int)RmsAccessAuditAction.Change);
		}

		[Test]
		public async Task Start_from_call_twice_returns_the_existing_report()
		{
			var first = await _service.StartFromCallAsync(Dept, "author", CallId);
			var second = await _service.StartFromCallAsync(Dept, "p2", CallId);

			second.Report.RmsIncidentReportId.Should().Be(first.Report.RmsIncidentReportId, "one authoritative report per entity per Call");
			_store.Reports.Should().ContainSingle();
			_store.Outbox.Should().ContainSingle();
		}

		[Test]
		public async Task Save_draft_records_corrections_on_the_provenance_row()
		{
			var started = await _service.StartFromCallAsync(Dept, "author", CallId);
			var version = started.Report.RowVersion;
			var input = DraftFrom(started);
			input.IncidentNumber = "2026-000124";
			input.Units[0].OnSceneOn = LoggedOn.AddMinutes(12);
			input.Narrative = "Fire confined to the attic.";
			input.Location.Street = "Main St";

			var saved = await _service.SaveDraftAsync(Dept, "author", started.Report.RmsIncidentReportId, started.Report.RowVersion, input);

			saved.Report.IncidentNumber.Should().Be("2026-000124");
			var number = saved.Facts.Single(f => f.FactKey == NerisFactKeys.IncidentNumber);
			number.SourceValue.Should().Be("2026-000123", "the dispatch value survives");
			number.CurrentValue.Should().Be("2026-000124");
			number.CorrectedOn.Should().NotBeNull();
			number.CorrectedByUserId.Should().Be("author");

			var onScene = saved.Facts.Single(f => f.FactKey == NerisFactKeys.UnitTime(5, "on_scene"));
			onScene.CurrentValue.Should().Be(IncidentReportsService.Iso(LoggedOn.AddMinutes(12)));
			onScene.SourceValue.Should().Be(IncidentReportsService.Iso(LoggedOn.AddMinutes(10)));
			saved.Facts.Single(f => f.FactKey == NerisFactKeys.UnitTime(5, "dispatch")).CorrectedOn.Should().BeNull("unchanged values keep their provenance untouched");

			saved.Units.Should().ContainSingle(u => u.UnitId == 5 && u.OnSceneOn == LoggedOn.AddMinutes(12));
			saved.Narrative.Narrative.Should().Be("Fire confined to the attic.");
			saved.Location.Street.Should().Be("Main St");
			saved.Report.RowVersion.Should().Be(version + 1);
			_store.Outbox.Should().ContainSingle("draft saves never raise Workflow events");
		}

		[Test]
		public async Task Finalize_is_blocked_by_local_validation_errors()
		{
			var started = await _service.StartFromCallAsync(Dept, "author", CallId);
			_localIssues.Add(new RmsValidationIssue { RuleKey = "neris.dispatch.call_answered.required", Severity = (int)RmsValidationSeverity.Error, FieldPath = "dispatch.call_answered", Message = "required" });

			Func<Task> act = () => _service.FinalizeAsync(Dept, "author", started.Report.RmsIncidentReportId, started.Report.RowVersion, null, "10.0.0.1", null, null);

			await act.Should().ThrowAsync<IncidentReportValidationException>();
			_store.Reports.Single().State.Should().Be((int)RmsRecordState.Draft);
			_store.Revisions.Should().BeEmpty();
			_store.Signatures.Should().BeEmpty();
			_store.Submissions.Should().BeEmpty();
			_store.Issues.Should().ContainSingle(i => i.RuleKey == "neris.dispatch.call_answered.required" && i.Source == (int)RmsValidationSource.Local);
			_store.Shared.Discards.Should().BeGreaterThan(0, "the transaction rolls back");
		}

		[Test]
		public async Task Finalize_writes_revision_signature_and_queues_the_submission()
		{
			var started = await _service.StartFromCallAsync(Dept, "author", CallId);
			var reportId = started.Report.RmsIncidentReportId;

			var final = await _service.FinalizeAsync(Dept, "author", reportId, started.Report.RowVersion, null, "10.0.0.1", null, null);

			final.State.Should().Be(RmsRecordState.Submitted, "auto-submit is on and the destination is enabled");
			final.Report.RecordNumber.Should().StartWith(IncidentReportsService.NumberPrefix + "-");
			final.Report.FinalizedOn.Should().NotBeNull();
			final.Report.RevisionCount.Should().Be(1);

			var revision = _store.Revisions.Single();
			revision.RecordKind.Should().Be((int)RmsRecordKind.IncidentReport);
			revision.Transition.Should().Be((int)RmsRevisionTransition.Finalized);
			revision.Checksum.Should().NotBeNullOrEmpty();
			revision.AttestationStatementVersion.Should().Be(IncidentReportsService.AttestationStatementVersion);
			final.Report.CurrentRevisionId.Should().Be(revision.RmsRevisionId);
			_store.Facts.Should().Contain(f => f.RevisionId == revision.RmsRevisionId, "the revision keeps its own copies of the provenance rows");
			_store.Units.Should().Contain(u => u.RevisionId == revision.RmsRevisionId);

			var signature = _store.Signatures.Single();
			signature.RevisionId.Should().Be(revision.RmsRevisionId);
			signature.ArtifactChecksum.Should().Be(revision.Checksum, "the attestation binds to the revision checksum");
			signature.Intent.Should().Be((int)RmsSignatureIntent.Attestation);
			signature.SignerRoleSnapshot.Should().Be("Captain");
			signature.IpAddress.Should().Be("10.0.0.1");

			var submission = _store.Submissions.Single();
			submission.State.Should().Be((int)RmsSubmissionState.Queued);
			submission.Destination.Should().Be(RmsSubmissionDestinations.Neris);
			submission.DestinationVersion.Should().Be("1.4.78");
			submission.RevisionId.Should().Be(revision.RmsRevisionId);
			submission.IdempotencyKey.Should().Be(IncidentReportsService.IdempotencyKey(Dept, reportId, revision.RmsRevisionId));
			submission.PayloadJson.Should().Contain("\"department_neris_id\":\"FD24027000\"");
			submission.PayloadChecksum.Should().Be(RecordSnapshotSerializer.Checksum(submission.PayloadJson));
			final.Report.LastSubmissionId.Should().Be(submission.RmsSubmissionId);
			final.Submissions.Should().ContainSingle();

			_store.Outbox.Select(o => o.TriggerEventType).Should().ContainInOrder((int)WorkflowTriggerEventType.RecordCreated, (int)WorkflowTriggerEventType.RecordFinalized, (int)WorkflowTriggerEventType.RecordSubmissionQueued);
			var queued = JObject.Parse(_store.Outbox.Single(o => o.TriggerEventType == (int)WorkflowTriggerEventType.RecordSubmissionQueued).PayloadJson);
			queued["submission"]["state"].Value<string>().Should().Be("Queued");
			queued["submission"]["destination"].Value<string>().Should().Be("NERIS");
			queued["submission"].Children<JProperty>().Select(p => p.Name).Should().NotContain(new[] { "payload", "payload_json", "response" }, "the sanitized block never carries payloads");
			queued["record"]["kind"].Value<string>().Should().Be("IncidentReport");
			_store.Audits.Should().Contain(a => a.Action == (int)RmsAccessAuditAction.Sign && a.IpAddress == "10.0.0.1");
			_store.Audits.Should().Contain(a => a.Action == (int)RmsAccessAuditAction.Submit);
		}

		[Test]
		public async Task Finalize_without_auto_submit_stays_finalized_until_queued_explicitly()
		{
			_profile.AutoSubmitOnFinalize = false;
			var started = await _service.StartFromCallAsync(Dept, "author", CallId);
			var reportId = started.Report.RmsIncidentReportId;

			var final = await _service.FinalizeAsync(Dept, "author", reportId, started.Report.RowVersion, null, null, null, null);
			final.State.Should().Be(RmsRecordState.Finalized);
			_store.Submissions.Should().BeEmpty();

			var queued = await _service.QueueSubmissionAsync(Dept, "author", reportId);
			queued.State.Should().Be(RmsRecordState.Submitted);
			_store.Submissions.Should().ContainSingle(s => s.State == (int)RmsSubmissionState.Queued && s.RevisionId == final.Report.CurrentRevisionId);
			_store.Outbox.Should().ContainSingle(o => o.TriggerEventType == (int)WorkflowTriggerEventType.RecordSubmissionQueued);

			Func<Task> again = () => _service.QueueSubmissionAsync(Dept, "author", reportId);
			await again.Should().ThrowAsync<InvalidOperationException>("the same revision is already queued");
		}

		[Test]
		public async Task Queue_submission_refuses_when_the_destination_is_disabled()
		{
			_submissionEnabled = false;
			var started = await _service.StartFromCallAsync(Dept, "author", CallId);

			var final = await _service.FinalizeAsync(Dept, "author", started.Report.RmsIncidentReportId, started.Report.RowVersion, null, null, null, null);
			final.State.Should().Be(RmsRecordState.Finalized, "nothing is queued while the destination is off");
			_store.Submissions.Should().BeEmpty();

			Func<Task> act = () => _service.QueueSubmissionAsync(Dept, "author", started.Report.RmsIncidentReportId);
			await act.Should().ThrowAsync<InvalidOperationException>();
		}

		[Test]
		public async Task Correct_and_resubmit_after_rejection_issues_a_new_revision_and_key()
		{
			var started = await _service.StartFromCallAsync(Dept, "author", CallId);
			var reportId = started.Report.RmsIncidentReportId;
			var final = await _service.FinalizeAsync(Dept, "author", reportId, started.Report.RowVersion, null, null, null, null);
			var firstRevision = final.Report.CurrentRevisionId;
			var firstSubmission = _store.Submissions.Single();

			// The worker rejected the delivery (RecordsSubmissionService is covered separately).
			var report = _store.Reports.Single();
			report.State = (int)RmsRecordState.Rejected;
			report.RejectedOn = DateTime.UtcNow;
			report.RejectionSummary = "dispatch.call_answered (required)";
			firstSubmission.State = (int)RmsSubmissionState.Rejected;
			firstSubmission.CompletedOn = DateTime.UtcNow;

			var input = DraftFrom(await _service.GetAsync(Dept, reportId));
			input.CallAnsweredOn = LoggedOn.AddSeconds(30);
			var edited = await _service.SaveDraftAsync(Dept, "author", reportId, report.RowVersion, input);
			edited.State.Should().Be(RmsRecordState.Rejected, "a rejected report is editable in place");

			var corrected = await _service.CorrectAndResubmitAsync(Dept, "author", reportId, edited.Report.RowVersion, null, null, "destination-rejection", "Answered time added");

			corrected.State.Should().Be(RmsRecordState.Submitted);
			corrected.Report.RevisionCount.Should().Be(2);
			var second = _store.Revisions.Single(r => r.RevisionNumber == 2);
			second.PriorRevisionId.Should().Be(firstRevision, "N+1 references N");
			second.Transition.Should().Be((int)RmsRevisionTransition.Amended);
			second.ReasonCode.Should().Be("destination-rejection");
			_store.Signatures.Should().HaveCount(2);

			_store.Submissions.Should().HaveCount(2);
			var resubmission = _store.Submissions.Single(s => s.RevisionId == second.RmsRevisionId);
			resubmission.State.Should().Be((int)RmsSubmissionState.Queued);
			resubmission.IdempotencyKey.Should().NotBe(firstSubmission.IdempotencyKey, "a new revision gets a new idempotency key");
			resubmission.PayloadJson.Should().Contain(IncidentReportsService.Iso(LoggedOn.AddSeconds(30)));
			firstSubmission.State.Should().Be((int)RmsSubmissionState.Rejected, "closed submissions are history, never rewritten");

			_store.Outbox.Count(o => o.TriggerEventType == (int)WorkflowTriggerEventType.RecordAmended).Should().Be(1);
			_store.Outbox.Count(o => o.TriggerEventType == (int)WorkflowTriggerEventType.RecordSubmissionQueued).Should().Be(2);
		}

		[Test]
		public async Task Correct_and_resubmit_requires_a_rejected_report()
		{
			var started = await _service.StartFromCallAsync(Dept, "author", CallId);

			Func<Task> act = () => _service.CorrectAndResubmitAsync(Dept, "author", started.Report.RmsIncidentReportId, started.Report.RowVersion, null, null, "x", null);

			await act.Should().ThrowAsync<RecordTransitionException>();
		}

		[Test]
		public async Task Void_waits_for_the_destination_answer_then_records_a_void_revision()
		{
			var started = await _service.StartFromCallAsync(Dept, "author", CallId);
			var reportId = started.Report.RmsIncidentReportId;
			await _service.FinalizeAsync(Dept, "author", reportId, started.Report.RowVersion, null, null, null, null);
			_store.Submissions.Single().State.Should().Be((int)RmsSubmissionState.Queued);

			// In flight: the destination has (or may have) the revision, so the void must wait for its answer.
			Func<Task> inFlight = () => _service.VoidAsync(Dept, "author", reportId, "duplicate", "Entered twice");
			await inFlight.Should().ThrowAsync<RecordTransitionException>();

			// The worker rejected the delivery (RecordsSubmissionService is covered separately).
			var report = _store.Reports.Single();
			report.State = (int)RmsRecordState.Rejected;
			var submission = _store.Submissions.Single();
			submission.State = (int)RmsSubmissionState.Rejected;
			submission.CompletedOn = DateTime.UtcNow;

			var voided = await _service.VoidAsync(Dept, "author", reportId, "duplicate", "Entered twice");

			voided.State.Should().Be(RmsRecordState.Voided);
			voided.Report.RevisionCount.Should().Be(2);
			voided.Report.VoidReasonCode.Should().Be("duplicate");
			var revision = _store.Revisions.Single(r => r.RevisionNumber == 2);
			revision.Transition.Should().Be((int)RmsRevisionTransition.Voided);
			revision.AttestedOn.Should().BeNull("a void is not attested");
			_store.Signatures.Should().ContainSingle("only the finalize was signed");
			_store.Submissions.Single().State.Should().Be((int)RmsSubmissionState.Rejected, "closed submissions are history");
			_store.Outbox.Should().ContainSingle(o => o.TriggerEventType == (int)WorkflowTriggerEventType.RecordVoided);
			_store.Projections.Single().State.Should().Be((int)RmsRecordState.Voided);
		}

		[Test]
		public async Task Cancel_from_draft_keeps_no_number_and_raises_107()
		{
			var started = await _service.StartFromCallAsync(Dept, "author", CallId);

			var cancelled = await _service.CancelAsync(Dept, "author", started.Report.RmsIncidentReportId);

			cancelled.State.Should().Be(RmsRecordState.Cancelled);
			cancelled.Report.RecordNumber.Should().BeNull();
			var payload = JObject.Parse(_store.Outbox.Single(o => o.TriggerEventType == (int)WorkflowTriggerEventType.RecordCancelled).PayloadJson);
			payload["extra"]["number_disposition"].Value<string>().Should().Be("none");
		}

		[Test]
		public async Task Snapshot_carries_dispatch_comments_in_time_order()
		{
			var started = await _service.StartFromCallAsync(Dept, "author", CallId);

			var snapshot = await _service.BuildSnapshotAsync(Dept, started.Report.RmsIncidentReportId);

			snapshot.Report.RmsIncidentReportId.Should().Be(started.Report.RmsIncidentReportId);
			snapshot.DispatchComments.Should().ContainSingle(c => c.Comment == "Caller reports smoke from the roof" && c.Timestamp == LoggedOn.AddMinutes(1));
			snapshot.Units.Should().ContainSingle();
		}

		/// <summary>A draft input carrying the aggregate's current values, so a test can change one thing and save.</summary>
		private static IncidentReportDraftInput DraftFrom(IncidentReportAggregate a)
		{
			var r = a.Report;
			return new IncidentReportDraftInput
			{
				IncidentNumber = r.IncidentNumber,
				CallCreatedOn = r.CallCreatedOn,
				CallAnsweredOn = r.CallAnsweredOn,
				CallArrivalOn = r.CallArrivalOn,
				IncidentClearedOn = r.IncidentClearedOn,
				DispatchCenterId = r.DispatchCenterId,
				DeterminantCode = r.DeterminantCode,
				DispatchIncidentCode = r.DispatchIncidentCode,
				Disposition = r.Disposition,
				PeoplePresent = r.PeoplePresent,
				DisplacementCount = r.DisplacementCount,
				AnimalsRescued = r.AnimalsRescued,
				StationGroupId = r.StationGroupId,
				Location = a.Location == null ? null : new IncidentLocationInput
				{
					AddressText = a.Location.AddressText, Number = a.Location.Number, Street = a.Location.Street, Municipality = a.Location.Municipality,
					County = a.Location.County, State = a.Location.State, PostalCode = a.Location.PostalCode, Country = a.Location.Country,
					Latitude = a.Location.Latitude, Longitude = a.Location.Longitude
				},
				Types = a.Types.Select(t => new IncidentTypeInput { TypeCode = t.TypeCode, IsPrimary = t.IsPrimary }).ToList(),
				Units = a.Units.Select(u => new IncidentUnitResponseInput
				{
					UnitId = u.UnitId, UnitNerisId = u.UnitNerisId, Staffing = u.Staffing, DispatchedOn = u.DispatchedOn, EnrouteOn = u.EnrouteOn,
					OnSceneOn = u.OnSceneOn, StagingOn = u.StagingOn, CanceledEnrouteOn = u.CanceledEnrouteOn, ClearedOn = u.ClearedOn, ResponseMode = u.ResponseMode, TransportMode = u.TransportMode
				}).ToList(),
				Aids = a.Aids.Select(x => new IncidentAidInput { Direction = x.Direction, AidType = x.AidType, CounterpartNerisId = x.CounterpartNerisId, CounterpartName = x.CounterpartName, IsNonFireDepartment = x.IsNonFireDepartment, NonFdType = x.NonFdType }).ToList(),
				Tactics = a.Tactics.Select(t => new IncidentTacticInput { TacticCode = t.TacticCode, ActorUnitId = t.ActorUnitId, OccurredOn = t.OccurredOn }).ToList(),
				Narrative = a.Narrative?.Narrative,
				ImpedimentNarrative = a.Narrative?.ImpedimentNarrative,
				OutcomeNarrative = a.Narrative?.OutcomeNarrative,
				SupplementalJson = a.Narrative?.SupplementalJson
			};
		}
	}
}
