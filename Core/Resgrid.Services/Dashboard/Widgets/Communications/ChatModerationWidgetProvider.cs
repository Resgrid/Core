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
	/// chat.moderation (plan §7.5): open chat flags. GetFlagsAsync re-checks department-admin authority itself (a non-admin
	/// gets an empty queue) and reads one capped page; the flag note (ADP) is never selected.
	/// </summary>
	[DashboardWidget("chat.moderation")]
	public class ChatModerationWidgetProvider : IDashboardWidgetDataProvider
	{
		/// <summary>The most flags read (the service's page-size ceiling).</summary>
		public const int ReadCap = 100;

		private readonly IChatModerationService _chatModerationService;
		private readonly IChatChannelService _chatChannelService;

		public ChatModerationWidgetProvider(IChatModerationService chatModerationService, IChatChannelService chatChannelService)
		{
			_chatModerationService = chatModerationService;
			_chatChannelService = chatChannelService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var flags = (await _chatModerationService.GetFlagsAsync(context.DepartmentId, context.UserId, ChatFlagStatus.Open, 1, ReadCap) ?? new List<ChatMessageFlag>())
				.Where(f => f != null && f.DepartmentId == context.DepartmentId && f.Status == (int)ChatFlagStatus.Open)
				.ToList();

			var data = new ChatModerationWidgetData
			{
				OpenCount = flags.Count,
				CountCapped = flags.Count >= ReadCap,
				OldestFlaggedOnUtc = flags.Count > 0 ? flags.Min(f => f.FlaggedOn) : (DateTime?)null
			};

			var shown = flags.OrderBy(f => f.FlaggedOn).Take(context.GetInt("maxItems", 5)).ToList();
			if (shown.Count == 0)
				return data;

			var channels = (await _chatChannelService.GetChannelsByIdsAsync(shown.Select(f => f.ChatChannelId).Where(id => id != null)) ?? new List<ChatChannel>())
				.Where(c => c != null && c.DepartmentId == context.DepartmentId)
				.GroupBy(c => c.ChatChannelId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

			foreach (var flag in shown)
			{
				var channel = flag.ChatChannelId != null && channels.TryGetValue(flag.ChatChannelId, out var found) ? found : null;
				data.Items.Add(new ChatModerationFlagItem
				{
					ChatMessageFlagId = flag.ChatMessageFlagId,
					ChatChannelId = flag.ChatChannelId,
					ChannelName = channel?.Name ?? string.Empty,
					ChannelType = channel?.ChannelType ?? -1,
					Reason = flag.Reason,
					ReasonKey = ReasonKey(flag.Reason),
					FlaggedOnUtc = flag.FlaggedOn
				});
			}

			return data;
		}

		internal static string ReasonKey(int reason)
		{
			var name = Enum.IsDefined(typeof(ChatFlagReason), reason) ? ((ChatFlagReason)reason).ToString() : ChatFlagReason.Other.ToString();
			return char.ToLowerInvariant(name[0]) + name.Substring(1);
		}
	}
}
