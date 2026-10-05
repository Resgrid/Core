using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Security.Audit20261005
{
	/// <summary>
	/// Permission audit 2026-10-05 item 3.13: Delete Call / Close Call / Add Call Data set to "Everyone" with "group only"
	/// used to allow everyone, because the source-group IsUserAllowed returned before reading the lock.
	/// </summary>
	[TestFixture]
	public class PeoplePermissionsServiceTests
	{
		private PermissionsService _service;

		[SetUp]
		public void SetUp()
		{
			_service = new PermissionsService(Mock.Of<IPermissionsRepository>(), Mock.Of<IUsersService>(), Mock.Of<IDepartmentGroupsService>());
		}

		private static Permission Everyone(bool lockToGroup) => new Permission { Action = (int)PermissionActions.Everyone, LockToGroup = lockToGroup };

		[Test]
		public void Locked_everyone_admits_a_member_whose_group_is_on_the_call()
		{
			_service.IsUserAllowed(Everyone(true), 1, 10, 10, false, false, new List<PersonnelRole>()).Should().BeTrue();
		}

		[Test]
		public void Locked_everyone_refuses_a_member_whose_group_is_not_on_the_call()
		{
			// Callers pass -1 as the source group when the member's group was not dispatched.
			_service.IsUserAllowed(Everyone(true), 1, -1, 10, false, false, null).Should().BeFalse();
			_service.IsUserAllowed(Everyone(true), 1, 20, 10, false, true, null).Should().BeFalse("a group admin of another group is no exception");
		}

		[Test]
		public void Locked_everyone_refuses_a_member_in_no_group()
		{
			_service.IsUserAllowed(Everyone(true), 1, -1, 0, false, false, null).Should().BeFalse();
			_service.IsUserAllowed(Everyone(true), 1, null, null, false, false, null).Should().BeFalse("no group matches no group");
		}

		[Test]
		public void Locked_everyone_always_admits_department_admins()
		{
			_service.IsUserAllowed(Everyone(true), 1, -1, 0, true, false, null).Should().BeTrue();
		}

		[Test]
		public void Unlocked_everyone_is_unchanged()
		{
			_service.IsUserAllowed(Everyone(false), 1, -1, 0, false, false, null).Should().BeTrue();
			_service.IsUserAllowed(null, 1, -1, 0, false, false, null).Should().BeTrue();
		}

		[Test]
		public async Task Close_call_with_locked_everyone_follows_the_dispatched_groups()
		{
			const int departmentId = 3;
			const int callId = 70;
			var departments = new Mock<IDepartmentsService>();
			var groups = new Mock<IDepartmentGroupsService>();
			var calls = new Mock<ICallsService>();
			var permissions = new Mock<IPermissionsService>();
			var scope = new Mock<IDispatchScopeService>();

			departments.Setup(x => x.GetDepartmentByIdAsync(departmentId, It.IsAny<bool>())).ReturnsAsync(new Department
			{
				DepartmentId = departmentId,
				ManagingUserId = "owner",
				Members = new List<DepartmentMember>
				{
					new DepartmentMember { DepartmentId = departmentId, UserId = "owner", IsAdmin = true },
					new DepartmentMember { DepartmentId = departmentId, UserId = "on-call" },
					new DepartmentMember { DepartmentId = departmentId, UserId = "off-call" }
				}
			});
			permissions.Setup(x => x.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.CloseCall)).ReturnsAsync(Everyone(true));
			permissions.Setup(x => x.IsUserAllowed(It.IsAny<Permission>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<List<PersonnelRole>>()))
				.Returns((Permission p, int d, int? s, int? u, bool a, bool g, List<PersonnelRole> r) => _service.IsUserAllowed(p, d, s, u, a, g, r));
			groups.Setup(x => x.GetGroupForUserAsync("on-call", departmentId)).ReturnsAsync(new DepartmentGroup { DepartmentGroupId = 5, DepartmentId = departmentId, Members = new List<DepartmentGroupMember>() });
			groups.Setup(x => x.GetGroupForUserAsync("off-call", departmentId)).ReturnsAsync(new DepartmentGroup { DepartmentGroupId = 6, DepartmentId = departmentId, Members = new List<DepartmentGroupMember>() });
			var call = new Call
			{
				CallId = callId,
				DepartmentId = departmentId,
				GroupDispatches = new List<CallDispatchGroup> { new CallDispatchGroup { DepartmentGroupId = 5 } }
			};
			calls.Setup(x => x.GetCallByIdAsync(callId, It.IsAny<bool>())).ReturnsAsync(call);
			calls.Setup(x => x.PopulateCallData(call, It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
				It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>())).ReturnsAsync(call);
			scope.Setup(x => x.CanUserAccessCallAsync(departmentId, It.IsAny<string>(), It.IsAny<Call>())).ReturnsAsync(true);

			var authorization = new AuthorizationService(departments.Object, Mock.Of<IInvitesService>(), calls.Object, Mock.Of<IMessageService>(),
				Mock.Of<IWorkLogsService>(), Mock.Of<ISubscriptionsService>(), groups.Object, Mock.Of<IPersonnelRolesService>(), Mock.Of<IUnitsService>(),
				permissions.Object, Mock.Of<ICalendarService>(), Mock.Of<IProtocolsService>(), Mock.Of<IShiftsService>(), Mock.Of<ICustomStateService>(),
				Mock.Of<ICertificationService>(), Mock.Of<IDocumentsService>(), Mock.Of<INotesService>(), Mock.Of<ICacheProvider>(), Mock.Of<IContactsService>(),
				Mock.Of<IEventAggregator>(), scope.Object);

			(await authorization.CanUserCloseCallAsync("on-call", callId, departmentId)).Should().BeTrue();
			(await authorization.CanUserCloseCallAsync("off-call", callId, departmentId)).Should().BeFalse();
			(await authorization.CanUserCloseCallAsync("owner", callId, departmentId)).Should().BeTrue();
		}
	}
}
