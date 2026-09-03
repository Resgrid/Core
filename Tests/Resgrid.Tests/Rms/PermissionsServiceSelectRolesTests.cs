using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// PermissionActions.DepartmentAndGroupAdminsAndSelectRoles (4) in both IsUserAllowed overloads,
	/// with and without LockToGroup, plus the unknown-future-value denial (registry section 4.4).
	/// </summary>
	[TestFixture]
	public class PermissionsServiceSelectRolesTests
	{
		private PermissionsService _service;
		private static readonly List<PersonnelRole> Role9 = new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = 9 } };
		private static readonly List<PersonnelRole> Role2 = new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = 2 } };

		[SetUp]
		public void SetUp()
		{
			_service = new PermissionsService(new Mock<IPermissionsRepository>().Object, new Mock<IUsersService>().Object);
		}

		private static Permission Value4(string data = "9", bool lockToGroup = false)
		{
			return new Permission { PermissionType = (int)PermissionTypes.ReviewRecords, Action = (int)PermissionActions.DepartmentAndGroupAdminsAndSelectRoles, Data = data, LockToGroup = lockToGroup };
		}

		[Test]
		public void Simple_overload_grants_admins_group_admins_and_selected_roles()
		{
			_service.IsUserAllowed(Value4(), true, false, null).Should().BeTrue();
			_service.IsUserAllowed(Value4(), false, true, null).Should().BeTrue();
			_service.IsUserAllowed(Value4(), false, false, Role9).Should().BeTrue();
			_service.IsUserAllowed(Value4(), false, false, Role2).Should().BeFalse();
			_service.IsUserAllowed(Value4(), false, false, null).Should().BeFalse();
		}

		[Test]
		public void Group_overload_without_lock_ignores_group_membership()
		{
			_service.IsUserAllowed(Value4(), 1, 10, 20, false, true, null).Should().BeTrue();
			_service.IsUserAllowed(Value4(), 1, 10, 20, false, false, Role9).Should().BeTrue();
			_service.IsUserAllowed(Value4(), 1, 10, 20, false, false, Role2).Should().BeFalse();
		}

		[Test]
		public void Group_overload_with_lock_requires_the_same_group_unless_department_admin()
		{
			_service.IsUserAllowed(Value4(lockToGroup: true), 1, 10, 10, false, true, null).Should().BeTrue();
			_service.IsUserAllowed(Value4(lockToGroup: true), 1, 10, 20, false, true, null).Should().BeFalse();
			_service.IsUserAllowed(Value4(lockToGroup: true), 1, 10, 10, false, false, Role9).Should().BeTrue();
			_service.IsUserAllowed(Value4(lockToGroup: true), 1, 10, 20, false, false, Role9).Should().BeFalse();
			_service.IsUserAllowed(Value4(lockToGroup: true), 1, 10, 20, true, false, null).Should().BeTrue("department admins pass every LockToGroup permission");
		}

		[Test]
		public void Unknown_future_action_value_denies_in_both_overloads()
		{
			var future = new Permission { PermissionType = (int)PermissionTypes.ReviewRecords, Action = 42, Data = "9" };

			_service.IsUserAllowed(future, false, true, Role9).Should().BeFalse();
			_service.IsUserAllowed(future, 1, 10, 10, false, true, Role9).Should().BeFalse();
		}

		[Test]
		public void Existing_values_are_unchanged()
		{
			var everyone = new Permission { Action = (int)PermissionActions.Everyone };
			var adminsOnly = new Permission { Action = (int)PermissionActions.DepartmentAdminsOnly };

			_service.IsUserAllowed(everyone, false, false, null).Should().BeTrue();
			_service.IsUserAllowed(adminsOnly, false, true, null).Should().BeFalse();
			_service.IsUserAllowed(null, false, false, null).Should().BeTrue("a missing row means allowed");
		}
	}
}
