using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Communications
{
	/// <summary>
	/// messages.inbox (plan §7.5): unread count and the newest unread messages. The inbox spans every department the member
	/// belongs to, exactly like the Inbox page; subjects are resolved per owning department (ADP <c>messages.subject</c>).
	/// </summary>
	public sealed class MessagesInboxWidgetData
	{
		/// <summary>The navbar's unread count (every unread inbox message).</summary>
		public int UnreadCount { get; set; }

		public List<MessagesInboxItem> Items { get; set; } = new List<MessagesInboxItem>();
	}

	public sealed class MessagesInboxItem
	{
		public int MessageId { get; set; }

		/// <summary>The sender's name; empty for a system-generated message.</summary>
		public string SenderName { get; set; }

		public bool SystemGenerated { get; set; }

		/// <summary>ADP <c>messages.subject</c>: plain unless the owning department is enforced and no grant revealed it.</summary>
		public DashboardProtectedText Subject { get; set; }

		public DateTime SentOnUtc { get; set; }

		/// <summary>MessageTypes value (0 = normal, 1 = callback, 2 = poll).</summary>
		public int Type { get; set; }

		public string Url { get; set; }
	}
}
