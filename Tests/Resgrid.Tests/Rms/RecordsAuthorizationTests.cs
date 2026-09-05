using System;
using System.Collections.Generic;
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
	[TestFixture]
	public class RecordsAuthorizationTests
	{
		private Mock<IDepartmentsService> _departments;
		private Mock<IPermissionsService> _permissions;
		private Mock<IDepartmentGroupsService> _groups;
		private Mock<IPersonnelRolesService> _roles;
		private Mock<IDepartmentSettingsService> _settings;
		private Mock<IRmsRecordGroupScopesRepository> _scopes;
		private DepartmentMember _member;
		private RecordsAuthorizationService _service;

		[SetUp]
		public void SetUp()
		{
			_member = new DepartmentMember { DepartmentId = 9, UserId = "author" };
			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(d => d.GetDepartmentMemberAsync("author", 9, true)).ReturnsAsync(() => _member);
			_departments.Setup(d => d.GetDepartmentByIdAsync(9, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = 9, ManagingUserId = "admin" });
			_permissions = new Mock<IPermissionsService>();
			_groups = new Mock<IDepartmentGroupsService>();
			_roles = new Mock<IPersonnelRolesService>();
			_settings = new Mock<IDepartmentSettingsService>();
			_scopes = new Mock<IRmsRecordGroupScopesRepository>();
			var records = new Mock<IRmsOperationalRecordsRepository>();
			records.Setup(r => r.GetByIdForDepartmentAsync(9, "r1")).ReturnsAsync(new RmsOperationalRecord { RmsOperationalRecordId = "r1", DepartmentId = 9, AuthorUserId = "author" });
			_service = new RecordsAuthorizationService(_permissions.Object, _departments.Object, _groups.Object, _roles.Object,
				_settings.Object, records.Object, _scopes.Object, Mock.Of<IRmsRecordParticipantsRepository>(),
				Mock.Of<ICacheProvider>(), Mock.Of<IRmsLegacyStatsRepository>(), Mock.Of<IRmsIncidentReportsRepository>(), new Lazy<IAuthorizationService>(() => Mock.Of<IAuthorizationService>()));
		}

		[Test]
		public async Task Removed_or_disabled_authors_do_not_keep_the_author_visibility_exception()
		{
			(await _service.CanUserViewRecordAsync("author", "r1", 9)).Should().BeTrue();
			_member.IsDisabled = true;
			(await _service.CanUserViewRecordAsync("author", "r1", 9)).Should().BeFalse();
			_member.IsDisabled = false;
			_member.IsDeleted = true;
			(await _service.CanUserViewRecordAsync("author", "r1", 9)).Should().BeFalse();
			_member = null;
			(await _service.GetVisibleGroupIdsAsync("author", 9)).Should().BeEmpty();
		}

		[Test]
		public async Task Missing_permission_rows_use_the_RMS_default_and_grant_revocation_is_immediate()
		{
			(await _service.HasPermissionAsync("author", 9, PermissionTypes.CreateRecord)).Should().BeTrue();
			(await _service.HasPermissionAsync("author", 9, PermissionTypes.ViewRestrictedRecords)).Should().BeFalse();
			_member.IsAdmin = true;
			(await _service.HasPermissionAsync("author", 9, PermissionTypes.ViewRestrictedRecords)).Should().BeTrue();
			_member.IsAdmin = false;
			(await _service.HasPermissionAsync("author", 9, PermissionTypes.ViewRestrictedRecords)).Should().BeFalse();
			(await _service.HasPermissionAsync(null, 9, PermissionTypes.CreateRecord)).Should().BeFalse();
		}

		[Test]
		public async Task Author_exception_and_cache_scope_require_active_membership()
		{
			_member.IsDisabled = true;
			(await _service.CanUserViewRecordAsync("author", "r1", 9)).Should().BeFalse();
			(await _service.GetReadScopeStampAsync("author", 9)).Should().BeNull();
		}

		[Test]
		public async Task Scope_stamp_tracks_admin_roles_and_permission_policy_even_in_department_wide_mode()
		{
			var initial = await _service.GetReadScopeStampAsync("author", 9);
			initial.Should().NotBeNullOrWhiteSpace();
			_member.IsAdmin = true;
			(await _service.GetReadScopeStampAsync("author", 9)).Should().NotBe(initial);
			_member.IsAdmin = false;
			_roles.Setup(r => r.GetRolesForUserAsync("author", 9)).ReturnsAsync(new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = 7 } });
			(await _service.GetReadScopeStampAsync("author", 9)).Should().NotBe(initial);
			_roles.Setup(r => r.GetRolesForUserAsync("author", 9)).ReturnsAsync(new List<PersonnelRole>());
			_permissions.Setup(p => p.GetAllPermissionsForDepartmentAsync(9)).ReturnsAsync(new List<Permission>
			{
				new Permission { PermissionType = (int)PermissionTypes.ViewRestrictedRecords, Action = (int)PermissionActions.DepartmentAdminsOnly }
			});
			(await _service.GetReadScopeStampAsync("author", 9)).Should().NotBe(initial);
		}

		[Test]
		public async Task An_expired_or_revoked_share_changes_cache_scope_without_modifying_a_report()
		{
			_settings.Setup(s => s.GetRecordsGroupVisibilityModeAsync(9, It.IsAny<bool>())).ReturnsAsync(RecordsGroupVisibilityMode.GroupScoped);
			_permissions.Setup(p => p.GetPermissionByDepartmentTypeAsync(9, PermissionTypes.ViewGroupRecords))
				.ReturnsAsync(new Permission { PermissionType = (int)PermissionTypes.ViewGroupRecords, Action = (int)PermissionActions.Everyone, LockToGroup = true });
			_groups.Setup(g => g.GetGroupForUserAsync("author", 9)).ReturnsAsync(new DepartmentGroup { DepartmentId = 9, DepartmentGroupId = 27 });
			var shares = new List<RmsRecordShare> { new RmsRecordShare { RmsRecordShareId = "share", DepartmentId = 9, RecordId = "r2", DepartmentGroupId = 27, RowVersion = 1, ExpiresOn = DateTime.UtcNow.AddHours(1) } };
			_scopes.Setup(s => s.GetEffectiveSharesAsync(9, It.IsAny<IEnumerable<int>>())).ReturnsAsync(() => shares);
			var initial = await _service.GetReadScopeStampAsync("author", 9);
			initial.Should().NotBeNullOrWhiteSpace();
			shares.Clear();
			(await _service.GetReadScopeStampAsync("author", 9)).Should().NotBe(initial);
			_scopes.Setup(s => s.GetEffectiveSharesAsync(9, It.IsAny<IEnumerable<int>>())).ThrowsAsync(new InvalidOperationException("share lookup unavailable"));
			(await _service.GetReadScopeStampAsync("author", 9)).Should().BeNull();
		}

		[Test]
		public async Task Scope_stamp_is_order_independent_and_refuses_unavailable_membership_sources()
		{
			_roles.Setup(r => r.GetRolesForUserAsync("author", 9)).ReturnsAsync(new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = 2 }, new PersonnelRole { PersonnelRoleId = 1 } });
			var initial = await _service.GetReadScopeStampAsync("author", 9);
			_roles.Setup(r => r.GetRolesForUserAsync("author", 9)).ReturnsAsync(new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = 1 }, new PersonnelRole { PersonnelRoleId = 2 } });
			(await _service.GetReadScopeStampAsync("author", 9)).Should().Be(initial);
			_roles.Setup(r => r.GetRolesForUserAsync("author", 9)).ThrowsAsync(new InvalidOperationException("unavailable"));
			(await _service.GetReadScopeStampAsync("author", 9)).Should().BeNull();
		}
	}
}
