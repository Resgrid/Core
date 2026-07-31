using System;
using System.Linq;
using System.Threading.Tasks;
using CommonServiceLocator;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Chatbot.Services
{
	/// <summary>
	/// Real Web Chat notifier: delivers a chatbot response into the user's durable chatbot chat channel
	/// via the chat message pipeline, which persists it (history survives restarts) and fans it out over
	/// SignalR to every one of the user's connected apps. Replaces <see cref="NullChatbotWebChatNotifier"/>.
	/// </summary>
	public class ChatWebChatNotifier : IChatbotWebChatNotifier
	{
		public async Task PushToUserAsync(string userId, string text)
		{
			try
			{
				if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(text))
					return;

				// Resolved lazily per call: chat services are scoped and this notifier is a singleton.
				var departmentsService = ServiceLocator.Current.GetInstance<IDepartmentsService>();
				var chatChannelService = ServiceLocator.Current.GetInstance<IChatChannelService>();
				var chatMessageService = ServiceLocator.Current.GetInstance<IChatMessageService>();

				var memberships = await departmentsService.GetAllDepartmentsForUserAsync(userId);
				var membership = memberships?.FirstOrDefault(m => !m.IsDisabled.GetValueOrDefault() && !m.IsDeleted);
				if (membership == null)
					return;

				var channel = await chatChannelService.EnsureChatbotChannelAsync(membership.DepartmentId, userId);
				if (channel == null)
					return;

				await chatMessageService.SendMessageAsync(new ChatMessageSendRequest
				{
					ChatChannelId = channel.ChatChannelId,
					DepartmentId = channel.DepartmentId,
					AsBot = true,
					Body = text,
					MessageType = ChatMessageType.Bot,
					Priority = ChatMessagePriority.Normal,
					SenderDisplayName = "Resgrid Assistant"
				});
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
		}
	}
}
