using System;
using System.Collections.Generic;

namespace Resgrid.Chatbot.Models
{
	public class ChatbotPlatformCapabilities
	{
		public bool SupportsMarkdown { get; set; }
		public bool SupportsButtons { get; set; }
		public bool SupportsQuickReplies { get; set; }
		public bool SupportsEmbeds { get; set; }
		public bool SupportsImages { get; set; }
		public bool SupportsSelectMenus { get; set; }
		public bool SupportsModals { get; set; }
		public int MaxMessageLength { get; set; } = 1600;
		public bool SupportsTypingIndicator { get; set; }

		public static ChatbotPlatformCapabilities ForPlatform(ChatbotPlatform platform)
		{
			return platform switch
			{
				ChatbotPlatform.SmsTwilio => new ChatbotPlatformCapabilities
				{
					MaxMessageLength = 1600,
					SupportsMarkdown = false,
					SupportsButtons = false,
					SupportsEmbeds = false,
					SupportsImages = false,
					SupportsSelectMenus = false,
					SupportsModals = false,
					SupportsQuickReplies = false,
					SupportsTypingIndicator = false,
				},
				ChatbotPlatform.SmsSignalWire => new ChatbotPlatformCapabilities
				{
					MaxMessageLength = 1600,
					SupportsMarkdown = false,
					SupportsButtons = false,
					SupportsEmbeds = false,
					SupportsImages = false,
					SupportsSelectMenus = false,
					SupportsModals = false,
					SupportsQuickReplies = false,
					SupportsTypingIndicator = false,
				},
				ChatbotPlatform.Discord => new ChatbotPlatformCapabilities
				{
					MaxMessageLength = 2000,
					SupportsMarkdown = true,
					SupportsButtons = true,
					SupportsEmbeds = true,
					SupportsImages = true,
					SupportsSelectMenus = true,
					SupportsModals = true,
					SupportsQuickReplies = false,
					SupportsTypingIndicator = true,
				},
				ChatbotPlatform.Slack => new ChatbotPlatformCapabilities
				{
					MaxMessageLength = 3000,
					SupportsMarkdown = true,
					SupportsButtons = true,
					SupportsEmbeds = false,
					SupportsImages = true,
					SupportsSelectMenus = true,
					SupportsModals = true,
					SupportsQuickReplies = false,
					SupportsTypingIndicator = false,
				},
				ChatbotPlatform.Telegram => new ChatbotPlatformCapabilities
				{
					MaxMessageLength = 4096,
					SupportsMarkdown = true,
					SupportsButtons = true,
					SupportsEmbeds = false,
					SupportsImages = true,
					SupportsSelectMenus = false,
					SupportsModals = false,
					SupportsQuickReplies = false,
					SupportsTypingIndicator = false,
				},
				ChatbotPlatform.WebChat => new ChatbotPlatformCapabilities
				{
					MaxMessageLength = 4000,
					SupportsMarkdown = true,
					SupportsButtons = true,
					SupportsEmbeds = true,
					SupportsImages = true,
					SupportsSelectMenus = true,
					SupportsModals = false,
					SupportsQuickReplies = true,
					SupportsTypingIndicator = true,
				},
				ChatbotPlatform.WhatsApp => new ChatbotPlatformCapabilities
				{
					MaxMessageLength = 1024,
					SupportsMarkdown = false,
					SupportsButtons = true,
					SupportsEmbeds = false,
					SupportsImages = true,
					SupportsSelectMenus = false,
					SupportsModals = false,
					SupportsQuickReplies = true,
					SupportsTypingIndicator = false,
				},
				ChatbotPlatform.MicrosoftTeams => new ChatbotPlatformCapabilities
				{
					MaxMessageLength = 4000,
					SupportsMarkdown = true,
					SupportsButtons = true,
					SupportsEmbeds = true,
					SupportsImages = true,
					SupportsSelectMenus = true,
					SupportsModals = false,
					SupportsQuickReplies = false,
					SupportsTypingIndicator = true,
				},
				ChatbotPlatform.Signal => new ChatbotPlatformCapabilities
				{
					MaxMessageLength = 2000,
					SupportsMarkdown = false,
					SupportsButtons = false,
					SupportsEmbeds = false,
					SupportsImages = true,
					SupportsSelectMenus = false,
					SupportsModals = false,
					SupportsQuickReplies = false,
					SupportsTypingIndicator = false,
				},
				_ => new ChatbotPlatformCapabilities()
			};
		}
	}
}
