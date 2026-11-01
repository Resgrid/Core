using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Communications;
using Resgrid.Model.Services;
using Resgrid.Services.Dashboard;
using Resgrid.Services.Dashboard.Widgets.Communications;
using static Resgrid.Tests.Dashboard.Widgets.CommsPlanningWidgetContexts;

namespace Resgrid.Tests.Dashboard.Widgets
{
	[TestFixture]
	public class ChatModerationWidgetProviderTests
	{
		private static ChatMessageFlag Flag(string id, int hoursAgo, string channel = "c1", int departmentId = DepartmentId, int reason = (int)ChatFlagReason.Spam) => new ChatMessageFlag
		{
			ChatMessageFlagId = id, ChatChannelId = channel, DepartmentId = departmentId, FlaggedOn = Now.AddHours(-hoursAgo), Reason = reason,
			Status = (int)ChatFlagStatus.Open, Note = "private note"
		};

		[Test]
		public void Is_a_department_admin_widget_behind_chat_system()
		{
			var definition = DashboardWidgetCatalog.Find("chat.moderation");

			definition.Gates.Role.Should().Be(DashboardRoles.DepartmentAdmin);
			definition.Gates.Flags.Should().Contain(FeatureFlagKeys.ChatSystem);
		}

		[Test]
		public async Task Lists_the_oldest_open_flags_with_channel_names()
		{
			var moderation = new Mock<IChatModerationService>(MockBehavior.Strict);
			moderation.Setup(x => x.GetFlagsAsync(DepartmentId, UserId, ChatFlagStatus.Open, 1, ChatModerationWidgetProvider.ReadCap))
				.ReturnsAsync(new List<ChatMessageFlag> { Flag("new", 1), Flag("old", 30, "c2", reason: (int)ChatFlagReason.SensitiveInformation), Flag("foreign", 50, departmentId: OtherDepartmentId) });
			var channels = new Mock<IChatChannelService>(MockBehavior.Strict);
			channels.Setup(x => x.GetChannelsByIdsAsync(It.IsAny<IEnumerable<string>>())).ReturnsAsync(new List<ChatChannel>
			{
				new ChatChannel { ChatChannelId = "c1", DepartmentId = DepartmentId, Name = "Station 1", ChannelType = (int)ChatChannelType.GroupDefault },
				new ChatChannel { ChatChannelId = "c2", DepartmentId = OtherDepartmentId, Name = "Not ours" }
			});
			var provider = new ChatModerationWidgetProvider(moderation.Object, channels.Object);

			var data = (ChatModerationWidgetData)await provider.GetDataAsync(For("chat.moderation", isAdmin: true), CancellationToken.None);

			data.OpenCount.Should().Be(2);
			data.CountCapped.Should().BeFalse();
			data.OldestFlaggedOnUtc.Should().Be(Now.AddHours(-30));
			data.Items.Select(i => i.ChatMessageFlagId).Should().Equal("old", "new");
			data.Items[0].ReasonKey.Should().Be("sensitiveInformation");
			data.Items[0].ChannelName.Should().BeEmpty("a channel from another department is never named");
			data.Items[0].ChannelType.Should().Be(-1);
			data.Items[1].ChannelName.Should().Be("Station 1");
		}

		[Test]
		public async Task A_non_admin_gets_the_services_empty_queue_and_no_channel_read()
		{
			var moderation = new Mock<IChatModerationService>(MockBehavior.Strict);
			moderation.Setup(x => x.GetFlagsAsync(DepartmentId, UserId, ChatFlagStatus.Open, 1, ChatModerationWidgetProvider.ReadCap)).ReturnsAsync(new List<ChatMessageFlag>());
			var channels = new Mock<IChatChannelService>(MockBehavior.Strict);
			var provider = new ChatModerationWidgetProvider(moderation.Object, channels.Object);

			var data = (ChatModerationWidgetData)await provider.GetDataAsync(For("chat.moderation"), CancellationToken.None);

			data.OpenCount.Should().Be(0);
			data.OldestFlaggedOnUtc.Should().BeNull();
		}
	}
}
