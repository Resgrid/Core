using System;
using System.Text;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Localization;
using Resgrid.Chatbot.Models;
using Resgrid.Framework;
using Resgrid.Model.Services;

namespace Resgrid.Chatbot.Handlers
{
	/// <summary>
	/// Reads a single inbox message by id (intent <see cref="ChatbotIntentType.MessageDetail"/>). Ownership
	/// is enforced via the recipient row (security addendum §3); responses are localized to the user's culture.
	/// </summary>
	public class MessageReadHandler : IChatbotActionHandler
	{
		private readonly IMessageService _messageService;

		public MessageReadHandler(IMessageService messageService)
		{
			_messageService = messageService;
		}

		public ChatbotIntentType IntentType => ChatbotIntentType.MessageDetail;

		public async Task<ChatbotResponse> HandleAsync(ChatbotMessage message, ChatbotIntent intent, ChatbotSession session)
		{
			var culture = session.Culture;
			try
			{
				if (!intent.Parameters.TryGetValue("messageId", out var messageIdStr) || !int.TryParse(messageIdStr, out var messageId))
					return new ChatbotResponse { Text = ChatbotResources.Get("Msg_SpecifyNumberRead", culture), Processed = false };

				var msg = await _messageService.GetMessageByIdAsync(messageId);
				var recipient = await _messageService.GetMessageRecipientByMessageAndUserAsync(messageId, session.UserId);

				var isOwner = recipient != null || (msg != null && string.Equals(msg.SendingUserId, session.UserId, StringComparison.Ordinal));
				if (msg == null || !isOwner)
					return new ChatbotResponse { Text = ChatbotResources.Get("Msg_NotFound", culture, messageId), Processed = true };

				if (recipient != null && recipient.ReadOn == null)
					await _messageService.ReadMessageRecipientAsync(messageId, session.UserId);

				var subject = string.IsNullOrWhiteSpace(msg.Subject) ? ChatbotResources.Get("Msg_NoSubject", culture) : msg.Subject;

				var sb = new StringBuilder();
				sb.AppendLine(ChatbotResources.Get("Msg_ReadHeader", culture, msg.MessageId, subject));
				sb.AppendLine(ChatbotResources.Get("Msg_Sent", culture, msg.SentOn.ToString("g")));
				sb.AppendLine("----------------------");
				sb.AppendLine(msg.Body);
				sb.AppendLine();
				sb.AppendLine(ChatbotResources.Get("Msg_ReadFooter", culture, msg.MessageId));

				return new ChatbotResponse { Text = sb.ToString(), Processed = true };
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return new ChatbotResponse { Text = ChatbotResources.Get("Msg_ErrorRetrievingOne", culture), Processed = false };
			}
		}
	}
}
