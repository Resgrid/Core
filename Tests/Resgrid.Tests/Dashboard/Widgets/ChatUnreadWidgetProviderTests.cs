using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Dashboard.Widgets.Communications;
using Resgrid.Model.Services;
using Resgrid.Services.Dashboard.Widgets.Communications;
using static Resgrid.Tests.Dashboard.Widgets.CommsPlanningWidgetContexts;

namespace Resgrid.Tests.Dashboard.Widgets
{
	[TestFixture]
	public class ChatUnreadWidgetProviderTests
	{
		private static ChatChannelUnread Channel(string id, long unread, int minutesAgo, int departmentId = DepartmentId) => new ChatChannelUnread
		{
			Channel = new ChatChannel
			{
				ChatChannelId = id, DepartmentId = departmentId, Name = "Channel " + id, ChannelType = (int)ChatChannelType.GroupDefault,
				LastMessageSeq = 20, LastMessageOn = Now.AddMinutes(-minutesAgo)
			},
			Member = new ChatChannelMember { ChatChannelId = id, LastReadSeq = 20 - unread, NotificationPreference = 1 },
			UnreadCount = unread,
			DisplayName = "Channel " + id
		};

		[Test]
		public void Unread_count_is_messages_past_the_read_pointer_and_never_negative()
		{
			var channel = new ChatChannel { LastMessageSeq = 12 };

			ChatReadState.UnreadCount(channel, new ChatChannelMember { LastReadSeq = 9 }).Should().Be(3);
			ChatReadState.UnreadCount(channel, null).Should().Be(12, "no member row means nothing read yet");
			ChatReadState.UnreadCount(channel, new ChatChannelMember { LastReadSeq = 15 }).Should().Be(0);
			ChatReadState.UnreadCount(null, null).Should().Be(0);
		}

		[Test]
		public async Task Lists_unread_channels_newest_first_with_totals()
		{
			var channels = new Mock<IChatChannelService>(MockBehavior.Strict);
			channels.Setup(x => x.GetUnreadChannelsForUserAsync(DepartmentId, UserId, false))
				.ReturnsAsync(new List<ChatChannelUnread> { Channel("a", 2, 30), Channel("b", 5, 5), Channel("x", 9, 1, OtherDepartmentId) });
			var messages = new Mock<IChatMessageService>(MockBehavior.Strict);
			messages.Setup(x => x.GetPendingAcksForUserAsync(DepartmentId, UserId)).ReturnsAsync(new List<ChatMessageAck>());
			var provider = new ChatUnreadWidgetProvider(channels.Object, messages.Object);

			var data = (ChatUnreadWidgetData)await provider.GetDataAsync(For("chat.unread"), CancellationToken.None);

			data.TotalUnread.Should().Be(7, "another department's channel is never counted");
			data.UnreadChannelCount.Should().Be(2);
			data.Channels.Select(c => c.ChatChannelId).Should().Equal("b", "a");
			data.Channels[0].NotificationPreference.Should().Be(1);
		}

		[Test]
		public async Task Pending_acks_pull_in_read_channels_and_go_first()
		{
			var channels = new Mock<IChatChannelService>(MockBehavior.Strict);
			channels.Setup(x => x.GetUnreadChannelsForUserAsync(DepartmentId, UserId, true))
				.ReturnsAsync(new List<ChatChannelUnread> { Channel("a", 2, 1), Channel("urgent", 0, 60), Channel("quiet", 0, 2) });
			var messages = new Mock<IChatMessageService>(MockBehavior.Strict);
			messages.Setup(x => x.GetPendingAcksForUserAsync(DepartmentId, UserId)).ReturnsAsync(new List<ChatMessageAck>
			{
				new ChatMessageAck { ChatChannelId = "urgent", DepartmentId = DepartmentId, UserId = UserId },
				new ChatMessageAck { ChatChannelId = "urgent", DepartmentId = DepartmentId, UserId = UserId },
				new ChatMessageAck { ChatChannelId = "gone", DepartmentId = DepartmentId, UserId = UserId },
				new ChatMessageAck { ChatChannelId = "a", DepartmentId = DepartmentId, UserId = UserId, AcknowledgedOn = Now }
			});
			var provider = new ChatUnreadWidgetProvider(channels.Object, messages.Object);

			var data = (ChatUnreadWidgetData)await provider.GetDataAsync(For("chat.unread"), CancellationToken.None);

			data.PendingAckCount.Should().Be(2, "acks in channels the member can no longer open, and acknowledged ones, are not counted");
			data.Channels.Select(c => c.ChatChannelId).Should().Equal("urgent", "a");
			data.Channels[0].PendingAcks.Should().Be(2);
		}
	}
}
