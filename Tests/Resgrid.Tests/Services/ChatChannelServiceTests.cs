using System;
using System.Collections.Generic;
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
