using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Who supervises which shift groups: the department sees everything, a contracted provider's group admin only
	/// their own teams (and the teams beneath them).
	/// </summary>
	[TestFixture]
	public class ShiftManagementScopeAuthorizationTests
	{
		private const int DepartmentId = 4;
		private const int ProviderGroup = 10;
		private const int ProviderTeam = 11;
		private const int OtherGroup = 20;
		private const int SupervisorRole = 7;

		private Mock<IDepartmentsService> _departmentsService;
		private Mock<IDepartmentGroupsService> _departmentGroupsService;
		private Mock<IPersonnelRolesService> _personnelRolesService;
		private Mock<IPermissionsService> _permissionsService;
		private Department _department;
		private AuthorizationService _service;

		[SetUp]
		public void SetUp()
		{
			_departmentsService = new Mock<IDepartmentsService>();
			_departmentGroupsService = new Mock<IDepartmentGroupsService>();
			_personnelRolesService = new Mock<IPersonnelRolesService>();
			_permissionsService = new Mock<IPermissionsService>();

			_department = new Department { DepartmentId = DepartmentId, ManagingUserId = "owner", AdminUsers = new List<string> { "dmh-admin" } };

			_departmentsService.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(_department);
			_departmentsService.Setup(x => x.GetDepartmentMemberAsync(It.IsAny<string>(), DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync((string userId, int _, bool __) => userId == "stranger" ? null : new DepartmentMember { UserId = userId, DepartmentId = DepartmentId });
			_departmentGroupsService.Setup(x => x.GetAllGroupsForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<DepartmentGroup>
			{
				new DepartmentGroup
				{
					DepartmentGroupId = ProviderGroup,
					Members = new List<DepartmentGroupMember> { new DepartmentGroupMember { UserId = "provider-lead", IsAdmin = true } }
				},
				new DepartmentGroup { DepartmentGroupId = ProviderTeam, ParentDepartmentGroupId = ProviderGroup, Members = new List<DepartmentGroupMember>() },
				new DepartmentGroup
				{
					DepartmentGroupId = OtherGroup,
					Members = new List<DepartmentGroupMember> { new DepartmentGroupMember { UserId = "provider-lead", IsAdmin = false } }
				}
			});
			_personnelRolesService.Setup(x => x.GetRolesForUserAsync(It.IsAny<string>(), DepartmentId)).ReturnsAsync(new List<PersonnelRole>());

			_service = new AuthorizationService(_departmentsService.Object, new Mock<IInvitesService>().Object, new Mock<ICallsService>().Object,
				new Mock<IMessageService>().Object, new Mock<IWorkLogsService>().Object, new Mock<ISubscriptionsService>().Object,
				_departmentGroupsService.Object, _personnelRolesService.Object, new Mock<IUnitsService>().Object, _permissionsService.Object,
				new Mock<ICalendarService>().Object, new Mock<IProtocolsService>().Object, new Mock<IShiftsService>().Object,
				new Mock<ICustomStateService>().Object, new Mock<ICertificationService>().Object, new Mock<IDocumentsService>().Object,
				new Mock<INotesService>().Object, new Mock<ICacheProvider>().Object, new Mock<IContactsService>().Object,
				new Mock<IEventAggregator>().Object, new Mock<IDispatchScopeService>().Object);
		}

		[Test]
		public async Task Department_admin_supervises_every_group()
		{
			var scope = await _service.GetShiftManagementScopeAsync("dmh-admin", DepartmentId);

			scope.AllGroups.Should().BeTrue();
		}

		[Test]
		public async Task Managing_user_without_a_member_record_supervises_every_group()
		{
			_departmentsService.Setup(x => x.GetDepartmentMemberAsync("owner", DepartmentId, It.IsAny<bool>())).ReturnsAsync((DepartmentMember)null);

			var scope = await _service.GetShiftManagementScopeAsync("owner", DepartmentId);

			scope.AllGroups.Should().BeTrue();
		}

		[Test]
		public async Task Group_admin_supervises_their_group_and_its_child_teams_only()
		{
			var scope = await _service.GetShiftManagementScopeAsync("provider-lead", DepartmentId);

			scope.AllGroups.Should().BeFalse();
			scope.GroupIds.Should().BeEquivalentTo(new[] { ProviderGroup, ProviderTeam });
			scope.CanManageGroup(OtherGroup).Should().BeFalse();
		}

		[Test]
		public async Task Select_role_shift_managers_supervise_every_group()
		{
			_permissionsService.Setup(x => x.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.CreateShift))
				.ReturnsAsync(new Permission { Action = (int)PermissionActions.DepartmentAdminsAndSelectRoles, Data = $"3, {SupervisorRole}" });
			_personnelRolesService.Setup(x => x.GetRolesForUserAsync("scheduler", DepartmentId))
				.ReturnsAsync(new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = SupervisorRole } });

			(await _service.GetShiftManagementScopeAsync("scheduler", DepartmentId)).AllGroups.Should().BeTrue();
			(await _service.GetShiftManagementScopeAsync("responder", DepartmentId)).IsSupervisor.Should().BeFalse();
		}

		[Test]
		public async Task Everyone_permission_makes_everyone_a_shift_manager()
		{
			_permissionsService.Setup(x => x.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.CreateShift))
				.ReturnsAsync(new Permission { Action = (int)PermissionActions.Everyone });

			(await _service.GetShiftManagementScopeAsync("responder", DepartmentId)).AllGroups.Should().BeTrue();
		}

		[Test]
		public async Task Non_members_get_no_scope()
		{
			var scope = await _service.GetShiftManagementScopeAsync("stranger", DepartmentId);

			scope.IsSupervisor.Should().BeFalse();
		}
	}
}
