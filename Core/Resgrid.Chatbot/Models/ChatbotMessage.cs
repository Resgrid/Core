using System;
using System.Collections.Generic;

namespace Resgrid.Chatbot.Models
{
	public class ChatbotMessage
	{
		public string MessageId { get; set; }
		public string From { get; set; }
		public string To { get; set; }
		public string Text { get; set; }
		public ChatbotPlatform Platform { get; set; }
		public DateTime Timestamp { get; set; }
		public Dictionary<string, object> PlatformMetadata { get; set; } = new Dictionary<string, object>();

		public string GetMetaString(string key, string defaultValue = null)
		{
			return PlatformMetadata.TryGetValue(key, out var val) ? val?.ToString() : defaultValue;
		}

		public string UserName => GetMetaString("username") ?? From;

		public string ChannelId => GetMetaString("channelId");

		public string GuildId => GetMetaString("guildId");
	}
}
