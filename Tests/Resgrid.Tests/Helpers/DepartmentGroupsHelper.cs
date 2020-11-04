using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Moq;
using Resgrid.Framework.Testing;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Tests.Helpers
{
	public static class DepartmentGroupsHelper
	{
		public static DepartmentGroup CreateDepartment4Group()
		{
			DepartmentGroup group = new DepartmentGroup();
			group.Name = "Department 4 Group";
			group.Members = new Collection<DepartmentGroupMember>();

			group.Members.Add(new DepartmentGroupMember { UserId = TestData.Users.TestUser9Id });
			group.Members.Add(new DepartmentGroupMember { UserId = TestData.Users.TestUser11Id });


			return group;
		}

		public static void SetupGroupAndRoles(int groupId, Mock<IDepartmentGroupsService> departmentGroupsServiceMock,
			Mock<IPersonnelRolesService> personnelRolesServiceMock)
		{
			var users = new List<string>();
			users.Add(Guid.NewGuid().ToString());
			users.Add(Guid.NewGuid().ToString());
			users.Add(Guid.NewGuid().ToString());
			users.Add(Guid.NewGuid().ToString());
			users.Add(Guid.NewGuid().ToString());
			users.Add(Guid.NewGuid().ToString());
			users.Add(Guid.NewGuid().ToString());
			users.Add(Guid.NewGuid().ToString());
			users.Add(Guid.NewGuid().ToString());
			users.Add(Guid.NewGuid().ToString());

			departmentGroupsServiceMock.Setup(x => x.GetGroupByIdAsync(groupId, true)).ReturnsAsync(new DepartmentGroup()
			{
				DepartmentGroupId = groupId,
				Name = "Test Group"
			});

			departmentGroupsServiceMock.Setup(x => x.GetAllAdminsForGroupAsync(groupId)).ReturnsAsync(new List<DepartmentGroupMember>()
				{
					new DepartmentGroupMember()
					{
						DepartmentGroupMemberId = 1,
						DepartmentGroupId = groupId,
						IsAdmin = true,
						UserId = users[0]
					},
					new DepartmentGroupMember()
					{
						DepartmentGroupMemberId = 2,
						DepartmentGroupId = groupId,
						IsAdmin = true,
						UserId = users[1]
					}
				});

			departmentGroupsServiceMock.Setup(x => x.GetAllMembersForGroupAsync(groupId)).ReturnsAsync(new List<DepartmentGroupMember>()
				{
					new DepartmentGroupMember()
					{
						DepartmentGroupMemberId = 1,
						DepartmentGroupId = groupId,
						IsAdmin = true,
						UserId = users[0]
					},
					new DepartmentGroupMember()
					{
						DepartmentGroupMemberId = 2,
						DepartmentGroupId = groupId,
						IsAdmin = true,
						UserId = users[1]
					},
					new DepartmentGroupMember()
					{
						DepartmentGroupMemberId = 4,
						DepartmentGroupId = groupId,
						IsAdmin = false,
						UserId = users[2]
					},
					new DepartmentGroupMember()
					{
						DepartmentGroupMemberId = 5,
						DepartmentGroupId = groupId,
						IsAdmin = false,
						UserId = users[3]
					},
					new DepartmentGroupMember()
					{
						DepartmentGroupMemberId = 6,
						DepartmentGroupId = groupId,
						IsAdmin = false,
						UserId = users[4]
					},
					new DepartmentGroupMember()
					{
						DepartmentGroupMemberId = 7,
						DepartmentGroupId = groupId,
						IsAdmin = false,
						UserId = users[5]
					}
				});

			personnelRolesServiceMock.Setup(x => x.GetAllMembersOfRoleAsync(1)).ReturnsAsync(new List<PersonnelRoleUser>()
				{
					new PersonnelRoleUser()
					{
						PersonnelRoleId = 1,
						PersonnelRoleUserId = 1,
						UserId = users[1]
					},
					new PersonnelRoleUser()
					{
						PersonnelRoleId = 1,
						PersonnelRoleUserId = 2,
						UserId = users[2]
					}
				});

			personnelRolesServiceMock.Setup(x => x.GetAllMembersOfRoleAsync(2)).ReturnsAsync(new List<PersonnelRoleUser>()
				{
					new PersonnelRoleUser()
					{
						PersonnelRoleId = 1,
						PersonnelRoleUserId = 3,
						UserId = users[3]
					},
					new PersonnelRoleUser()
					{
						PersonnelRoleId = 1,
						PersonnelRoleUserId = 4,
						UserId = users[4]
					},
					new PersonnelRoleUser()
					{
						PersonnelRoleId = 1,
						PersonnelRoleUserId = 5,
						UserId = users[5]
					}
				});
		}

		public static void SetupGroup(int groupId, Mock<IDepartmentGroupsService> departmentGroupsServiceMock)
		{
			var users = new List<string>();
			users.Add(Guid.NewGuid().ToString());
			users.Add(Guid.NewGuid().ToString());
			users.Add(Guid.NewGuid().ToString());
			users.Add(Guid.NewGuid().ToString());
			users.Add(Guid.NewGuid().ToString());
			users.Add(Guid.NewGuid().ToString());
			users.Add(Guid.NewGuid().ToString());
			users.Add(Guid.NewGuid().ToString());
			users.Add(Guid.NewGuid().ToString());
			users.Add(Guid.NewGuid().ToString());

			departmentGroupsServiceMock.Setup(x => x.GetGroupByIdAsync(groupId, true)).ReturnsAsync(new DepartmentGroup()
			{
				DepartmentGroupId = groupId,
				Name = "Test Group"
			});

			departmentGroupsServiceMock.Setup(x => x.GetAllAdminsForGroupAsync(groupId)).ReturnsAsync(new List<DepartmentGroupMember>()
				{
					new DepartmentGroupMember()
					{
						DepartmentGroupMemberId = 1,
						DepartmentGroupId = groupId,
						IsAdmin = true,
						UserId = users[0]
					},
					new DepartmentGroupMember()
					{
						DepartmentGroupMemberId = 2,
						DepartmentGroupId = groupId,
						IsAdmin = true,
						UserId = users[1]
					}
				});

			departmentGroupsServiceMock.Setup(x => x.GetAllMembersForGroupAsync(groupId)).ReturnsAsync(new List<DepartmentGroupMember>()
				{
					new DepartmentGroupMember()
					{
						DepartmentGroupMemberId = 1,
						DepartmentGroupId = groupId,
						IsAdmin = true,
						UserId = users[0]
					},
					new DepartmentGroupMember()
					{
						DepartmentGroupMemberId = 2,
						DepartmentGroupId = groupId,
						IsAdmin = true,
						UserId = users[1]
					},
					new DepartmentGroupMember()
					{
						DepartmentGroupMemberId = 4,
						DepartmentGroupId = groupId,
						IsAdmin = false,
						UserId = users[2]
					},
					new DepartmentGroupMember()
					{
						DepartmentGroupMemberId = 5,
						DepartmentGroupId = groupId,
						IsAdmin = false,
						UserId = users[3]
					},
					new DepartmentGroupMember()
					{
						DepartmentGroupMemberId = 6,
						DepartmentGroupId = groupId,
						IsAdmin = false,
						UserId = users[4]
					},
					new DepartmentGroupMember()
					{
						DepartmentGroupMemberId = 7,
						DepartmentGroupId = groupId,
						IsAdmin = false,
						UserId = users[5]
					}
				});

		}
	}
}
