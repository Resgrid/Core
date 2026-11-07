using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Services
{
	namespace ChatChannelServiceTests
	{
		/// <summary>
		/// The chat badge is the sum of the v4 channel list's UnreadCount over the same channels and read pointers, except
		/// that each channel's sequence is read fresh: the cached list is not rolled forward on every send.
		/// </summary>
		[TestFixture]
		public class when_counting_unread_chat_messages : with_the_chat_channel_service
		{
			private void SetupDepartmentChannel(long cachedSeq)
			{
				_chatChannelRepositoryMock.Setup(x => x.GetDepartmentDefaultAsync(1)).ReturnsAsync(new ChatChannel
				{
					ChatChannelId = "dept-chan",
					DepartmentId = 1,
					ChannelType = (int)ChatChannelType.DepartmentDefault,
					Name = "First Battalion",
					LastMessageSeq = cachedSeq
				});
				_chatPermissionServiceMock.Setup(x => x.IsDepartmentAdminAsync(1, "user-a")).ReturnsAsync(false);
			}

			[Test]
			public async Task unread_uses_the_fresh_sequence_and_the_members_read_pointer()
			{
				SetupDepartmentChannel(cachedSeq: 5);
				_chatChannelMemberRepositoryMock.Setup(x => x.GetActiveByUserIdAsync(1, "user-a")).ReturnsAsync(new List<ChatChannelMember>
				{
					new ChatChannelMember { ChatChannelMemberId = "m-dept", ChatChannelId = "dept-chan", DepartmentId = 1, UserId = "user-a", LastReadSeq = 6 }
				});
				_chatChannelRepositoryMock.Setup(x => x.GetLastMessageSeqsByIdsAsync(It.Is<IEnumerable<string>>(ids => ids.Contains("dept-chan"))))
					.ReturnsAsync(new Dictionary<string, long> { ["dept-chan"] = 9 });

				var unread = await _chatChannelService.GetUnreadMessageCountForUserAsync(1, "user-a", null);

				unread.Should().Be(3);
			}

			[Test]
			public async Task a_channel_without_a_member_row_counts_its_whole_history_as_the_list_does()
			{
				SetupDepartmentChannel(cachedSeq: 4);
				_chatChannelRepositoryMock.Setup(x => x.GetLastMessageSeqsByIdsAsync(It.IsAny<IEnumerable<string>>()))
					.ReturnsAsync(new Dictionary<string, long>());

				var unread = await _chatChannelService.GetUnreadMessageCountForUserAsync(1, "user-a", null);

				unread.Should().Be(4);
			}

			[Test]
			public async Task the_active_units_channels_and_read_pointers_count_too()
			{
				SetupDepartmentChannel(cachedSeq: 2);
				_chatPermissionServiceMock.Setup(x => x.CanSendAsUnitAsync("user-a", 7, 1)).ReturnsAsync(true);
				var unitDm = new ChatChannel { ChatChannelId = "dm-unit-7", DepartmentId = 1, ChannelType = (int)ChatChannelType.DirectMessage, DmKey = "u:dispatcher|unit:7", LastMessageSeq = 3 };
				_chatChannelMemberRepositoryMock.Setup(x => x.GetActiveByUserIdAsync(1, "user-a")).ReturnsAsync(new List<ChatChannelMember>
				{
					new ChatChannelMember { ChatChannelMemberId = "m-dept", ChatChannelId = "dept-chan", DepartmentId = 1, UserId = "user-a", LastReadSeq = 2 }
				});
				_chatChannelMemberRepositoryMock.Setup(x => x.GetActiveByUnitIdAsync(1, 7)).ReturnsAsync(new List<ChatChannelMember>
				{
					new ChatChannelMember { ChatChannelMemberId = "m-unit", ChatChannelId = "dm-unit-7", DepartmentId = 1, ParticipantType = (int)ChatParticipantType.Unit, UnitId = 7, LastReadSeq = 1 }
				});
				_chatChannelRepositoryMock.Setup(x => x.GetByIdsAsync(It.Is<IEnumerable<string>>(ids => ids.Contains("dm-unit-7")))).ReturnsAsync(new List<ChatChannel> { unitDm });
				_chatPermissionServiceMock.Setup(x => x.CanAccessChannelAsync(unitDm, "user-a", 7)).ReturnsAsync(true);
				_chatChannelRepositoryMock.Setup(x => x.GetLastMessageSeqsByIdsAsync(It.IsAny<IEnumerable<string>>()))
					.ReturnsAsync(new Dictionary<string, long> { ["dept-chan"] = 2, ["dm-unit-7"] = 4 });

				var unread = await _chatChannelService.GetUnreadMessageCountForUserAsync(1, "user-a", 7);

				unread.Should().Be(3);
			}
		}
	}
}
