using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Services;
using Resgrid.Model.WorkOrders;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class WorkOrderAuthorizationTests
	{
		[Test]
		public async Task Assignment_choices_use_full_names_and_only_load_visible_profiles()
		{
			var actor = new ChecklistActor { DepartmentId = 77, UserId = "manager" };
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentMemberAsync(actor.UserId, 77, true)).ReturnsAsync(new DepartmentMember { DepartmentId = 77, UserId = actor.UserId });
			var assignments = new Mock<IChecklistAssignmentService>();
			assignments.Setup(a => a.ChoicesAsync(actor)).ReturnsAsync(new List<ChecklistAssignmentChoice> {
				new() { Type = 1, Id = "a", Name = "login-a" }, new() { Type = 1, Id = "b", Name = "login-b" },
				new() { Type = 1, Id = "missing", Name = "fallback-login" }, new() { Type = 1, Id = "hidden", Name = "hidden-login" }
			});
			var resources = new Mock<IAuthorizationService>();
			resources.Setup(r => r.CanUserViewPersonAsync(actor.UserId, It.IsAny<string>(), 77)).ReturnsAsync((string caller, string user, int department) => user != "hidden");
			var profiles = new Mock<IUserProfileService>();
			profiles.Setup(p => p.GetSelectedUserProfilesAsync(It.IsAny<List<string>>())).ReturnsAsync(new List<UserProfile> {
				new() { UserId = "a", FirstName = "Zoe", LastName = "Taylor" }, new() { UserId = "b", FirstName = "Alex", LastName = "Smith" }
			});
			var service = new WorkOrderAuthorizationService(departments.Object, Mock.Of<IDepartmentGroupsService>(), Mock.Of<IPersonnelRolesService>(),
				Mock.Of<IPermissionsService>(), Mock.Of<IUnitsService>(), resources.Object, assignments.Object, profiles.Object);
			var choices = await service.ChoicesAsync(actor);
			choices.Users.Should().HaveCount(3);
			choices.Users.Should().Contain(u => u.Id == "a" && u.Name == "Zoe Taylor").And.Contain(u => u.Id == "b" && u.Name == "Alex Smith");
			choices.Users.Should().Contain(u => u.Id == "missing" && u.Name == "fallback-login");
			profiles.Verify(p => p.GetSelectedUserProfilesAsync(It.Is<List<string>>(ids => ids.Count == 3 && !ids.Contains("hidden"))), Times.Once);
		}

		[Test]
		public async Task Hidden_members_are_labelled_but_not_offered_and_removed_or_disabled_members_are_neither()
		{
			var actor = new ChecklistActor { DepartmentId = 77, UserId = "manager" };
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentMemberAsync(actor.UserId, 77, true)).ReturnsAsync(new DepartmentMember { DepartmentId = 77, UserId = actor.UserId });
			departments.Setup(d => d.GetAllMembersForDepartmentUnlimitedAsync(77, true)).ReturnsAsync(new List<DepartmentMember> {
				new() { DepartmentId = 77, UserId = "a" }, new() { DepartmentId = 77, UserId = "quiet", IsHidden = true },
				new() { DepartmentId = 77, UserId = "off", IsDisabled = true }, new() { DepartmentId = 77, UserId = "gone", IsDeleted = true }
			});
			var assignments = new Mock<IChecklistAssignmentService>();
			assignments.Setup(a => a.ChoicesAsync(actor)).ReturnsAsync(new List<ChecklistAssignmentChoice> { new() { Type = 1, Id = "a", Name = "login-a" } });
			var resources = new Mock<IAuthorizationService>();
			resources.Setup(r => r.CanUserViewPersonAsync(actor.UserId, It.IsAny<string>(), 77)).ReturnsAsync(true);
			var profiles = new Mock<IUserProfileService>();
			profiles.Setup(p => p.GetSelectedUserProfilesAsync(It.IsAny<List<string>>())).ReturnsAsync((List<string> ids) => ids.Select(id => new UserProfile { UserId = id, FirstName = "Member", LastName = id }).ToList());
			var service = new WorkOrderAuthorizationService(departments.Object, Mock.Of<IDepartmentGroupsService>(), Mock.Of<IPersonnelRolesService>(),
				Mock.Of<IPermissionsService>(), Mock.Of<IUnitsService>(), resources.Object, assignments.Object, profiles.Object);
			var choices = await service.ChoicesAsync(actor);
			choices.Users.Should().ContainSingle().Which.Name.Should().Be("Member a");
			choices.UserNames.Keys.Should().BeEquivalentTo(new[] { "a", "quiet" }, "a hidden member keeps a label for history and kept assignments");
			choices.UserNames["quiet"].Should().Be("Member quiet");
		}

		[Test]
		public async Task Multiple_users_and_roles_share_access_and_deduplicated_current_recipients()
		{
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentMemberAsync(It.IsAny<string>(), 77, true)).ReturnsAsync((string user, int d, bool fresh) => new DepartmentMember { DepartmentId = d, UserId = user });
			var roles = new Mock<IPersonnelRolesService>();
			roles.Setup(r => r.GetRolesForUserAsync(It.IsAny<string>(), 77)).ReturnsAsync(new List<PersonnelRole>());
			var assignments = new Mock<IChecklistAssignmentService>();
			assignments.Setup(a => a.MembersAsync(77, 1, "primary")).ReturnsAsync(new HashSet<string> { "primary" });
			assignments.Setup(a => a.MembersAsync(77, 1, "backup")).ReturnsAsync(new HashSet<string> { "backup" });
			assignments.Setup(a => a.MembersAsync(77, 2, "11")).ReturnsAsync(new HashSet<string> { "primary", "role-member" });
			assignments.Setup(a => a.MembersAsync(77, 2, "12")).ReturnsAsync(new HashSet<string> { "role-member", "other-role-member" });
			var service = new WorkOrderAuthorizationService(departments.Object, Mock.Of<IDepartmentGroupsService>(), roles.Object,
				Mock.Of<IPermissionsService>(), Mock.Of<IUnitsService>(), Mock.Of<IAuthorizationService>(), assignments.Object, Mock.Of<IUserProfileService>());
			var row = new WorkOrder { DepartmentId = 77, AssignedToUserIds = new() { "primary", "backup" }, AssignedToRoleIds = new() { 11, 12 } };
			(await service.RecipientsAsync(77, row)).Should().BeEquivalentTo("primary", "backup", "role-member", "other-role-member");
			foreach (var user in new[] { "backup", "role-member", "other-role-member" })
				(await service.CanContributeAsync(new ChecklistActor { DepartmentId = 77, UserId = user }, row)).Should().BeTrue();
			(await service.CanContributeAsync(new ChecklistActor { DepartmentId = 77, UserId = "outsider" }, row)).Should().BeFalse();
			(await service.RecipientsAsync(88, row)).Should().BeEmpty();
			new WorkOrderReadScope { UserId = "backup" }.Allows(row).Should().BeTrue();
			new WorkOrderReadScope { UserId = "other-role-member", RoleIds = new[] { 12 } }.Allows(row).Should().BeTrue();
			new WorkOrderReadScope { UserId = "outsider" }.Allows(row).Should().BeFalse();
			assignments.Setup(a => a.MembersAsync(77, 1, "backup")).ReturnsAsync(new HashSet<string>());
			assignments.Setup(a => a.MembersAsync(77, 2, "12")).ReturnsAsync(new HashSet<string>());
			(await service.RecipientsAsync(77, row)).Should().BeEquivalentTo("primary", "role-member");
		}

		[TestCase(false, 403, "ProtectedDataRequired")]
		[TestCase(true, 403, "ProtectedDataRequired")]
		[TestCase(false, 403, "MembershipRequired")]
		[TestCase(true, 403, "MembershipRequired")]
		public async Task Inventory_asset_access_failures_use_the_work_order_error_contract(bool validateTarget, int statusCode, string code)
		{
			var actor = new ChecklistActor { DepartmentId = 77, UserId = "manager" };
			var assetId = Guid.NewGuid().ToString("D");
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentMemberAsync(actor.UserId, actor.DepartmentId, true))
				.ReturnsAsync(new DepartmentMember { DepartmentId = actor.DepartmentId, UserId = actor.UserId });
			var assignments = new Mock<IChecklistAssignmentService>();
			assignments.Setup(a => a.ChoicesAsync(actor)).ReturnsAsync(new List<ChecklistAssignmentChoice>());
			var assets = new Mock<IChecklistAssetSource>();
			assets.Setup(a => a.IsAvailableAsync(actor.DepartmentId)).ReturnsAsync(true);
			assets.Setup(a => a.GetAsync(actor, assetId)).ThrowsAsync(new ChecklistException(statusCode, code));
			assets.Setup(a => a.ListAsync(actor)).ThrowsAsync(new ChecklistException(statusCode, code));
			var service = new WorkOrderAuthorizationService(departments.Object, Mock.Of<IDepartmentGroupsService>(),
				Mock.Of<IPersonnelRolesService>(), Mock.Of<IPermissionsService>(), Mock.Of<IUnitsService>(),
				Mock.Of<IAuthorizationService>(), assignments.Object, Mock.Of<IUserProfileService>(), assets.Object);

			Func<Task> action = validateTarget
				? () => service.ValidateTargetAsync(actor, new WorkOrderInput { InventoryAssetId = assetId })
				: async () => { await service.ChoicesAsync(actor); };
			var error = (await action.Should().ThrowAsync<WorkOrderException>()).Which;
			error.StatusCode.Should().Be(statusCode);
			error.Code.Should().Be(code);
		}
	}
}
