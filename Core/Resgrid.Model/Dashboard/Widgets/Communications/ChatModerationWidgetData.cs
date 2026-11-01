using System;
using System.Collections.Generic;

namespace Resgrid.Model.Dashboard.Widgets.Communications
{
	/// <summary>
	/// chat.moderation (plan §7.5): open chat flags for department administrators (the flag queue is theirs alone —
	/// IChatModerationService.GetFlagsAsync). The flag note is ADP-catalogued and never selected here.
	/// </summary>
	public sealed class ChatModerationWidgetData
	{
		public int OpenCount { get; set; }

		/// <summary>True when OpenCount reached the read cap (show "100+").</summary>
		public bool CountCapped { get; set; }

		public DateTime? OldestFlaggedOnUtc { get; set; }

		/// <summary>The oldest open flags first.</summary>
		public List<ChatModerationFlagItem> Items { get; set; } = new List<ChatModerationFlagItem>();
	}

	public sealed class ChatModerationFlagItem
	{
		public string ChatMessageFlagId { get; set; }

		public string ChatChannelId { get; set; }

		/// <summary>Empty for a direct message or a channel that no longer exists (the client labels it by ChannelType).</summary>
		public string ChannelName { get; set; }

		/// <summary>ChatChannelType value, -1 when the channel no longer exists.</summary>
		public int ChannelType { get; set; } = -1;

		/// <summary>ChatFlagReason value.</summary>
		public int Reason { get; set; }

		/// <summary>ChatFlagReason name in lower camel ("spam", "harassment"…), the client's label key.</summary>
		public string ReasonKey { get; set; }

		public DateTime FlaggedOnUtc { get; set; }
	}
}
