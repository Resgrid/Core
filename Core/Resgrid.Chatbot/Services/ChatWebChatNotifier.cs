using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
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
	/// InstancePerLifetimeScope, so the scoped chat services are constructor-injected (no ServiceLocator).
	/// </summary>
	public class ChatWebChatNotifier : IChatbotWebChatNotifier
	{
		private readonly IDepartmentsService _departmentsService;
		private readonly IChatChannelService _chatChannelService;
		private readonly IChatMessageService _chatMessageService;

		public ChatWebChatNotifier(IDepartmentsService departmentsService, IChatChannelService chatChannelService, IChatMessageService chatMessageService)
		{
			_departmentsService = departmentsService;
			_chatChannelService = chatChannelService;
			_chatMessageService = chatMessageService;
		}

		public Task PushToUserAsync(string userId, string text) => PushToUserAsync(userId, text, 0);

		public async Task PushToUserAsync(string userId, string text, int departmentId)
		{
			try
			{
				if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(text))
					return;

				// Prefer the ingress-resolved department; a user can belong to many departments and the
				// reply must land in the department the message actually came from. Only when the caller
				// doesn't know the department do we fall back to the first active membership.
				var targetDepartmentId = departmentId;
				if (targetDepartmentId <= 0)
				{
					var memberships = await _departmentsService.GetAllDepartmentsForUserAsync(userId);
					var membership = memberships?.FirstOrDefault(m => !m.IsDisabled.GetValueOrDefault() && !m.IsDeleted);
					if (membership == null)
						return;

					targetDepartmentId = membership.DepartmentId;
				}

				var channel = await _chatChannelService.EnsureChatbotChannelAsync(targetDepartmentId, userId);
				if (channel == null)
					return;

				await _chatMessageService.SendBotMessageAsync(channel.ChatChannelId,
					channel.DepartmentId.ToString(CultureInfo.InvariantCulture), text, "Resgrid Assistant");
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
		}
	}
}
