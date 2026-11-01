using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Communications;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Communications
{
	/// <summary>
	/// chat.unread (plan §7.5): the member's channels with unread messages — IChatChannelService.GetUnreadChannelsForUserAsync,
	/// the same channel list, member rows and arithmetic the v4 channel list uses — plus urgent messages waiting for their
	/// acknowledgement. An ack in a channel the member can no longer open is not counted.
	/// </summary>
	[DashboardWidget("chat.unread")]
	public class ChatUnreadWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IChatChannelService _chatChannelService;
		private readonly IChatMessageService _chatMessageService;

		public ChatUnreadWidgetProvider(IChatChannelService chatChannelService, IChatMessageService chatMessageService)
		{
			_chatChannelService = chatChannelService;
			_chatMessageService = chatMessageService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var acks = (await _chatMessageService.GetPendingAcksForUserAsync(context.DepartmentId, context.UserId) ?? new List<ChatMessageAck>())
				.Where(a => a != null && a.DepartmentId == context.DepartmentId && !a.AcknowledgedOn.HasValue && a.ChatChannelId != null)
				.GroupBy(a => a.ChatChannelId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

			// Read channels are needed only to place pending acks.
			var channels = await _chatChannelService.GetUnreadChannelsForUserAsync(context.DepartmentId, context.UserId, includeRead: acks.Count > 0)
				?? new List<ChatChannelUnread>();

			var rows = channels
				.Where(c => c?.Channel != null && c.Channel.DepartmentId == context.DepartmentId)
				.Select(c => new ChatUnreadChannel
				{
					ChatChannelId = c.Channel.ChatChannelId,
					Name = c.DisplayName ?? c.Channel.Name ?? string.Empty,
					ChannelType = c.Channel.ChannelType,
					UnreadCount = c.UnreadCount,
					PendingAcks = acks.TryGetValue(c.Channel.ChatChannelId, out var count) ? count : 0,
					LastMessageOnUtc = c.Channel.LastMessageOn,
					NotificationPreference = c.Member?.NotificationPreference ?? 0
				})
				.Where(c => c.UnreadCount > 0 || c.PendingAcks > 0)
				.ToList();

			return new ChatUnreadWidgetData
			{
				TotalUnread = rows.Sum(r => r.UnreadCount),
				UnreadChannelCount = rows.Count(r => r.UnreadCount > 0),
				PendingAckCount = rows.Sum(r => r.PendingAcks),
				Channels = rows
					.OrderByDescending(r => r.PendingAcks > 0)
					.ThenByDescending(r => r.LastMessageOnUtc ?? DateTime.MinValue)
					.Take(context.GetInt("maxItems", 5))
					.ToList()
			};
		}
	}
}
