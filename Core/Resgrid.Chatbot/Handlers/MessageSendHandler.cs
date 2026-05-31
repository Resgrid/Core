using System;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Localization;
using Resgrid.Chatbot.Models;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;

namespace Resgrid.Chatbot.Handlers
{
	/// <summary>
	/// Sends a message to a single department member resolved by name (intent <see cref="ChatbotIntentType.SendMessage"/>).
	/// Recipient resolution is department-scoped (security addendum §2/§3); responses localized to the user's culture.
	/// </summary>
	public class MessageSendHandler : IChatbotActionHandler
	{
		private readonly IMessageService _messageService;
		private readonly IChatbotUserSearchService _userSearchService;

		public MessageSendHandler(IMessageService messageService, IChatbotUserSearchService userSearchService)
		{
			_messageService = messageService;
			_userSearchService = userSearchService;
		}

		public ChatbotIntentType IntentType => ChatbotIntentType.SendMessage;

		public async Task<ChatbotResponse> HandleAsync(ChatbotMessage message, ChatbotIntent intent, ChatbotSession session)
		{
			var culture = session.Culture;
			try
			{
				intent.Parameters.TryGetValue("recipient", out var recipient);
				intent.Parameters.TryGetValue("body", out var body);

				if (string.IsNullOrWhiteSpace(recipient) || string.IsNullOrWhiteSpace(body))
					return new ChatbotResponse { Text = ChatbotResources.Get("Msg_SendUsage", culture), Processed = false };

				// Resolve the recipient strictly within the sender's active department.
				var match = await _userSearchService.ResolveSingleAsync(session.DepartmentId, recipient);
				if (match == null)
					return new ChatbotResponse { Text = ChatbotResources.Get("Msg_NoSingleMatch", culture, recipient), Processed = true };

				var msg = new Message
				{
					Subject = "Message via chatbot",
					Body = body.Truncate(4000),
					SendingUserId = session.UserId,
					SentOn = DateTime.UtcNow,
					IsBroadcast = false,
					Type = 0
				};
				msg.AddRecipient(match.UserId);

				var saved = await _messageService.SaveMessageAsync(msg);
				await _messageService.SendMessageAsync(saved, string.Empty, session.DepartmentId, false);

				var name = string.IsNullOrWhiteSpace(match.FullName) ? recipient : match.FullName.Trim();
				return new ChatbotResponse { Text = ChatbotResources.Get("Msg_MessageSent", culture, name), Processed = true };
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return new ChatbotResponse { Text = ChatbotResources.Get("Msg_ErrorSending", culture), Processed = false };
			}
		}
	}
}
