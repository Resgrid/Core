using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;
using Resgrid.Framework;
using Resgrid.Model.Services;

namespace Resgrid.Chatbot.Handlers
{
	public class MessagesActionHandler : IChatbotActionHandler
	{
		private readonly ICommunicationService _communicationService;
		private readonly IUserProfileService _userProfileService;

		public MessagesActionHandler(ICommunicationService communicationService, IUserProfileService userProfileService)
		{
			_communicationService = communicationService;
			_userProfileService = userProfileService;
		}

		public ChatbotIntentType IntentType => ChatbotIntentType.ListMessages;

		public async Task<ChatbotResponse> HandleAsync(ChatbotMessage message, ChatbotIntent intent, ChatbotSession session)
		{
			try
			{
				// For Phase 1, return a simple message since we can't easily query unread messages via the current API
				// Phase 2+: Implement full message listing via new MessageService methods

				var profile = await _userProfileService.GetProfileByUserIdAsync(session.UserId);

				var sb = new StringBuilder();
				sb.AppendLine("Messages for " + (profile?.FullName.AsFirstNameLastName ?? "User") + ":");
				sb.AppendLine("----------------------");
				sb.AppendLine("Message listing via chatbot is coming soon.");
				sb.AppendLine("For now, please check your messages on the Resgrid web portal or mobile app.");

				return new ChatbotResponse { Text = sb.ToString(), Processed = true };
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return new ChatbotResponse { Text = "Error retrieving messages.", Processed = false };
			}
		}
	}
}
