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

namespace Resgrid.Tests.Security.Audit20261005
{
	/// <summary>
	/// Audit 2026-10-05, 3.22: the Records inventory-usage path resolves a missing AdjustInventory row the way
	/// InventoryAuthorizationService does (department administrators), not as Everyone. A saved row still decides.
	/// </summary>
	[TestFixture]
	public class RecordsCommandInventoryAccessTests
	{
		private const int Dept = 9;

		private Mock<IPermissionsService> _permissions;
		private Mock<IDepartmentsService> _departments;
		private RecordsAuthorizationService _service;

		[SetUp]
		public void SetUp()
		{
			_permissions = new Mock<IPermissionsService>();
			_departments = new Mock<IDepartmentsService>();
			_departments.Setup(d => d.GetDepartmentMemberAsync("member", Dept, true)).ReturnsAsync(new DepartmentMember { UserId = "member", DepartmentId = Dept });
			_departments.Setup(d => d.GetDepartmentMemberAsync("admin", Dept, true)).ReturnsAsync(new DepartmentMember { UserId = "admin", DepartmentId = Dept, IsAdmin = true });
			_departments.Setup(d => d.GetDepartmentByIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = Dept, ManagingUserId = "owner" });

			_service = new RecordsAuthorizationService(_permissions.Object, _departments.Object, Mock.Of<IDepartmentGroupsService>(), Mock.Of<IPersonnelRolesService>(),
				Mock.Of<IDepartmentSettingsService>(), Mock.Of<IRmsOperationalRecordsRepository>(), Mock.Of<IRmsRecordGroupScopesRepository>(), Mock.Of<IRmsRecordParticipantsRepository>(),
				Mock.Of<ICacheProvider>(), Mock.Of<IRmsLegacyStatsRepository>(), Mock.Of<IRmsIncidentReportsRepository>(), new Lazy<IAuthorizationService>(() => Mock.Of<IAuthorizationService>()));
		}

		[Test]
		public async Task With_no_AdjustInventory_row_a_member_cannot_use_source_inventory()
		{
			(await _service.CanUseSourceInventoryAsync("member", Dept)).Should().BeFalse();
		}

		[Test]
		public async Task With_no_AdjustInventory_row_a_department_admin_can_use_source_inventory()
		{
			(await _service.CanUseSourceInventoryAsync("admin", Dept)).Should().BeTrue();
		}

		[Test]
		public async Task A_saved_AdjustInventory_row_still_decides_for_members()
		{
			var row = new Permission { DepartmentId = Dept, PermissionType = (int)PermissionTypes.AdjustInventory, Action = (int)PermissionActions.Everyone };
			_permissions.Setup(p => p.GetPermissionByDepartmentTypeAsync(Dept, PermissionTypes.AdjustInventory)).ReturnsAsync(row);
			_permissions.Setup(p => p.IsUserAllowed(row, Dept, It.IsAny<int?>(), It.IsAny<int?>(), false, false, It.IsAny<List<PersonnelRole>>())).Returns(true);

			(await _service.CanUseSourceInventoryAsync("member", Dept)).Should().BeTrue();
		}
	}
}
