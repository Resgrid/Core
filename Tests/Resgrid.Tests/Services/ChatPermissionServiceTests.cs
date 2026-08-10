using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Framework.Testing;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	namespace ChatPermissionServiceTests
	{
		public class with_the_chat_permission_service : TestBase
		{
			protected IChatPermissionService _chatPermissionService;

			protected Mock<IChatChannelMemberRepository> _chatChannelMemberRepositoryMock;
			protected Mock<IChatChannelAccessRuleRepository> _chatChannelAccessRuleRepositoryMock;
			protected Mock<IAuthorizationService> _authorizationServiceMock;
			protected Mock<IDepartmentsService> _departmentsServiceMock;
			protected Mock<IDepartmentGroupsService> _departmentGroupsServiceMock;
			protected Mock<IPersonnelRolesService> _personnelRolesServiceMock;
			protected Mock<IUnitsService> _unitsServiceMock;
			protected Mock<ICallsService> _callsServiceMock;
			protected Mock<IIncidentCommandService> _incidentCommandServiceMock;
			protected Mock<ICacheProvider> _cacheProviderMock;

			protected with_the_chat_permission_service()
			{
				BuildService();
			}

			// Rebuild the mocks before every test so setups from one test never leak into the next
			// (NUnit reuses the fixture instance for every test in the fixture).
			protected override void Before_all_tests()
			{
				BuildService();
			}

			private void BuildService()
			{
				_chatChannelMemberRepositoryMock = new Mock<IChatChannelMemberRepository>();
				_chatChannelAccessRuleRepositoryMock = new Mock<IChatChannelAccessRuleRepository>();
				_authorizationServiceMock = new Mock<IAuthorizationService>();
				_departmentsServiceMock = new Mock<IDepartmentsService>();
				_departmentGroupsServiceMock = new Mock<IDepartmentGroupsService>();
				_personnelRolesServiceMock = new Mock<IPersonnelRolesService>();
				_unitsServiceMock = new Mock<IUnitsService>();
				_callsServiceMock = new Mock<ICallsService>();
				_incidentCommandServiceMock = new Mock<IIncidentCommandService>();
				_cacheProviderMock = new Mock<ICacheProvider>();

				// No cached results so the evaluation logic always runs.
				_cacheProviderMock.Setup(x => x.GetStringAsync(It.IsAny<string>())).ReturnsAsync((string)null);
				_cacheProviderMock.Setup(x => x.SetStringAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(true);

				// Default: nobody is a department admin unless a test says otherwise.
				_authorizationServiceMock.Setup(x => x.CanUserModifyDepartmentAsync(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(false);

				_chatPermissionService = new ChatPermissionService(
					_chatChannelMemberRepositoryMock.Object,
					_chatChannelAccessRuleRepositoryMock.Object,
					_authorizationServiceMock.Object,
					_departmentsServiceMock.Object,
					_departmentGroupsServiceMock.Object,
					_personnelRolesServiceMock.Object,
					_unitsServiceMock.Object,
					_callsServiceMock.Object,
					_incidentCommandServiceMock.Object,
					_cacheProviderMock.Object);
			}

			protected static ChatChannel CreateChannel(ChatChannelType channelType, int departmentId = 1)
			{
				return new ChatChannel
				{
					ChatChannelId = Guid.NewGuid().ToString(),
					DepartmentId = departmentId,
					ChannelType = (int)channelType,
					CreatedOn = DateTime.UtcNow
				};
			}

			protected static ChatChannelMember CreateUserMember(ChatChannel channel, string userId)
			{
				return new ChatChannelMember
				{
					ChatChannelMemberId = Guid.NewGuid().ToString(),
					ChatChannelId = channel.ChatChannelId,
					DepartmentId = channel.DepartmentId,
					ParticipantType = (int)ChatParticipantType.User,
					UserId = userId,
					JoinedOn = DateTime.UtcNow
				};
			}
		}

		[TestFixture]
		public class when_evaluating_channel_access : with_the_chat_permission_service
		{
			[Test]
			public async Task chatbot_owner_should_have_access()
			{
				var channel = CreateChannel(ChatChannelType.Chatbot);
				channel.OwnerUserId = TestData.Users.TestUser1Id;

				var result = await _chatPermissionService.CanAccessChannelAsync(channel, TestData.Users.TestUser1Id, null);

				result.Should().BeTrue();
			}

			[Test]
			public async Task chatbot_non_owner_should_not_have_access()
			{
				var channel = CreateChannel(ChatChannelType.Chatbot);
				channel.OwnerUserId = TestData.Users.TestUser1Id;

				var result = await _chatPermissionService.CanAccessChannelAsync(channel, TestData.Users.TestUser2Id, null);

				result.Should().BeFalse();
			}

			[Test]
			public async Task dm_active_member_should_have_access()
			{
				var channel = CreateChannel(ChatChannelType.DirectMessage);
				var member = CreateUserMember(channel, TestData.Users.TestUser1Id);
				_chatChannelMemberRepositoryMock.Setup(x => x.GetUserMemberAsync(channel.ChatChannelId, TestData.Users.TestUser1Id)).ReturnsAsync(member);

				var result = await _chatPermissionService.CanAccessChannelAsync(channel, TestData.Users.TestUser1Id, null);

				result.Should().BeTrue();
			}

			[Test]
			public async Task dm_removed_member_should_not_have_access()
			{
				var channel = CreateChannel(ChatChannelType.DirectMessage);
				var member = CreateUserMember(channel, TestData.Users.TestUser1Id);
				member.RemovedOn = DateTime.UtcNow;
				_chatChannelMemberRepositoryMock.Setup(x => x.GetUserMemberAsync(channel.ChatChannelId, TestData.Users.TestUser1Id)).ReturnsAsync(member);

				var result = await _chatPermissionService.CanAccessChannelAsync(channel, TestData.Users.TestUser1Id, null);

				result.Should().BeFalse();
			}

			[Test]
			public async Task dm_banned_member_should_not_have_access()
			{
				var channel = CreateChannel(ChatChannelType.DirectMessage);
				var member = CreateUserMember(channel, TestData.Users.TestUser1Id);
				member.IsBanned = true;
				_chatChannelMemberRepositoryMock.Setup(x => x.GetUserMemberAsync(channel.ChatChannelId, TestData.Users.TestUser1Id)).ReturnsAsync(member);

				var result = await _chatPermissionService.CanAccessChannelAsync(channel, TestData.Users.TestUser1Id, null);

				result.Should().BeFalse();
			}

			[Test]
			public async Task dm_non_member_should_not_have_access()
			{
				var channel = CreateChannel(ChatChannelType.DirectMessage);
				_chatChannelMemberRepositoryMock.Setup(x => x.GetUserMemberAsync(channel.ChatChannelId, TestData.Users.TestUser1Id)).ReturnsAsync((ChatChannelMember)null);

				var result = await _chatPermissionService.CanAccessChannelAsync(channel, TestData.Users.TestUser1Id, null);

				result.Should().BeFalse();
			}

			[Test]
			public async Task department_default_department_member_should_have_access()
			{
				var channel = CreateChannel(ChatChannelType.DepartmentDefault);
				_departmentsServiceMock.Setup(x => x.IsUserInDepartmentAsync(1, TestData.Users.TestUser1Id)).ReturnsAsync(true);

				var result = await _chatPermissionService.CanAccessChannelAsync(channel, TestData.Users.TestUser1Id, null);

				result.Should().BeTrue();
			}

			[Test]
			public async Task department_default_non_member_should_not_have_access()
			{
				var channel = CreateChannel(ChatChannelType.DepartmentDefault);
				_departmentsServiceMock.Setup(x => x.IsUserInDepartmentAsync(1, TestData.Users.TestUser1Id)).ReturnsAsync(false);

				var result = await _chatPermissionService.CanAccessChannelAsync(channel, TestData.Users.TestUser1Id, null);

				result.Should().BeFalse();
			}

			[Test]
			public async Task group_default_group_member_should_have_access()
			{
				var channel = CreateChannel(ChatChannelType.GroupDefault);
				channel.GroupId = 9;
				_departmentGroupsServiceMock.Setup(x => x.GetAllMembersForGroupAsync(9)).ReturnsAsync(new List<DepartmentGroupMember>
				{
					new DepartmentGroupMember { DepartmentGroupId = 9, UserId = TestData.Users.TestUser1Id }
				});

				var result = await _chatPermissionService.CanAccessChannelAsync(channel, TestData.Users.TestUser1Id, null);

				result.Should().BeTrue();
			}

			[Test]
			public async Task group_default_non_member_should_not_have_access()
			{
				var channel = CreateChannel(ChatChannelType.GroupDefault);
				channel.GroupId = 9;
				_departmentGroupsServiceMock.Setup(x => x.GetAllMembersForGroupAsync(9)).ReturnsAsync(new List<DepartmentGroupMember>
				{
					new DepartmentGroupMember { DepartmentGroupId = 9, UserId = TestData.Users.TestUser2Id }
				});

				var result = await _chatPermissionService.CanAccessChannelAsync(channel, TestData.Users.TestUser1Id, null);

				result.Should().BeFalse();
			}

			[Test]
			public async Task group_default_department_admin_should_have_access()
			{
				var channel = CreateChannel(ChatChannelType.GroupDefault);
				channel.GroupId = 9;
				_authorizationServiceMock.Setup(x => x.CanUserModifyDepartmentAsync(TestData.Users.TestUser1Id, 1)).ReturnsAsync(true);
				_departmentGroupsServiceMock.Setup(x => x.GetAllMembersForGroupAsync(9)).ReturnsAsync(new List<DepartmentGroupMember>());

				var result = await _chatPermissionService.CanAccessChannelAsync(channel, TestData.Users.TestUser1Id, null);

				result.Should().BeTrue();
			}

			[Test]
			public async Task custom_locked_matching_user_rule_should_have_access()
			{
				var channel = CreateChannel(ChatChannelType.CustomLocked);
				_chatChannelAccessRuleRepositoryMock.Setup(x => x.GetByChannelIdAsync(channel.ChatChannelId)).ReturnsAsync(new List<ChatChannelAccessRule>
				{
					new ChatChannelAccessRule { RuleType = (int)ChatAccessRuleType.User, UserId = TestData.Users.TestUser1Id }
				});

				var result = await _chatPermissionService.CanAccessChannelAsync(channel, TestData.Users.TestUser1Id, null);

				result.Should().BeTrue();
			}

			[Test]
			public async Task custom_locked_matching_role_rule_should_have_access()
			{
				var channel = CreateChannel(ChatChannelType.CustomLocked);
				_chatChannelAccessRuleRepositoryMock.Setup(x => x.GetByChannelIdAsync(channel.ChatChannelId)).ReturnsAsync(new List<ChatChannelAccessRule>
				{
					new ChatChannelAccessRule { RuleType = (int)ChatAccessRuleType.Role, PersonnelRoleId = 5 }
				});
				_personnelRolesServiceMock.Setup(x => x.GetRolesForUserAsync(TestData.Users.TestUser1Id, 1)).ReturnsAsync(new List<PersonnelRole>
				{
					new PersonnelRole { PersonnelRoleId = 5 }
				});

				var result = await _chatPermissionService.CanAccessChannelAsync(channel, TestData.Users.TestUser1Id, null);

				result.Should().BeTrue();
			}

			[Test]
			public async Task custom_locked_matching_group_rule_should_have_access()
			{
				var channel = CreateChannel(ChatChannelType.CustomLocked);
				_chatChannelAccessRuleRepositoryMock.Setup(x => x.GetByChannelIdAsync(channel.ChatChannelId)).ReturnsAsync(new List<ChatChannelAccessRule>
				{
					new ChatChannelAccessRule { RuleType = (int)ChatAccessRuleType.GroupMembership, GroupId = 9 }
				});
				_departmentGroupsServiceMock.Setup(x => x.GetAllMembersForGroupAsync(9)).ReturnsAsync(new List<DepartmentGroupMember>
				{
					new DepartmentGroupMember { DepartmentGroupId = 9, UserId = TestData.Users.TestUser1Id }
				});

				var result = await _chatPermissionService.CanAccessChannelAsync(channel, TestData.Users.TestUser1Id, null);

				result.Should().BeTrue();
			}

			[Test]
			public async Task custom_locked_unmatched_user_should_not_have_access()
			{
				var channel = CreateChannel(ChatChannelType.CustomLocked);
				_chatChannelMemberRepositoryMock.Setup(x => x.GetUserMemberAsync(channel.ChatChannelId, TestData.Users.TestUser1Id)).ReturnsAsync((ChatChannelMember)null);
				_chatChannelAccessRuleRepositoryMock.Setup(x => x.GetByChannelIdAsync(channel.ChatChannelId)).ReturnsAsync(new List<ChatChannelAccessRule>
				{
					new ChatChannelAccessRule { RuleType = (int)ChatAccessRuleType.User, UserId = TestData.Users.TestUser2Id },
					new ChatChannelAccessRule { RuleType = (int)ChatAccessRuleType.Role, PersonnelRoleId = 5 },
					new ChatChannelAccessRule { RuleType = (int)ChatAccessRuleType.GroupMembership, GroupId = 9 }
				});
				_personnelRolesServiceMock.Setup(x => x.GetRolesForUserAsync(TestData.Users.TestUser1Id, 1)).ReturnsAsync(new List<PersonnelRole>
				{
					new PersonnelRole { PersonnelRoleId = 6 }
				});
				_departmentGroupsServiceMock.Setup(x => x.GetAllMembersForGroupAsync(9)).ReturnsAsync(new List<DepartmentGroupMember>
				{
					new DepartmentGroupMember { DepartmentGroupId = 9, UserId = TestData.Users.TestUser2Id }
				});

				var result = await _chatPermissionService.CanAccessChannelAsync(channel, TestData.Users.TestUser1Id, null);

				result.Should().BeFalse();
			}
		}

		[TestFixture]
		public class when_evaluating_incident_channel_access : with_the_chat_permission_service
		{
			[Test]
			public async Task dispatched_user_should_have_access_to_incident_channel()
			{
				var channel = CreateChannel(ChatChannelType.Incident);
				channel.CallId = 42;
				_callsServiceMock.Setup(x => x.GetCallByIdAsync(42, It.IsAny<bool>())).ReturnsAsync(new Call
				{
					CallId = 42,
					DepartmentId = 1,
					Dispatches = new List<CallDispatch> { new CallDispatch { CallId = 42, UserId = TestData.Users.TestUser1Id } }
				});

				var result = await _chatPermissionService.CanAccessChannelAsync(channel, TestData.Users.TestUser1Id, null);

				result.Should().BeTrue();
			}

			[Test]
			public async Task unit_dispatched_user_with_matching_active_unit_should_have_access_to_incident_channel()
			{
				var channel = CreateChannel(ChatChannelType.Incident);
				channel.CallId = 42;
				_callsServiceMock.Setup(x => x.GetCallByIdAsync(42, It.IsAny<bool>())).ReturnsAsync(new Call
				{
					CallId = 42,
					DepartmentId = 1,
					UnitDispatches = new List<CallDispatchUnit> { new CallDispatchUnit { CallId = 42, UnitId = 7 } }
				});
				_unitsServiceMock.Setup(x => x.GetUnitByIdAsync(7)).ReturnsAsync(new Unit { UnitId = 7, DepartmentId = 1, Name = "Engine 6" });
				_unitsServiceMock.Setup(x => x.GetActiveRolesForUnitAsync(7)).ReturnsAsync(new List<UnitActiveRole>
				{
					new UnitActiveRole { UnitId = 7, UserId = TestData.Users.TestUser1Id }
				});

				var result = await _chatPermissionService.CanAccessChannelAsync(channel, TestData.Users.TestUser1Id, 7);

				result.Should().BeTrue();
			}

			[Test]
			public async Task unit_dispatched_user_who_does_not_crew_the_unit_should_not_have_access_to_incident_channel()
			{
				var channel = CreateChannel(ChatChannelType.Incident);
				channel.CallId = 42;
				_callsServiceMock.Setup(x => x.GetCallByIdAsync(42, It.IsAny<bool>())).ReturnsAsync(new Call
				{
					CallId = 42,
					DepartmentId = 1,
					UnitDispatches = new List<CallDispatchUnit> { new CallDispatchUnit { CallId = 42, UnitId = 7 } }
				});
				// User claims unit 7 as their active unit, but crews nothing.
				_unitsServiceMock.Setup(x => x.GetUnitByIdAsync(7)).ReturnsAsync(new Unit { UnitId = 7, DepartmentId = 1, Name = "Engine 6" });
				_unitsServiceMock.Setup(x => x.GetActiveRolesForUnitAsync(7)).ReturnsAsync(new List<UnitActiveRole>
				{
					new UnitActiveRole { UnitId = 7, UserId = TestData.Users.TestUser2Id }
				});
				_incidentCommandServiceMock.Setup(x => x.GetAssignmentsForCallAsync(1, 42)).ReturnsAsync(new List<ResourceAssignment>());

				var result = await _chatPermissionService.CanAccessChannelAsync(channel, TestData.Users.TestUser1Id, 7);

				result.Should().BeFalse();
			}

			[Test]
			public async Task active_incident_role_holder_should_have_access_to_incident_channel()
			{
				var channel = CreateChannel(ChatChannelType.Incident);
				channel.CallId = 42;
				_incidentCommandServiceMock.Setup(x => x.GetIncidentRolesAsync(1, 42)).ReturnsAsync(new List<IncidentRoleAssignment>
				{
					new IncidentRoleAssignment { CallId = 42, UserId = TestData.Users.TestUser1Id }
				});

				var result = await _chatPermissionService.CanAccessChannelAsync(channel, TestData.Users.TestUser1Id, null);

				result.Should().BeTrue();
			}

			[Test]
			public async Task unrelated_user_should_not_have_access_to_incident_channel()
			{
				var channel = CreateChannel(ChatChannelType.Incident);
				channel.CallId = 42;
				_callsServiceMock.Setup(x => x.GetCallByIdAsync(42, It.IsAny<bool>())).ReturnsAsync(new Call
				{
					CallId = 42,
					DepartmentId = 1,
					Dispatches = new List<CallDispatch> { new CallDispatch { CallId = 42, UserId = TestData.Users.TestUser2Id } },
					GroupDispatches = new List<CallDispatchGroup>(),
					RoleDispatches = new List<CallDispatchRole>(),
					UnitDispatches = new List<CallDispatchUnit>()
				});
				_incidentCommandServiceMock.Setup(x => x.GetAssignmentsForCallAsync(1, 42)).ReturnsAsync(new List<ResourceAssignment>());

				var result = await _chatPermissionService.CanAccessChannelAsync(channel, TestData.Users.TestUser1Id, null);

				result.Should().BeFalse();
			}

			[Test]
			public async Task lane_assigned_personnel_should_have_access_to_lane_channel()
			{
				var channel = CreateChannel(ChatChannelType.IncidentLane);
				channel.CallId = 42;
				channel.CommandStructureNodeId = "node-1";
				_incidentCommandServiceMock.Setup(x => x.GetNodesForCallAsync(1, 42)).ReturnsAsync(new List<CommandStructureNode>
				{
					new CommandStructureNode { CommandStructureNodeId = "node-1", DepartmentId = 1, CallId = 42, SupervisorUserId = TestData.Users.TestUser2Id }
				});
				_incidentCommandServiceMock.Setup(x => x.GetAssignmentsForCallAsync(1, 42)).ReturnsAsync(new List<ResourceAssignment>
				{
					new ResourceAssignment
					{
						CommandStructureNodeId = "node-1",
						ResourceKind = (int)ResourceAssignmentKind.RealPersonnel,
						ResourceId = TestData.Users.TestUser1Id
					}
				});

				var result = await _chatPermissionService.CanAccessChannelAsync(channel, TestData.Users.TestUser1Id, null);

				result.Should().BeTrue();
			}

			[Test]
			public async Task lane_supervisor_should_have_access_to_lane_channel()
			{
				var channel = CreateChannel(ChatChannelType.IncidentLane);
				channel.CallId = 42;
				channel.CommandStructureNodeId = "node-1";
				_incidentCommandServiceMock.Setup(x => x.GetNodesForCallAsync(1, 42)).ReturnsAsync(new List<CommandStructureNode>
				{
					new CommandStructureNode { CommandStructureNodeId = "node-1", DepartmentId = 1, CallId = 42, SupervisorUserId = TestData.Users.TestUser1Id }
				});

				var result = await _chatPermissionService.CanAccessChannelAsync(channel, TestData.Users.TestUser1Id, null);

				result.Should().BeTrue();
			}

			[Test]
			public async Task dispatched_user_without_lane_assignment_should_not_have_access_to_lane_channel()
			{
				var channel = CreateChannel(ChatChannelType.IncidentLane);
				channel.CallId = 42;
				channel.CommandStructureNodeId = "node-1";

				// User is dispatched to the call, but lane channels only admit lane resources, leads and command staff.
				_callsServiceMock.Setup(x => x.GetCallByIdAsync(42, It.IsAny<bool>())).ReturnsAsync(new Call
				{
					CallId = 42,
					DepartmentId = 1,
					Dispatches = new List<CallDispatch> { new CallDispatch { CallId = 42, UserId = TestData.Users.TestUser1Id } }
				});
				_incidentCommandServiceMock.Setup(x => x.GetNodesForCallAsync(1, 42)).ReturnsAsync(new List<CommandStructureNode>
				{
					new CommandStructureNode { CommandStructureNodeId = "node-1", DepartmentId = 1, CallId = 42, SupervisorUserId = TestData.Users.TestUser2Id }
				});
				_incidentCommandServiceMock.Setup(x => x.GetAssignmentsForCallAsync(1, 42)).ReturnsAsync(new List<ResourceAssignment>
				{
					new ResourceAssignment
					{
						CommandStructureNodeId = "node-1",
						ResourceKind = (int)ResourceAssignmentKind.RealPersonnel,
						ResourceId = TestData.Users.TestUser2Id
					}
				});

				var result = await _chatPermissionService.CanAccessChannelAsync(channel, TestData.Users.TestUser1Id, null);

				result.Should().BeFalse();
			}

			[Test]
			public async Task command_staff_should_have_access_to_lane_channel()
			{
				var channel = CreateChannel(ChatChannelType.IncidentLane);
				channel.CallId = 42;
				channel.CommandStructureNodeId = "node-1";
				_incidentCommandServiceMock.Setup(x => x.GetIncidentRolesAsync(1, 42)).ReturnsAsync(new List<IncidentRoleAssignment>
				{
					new IncidentRoleAssignment { CallId = 42, UserId = TestData.Users.TestUser1Id }
				});

				var result = await _chatPermissionService.CanAccessChannelAsync(channel, TestData.Users.TestUser1Id, null);

				result.Should().BeTrue();
			}

			[Test]
			public async Task current_commander_should_have_access_to_command_channel()
			{
				var channel = CreateChannel(ChatChannelType.IncidentCommand);
				channel.CallId = 42;
				_incidentCommandServiceMock.Setup(x => x.GetCommandForCallAsync(1, 42)).ReturnsAsync(new IncidentCommand
				{
					CallId = 42,
					DepartmentId = 1,
					CurrentCommanderUserId = TestData.Users.TestUser1Id,
					EstablishedByUserId = TestData.Users.TestUser2Id
				});

				var result = await _chatPermissionService.CanAccessChannelAsync(channel, TestData.Users.TestUser1Id, null);

				result.Should().BeTrue();
			}

			[Test]
			public async Task active_role_holder_should_have_access_to_command_channel()
			{
				var channel = CreateChannel(ChatChannelType.IncidentCommand);
				channel.CallId = 42;
				_incidentCommandServiceMock.Setup(x => x.GetCommandForCallAsync(1, 42)).ReturnsAsync(new IncidentCommand
				{
					CallId = 42,
					DepartmentId = 1,
					CurrentCommanderUserId = TestData.Users.TestUser2Id,
					EstablishedByUserId = TestData.Users.TestUser2Id
				});
				_incidentCommandServiceMock.Setup(x => x.GetIncidentRolesAsync(1, 42)).ReturnsAsync(new List<IncidentRoleAssignment>
				{
					new IncidentRoleAssignment { CallId = 42, UserId = TestData.Users.TestUser1Id }
				});

				var result = await _chatPermissionService.CanAccessChannelAsync(channel, TestData.Users.TestUser1Id, null);

				result.Should().BeTrue();
			}

			[Test]
			public async Task dispatched_user_who_is_not_command_staff_should_not_have_access_to_command_channel()
			{
				var channel = CreateChannel(ChatChannelType.IncidentCommand);
				channel.CallId = 42;
				_callsServiceMock.Setup(x => x.GetCallByIdAsync(42, It.IsAny<bool>())).ReturnsAsync(new Call
				{
					CallId = 42,
					DepartmentId = 1,
					Dispatches = new List<CallDispatch> { new CallDispatch { CallId = 42, UserId = TestData.Users.TestUser1Id } }
				});
				_incidentCommandServiceMock.Setup(x => x.GetCommandForCallAsync(1, 42)).ReturnsAsync(new IncidentCommand
				{
					CallId = 42,
					DepartmentId = 1,
					CurrentCommanderUserId = TestData.Users.TestUser2Id,
					EstablishedByUserId = TestData.Users.TestUser2Id
				});
				_incidentCommandServiceMock.Setup(x => x.GetIncidentRolesAsync(1, 42)).ReturnsAsync(new List<IncidentRoleAssignment>
				{
					new IncidentRoleAssignment { CallId = 42, UserId = TestData.Users.TestUser1Id, RemovedOn = DateTime.UtcNow }
				});

				var result = await _chatPermissionService.CanAccessChannelAsync(channel, TestData.Users.TestUser1Id, null);

				result.Should().BeFalse();
			}
		}

		[TestFixture]
		public class when_evaluating_posting : with_the_chat_permission_service
		{
			[Test]
			public async Task archived_channel_should_block_posting_even_for_accessible_member()
			{
				var channel = CreateChannel(ChatChannelType.DirectMessage);
				channel.IsArchived = true;
				var member = CreateUserMember(channel, TestData.Users.TestUser1Id);
				_chatChannelMemberRepositoryMock.Setup(x => x.GetUserMemberAsync(channel.ChatChannelId, TestData.Users.TestUser1Id)).ReturnsAsync(member);

				var result = await _chatPermissionService.CanPostAsync(channel, TestData.Users.TestUser1Id, null);

				result.Should().BeFalse();
			}

			[Test]
			public async Task locked_channel_should_block_non_moderator()
			{
				var channel = CreateChannel(ChatChannelType.DirectMessage);
				channel.IsLocked = true;
				var member = CreateUserMember(channel, TestData.Users.TestUser1Id);
				_chatChannelMemberRepositoryMock.Setup(x => x.GetUserMemberAsync(channel.ChatChannelId, TestData.Users.TestUser1Id)).ReturnsAsync(member);

				var result = await _chatPermissionService.CanPostAsync(channel, TestData.Users.TestUser1Id, null);

				result.Should().BeFalse();
			}

			[Test]
			public async Task locked_channel_should_allow_department_admin()
			{
				var channel = CreateChannel(ChatChannelType.DirectMessage);
				channel.IsLocked = true;
				var member = CreateUserMember(channel, TestData.Users.TestUser1Id);
				_chatChannelMemberRepositoryMock.Setup(x => x.GetUserMemberAsync(channel.ChatChannelId, TestData.Users.TestUser1Id)).ReturnsAsync(member);
				_authorizationServiceMock.Setup(x => x.CanUserModifyDepartmentAsync(TestData.Users.TestUser1Id, 1)).ReturnsAsync(true);

				var result = await _chatPermissionService.CanPostAsync(channel, TestData.Users.TestUser1Id, null);

				result.Should().BeTrue();
			}

			[Test]
			public async Task muted_member_should_not_be_able_to_post()
			{
				var channel = CreateChannel(ChatChannelType.DirectMessage);
				var member = CreateUserMember(channel, TestData.Users.TestUser1Id);
				member.MutedUntil = DateTime.UtcNow.AddHours(1);
				_chatChannelMemberRepositoryMock.Setup(x => x.GetUserMemberAsync(channel.ChatChannelId, TestData.Users.TestUser1Id)).ReturnsAsync(member);

				var result = await _chatPermissionService.CanPostAsync(channel, TestData.Users.TestUser1Id, null);

				result.Should().BeFalse();
			}

			[Test]
			public async Task banned_member_should_not_be_able_to_post()
			{
				var channel = CreateChannel(ChatChannelType.DirectMessage);
				var member = CreateUserMember(channel, TestData.Users.TestUser1Id);
				member.IsBanned = true;
				_chatChannelMemberRepositoryMock.Setup(x => x.GetUserMemberAsync(channel.ChatChannelId, TestData.Users.TestUser1Id)).ReturnsAsync(member);

				var result = await _chatPermissionService.CanPostAsync(channel, TestData.Users.TestUser1Id, null);

				result.Should().BeFalse();
			}

			[Test]
			public async Task normal_member_should_be_able_to_post()
			{
				var channel = CreateChannel(ChatChannelType.DirectMessage);
				var member = CreateUserMember(channel, TestData.Users.TestUser1Id);
				_chatChannelMemberRepositoryMock.Setup(x => x.GetUserMemberAsync(channel.ChatChannelId, TestData.Users.TestUser1Id)).ReturnsAsync(member);

				var result = await _chatPermissionService.CanPostAsync(channel, TestData.Users.TestUser1Id, null);

				result.Should().BeTrue();
			}
		}

		[TestFixture]
		public class when_evaluating_moderation : with_the_chat_permission_service
		{
			[Test]
			public async Task department_admin_should_moderate_any_channel()
			{
				var channel = CreateChannel(ChatChannelType.DirectMessage);
				_authorizationServiceMock.Setup(x => x.CanUserModifyDepartmentAsync(TestData.Users.TestUser1Id, 1)).ReturnsAsync(true);

				var result = await _chatPermissionService.CanModerateChannelAsync(channel, TestData.Users.TestUser1Id);

				result.Should().BeTrue();
			}

			[Test]
			public async Task group_admin_should_moderate_group_default_channel()
			{
				var channel = CreateChannel(ChatChannelType.GroupDefault);
				channel.GroupId = 9;
				_departmentGroupsServiceMock.Setup(x => x.GetAllMembersForGroupAsync(9)).ReturnsAsync(new List<DepartmentGroupMember>
				{
					new DepartmentGroupMember { DepartmentGroupId = 9, UserId = TestData.Users.TestUser1Id, IsAdmin = true }
				});

				var result = await _chatPermissionService.CanModerateChannelAsync(channel, TestData.Users.TestUser1Id);

				result.Should().BeTrue();
			}

			[Test]
			public async Task regular_group_member_should_not_moderate_group_default_channel()
			{
				var channel = CreateChannel(ChatChannelType.GroupDefault);
				channel.GroupId = 9;
				_departmentGroupsServiceMock.Setup(x => x.GetAllMembersForGroupAsync(9)).ReturnsAsync(new List<DepartmentGroupMember>
				{
					new DepartmentGroupMember { DepartmentGroupId = 9, UserId = TestData.Users.TestUser1Id, IsAdmin = false }
				});

				var result = await _chatPermissionService.CanModerateChannelAsync(channel, TestData.Users.TestUser1Id);

				result.Should().BeFalse();
			}

			[Test]
			public async Task member_row_moderator_should_moderate_channel()
			{
				var channel = CreateChannel(ChatChannelType.DirectMessage);
				var member = CreateUserMember(channel, TestData.Users.TestUser1Id);
				member.IsModerator = true;
				_chatChannelMemberRepositoryMock.Setup(x => x.GetUserMemberAsync(channel.ChatChannelId, TestData.Users.TestUser1Id)).ReturnsAsync(member);

				var result = await _chatPermissionService.CanModerateChannelAsync(channel, TestData.Users.TestUser1Id);

				result.Should().BeTrue();
			}

			[Test]
			public async Task current_incident_commander_should_moderate_incident_channel()
			{
				var channel = CreateChannel(ChatChannelType.Incident);
				channel.CallId = 42;
				_incidentCommandServiceMock.Setup(x => x.GetCommandForCallAsync(1, 42)).ReturnsAsync(new IncidentCommand
				{
					CallId = 42,
					DepartmentId = 1,
					CurrentCommanderUserId = TestData.Users.TestUser1Id
				});

				var result = await _chatPermissionService.CanModerateChannelAsync(channel, TestData.Users.TestUser1Id);

				result.Should().BeTrue();
			}
		}

		[TestFixture]
		public class when_evaluating_ic_sending : with_the_chat_permission_service
		{
			[Test]
			public async Task current_commander_should_send_as_ic()
			{
				_incidentCommandServiceMock.Setup(x => x.GetCommandForCallAsync(1, 42)).ReturnsAsync(new IncidentCommand
				{
					CallId = 42,
					DepartmentId = 1,
					CurrentCommanderUserId = TestData.Users.TestUser1Id
				});

				var result = await _chatPermissionService.CanSendAsIcAsync(TestData.Users.TestUser1Id, 42, 1);

				result.Should().BeTrue();
			}

			[Test]
			public async Task active_role_holder_should_send_as_ic()
			{
				_incidentCommandServiceMock.Setup(x => x.GetCommandForCallAsync(1, 42)).ReturnsAsync(new IncidentCommand
				{
					CallId = 42,
					DepartmentId = 1,
					CurrentCommanderUserId = TestData.Users.TestUser2Id,
					EstablishedByUserId = TestData.Users.TestUser2Id
				});
				_incidentCommandServiceMock.Setup(x => x.GetIncidentRolesAsync(1, 42)).ReturnsAsync(new List<IncidentRoleAssignment>
				{
					new IncidentRoleAssignment { CallId = 42, UserId = TestData.Users.TestUser1Id }
				});

				var result = await _chatPermissionService.CanSendAsIcAsync(TestData.Users.TestUser1Id, 42, 1);

				result.Should().BeTrue();
			}

			[Test]
			public async Task removed_role_holder_should_not_send_as_ic()
			{
				_incidentCommandServiceMock.Setup(x => x.GetCommandForCallAsync(1, 42)).ReturnsAsync(new IncidentCommand
				{
					CallId = 42,
					DepartmentId = 1,
					CurrentCommanderUserId = TestData.Users.TestUser2Id,
					EstablishedByUserId = TestData.Users.TestUser2Id
				});
				_incidentCommandServiceMock.Setup(x => x.GetIncidentRolesAsync(1, 42)).ReturnsAsync(new List<IncidentRoleAssignment>
				{
					new IncidentRoleAssignment { CallId = 42, UserId = TestData.Users.TestUser1Id, RemovedOn = DateTime.UtcNow }
				});

				var result = await _chatPermissionService.CanSendAsIcAsync(TestData.Users.TestUser1Id, 42, 1);

				result.Should().BeFalse();
			}

			[Test]
			public async Task user_with_no_established_command_should_not_send_as_ic()
			{
				_incidentCommandServiceMock.Setup(x => x.GetCommandForCallAsync(1, 42)).ReturnsAsync((IncidentCommand)null);

				var result = await _chatPermissionService.CanSendAsIcAsync(TestData.Users.TestUser1Id, 42, 1);

				result.Should().BeFalse();
			}
		}

		[TestFixture]
		public class when_evaluating_unit_sending : with_the_chat_permission_service
		{
			[Test]
			public async Task active_crew_member_should_send_as_unit()
			{
				_unitsServiceMock.Setup(x => x.GetUnitByIdAsync(7)).ReturnsAsync(new Unit { UnitId = 7, DepartmentId = 1, Name = "Engine 6" });
				_unitsServiceMock.Setup(x => x.GetActiveRolesForUnitAsync(7)).ReturnsAsync(new List<UnitActiveRole>
				{
					new UnitActiveRole { UnitId = 7, UserId = TestData.Users.TestUser1Id }
				});

				var result = await _chatPermissionService.CanSendAsUnitAsync(TestData.Users.TestUser1Id, 7, 1);

				result.Should().BeTrue();
			}

			[Test]
			public async Task department_member_who_does_not_crew_the_unit_should_not_send_as_unit()
			{
				_departmentsServiceMock.Setup(x => x.IsUserInDepartmentAsync(1, TestData.Users.TestUser1Id)).ReturnsAsync(true);
				_unitsServiceMock.Setup(x => x.GetUnitByIdAsync(7)).ReturnsAsync(new Unit { UnitId = 7, DepartmentId = 1, Name = "Engine 6" });
				_unitsServiceMock.Setup(x => x.GetActiveRolesForUnitAsync(7)).ReturnsAsync(new List<UnitActiveRole>
				{
					new UnitActiveRole { UnitId = 7, UserId = TestData.Users.TestUser2Id }
				});

				var result = await _chatPermissionService.CanSendAsUnitAsync(TestData.Users.TestUser1Id, 7, 1);

				result.Should().BeFalse();
			}

			[Test]
			public async Task unit_from_another_department_should_not_send_as_unit()
			{
				_unitsServiceMock.Setup(x => x.GetUnitByIdAsync(7)).ReturnsAsync(new Unit { UnitId = 7, DepartmentId = 2, Name = "Engine 6" });

				var result = await _chatPermissionService.CanSendAsUnitAsync(TestData.Users.TestUser1Id, 7, 1);

				result.Should().BeFalse();
			}
		}

		[TestFixture]
		public class when_resolving_audience : with_the_chat_permission_service
		{
			[Test]
			public async Task dm_with_unit_member_should_expand_to_unit_crew()
			{
				var channel = CreateChannel(ChatChannelType.DirectMessage);
				var userMember = CreateUserMember(channel, TestData.Users.TestUser1Id);
				var unitMember = new ChatChannelMember
				{
					ChatChannelMemberId = Guid.NewGuid().ToString(),
					ChatChannelId = channel.ChatChannelId,
					DepartmentId = channel.DepartmentId,
					ParticipantType = (int)ChatParticipantType.Unit,
					UnitId = 7,
					JoinedOn = DateTime.UtcNow
				};
				_chatChannelMemberRepositoryMock.Setup(x => x.GetByChannelIdAsync(channel.ChatChannelId)).ReturnsAsync(new List<ChatChannelMember> { userMember, unitMember });
				_unitsServiceMock.Setup(x => x.GetActiveRolesForUnitAsync(7)).ReturnsAsync(new List<UnitActiveRole>
				{
					new UnitActiveRole { UnitId = 7, UserId = TestData.Users.TestUser2Id },
					new UnitActiveRole { UnitId = 7, UserId = TestData.Users.TestUser3Id }
				});

				var audience = await _chatPermissionService.ResolveChannelAudienceUserIdsAsync(channel);

				audience.Should().BeEquivalentTo(new[] { TestData.Users.TestUser1Id, TestData.Users.TestUser2Id, TestData.Users.TestUser3Id });
			}

			[Test]
			public async Task department_default_should_only_include_active_non_deleted_members()
			{
				var channel = CreateChannel(ChatChannelType.DepartmentDefault);
				_departmentsServiceMock.Setup(x => x.GetAllMembersForDepartmentAsync(1)).ReturnsAsync(new List<DepartmentMember>
				{
					new DepartmentMember { DepartmentId = 1, UserId = TestData.Users.TestUser1Id },
					new DepartmentMember { DepartmentId = 1, UserId = TestData.Users.TestUser2Id, IsDisabled = true },
					new DepartmentMember { DepartmentId = 1, UserId = TestData.Users.TestUser3Id, IsDeleted = true },
					new DepartmentMember { DepartmentId = 1, UserId = TestData.Users.TestUser4Id, IsDisabled = false }
				});

				var audience = await _chatPermissionService.ResolveChannelAudienceUserIdsAsync(channel);

				audience.Should().BeEquivalentTo(new[] { TestData.Users.TestUser1Id, TestData.Users.TestUser4Id });
			}

			[Test]
			public async Task incident_command_should_include_commander_and_role_holders_without_duplicates()
			{
				var channel = CreateChannel(ChatChannelType.IncidentCommand);
				channel.CallId = 42;
				_incidentCommandServiceMock.Setup(x => x.GetCommandForCallAsync(1, 42)).ReturnsAsync(new IncidentCommand
				{
					CallId = 42,
					DepartmentId = 1,
					CurrentCommanderUserId = TestData.Users.TestUser1Id,
					EstablishedByUserId = TestData.Users.TestUser2Id
				});
				_incidentCommandServiceMock.Setup(x => x.GetIncidentRolesAsync(1, 42)).ReturnsAsync(new List<IncidentRoleAssignment>
				{
					new IncidentRoleAssignment { CallId = 42, UserId = TestData.Users.TestUser1Id },
					new IncidentRoleAssignment { CallId = 42, UserId = TestData.Users.TestUser3Id },
					new IncidentRoleAssignment { CallId = 42, UserId = TestData.Users.TestUser4Id, RemovedOn = DateTime.UtcNow }
				});

				var audience = await _chatPermissionService.ResolveChannelAudienceUserIdsAsync(channel);

				audience.Should().BeEquivalentTo(new[] { TestData.Users.TestUser1Id, TestData.Users.TestUser2Id, TestData.Users.TestUser3Id });
			}
		}

		/// <summary>
		/// The "All Leads" channel: the Incident Commander talking to the people running the lanes, and
		/// nobody else. Membership is derived from the board on every check, so promoting or demoting a
		/// lead changes access with no membership bookkeeping.
		/// </summary>
		[TestFixture]
		public class when_evaluating_the_all_leads_channel : with_the_chat_permission_service
		{
			private static ChatChannel BuildLeadsChannel()
			{
				var channel = CreateChannel(ChatChannelType.IncidentLeads);
				channel.CallId = 42;
				return channel;
			}

			private void GivenCommand(string commanderUserId)
			{
				_incidentCommandServiceMock.Setup(x => x.GetCommandForCallAsync(1, 42)).ReturnsAsync(new IncidentCommand
				{
					CallId = 42,
					DepartmentId = 1,
					CurrentCommanderUserId = commanderUserId,
					EstablishedByUserId = commanderUserId
				});
			}

			private void GivenLanes(params CommandStructureNode[] nodes)
			{
				_incidentCommandServiceMock.Setup(x => x.GetNodesForCallAsync(1, 42)).ReturnsAsync(new List<CommandStructureNode>(nodes));
			}

			[Test]
			public async Task the_incident_commander_should_have_access()
			{
				GivenCommand(TestData.Users.TestUser1Id);
				GivenLanes();

				var result = await _chatPermissionService.CanAccessChannelAsync(BuildLeadsChannel(), TestData.Users.TestUser1Id, null);

				result.Should().BeTrue();
			}

			[TestCase(true)]
			[TestCase(false)]
			public async Task a_lane_lead_should_have_access(bool isPrimary)
			{
				GivenCommand(TestData.Users.TestUser1Id);
				GivenLanes(new CommandStructureNode
				{
					CommandStructureNodeId = "node-1",
					CallId = 42,
					DepartmentId = 1,
					PrimaryLeadUserId = isPrimary ? TestData.Users.TestUser2Id : null,
					SecondaryLeadUserId = isPrimary ? null : TestData.Users.TestUser2Id
				});

				var result = await _chatPermissionService.CanAccessChannelAsync(BuildLeadsChannel(), TestData.Users.TestUser2Id, null);

				result.Should().BeTrue();
			}

			[Test]
			public async Task a_lead_who_has_been_replaced_should_lose_access()
			{
				GivenCommand(TestData.Users.TestUser1Id);
				// TestUser3 used to lead this lane; the board now shows TestUser2.
				GivenLanes(new CommandStructureNode
				{
					CommandStructureNodeId = "node-1",
					CallId = 42,
					DepartmentId = 1,
					PrimaryLeadUserId = TestData.Users.TestUser2Id
				});

				var result = await _chatPermissionService.CanAccessChannelAsync(BuildLeadsChannel(), TestData.Users.TestUser3Id, null);

				result.Should().BeFalse();
			}

			[Test]
			public async Task a_lead_on_a_deleted_lane_should_lose_access()
			{
				GivenCommand(TestData.Users.TestUser1Id);
				GivenLanes(new CommandStructureNode
				{
					CommandStructureNodeId = "node-1",
					CallId = 42,
					DepartmentId = 1,
					PrimaryLeadUserId = TestData.Users.TestUser2Id,
					DeletedOn = DateTime.UtcNow
				});

				var result = await _chatPermissionService.CanAccessChannelAsync(BuildLeadsChannel(), TestData.Users.TestUser2Id, null);

				result.Should().BeFalse();
			}

			[Test]
			public async Task an_ics_role_holder_who_leads_no_lane_should_not_have_access()
			{
				GivenCommand(TestData.Users.TestUser1Id);
				GivenLanes();
				_incidentCommandServiceMock.Setup(x => x.GetIncidentRolesAsync(1, 42)).ReturnsAsync(new List<IncidentRoleAssignment>
				{
					new IncidentRoleAssignment { CallId = 42, UserId = TestData.Users.TestUser3Id }
				});

				// A Safety Officer belongs in the Command channel, not the leads channel.
				var result = await _chatPermissionService.CanAccessChannelAsync(BuildLeadsChannel(), TestData.Users.TestUser3Id, null);

				result.Should().BeFalse();
			}

			[Test]
			public async Task the_audience_should_be_the_commander_and_every_lane_lead()
			{
				GivenCommand(TestData.Users.TestUser1Id);
				GivenLanes(
					new CommandStructureNode { CommandStructureNodeId = "node-1", CallId = 42, DepartmentId = 1, PrimaryLeadUserId = TestData.Users.TestUser2Id },
					new CommandStructureNode { CommandStructureNodeId = "node-2", CallId = 42, DepartmentId = 1, SecondaryLeadUserId = TestData.Users.TestUser3Id });

				var audience = await _chatPermissionService.ResolveChannelAudienceUserIdsAsync(BuildLeadsChannel());

				audience.Should().BeEquivalentTo(new[] { TestData.Users.TestUser1Id, TestData.Users.TestUser2Id, TestData.Users.TestUser3Id });
			}
		}
	}
}
