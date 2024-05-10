using System;
using System.Collections.Generic;
using System.Linq;
using Moq;
using Resgrid.Framework.Testing;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Model.Identity;

namespace Resgrid.Tests.Helpers
{
	public static class DepartmentMembershipHelpers
	{
		public static void SetupDisabledAndHiddenUsers(Mock<IDepartmentsService> mock)
		{
			mock.Setup(m => m.IsUserDisabledAsync(TestData.Users.TestUser10Id, 1)).ReturnsAsync(true);
			mock.Setup(m => m.IsUserHiddenAsync(TestData.Users.TestUser11Id, 1)).ReturnsAsync(true);

			mock.Setup(m => m.GetDepartmentMemberAsync(TestData.Users.TestUser9Id, 1, true)).ReturnsAsync(CreateDepartmentMembershipsForDepartment4().FirstOrDefault(x => x.UserId == TestData.Users.TestUser9Id));
			mock.Setup(m => m.GetDepartmentMemberAsync(TestData.Users.TestUser10Id, 1, true)).ReturnsAsync(CreateDepartmentMembershipsForDepartment4().FirstOrDefault(x => x.UserId == TestData.Users.TestUser10Id));
			mock.Setup(m => m.GetDepartmentMemberAsync(TestData.Users.TestUser11Id, 1, true)).ReturnsAsync(CreateDepartmentMembershipsForDepartment4().FirstOrDefault(x => x.UserId == TestData.Users.TestUser11Id));
		}

		public static void SetupUsersForDepartment1(Mock<IDepartmentsService> mock)
		{

			var users = new List<IdentityUser>();
			users.Add(UsersHelpers.CreateUser(Guid.NewGuid().ToString()));
			users.Add(UsersHelpers.CreateUser(Guid.NewGuid().ToString()));
			users.Add(UsersHelpers.CreateUser(Guid.NewGuid().ToString()));
			users.Add(UsersHelpers.CreateUser(Guid.NewGuid().ToString()));
			users.Add(UsersHelpers.CreateUser(Guid.NewGuid().ToString()));
			users.Add(UsersHelpers.CreateUser(Guid.NewGuid().ToString()));

			var admins = new List<IdentityUser>();
			admins.Add(UsersHelpers.CreateUser(Guid.NewGuid().ToString()));
			admins.Add(UsersHelpers.CreateUser(Guid.NewGuid().ToString()));

			mock.Setup(m => m.GetAllUsersForDepartment(1,true,false)).ReturnsAsync(users);
			mock.Setup(m => m.GetAllAdminsForDepartmentAsync(1)).ReturnsAsync(admins);
		}

		public static IQueryable<DepartmentMember> CreateDepartmentMembershipsForDepartment4()
		{
			List<DepartmentMember> members = new List<DepartmentMember>();

			members.Add(
																						new DepartmentMember
																						{
																							DepartmentMemberId = 9,
																							DepartmentId = 4,
																							UserId = TestData.Users.TestUser9Id,
																							IsAdmin = true,
																							IsHidden = true,
																							User = new IdentityUser
																							{
																								UserId = TestData.Users.TestUser9Id
																							}
																						});

			members.Add(
																						new DepartmentMember
																						{
																							DepartmentMemberId = 10,
																							DepartmentId = 4,
																							UserId = TestData.Users.TestUser10Id,
																							IsAdmin = false,
																							IsDisabled = true,
																							User = new IdentityUser
																							{
																								UserId = TestData.Users.TestUser10Id
																							}
																						});

			members.Add(
																						new DepartmentMember
																						{
																							DepartmentMemberId = 11,
																							DepartmentId = 4,
																							UserId = TestData.Users.TestUser11Id,
																							IsAdmin = false,
																							User = new IdentityUser
																							{
																								UserId = TestData.Users.TestUser11Id
																							}
																						});

			return members.AsQueryable();
		}
	}
}
