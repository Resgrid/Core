using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Checklists;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class ChecklistAuthorizationTests
	{
		private ChecklistAuthorizationService _service;
		private Mock<IDepartmentsService> _departments;
		private Mock<IDepartmentGroupsService> _groups;
		private Mock<IPermissionsService> _permissions;
		private Mock<IUnitsService> _units;
		private DepartmentMember _member;
		private readonly ChecklistActor _actor = new ChecklistActor { DepartmentId = 77, UserId = "member" };
		[SetUp]
		public void Setup()
		{
			_member = new DepartmentMember { DepartmentId = 77, UserId = "member" };
			_departments = new Mock<IDepartmentsService>(); _groups = new Mock<IDepartmentGroupsService>(); _permissions = new Mock<IPermissionsService>(); _units = new Mock<IUnitsService>();
			_departments.Setup(d => d.GetDepartmentMemberAsync("member", 77, true)).ReturnsAsync(() => _member);
			_departments.Setup(d => d.GetDepartmentByIdAsync(77, true)).ReturnsAsync(new Department { DepartmentId = 77, ManagingUserId = "owner", Name = "Department" });
			_groups.Setup(g => g.GetGroupForUserAsync("member", 77)).ReturnsAsync(new DepartmentGroup { DepartmentId = 77, DepartmentGroupId = 10, Members = new List<DepartmentGroupMember> { new DepartmentGroupMember { UserId = "member", IsAdmin = true } } });
			var roles = new Mock<IPersonnelRolesService>(); roles.Setup(r => r.GetRolesForUserAsync("member", 77)).ReturnsAsync(new List<PersonnelRole>());
			_service = new ChecklistAuthorizationService(_departments.Object, _groups.Object, roles.Object, _permissions.Object, _units.Object, new Mock<IAuthorizationService>().Object, new Mock<IUserProfileService>().Object);
		}
		[Test]
		public async Task Group_admin_default_is_limited_to_their_group_and_does_not_allow_definition_management()
		{
			(await _service.CanManageAsync(_actor)).Should().BeFalse();
			var run = new ChecklistCompletion { DepartmentId = 77, CreatedBy = "other", TargetType = (int)ChecklistTargetType.Group, TargetId = "10" };
			(await _service.CanReadAsync(_actor, run)).Should().BeTrue(); run.TargetId = "20"; (await _service.CanReadAsync(_actor, run)).Should().BeFalse();
			_member.IsAdmin = true; (await _service.CanReadAsync(_actor, run)).Should().BeTrue(); (await _service.CanManageAsync(_actor)).Should().BeTrue();
		}
		[Test]
		public async Task Moving_or_removing_a_unit_does_not_reassign_its_historical_results_to_another_group()
		{
			var run = new ChecklistCompletion { DepartmentId = 77, CreatedBy = "other", TargetType = (int)ChecklistTargetType.Unit, TargetId = "9", TargetGroupId = 10 };
			_units.Setup(u => u.GetUnitByIdAsync(9)).ReturnsAsync(new Unit { UnitId = 9, DepartmentId = 77, StationGroupId = 20 });
			(await _service.CanReadAsync(_actor, run)).Should().BeTrue();
			_units.Verify(u => u.GetUnitByIdAsync(It.IsAny<int>()), Times.Never);
			run.TargetGroupId = 20; (await _service.CanReadAsync(_actor, run)).Should().BeFalse();
		}
		[Test]
		public async Task Explicit_permission_can_widen_results_but_cannot_widen_department_membership()
		{
			_permissions.Setup(p => p.GetPermissionByDepartmentTypeAsync(77, PermissionTypes.ViewChecklistResults)).ReturnsAsync(new Permission { Action = (int)PermissionActions.Everyone, LockToGroup = false });
			var run = new ChecklistCompletion { DepartmentId = 77, CreatedBy = "other", TargetType = 0, TargetId = "77" };
			(await _service.CanReadAsync(_actor, run)).Should().BeTrue(); run.DepartmentId = 88; (await _service.CanReadAsync(_actor, run)).Should().BeFalse();
		}
		[Test]
		public async Task Disabled_or_deleted_members_cannot_use_old_claims_or_read_their_own_history()
		{
			_member.IsDisabled = true; _member.IsAdmin = true;
			Func<Task> read = () => _service.CanReadAsync(_actor, new ChecklistCompletion { DepartmentId = 77, CreatedBy = "member" });
			(await read.Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(403);
			_member.IsDisabled = false; _member.IsDeleted = true;
			(await read.Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(403);
		}
		[Test]
		public async Task Unit_and_group_target_ids_cannot_cross_departments()
		{
			_member.IsAdmin = true; _units.Setup(u => u.GetUnitByIdAsync(9)).ReturnsAsync(new Unit { UnitId = 9, DepartmentId = 88, Name = "Foreign" });
			_groups.Setup(g => g.GetGroupByIdAsync(9, true)).ReturnsAsync(new DepartmentGroup { DepartmentGroupId = 9, DepartmentId = 88, Name = "Foreign" });
			Func<Task> target = () => _service.TargetAsync(_actor, ChecklistTargetType.Unit, "9"); (await target.Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(404);
			target = () => _service.TargetAsync(_actor, ChecklistTargetType.Group, "9"); (await target.Should().ThrowAsync<ChecklistException>()).Which.StatusCode.Should().Be(404);
		}
		[TestCase(false, false, false, false)]
		[TestCase(false, true, false, true)]
		[TestCase(true, false, true, true)]
		public void Existing_claim_issuers_get_checklist_defaults_from_the_shared_catalog(bool admin, bool groupAdmin, bool manage, bool view)
		{
			var identity = new ClaimsIdentity(); ClaimsLogic.AddRecordClaims(identity, admin, new List<Permission>(), groupAdmin, new List<PersonnelRole>());
			identity.HasClaim(ResgridClaimTypes.Resources.Checklist, ResgridClaimTypes.Actions.Update).Should().Be(manage);
			identity.HasClaim(ResgridClaimTypes.Resources.ChecklistResults, ResgridClaimTypes.Actions.View).Should().Be(view);
		}
		[Test]
		public void Permission_screen_defaults_match_the_service_group_boundary()
		{
			var rows = Resgrid.Web.Areas.User.Models.Security.RecordsPermissionRows.Build(new List<Permission>(), ChecklistPermissionCatalog.All);
			rows.Should().ContainSingle(r => r.Type == PermissionTypes.ViewChecklistResults && r.LockToGroup && r.ShowLockToGroup);
			rows.Should().ContainSingle(r => r.Type == PermissionTypes.ManageChecklists && r.Value == (int)PermissionActions.DepartmentAdminsOnly);
		}
	}
}
