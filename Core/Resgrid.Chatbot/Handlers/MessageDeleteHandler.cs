using System;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Localization;
using Resgrid.Chatbot.Models;
using Resgrid.Framework;
using Resgrid.Model.Services;

namespace Resgrid.Chatbot.Handlers
{
	/// <summary>
	/// Removes a message from the requesting user's inbox (intent <see cref="ChatbotIntentType.DeleteMessage"/>).
	/// Scoped by construction to the user's own recipient row; responses localized to the user's culture.
	/// </summary>
	public class MessageDeleteHandler : IChatbotActionHandler
	{
		private readonly IMessageService _messageService;

		public MessageDeleteHandler(IMessageService messageService)
		{
			_messageService = messageService;
		}

		public ChatbotIntentType IntentType => ChatbotIntentType.DeleteMessage;

		public async Task<ChatbotResponse> HandleAsync(ChatbotMessage message, ChatbotIntent intent, ChatbotSession session)
		{
			var culture = session.Culture;
			try
			{
				if (!intent.Parameters.TryGetValue("messageId", out var messageIdStr) || !int.TryParse(messageIdStr, out var messageId))
					return new ChatbotResponse { Text = ChatbotResources.Get("Msg_SpecifyNumberDelete", culture), Processed = false };

				var recipient = await _messageService.GetMessageRecipientByMessageAndUserAsync(messageId, session.UserId);
				if (recipient == null)
					return new ChatbotResponse { Text = ChatbotResources.Get("Msg_NotFound", culture, messageId), Processed = true };

				await _messageService.MarkMessageRecipientAsDeletedAsync(messageId, session.UserId);

				return new ChatbotResponse { Text = ChatbotResources.Get("Msg_Deleted", culture, messageId), Processed = true };
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return new ChatbotResponse { Text = ChatbotResources.Get("Msg_ErrorDeleting", culture), Processed = false };
			}
		}
	}
}
