using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Communications
{
	/// <summary>
	/// chat.unread (plan §7.5): the member's channels with unread messages (LastMessageSeq − LastReadSeq, the v4 channel
	/// list's arithmetic) and the urgent messages waiting for their acknowledgement. Chat messages are not ADP-catalogued.
	/// </summary>
	public sealed class ChatUnreadWidgetData
	{
		public long TotalUnread { get; set; }

		public int UnreadChannelCount { get; set; }

		public int PendingAckCount { get; set; }

		/// <summary>Channels with unread messages (or a pending acknowledgement), newest activity first, capped.</summary>
		public List<ChatUnreadChannel> Channels { get; set; } = new List<ChatUnreadChannel>();
	}

	public sealed class ChatUnreadChannel
	{
		public string ChatChannelId { get; set; }

		/// <summary>Channel name; a direct message carries the counterpart's name (empty when it cannot be resolved).</summary>
		public string Name { get; set; }

		/// <summary>ChatChannelType value.</summary>
		public int ChannelType { get; set; }

		public long UnreadCount { get; set; }

		public int PendingAcks { get; set; }

		public DateTime? LastMessageOnUtc { get; set; }

		/// <summary>ChatNotificationPreference value of the member's row (0 when the member has none yet).</summary>
		public int NotificationPreference { get; set; }
	}
}
