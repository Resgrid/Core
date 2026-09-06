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
using Resgrid.Model.Services;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// The Records work queues and the NERIS crosswalk gap report (RMS-3d). Two behaviours matter beyond the
	/// arithmetic: one broken count degrades to a warning instead of taking the dashboard down, and a crosswalk
	/// that points at a code the pinned contract no longer carries is reported as stale rather than as mapped.
	/// </summary>
	[TestFixture]
	public class RecordsDashboardServiceTests
	{
		private const int Dept = 31;

		private FakeRmsStore _store;
		private FakeIncidentStore _incidents;
		private Mock<INerisProfileService> _neris;
		private Mock<ICallsService> _calls;
		private List<RmsNerisCrosswalk> _crosswalks;
		private List<CallType> _callTypes;
		private RecordsDashboardService _service;
		private Mock<IRecordsAuthorizationService> _authorization;

		[SetUp]
		public void SetUp()
		{
			_store = new FakeRmsStore();
			_incidents = new FakeIncidentStore();

			_crosswalks = new List<RmsNerisCrosswalk>();
			_callTypes = new List<CallType>();

			_neris = new Mock<INerisProfileService>();
			_neris.SetupGet(n => n.ContractVersion).Returns("1.4.78");
			_neris.Setup(n => n.GetCrosswalksAsync(Dept)).ReturnsAsync(() => _crosswalks);
			_neris.Setup(n => n.GetValueSet("incident_type")).Returns(new NerisValueSet
			{
				SetKey = "incident_type",
				Codes = new List<string> { "FIRE||STRUCTURE_FIRE||RESIDENTIAL", "MEDICAL||ILLNESS||OTHER_ILLNESS" }
			});

			_calls = new Mock<ICallsService>();
			_calls.Setup(c => c.GetCallTypesForDepartmentAsync(Dept)).ReturnsAsync(() => _callTypes);
			_authorization = new Mock<IRecordsAuthorizationService>();
			_authorization.Setup(a => a.IsActiveMemberAsync(It.IsAny<string>(), Dept)).ReturnsAsync(true);
			_authorization.Setup(a => a.GetVisibleGroupIdsAsync(It.IsAny<string>(), Dept)).ReturnsAsync((List<int>)null);
			_authorization.Setup(a => a.HasPermissionAsync(It.IsAny<string>(), Dept, PermissionTypes.ManageRecordDisclosures)).ReturnsAsync(true);

			_service = new RecordsDashboardService(_store.RecordsRepo.Object, _incidents.ReportsRepo.Object,
				_incidents.AnalysesRepo.Object, _store.DueStatesRepo.Object, _store.DisclosureRequestsRepo.Object,
				_neris.Object, _calls.Object, _authorization.Object);
		}

		private void SeedRecord(RmsRecordState state)
		{
			_store.Records.Add(new RmsOperationalRecord
			{
				RmsOperationalRecordId = Guid.NewGuid().ToString(),
				DepartmentId = Dept,
				ProtectionId = Guid.NewGuid().ToString(),
				DefinitionKey = RmsDefinitionKeys.Run,
				State = (int)state,
				AuthorUserId = "author",
				CreatedOn = DateTime.UtcNow,
				ModifiedOn = DateTime.UtcNow,
				RowVersion = 1
			});
		}

		private void SeedReport(RmsRecordState state)
		{
			_incidents.Reports.Add(new RmsIncidentReport
			{
				RmsIncidentReportId = Guid.NewGuid().ToString(),
				DepartmentId = Dept,
				ProtectionId = Guid.NewGuid().ToString(),
				CallId = 1,
				ReportingEntityId = "FD24027000",
				DefinitionKey = RmsDefinitionKeys.NerisIncidentReport,
				State = (int)state,
				AuthorUserId = "author",
				CreatedOn = DateTime.UtcNow,
				ModifiedOn = DateTime.UtcNow,
				RowVersion = 1
			});
		}

		private void SeedDisclosure(RmsDisclosureState state, DateTime? dueOn = null, bool closed = false)
		{
			_store.DisclosureRequests.Add(new RmsDisclosureRequest
			{
				RmsDisclosureRequestId = Guid.NewGuid().ToString(),
				DepartmentId = Dept,
				ProtectionId = Guid.NewGuid().ToString(),
				RequesterName = "A. Reporter",
				ReceivedOn = DateTime.UtcNow.AddDays(-10),
				StatutoryDueOn = dueOn,
				State = (int)state,
				ClosedOn = closed ? DateTime.UtcNow : (DateTime?)null,
				CreatedOn = DateTime.UtcNow.AddDays(-10),
				ModifiedOn = DateTime.UtcNow.AddDays(-10),
				RowVersion = 1
			});
		}

		[Test]
		public async Task The_queues_count_both_aggregates()
		{
			SeedRecord(RmsRecordState.Draft);
			SeedRecord(RmsRecordState.Draft);
			SeedRecord(RmsRecordState.ReadyForReview);
			SeedRecord(RmsRecordState.Returned);

			SeedReport(RmsRecordState.Draft);
			SeedReport(RmsRecordState.Returned);
			SeedReport(RmsRecordState.ReadyForReview);
			SeedReport(RmsRecordState.Rejected);
			SeedReport(RmsRecordState.Accepted);

			var dashboard = await _service.GetAsync(Dept, "chief");

			dashboard.OperationalDrafts.Should().Be(2);
			dashboard.OperationalAwaitingReview.Should().Be(1);
			dashboard.OperationalReturned.Should().Be(1);
			dashboard.IncidentIncomplete.Should().Be(2, "draft and returned are both still incomplete");
			dashboard.IncidentAwaitingReview.Should().Be(1);
			dashboard.IncidentRejected.Should().Be(1);
			dashboard.IncidentAccepted.Should().Be(1);
			dashboard.Warnings.Should().BeEmpty();
		}

		[Test]
		public async Task Overdue_comes_from_the_persisted_due_states()
		{
			SeedRecord(RmsRecordState.Draft);
			_store.Records.Single().RmsOperationalRecordId = "r1";
			_store.DueStates.Add(new RmsRecordDueState { RmsRecordDueStateId = Guid.NewGuid().ToString(), DepartmentId = Dept, RecordId = "r1", Obligation = (int)RmsRecordObligation.Review, LastEmittedState = (int)RmsDueState.Overdue });
			_store.DueStates.Add(new RmsRecordDueState { RmsRecordDueStateId = Guid.NewGuid().ToString(), DepartmentId = Dept, RecordId = "r2", Obligation = (int)RmsRecordObligation.Review, LastEmittedState = (int)RmsDueState.NotDue });

			var dashboard = await _service.GetAsync(Dept, "chief");

			dashboard.Overdue.Should().Be(1);
		}

		[Test]
		public async Task The_statutory_clock_counts_only_open_requests()
		{
			SeedDisclosure(RmsDisclosureState.Received, DateTime.UtcNow.AddDays(-2));
			SeedDisclosure(RmsDisclosureState.InReview, DateTime.UtcNow.AddDays(3));
			// Already released, and it went out late; a closed request is not still ticking.
			SeedDisclosure(RmsDisclosureState.Released, DateTime.UtcNow.AddDays(-5), closed: true);

			var dashboard = await _service.GetAsync(Dept, "chief");

			dashboard.DisclosuresOpen.Should().Be(2);
			dashboard.DisclosuresOverdue.Should().Be(1);
		}

		[Test]
		public async Task A_broken_count_degrades_to_a_warning()
		{
			_store.DueStatesRepo.Setup(r => r.CountVisibleOverdueAsync(It.IsAny<int>(), It.IsAny<List<int>>(), It.IsAny<string>())).ThrowsAsync(new InvalidOperationException("table missing"));

			SeedRecord(RmsRecordState.Draft);
			var dashboard = await _service.GetAsync(Dept, "chief");

			dashboard.OperationalDrafts.Should().Be(1, "the rest of the dashboard still renders");
			dashboard.Overdue.Should().Be(0);
			dashboard.Warnings.Should().ContainSingle().Which.Should().Contain("overdue obligations");
		}

		[Test]
		public async Task Crosswalk_coverage_separates_mapped_unmapped_and_stale()
		{
			_callTypes.Add(new CallType { CallTypeId = 1, DepartmentId = Dept, Type = "Structure Fire" });
			_callTypes.Add(new CallType { CallTypeId = 2, DepartmentId = Dept, Type = "Medical" });
			_callTypes.Add(new CallType { CallTypeId = 3, DepartmentId = Dept, Type = "Service Call" });

			_crosswalks.Add(new RmsNerisCrosswalk { DepartmentId = Dept, SetKey = "incident_type", LocalSource = NerisCrosswalkSources.CallType, LocalCode = "Structure Fire", NerisCode = "FIRE||STRUCTURE_FIRE||RESIDENTIAL" });
			// Points at a code the pinned contract no longer carries: looks configured, fails at submission.
			_crosswalks.Add(new RmsNerisCrosswalk { DepartmentId = Dept, SetKey = "incident_type", LocalSource = NerisCrosswalkSources.CallType, LocalCode = "Medical", NerisCode = "MEDICAL||RETIRED_CODE" });

			var coverage = await _service.GetCrosswalkCoverageAsync(Dept);

			coverage.ContractVersion.Should().Be("1.4.78");
			coverage.TotalLocalCodes.Should().Be(3);
			coverage.MappedCount.Should().Be(2);
			coverage.UnmappedCount.Should().Be(1, "Service Call has no mapping and will need classifying by hand");
			coverage.StaleMappingCount.Should().Be(1, "a mapping to a retired code is not an all-clear");
			coverage.Items.Single(i => i.LocalCode == "Service Call").Mapped.Should().BeFalse();
		}

		[Test]
		public async Task A_member_sees_only_their_queues_and_no_unauthorized_disclosure_counts()
		{
			SeedRecord(RmsRecordState.Draft);
			SeedRecord(RmsRecordState.Draft);
			_store.Records[0].OwnerUserId = "officer";
			SeedReport(RmsRecordState.Rejected);
			SeedReport(RmsRecordState.Rejected);
			_incidents.Reports[0].OwnerUserId = "officer";
			_incidents.Analyses.Add(new RmsIncidentAnalysis { DepartmentId = Dept, IncidentReportId = _incidents.Reports[0].RmsIncidentReportId, State = (int)RmsIncidentAnalysisState.Finalized });
			_incidents.Analyses.Add(new RmsIncidentAnalysis { DepartmentId = Dept, IncidentReportId = _incidents.Reports[1].RmsIncidentReportId, State = (int)RmsIncidentAnalysisState.Finalized });
			foreach (var record in _store.Records) _store.DueStates.Add(new RmsRecordDueState { DepartmentId = Dept, RecordId = record.RmsOperationalRecordId, LastEmittedState = (int)RmsDueState.Overdue });
			SeedDisclosure(RmsDisclosureState.Received, DateTime.UtcNow.AddDays(-1));
			_authorization.Setup(a => a.GetVisibleGroupIdsAsync("officer", Dept)).ReturnsAsync(new List<int>());
			_authorization.Setup(a => a.HasPermissionAsync("officer", Dept, PermissionTypes.ManageRecordDisclosures)).ReturnsAsync(false);
			var dashboard = await _service.GetAsync(Dept, "officer");
			dashboard.OperationalDrafts.Should().Be(1);
			dashboard.IncidentRejected.Should().Be(1);
			dashboard.AnalysesAwaitingFiling.Should().Be(1);
			dashboard.Overdue.Should().Be(1);
			dashboard.DisclosuresOpen.Should().Be(0);
			dashboard.DisclosuresOverdue.Should().Be(0);
			_store.DisclosureRequestsRepo.Invocations.Should().BeEmpty();
		}

		[Test]
		public async Task Scope_revoked_during_counts_cannot_return_the_former_department_totals()
		{
			SeedRecord(RmsRecordState.Draft);
			_authorization.SetupSequence(a => a.GetVisibleGroupIdsAsync("officer", Dept)).ReturnsAsync((List<int>)null).ReturnsAsync(new List<int>());
			Func<Task> read = () => _service.GetAsync(Dept, "officer");
			await read.Should().ThrowAsync<UnauthorizedAccessException>();
		}

		[Test]
		public async Task Disclosure_permission_revoked_during_counts_removes_only_the_protected_category()
		{
			SeedRecord(RmsRecordState.Draft); SeedDisclosure(RmsDisclosureState.Received, DateTime.UtcNow.AddDays(-1));
			_authorization.SetupSequence(a => a.HasPermissionAsync("officer", Dept, PermissionTypes.ManageRecordDisclosures)).ReturnsAsync(true).ReturnsAsync(false);
			var dashboard = await _service.GetAsync(Dept, "officer");
			dashboard.OperationalDrafts.Should().Be(1); dashboard.DisclosuresOpen.Should().Be(0); dashboard.DisclosuresOverdue.Should().Be(0);
		}

		[Test]
		public async Task A_removed_member_cannot_obtain_dashboard_counts()
		{
			_authorization.Setup(a => a.IsActiveMemberAsync("former-chief", Dept)).ReturnsAsync(false);
			Func<Task> read = () => _service.GetAsync(Dept, "former-chief");
			await read.Should().ThrowAsync<UnauthorizedAccessException>();
			_store.RecordsRepo.Invocations.Should().BeEmpty();
		}

		[Test]
		public async Task Crosswalk_coverage_degrades_when_the_sources_cannot_be_read()
		{
			_neris.Setup(n => n.GetCrosswalksAsync(Dept)).ThrowsAsync(new InvalidOperationException("unreachable"));

			var coverage = await _service.GetCrosswalkCoverageAsync(Dept);

			coverage.Items.Should().BeEmpty();
			coverage.Warnings.Should().ContainSingle();
		}
	}
}
