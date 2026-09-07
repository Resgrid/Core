using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// RecordOperationalSummaryV1 (RMS plan sections 5.1 and 4.7): built only from official revisions, carries
	/// identity/correlation/dates/participation and nothing narrative or restricted, pins the revision checksum,
	/// reports correction status across an amendment and a void, pages the change feed with a stable cursor,
	/// and refuses a revision whose checksum no longer matches its content.
	/// </summary>
	[TestFixture]
	public class RecordOperationalSummaryServiceTests
	{
		private const int Dept = 9;
		private FakeRmsStore _store;
		private FakeIncidentStore _incidentStore;
		private Mock<IRecordsAuthorizationService> _authorization;
		private RecordsService _records;
		private RecordOperationalSummaryService _service;

		[SetUp]
		public void SetUp()
		{
			Resgrid.Config.SystemBehaviorConfig.CacheEnabled = false;
			_incidentStore = new FakeIncidentStore();
			_store = _incidentStore.Shared;
			_authorization = new Mock<IRecordsAuthorizationService>();
			_authorization.Setup(a => a.IsActiveMemberAsync(It.IsAny<string>(), Dept)).ReturnsAsync(true);
			_authorization.Setup(a => a.HasPermissionAsync(It.IsAny<string>(), Dept, It.IsAny<PermissionTypes>())).ReturnsAsync(true);
			_authorization.Setup(a => a.CanUserViewRecordAsync(It.IsAny<string>(), It.IsAny<string>(), Dept)).ReturnsAsync(true);
			_authorization.Setup(a => a.CanReadSourceCallAsync(It.IsAny<string>(), Dept, It.IsAny<Call>())).ReturnsAsync(true);

			var cutover = new Mock<IRecordsCutoverService>();
			cutover.Setup(c => c.GetModuleStateAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new RecordsModuleState { DepartmentId = Dept, FlagEnabled = true, Activated = true, CutoverState = RmsDepartmentCutoverState.Active, LegacyWritesBlocked = true });
			var settings = new Mock<IDepartmentSettingsService>();
			settings.Setup(s => s.GetRecordsNumberingConfigAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new RecordsNumberingConfig());
			settings.Setup(s => s.GetRecordsReviewDueHoursAsync(Dept, It.IsAny<bool>())).ReturnsAsync(72);
			var groups = new Mock<IDepartmentGroupsService>();
			groups.Setup(g => g.GetGroupForUserAsync(It.IsAny<string>(), Dept)).ReturnsAsync(new DepartmentGroup { DepartmentGroupId = 11, Name = "Station 1" });
			var profiles = new Mock<IUserProfileService>();
			profiles.Setup(p => p.GetProfileByUserIdAsync(It.IsAny<string>(), It.IsAny<bool>())).ReturnsAsync((string id, bool b) => new UserProfile { UserId = id, FirstName = "First", LastName = id });
			var units = new Mock<IUnitsService>();
			units.Setup(u => u.GetUnitByIdAsync(5)).ReturnsAsync(new Unit { UnitId = 5, DepartmentId = Dept, Name = "Engine 5", Type = "Engine", StationGroupId = 13 });
			var calls = new Mock<ICallsService>();
			calls.Setup(c => c.GetCallByIdAsync(77, It.IsAny<bool>())).ReturnsAsync(new Call { CallId = 77, DepartmentId = Dept, Number = "C2026-0009", Name = "Structure fire", Type = "Fire", LoggedOn = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc), Address = "1 Main St" });
			var adp = new Mock<IDepartmentDataProtectionService>();
			adp.Setup(a => a.GetPinnedCatalogVersionAsync(Dept)).ReturnsAsync(0);
			var outbox = new DomainEventOutboxService(_store.OutboxRepo.Object, Mock.Of<IEventAggregator>());
			var queue = new Mock<IOutboundQueueProvider>();
			queue.Setup(q => q.EnqueueNotification(It.IsAny<Resgrid.Model.Queue.NotificationItem>())).ReturnsAsync(true);
			var evidence = new Mock<IRecordsEvidenceService>();
			evidence.Setup(e => e.BindToRevisionAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);

			_records = new RecordsService(_store.RecordsRepo.Object, new RmsRecordValueService(_store.DetailsRepo.Object), _store.ParticipantsRepo.Object, _store.UnitsRepo.Object,
				_store.AttachmentsRepo.Object, _store.RevisionsRepo.Object, evidence.Object, _store.ScopesRepo.Object, _store.SharesRepo.Object, _store.ProjectionsRepo.Object,
				_store.AuditsRepo.Object, outbox, cutover.Object, settings.Object, groups.Object, profiles.Object, units.Object, calls.Object, adp.Object,
				_store.UnitOfWork.Object, queue.Object, new NullRecordAttachmentScanner(), _authorization.Object, Mock.Of<IRecordsUdfService>(), new PassthroughRecordsProtection(), Mock.Of<IRecordDefinitionsService>(), Mock.Of<IRecordTypedValuesService>(), Mock.Of<IPersonnelRolesService>());

			_store.ProjectionsRepo.Setup(r => r.GetModifiedSinceAsync(Dept, It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<string>()))
				.ReturnsAsync((int d, DateTime? since, int take, string sinceId) => _store.Projections
					.Where(p => p.DepartmentId == d && (!since.HasValue || p.ModifiedOn > since.Value || p.ModifiedOn == since.Value && string.CompareOrdinal(p.RmsRecordSearchProjectionId, sinceId ?? string.Empty) > 0))
					.OrderBy(p => p.ModifiedOn).ThenBy(p => p.RmsRecordSearchProjectionId, StringComparer.Ordinal).Take(take).ToList());

			_service = new RecordOperationalSummaryService(_store.RecordsRepo.Object, _incidentStore.ReportsRepo.Object, _store.RevisionsRepo.Object, _store.ProjectionsRepo.Object, _authorization.Object, new PassthroughRecordsProtection());
		}

		private static RecordDraftInput RunInput(string narrative = "Engine 5 responded to a reported structure fire.")
		{
			return new RecordDraftInput
			{
				DefinitionKey = RmsDefinitionKeys.Run,
				CallId = 77,
				StartedOn = new DateTime(2026, 9, 1, 8, 5, 0, DateTimeKind.Utc),
				EndedOn = new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc),
				Details = new RmsOperationalRecordDetail { Narrative = narrative, Cause = "Cooking", ContactName = "Private Person", ContactNumber = "555-0100" },
				Participants = new List<RecordParticipantInput> { new RecordParticipantInput { UserId = "p2", UnitId = 5, Role = "Firefighter" } },
				Units = new List<RecordUnitResponseInput> { new RecordUnitResponseInput { UnitId = 5, Dispatched = new DateTime(2026, 9, 1, 8, 1, 0, DateTimeKind.Utc), OnScene = new DateTime(2026, 9, 1, 8, 9, 0, DateTimeKind.Utc) } }
			};
		}

		private async Task<RecordAggregate> FinalizedRunAsync()
		{
			var draft = await _records.CreateDraftAsync(Dept, "author", RunInput());
			return await _records.FinalizeAsync(Dept, "author", draft.Record.RmsOperationalRecordId, draft.Record.RowVersion, "1", null, null);
		}

		[Test]
		public async Task A_draft_has_no_summary_and_a_finalized_record_pins_its_revision()
		{
			var draft = await _records.CreateDraftAsync(Dept, "author", RunInput());
			(await _service.GetAsync(Dept, "viewer", draft.Record.RmsOperationalRecordId, RmsRecordKind.Operational)).Should().BeNull("a working draft is never a downstream fact");

			var finalized = await _records.FinalizeAsync(Dept, "author", draft.Record.RmsOperationalRecordId, draft.Record.RowVersion, "1", null, null);
			var summary = await _service.GetAsync(Dept, "viewer", finalized.Record.RmsOperationalRecordId, RmsRecordKind.Operational);

			summary.ContractVersion.Should().Be(1);
			summary.RecordKind.Should().Be(RmsRecordKind.Operational);
			summary.RecordNumber.Should().StartWith("RUN");
			summary.RevisionId.Should().Be(finalized.Record.CurrentRevisionId);
			summary.RevisionNumber.Should().Be(1);
			summary.RevisionChecksum.Should().Be(_store.Revisions.Single().Checksum);
			summary.CorrectionStatus.Should().Be(RecordOperationalSummaryCorrectionStatus.Current);
			summary.AmendmentOpen.Should().BeFalse();
			summary.State.Should().Be("Finalized");
			summary.CallId.Should().Be(77);
			summary.CallNumber.Should().Be("C2026-0009");
			summary.StartedOn.Should().Be(new DateTime(2026, 9, 1, 8, 5, 0, DateTimeKind.Utc));
			summary.FinalizedOn.Should().Be(finalized.Record.FinalizedOn);
			summary.Units.Should().ContainSingle(u => u.UnitId == 5 && u.UnitName == "Engine 5" && u.OnScene == new DateTime(2026, 9, 1, 8, 9, 0, DateTimeKind.Utc));
			summary.Participants.Should().ContainSingle(p => p.UserId == "p2" && p.UnitId == 5 && p.Role == "Firefighter" && p.DisplayName == "First p2");
		}

		[Test]
		public async Task The_contract_carries_no_narrative_restricted_or_contact_detail()
		{
			var finalized = await FinalizedRunAsync();
			var summary = await _service.BuildAsync(Dept, finalized.Record.RmsOperationalRecordId, RmsRecordKind.Operational);

			var json = JsonConvert.SerializeObject(summary);
			json.Should().NotContain("reported structure fire").And.NotContain("Cooking").And.NotContain("Private Person").And.NotContain("555-0100");
			typeof(RecordOperationalSummaryV1).GetProperties().Select(p => p.Name).Should().NotContain(new[] { "Narrative", "Details", "Cause", "ContactName", "ContactNumber", "CaseNumber", "BodyLocation" });
		}

		[Test]
		public async Task An_amendment_supersedes_the_pinned_revision_and_a_void_withdraws_it()
		{
			var finalized = await FinalizedRunAsync();
			var id = finalized.Record.RmsOperationalRecordId;
			var first = finalized.Record.CurrentRevisionId;

			var amendment = await _records.OpenAmendmentAsync(Dept, "author", id);
			(await _service.BuildAsync(Dept, id, RmsRecordKind.Operational)).AmendmentOpen.Should().BeTrue();
			(await _service.BuildAsync(Dept, id, RmsRecordKind.Operational)).RevisionId.Should().Be(first, "an open amendment draft is not yet a fact");

			var input = RunInput("Corrected narrative");
			var saved = await _records.SaveDraftAsync(Dept, "author", id, amendment.Record.RowVersion, input);
			var amended = await _records.FinalizeAsync(Dept, "author", id, saved.Record.RowVersion, "1", "correction", "Wrong unit time");

			var pinned = await _service.BuildAsync(Dept, id, RmsRecordKind.Operational, first);
			pinned.CorrectionStatus.Should().Be(RecordOperationalSummaryCorrectionStatus.Superseded);
			pinned.SupersededByRevisionId.Should().Be(amended.Record.CurrentRevisionId);
			pinned.RevisionNumber.Should().Be(1);
			var current = await _service.BuildAsync(Dept, id, RmsRecordKind.Operational);
			current.CorrectionStatus.Should().Be(RecordOperationalSummaryCorrectionStatus.Current);
			current.RevisionNumber.Should().Be(2);
			current.State.Should().Be("Amended");

			await _records.VoidAsync(Dept, "author", id, "duplicate", "Entered twice");
			var voided = await _service.BuildAsync(Dept, id, RmsRecordKind.Operational, first);
			voided.CorrectionStatus.Should().Be(RecordOperationalSummaryCorrectionStatus.Voided);
			voided.VoidedOn.Should().NotBeNull();
		}

		[Test]
		public async Task A_tampered_revision_is_refused_rather_than_summarized()
		{
			var finalized = await FinalizedRunAsync();
			var revision = _store.Revisions.Single();
			revision.SnapshotJson = revision.SnapshotJson.Replace("Engine 5", "Engine 9");

			Func<Task> build = () => _service.BuildAsync(Dept, finalized.Record.RmsOperationalRecordId, RmsRecordKind.Operational);
			await build.Should().ThrowAsync<InvalidOperationException>().WithMessage("*checksum*");
		}

		[Test]
		public async Task Member_path_requires_membership_and_visibility()
		{
			var finalized = await FinalizedRunAsync();
			var id = finalized.Record.RmsOperationalRecordId;
			_authorization.Setup(a => a.CanUserViewRecordAsync("outsider", id, Dept)).ReturnsAsync(false);
			(await _service.GetAsync(Dept, "outsider", id, RmsRecordKind.Operational)).Should().BeNull();
			_authorization.Setup(a => a.IsActiveMemberAsync("former", Dept)).ReturnsAsync(false);
			(await _service.GetAsync(Dept, "former", id, RmsRecordKind.Operational)).Should().BeNull();
			(await _service.GetAsync(Dept, "viewer", id, RmsRecordKind.Operational)).Should().NotBeNull();
		}

		[Test]
		public async Task Foreign_revisions_and_the_wrong_kind_never_resolve()
		{
			var finalized = await FinalizedRunAsync();
			var id = finalized.Record.RmsOperationalRecordId;
			(await _service.BuildAsync(Dept, id, RmsRecordKind.IncidentReport)).Should().BeNull("the id is an operational record, not an incident report");
			(await _service.BuildAsync(Dept, id, RmsRecordKind.Operational, "not-a-revision")).Should().BeNull();
			(await _service.BuildAsync(Dept + 1, id, RmsRecordKind.Operational)).Should().BeNull();
		}

		[Test]
		public async Task The_change_feed_pages_with_a_stable_cursor_and_skips_drafts()
		{
			var first = await FinalizedRunAsync();
			var second = await FinalizedRunAsync();
			await _records.CreateDraftAsync(Dept, "author", RunInput("Still a draft"));

			var page1 = await _service.QueryAsync(Dept, new RecordOperationalSummaryQuery { Take = 1 });
			page1.Items.Should().ContainSingle().Which.RecordId.Should().Be(first.Record.RmsOperationalRecordId);
			page1.HasMore.Should().BeTrue();
			page1.NextCursor.Should().StartWith("ros1:");

			var page2 = await _service.QueryAsync(Dept, new RecordOperationalSummaryQuery { Take = 1, Cursor = page1.NextCursor });
			page2.Items.Should().ContainSingle().Which.RecordId.Should().Be(second.Record.RmsOperationalRecordId);

			var page3 = await _service.QueryAsync(Dept, new RecordOperationalSummaryQuery { Take = 1, Cursor = page2.NextCursor });
			page3.Items.Should().BeEmpty("the third projection row is a draft with no official revision");
			page3.HasMore.Should().BeFalse();
			page3.NextCursor.Should().BeNull();

			var all = await _service.QueryAsync(Dept, new RecordOperationalSummaryQuery { Take = 50 });
			all.Items.Select(i => i.RecordId).Should().Equal(first.Record.RmsOperationalRecordId, second.Record.RmsOperationalRecordId);

			Func<Task> bad = () => _service.QueryAsync(Dept, new RecordOperationalSummaryQuery { Cursor = "garbage" });
			await bad.Should().ThrowAsync<ArgumentException>();
		}

		[Test]
		public void Cursors_round_trip_and_reject_foreign_shapes()
		{
			var when = new DateTime(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);
			var cursor = RecordOperationalSummaryService.Cursor(when, "rec-1");
			RecordOperationalSummaryService.TryReadCursor(cursor, out var back, out var id).Should().BeTrue();
			back.Should().Be(when);
			id.Should().Be("rec-1");
			RecordOperationalSummaryService.TryReadCursor("rms1:1:AA==", out _, out _).Should().BeFalse("the Records changes cursor is a different feed");
			RecordOperationalSummaryService.TryReadCursor("ros1:notanumber:AA==", out _, out _).Should().BeFalse();
		}

		[Test]
		public async Task An_incident_report_revision_summarizes_from_its_frozen_aggregate()
		{
			var report = new RmsIncidentReport
			{
				RmsIncidentReportId = "rep-1", DepartmentId = Dept, CallId = 77, IncidentNumber = "2026-000123", ReportingEntityId = "FD24027000", RecordNumber = "INC-2026-0001",
				DefinitionKey = RmsDefinitionKeys.NerisIncidentReport, DefinitionVersion = 1, State = (int)RmsRecordState.Finalized, CurrentRevisionId = "rev-1", NerisIncidentId = "neris-123",
				StationGroupId = 11, CallCreatedOn = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc), IncidentClearedOn = new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc), FinalizedOn = DateTime.UtcNow
			};
			_incidentStore.Reports.Add(report);
			var frozen = new IncidentReportAggregate
			{
				Report = JsonConvert.DeserializeObject<RmsIncidentReport>(JsonConvert.SerializeObject(report)),
				Units = new List<RmsUnitResponse> { new RmsUnitResponse { UnitId = 5, UnitNameSnapshot = "Engine 5", DispatchedOn = new DateTime(2026, 9, 1, 8, 1, 0, DateTimeKind.Utc), ClearedOn = new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc) } },
				Narrative = new RmsNarrative { Narrative = "Officer narrative that must not leave" }
			};
			var json = JsonConvert.SerializeObject(frozen);
			_store.Revisions.Add(new RmsRevision { RmsRevisionId = "rev-1", DepartmentId = Dept, RecordId = "rep-1", RecordKind = (int)RmsRecordKind.IncidentReport, RevisionNumber = 1, DefinitionKey = RmsDefinitionKeys.NerisIncidentReport, DefinitionVersion = 1, SnapshotJson = json, Checksum = RecordSnapshotSerializer.Checksum(json), CreatedOn = DateTime.UtcNow });

			var summary = await _service.BuildAsync(Dept, "rep-1", RmsRecordKind.IncidentReport);

			summary.RecordKind.Should().Be(RmsRecordKind.IncidentReport);
			summary.IncidentNumber.Should().Be("2026-000123");
			summary.ReportingEntityId.Should().Be("FD24027000");
			summary.ExternalIncidentId.Should().Be("neris-123");
			summary.StartedOn.Should().Be(report.CallCreatedOn);
			summary.EndedOn.Should().Be(report.IncidentClearedOn);
			summary.Units.Should().ContainSingle(u => u.UnitName == "Engine 5" && u.Released == report.IncidentClearedOn);
			JsonConvert.SerializeObject(summary).Should().NotContain("must not leave");
		}
	}
}
