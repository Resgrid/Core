using System;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Providers.Chatbot.Services
{
	/// <summary>
	/// Outbound-boundary net for chat platforms (ADP plan section 7.5.1). Discord, Slack and
	/// Telegram messages leave through <see cref="IChatbotOutboundService"/>, so wrapping the one
	/// interface covers every platform adapter behind it.
	///
	/// This channel is worth covering even though the dispatch path already projects: a chat
	/// message lands in a third-party service's storage and, on a shared channel, in front of
	/// people who are not the intended recipient. Like the other carrier decorators it scrubs and
	/// logs rather than refusing — a degraded message still tells someone to open the app.
	/// </summary>
	public class ProtectedChatbotOutboundDecorator : IChatbotOutboundService
	{
		private readonly IChatbotOutboundService _inner;

		public ProtectedChatbotOutboundDecorator(IChatbotOutboundService inner)
		{
			_inner = inner;
		}

		public async Task<ChatbotOutboundResult> SendToUserAsync(string userId, int departmentId,
			ChatbotOutboundMessage message)
		{
			try
			{
				if (message != null)
				{
					var scrubbed = 0;

					message.Title = ProtectedOutboundGuard.Scrub(message.Title, out var titleCount);
					scrubbed += titleCount;

					message.Body = ProtectedOutboundGuard.Scrub(message.Body, out var bodyCount);
					scrubbed += bodyCount;

					if (scrubbed > 0)
						Logging.LogError($"ADP outbound net scrubbed {scrubbed} enveloped value(s) from a chat-platform message " +
							$"for department {departmentId}. A notification path is missing its protected projection.");
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"ProtectedChatbotOutboundDecorator failed while sanitizing a chat message for department {departmentId}");
			}

			return await _inner.SendToUserAsync(userId, departmentId, message);
		}
	}
}
