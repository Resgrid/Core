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
using Resgrid.Services;
using Resgrid.Services.AdminAssist;

namespace Resgrid.Tests.AdminAssist
{
	[TestFixture]
	public class PermissionImpactTests
	{
		private sealed class Fixture
		{
			public readonly Mock<IAdminAssistAccessService> Access = new();
			public readonly Mock<IAdminAssistRepository> Store = new();
			public readonly Mock<IDepartmentsService> Departments = new();
			public readonly Mock<IDepartmentMembersRepository> Members = new();
			public readonly Mock<IDepartmentGroupsRepository> Groups = new();
			public readonly Mock<IPersonnelRolesRepository> Roles = new();
			public readonly Mock<IPersonnelRoleUsersRepository> Assignments = new();
			public readonly Mock<IUnitsRepository> Units = new();
			public readonly Mock<IPermissionsRepository> Permissions = new();
			public readonly List<DepartmentMember> People = new();
			public readonly List<DepartmentGroup> GroupRows = new();
			public PermissionImpactService Service => new(Access.Object, Store.Object, new ConfigurationCatalog(), Departments.Object, Members.Object,
				Groups.Object, Roles.Object, Assignments.Object, Units.Object, Permissions.Object,
				new PermissionsService(Permissions.Object, Mock.Of<IUsersService>(), Mock.Of<IDepartmentGroupsService>()), TimeProvider.System);
			public PermissionImpactRequest Request => new("0", "ViewGroupUsers", 1, true, Array.Empty<int>());
			public Fixture()
			{
				Access.Setup(a => a.CanAccessAsync(It.IsAny<AdminAssistActor>(), false, It.IsAny<CancellationToken>())).ReturnsAsync(true);
				Departments.Setup(d => d.GetDepartmentByIdAsync(7, true)).ReturnsAsync(new Department { DepartmentId = 7, ManagingUserId = "owner" });
				foreach (var id in new[] { "owner", "supervisor", "member", "ungrouped", "disabled" }) People.Add(new DepartmentMember { DepartmentId = 7, UserId = id, IsDisabled = id == "disabled", IsHidden = id == "supervisor" });
				Members.Setup(m => m.GetAllDepartmentMembersUnlimitedAsync(7)).ReturnsAsync(People);
				GroupRows.Add(new DepartmentGroup { DepartmentId = 7, DepartmentGroupId = 10, Members = new List<DepartmentGroupMember> { new() { DepartmentId = 7, DepartmentGroupId = 10, UserId = "supervisor", IsAdmin = true } } });
				GroupRows.Add(new DepartmentGroup { DepartmentId = 7, DepartmentGroupId = 11, ParentDepartmentGroupId = 10, Members = new List<DepartmentGroupMember> { new() { DepartmentId = 7, DepartmentGroupId = 11, UserId = "member" } } });
				Groups.Setup(g => g.GetAllGroupsByDepartmentIdAsync(7)).ReturnsAsync(GroupRows);
				Roles.Setup(r => r.GetPersonnelRolesByDepartmentIdAsync(7)).ReturnsAsync(new[] { new PersonnelRole { DepartmentId = 7, PersonnelRoleId = 20 } });
				Assignments.Setup(r => r.GetAllRoleUsersForDepartmentAsync(7)).ReturnsAsync(new[] { new PersonnelRoleUser { PersonnelRoleId = 20, UserId = "member" } });
				Units.Setup(u => u.GetAllUnitsByDepartmentIdAsync(7)).ReturnsAsync(new[] { new Unit { DepartmentId = 7, UnitId = 1, StationGroupId = 11 }, new Unit { DepartmentId = 7, UnitId = 2 } });
				Permissions.Setup(p => p.GetAllByDepartmentIdAsync(7)).ReturnsAsync(Array.Empty<Permission>());
			}
		}
		[Test]
		public async Task Role_picker_returns_department_role_names_without_loading_people_or_assignments()
		{
			var f = new Fixture(); f.Roles.Setup(r => r.GetPersonnelRolesByDepartmentIdAsync(7)).ReturnsAsync(new[] {
				new PersonnelRole { DepartmentId = 7, PersonnelRoleId = 21, Name = "Dispatcher" }, new PersonnelRole { DepartmentId = 7, PersonnelRoleId = 20, Name = "Crew" } });
			var result = await f.Service.GetRoleOptionsAsync(new(7, "owner"), "0");
			Assert.That(result.Select(r => r.Name), Is.EqualTo(new[] { "Crew", "Dispatcher" }));
			f.Members.VerifyNoOtherCalls(); f.Assignments.VerifyNoOtherCalls(); f.Units.VerifyNoOtherCalls();
		}
		[Test]
		public void Role_picker_rejects_cross_tenant_sources_and_changed_or_revoked_access()
		{
			var f = new Fixture(); f.Roles.Setup(r => r.GetPersonnelRolesByDepartmentIdAsync(7)).ReturnsAsync(new[] { new PersonnelRole { DepartmentId = 8, PersonnelRoleId = 20, Name = "Foreign" } });
			Assert.ThrowsAsync<InvalidOperationException>(async () => await f.Service.GetRoleOptionsAsync(new(7, "owner"), "0"));
			f = new Fixture(); f.Roles.SetupSequence(r => r.GetPersonnelRolesByDepartmentIdAsync(7)).ReturnsAsync(new[] { new PersonnelRole { DepartmentId = 7, PersonnelRoleId = 20, Name = "Crew" } }).ReturnsAsync(Array.Empty<PersonnelRole>());
			Assert.ThrowsAsync<AdminAssistConcurrencyException>(async () => await f.Service.GetRoleOptionsAsync(new(7, "owner"), "0"));
			f = new Fixture(); f.Roles.Setup(r => r.GetPersonnelRolesByDepartmentIdAsync(7)).ReturnsAsync(Array.Empty<PersonnelRole>());
			f.Access.SetupSequence(a => a.CanAccessAsync(It.IsAny<AdminAssistActor>(), false, It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false);
			Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await f.Service.GetRoleOptionsAsync(new(7, "owner"), "0"));
		}
		private static ConfigurationImpactMetric Metric(ConfigurationImpactReport report, string name) => report.Metrics.Single(m => m.LabelKey == "Impact." + name);
		[Test]
		public async Task Hierarchical_resource_scope_counts_actors_and_pairs_including_hidden_current_members()
		{
			var f = new Fixture(); var report = await f.Service.PreviewAsync(new(7, "owner"), f.Request);
			Assert.That(Metric(report, "PermissionSample").After, Is.EqualTo(4));
			Assert.That(Metric(report, "PermissionActors").Before, Is.EqualTo(4));
			Assert.That(Metric(report, "PermissionActors").After, Is.EqualTo(2));
			Assert.That(Metric(report, "PermissionEdges").Before, Is.EqualTo(16));
			Assert.That(Metric(report, "PermissionEdges").After, Is.EqualTo(6)); // owner: all four; area supervisor: own + child station
			Assert.That(Metric(report, "PermissionLost").After, Is.EqualTo(10));
			Assert.That(Newtonsoft.Json.JsonConvert.SerializeObject(report), Does.Not.Contain("supervisor"));
		}
		[Test]
		public async Task Unit_scope_does_not_treat_two_missing_groups_as_the_same_group()
		{
			var f = new Fixture(); var report = await f.Service.PreviewAsync(new(7, "owner"), f.Request with { PermissionType = "CanSeeUnitLocations", Action = 3 });
			Assert.That(Metric(report, "PermissionEdges").Before, Is.EqualTo(8));
			Assert.That(Metric(report, "PermissionEdges").After, Is.EqualTo(3)); // owner sees both; member sees own station
			Assert.That(Metric(report, "PermissionActors").After, Is.EqualTo(2));
		}
		[Test]
		public async Task Department_action_checks_each_actors_actual_roles_through_the_permission_service()
		{
			var f = new Fixture(); var report = await f.Service.PreviewAsync(new(7, "owner"), f.Request with { PermissionType = "CreateCall", Action = 2, LockToGroup = false, RoleIds = new[] { 20 } });
			Assert.That(Metric(report, "PermissionActors").Before, Is.EqualTo(4));
			Assert.That(Metric(report, "PermissionActors").After, Is.EqualTo(2));
			Assert.That(Metric(report, "PermissionTargets").After, Is.EqualTo(1));
		}
		[Test]
		public async Task Missing_or_ambiguous_sources_are_unknown_instead_of_reducing_the_sample()
		{
			var f = new Fixture(); f.GroupRows[1].Members.Add(new() { DepartmentId = 7, DepartmentGroupId = 11, UserId = "supervisor" });
			var report = await f.Service.PreviewAsync(new(7, "owner"), f.Request);
			Assert.That(report.Metrics.Single().State, Is.EqualTo(EvidenceState.Unknown)); Assert.That(report.Metrics.Single().After, Is.Null);
			f = new Fixture(); f.Roles.Setup(r => r.GetPersonnelRolesByDepartmentIdAsync(7)).ReturnsAsync((IEnumerable<PersonnelRole>)null);
			report = await f.Service.PreviewAsync(new(7, "owner"), f.Request);
			Assert.That(report.Metrics.Single().State, Is.EqualTo(EvidenceState.Unknown));
		}
		[Test]
		public void Revoked_admin_and_cross_tenant_role_proposals_are_rejected()
		{
			var f = new Fixture();
			Assert.ThrowsAsync<ArgumentException>(async () => await f.Service.PreviewAsync(new(7, "owner"), f.Request with { Action = 2, RoleIds = new[] { 99 } }));
			f.Access.SetupSequence(a => a.CanAccessAsync(It.IsAny<AdminAssistActor>(), false, It.IsAny<CancellationToken>())).ReturnsAsync(true).ReturnsAsync(false);
			Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await f.Service.PreviewAsync(new(7, "owner"), f.Request));
		}
		[Test]
		public void Changed_membership_invalidates_preview_even_without_a_journal_revision()
		{
			var f = new Fixture();
			f.Members.SetupSequence(m => m.GetAllDepartmentMembersUnlimitedAsync(7)).ReturnsAsync(f.People).ReturnsAsync(f.People.Take(3));
			Assert.ThrowsAsync<AdminAssistConcurrencyException>(async () => await f.Service.PreviewAsync(new(7, "owner"), f.Request));
		}
		[Test]
		public async Task Cyclic_group_hierarchy_produces_unknown_and_does_not_hang()
		{
			var f = new Fixture(); f.GroupRows[0].ParentDepartmentGroupId = 11;
			var report = await f.Service.PreviewAsync(new(7, "owner"), f.Request);
			Assert.That(report.Metrics.Single().State, Is.EqualTo(EvidenceState.Unknown));
		}
		[TestCase(-1)] [TestCase(4)] [TestCase(90)]
		public void Unsupported_actions_are_rejected(int action)
		{
			var f = new Fixture(); Assert.ThrowsAsync<ArgumentException>(async () => await f.Service.PreviewAsync(new(7, "owner"), f.Request with { Action = action }));
		}
	}
}
