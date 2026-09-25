using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Resgrid.AdminAssist;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture]
	public class DispatchImpactTests
	{
		private sealed class Fixture
		{
			public readonly DateTime Now = DateTime.UtcNow;
			public readonly Mock<IAdminAssistAccessService> Access = new();
			public readonly Mock<IAdminAssistRepository> Store = new();
			public readonly Mock<ICallsService> Calls = new();
			public readonly Mock<IDepartmentsService> Departments = new();
			public readonly Mock<IDepartmentGroupsRepository> Groups = new();
			public readonly Mock<IUnitsRepository> Units = new();
			public readonly Mock<IUnitStateRoleRepository> Crews = new();
			public readonly Mock<IPersonnelRolesRepository> Roles = new();
			public readonly Mock<IPersonnelRoleUsersRepository> RoleMembers = new();
			public readonly Mock<IDepartmentSettingsRepository> Settings = new();
			public readonly Mock<IShiftsService> Shifts = new();
			public readonly Mock<IAuthorizationService> Visibility = new();
			public readonly Mock<IRecordsAuthorizationService> Authorization = new();
			public readonly Mock<ICallDispatchesRepository> Direct = new();
			public readonly Mock<ICallDispatchGroupRepository> GroupRoutes = new();
			public readonly Mock<ICallDispatchUnitRepository> UnitRoutes = new();
			public readonly Mock<ICallDispatchRoleRepository> RoleRoutes = new();
			public DispatchImpactRequest Request => new("0", 14, Now, true, true, true);
			public DispatchImpactService Service => new(Access.Object, Store.Object, new ConfigurationCatalog(), Calls.Object,
				Departments.Object, Groups.Object, Units.Object, Crews.Object, Roles.Object, RoleMembers.Object, Settings.Object,
				Shifts.Object, Visibility.Object, Authorization.Object, TimeProvider.System, Direct.Object, GroupRoutes.Object, UnitRoutes.Object, RoleRoutes.Object);
			public Fixture()
			{
				Access.Setup(a => a.CanAccessAsync(It.IsAny<AdminAssistActor>(), false, It.IsAny<CancellationToken>())).ReturnsAsync(true);
				Calls.Setup(c => c.GetCallByIdAsync(14, true)).ReturnsAsync(new Call { CallId = 14, DepartmentId = 7, NatureOfCall = "Must not appear" });
				Authorization.Setup(a => a.CanReadSourceCallAsync("admin", 7, It.IsAny<Call>())).ReturnsAsync(true);
				Authorization.Setup(a => a.IsAssignableMemberAsync(It.IsAny<string>(), 7)).ReturnsAsync(true);
				Visibility.Setup(a => a.CanUserViewPersonAsync("admin", It.IsAny<string>(), 7)).ReturnsAsync(true);
				Visibility.Setup(a => a.CanUserViewUnitAsync("admin", 20)).ReturnsAsync(true);
				Settings.Setup(s => s.GetAllByDepartmentIdAsync(7)).ReturnsAsync(Array.Empty<DepartmentSetting>());
				Departments.Setup(d => d.GetDepartmentByIdAsync(7, true)).ReturnsAsync(new Department { DepartmentId = 7, TimeZone = "UTC" });
				Groups.Setup(g => g.GetAllGroupsByDepartmentIdAsync(7)).ReturnsAsync(new[] { new DepartmentGroup { DepartmentGroupId = 10, DepartmentId = 7,
					Members = new List<DepartmentGroupMember> { new() { UserId = "a" }, new() { UserId = "b" } } } });
				Shifts.Setup(s => s.ReadSchedulesForAdministrationAsync(7, It.IsAny<DateTime>(), It.IsAny<DateTime>(), Now, It.IsAny<int>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync(new List<ShiftDaySchedule> { new() { IsActive = true, Roster = new List<ShiftDayRosterEntry> {
						new() { UserId = "c", DepartmentGroupId = 10, Source = ShiftRosterSources.Trade },
						new() { UserId = "pending", DepartmentGroupId = 10, ApprovalPending = true } } } });
				Units.Setup(u => u.GetByIdAsync(20)).ReturnsAsync(new Unit { UnitId = 20, DepartmentId = 7, StationGroupId = 10 });
				Crews.Setup(c => c.GetCurrentRolesForUnitAsync(20)).ReturnsAsync(new[] { new UnitStateRole { UserId = "b" }, new UnitStateRole { UserId = "d" } });
				Roles.Setup(r => r.GetRoleByRoleIdAsync(30)).ReturnsAsync(new PersonnelRole { PersonnelRoleId = 30, DepartmentId = 7 });
				RoleMembers.Setup(r => r.GetAllMembersOfRoleAsync(30)).ReturnsAsync(new[] { new PersonnelRoleUser { UserId = "e" }, new PersonnelRoleUser { UserId = "d" } });
				Direct.Setup(r => r.GetCallDispatchesByCallIdAsync(14)).ReturnsAsync(new[] { new CallDispatch { UserId = "a" }, new CallDispatch { UserId = "a" } });
				GroupRoutes.Setup(r => r.GetAllCallDispatchGroupByCallIdAsync(14)).ReturnsAsync(new[] { new CallDispatchGroup { DepartmentGroupId = 10 } });
				UnitRoutes.Setup(r => r.GetCallUnitDispatchesByCallIdAsync(14)).ReturnsAsync(new[] { new CallDispatchUnit { UnitId = 20 } });
				RoleRoutes.Setup(r => r.GetCallRoleDispatchesByCallIdAsync(14)).ReturnsAsync(new[] { new CallDispatchRole { RoleId = 30 } });
			}
		}
		[Test]
		public async Task All_route_families_share_deduplication_and_explicit_roster_time_without_exposing_identities()
		{
			var f = new Fixture(); var result = await f.Service.PreviewAsync(new(7, "admin"), f.Request);
			ConfigurationImpactMetric Metric(string key) => result.Metrics.Single(m => m.LabelKey == "Impact." + key);
			Assert.That(Metric("DispatchPeople").Before, Is.EqualTo(4)); Assert.That(Metric("DispatchPeople").After, Is.EqualTo(5));
			Assert.That(Metric("DispatchAttempts").Before, Is.EqualTo(5)); Assert.That(Metric("DispatchAttempts").After, Is.EqualTo(6));
			Assert.That(Metric("DispatchAdded").After, Is.EqualTo(1)); Assert.That(Metric("DispatchRemoved").After, Is.Zero);
			Assert.That(result.AsOfUtc, Is.EqualTo(f.Now));
			Assert.That(Newtonsoft.Json.JsonConvert.SerializeObject(result), Does.Not.Contain("Must not appear"));
			f.Shifts.Verify(s => s.ReadSchedulesForAdministrationAsync(7, It.IsAny<DateTime>(), It.IsAny<DateTime>(), f.Now, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
		}
		[Test]
		public async Task Empty_shift_falls_back_but_missing_roster_is_unknown()
		{
			var f = new Fixture();
			f.Shifts.Setup(s => s.ReadSchedulesForAdministrationAsync(7, It.IsAny<DateTime>(), It.IsAny<DateTime>(), f.Now, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<ShiftDaySchedule>());
			var result = await f.Service.PreviewAsync(new(7, "admin"), f.Request);
			Assert.That(result.Metrics.Single(m => m.LabelKey == "Impact.DispatchFallback").After, Is.EqualTo(1));
			f.Shifts.Setup(s => s.ReadSchedulesForAdministrationAsync(7, It.IsAny<DateTime>(), It.IsAny<DateTime>(), f.Now, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((List<ShiftDaySchedule>)null);
			result = await f.Service.PreviewAsync(new(7, "admin"), f.Request);
			Assert.That(result.Metrics.Single().State, Is.EqualTo(EvidenceState.Unknown)); Assert.That(result.Metrics.Single().After, Is.Null);
		}
		[Test]
		public void Cross_tenant_call_is_rejected_before_routes_are_read()
		{
			var f = new Fixture(); f.Calls.Setup(c => c.GetCallByIdAsync(14, true)).ReturnsAsync(new Call { CallId = 14, DepartmentId = 8 });
			Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await f.Service.PreviewAsync(new(7, "admin"), f.Request));
			f.Direct.Verify(r => r.GetCallDispatchesByCallIdAsync(It.IsAny<int>()), Times.Never);
		}
		[Test]
		public void Restricted_person_is_not_included_in_a_partial_count()
		{
			var f = new Fixture(); f.Visibility.Setup(v => v.CanUserViewPersonAsync("admin", "c", 7)).ReturnsAsync(false);
			Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await f.Service.PreviewAsync(new(7, "admin"), f.Request));
		}
		[Test]
		public void Current_route_change_during_preview_conflicts_even_without_a_configuration_revision_change()
		{
			var f = new Fixture(); f.Direct.SetupSequence(r => r.GetCallDispatchesByCallIdAsync(14))
				.ReturnsAsync(new[] { new CallDispatch { UserId = "a" } }).ReturnsAsync(new[] { new CallDispatch { UserId = "b" } });
			Assert.ThrowsAsync<AdminAssistConcurrencyException>(async () => await f.Service.PreviewAsync(new(7, "admin"), f.Request));
		}
		[Test]
		public void Missing_current_route_source_and_invalid_time_are_not_silently_accepted()
		{
			var f = new Fixture();
			Assert.ThrowsAsync<ArgumentException>(async () => await f.Service.PreviewAsync(new(7, "admin"), f.Request with { SimulationTimeUtc = DateTime.SpecifyKind(f.Now, DateTimeKind.Unspecified) }));
			Assert.ThrowsAsync<ArgumentException>(async () => await f.Service.PreviewAsync(new(7, "admin"), f.Request with { SimulationTimeUtc = f.Now.AddDays(8) }));
			Assert.ThrowsAsync<AdminAssistConcurrencyException>(async () => await f.Service.PreviewAsync(new(7, "admin"), f.Request with { ExpectedRevision = "1" }));
		}
		[Test]
		public void Shared_group_projection_preserves_ungrouped_members_pending_approvals_and_case_insensitive_deduplication()
		{
			var roster = new[] { new ShiftDayRosterEntry { UserId = "A", DepartmentGroupId = 10 }, new() { UserId = "a" }, new() { UserId = "b" },
				new() { UserId = "c", DepartmentGroupId = 20 }, new() { UserId = "d", DepartmentGroupId = 10, ApprovalPending = true } };
			Assert.That(ShiftRosterGroups.Select(10, roster, new[] { "a", "b", "c", "d" }), Is.EquivalentTo(new[] { "A", "b" }));
		}
	}
}
