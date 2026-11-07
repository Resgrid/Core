using System;

namespace Resgrid.Model
{
	/// <summary>
	/// Unread arithmetic for chat channels, shared by the v4 channel list and IChatChannelService.GetUnreadChannelsForUserAsync
	/// so every surface reports the same number: messages past the participant's read pointer, never negative.
	/// </summary>
	public static class ChatReadState
	{
		/// <summary>LastMessageSeq − LastReadSeq for the participant's member row (no row = nothing read yet).</summary>
		public static long UnreadCount(ChatChannel channel, ChatChannelMember member)
		{
			if (channel == null)
				return 0;

			return UnreadCount(channel.LastMessageSeq, member);
		}

		/// <summary>The same arithmetic against a channel sequence read separately (the badge count reads it fresh).</summary>
		public static long UnreadCount(long lastMessageSeq, ChatChannelMember member)
		{
			return Math.Max(0, lastMessageSeq - (member?.LastReadSeq ?? 0));
		}
	}

	/// <summary>A channel the member can read, with their member row and its unread count (see <see cref="ChatReadState"/>).</summary>
	public sealed class ChatChannelUnread
	{
		public ChatChannel Channel { get; set; }

		/// <summary>The member's own row, or null when they have none yet (implicit-audience channels).</summary>
		public ChatChannelMember Member { get; set; }

		public long UnreadCount { get; set; }

		/// <summary>The channel name, or for a direct message the counterpart's display name (null when it cannot be resolved).</summary>
		public string DisplayName { get; set; }
	}
}
