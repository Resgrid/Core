using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Inventories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class InventoryAuthorizationTests
	{
		private InventoryActor _actor;
		private DepartmentMember _member;
		private Mock<IDepartmentsService> _departments;
		private Mock<IDepartmentGroupsService> _groups;
		private Mock<IUnitsService> _units;
		private Mock<IAuthorizationService> _resources;
		private Mock<IPermissionsService> _permissions;
		private Mock<IPersonnelRolesService> _roles;
		private Mock<IDepartmentSettingsService> _settings;
		private InventoryAuthorizationService _service;
		[SetUp]
		public void Setup()
		{
			_actor = new InventoryActor { DepartmentId = 77, UserId = "member" };
			_member = new DepartmentMember { DepartmentId = 77, UserId = _actor.UserId };
			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(d => d.GetDepartmentMemberAsync(_actor.UserId, 77, true)).ReturnsAsync(() => _member);
			_departments.Setup(d => d.GetDepartmentByIdAsync(77, true)).ReturnsAsync(new Department { DepartmentId = 77, ManagingUserId = "another-user" });
			_groups = new Mock<IDepartmentGroupsService>();
			_groups.Setup(g => g.GetGroupForUserAsync(_actor.UserId, 77)).ReturnsAsync(new DepartmentGroup { DepartmentId = 77, DepartmentGroupId = 101 });
			_units = new Mock<IUnitsService>(); _resources = new Mock<IAuthorizationService>();
			_permissions = new Mock<IPermissionsService>(); _roles = new Mock<IPersonnelRolesService>();
			_roles.Setup(r => r.GetRolesForUserAsync(_actor.UserId, 77)).ReturnsAsync(new List<PersonnelRole>());
			_settings = new Mock<IDepartmentSettingsService>();
			_settings.Setup(s => s.GetDepartmentModuleSettingsAsync(77, true)).ReturnsAsync(new DepartmentModuleSettings());
			_service = new InventoryAuthorizationService(_departments.Object, _groups.Object, _units.Object, _resources.Object, _permissions.Object, _roles.Object, _settings.Object);
		}
		private void Allow(PermissionTypes type, bool groupLocked = false) => _permissions.Setup(p => p.GetPermissionByDepartmentTypeAsync(77, type))
			.ReturnsAsync(new Permission { DepartmentId = 77, PermissionType = (int)type, Action = (int)PermissionActions.Everyone, LockToGroup = groupLocked });
		private static async Task Denied(Func<Task> action, string code, int status = 403)
		{
			var error = (await action.Should().ThrowAsync<InventoryException>()).Which;
			error.Code.Should().Be(code); error.StatusCode.Should().Be(status);
		}
		[TestCase("missing")]
		[TestCase("foreign")]
		[TestCase("deleted")]
		[TestCase("disabled")]
		public async Task Current_membership_is_required_even_for_reads(string state)
		{
			if (state == "missing") _member = null;
			else if (state == "foreign") _member.DepartmentId = 78;
			else if (state == "deleted") _member.IsDeleted = true;
			else _member.IsDisabled = true;
			await Denied(() => _service.RequireAsync(_actor), "MembershipRequired");
			_departments.Verify(d => d.GetDepartmentMemberAsync(_actor.UserId, 77, true), Times.Once);
		}
		[Test]
		public async Task Revoked_membership_is_rechecked_on_the_next_operation()
		{
			await _service.RequireAsync(_actor);
			_member.IsDisabled = true;
			await Denied(() => _service.RequireAsync(_actor), "MembershipRequired");
			_departments.Verify(d => d.GetDepartmentMemberAsync(_actor.UserId, 77, true), Times.Exactly(2));
		}
		[Test]
		public async Task Module_suspension_blocks_mutation_and_keeps_historical_read_authorization()
		{
			Allow(PermissionTypes.AdjustInventory);
			_settings.Setup(s => s.GetDepartmentModuleSettingsAsync(77, true)).ReturnsAsync(new DepartmentModuleSettings { InventoryDisabled = true });
			await _service.RequireAsync(_actor);
			await Denied(() => _service.RequireAsync(_actor, true), "InventoryDisabled", 409);
			(await _service.IsEnabledAsync(77)).Should().BeFalse();
			_settings.Verify(s => s.GetDepartmentModuleSettingsAsync(77, true), Times.Exactly(2));
		}
		[TestCase(PermissionTypes.TransferInventory)]
		[TestCase(PermissionTypes.IssueInventory)]
		public async Task Missing_transfer_and_issue_rules_inherit_adjust_permission(PermissionTypes type)
		{
			Allow(PermissionTypes.AdjustInventory);
			await _service.RequireAsync(_actor, true, type);
			_permissions.Verify(p => p.GetPermissionByDepartmentTypeAsync(77, PermissionTypes.AdjustInventory), Times.Once);
		}
		[TestCase(PermissionTypes.TransferInventory)]
		[TestCase(PermissionTypes.IssueInventory)]
		public async Task Explicit_transfer_or_issue_rule_overrides_permissive_adjust_rule(PermissionTypes type)
		{
			Allow(PermissionTypes.AdjustInventory);
			_permissions.Setup(p => p.GetPermissionByDepartmentTypeAsync(77, type)).ReturnsAsync(new Permission { DepartmentId = 77, PermissionType = (int)type, Action = (int)PermissionActions.DepartmentAdminsOnly });
			await Denied(() => _service.RequireAsync(_actor, true, type), "PermissionRequired");
			_permissions.Verify(p => p.GetPermissionByDepartmentTypeAsync(77, PermissionTypes.AdjustInventory), Times.Never);
		}
		[Test]
		public async Task Controlled_operations_do_not_inherit_adjust_access_and_default_to_department_administrators()
		{
			Allow(PermissionTypes.AdjustInventory);
			await Denied(() => _service.RequireAsync(_actor, true, PermissionTypes.ManageControlledSubstances), "PermissionRequired");
			_member.IsAdmin = true;
			await _service.RequireAsync(_actor, true, PermissionTypes.ManageControlledSubstances);
			_permissions.Verify(p => p.GetPermissionByDepartmentTypeAsync(77, PermissionTypes.AdjustInventory), Times.Never);
		}
		[TestCase(null)]
		[TestCase(102)]
		public async Task Group_locked_fallback_rejects_missing_or_other_group(int? targetGroup)
		{
			Allow(PermissionTypes.AdjustInventory, true);
			await Denied(() => _service.RequireAsync(_actor, true, PermissionTypes.TransferInventory, targetGroup), "PermissionRequired");
			await _service.RequireAsync(_actor, true, PermissionTypes.TransferInventory, 101);
			_member.IsAdmin = true;
			await _service.RequireAsync(_actor, true, PermissionTypes.TransferInventory, targetGroup);
		}
		[Test]
		public async Task Explicit_selected_role_can_issue_without_adjust_permission()
		{
			_permissions.Setup(p => p.GetPermissionByDepartmentTypeAsync(77, PermissionTypes.IssueInventory)).ReturnsAsync(new Permission
				{ DepartmentId = 77, PermissionType = (int)PermissionTypes.IssueInventory, Action = (int)PermissionActions.DepartmentAdminsAndSelectRoles, Data = "12,13" });
			_roles.Setup(r => r.GetRolesForUserAsync(_actor.UserId, 77)).ReturnsAsync(new List<PersonnelRole> { new() { DepartmentId = 77, PersonnelRoleId = 13 } });
			await _service.RequireAsync(_actor, true, PermissionTypes.IssueInventory);
			_permissions.Verify(p => p.GetPermissionByDepartmentTypeAsync(77, PermissionTypes.AdjustInventory), Times.Never);
		}
		[TestCase(InventoryLocationType.Unit)]
		[TestCase(InventoryLocationType.Station)]
		[TestCase(InventoryLocationType.Personnel)]
		public async Task Holder_identity_must_belong_to_the_current_department(InventoryLocationType type)
		{
			var location = new InventoryLocation { DepartmentId = 77, LocationType = (int)type };
			if (type == InventoryLocationType.Unit)
			{
				location.UnitId = 501; _units.Setup(u => u.GetUnitByIdAsync(501)).ReturnsAsync(new Unit { DepartmentId = 78, UnitId = 501 });
				_resources.Setup(r => r.CanUserViewUnitAsync(_actor.UserId, 501)).ReturnsAsync(true);
			}
			else if (type == InventoryLocationType.Station)
			{
				location.GroupId = 501; _groups.Setup(g => g.GetGroupByIdAsync(501, true)).ReturnsAsync(new DepartmentGroup { DepartmentId = 78, DepartmentGroupId = 501 });
			}
			else
			{
				location.UserId = "foreign-member"; _departments.Setup(d => d.GetDepartmentMemberAsync(location.UserId, 77, true)).ReturnsAsync(new DepartmentMember { DepartmentId = 78, UserId = location.UserId });
				_resources.Setup(r => r.CanUserViewPersonAsync(_actor.UserId, location.UserId, 77)).ReturnsAsync(true);
			}
			await Denied(() => _service.ValidateHolderAsync(_actor, location), "LocationUnavailable", 404);
		}
		[Test]
		public async Task Same_department_holder_still_requires_resource_visibility()
		{
			var location = new InventoryLocation { DepartmentId = 77, LocationType = (int)InventoryLocationType.Unit, UnitId = 501 };
			_units.Setup(u => u.GetUnitByIdAsync(501)).ReturnsAsync(new Unit { DepartmentId = 77, UnitId = 501 });
			await Denied(() => _service.ValidateHolderAsync(_actor, location), "LocationUnavailable", 404);
			_resources.Setup(r => r.CanUserViewUnitAsync(_actor.UserId, 501)).ReturnsAsync(true);
			await _service.ValidateHolderAsync(_actor, location);
		}
	}
}
