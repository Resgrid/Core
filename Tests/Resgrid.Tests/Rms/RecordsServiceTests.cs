using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// Aggregate behavior for the locked Logs-parity definitions: draft create/save with ETags, the
	/// Quick Entry finalize path with numbering and immutable revisions, amendment, void, cancel, group
	/// scope, search projection and exactly-one outbox row per committed transition (RMS plan section 7).
	/// </summary>
	[TestFixture]
	public class RecordsServiceTests
	{
		private const int Dept = 9;
		private FakeRmsStore _store;
		private Mock<IRecordsCutoverService> _cutover;
		private Mock<IDepartmentSettingsService> _settings;
		private Mock<IDepartmentGroupsService> _groups;
		private Mock<IUserProfileService> _profiles;
		private Mock<IUnitsService> _units;
		private Mock<ICallsService> _calls;
		private Mock<IDepartmentDataProtectionService> _adp;
		private Mock<IEventAggregator> _aggregator;
		private List<Resgrid.Model.Events.DomainEventDispatchedEvent> _published;
		private List<Resgrid.Model.Queue.NotificationItem> _notifications;
		private Mock<Resgrid.Model.Providers.IOutboundQueueProvider> _outboundQueue;
		private RecordsService _service;
		private Mock<IRecordsEvidenceService> _evidence;
		/// <summary>Revisions the finalize path bound draft evidence to (RMS-3c).</summary>
		private readonly List<string> _boundRevisions = new List<string>();

		[SetUp]
		public void SetUp()
		{
			Resgrid.Config.SystemBehaviorConfig.CacheEnabled = false;
			_store = new FakeRmsStore();
			_cutover = new Mock<IRecordsCutoverService>();
			_cutover.Setup(c => c.GetModuleStateAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new RecordsModuleState { DepartmentId = Dept, FlagEnabled = true, Activated = true, CutoverState = RmsDepartmentCutoverState.Active, LegacyWritesBlocked = true });

			_settings = new Mock<IDepartmentSettingsService>();
			_settings.Setup(s => s.GetRecordsNumberingConfigAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new RecordsNumberingConfig());
			_settings.Setup(s => s.GetRecordsReviewDueHoursAsync(Dept, It.IsAny<bool>())).ReturnsAsync(72);

			_groups = new Mock<IDepartmentGroupsService>();
			_groups.Setup(g => g.GetGroupForUserAsync("author", Dept)).ReturnsAsync(new DepartmentGroup { DepartmentGroupId = 11, Name = "Station 1" });
			_groups.Setup(g => g.GetGroupForUserAsync("p2", Dept)).ReturnsAsync(new DepartmentGroup { DepartmentGroupId = 12, Name = "Station 2" });

			_profiles = new Mock<IUserProfileService>();
			_profiles.Setup(p => p.GetProfileByUserIdAsync(It.IsAny<string>(), It.IsAny<bool>())).ReturnsAsync((string id, bool b) => new UserProfile { UserId = id, FirstName = "First", LastName = id });

			_units = new Mock<IUnitsService>();
			_units.Setup(u => u.GetUnitByIdAsync(5)).ReturnsAsync(new Unit { UnitId = 5, DepartmentId = Dept, Name = "Engine 5", Type = "Engine", StationGroupId = 13 });
			_units.Setup(u => u.GetUnitByIdAsync(99)).ReturnsAsync(new Unit { UnitId = 99, DepartmentId = 1, Name = "Other dept" });

			_calls = new Mock<ICallsService>();
			_calls.Setup(c => c.GetCallByIdAsync(77, It.IsAny<bool>())).ReturnsAsync(new Call { CallId = 77, DepartmentId = Dept, Number = "C2026-0009", Name = "Structure fire", Type = "Fire", Priority = 3, LoggedOn = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc), Address = "1 Main St", NatureOfCall = "Smoke showing" });

			_adp = new Mock<IDepartmentDataProtectionService>();
			_adp.Setup(a => a.GetPinnedCatalogVersionAsync(Dept)).ReturnsAsync(0);

			_published = new List<Resgrid.Model.Events.DomainEventDispatchedEvent>();
			_aggregator = new Mock<IEventAggregator>();
			_aggregator.Setup(a => a.SendMessage(It.IsAny<Resgrid.Model.Events.DomainEventDispatchedEvent>())).Callback<Resgrid.Model.Events.DomainEventDispatchedEvent>(e => _published.Add(e));
			var outbox = new DomainEventOutboxService(_store.OutboxRepo.Object, _aggregator.Object);

			_notifications = new List<Resgrid.Model.Queue.NotificationItem>();
			_outboundQueue = new Mock<Resgrid.Model.Providers.IOutboundQueueProvider>();
			_outboundQueue.Setup(q => q.EnqueueNotification(It.IsAny<Resgrid.Model.Queue.NotificationItem>()))
				.Callback<Resgrid.Model.Queue.NotificationItem>(n => _notifications.Add(n)).ReturnsAsync(true);

			_evidence = new Mock<IRecordsEvidenceService>();
			_evidence.Setup(e => e.BindToRevisionAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int d, string r, string rev, CancellationToken c) => { _boundRevisions.Add(rev); return 0; });

			_service = new RecordsService(_store.RecordsRepo.Object, new Resgrid.Services.Records.RmsRecordValueService(_store.DetailsRepo.Object), _store.ParticipantsRepo.Object, _store.UnitsRepo.Object,
				_store.AttachmentsRepo.Object, _store.RevisionsRepo.Object, _evidence.Object, _store.ScopesRepo.Object, _store.SharesRepo.Object, _store.ProjectionsRepo.Object,
				_store.AuditsRepo.Object, outbox, _cutover.Object, _settings.Object, _groups.Object, _profiles.Object, _units.Object, _calls.Object, _adp.Object,
				_store.UnitOfWork.Object, _outboundQueue.Object, new Resgrid.Services.Records.NullRecordAttachmentScanner());
		}

		private static RecordDraftInput TrainingInput(string narrative = "Hose evolutions")
		{
			return new RecordDraftInput
			{
				DefinitionKey = RmsDefinitionKeys.Training,
				StartedOn = new DateTime(2026, 9, 1, 18, 0, 0, DateTimeKind.Utc),
				EndedOn = new DateTime(2026, 9, 1, 20, 0, 0, DateTimeKind.Utc),
				Details = new RmsOperationalRecordDetail { Narrative = narrative, Course = "Engine Ops", CourseCode = "ENG-101" },
				Participants = new List<RecordParticipantInput> { new RecordParticipantInput { UserId = "p2", Role = "Attendee" } },
				Units = new List<RecordUnitResponseInput> { new RecordUnitResponseInput { UnitId = 5 } }
			};
		}

		[Test]
		public async Task Create_draft_persists_header_details_snapshots_scope_projection_and_one_created_event()
		{
			var aggregate = await _service.CreateDraftAsync(Dept, "author", TrainingInput());

			var record = aggregate.Record;
			record.State.Should().Be((int)RmsRecordState.Draft);
			record.LifecyclePreset.Should().Be((int)RmsLifecyclePreset.QuickEntry);
			record.RecordType.Should().Be((int)RmsOperationalRecordType.Training);
			record.RecordNumber.Should().BeNull("numbers are assigned at finalization under the default policy");
			record.DraftReference.Should().MatchRegex("^D-[0-9A-Z]{5}$");
			record.AuthorUserId.Should().Be("author");
			record.OwnerUserId.Should().Be("author");
			record.AuthorGroupIdSnapshot.Should().Be(11);
			record.StationGroupId.Should().Be(11, "defaults to the author's group");
			record.RowVersion.Should().Be(1);
			Guid.TryParse(record.ProtectionId, out _).Should().BeTrue();
			record.DisplaySummary.Should().Be("Engine Ops (ENG-101)");

			aggregate.Details.Narrative.Should().Be("Hose evolutions");
			aggregate.Details.RevisionId.Should().BeNull();
			aggregate.Participants.Single().DisplayNameSnapshot.Should().Be("First p2");
			aggregate.Participants.Single().GroupIdSnapshot.Should().Be(12);
			aggregate.Units.Single().UnitNameSnapshot.Should().Be("Engine 5");
			aggregate.Units.Single().StationGroupIdSnapshot.Should().Be(13);

			_store.Scopes.Select(s => (s.DepartmentGroupId, (RmsGroupScopeAnchorType)s.AnchorType)).Should().BeEquivalentTo(new[]
			{
				(11, RmsGroupScopeAnchorType.RecordGroup), (11, RmsGroupScopeAnchorType.Author), (12, RmsGroupScopeAnchorType.Participant), (13, RmsGroupScopeAnchorType.Unit)
			});

			var projection = _store.Projections.Single();
			projection.RmsRecordSearchProjectionId.Should().Be(record.RmsOperationalRecordId);
			projection.State.Should().Be((int)RmsRecordState.Draft);
			projection.ParticipantUserIds.Should().Be("p2");
			projection.UnitIds.Should().Be("5");
			projection.GroupScopeIds.Split(',').Should().BeEquivalentTo(new[] { "11", "12", "13" });
			projection.SearchText.Should().NotContain("Hose evolutions", "narrative never enters the projection");

			_store.Outbox.Should().ContainSingle(o => o.TriggerEventType == (int)WorkflowTriggerEventType.RecordCreated && o.Sequence == 1);
			_published.Should().ContainSingle(e => e.EventName == "RecordCreated");
			_store.Audits.Should().ContainSingle(a => a.Action == (int)RmsAccessAuditAction.Change && a.Purpose == "Create draft");
			_store.Commits.Should().Be(1);
		}

		[Test]
		public async Task Create_draft_refuses_when_records_is_not_usable_or_the_definition_is_unknown()
		{
			_cutover.Setup(c => c.GetModuleStateAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new RecordsModuleState { DepartmentId = Dept, FlagEnabled = true, Activated = false });
			await _service.Invoking(s => s.CreateDraftAsync(Dept, "author", TrainingInput())).Should().ThrowAsync<InvalidOperationException>();

			await _service.Invoking(s => s.CreateDraftAsync(Dept, "author", new RecordDraftInput { DefinitionKey = "security-patrol" })).Should().ThrowAsync<ArgumentException>();
			_store.Records.Should().BeEmpty();
			_store.Outbox.Should().BeEmpty();
		}

		[Test]
		public async Task Create_draft_is_idempotent_on_the_scoped_key_and_rejects_foreign_units_and_calls()
		{
			var input = TrainingInput();
			input.IdempotencyKey = "client-abc";
			var first = await _service.CreateDraftAsync(Dept, "author", input);
			var replay = await _service.CreateDraftAsync(Dept, "author", input);

			replay.Record.RmsOperationalRecordId.Should().Be(first.Record.RmsOperationalRecordId);
			_store.Records.Should().HaveCount(1);
			_store.Outbox.Should().HaveCount(1);

			var foreignUnit = TrainingInput();
			foreignUnit.Units = new List<RecordUnitResponseInput> { new RecordUnitResponseInput { UnitId = 99 } };
			await _service.Invoking(s => s.CreateDraftAsync(Dept, "author", foreignUnit)).Should().ThrowAsync<ArgumentException>();

			var foreignCall = TrainingInput();
			foreignCall.CallId = 12345;
			await _service.Invoking(s => s.CreateDraftAsync(Dept, "author", foreignCall)).Should().ThrowAsync<ArgumentException>();
		}

		[Test]
		public async Task Run_record_snapshots_the_call_with_provenance_and_never_mutates_the_call()
		{
			var input = new RecordDraftInput { DefinitionKey = RmsDefinitionKeys.Run, CallId = 77, Details = new RmsOperationalRecordDetail { Narrative = "Knocked down in 10", Cause = "Electrical" } };
			var aggregate = await _service.CreateDraftAsync(Dept, "author", input);

			aggregate.Details.CallNumber.Should().Be("C2026-0009");
			aggregate.Details.CallName.Should().Be("Structure fire");
			aggregate.Details.CallAddress.Should().Be("1 Main St");
			aggregate.Details.CallNature.Should().Be("Smoke showing");
			aggregate.Record.DisplaySummary.Should().Be("C2026-0009 Structure fire");
			_calls.Verify(c => c.GetCallByIdAsync(77, It.IsAny<bool>()), Times.Once);
			_calls.VerifyNoOtherCalls();

			(await _service.GetDuplicateCandidatesAsync(Dept, RmsDefinitionKeys.Run, 77)).Should().ContainSingle();
			(await _service.GetDuplicateCandidatesAsync(Dept, RmsDefinitionKeys.Training, 77)).Should().BeEmpty();
		}

		[Test]
		public async Task Save_draft_requires_the_current_row_version_and_emits_no_event()
		{
			var created = await _service.CreateDraftAsync(Dept, "author", TrainingInput());
			var outboxBefore = _store.Outbox.Count;

			var updated = await _service.SaveDraftAsync(Dept, "author", created.Record.RmsOperationalRecordId, created.Record.RowVersion, TrainingInput("Revised narrative"));
			updated.Record.RowVersion.Should().Be(2);
			updated.Details.Narrative.Should().Be("Revised narrative");
			_store.Outbox.Should().HaveCount(outboxBefore, "draft autosaves never create Workflow events");

			var stale = () => _service.SaveDraftAsync(Dept, "author", created.Record.RmsOperationalRecordId, 1, TrainingInput("Lost update"));
			var ex = await stale.Should().ThrowAsync<RecordConcurrencyException>();
			ex.Which.CurrentRowVersion.Should().Be(2);
			_store.Discards.Should().Be(1);
			(await _service.GetAsync(Dept, created.Record.RmsOperationalRecordId)).Details.Narrative.Should().Be("Revised narrative");
		}

		[Test]
		public async Task Outstanding_records_for_a_member_are_the_open_ones_they_still_own()
		{
			var first = await _service.CreateDraftAsync(Dept, "author", TrainingInput());
			var second = await _service.CreateDraftAsync(Dept, "author", TrainingInput("Second"));
			await _service.FinalizeAsync(Dept, "author", first.Record.RmsOperationalRecordId, first.Record.RowVersion, "1", null, null);
			await _service.ReassignDraftAsync(Dept, "chief", second.Record.RmsOperationalRecordId, "successor", "leaving");
			var third = await _service.CreateDraftAsync(Dept, "author", TrainingInput("Third"));

			var outstanding = await _service.GetOutstandingForOwnerAsync(Dept, "author");

			outstanding.Select(r => r.RmsOperationalRecordId).Should().Equal(new[] { third.Record.RmsOperationalRecordId }, "finalized and reassigned records are no longer the member's");
			(await _service.GetOutstandingForOwnerAsync(Dept, "successor")).Select(r => r.RmsOperationalRecordId).Should().Equal(new[] { second.Record.RmsOperationalRecordId });
		}

		// ── Protected Data, inert (plan section 5.9.1) ────────────────────────────────────────────────

		[Test]
		public async Task Protected_data_columns_are_written_inert()
		{
			var created = await _service.CreateDraftAsync(Dept, "author", TrainingInput());
			await _service.FinalizeAsync(Dept, "author", created.Record.RmsOperationalRecordId, created.Record.RowVersion, "1", null, null);

			_store.Details.Should().HaveCount(2, "the draft row and the revision copy");
			foreach (var details in _store.Details)
			{
				details.IsProtected.Should().BeFalse();
				details.ProtectedEnvelope.Should().BeNull();
				details.ProtectedCatalogVersion.Should().Be(0);
				details.ProtectionId.Should().NotBeNullOrWhiteSpace("written at insert, read by nothing until enrollment");
			}
			_store.Revisions.Single().IsProtected.Should().BeFalse();
			_store.Revisions.Single().ProtectedCatalogVersion.Should().Be(0);
		}

		[Test]
		public async Task A_synthetic_envelope_round_trips_through_the_seam_the_revision_copy_and_the_snapshot()
		{
			var created = await _service.CreateDraftAsync(Dept, "author", TrainingInput());
			var draft = _store.Details.Single(d => d.RecordId == created.Record.RmsOperationalRecordId);
			draft.IsProtected = true;
			draft.ProtectedCatalogVersion = 3;
			draft.ProtectedEnvelope = "rgdp:1:7:AAECAwQFBgcICQoLDA0ODw==";

			var finalized = await _service.FinalizeAsync(Dept, "author", created.Record.RmsOperationalRecordId, created.Record.RowVersion, "1", null, null);

			var copy = _store.Details.Single(d => d.RevisionId == finalized.Record.CurrentRevisionId);
			copy.IsProtected.Should().BeTrue();
			copy.ProtectedCatalogVersion.Should().Be(3);
			copy.ProtectedEnvelope.Should().Be("rgdp:1:7:AAECAwQFBgcICQoLDA0ODw==");

			var snapshot = await _service.GetRevisionSnapshotAsync(Dept, finalized.Record.CurrentRevisionId);
			snapshot.Details.ProtectedEnvelope.Should().Be("rgdp:1:7:AAECAwQFBgcICQoLDA0ODw==", "the envelope is part of the immutable revision");
		}

		[Test]
		public void The_value_seam_refuses_an_envelope_that_did_not_come_through_enrollment()
		{
			var stray = new RmsOperationalRecordDetail { IsProtected = false, ProtectedEnvelope = "rgdp:1:1:xx" };
			FluentActions.Invoking(() => Resgrid.Services.Records.RmsRecordValueService.PrepareForStorage(stray)).Should().Throw<InvalidOperationException>();

			var unmarked = new RmsOperationalRecordDetail { IsProtected = true };
			FluentActions.Invoking(() => Resgrid.Services.Records.RmsRecordValueService.PrepareForStorage(unmarked)).Should().Throw<InvalidOperationException>();

			var plain = new RmsOperationalRecordDetail { IsProtected = false, ProtectedCatalogVersion = 9 };
			Resgrid.Services.Records.RmsRecordValueService.PrepareForStorage(plain);
			plain.ProtectedCatalogVersion.Should().Be(0, "an unprotected row always reads catalog version 0");
		}

		[Test]
		public async Task Review_path_events_carry_the_review_block()
		{
			var created = await _service.CreateDraftAsync(Dept, "author", TrainingInput());
			_store.Records.Single(r => r.RmsOperationalRecordId == created.Record.RmsOperationalRecordId).LifecyclePreset = (int)RmsLifecyclePreset.ReviewRequired;
			await _service.SubmitForReviewAsync(Dept, "author", created.Record.RmsOperationalRecordId, created.Record.RowVersion);
			await _service.ReturnForCorrectionAsync(Dept, "chief", created.Record.RmsOperationalRecordId, "incomplete", "Add the roster");

			var submitted = Newtonsoft.Json.Linq.JObject.Parse(_store.Outbox.Single(o => o.EventName == "RecordSubmittedForReview").PayloadJson);
			submitted["review"].Should().NotBeNull();
			submitted["review"]["review_due_on"].Type.Should().Be(Newtonsoft.Json.Linq.JTokenType.Date);
			((int)submitted["review"]["return_count"]).Should().Be(0);

			var returned = Newtonsoft.Json.Linq.JObject.Parse(_store.Outbox.Single(o => o.EventName == "RecordReturnedForCorrection").PayloadJson);
			((string)returned["review"]["reviewer_user_id"]).Should().Be("chief");
			((string)returned["review"]["reason_code"]).Should().Be("incomplete");
			((string)returned["review"]["reason_text"]).Should().Be("Add the roster");
			((int)returned["review"]["return_count"]).Should().Be(1);

			var createdPayload = Newtonsoft.Json.Linq.JObject.Parse(_store.Outbox.Single(o => o.EventName == "RecordCreated").PayloadJson);
			createdPayload["review"].Should().BeNull("only the review-path triggers carry review.*");
		}

		[Test]
		public async Task Quick_entry_finalize_assigns_the_number_writes_a_checksummed_revision_and_emits_finalized_once()
		{
			var created = await _service.CreateDraftAsync(Dept, "author", TrainingInput());

			var finalized = await _service.FinalizeAsync(Dept, "author", created.Record.RmsOperationalRecordId, created.Record.RowVersion, "1", null, null);

			var record = finalized.Record;
			record.State.Should().Be((int)RmsRecordState.Finalized);
			record.RecordNumber.Should().Be("TRN-2026-0001");
			record.FinalizedByUserId.Should().Be("author");
			record.RevisionCount.Should().Be(1);
			record.CurrentRevisionId.Should().NotBeNull();

			var revision = _store.Revisions.Single();
			revision.RevisionNumber.Should().Be(1);
			revision.Transition.Should().Be((int)RmsRevisionTransition.Finalized);
			revision.PriorRevisionId.Should().BeNull();
			revision.DefinitionKey.Should().Be(RmsDefinitionKeys.Training);
			revision.Checksum.Should().Be(RecordSnapshotSerializer.Checksum(revision.SnapshotJson));
			revision.AttestedOn.Should().NotBeNull();
			RecordSnapshotSerializer.Deserialize(revision.SnapshotJson).RecordNumber.Should().Be("TRN-2026-0001");

			// Revision-bound copies exist alongside the untouched draft rows.
			_store.Details.Should().HaveCount(2);
			_store.Details.Should().ContainSingle(d => d.RevisionId == revision.RmsRevisionId);
			_store.Participants.Should().ContainSingle(p => p.RevisionId == revision.RmsRevisionId);
			_store.Units.Should().ContainSingle(u => u.RevisionId == revision.RmsRevisionId);

			_store.Outbox.Select(o => (WorkflowTriggerEventType)o.TriggerEventType).Should().Equal(WorkflowTriggerEventType.RecordCreated, WorkflowTriggerEventType.RecordFinalized, WorkflowTriggerEventType.LogAdded);
			_store.Outbox.Select(o => o.Sequence).Should().Equal(1, 2, 3);
			_published.Select(p => p.EventName).Should().Equal("RecordCreated", "RecordFinalized", "LogAdded");

			// The legacy LogAdded compatibility row carries the exact log.* contract and is caused by RecordFinalized.
			var compatibility = _store.Outbox.Single(o => o.TriggerEventType == (int)WorkflowTriggerEventType.LogAdded);
			compatibility.CausationId.Should().Be(_store.Outbox.Single(o => o.TriggerEventType == (int)WorkflowTriggerEventType.RecordFinalized).EventId);
			var legacyLog = Newtonsoft.Json.JsonConvert.DeserializeObject<Resgrid.Model.Events.LogAddedEvent>(compatibility.PayloadJson).Log;
			legacyLog.LogType.Should().Be((int)LogTypes.Training);
			legacyLog.Narrative.Should().Be("Hose evolutions");
			legacyLog.LoggedByUserId.Should().Be("author");
			legacyLog.LogId.Should().Be(0, "no legacy Logs row is ever written for compatibility");
			_store.Projections.Single().State.Should().Be((int)RmsRecordState.Finalized);
			_store.Projections.Single().RecordNumber.Should().Be("TRN-2026-0001");

			// The second training record of the year takes the next sequence.
			var second = await _service.CreateDraftAsync(Dept, "author", TrainingInput());
			(await _service.FinalizeAsync(Dept, "author", second.Record.RmsOperationalRecordId, second.Record.RowVersion, "1", null, null)).Record.RecordNumber.Should().Be("TRN-2026-0002");
		}

		[Test]
		public async Task Return_for_correction_enqueues_the_author_notification_after_commit()
		{
			var created = await _service.CreateDraftAsync(Dept, "author", TrainingInput());
			// Locked Logs-parity definitions keep Quick Entry (plan section 4.9); a Review Required definition arrives with
			// RMS-1B, so the stored preset is flipped directly to exercise the review path the service already implements.
			_store.Records.Single(r => r.RmsOperationalRecordId == created.Record.RmsOperationalRecordId).LifecyclePreset = (int)RmsLifecyclePreset.ReviewRequired;
			var submitted = await _service.SubmitForReviewAsync(Dept, "author", created.Record.RmsOperationalRecordId, created.Record.RowVersion);
			submitted.Record.State.Should().Be((int)RmsRecordState.ReadyForReview);
			_notifications.Should().BeEmpty("submission does not notify the author");

			var returned = await _service.ReturnForCorrectionAsync(Dept, "chief", created.Record.RmsOperationalRecordId, "incomplete", "Add the roster");

			returned.Record.State.Should().Be((int)RmsRecordState.Returned);
			returned.Record.ReturnReasonCode.Should().Be("incomplete");
			_notifications.Should().ContainSingle(n => n.Type == (int)Resgrid.Model.Events.EventTypes.RecordReturnedForCorrection
				&& n.Value == created.Record.RmsOperationalRecordId && n.UserId == "author" && n.DepartmentId == Dept);
			_published.Select(p => p.EventName).Should().Equal("RecordCreated", "RecordSubmittedForReview", "RecordReturnedForCorrection");
		}

		[Test]
		public async Task Finalize_requires_a_narrative_and_a_fresh_row_version()
		{
			var created = await _service.CreateDraftAsync(Dept, "author", TrainingInput(narrative: ""));
			await _service.Invoking(s => s.FinalizeAsync(Dept, "author", created.Record.RmsOperationalRecordId, created.Record.RowVersion, "1", null, null)).Should().ThrowAsync<ArgumentException>();
			_store.Revisions.Should().BeEmpty();
			_store.Records.Single().State.Should().Be((int)RmsRecordState.Draft);

			var ok = await _service.CreateDraftAsync(Dept, "author", TrainingInput());
			await _service.Invoking(s => s.FinalizeAsync(Dept, "author", ok.Record.RmsOperationalRecordId, 99, "1", null, null)).Should().ThrowAsync<RecordConcurrencyException>();
		}

		[Test]
		public async Task Submit_for_review_is_not_a_quick_entry_transition()
		{
			var created = await _service.CreateDraftAsync(Dept, "author", TrainingInput());
			await _service.Invoking(s => s.SubmitForReviewAsync(Dept, "author", created.Record.RmsOperationalRecordId, created.Record.RowVersion)).Should().ThrowAsync<RecordTransitionException>();
		}

		[Test]
		public async Task Amendment_keeps_the_finalized_revision_authoritative_until_it_finalizes_as_revision_two()
		{
			var created = await _service.CreateDraftAsync(Dept, "author", TrainingInput());
			var finalized = await _service.FinalizeAsync(Dept, "author", created.Record.RmsOperationalRecordId, created.Record.RowVersion, "1", null, null);
			var id = finalized.Record.RmsOperationalRecordId;
			var revisionOne = finalized.Record.CurrentRevisionId;

			var opened = await _service.OpenAmendmentAsync(Dept, "chief", id);
			opened.Record.State.Should().Be((int)RmsRecordState.Finalized, "the state does not change while an amendment draft is open");
			opened.Record.AmendsRevisionId.Should().Be(revisionOne);
			opened.Record.CurrentRevisionId.Should().Be(revisionOne);
			await _service.Invoking(s => s.OpenAmendmentAsync(Dept, "chief", id)).Should().ThrowAsync<RecordTransitionException>("at most one open amendment per Record");

			var edited = await _service.SaveDraftAsync(Dept, "chief", id, opened.Record.RowVersion, TrainingInput("Corrected narrative"));
			edited.Record.State.Should().Be((int)RmsRecordState.Finalized);
			RecordSnapshotSerializer.Deserialize(_store.Revisions.Single().SnapshotJson).Details.Narrative.Should().Be("Hose evolutions", "revision 1 is untouched");

			await _service.Invoking(s => s.FinalizeAsync(Dept, "chief", id, edited.Record.RowVersion, "1", null, null)).Should().ThrowAsync<ArgumentException>("a reason code is required at finalize");

			var amended = await _service.FinalizeAsync(Dept, "chief", id, edited.Record.RowVersion, "1", "correction", "Wrong evolution listed");
			amended.Record.State.Should().Be((int)RmsRecordState.Amended);
			amended.Record.AmendsRevisionId.Should().BeNull();
			amended.Record.RevisionCount.Should().Be(2);
			amended.Record.RecordNumber.Should().Be("TRN-2026-0001", "amendments never consume a new number");

			var two = _store.Revisions.Single(r => r.RevisionNumber == 2);
			two.PriorRevisionId.Should().Be(revisionOne);
			two.Transition.Should().Be((int)RmsRevisionTransition.Amended);
			two.ReasonCode.Should().Be("correction");
			_store.Revisions.Single(r => r.RevisionNumber == 1).Checksum.Should().NotBe(two.Checksum);

			_published.Select(p => p.EventName).Should().Equal("RecordCreated", "RecordFinalized", "LogAdded", "RecordAmended");
			var diff = await _service.DiffRevisionsAsync(Dept, revisionOne, two.RmsRevisionId, canViewRestricted: true);
			diff.Should().ContainSingle(d => d.FieldKey == "Narrative" && d.NewValue == "Corrected narrative");
		}

		[Test]
		public async Task Abandoning_an_amendment_restores_the_draft_rows_from_the_current_revision()
		{
			var created = await _service.CreateDraftAsync(Dept, "author", TrainingInput());
			var finalized = await _service.FinalizeAsync(Dept, "author", created.Record.RmsOperationalRecordId, created.Record.RowVersion, "1", null, null);
			var id = finalized.Record.RmsOperationalRecordId;

			var opened = await _service.OpenAmendmentAsync(Dept, "chief", id);
			await _service.SaveDraftAsync(Dept, "chief", id, opened.Record.RowVersion, TrainingInput("Half-done edit"));

			var restored = await _service.AbandonAmendmentAsync(Dept, "chief", id);
			restored.Record.AmendsRevisionId.Should().BeNull();
			restored.Details.Narrative.Should().Be("Hose evolutions");
			restored.Record.RevisionCount.Should().Be(1);
			_store.Revisions.Should().HaveCount(1);
			_published.Select(p => p.EventName).Should().Equal("RecordCreated", "RecordFinalized", "LogAdded");
		}

		[Test]
		public async Task Void_requires_a_reason_retains_history_and_is_terminal()
		{
			var created = await _service.CreateDraftAsync(Dept, "author", TrainingInput());
			var finalized = await _service.FinalizeAsync(Dept, "author", created.Record.RmsOperationalRecordId, created.Record.RowVersion, "1", null, null);
			var id = finalized.Record.RmsOperationalRecordId;

			await _service.Invoking(s => s.VoidAsync(Dept, "chief", id, null, null)).Should().ThrowAsync<ArgumentException>();

			var voided = await _service.VoidAsync(Dept, "chief", id, "duplicate", "Filed twice");
			voided.Record.State.Should().Be((int)RmsRecordState.Voided);
			voided.Record.VoidReasonCode.Should().Be("duplicate");
			voided.Record.RecordNumber.Should().Be("TRN-2026-0001", "a number is never reused or removed");
			_store.Revisions.Should().HaveCount(2);
			_store.Revisions.Single(r => r.RevisionNumber == 2).Transition.Should().Be((int)RmsRevisionTransition.Voided);
			_published.Last().EventName.Should().Be("RecordVoided");

			await _service.Invoking(s => s.OpenAmendmentAsync(Dept, "chief", id)).Should().ThrowAsync<RecordTransitionException>("Voided is terminal");
			await _service.Invoking(s => s.SaveDraftAsync(Dept, "chief", id, voided.Record.RowVersion, TrainingInput())).Should().ThrowAsync<RecordTransitionException>();
		}

		[Test]
		public async Task Cancel_abandons_a_draft_without_a_revision_or_a_number()
		{
			var created = await _service.CreateDraftAsync(Dept, "author", TrainingInput());

			var cancelled = await _service.CancelAsync(Dept, "author", created.Record.RmsOperationalRecordId);

			cancelled.Record.State.Should().Be((int)RmsRecordState.Cancelled);
			cancelled.Record.RecordNumber.Should().BeNull();
			_store.Revisions.Should().BeEmpty();
			_published.Last().EventName.Should().Be("RecordCancelled");
			_store.Outbox.Last().PayloadJson.Should().Contain("\"number_disposition\":\"none\"");
			await _service.Invoking(s => s.FinalizeAsync(Dept, "author", created.Record.RmsOperationalRecordId, cancelled.Record.RowVersion, "1", null, null)).Should().ThrowAsync<RecordTransitionException>();
		}

		[Test]
		public async Task Unit_activity_requires_a_unit_and_an_activity_time()
		{
			var missingUnit = new RecordDraftInput { DefinitionKey = RmsDefinitionKeys.UnitActivity, Details = new RmsOperationalRecordDetail { Narrative = "Checked hose" } };
			await _service.Invoking(s => s.CreateDraftAsync(Dept, "author", missingUnit)).Should().ThrowAsync<ArgumentException>();

			var input = new RecordDraftInput { DefinitionKey = RmsDefinitionKeys.UnitActivity, Details = new RmsOperationalRecordDetail { Narrative = "Checked hose", UnitId = 5 }, Units = new List<RecordUnitResponseInput> { new RecordUnitResponseInput { UnitId = 5 } } };
			var created = await _service.CreateDraftAsync(Dept, "author", input);
			created.Record.DisplaySummary.Should().Be("Engine 5");
			await _service.Invoking(s => s.FinalizeAsync(Dept, "author", created.Record.RmsOperationalRecordId, created.Record.RowVersion, "1", null, null)).Should().ThrowAsync<ArgumentException>("ActivityOn is required");

			input.Details.ActivityOn = DateTime.UtcNow;
			var saved = await _service.SaveDraftAsync(Dept, "author", created.Record.RmsOperationalRecordId, created.Record.RowVersion, input);
			var finalized = await _service.FinalizeAsync(Dept, "author", created.Record.RmsOperationalRecordId, saved.Record.RowVersion, "1", null, null);
			finalized.Record.RecordNumber.Should().StartWith("UNT-");
			_store.Outbox.Should().NotContain(o => o.TriggerEventType == (int)WorkflowTriggerEventType.LogAdded, "Unit Activity never emitted LogAdded and must not start now");
		}

		[Test]
		public async Task Attachments_are_checksummed_and_never_added_to_a_terminal_record()
		{
			var created = await _service.CreateDraftAsync(Dept, "author", TrainingInput());
			var bytes = new byte[] { 1, 2, 3, 4 };

			var attachment = await _service.AddAttachmentAsync(Dept, "author", created.Record.RmsOperationalRecordId, "roster.pdf", "application/pdf", bytes, null);
			attachment.Checksum.Should().Be(RecordSnapshotSerializer.Checksum(bytes));
			attachment.ByteSize.Should().Be(4);
			attachment.Data.Should().BeNull("the returned metadata never carries bytes");
			(await _service.GetAsync(Dept, created.Record.RmsOperationalRecordId)).Attachments.Should().HaveCount(1);

			await _service.CancelAsync(Dept, "author", created.Record.RmsOperationalRecordId);
			await _service.Invoking(s => s.AddAttachmentAsync(Dept, "author", created.Record.RmsOperationalRecordId, "x", "text/plain", bytes, null)).Should().ThrowAsync<RecordTransitionException>();
		}

		[Test]
		public async Task Reassign_changes_the_owner_but_never_the_author()
		{
			var created = await _service.CreateDraftAsync(Dept, "author", TrainingInput());
			var reassigned = await _service.ReassignDraftAsync(Dept, "chief", created.Record.RmsOperationalRecordId, "successor", "left department");

			reassigned.Record.OwnerUserId.Should().Be("successor");
			reassigned.Record.AuthorUserId.Should().Be("author");
			_store.Audits.Should().ContainSingle(a => a.Action == (int)RmsAccessAuditAction.Admin && a.Purpose == "Reassign draft");
		}

		[Test]
		public async Task Failed_transaction_discards_and_leaves_no_outbox_row()
		{
			_store.RevisionsRepo.Setup(r => r.InsertAsync(It.IsAny<RmsRevision>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ThrowsAsync(new InvalidOperationException("db down"));
			var created = await _service.CreateDraftAsync(Dept, "author", TrainingInput());
			var outboxBefore = _store.Outbox.Count;

			await _service.Invoking(s => s.FinalizeAsync(Dept, "author", created.Record.RmsOperationalRecordId, created.Record.RowVersion, "1", null, null)).Should().ThrowAsync<InvalidOperationException>();

			_store.Discards.Should().Be(1);
			_store.Outbox.Should().HaveCount(outboxBefore, "a rolled-back transition publishes nothing");
			_published.Should().HaveCount(1);
		}
	}
}
