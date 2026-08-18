using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Framework.Testing;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	namespace ChatCommanderLineTests
	{
		/// <summary>
		/// The IncidentCommanderLine channel is addressed to the command ROLE rather than to a person, so
		/// the behaviour worth pinning is what happens as command changes hands: the conversation and its
		/// history stay on the incident, the incoming commander picks them up, and the outgoing one loses
		/// them — all without touching a membership row.
		/// </summary>
		public class with_the_commander_line : TestBase
		{
			protected const int DepartmentId = 1;
			protected const int CallId = 42;
			protected const string CommandId = "command-1";

			protected IChatChannelService _chatChannelService;
			protected IChatPermissionService _chatPermissionService;

			protected Mock<IChatChannelRepository> _channelRepositoryMock;
			protected Mock<IChatChannelMemberRepository> _memberRepositoryMock;
			protected Mock<IIncidentCommandService> _incidentCommandServiceMock;
			protected Mock<IUnitsService> _unitsServiceMock;
			protected Mock<IUserProfileService> _userProfileServiceMock;
			protected Mock<ICallsService> _callsServiceMock;
			protected Mock<IDispatchAccessService> _dispatchAccessServiceMock;
			protected Mock<IAuthorizationService> _authorizationServiceMock;

			protected with_the_commander_line()
			{
				BuildServices();
			}

			protected override void Before_all_tests()
			{
				BuildServices();
			}

			private void BuildServices()
			{
				_channelRepositoryMock = new Mock<IChatChannelRepository>();
				_memberRepositoryMock = new Mock<IChatChannelMemberRepository>();
				_incidentCommandServiceMock = new Mock<IIncidentCommandService>();
				_unitsServiceMock = new Mock<IUnitsService>();
				_userProfileServiceMock = new Mock<IUserProfileService>();
				_callsServiceMock = new Mock<ICallsService>();
				_dispatchAccessServiceMock = new Mock<IDispatchAccessService>();
				_authorizationServiceMock = new Mock<IAuthorizationService>();

				var cacheProviderMock = new Mock<ICacheProvider>();
				cacheProviderMock.Setup(x => x.GetStringAsync(It.IsAny<string>())).ReturnsAsync((string)null);
				cacheProviderMock.Setup(x => x.SetStringAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>())).ReturnsAsync(true);

				_authorizationServiceMock.Setup(x => x.CanUserModifyDepartmentAsync(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(false);
				_dispatchAccessServiceMock.Setup(x => x.CanUseDispatchAsync(It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync(false);
				_dispatchAccessServiceMock.Setup(x => x.GetDispatchUserIdsAsync(It.IsAny<int>())).ReturnsAsync(new List<string>());

				// The atomic channel+members insert echoes the channel back, and the member rows stay
				// inspectable through the callback argument.
				_channelRepositoryMock
					.Setup(x => x.CreateDirectMessageChannelAsync(It.IsAny<ChatChannel>(), It.IsAny<IEnumerable<ChatChannelMember>>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((ChatChannel c, IEnumerable<ChatChannelMember> m, CancellationToken t) => c);

				_callsServiceMock.Setup(x => x.GetCallByIdAsync(CallId, It.IsAny<bool>())).ReturnsAsync(new Call
				{
					CallId = CallId,
					DepartmentId = DepartmentId,
					Name = "Structure Fire"
				});

				_chatPermissionService = new ChatPermissionService(
					_memberRepositoryMock.Object,
					Mock.Of<IChatChannelAccessRuleRepository>(),
					_authorizationServiceMock.Object,
					Mock.Of<IDepartmentsService>(),
					Mock.Of<IDepartmentGroupsService>(),
					Mock.Of<IPersonnelRolesService>(),
					_unitsServiceMock.Object,
					_callsServiceMock.Object,
					_incidentCommandServiceMock.Object,
					_dispatchAccessServiceMock.Object,
					cacheProviderMock.Object);

				_chatChannelService = new ChatChannelService(
					_channelRepositoryMock.Object,
					_memberRepositoryMock.Object,
					Mock.Of<IChatChannelAccessRuleRepository>(),
					Mock.Of<IChatDepartmentSettingRepository>(),
					Mock.Of<IChatPermissionService>(),
					Mock.Of<IDepartmentsService>(),
					Mock.Of<IDepartmentGroupsService>(),
					_unitsServiceMock.Object,
					_userProfileServiceMock.Object,
					_callsServiceMock.Object,
					Mock.Of<IEventAggregator>(),
					cacheProviderMock.Object,
					Mock.Of<IUnitOfWork>(),
					_incidentCommandServiceMock.Object);
			}

			protected void GivenCommanderIs(string userId)
			{
				_incidentCommandServiceMock.Setup(x => x.GetCommandForCallAsync(DepartmentId, CallId)).ReturnsAsync(new IncidentCommand
				{
					IncidentCommandId = CommandId,
					DepartmentId = DepartmentId,
					CallId = CallId,
					Name = "Structure Fire",
					CurrentCommanderUserId = userId,
					EstablishedByUserId = TestData.Users.TestUser3Id,
					Status = (int)IncidentCommandStatus.Active
				});
			}

			protected void GivenNoCommand()
			{
				_incidentCommandServiceMock.Setup(x => x.GetCommandForCallAsync(DepartmentId, CallId)).ReturnsAsync((IncidentCommand)null);
			}

			protected ChatChannel BuildCommanderLine()
				=> new ChatChannel
				{
					ChatChannelId = "commander-line-1",
					DepartmentId = DepartmentId,
					ChannelType = (int)ChatChannelType.IncidentCommanderLine,
					CallId = CallId,
					IncidentCommandId = CommandId,
					DmKey = $"iccommander:{CallId}|u:{TestData.Users.TestUser1Id.ToLowerInvariant()}"
				};
		}

		[TestFixture]
		public class when_provisioning_a_commander_line : with_the_commander_line
		{
			[Test]
			public async Task no_established_command_should_not_provision_a_line()
			{
				GivenNoCommand();

				var result = await _chatChannelService.EnsureIncidentCommanderLineAsync(DepartmentId, CallId, TestData.Users.TestUser1Id, null);

				result.Should().BeNull("there is no command role to address yet");
				_channelRepositoryMock.Verify(x => x.CreateDirectMessageChannelAsync(It.IsAny<ChatChannel>(), It.IsAny<IEnumerable<ChatChannelMember>>(), It.IsAny<CancellationToken>()), Times.Never);
			}

			[Test]
			public async Task a_command_with_no_current_commander_should_not_provision_a_line()
			{
				GivenCommanderIs(null);

				var result = await _chatChannelService.EnsureIncidentCommanderLineAsync(DepartmentId, CallId, TestData.Users.TestUser1Id, null);

				result.Should().BeNull("the seat is empty even though a command record exists");
			}

			[Test]
			public async Task a_new_line_should_be_anchored_to_the_call_not_the_commander()
			{
				GivenCommanderIs(TestData.Users.TestUser2Id);
				_channelRepositoryMock.Setup(x => x.GetByDmKeyAsync(DepartmentId, It.IsAny<string>())).ReturnsAsync((ChatChannel)null);

				var result = await _chatChannelService.EnsureIncidentCommanderLineAsync(DepartmentId, CallId, TestData.Users.TestUser1Id, null);

				result.Should().NotBeNull();
				result.ChannelType.Should().Be((int)ChatChannelType.IncidentCommanderLine);
				result.CallId.Should().Be(CallId);

				// The key carries the call and the requester and NOT the commander — that is precisely what
				// lets command change hands without forking the conversation.
				result.DmKey.Should().Be($"iccommander:{CallId}|u:{TestData.Users.TestUser1Id.ToLowerInvariant()}");
				result.DmKey.Should().NotContain(TestData.Users.TestUser2Id.ToLowerInvariant());
			}

			[Test]
			public async Task only_the_requester_should_get_a_member_row()
			{
				GivenCommanderIs(TestData.Users.TestUser2Id);
				_channelRepositoryMock.Setup(x => x.GetByDmKeyAsync(DepartmentId, It.IsAny<string>())).ReturnsAsync((ChatChannel)null);

				List<ChatChannelMember> captured = null;
				_channelRepositoryMock
					.Setup(x => x.CreateDirectMessageChannelAsync(It.IsAny<ChatChannel>(), It.IsAny<IEnumerable<ChatChannelMember>>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((ChatChannel c, IEnumerable<ChatChannelMember> m, CancellationToken t) =>
					{
						captured = new List<ChatChannelMember>(m);
						return c;
					});

				await _chatChannelService.EnsureIncidentCommanderLineAsync(DepartmentId, CallId, TestData.Users.TestUser1Id, null);

				captured.Should().HaveCount(1, "the commander side is implicit so the row cannot go stale on transfer");
				captured[0].UserId.Should().Be(TestData.Users.TestUser1Id);
			}

			[Test]
			public async Task a_unit_requester_should_get_a_unit_keyed_line()
			{
				GivenCommanderIs(TestData.Users.TestUser2Id);
				_unitsServiceMock.Setup(x => x.GetUnitByIdAsync(7)).ReturnsAsync(new Unit { UnitId = 7, DepartmentId = DepartmentId, Name = "Engine 6" });
				_channelRepositoryMock.Setup(x => x.GetByDmKeyAsync(DepartmentId, It.IsAny<string>())).ReturnsAsync((ChatChannel)null);

				var result = await _chatChannelService.EnsureIncidentCommanderLineAsync(DepartmentId, CallId, TestData.Users.TestUser1Id, 7);

				result.Should().NotBeNull();
				result.DmKey.Should().Be($"iccommander:{CallId}|unit:7");
			}

			[Test]
			public async Task a_unit_from_another_department_should_be_rejected()
			{
				GivenCommanderIs(TestData.Users.TestUser2Id);
				_unitsServiceMock.Setup(x => x.GetUnitByIdAsync(7)).ReturnsAsync(new Unit { UnitId = 7, DepartmentId = 99, Name = "Engine 6" });

				var act = async () => await _chatChannelService.EnsureIncidentCommanderLineAsync(DepartmentId, CallId, TestData.Users.TestUser1Id, 7);

				await act.Should().ThrowAsync<UnauthorizedAccessException>();
			}

			[Test]
			public async Task an_existing_line_should_be_reused_rather_than_duplicated()
			{
				GivenCommanderIs(TestData.Users.TestUser2Id);
				var existing = BuildCommanderLine();
				existing.Name = "Structure Fire Incident Commander";
				_channelRepositoryMock.Setup(x => x.GetByDmKeyAsync(DepartmentId, existing.DmKey)).ReturnsAsync(existing);

				var result = await _chatChannelService.EnsureIncidentCommanderLineAsync(DepartmentId, CallId, TestData.Users.TestUser1Id, null);

				result.Should().BeSameAs(existing);
				_channelRepositoryMock.Verify(x => x.CreateDirectMessageChannelAsync(It.IsAny<ChatChannel>(), It.IsAny<IEnumerable<ChatChannelMember>>(), It.IsAny<CancellationToken>()), Times.Never);
			}

			[Test]
			public async Task the_same_line_should_be_reused_after_command_changes_hands()
			{
				var existing = BuildCommanderLine();
				_channelRepositoryMock.Setup(x => x.GetByDmKeyAsync(DepartmentId, existing.DmKey)).ReturnsAsync(existing);

				GivenCommanderIs(TestData.Users.TestUser2Id);
				var before = await _chatChannelService.EnsureIncidentCommanderLineAsync(DepartmentId, CallId, TestData.Users.TestUser1Id, null);

				GivenCommanderIs(TestData.Users.TestUser3Id);
				var after = await _chatChannelService.EnsureIncidentCommanderLineAsync(DepartmentId, CallId, TestData.Users.TestUser1Id, null);

				after.ChatChannelId.Should().Be(before.ChatChannelId, "the history has to follow the incident, not the outgoing commander");
			}
		}

		[TestFixture]
		public class when_resolving_commander_line_access : with_the_commander_line
		{
			[Test]
			public async Task the_current_commander_should_have_access_without_a_member_row()
			{
				GivenCommanderIs(TestData.Users.TestUser2Id);
				_memberRepositoryMock.Setup(x => x.GetUserMemberAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync((ChatChannelMember)null);

				var result = await _chatPermissionService.CanAccessChannelAsync(BuildCommanderLine(), TestData.Users.TestUser2Id, null);

				result.Should().BeTrue();
			}

			[Test]
			public async Task an_outgoing_commander_should_lose_access_on_the_next_check()
			{
				_memberRepositoryMock.Setup(x => x.GetUserMemberAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync((ChatChannelMember)null);

				GivenCommanderIs(TestData.Users.TestUser2Id);
				var whileInCommand = await _chatPermissionService.CanAccessChannelAsync(BuildCommanderLine(), TestData.Users.TestUser2Id, null);

				GivenCommanderIs(TestData.Users.TestUser3Id);
				var afterHandover = await _chatPermissionService.CanAccessChannelAsync(BuildCommanderLine(), TestData.Users.TestUser2Id, null);

				whileInCommand.Should().BeTrue();
				afterHandover.Should().BeFalse();
			}

			[Test]
			public async Task the_requester_should_keep_access_across_a_handover()
			{
				GivenCommanderIs(TestData.Users.TestUser3Id);
				_memberRepositoryMock
					.Setup(x => x.GetUserMemberAsync("commander-line-1", TestData.Users.TestUser1Id))
					.ReturnsAsync(new ChatChannelMember
					{
						ChatChannelId = "commander-line-1",
						UserId = TestData.Users.TestUser1Id,
						ParticipantType = (int)ChatParticipantType.User
					});

				var result = await _chatPermissionService.CanAccessChannelAsync(BuildCommanderLine(), TestData.Users.TestUser1Id, null);

				result.Should().BeTrue();
			}

			[Test]
			public async Task an_uninvolved_user_should_be_denied()
			{
				GivenCommanderIs(TestData.Users.TestUser2Id);
				_memberRepositoryMock.Setup(x => x.GetUserMemberAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync((ChatChannelMember)null);

				var result = await _chatPermissionService.CanAccessChannelAsync(BuildCommanderLine(), TestData.Users.TestUser1Id, null);

				result.Should().BeFalse();
			}

			[Test]
			public async Task a_dispatcher_should_not_get_in_on_dispatch_standing_alone()
			{
				GivenCommanderIs(TestData.Users.TestUser2Id);
				_memberRepositoryMock.Setup(x => x.GetUserMemberAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync((ChatChannelMember)null);
				_dispatchAccessServiceMock.Setup(x => x.CanUseDispatchAsync(DepartmentId, TestData.Users.TestUser1Id)).ReturnsAsync(true);

				var result = await _chatPermissionService.CanAccessChannelAsync(BuildCommanderLine(), TestData.Users.TestUser1Id, null);

				result.Should().BeFalse("this is a private line, not incident-wide dispatch traffic");
			}

			[Test]
			public async Task a_department_admin_should_not_get_in_on_admin_standing_alone()
			{
				GivenCommanderIs(TestData.Users.TestUser2Id);
				_memberRepositoryMock.Setup(x => x.GetUserMemberAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync((ChatChannelMember)null);
				_authorizationServiceMock.Setup(x => x.CanUserModifyDepartmentAsync(TestData.Users.TestUser1Id, DepartmentId)).ReturnsAsync(true);

				var result = await _chatPermissionService.CanAccessChannelAsync(BuildCommanderLine(), TestData.Users.TestUser1Id, null);

				result.Should().BeFalse();
			}

			[Test]
			public async Task the_audience_should_be_the_requester_plus_the_current_commander_only()
			{
				GivenCommanderIs(TestData.Users.TestUser2Id);
				_memberRepositoryMock.Setup(x => x.GetByChannelIdAsync("commander-line-1")).ReturnsAsync(new List<ChatChannelMember>
				{
					new ChatChannelMember
					{
						ChatChannelId = "commander-line-1",
						UserId = TestData.Users.TestUser1Id,
						ParticipantType = (int)ChatParticipantType.User
					}
				});

				var audience = await _chatPermissionService.ResolveChannelAudienceUserIdsAsync(BuildCommanderLine());

				audience.Should().BeEquivalentTo(new[] { TestData.Users.TestUser1Id, TestData.Users.TestUser2Id });

				// EstablishedByUserId is TestUser3 — deliberately excluded. Only the seat, not the wider
				// command staff, which is what separates this from the IncidentCommand channel.
				audience.Should().NotContain(TestData.Users.TestUser3Id);
			}
		}
	}
}
