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
	/// Records the requesting user's response (yes/no/ack) to an inbox message
	/// (intent <see cref="ChatbotIntentType.RespondToMessage"/>). Scoped to the user's own recipient row;
	/// responses localized to the user's culture.
	/// </summary>
	public class MessageRespondHandler : IChatbotActionHandler
	{
		private readonly IMessageService _messageService;

		public MessageRespondHandler(IMessageService messageService)
		{
			_messageService = messageService;
		}

		public ChatbotIntentType IntentType => ChatbotIntentType.RespondToMessage;

		public async Task<ChatbotResponse> HandleAsync(ChatbotMessage message, ChatbotIntent intent, ChatbotSession session)
		{
			var culture = session.Culture;
			try
			{
				intent.Parameters.TryGetValue("messageId", out var messageIdStr);
				intent.Parameters.TryGetValue("response", out var response);

				if (string.IsNullOrWhiteSpace(messageIdStr) || !int.TryParse(messageIdStr, out var messageId) || string.IsNullOrWhiteSpace(response))
					return new ChatbotResponse { Text = ChatbotResources.Get("Msg_RespondUsage", culture), Processed = false };

				var recipient = await _messageService.GetMessageRecipientByMessageAndUserAsync(messageId, session.UserId);
				if (recipient == null)
					return new ChatbotResponse { Text = ChatbotResources.Get("Msg_NotFound", culture, messageId), Processed = true };

				recipient.Response = response;
				if (recipient.ReadOn == null)
					recipient.ReadOn = DateTime.UtcNow;

				await _messageService.SaveMessageRecipientAsync(recipient);

				return new ChatbotResponse { Text = ChatbotResources.Get("Msg_ResponseRecorded", culture, response, messageId), Processed = true };
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return new ChatbotResponse { Text = ChatbotResources.Get("Msg_ErrorResponding", culture), Processed = false };
			}
		}
	}
}
