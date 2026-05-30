using System.Collections.Generic;
using System.Linq;
using Resgrid.Chatbot.Models;
using Resgrid.Providers.Chatbot.Interfaces;

namespace Resgrid.Providers.Chatbot.Services
{
	/// <summary>
	/// Maps <see cref="ChatbotPlatform"/> to its registered <see cref="IChatbotPlatformAdapter"/> and
	/// encodes the per-platform proactive-initiation constraints from Phase 3 §15.3.
	/// </summary>
	public class ChatbotAdapterRegistry : IChatbotAdapterRegistry
	{
		private readonly Dictionary<ChatbotPlatform, IChatbotPlatformAdapter> _adapters;

		public ChatbotAdapterRegistry(IEnumerable<IChatbotPlatformAdapter> adapters)
		{
			_adapters = (adapters ?? Enumerable.Empty<IChatbotPlatformAdapter>())
				.GroupBy(a => a.Platform)
				.ToDictionary(g => g.Key, g => g.First());
		}

		public IChatbotPlatformAdapter GetAdapter(ChatbotPlatform platform)
			=> _adapters.TryGetValue(platform, out var adapter) ? adapter : null;

		/// <summary>
		/// Whether the bot may send an un-prompted message on this platform. SMS is delivered by the
		/// dedicated SMS channel (not here). Telegram/Teams/Discord/WhatsApp can't be initiated without a
		/// prior conversation reference or an approved template, so they return false until that capture
		/// lands; the caller then falls back to the user's other channels.
		/// </summary>
		public bool CanInitiateProactively(ChatbotPlatform platform)
		{
			switch (platform)
			{
				case ChatbotPlatform.Slack:
				case ChatbotPlatform.Signal:
				case ChatbotPlatform.WebChat:
					return true;
				default:
					return false; // WhatsApp, Discord, Telegram, Teams (conditional), SMS (other channel)
			}
		}
	}
}
