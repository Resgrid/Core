using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class ChecklistAssignmentTests
	{
		[Test]
		public async Task Assignment_membership_requires_current_same_department_members_and_valid_targets()
		{
			var departments = new Mock<IDepartmentsService>(); var groups = new Mock<IDepartmentGroupsService>(); var units = new Mock<IUnitsService>(); var roles = new Mock<IPersonnelRolesService>();
			var members = new List<DepartmentMember> { new DepartmentMember { DepartmentId = 77, UserId = "active" }, new DepartmentMember { DepartmentId = 77, UserId = "disabled", IsDisabled = true }, new DepartmentMember { DepartmentId = 77, UserId = "deleted", IsDeleted = true }, new DepartmentMember { DepartmentId = 88, UserId = "foreign" } };
			departments.Setup(d => d.GetAllMembersForDepartmentUnlimitedAsync(77, true)).ReturnsAsync(members);
			departments.Setup(d => d.GetDepartmentMemberAsync("active", 77, true)).ReturnsAsync(members[0]);
			departments.Setup(d => d.GetDepartmentMemberAsync("foreign", 77, true)).ReturnsAsync(members[3]);
			units.Setup(u => u.GetUnitByIdAsync(12)).ReturnsAsync(new Unit { UnitId = 12, DepartmentId = 77 });
			units.Setup(u => u.GetUnitByIdAsync(13)).ReturnsAsync(new Unit { UnitId = 13, DepartmentId = 88 });
			units.Setup(u => u.GetActiveRolesForUnitAsync(12)).ReturnsAsync(new List<UnitActiveRole> { new UnitActiveRole { UnitId = 12, DepartmentId = 77, UserId = "active" }, new UnitActiveRole { UnitId = 12, DepartmentId = 77, UserId = "disabled" }, new UnitActiveRole { UnitId = 12, DepartmentId = 77, UserId = "deleted" }, new UnitActiveRole { UnitId = 12, DepartmentId = 88, UserId = "foreign" } });
			groups.Setup(g => g.GetGroupByIdAsync(9, true)).ReturnsAsync(new DepartmentGroup { DepartmentId = 77, DepartmentGroupId = 9 });
			groups.Setup(g => g.GetAllMembersForGroupAsync(9)).ReturnsAsync(new List<DepartmentGroupMember> { new DepartmentGroupMember { DepartmentId = 77, UserId = "active" }, new DepartmentGroupMember { DepartmentId = 88, UserId = "foreign" } });
			roles.Setup(r => r.GetRoleByIdAsync(8)).ReturnsAsync(new PersonnelRole { DepartmentId = 77, PersonnelRoleId = 8 });
			roles.Setup(r => r.GetAllMembersOfRoleAsync(8)).ReturnsAsync(new List<PersonnelRoleUser> { new PersonnelRoleUser { UserId = "active" }, new PersonnelRoleUser { UserId = "deleted" }, new PersonnelRoleUser { UserId = "foreign" } });
			var service = new ChecklistAssignmentService(departments.Object, groups.Object, units.Object, roles.Object);
			foreach (var choice in new[] { (1, "active"), (2, "8"), (3, "9"), (4, "12") }) (await service.MembersAsync(77, choice.Item1, choice.Item2)).Should().Equal("active");
			foreach (var choice in new[] { (1, "foreign"), (4, "13"), (7, "12"), (0, "unexpected"), (2, "-1") }) { Func<Task> bad = () => service.ValidateAsync(77, choice.Item1, choice.Item2); await bad.Should().ThrowAsync<ChecklistException>(); }
			members[0].IsDisabled = true; (await service.MembersAsync(77, 4, "12")).Should().BeEmpty();
		}
		[Test]
		public async Task Asset_picker_revalidates_tenant_and_provider_authorization_and_hides_an_absent_module()
		{
			var actor = new ChecklistActor { DepartmentId = 77, UserId = "author" }; var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentMemberAsync("author", 77, true)).ReturnsAsync(new DepartmentMember { DepartmentId = 77, UserId = "author" });
			var assets = new Mock<IChecklistAssetSource>(); assets.Setup(a => a.IsAvailableAsync(77)).ReturnsAsync(true);
			var id = Guid.NewGuid().ToString(); var forbidden = Guid.NewGuid().ToString();
			assets.Setup(a => a.ListAsync(actor)).ReturnsAsync(new List<ChecklistAssetTarget> { new ChecklistAssetTarget { Id = id, DepartmentId = 77 }, new ChecklistAssetTarget { Id = forbidden, DepartmentId = 77 }, new ChecklistAssetTarget { Id = Guid.NewGuid().ToString(), DepartmentId = 88 } });
			assets.Setup(a => a.GetAsync(actor, id)).ReturnsAsync(new ChecklistAssetTarget { Id = id, DepartmentId = 77, Name = "Authorized asset", GroupId = 9 });
			assets.Setup(a => a.GetAsync(actor, forbidden)).ReturnsAsync(new ChecklistAssetTarget { Id = forbidden, DepartmentId = 88, Name = "Foreign asset" });
			var service = new ChecklistAuthorizationService(departments.Object, Mock.Of<IDepartmentGroupsService>(), Mock.Of<IPersonnelRolesService>(), Mock.Of<IPermissionsService>(), Mock.Of<IUnitsService>(), Mock.Of<IAuthorizationService>(), Mock.Of<IUserProfileService>(), assets.Object);
			(await service.TargetsAsync(actor, ChecklistTargetType.InventoryAsset)).Should().ContainSingle(t => t.Id == id && t.GroupId == 9);
			assets.Setup(a => a.IsAvailableAsync(77)).ReturnsAsync(false); (await service.TargetsAsync(actor, ChecklistTargetType.InventoryAsset)).Should().BeEmpty();
		}
	}
}
