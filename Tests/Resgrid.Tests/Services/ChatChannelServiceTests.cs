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

				// Inserts/updates echo back the entity they were handed (repository contract).
				_chatChannelRepositoryMock.Setup(x => x.InsertAsync(It.IsAny<ChatChannel>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((ChatChannel c, CancellationToken t, bool f) => c);
				_chatChannelRepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<ChatChannel>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((ChatChannel c, CancellationToken t, bool f) => c);
				_chatChannelMemberRepositoryMock.Setup(x => x.InsertAsync(It.IsAny<ChatChannelMember>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((ChatChannelMember m, CancellationToken t, bool f) => m);
				_chatChannelMemberRepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<ChatChannelMember>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((ChatChannelMember m, CancellationToken t, bool f) => m);

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
					_eventAggregatorMock.Object);
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
				_chatChannelMemberRepositoryMock.Verify(x => x.InsertAsync(It.Is<ChatChannelMember>(m =>
					m.ParticipantType == (int)ChatParticipantType.User && m.UserId == "user-a"),
					It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
				_chatChannelMemberRepositoryMock.Verify(x => x.InsertAsync(It.Is<ChatChannelMember>(m =>
					m.ParticipantType == (int)ChatParticipantType.User && m.UserId == "user-b"),
					It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
				_chatChannelMemberRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<ChatChannelMember>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Exactly(2));
			}

			[Test]
			public async Task unit_target_dm_should_insert_unit_member_row_with_unit_name()
			{
				_chatChannelRepositoryMock.Setup(x => x.GetByDmKeyAsync(1, It.IsAny<string>())).ReturnsAsync((ChatChannel)null);
				_unitsServiceMock.Setup(x => x.GetUnitByIdAsync(7)).ReturnsAsync(new Unit { UnitId = 7, DepartmentId = 1, Name = "Engine 6" });

				var result = await _chatChannelService.GetOrCreateDirectMessageChannelAsync(1, "user-a", null, 7);

				result.Should().NotBeNull();
				result.DmKey.Should().Be("u:user-a|unit:7");
				_chatChannelMemberRepositoryMock.Verify(x => x.InsertAsync(It.Is<ChatChannelMember>(m =>
					m.ParticipantType == (int)ChatParticipantType.Unit && m.UnitId == 7 && m.DisplayNameOverride == "Engine 6"),
					It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
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
				_chatChannelMemberRepositoryMock.Setup(x => x.GetUserMemberAsync("channel-1", "user-a")).ReturnsAsync((ChatChannelMember)null);

				var result = await _chatChannelService.SetNotificationPreferenceAsync("channel-1", 1, "user-a", ChatNotificationPreference.MentionsOnly);

				result.Should().BeTrue();
				_chatChannelMemberRepositoryMock.Verify(x => x.InsertAsync(It.Is<ChatChannelMember>(m =>
					m.ChatChannelId == "channel-1" && m.UserId == "user-a" && m.ParticipantType == (int)ChatParticipantType.User),
					It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
				_chatChannelMemberRepositoryMock.Verify(x => x.UpdateAsync(It.Is<ChatChannelMember>(m =>
					m.ChatChannelId == "channel-1" && m.UserId == "user-a" && m.NotificationPreference == (int)ChatNotificationPreference.MentionsOnly),
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
