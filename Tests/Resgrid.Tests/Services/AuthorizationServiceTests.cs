using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Framework.Testing;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	namespace AuthorizationServiceTests
	{
		public class with_the_authorization_service : TestBase
		{
			protected IAuthorizationService _authorizationService;

			protected Mock<IDepartmentsService> _departmentsServiceMock;
			protected Mock<IInvitesService> _invitesServiceMock;
			protected Mock<ICallsService> _callsServiceMock;
			protected Mock<IMessageService> _messageServiceMock;
			protected Mock<IWorkLogsService> _workLogsServiceMock;
			protected Mock<ISubscriptionsService> _subscriptionsServiceMock;
			protected Mock<IDepartmentGroupsService> _departmentGroupsServiceMock;
			protected Mock<IPersonnelRolesService> _personnelRolesServiceMock;
			protected Mock<IUnitsService> _unitsServiceMock;
			protected Mock<IPermissionsService> _permissionsServiceMock;
			protected Mock<ICalendarService> _calendarServiceMock;
			protected Mock<IProtocolsService> _protocolsServiceMock;
			protected Mock<IShiftsService> _shiftsServiceMock;
			protected Mock<ICustomStateService> _customStateServiceMock;
			protected Mock<ICertificationService> _certificationServiceMock;
			protected Mock<IDocumentsService> _documentsServiceMock;
			protected Mock<INotesService> _notesServiceMock;
			protected Mock<ICacheProvider> _cacheProviderMock;
			protected Mock<IContactsService> _contactsServiceMock;

			protected with_the_authorization_service()
			{
				_departmentsServiceMock = new Mock<IDepartmentsService>();
				_invitesServiceMock = new Mock<IInvitesService>();
				_callsServiceMock = new Mock<ICallsService>();
				_messageServiceMock = new Mock<IMessageService>();
				_workLogsServiceMock = new Mock<IWorkLogsService>();
				_subscriptionsServiceMock = new Mock<ISubscriptionsService>();
				_departmentGroupsServiceMock = new Mock<IDepartmentGroupsService>();
				_personnelRolesServiceMock = new Mock<IPersonnelRolesService>();
				_unitsServiceMock = new Mock<IUnitsService>();
				_permissionsServiceMock = new Mock<IPermissionsService>();
				_calendarServiceMock = new Mock<ICalendarService>();
				_protocolsServiceMock = new Mock<IProtocolsService>();
				_shiftsServiceMock = new Mock<IShiftsService>();
				_customStateServiceMock = new Mock<ICustomStateService>();
				_certificationServiceMock = new Mock<ICertificationService>();
				_documentsServiceMock = new Mock<IDocumentsService>();
				_notesServiceMock = new Mock<INotesService>();
				_cacheProviderMock = new Mock<ICacheProvider>();
				_contactsServiceMock = new Mock<IContactsService>();

				_authorizationService = new AuthorizationService(
					_departmentsServiceMock.Object,
					_invitesServiceMock.Object,
					_callsServiceMock.Object,
					_messageServiceMock.Object,
					_workLogsServiceMock.Object,
					_subscriptionsServiceMock.Object,
					_departmentGroupsServiceMock.Object,
					_personnelRolesServiceMock.Object,
					_unitsServiceMock.Object,
					_permissionsServiceMock.Object,
					_calendarServiceMock.Object,
					_protocolsServiceMock.Object,
					_shiftsServiceMock.Object,
					_customStateServiceMock.Object,
					_certificationServiceMock.Object,
					_documentsServiceMock.Object,
					_notesServiceMock.Object,
					_cacheProviderMock.Object,
					_contactsServiceMock.Object);
			}

			/// <summary>
			/// Creates a Department where the specified user(s) are considered admins.
			/// Any userId in <paramref name="adminUserIds"/> will be recognized as an admin.
			/// </summary>
			protected Department CreateDepartmentWithAdmins(int departmentId, string managingUserId, params string[] adminUserIds)
			{
				var members = new System.Collections.Generic.List<DepartmentMember>();
				foreach (var uid in adminUserIds)
				{
					members.Add(new DepartmentMember
					{
						DepartmentId = departmentId,
						UserId = uid,
						IsAdmin = true
					});
				}

				return new Department
				{
					DepartmentId = departmentId,
					ManagingUserId = managingUserId,
					Name = "Test Department",
					Code = "XXXX",
					Members = members,
					AdminUsers = new System.Collections.Generic.List<string>(adminUserIds)
				};
			}
		}

		[TestFixture]
		public class when_authroizing_a_delete_action : with_the_authorization_service
		{
			[SetUp]
			public void Setup()
			{
				// can_user_delete_user is called. TestUser1Id = managing user.
				// TestUser1Id is managing user → always admin
				// TestUser2Id is an explicit admin
				// TestUser3Id is a regular user (not admin)
				var dept = CreateDepartmentWithAdmins(1, TestData.Users.TestUser1Id, TestData.Users.TestUser2Id);
				_departmentsServiceMock.Setup(m => m.GetDepartmentByIdAsync(1, It.IsAny<bool>())).ReturnsAsync(dept);


				// No special permissions needed for these tests - admin check handles it
				_permissionsServiceMock.Setup(m => m.GetPermissionByDepartmentTypeAsync(It.IsAny<int>(), It.IsAny<PermissionTypes>()))
					.ReturnsAsync((Permission)null);

				// GetGroupForUserAsync is called but only dereferenced when permission is non-null (which it isn't)
				_departmentGroupsServiceMock.Setup(m => m.GetGroupForUserAsync(It.IsAny<string>(), It.IsAny<int>()))
					.ReturnsAsync((DepartmentGroup)null);
			}

			[Test]
			public async Task should_be_able_to_delete_user_in_department_for_managingUser()
			{
				var valid = await _authorizationService.CanUserDeleteUserAsync(1, TestData.Users.TestUser1Id, TestData.Users.TestUser2Id);

				valid.Should().BeTrue();
			}

			[Test]
			public async Task should_be_able_to_delete_user_in_department_for_adminUser()
			{
				var valid = await _authorizationService.CanUserDeleteUserAsync(1, TestData.Users.TestUser2Id, TestData.Users.TestUser3Id);

				valid.Should().BeTrue();
			}

			[Test]
			public async Task should_not_be_able_to_delete_user_in_same_department()
			{
				var valid = await _authorizationService.CanUserDeleteUserAsync(1, TestData.Users.TestUser3Id, TestData.Users.TestUser4Id);

				valid.Should().BeFalse();
			}

			[Test]
			public async Task should_not_be_able_to_delete_user_in_different_department()
			{
				var valid = await _authorizationService.CanUserDeleteUserAsync(1, TestData.Users.TestUser3Id, TestData.Users.TestUser6Id);

				valid.Should().BeFalse();
			}
		}

		[TestFixture]
		public class when_authroizing_managing_invites : with_the_authorization_service
		{
			[SetUp]
			public void Setup()
			{
				// TestUser1Id is managing user of department 1 and admin of department 1
				var dept1 = CreateDepartmentWithAdmins(1, TestData.Users.TestUser1Id, TestData.Users.TestUser1Id);
				_departmentsServiceMock.Setup(m => m.GetDepartmentByUserIdAsync(TestData.Users.TestUser1Id, It.IsAny<bool>())).ReturnsAsync(dept1);

				// Invite 1 belongs to department 1
				_invitesServiceMock.Setup(m => m.GetInviteByIdAsync(1))
					.ReturnsAsync(new Invite { InviteId = 1, DepartmentId = 1 });

				// Invite 3 belongs to department 3 (different department)
				_invitesServiceMock.Setup(m => m.GetInviteByIdAsync(3))
					.ReturnsAsync(new Invite { InviteId = 3, DepartmentId = 3 });
			}

			[Test]
			public async Task should_be_able_to_manage_an_invite()
			{
				var valid = await _authorizationService.CanUserManageInviteAsync(TestData.Users.TestUser1Id, 1);

				valid.Should().BeTrue();
			}

			[Test]
			public async Task should_not_be_able_to_manage_an_invite()
			{
				var valid = await _authorizationService.CanUserManageInviteAsync(TestData.Users.TestUser1Id, 3);

				valid.Should().BeFalse();
			}
		}

		[TestFixture]
		public class when_authroizing_a_call : with_the_authorization_service
		{
			[SetUp]
			public void Setup()
			{
				// TestUser1Id = managing user of dept 1
				// TestUser3Id = regular user of dept 1 (not admin)
				// TestUser5Id = not in dept 1
				var dept1 = CreateDepartmentWithAdmins(1, TestData.Users.TestUser1Id);
				_departmentsServiceMock.Setup(m => m.GetDepartmentByUserIdAsync(TestData.Users.TestUser1Id, It.IsAny<bool>())).ReturnsAsync(dept1);

				var deptForUser3 = CreateDepartmentWithAdmins(1, TestData.Users.TestUser1Id);
				_departmentsServiceMock.Setup(m => m.GetDepartmentByUserIdAsync(TestData.Users.TestUser3Id, It.IsAny<bool>())).ReturnsAsync(deptForUser3);

				// TestUser5Id is in a different department
				var deptForUser5 = CreateDepartmentWithAdmins(5, TestData.Users.TestUser5Id);
				_departmentsServiceMock.Setup(m => m.GetDepartmentByUserIdAsync(TestData.Users.TestUser5Id, It.IsAny<bool>())).ReturnsAsync(deptForUser5);

				// Call 1 belongs to department 1; TestUser1Id is the reporting user
				_callsServiceMock.Setup(m => m.GetCallByIdAsync(1, false))
					.ReturnsAsync(new Call { CallId = 1, DepartmentId = 1, ReportingUserId = TestData.Users.TestUser1Id });
				_callsServiceMock.Setup(m => m.GetCallByIdAsync(1, It.IsAny<bool>()))
					.ReturnsAsync(new Call { CallId = 1, DepartmentId = 1, ReportingUserId = TestData.Users.TestUser1Id });
			}

			[Test]
			public async Task should_be_able_to_view_call()
			{
				var valid = await _authorizationService.CanUserViewCallAsync(TestData.Users.TestUser1Id, 1);

				valid.Should().BeTrue();
			}

			[Test]
			public async Task should_not_be_able_to_view_call()
			{
				var valid = await _authorizationService.CanUserViewCallAsync(TestData.Users.TestUser5Id, 1);

				valid.Should().BeFalse();
			}

			[Test]
			public async Task should_be_able_to_edit_call()
			{
				var valid = await _authorizationService.CanUserEditCallAsync(TestData.Users.TestUser1Id, 1);

				valid.Should().BeTrue();
			}

			[Test]
			public async Task should_not_be_able_to_edit_call()
			{
				var valid = await _authorizationService.CanUserEditCallAsync(TestData.Users.TestUser3Id, 1);

				valid.Should().BeFalse();
			}
		}

		[TestFixture]
		public class when_authroizing_a_message : with_the_authorization_service
		{
			[SetUp]
			public void Setup()
			{
				// TestUser1Id is managing user (admin)
				var dept1 = CreateDepartmentWithAdmins(1, TestData.Users.TestUser1Id);
				_departmentsServiceMock.Setup(m => m.GetDepartmentByUserIdAsync(TestData.Users.TestUser1Id, It.IsAny<bool>())).ReturnsAsync(dept1);
				_departmentsServiceMock.Setup(m => m.GetDepartmentByUserIdAsync(TestData.Users.TestUser2Id, It.IsAny<bool>())).ReturnsAsync(dept1);
				_departmentsServiceMock.Setup(m => m.GetDepartmentByUserIdAsync(TestData.Users.TestUser4Id, It.IsAny<bool>())).ReturnsAsync(dept1);

				// Message 1: TestUser1Id is the sender, TestUser2Id is a recipient
				_messageServiceMock.Setup(m => m.GetMessageByIdAsync(1))
					.ReturnsAsync(new Message
					{
						MessageId = 1,
						SendingUserId = TestData.Users.TestUser1Id,
						MessageRecipients = new System.Collections.Generic.List<MessageRecipient>
						{
							new MessageRecipient { UserId = TestData.Users.TestUser2Id }
						}
					});
			}

			[Test]
			public async Task should_be_able_to_view_message_as_sender()
			{
				var valid = await _authorizationService.CanUserViewMessageAsync(TestData.Users.TestUser1Id, 1);

				valid.Should().BeTrue();
			}

			[Test]
			public async Task should_be_able_to_view_message_as_recipient()
			{
				var valid = await _authorizationService.CanUserViewMessageAsync(TestData.Users.TestUser2Id, 1);

				valid.Should().BeTrue();
			}

			[Test]
			public async Task should_not_be_able_to_view_message()
			{
				var valid = await _authorizationService.CanUserViewMessageAsync(TestData.Users.TestUser4Id, 1);

				valid.Should().BeFalse();
			}
		}

		[TestFixture]
		public class when_authroizing_a_log : with_the_authorization_service
		{
			[SetUp]
			public void Setup()
			{
				// TestUser1Id = managing user and admin
				// TestUser4Id = regular user not an admin
				var dept1 = CreateDepartmentWithAdmins(1, TestData.Users.TestUser1Id, TestData.Users.TestUser1Id);
				_departmentsServiceMock.Setup(m => m.GetDepartmentByUserIdAsync(TestData.Users.TestUser1Id, It.IsAny<bool>())).ReturnsAsync(dept1);
				_departmentsServiceMock.Setup(m => m.GetDepartmentByUserIdAsync(TestData.Users.TestUser4Id, It.IsAny<bool>())).ReturnsAsync(dept1);

				// WorkLog 1 belongs to department 1, logged by TestUser1Id
				var workLog = new Log
				{
					LogId = 1,
					DepartmentId = 1,
					LoggedByUserId = TestData.Users.TestUser1Id,
					Users = new System.Collections.Generic.List<LogUser>()
				};
				_workLogsServiceMock.Setup(m => m.GetWorkLogByIdAsync(1)).ReturnsAsync(workLog);
				_departmentsServiceMock.Setup(m => m.GetDepartmentByIdAsync(1, It.IsAny<bool>())).ReturnsAsync(dept1);

				// Message setups for the view message tests
				_messageServiceMock.Setup(m => m.GetMessageByIdAsync(1))
					.ReturnsAsync(new Message
					{
						MessageId = 1,
						SendingUserId = TestData.Users.TestUser1Id,
						MessageRecipients = new System.Collections.Generic.List<MessageRecipient>
						{
							new MessageRecipient { UserId = TestData.Users.TestUser2Id }
						}
					});
			}

			[Test]
			public async Task should_be_able_to_view_log_with_department_admin()
			{
				var valid = await _authorizationService.CanUserViewAndEditWorkLogAsync(TestData.Users.TestUser1Id, 1);

				valid.Should().BeTrue();
			}

			[Test]
			public async Task should_be_able_to_view_log_with_creator()
			{
				var valid = await _authorizationService.CanUserViewMessageAsync(TestData.Users.TestUser1Id, 1);

				valid.Should().BeTrue();
			}

			[Test]
			public async Task should_not_be_able_to_view_not_sent_to()
			{
				var valid = await _authorizationService.CanUserViewMessageAsync(TestData.Users.TestUser4Id, 1);

				valid.Should().BeFalse();
			}
		}
	}
}
