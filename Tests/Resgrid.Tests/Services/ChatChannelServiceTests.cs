using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	namespace ChatChannelServiceTests
	{
		public class with_the_chat_channel_service : TestBase
		{
			protected IChatChannelService _chatChannelService;

			protected Mock<IChatChannelRepository> _chatChannelRepositoryMock;
			protected Mock<IChatChannelMemberRepository> _chatChannelMemberRepositoryMock;
			protected Mock<IChatChannelAccessRuleRepository> _chatChannelAccessRuleRepositoryMock;
			protected Mock<IChatDepartmentSettingRepository> _chatDepartmentSettingRepositoryMock;
			protected Mock<IChatPermissionService> _chatPermissionServiceMock;
			protected Mock<IDepartmentsService> _departmentsServiceMock;
			protected Mock<IDepartmentGroupsService> _departmentGroupsServiceMock;
			protected Mock<IUnitsService> _unitsServiceMock;
			protected Mock<IUserProfileService> _userProfileServiceMock;
			protected Mock<ICallsService> _callsServiceMock;
			protected Mock<IEventAggregator> _eventAggregatorMock;
			protected Mock<ICacheProvider> _cacheProviderMock;
			protected Mock<IUnitOfWork> _unitOfWorkMock;

			protected with_the_chat_channel_service()
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
				_chatChannelRepositoryMock = new Mock<IChatChannelRepository>();
				_chatChannelMemberRepositoryMock = new Mock<IChatChannelMemberRepository>();
				_chatChannelAccessRuleRepositoryMock = new Mock<IChatChannelAccessRuleRepository>();
				_chatDepartmentSettingRepositoryMock = new Mock<IChatDepartmentSettingRepository>();
				_chatPermissionServiceMock = new Mock<IChatPermissionService>();
				_departmentsServiceMock = new Mock<IDepartmentsService>();
				_departmentGroupsServiceMock = new Mock<IDepartmentGroupsService>();
				_unitsServiceMock = new Mock<IUnitsService>();
				_userProfileServiceMock = new Mock<IUserProfileService>();
				_callsServiceMock = new Mock<ICallsService>();
				_eventAggregatorMock = new Mock<IEventAggregator>();
				_cacheProviderMock = new Mock<ICacheProvider>();
				_unitOfWorkMock = new Mock<IUnitOfWork>();

				// Inserts/updates echo back the entity they were handed (repository contract).
				_chatChannelRepositoryMock.Setup(x => x.InsertAsync(It.IsAny<ChatChannel>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((ChatChannel c, CancellationToken t, bool f) => c);
				_chatChannelRepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<ChatChannel>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((ChatChannel c, CancellationToken t, bool f) => c);
				_chatChannelMemberRepositoryMock.Setup(x => x.InsertAsync(It.IsAny<ChatChannelMember>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((ChatChannelMember m, CancellationToken t, bool f) => m);
				_chatChannelMemberRepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<ChatChannelMember>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((ChatChannelMember m, CancellationToken t, bool f) => m);

				// DM creation echoes the channel back; member rows are inspectable via the callback argument.
				_chatChannelRepositoryMock.Setup(x => x.CreateDirectMessageChannelAsync(It.IsAny<ChatChannel>(), It.IsAny<IEnumerable<ChatChannelMember>>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((ChatChannel c, IEnumerable<ChatChannelMember> m, CancellationToken t) => c);

				// Cross-tenant validation passes by default; negative tests override this.
				_departmentsServiceMock.Setup(x => x.IsUserInDepartmentAsync(It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync(true);

				// Batch membership check (ad-hoc group creation): by default every queried id is a member.
				_departmentsServiceMock
					.Setup(x => x.GetMemberUserIdsInDepartmentAsync(It.IsAny<int>(), It.IsAny<IEnumerable<string>>()))
					.ReturnsAsync((int _, IEnumerable<string> ids) => ids == null ? new HashSet<string>() : new HashSet<string>(ids));

				_chatChannelService = new ChatChannelService(
					_chatChannelRepositoryMock.Object,
					_chatChannelMemberRepositoryMock.Object,
					_chatChannelAccessRuleRepositoryMock.Object,
					_chatDepartmentSettingRepositoryMock.Object,
					_chatPermissionServiceMock.Object,
					_departmentsServiceMock.Object,
					_departmentGroupsServiceMock.Object,
					_unitsServiceMock.Object,
					_userProfileServiceMock.Object,
					_callsServiceMock.Object,
					_eventAggregatorMock.Object,
					_cacheProviderMock.Object,
					_unitOfWorkMock.Object);
			}
		}

		[TestFixture]
		public class when_ensuring_department_channels : with_the_chat_channel_service
		{
			[Test]
			public async Task existing_department_channel_should_be_returned_without_insert()
			{
				var existing = new ChatChannel
				{
					ChatChannelId = Guid.NewGuid().ToString(),
					DepartmentId = 1,
					ChannelType = (int)ChatChannelType.DepartmentDefault,
					Name = "First Battalion"
				};
				_chatChannelRepositoryMock.Setup(x => x.GetDepartmentDefaultAsync(1)).ReturnsAsync(existing);

				var result = await _chatChannelService.EnsureDepartmentChannelAsync(1);

				result.Should().BeSameAs(existing);
				_chatChannelRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<ChatChannel>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
			}

			[Test]
			public async Task missing_department_channel_should_be_created_named_after_department()
			{
				_chatChannelRepositoryMock.Setup(x => x.GetDepartmentDefaultAsync(1)).ReturnsAsync((ChatChannel)null);
				_departmentsServiceMock.Setup(x => x.GetDepartmentByIdAsync(1, It.IsAny<bool>())).ReturnsAsync(new Department
				{
					DepartmentId = 1,
					Name = "First Battalion"
				});

				var result = await _chatChannelService.EnsureDepartmentChannelAsync(1);

				result.Should().NotBeNull();
				result.ChannelType.Should().Be((int)ChatChannelType.DepartmentDefault);
				result.DepartmentId.Should().Be(1);
				result.Name.Should().Be("First Battalion");
				_chatChannelRepositoryMock.Verify(x => x.InsertAsync(It.Is<ChatChannel>(c =>
					c.ChannelType == (int)ChatChannelType.DepartmentDefault && c.DepartmentId == 1 && c.Name == "First Battalion"),
					It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
			}
		}

		[TestFixture]
		public class when_ensuring_group_channels : with_the_chat_channel_service
		{
			[Test]
			public async Task existing_group_channel_should_be_returned_without_insert()
			{
				var group = new DepartmentGroup { DepartmentGroupId = 9, DepartmentId = 1, Name = "Station 1" };
				var existing = new ChatChannel
				{
					ChatChannelId = Guid.NewGuid().ToString(),
					DepartmentId = 1,
					ChannelType = (int)ChatChannelType.GroupDefault,
					GroupId = 9,
					Name = "Station 1"
				};
				_chatChannelRepositoryMock.Setup(x => x.GetByGroupIdAsync(9)).ReturnsAsync(existing);

				var result = await _chatChannelService.EnsureGroupChannelAsync(group);

				result.Should().BeSameAs(existing);
				_chatChannelRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<ChatChannel>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
			}
		}

		[TestFixture]
		public class when_ensuring_chatbot_channels : with_the_chat_channel_service
		{
			[Test]
			public async Task missing_chatbot_channel_should_create_channel_with_owner_and_bot_members()
			{
				_chatChannelRepositoryMock.Setup(x => x.GetChatbotChannelAsync(1, "user-a")).ReturnsAsync((ChatChannel)null);
				_chatChannelMemberRepositoryMock.Setup(x => x.GetUserMemberAsync(It.IsAny<string>(), "user-a")).ReturnsAsync((ChatChannelMember)null);

				var result = await _chatChannelService.EnsureChatbotChannelAsync(1, "user-a");

				result.Should().NotBeNull();
				result.ChannelType.Should().Be((int)ChatChannelType.Chatbot);
				result.OwnerUserId.Should().Be("user-a");
				_chatChannelMemberRepositoryMock.Verify(x => x.InsertAsync(It.Is<ChatChannelMember>(m =>
					m.ParticipantType == (int)ChatParticipantType.User && m.UserId == "user-a"),
					It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
				_chatChannelMemberRepositoryMock.Verify(x => x.InsertAsync(It.Is<ChatChannelMember>(m =>
					m.ParticipantType == (int)ChatParticipantType.Bot && m.DisplayNameOverride == "Resgrid Assistant"),
					It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
				_chatChannelMemberRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<ChatChannelMember>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Exactly(2));
			}

			[Test]
			public async Task existing_chatbot_channel_with_owner_member_should_not_insert_members()
			{
				var existing = new ChatChannel
				{
					ChatChannelId = Guid.NewGuid().ToString(),
					DepartmentId = 1,
					ChannelType = (int)ChatChannelType.Chatbot,
					OwnerUserId = "user-a"
				};
				_chatChannelRepositoryMock.Setup(x => x.GetChatbotChannelAsync(1, "user-a")).ReturnsAsync(existing);
				_chatChannelMemberRepositoryMock.Setup(x => x.GetUserMemberAsync(existing.ChatChannelId, "user-a")).ReturnsAsync(new ChatChannelMember
				{
					ChatChannelMemberId = Guid.NewGuid().ToString(),
					ChatChannelId = existing.ChatChannelId,
					ParticipantType = (int)ChatParticipantType.User,
					UserId = "user-a"
				});

				var result = await _chatChannelService.EnsureChatbotChannelAsync(1, "user-a");

				result.Should().BeSameAs(existing);
				_chatChannelRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<ChatChannel>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
				_chatChannelMemberRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<ChatChannelMember>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
			}
		}

		[TestFixture]
		public class when_getting_or_creating_direct_message_channels : with_the_chat_channel_service
		{
			[Test]
			public async Task existing_dm_key_should_return_existing_channel_without_insert()
			{
				var existing = new ChatChannel
				{
					ChatChannelId = Guid.NewGuid().ToString(),
					DepartmentId = 1,
					ChannelType = (int)ChatChannelType.DirectMessage,
					DmKey = "u:user-a|u:user-b"
				};
				_chatChannelRepositoryMock.Setup(x => x.GetByDmKeyAsync(1, It.IsAny<string>())).ReturnsAsync(existing);

				var result = await _chatChannelService.GetOrCreateDirectMessageChannelAsync(1, "user-a", "user-b", null);

				result.Should().BeSameAs(existing);
				_chatChannelRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<ChatChannel>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
				_chatChannelMemberRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<ChatChannelMember>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
			}

			[Test]
			public async Task self_target_dm_should_return_null_without_insert()
			{
				var result = await _chatChannelService.GetOrCreateDirectMessageChannelAsync(1, "user-a", "USER-A", null);

				result.Should().BeNull();
				_chatChannelRepositoryMock.Verify(x => x.GetByDmKeyAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
				_chatChannelRepositoryMock.Verify(x => x.CreateDirectMessageChannelAsync(
					It.IsAny<ChatChannel>(),
					It.IsAny<IEnumerable<ChatChannelMember>>(),
					It.IsAny<CancellationToken>()), Times.Never);
			}

			[Test]
			public async Task dm_key_should_be_sorted_regardless_of_initiator()
			{
				_chatChannelRepositoryMock.Setup(x => x.GetByDmKeyAsync(1, It.IsAny<string>())).ReturnsAsync((ChatChannel)null);

				var first = await _chatChannelService.GetOrCreateDirectMessageChannelAsync(1, "user-a", "user-b", null);
				var second = await _chatChannelService.GetOrCreateDirectMessageChannelAsync(1, "user-b", "user-a", null);

				first.DmKey.Should().Be("u:user-a|u:user-b");
				second.DmKey.Should().Be("u:user-a|u:user-b");
				_chatChannelRepositoryMock.Verify(x => x.GetByDmKeyAsync(1, "u:user-a|u:user-b"), Times.Exactly(2));
			}

			[Test]
			public async Task new_user_dm_should_insert_creator_and_target_member_rows()
			{
				_chatChannelRepositoryMock.Setup(x => x.GetByDmKeyAsync(1, It.IsAny<string>())).ReturnsAsync((ChatChannel)null);

				var result = await _chatChannelService.GetOrCreateDirectMessageChannelAsync(1, "user-a", "user-b", null);

				result.Should().NotBeNull();
				result.ChannelType.Should().Be((int)ChatChannelType.DirectMessage);
				_chatChannelRepositoryMock.Verify(x => x.CreateDirectMessageChannelAsync(
					It.Is<ChatChannel>(c => c.ChannelType == (int)ChatChannelType.DirectMessage),
					It.Is<IEnumerable<ChatChannelMember>>(members =>
						System.Linq.Enumerable.Count(members) == 2 &&
						System.Linq.Enumerable.Any(members, m => m.ParticipantType == (int)ChatParticipantType.User && m.UserId == "user-a") &&
						System.Linq.Enumerable.Any(members, m => m.ParticipantType == (int)ChatParticipantType.User && m.UserId == "user-b")),
					It.IsAny<CancellationToken>()), Times.Once);
			}

			[Test]
			public async Task unit_target_dm_should_insert_unit_member_row_with_unit_name()
			{
				_chatChannelRepositoryMock.Setup(x => x.GetByDmKeyAsync(1, It.IsAny<string>())).ReturnsAsync((ChatChannel)null);
				_unitsServiceMock.Setup(x => x.GetUnitByIdAsync(7)).ReturnsAsync(new Unit { UnitId = 7, DepartmentId = 1, Name = "Engine 6" });

				var result = await _chatChannelService.GetOrCreateDirectMessageChannelAsync(1, "user-a", null, 7);

				result.Should().NotBeNull();
				result.DmKey.Should().Be("u:user-a|unit:7");
				_chatChannelRepositoryMock.Verify(x => x.CreateDirectMessageChannelAsync(
					It.IsAny<ChatChannel>(),
					It.Is<IEnumerable<ChatChannelMember>>(members =>
						System.Linq.Enumerable.Any(members, m => m.ParticipantType == (int)ChatParticipantType.Unit && m.UnitId == 7 && m.DisplayNameOverride == "Engine 6")),
					It.IsAny<CancellationToken>()), Times.Once);
			}

			[Test]
			public void cross_department_target_user_should_be_rejected()
			{
				_chatChannelRepositoryMock.Setup(x => x.GetByDmKeyAsync(1, It.IsAny<string>())).ReturnsAsync((ChatChannel)null);
				_departmentsServiceMock.Setup(x => x.IsUserInDepartmentAsync(1, "outsider")).ReturnsAsync(false);

				Func<Task> act = async () => await _chatChannelService.GetOrCreateDirectMessageChannelAsync(1, "user-a", "outsider", null);

				act.Should().ThrowAsync<UnauthorizedAccessException>();
				_chatChannelRepositoryMock.Verify(x => x.CreateDirectMessageChannelAsync(It.IsAny<ChatChannel>(), It.IsAny<IEnumerable<ChatChannelMember>>(), It.IsAny<CancellationToken>()), Times.Never);
			}

			[Test]
			public void cross_department_target_unit_should_be_rejected()
			{
				_chatChannelRepositoryMock.Setup(x => x.GetByDmKeyAsync(1, It.IsAny<string>())).ReturnsAsync((ChatChannel)null);
				_unitsServiceMock.Setup(x => x.GetUnitByIdAsync(7)).ReturnsAsync(new Unit { UnitId = 7, DepartmentId = 2, Name = "Engine 6" });

				Func<Task> act = async () => await _chatChannelService.GetOrCreateDirectMessageChannelAsync(1, "user-a", null, 7);

				act.Should().ThrowAsync<UnauthorizedAccessException>();
			}
		}

		[TestFixture]
		public class when_archiving_incident_channels : with_the_chat_channel_service
		{
			[Test]
			public async Task archived_channels_should_invalidate_cache_per_channel_and_return_true()
			{
				_chatChannelRepositoryMock.Setup(x => x.SetArchivedByCallIdAsync(42, true, It.IsAny<DateTime?>())).ReturnsAsync(new List<string> { "channel-1", "channel-2" });
				_chatChannelRepositoryMock.Setup(x => x.GetByIdsAsync(It.IsAny<IEnumerable<string>>())).ReturnsAsync(new List<ChatChannel>
				{
					new ChatChannel { ChatChannelId = "channel-1", DepartmentId = 1, ChannelType = (int)ChatChannelType.Incident, CallId = 42 },
					new ChatChannel { ChatChannelId = "channel-2", DepartmentId = 1, ChannelType = (int)ChatChannelType.IncidentLane, CallId = 42 }
				});

				var result = await _chatChannelService.SetIncidentChannelsArchivedAsync(42, true);

				result.Should().BeTrue();
				_chatPermissionServiceMock.Verify(x => x.InvalidateChannelCacheAsync("channel-1"), Times.Once);
				_chatPermissionServiceMock.Verify(x => x.InvalidateChannelCacheAsync("channel-2"), Times.Once);
			}

			[Test]
			public async Task no_affected_channels_should_return_false()
			{
				_chatChannelRepositoryMock.Setup(x => x.SetArchivedByCallIdAsync(42, true, It.IsAny<DateTime?>())).ReturnsAsync(new List<string>());

				var result = await _chatChannelService.SetIncidentChannelsArchivedAsync(42, true);

				result.Should().BeFalse();
				_chatPermissionServiceMock.Verify(x => x.InvalidateChannelCacheAsync(It.IsAny<string>()), Times.Never);
			}
		}

		[TestFixture]
		public class when_setting_notification_preferences : with_the_chat_channel_service
		{
			[Test]
			public async Task missing_member_row_should_be_created_then_preference_updated()
			{
				_chatChannelRepositoryMock.Setup(x => x.GetByIdAsync("channel-1")).ReturnsAsync(new ChatChannel
				{
					ChatChannelId = "channel-1",
					DepartmentId = 1,
					ChannelType = (int)ChatChannelType.DepartmentDefault
				});
				_chatChannelMemberRepositoryMock.Setup(x => x.GetUserMemberAsync("channel-1", "user-a")).ReturnsAsync((ChatChannelMember)null);
				_chatChannelMemberRepositoryMock.Setup(x => x.SetMemberNotificationPreferenceAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

				var result = await _chatChannelService.SetNotificationPreferenceAsync("channel-1", 1, "user-a", ChatNotificationPreference.MentionsOnly);

				result.Should().BeTrue();
				_chatChannelMemberRepositoryMock.Verify(x => x.InsertAsync(It.Is<ChatChannelMember>(m =>
					m.ChatChannelId == "channel-1" && m.UserId == "user-a" && m.ParticipantType == (int)ChatParticipantType.User),
					It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
				_chatChannelMemberRepositoryMock.Verify(x => x.SetMemberNotificationPreferenceAsync(
					It.IsAny<string>(), (int)ChatNotificationPreference.MentionsOnly, It.IsAny<CancellationToken>()), Times.Once);
				_chatChannelMemberRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<ChatChannelMember>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
			}
		}

		[TestFixture]
		public class when_adding_members : with_the_chat_channel_service
		{
			private static ChatChannel CreateChannel(string channelId, ChatChannelType type)
			{
				return new ChatChannel
				{
					ChatChannelId = channelId,
					DepartmentId = 1,
					ChannelType = (int)type,
					CreatedOn = DateTime.UtcNow
				};
			}

			[Test]
			public void direct_message_channel_should_reject_member_adds()
			{
				_chatChannelRepositoryMock.Setup(x => x.GetByIdAsync("dm-1")).ReturnsAsync(CreateChannel("dm-1", ChatChannelType.DirectMessage));

				Func<Task> act = async () => await _chatChannelService.AddMembersAsync("dm-1", new List<string> { "user-b" }, "user-a");

				act.Should().ThrowAsync<InvalidOperationException>();
			}

			[Test]
			public void custom_locked_non_moderator_should_be_rejected()
			{
				var channel = CreateChannel("custom-1", ChatChannelType.CustomLocked);
				_chatChannelRepositoryMock.Setup(x => x.GetByIdAsync("custom-1")).ReturnsAsync(channel);
				_chatPermissionServiceMock.Setup(x => x.CanModerateChannelAsync(channel, "user-a")).ReturnsAsync(false);

				Func<Task> act = async () => await _chatChannelService.AddMembersAsync("custom-1", new List<string> { "user-b" }, "user-a");

				act.Should().ThrowAsync<UnauthorizedAccessException>();
			}

			[Test]
			public async Task custom_locked_moderator_should_add_members()
			{
				var channel = CreateChannel("custom-1", ChatChannelType.CustomLocked);
				_chatChannelRepositoryMock.Setup(x => x.GetByIdAsync("custom-1")).ReturnsAsync(channel);
				_chatPermissionServiceMock.Setup(x => x.CanModerateChannelAsync(channel, "user-a")).ReturnsAsync(true);
				_chatChannelMemberRepositoryMock.Setup(x => x.GetUserMemberAsync("custom-1", "user-b")).ReturnsAsync((ChatChannelMember)null);

				var result = await _chatChannelService.AddMembersAsync("custom-1", new List<string> { "user-b" }, "user-a");

				result.Should().HaveCount(1);
				_chatChannelMemberRepositoryMock.Verify(x => x.InsertAsync(It.Is<ChatChannelMember>(m =>
					m.ChatChannelId == "custom-1" && m.UserId == "user-b"),
					It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
			}

			[Test]
			public void cross_department_member_should_be_rejected()
			{
				var channel = CreateChannel("adhoc-1", ChatChannelType.AdHocGroup);
				_chatChannelRepositoryMock.Setup(x => x.GetByIdAsync("adhoc-1")).ReturnsAsync(channel);
				_departmentsServiceMock.Setup(x => x.IsUserInDepartmentAsync(1, "outsider")).ReturnsAsync(false);

				Func<Task> act = async () => await _chatChannelService.AddMembersAsync("adhoc-1", new List<string> { "outsider" }, "user-a");

				act.Should().ThrowAsync<UnauthorizedAccessException>();
			}

			[Test]
			public async Task removed_member_should_be_reactivated_with_targeted_update()
			{
				var channel = CreateChannel("adhoc-1", ChatChannelType.AdHocGroup);
				_chatChannelRepositoryMock.Setup(x => x.GetByIdAsync("adhoc-1")).ReturnsAsync(channel);
				_chatChannelMemberRepositoryMock.Setup(x => x.GetUserMemberAsync("adhoc-1", "user-b")).ReturnsAsync(new ChatChannelMember
				{
					ChatChannelMemberId = "member-1",
					ChatChannelId = "adhoc-1",
					DepartmentId = 1,
					ParticipantType = (int)ChatParticipantType.User,
					UserId = "user-b",
					RemovedOn = DateTime.UtcNow
				});
				_chatChannelMemberRepositoryMock.Setup(x => x.SetMemberActiveAsync("member-1", true, It.IsAny<CancellationToken>())).ReturnsAsync(true);

				var result = await _chatChannelService.AddMembersAsync("adhoc-1", new List<string> { "user-b" }, "user-a");

				result.Should().HaveCount(1);
				_chatChannelMemberRepositoryMock.Verify(x => x.SetMemberActiveAsync("member-1", true, It.IsAny<CancellationToken>()), Times.Once);
				_chatChannelMemberRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<ChatChannelMember>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
			}
		}

		[TestFixture]
		public class when_ensuring_member_state : with_the_chat_channel_service
		{
			[Test]
			public void invite_only_channel_without_membership_should_throw()
			{
				_chatChannelRepositoryMock.Setup(x => x.GetByIdAsync("dm-1")).ReturnsAsync(new ChatChannel
				{
					ChatChannelId = "dm-1",
					DepartmentId = 1,
					ChannelType = (int)ChatChannelType.DirectMessage
				});
				_chatChannelMemberRepositoryMock.Setup(x => x.GetUserMemberAsync("dm-1", "user-a")).ReturnsAsync((ChatChannelMember)null);

				Func<Task> act = async () => await _chatChannelService.EnsureMemberStateAsync("dm-1", 1, "user-a", null);

				act.Should().ThrowAsync<UnauthorizedAccessException>();
				_chatChannelMemberRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<ChatChannelMember>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
			}

			[Test]
			public async Task invite_only_channel_with_removed_membership_should_reactivate()
			{
				_chatChannelRepositoryMock.Setup(x => x.GetByIdAsync("dm-1")).ReturnsAsync(new ChatChannel
				{
					ChatChannelId = "dm-1",
					DepartmentId = 1,
					ChannelType = (int)ChatChannelType.DirectMessage
				});
				_chatChannelMemberRepositoryMock.Setup(x => x.GetUserMemberAsync("dm-1", "user-a")).ReturnsAsync(new ChatChannelMember
				{
					ChatChannelMemberId = "member-1",
					ChatChannelId = "dm-1",
					DepartmentId = 1,
					ParticipantType = (int)ChatParticipantType.User,
					UserId = "user-a",
					RemovedOn = DateTime.UtcNow
				});
				_chatChannelMemberRepositoryMock.Setup(x => x.SetMemberActiveAsync("member-1", true, It.IsAny<CancellationToken>())).ReturnsAsync(true);

				var result = await _chatChannelService.EnsureMemberStateAsync("dm-1", 1, "user-a", null);

				result.Should().NotBeNull();
				result.RemovedOn.Should().BeNull();
				_chatChannelMemberRepositoryMock.Verify(x => x.SetMemberActiveAsync("member-1", true, It.IsAny<CancellationToken>()), Times.Once);
				_chatChannelMemberRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<ChatChannelMember>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
			}

			[Test]
			public async Task implicit_channel_without_membership_should_create_row()
			{
				_chatChannelRepositoryMock.Setup(x => x.GetByIdAsync("dept-1")).ReturnsAsync(new ChatChannel
				{
					ChatChannelId = "dept-1",
					DepartmentId = 1,
					ChannelType = (int)ChatChannelType.DepartmentDefault
				});
				_chatChannelMemberRepositoryMock.Setup(x => x.GetUserMemberAsync("dept-1", "user-a")).ReturnsAsync((ChatChannelMember)null);

				var result = await _chatChannelService.EnsureMemberStateAsync("dept-1", 1, "user-a", null);

				result.Should().NotBeNull();
				_chatChannelMemberRepositoryMock.Verify(x => x.InsertAsync(It.Is<ChatChannelMember>(m =>
					m.ChatChannelId == "dept-1" && m.UserId == "user-a"),
					It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
			}
		}

		[TestFixture]
		public class when_listing_channels_for_users : with_the_chat_channel_service
		{
			private ChatChannel SetupDepartmentChannel()
			{
				var departmentChannel = new ChatChannel
				{
					ChatChannelId = "dept-chan",
					DepartmentId = 1,
					ChannelType = (int)ChatChannelType.DepartmentDefault,
					Name = "First Battalion",
					CreatedOn = DateTime.UtcNow
				};
				_chatChannelRepositoryMock.Setup(x => x.GetDepartmentDefaultAsync(1)).ReturnsAsync(departmentChannel);
				return departmentChannel;
			}

			[Test]
			public async Task department_admin_should_get_every_group_channel_provisioned()
			{
				SetupDepartmentChannel();
				_chatPermissionServiceMock.Setup(x => x.IsDepartmentAdminAsync(1, "admin-a")).ReturnsAsync(true);
				_departmentGroupsServiceMock.Setup(x => x.GetAllGroupsForDepartmentAsync(1)).ReturnsAsync(new List<DepartmentGroup>
				{
					new DepartmentGroup { DepartmentGroupId = 9, DepartmentId = 1, Name = "Station 1" },
					new DepartmentGroup { DepartmentGroupId = 10, DepartmentId = 1, Name = "Station 2" }
				});
				_chatChannelRepositoryMock.Setup(x => x.GetByGroupIdAsync(It.IsAny<int>())).ReturnsAsync((ChatChannel)null);

				var result = await _chatChannelService.GetChannelsForUserAsync(1, "admin-a", null);

				result.Should().Contain(c => c.ChannelType == (int)ChatChannelType.GroupDefault && c.GroupId == 9);
				result.Should().Contain(c => c.ChannelType == (int)ChatChannelType.GroupDefault && c.GroupId == 10);
				_chatChannelRepositoryMock.Verify(x => x.InsertAsync(It.Is<ChatChannel>(c => c.ChannelType == (int)ChatChannelType.GroupDefault),
					It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Exactly(2));
				_departmentGroupsServiceMock.Verify(x => x.GetGroupForUserAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
			}

			[Test]
			public async Task admin_existing_group_channels_should_come_from_bulk_load_without_per_group_lookups()
			{
				SetupDepartmentChannel();
				_chatPermissionServiceMock.Setup(x => x.IsDepartmentAdminAsync(1, "admin-a")).ReturnsAsync(true);
				_departmentGroupsServiceMock.Setup(x => x.GetAllGroupsForDepartmentAsync(1)).ReturnsAsync(new List<DepartmentGroup>
				{
					new DepartmentGroup { DepartmentGroupId = 9, DepartmentId = 1, Name = "Station 1" },
					new DepartmentGroup { DepartmentGroupId = 10, DepartmentId = 1, Name = "Station 2" }
				});
				_chatChannelRepositoryMock.Setup(x => x.GetAllByDepartmentIdAsync(1, false)).ReturnsAsync(new List<ChatChannel>
				{
					new ChatChannel { ChatChannelId = "group-9", DepartmentId = 1, ChannelType = (int)ChatChannelType.GroupDefault, GroupId = 9, Name = "Station 1" },
					new ChatChannel { ChatChannelId = "group-10", DepartmentId = 1, ChannelType = (int)ChatChannelType.GroupDefault, GroupId = 10, Name = "Station 2" }
				});

				var result = await _chatChannelService.GetChannelsForUserAsync(1, "admin-a", null);

				result.Should().Contain(c => c.ChatChannelId == "group-9");
				result.Should().Contain(c => c.ChatChannelId == "group-10");
				_chatChannelRepositoryMock.Verify(x => x.GetByGroupIdAsync(It.IsAny<int>()), Times.Never);
				_chatChannelRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<ChatChannel>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
			}

			[Test]
			public async Task admin_single_group_provisioning_failure_should_not_abort_the_channel_list()
			{
				SetupDepartmentChannel();
				_chatPermissionServiceMock.Setup(x => x.IsDepartmentAdminAsync(1, "admin-a")).ReturnsAsync(true);
				_departmentGroupsServiceMock.Setup(x => x.GetAllGroupsForDepartmentAsync(1)).ReturnsAsync(new List<DepartmentGroup>
				{
					new DepartmentGroup { DepartmentGroupId = 9, DepartmentId = 1, Name = "Station 1" },
					new DepartmentGroup { DepartmentGroupId = 10, DepartmentId = 1, Name = "Station 2" }
				});
				_chatChannelRepositoryMock.Setup(x => x.GetByGroupIdAsync(9)).ThrowsAsync(new InvalidOperationException("db down"));
				_chatChannelRepositoryMock.Setup(x => x.GetByGroupIdAsync(10)).ReturnsAsync((ChatChannel)null);

				var result = await _chatChannelService.GetChannelsForUserAsync(1, "admin-a", null);

				result.Should().Contain(c => c.ChannelType == (int)ChatChannelType.GroupDefault && c.GroupId == 10);
				result.Should().Contain(c => c.ChannelType == (int)ChatChannelType.DepartmentDefault);
			}

			[Test]
			public async Task non_admin_should_only_get_their_own_group_channel()
			{
				SetupDepartmentChannel();
				_chatPermissionServiceMock.Setup(x => x.IsDepartmentAdminAsync(1, "user-a")).ReturnsAsync(false);
				_departmentGroupsServiceMock.Setup(x => x.GetGroupForUserAsync("user-a", 1)).ReturnsAsync(new DepartmentGroup
				{
					DepartmentGroupId = 9,
					DepartmentId = 1,
					Name = "Station 1"
				});
				_chatChannelRepositoryMock.Setup(x => x.GetByGroupIdAsync(9)).ReturnsAsync((ChatChannel)null);

				var result = await _chatChannelService.GetChannelsForUserAsync(1, "user-a", null);

				result.Should().ContainSingle(c => c.ChannelType == (int)ChatChannelType.GroupDefault).Which.GroupId.Should().Be(9);
				_departmentGroupsServiceMock.Verify(x => x.GetAllGroupsForDepartmentAsync(It.IsAny<int>()), Times.Never);
			}

			[Test]
			public async Task active_unit_should_see_channels_where_the_unit_is_the_member()
			{
				SetupDepartmentChannel();
				_chatPermissionServiceMock.Setup(x => x.IsDepartmentAdminAsync(1, "user-a")).ReturnsAsync(false);
				_chatPermissionServiceMock.Setup(x => x.CanSendAsUnitAsync("user-a", 7, 1)).ReturnsAsync(true);

				var unitDm = new ChatChannel { ChatChannelId = "dm-unit-7", DepartmentId = 1, ChannelType = (int)ChatChannelType.DirectMessage, DmKey = "u:dispatcher|unit:7" };
				_chatChannelMemberRepositoryMock.Setup(x => x.GetActiveByUnitIdAsync(1, 7)).ReturnsAsync(new List<ChatChannelMember>
				{
					new ChatChannelMember { ChatChannelMemberId = "m1", ChatChannelId = "dm-unit-7", DepartmentId = 1, ParticipantType = (int)ChatParticipantType.Unit, UnitId = 7 }
				});
				_chatChannelRepositoryMock.Setup(x => x.GetByIdsAsync(It.Is<IEnumerable<string>>(ids => ids.Contains("dm-unit-7")))).ReturnsAsync(new List<ChatChannel> { unitDm });

				var result = await _chatChannelService.GetChannelsForUserAsync(1, "user-a", 7);

				result.Should().Contain(c => c.ChatChannelId == "dm-unit-7");
			}

			[Test]
			public async Task active_unit_the_user_does_not_crew_should_not_expose_unit_channels()
			{
				SetupDepartmentChannel();
				_chatPermissionServiceMock.Setup(x => x.IsDepartmentAdminAsync(1, "user-a")).ReturnsAsync(false);
				_chatPermissionServiceMock.Setup(x => x.CanSendAsUnitAsync("user-a", 7, 1)).ReturnsAsync(false);

				var result = await _chatChannelService.GetChannelsForUserAsync(1, "user-a", 7);

				result.Should().NotContain(c => c.ChatChannelId == "dm-unit-7");
				_chatChannelMemberRepositoryMock.Verify(x => x.GetActiveByUnitIdAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
			}

			[Test]
			public async Task without_an_active_unit_no_unit_membership_lookup_happens()
			{
				SetupDepartmentChannel();
				_chatPermissionServiceMock.Setup(x => x.IsDepartmentAdminAsync(1, "user-a")).ReturnsAsync(false);

				await _chatChannelService.GetChannelsForUserAsync(1, "user-a", null);

				_chatChannelMemberRepositoryMock.Verify(x => x.GetActiveByUnitIdAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
			}

			[Test]
			public async Task an_active_unit_should_get_its_dispatch_line_provisioned_into_the_list()
			{
				SetupDepartmentChannel();
				_chatPermissionServiceMock.Setup(x => x.IsDepartmentAdminAsync(1, "user-a")).ReturnsAsync(false);
				_chatPermissionServiceMock.Setup(x => x.CanSendAsUnitAsync("user-a", 7, 1)).ReturnsAsync(true);
				_unitsServiceMock.Setup(x => x.GetUnitByIdAsync(7)).ReturnsAsync(new Unit { UnitId = 7, DepartmentId = 1, Name = "Engine 6" });
				_chatChannelRepositoryMock.Setup(x => x.GetByDmKeyAsync(1, "unitdispatch:7")).ReturnsAsync((ChatChannel)null);

				var result = await _chatChannelService.GetChannelsForUserAsync(1, "user-a", 7);

				result.Should().Contain(c => c.ChannelType == (int)ChatChannelType.UnitDispatch && c.Name == "Engine 6 Dispatch");
			}

			[Test]
			public async Task a_unit_dispatch_provisioning_failure_should_not_abort_the_channel_list()
			{
				SetupDepartmentChannel();
				_chatPermissionServiceMock.Setup(x => x.IsDepartmentAdminAsync(1, "user-a")).ReturnsAsync(false);
				_chatPermissionServiceMock.Setup(x => x.CanSendAsUnitAsync("user-a", 7, 1)).ReturnsAsync(true);
				_unitsServiceMock.Setup(x => x.GetUnitByIdAsync(7)).ThrowsAsync(new InvalidOperationException("units down"));

				var result = await _chatChannelService.GetChannelsForUserAsync(1, "user-a", 7);

				result.Should().Contain(c => c.ChannelType == (int)ChatChannelType.DepartmentDefault);
			}

			[Test]
			public async Task incident_leads_and_dispatch_channels_should_be_listed_for_users_with_access()
			{
				SetupDepartmentChannel();
				_chatPermissionServiceMock.Setup(x => x.IsDepartmentAdminAsync(1, "user-a")).ReturnsAsync(false);

				var leads = new ChatChannel { ChatChannelId = "leads-1", DepartmentId = 1, ChannelType = (int)ChatChannelType.IncidentLeads, CallId = 42, Name = "Barn Fire All Leads" };
				var dispatch = new ChatChannel { ChatChannelId = "dispatch-1", DepartmentId = 1, ChannelType = (int)ChatChannelType.IncidentDispatch, CallId = 42, Name = "Barn Fire Dispatch" };
				var unitDispatch = new ChatChannel { ChatChannelId = "unit-dispatch-7", DepartmentId = 1, ChannelType = (int)ChatChannelType.UnitDispatch, DmKey = "unitdispatch:7", Name = "Engine 6 Dispatch" };
				_chatChannelRepositoryMock.Setup(x => x.GetAllByDepartmentIdAsync(1, false)).ReturnsAsync(new List<ChatChannel> { leads, dispatch, unitDispatch });

				_chatPermissionServiceMock.Setup(x => x.CanAccessChannelAsync(leads, "user-a", null)).ReturnsAsync(true);
				_chatPermissionServiceMock.Setup(x => x.CanAccessChannelAsync(dispatch, "user-a", null)).ReturnsAsync(true);
				_chatPermissionServiceMock.Setup(x => x.CanAccessChannelAsync(unitDispatch, "user-a", null)).ReturnsAsync(true);

				var result = await _chatChannelService.GetChannelsForUserAsync(1, "user-a", null);

				result.Should().Contain(c => c.ChatChannelId == "leads-1");
				result.Should().Contain(c => c.ChatChannelId == "dispatch-1");
				result.Should().Contain(c => c.ChatChannelId == "unit-dispatch-7");
			}

			[Test]
			public async Task incident_leads_and_dispatch_channels_should_stay_hidden_without_access()
			{
				SetupDepartmentChannel();
				_chatPermissionServiceMock.Setup(x => x.IsDepartmentAdminAsync(1, "user-a")).ReturnsAsync(false);

				var leads = new ChatChannel { ChatChannelId = "leads-1", DepartmentId = 1, ChannelType = (int)ChatChannelType.IncidentLeads, CallId = 42 };
				var unitDispatch = new ChatChannel { ChatChannelId = "unit-dispatch-7", DepartmentId = 1, ChannelType = (int)ChatChannelType.UnitDispatch, DmKey = "unitdispatch:7" };
				_chatChannelRepositoryMock.Setup(x => x.GetAllByDepartmentIdAsync(1, false)).ReturnsAsync(new List<ChatChannel> { leads, unitDispatch });

				var result = await _chatChannelService.GetChannelsForUserAsync(1, "user-a", null);

				result.Should().NotContain(c => c.ChatChannelId == "leads-1");
				result.Should().NotContain(c => c.ChatChannelId == "unit-dispatch-7");
			}
		}

		[TestFixture]
		public class when_ensuring_unit_dispatch_channels : with_the_chat_channel_service
		{
			[Test]
			public async Task a_missing_channel_should_be_created_with_the_unit_as_the_member()
			{
				_unitsServiceMock.Setup(x => x.GetUnitByIdAsync(7)).ReturnsAsync(new Unit { UnitId = 7, DepartmentId = 1, Name = "Engine 6" });
				_chatChannelRepositoryMock.Setup(x => x.GetByDmKeyAsync(1, "unitdispatch:7")).ReturnsAsync((ChatChannel)null);

				List<ChatChannelMember> capturedMembers = null;
				_chatChannelRepositoryMock
					.Setup(x => x.CreateDirectMessageChannelAsync(It.IsAny<ChatChannel>(), It.IsAny<IEnumerable<ChatChannelMember>>(), It.IsAny<CancellationToken>()))
					.Callback((ChatChannel c, IEnumerable<ChatChannelMember> m, CancellationToken t) => capturedMembers = m.ToList())
					.ReturnsAsync((ChatChannel c, IEnumerable<ChatChannelMember> m, CancellationToken t) => c);

				var result = await _chatChannelService.EnsureUnitDispatchChannelAsync(1, 7);

				result.Should().NotBeNull();
				result.ChannelType.Should().Be((int)ChatChannelType.UnitDispatch);
				result.Name.Should().Be("Engine 6 Dispatch");
				result.DmKey.Should().Be("unitdispatch:7");
				capturedMembers.Should().ContainSingle(m => m.ParticipantType == (int)ChatParticipantType.Unit && m.UnitId == 7);
			}

			[Test]
			public async Task an_existing_channel_should_be_returned_without_creating_another()
			{
				var existing = new ChatChannel { ChatChannelId = "ud-7", DepartmentId = 1, ChannelType = (int)ChatChannelType.UnitDispatch, DmKey = "unitdispatch:7", Name = "Engine 6 Dispatch" };
				_unitsServiceMock.Setup(x => x.GetUnitByIdAsync(7)).ReturnsAsync(new Unit { UnitId = 7, DepartmentId = 1, Name = "Engine 6" });
				_chatChannelRepositoryMock.Setup(x => x.GetByDmKeyAsync(1, "unitdispatch:7")).ReturnsAsync(existing);

				var result = await _chatChannelService.EnsureUnitDispatchChannelAsync(1, 7);

				result.Should().BeSameAs(existing);
				_chatChannelRepositoryMock.Verify(x => x.CreateDirectMessageChannelAsync(It.IsAny<ChatChannel>(), It.IsAny<IEnumerable<ChatChannelMember>>(), It.IsAny<CancellationToken>()), Times.Never);
				_chatChannelRepositoryMock.Verify(x => x.UpdateChannelInfoAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
			}

			[Test]
			public async Task a_renamed_unit_should_refresh_the_channel_name()
			{
				var existing = new ChatChannel { ChatChannelId = "ud-7", DepartmentId = 1, ChannelType = (int)ChatChannelType.UnitDispatch, DmKey = "unitdispatch:7", Name = "Engine 6 Dispatch" };
				_unitsServiceMock.Setup(x => x.GetUnitByIdAsync(7)).ReturnsAsync(new Unit { UnitId = 7, DepartmentId = 1, Name = "Rescue 1" });
				_chatChannelRepositoryMock.Setup(x => x.GetByDmKeyAsync(1, "unitdispatch:7")).ReturnsAsync(existing);

				var result = await _chatChannelService.EnsureUnitDispatchChannelAsync(1, 7);

				result.Name.Should().Be("Rescue 1 Dispatch");
				_chatChannelRepositoryMock.Verify(x => x.UpdateChannelInfoAsync("ud-7", "Rescue 1 Dispatch", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
			}

			[Test]
			public async Task a_unit_from_another_department_should_not_get_a_channel()
			{
				_unitsServiceMock.Setup(x => x.GetUnitByIdAsync(7)).ReturnsAsync(new Unit { UnitId = 7, DepartmentId = 2, Name = "Engine 6" });

				var result = await _chatChannelService.EnsureUnitDispatchChannelAsync(1, 7);

				result.Should().BeNull();
				_chatChannelRepositoryMock.Verify(x => x.CreateDirectMessageChannelAsync(It.IsAny<ChatChannel>(), It.IsAny<IEnumerable<ChatChannelMember>>(), It.IsAny<CancellationToken>()), Times.Never);
			}
		}

		[TestFixture]
		public class when_creating_ad_hoc_group_channels : with_the_chat_channel_service
		{
			[Test]
			public async Task creator_should_be_inserted_as_moderator()
			{
				var result = await _chatChannelService.CreateAdHocGroupChannelAsync(1, "user-a", "Strike Team", new List<string> { "user-b" });

				result.Should().NotBeNull();
				result.ChannelType.Should().Be((int)ChatChannelType.AdHocGroup);
				_chatChannelMemberRepositoryMock.Verify(x => x.InsertAsync(It.Is<ChatChannelMember>(m =>
					m.UserId == "user-a" && m.IsModerator),
					It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
			}

			[Test]
			public async Task duplicate_and_creator_ids_should_not_be_double_inserted()
			{
				var result = await _chatChannelService.CreateAdHocGroupChannelAsync(1, "user-a", "Strike Team", new List<string> { "user-b", "user-b", "user-c", "user-a" });

				result.Should().NotBeNull();
				_chatChannelMemberRepositoryMock.Verify(x => x.InsertAsync(It.Is<ChatChannelMember>(m => m.UserId == "user-a"), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
				_chatChannelMemberRepositoryMock.Verify(x => x.InsertAsync(It.Is<ChatChannelMember>(m => m.UserId == "user-b"), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
				_chatChannelMemberRepositoryMock.Verify(x => x.InsertAsync(It.Is<ChatChannelMember>(m => m.UserId == "user-c"), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
				_chatChannelMemberRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<ChatChannelMember>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Exactly(3));
			}
		}
	}
}
