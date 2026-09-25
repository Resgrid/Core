using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.AdminAssist;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Certifications;
using Resgrid.Model.Checklists;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Model.WorkOrders;
using Resgrid.Services.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture]
	public class DiagnosticSourceTests
	{
		private static readonly DateTime Now = new(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
		private sealed class Fixture
		{
			public readonly AdminAssistActor Actor = new(7, "admin");
			public readonly Mock<IAdminAssistDiagnosticStore> Store = new();
			public readonly Mock<IAuthorizationService> Visibility = new();
			public readonly Mock<IAdminAssistPermissionEvaluator> Permissions = new();
			public readonly Mock<IDepartmentMembersRepository> Members = new();
			public readonly Mock<IProtectedReadService> ProtectedRead = new();
			public readonly Mock<IChecklistsService> Checklists = new();
			public readonly Mock<IWorkOrdersService> Orders = new();
			public readonly Mock<IRecordsAuthorizationService> Membership = new();
			public readonly Mock<ICertificationService> Qualifications = new();
			public readonly Mock<IShiftsService> Shifts = new();
			public readonly AdminAssistDiagnosticSource Source;
			public readonly string Capability;
			public Fixture()
			{
				Permissions.Setup(p => p.EvaluateCurrentAsync(Actor, "admin", "CanSeePersonnelLocations", "member", It.IsAny<CancellationToken>())).ReturnsAsync(true);
				Permissions.Setup(p => p.EvaluateCurrentAsync(Actor, "member", "CreateCall", null, It.IsAny<CancellationToken>())).ReturnsAsync(false);
				var catalog = new ConfigurationCatalog(); Capability = catalog.Capabilities[0].Id;
				var access = new Mock<IAdminAssistAccessService>();
				access.Setup(x => x.CanAccessAsync(Actor, false, It.IsAny<CancellationToken>())).ReturnsAsync(true);
				access.Setup(x => x.GetCapabilityAsync(Actor, Capability, It.IsAny<CancellationToken>())).ReturnsAsync(new CapabilityAccess(Capability, EvidenceState.Known, Array.Empty<string>(), true, null, Now, CommercialState: EvidenceState.Known));
				Members.Setup(m => m.GetDepartmentMemberByDepartmentIdAndUserIdAsync(7, "member")).ReturnsAsync(new DepartmentMember { DepartmentId = 7, UserId = "member" });
				Visibility.Setup(v => v.CanUserViewPersonAsync("admin", "member", 7)).ReturnsAsync(true);
				Visibility.Setup(v => v.CanUserViewPersonLocationViaMatrixAsync("member", "admin", 7)).ReturnsAsync(true);
				Visibility.Setup(v => v.CanUserViewCallAsync("admin", 1)).ReturnsAsync(true);
				Visibility.Setup(v => v.CanUserViewUnitAsync("admin", 2)).ReturnsAsync(true);
				Visibility.Setup(v => v.CanUserViewRoleAsync("admin", 3)).ReturnsAsync(true);
				// Coverage authorizes people in bulk; by default everyone asked about passes.
				Visibility.Setup(v => v.GetViewablePersonIdsAsync("admin", It.IsAny<IEnumerable<string>>(), 7)).ReturnsAsync((string _, IEnumerable<string> ids, int _) => ids.ToHashSet());
				Membership.Setup(x => x.GetAssignableMemberIdsAsync(It.IsAny<IEnumerable<string>>(), 7)).ReturnsAsync((IEnumerable<string> ids, int _) => ids.ToHashSet());
				Membership.Setup(x => x.IsAssignableMemberAsync("member", 7)).ReturnsAsync(true);
				Membership.Setup(x => x.IsActiveMemberAsync("member", 7)).ReturnsAsync(true);
				var calls = new Mock<ICallsService>(); calls.Setup(c => c.GetCallByIdAsync(1, true)).ReturnsAsync(new Call { CallId = 1, DepartmentId = 7 });
				var units = new Mock<IUnitsRepository>(); units.Setup(u => u.GetByIdAsync(2)).ReturnsAsync(new Unit { UnitId = 2, DepartmentId = 7 });
				var users = new Mock<IUsersService>(); users.Setup(u => u.ReadLatestLocationsForAdministrationAsync(7)).ReturnsAsync(new List<PersonnelLocation>());
				var actions = new Mock<IActionLogsRepository>(); actions.Setup(a => a.ReadLatestForAdministrationAsync(7, true, Now, 2000, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<ActionLog>());
				var profiles = new Mock<IUserProfileService>(); profiles.Setup(p => p.GetProfileByUserIdAsync("member", true)).ReturnsAsync(new UserProfile { SendPush = false, SendSms = false, SendEmail = false });
				Store.Setup(s => s.ReadDiagnosticTracesAsync(7, 1, It.IsAny<DateTime>(), It.IsAny<DateTime>(), 2000, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<AdminAssistDispatchTraceRow>());
				Store.Setup(s => s.ReadDiagnosticStatusesAsync(7, "member", null, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(new[] { new DiagnosticStatusHeader(Now.AddMinutes(-1), 2, "Unrecorded") });
				var departments = new Mock<IDepartmentsService>(); departments.Setup(d => d.GetDepartmentByIdAsync(7, true)).ReturnsAsync(new Department { DepartmentId = 7, TimeZone = "UTC" });
				var roles = new Mock<IPersonnelRolesService>(); roles.Setup(r => r.GetRolesForDepartmentUnlimitedAsync(7)).ReturnsAsync(new List<PersonnelRole> { new() { DepartmentId = 7, PersonnelRoleId = 3 } });
				Qualifications.Setup(q => q.EvaluateRoleRequirementsAsync(7, 3, Now.Date)).ReturnsAsync(new List<RoleCertificationEvaluation>());
				Shifts.Setup(s => s.ReadSchedulesForAdministrationAsync(7, Now.Date.AddDays(-3), Now.Date.AddDays(1), Now, 2000, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ShiftDaySchedule>());
				Checklists.Setup(c => c.GetComplianceSummaryAsync(It.IsAny<ChecklistActor>(), It.IsAny<ChecklistReportQuery>())).ReturnsAsync(new ChecklistComplianceSummary { Entries = new() { new() { Expected = true, DueUtc = Now.AddHours(-1), Passed = false } } });
				Orders.Setup(o => o.ListAsync(It.IsAny<ChecklistActor>(), It.IsAny<WorkOrderFilter>())).ReturnsAsync(new WorkOrderPage());
				var evidence = new[] { "DepartmentSettings", "EmailImportPolling", "Readiness" }.Select(id =>
				{
					var source = new Mock<IAdminAssistEvidenceSource>(); source.SetupGet(s => s.SourceId).Returns(id);
					source.Setup(s => s.ReadAsync(Actor, Now, It.IsAny<CancellationToken>())).ReturnsAsync(new[] {
						new ConfigurationEvidence("MappingPersonnelLocationTTL", EvidenceState.Known, id, "1", Now, Number: 5),
						new ConfigurationEvidence("MappingPersonnelAllowStatusWithNoLocationToOverwrite", EvidenceState.Known, id, "1", Now, Boolean: true),
						new ConfigurationEvidence("DisabledAutoAvailable", EvidenceState.Known, id, "1", Now, Boolean: true),
						new ConfigurationEvidence("emailImportFailureCount", EvidenceState.Known, id, "1", Now, Number: 2),
						new ConfigurationEvidence("failedWorkflowCount", EvidenceState.Known, id, "1", Now, Number: 1)
					}); return source.Object;
				}).ToArray();
				Source = new(Store.Object, access.Object, catalog, Visibility.Object, Membership.Object, Members.Object, calls.Object, Mock.Of<IUnitsService>(), users.Object, profiles.Object, ProtectedRead.Object, Mock.Of<IProtectedGrantContext>(), evidence, departments.Object, Mock.Of<IDepartmentGroupsService>(), roles.Object, Qualifications.Object, Shifts.Object, Checklists.Object, Orders.Object, Mock.Of<IWorkOrderReportingService>(), actions.Object, units.Object, Permissions.Object);
			}
			public DiagnosticRequest Request(string flow) => new(flow, Now.AddHours(-1), Now,
				CallId: flow == "paging" ? 1 : null, MemberId: flow is "paging" or "map" or "access" or "statuses" ? "member" : null,
				UnitId: flow == "equipment" ? 2 : null, RoleId: flow == "coverage" ? 3 : null,
				Permission: flow == "access" ? "CreateCall" : null, CapabilityId: flow is "access" or "integration" ? Capability : null);
		}
		[TestCase("paging", "PagingPreferences"), TestCase("map", "MapMarker"), TestCase("access", "EffectivePermission"), TestCase("imports", "emailImportFailureCount"), TestCase("coverage", "QualifiedRoster"), TestCase("equipment", "OverdueChecks"), TestCase("integration", "failedWorkflowCount")]
		public async Task Flows_identify_current_possible_causes_without_a_model_or_source_mutation(string flow, string checkId)
		{
			var f = new Fixture(); var result = await f.Source.ReadAsync(f.Actor, f.Request(flow), Now, CancellationToken.None);
			Assert.That(result.Checks.Single(c => c.Id == checkId).Outcome, Is.EqualTo("PossibleCause"));
			Assert.That(result.Checks.Any(c => c.Outcome == "ConfirmedCause"), Is.False);
		}
		private static void Coverage(Fixture f, string[] qualified, params string[] onDuty)
		{
			f.Qualifications.Setup(q => q.EvaluateRoleRequirementsAsync(7, 3, Now.Date)).ReturnsAsync(qualified.Select(id => new RoleCertificationEvaluation { PersonnelRoleId = 3, UserId = id, Qualified = true }).ToList());
			var shift = new Shift { ShiftId = 1, DepartmentId = 7, StartTime = "08:00", EndTime = "20:00" };
			var schedule = new ShiftDaySchedule { Shift = shift, Day = new ShiftDay { ShiftDayId = 1, ShiftId = 1, Shift = shift, Day = Now.Date },
				Roster = onDuty.Select(id => new ShiftDayRosterEntry { UserId = id, DepartmentGroupId = 4 }).ToList() };
			f.Shifts.Setup(s => s.ReadSchedulesForAdministrationAsync(7, Now.Date.AddDays(-3), Now.Date.AddDays(1), Now, 2000, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ShiftDaySchedule> { schedule });
		}
		[Test]
		public async Task Coverage_authorizes_each_person_once_in_bulk_and_never_one_at_a_time()
		{
			var f = new Fixture(); Coverage(f, new[] { "a", "b", "a" }, "a", "c", "c");
			var result = await f.Source.ReadAsync(f.Actor, f.Request("coverage"), Now, CancellationToken.None);
			Assert.That(result.Checks.Single(c => c.Id == "QualifiedRoster").Value, Is.EqualTo(1));
			Assert.That(result.Checks.Single(c => c.Id == "QualifiedRoster").Basis, Is.Not.EqualTo("Restricted"));
			f.Visibility.Verify(v => v.GetViewablePersonIdsAsync("admin", It.Is<IEnumerable<string>>(ids => ids.OrderBy(id => id).SequenceEqual(new[] { "a", "b" })), 7), Times.Once);
			f.Visibility.Verify(v => v.GetViewablePersonIdsAsync("admin", It.Is<IEnumerable<string>>(ids => ids.OrderBy(id => id).SequenceEqual(new[] { "a", "c" })), 7), Times.Once);
			f.Membership.Verify(m => m.GetAssignableMemberIdsAsync(It.Is<IEnumerable<string>>(ids => ids.OrderBy(id => id).SequenceEqual(new[] { "a", "c" })), 7), Times.Once);
			f.Visibility.Verify(v => v.CanUserViewPersonAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
			f.Membership.Verify(m => m.IsAssignableMemberAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
		}
		// A refused person makes the whole coverage read Restricted, with no partial counts (the flow's UnauthorizedAccessException).
		private static void ShouldBeRestricted(DiagnosticSourceResult result)
		{
			Assert.That(result.Checks.Single(c => c.Id == "QualifiedRoster").Basis, Is.EqualTo("Restricted"));
			Assert.That(result.Checks.Select(c => c.Id), Does.Not.Contain("QualificationGaps").And.Not.Contain("RosterOverlap"));
		}
		[Test]
		public async Task Coverage_with_a_hidden_qualified_person_stops_before_reading_schedules()
		{
			var f = new Fixture(); Coverage(f, new[] { "a", "b" }, "a");
			f.Visibility.Setup(v => v.GetViewablePersonIdsAsync("admin", It.IsAny<IEnumerable<string>>(), 7)).ReturnsAsync(new HashSet<string> { "a" });
			ShouldBeRestricted(await f.Source.ReadAsync(f.Actor, f.Request("coverage"), Now, CancellationToken.None));
			f.Shifts.Verify(s => s.ReadSchedulesForAdministrationAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
			f.Membership.Verify(m => m.GetAssignableMemberIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<int>()), Times.Never);
		}
		[TestCase(false, true), TestCase(true, false)]
		public async Task Coverage_with_a_roster_member_who_is_hidden_or_not_assignable_is_refused(bool visible, bool assignable)
		{
			var f = new Fixture(); Coverage(f, new[] { "a" }, "a", "c");
			f.Visibility.Setup(v => v.GetViewablePersonIdsAsync("admin", It.IsAny<IEnumerable<string>>(), 7))
				.ReturnsAsync((string _, IEnumerable<string> ids, int _) => ids.Where(id => visible || id != "c").ToHashSet());
			f.Membership.Setup(m => m.GetAssignableMemberIdsAsync(It.IsAny<IEnumerable<string>>(), 7))
				.ReturnsAsync((IEnumerable<string> ids, int _) => ids.Where(id => assignable || id != "c").ToHashSet());
			ShouldBeRestricted(await f.Source.ReadAsync(f.Actor, f.Request("coverage"), Now, CancellationToken.None));
		}
		[Test]
		public async Task Status_history_has_recorded_codes_without_inventing_transition_writers()
		{
			var f = new Fixture(); var result = await f.Source.ReadAsync(f.Actor, f.Request("statuses"), Now, CancellationToken.None);
			Assert.That(result.StatusHistory.Single().Status, Is.EqualTo(2));
			Assert.That(result.Checks.Single(c => c.Id == "StatusWriterUnknown").Outcome, Is.EqualTo("InsufficientEvidence"));
		}
		[Test]
		public async Task Missing_historical_trace_never_replays_current_preferences_as_past_failure()
		{
			var f = new Fixture(); var result = await f.Source.ReadAsync(f.Actor, f.Request("paging"), Now, CancellationToken.None);
			Assert.That(result.Attempts, Is.Empty);
			Assert.That(result.Checks.Single(c => c.Id == "HistoricalTrace").Outcome, Is.EqualTo("InsufficientEvidence"));
			Assert.That(result.Checks.Single(c => c.Id == "DeliveryUnknown").Outcome, Is.EqualTo("InsufficientEvidence"));
		}
		[Test]
		public void Location_permission_denial_prevents_reading_location_metadata()
		{
			var f = new Fixture(); f.Permissions.Setup(p => p.EvaluateCurrentAsync(f.Actor, "admin", "CanSeePersonnelLocations", "member", It.IsAny<CancellationToken>())).ReturnsAsync(false);
			Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Source.ReadAsync(f.Actor, f.Request("map"), Now, CancellationToken.None));
			f.Store.VerifyNoOtherCalls();
		}
		[Test]
		public async Task Hidden_trace_recipient_prevents_even_broadcast_coverage_counts()
		{
			var f = new Fixture();
			var observation = new DispatchTraceObservation(Guid.NewGuid().ToString("D"), 7, 1, null, Guid.NewGuid().ToString("D"), Now.AddMinutes(-1), DispatchTraceStage.Selected, DispatchTraceChannel.Routing, DispatchTraceReason.None, "hidden-person", null, null, Now, 1, 0, DispatchRecipientResolver.Version);
			var row = new AdminAssistDispatchTraceRow { AdminAssistDispatchTraceId = observation.Id, DepartmentId = 7, CallId = 1, AttemptId = observation.AttemptId, Stage = observation.Stage.ToString(), ResolverVersion = observation.ResolverVersion, Content = JsonSerializer.Serialize(observation) };
			f.Store.Setup(s => s.ReadDiagnosticTracesAsync(7, 1, It.IsAny<DateTime>(), It.IsAny<DateTime>(), 2000, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { row });
			var result = await f.Source.ReadAsync(f.Actor, f.Request("paging"), Now, CancellationToken.None);
			Assert.That(result.Attempts, Is.Empty); Assert.That(result.Checks.Single(c => c.Id == "HistoricalTrace").Basis, Is.EqualTo("Restricted"));
			Assert.That(JsonSerializer.Serialize(result), Does.Not.Contain("hidden-person"));
		}
		[Test]
		public async Task Unavailable_paid_maintenance_does_not_hide_authorized_free_checklist_evidence()
		{
			var f = new Fixture();
			f.Orders.Setup(o => o.ListAsync(It.IsAny<ChecklistActor>(), It.IsAny<WorkOrderFilter>())).ThrowsAsync(new UnauthorizedAccessException());
			var result = await f.Source.ReadAsync(f.Actor, f.Request("equipment"), Now, CancellationToken.None);
			Assert.That(result.Checks.Single(c => c.Id == "OverdueChecks").Value, Is.EqualTo(1));
			Assert.That(result.Checks.Single(c => c.Id == "MaintenanceReadiness").Basis, Is.EqualTo("Restricted"));
		}
		[Test]
		public async Task Redacted_equipment_evidence_does_not_become_zero_overdue_checks()
		{
			var f = new Fixture(); f.Checklists.Setup(c => c.GetComplianceSummaryAsync(It.IsAny<ChecklistActor>(), It.IsAny<ChecklistReportQuery>())).ReturnsAsync(new ChecklistComplianceSummary { IsRedacted = true });
			var result = await f.Source.ReadAsync(f.Actor, f.Request("equipment"), Now, CancellationToken.None);
			Assert.That(result.Checks.Single(c => c.Id == "EquipmentReadiness").Basis, Is.EqualTo("Restricted"));
		}
	}
}
