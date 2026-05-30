using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Localization;
using Resgrid.Chatbot.Models;
using Resgrid.Framework;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;

namespace Resgrid.Chatbot.Handlers
{
	/// <summary>
	/// Lists the requesting user's unread inbox messages (intent <see cref="ChatbotIntentType.ListMessages"/>).
	/// Self-read only; responses are localized to the user's culture via <see cref="ChatbotResources"/>.
	/// </summary>
	public class MessagesActionHandler : IChatbotActionHandler
	{
		private const int MaxMessagesToList = 10;

		private readonly IMessageService _messageService;

		public MessagesActionHandler(IMessageService messageService)
		{
			_messageService = messageService;
		}

		public ChatbotIntentType IntentType => ChatbotIntentType.ListMessages;

		public async Task<ChatbotResponse> HandleAsync(ChatbotMessage message, ChatbotIntent intent, ChatbotSession session)
		{
			var culture = session.Culture;
			try
			{
				var messages = await _messageService.GetUnreadInboxMessagesByUserIdAsync(session.UserId);

				if (messages == null || messages.Count == 0)
					return new ChatbotResponse { Text = ChatbotResources.Get("Msg_NoUnread", culture), Processed = true };

				var sb = new StringBuilder();
				sb.AppendLine(ChatbotResources.Get("Msg_UnreadCount", culture, messages.Count));
				sb.AppendLine("----------------------");

				foreach (var msg in messages.OrderByDescending(m => m.SentOn).Take(MaxMessagesToList))
				{
					var subject = string.IsNullOrWhiteSpace(msg.Subject)
						? ChatbotResources.Get("Msg_NoSubject", culture)
						: msg.Subject.Truncate(60);
					sb.AppendLine(ChatbotResources.Get("Msg_ListItem", culture, msg.MessageId, subject, msg.SentOn.ToString("g")));
				}

				if (messages.Count > MaxMessagesToList)
					sb.AppendLine(ChatbotResources.Get("Msg_AndMore", culture, messages.Count - MaxMessagesToList));

				sb.AppendLine();
				sb.AppendLine(ChatbotResources.Get("Msg_ListReadHint", culture));

				return new ChatbotResponse { Text = sb.ToString(), Processed = true };
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return new ChatbotResponse { Text = ChatbotResources.Get("Msg_ErrorRetrieving", culture), Processed = false };
			}
		}
	}
}
