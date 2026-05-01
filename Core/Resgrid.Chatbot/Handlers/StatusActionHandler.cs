using System;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;
using Resgrid.Model.Services;

namespace Resgrid.Chatbot.Handlers
{
	public class StatusActionHandler : IChatbotActionHandler
	{
		private readonly IActionLogsService _actionLogsService;
		private readonly ICustomStateService _customStateService;

		public StatusActionHandler(IActionLogsService actionLogsService, ICustomStateService customStateService)
		{
			_actionLogsService = actionLogsService;
			_customStateService = customStateService;
		}

		public ChatbotIntentType IntentType => ChatbotIntentType.SetStatus;

		public async Task<ChatbotResponse> HandleAsync(ChatbotMessage message, ChatbotIntent intent, ChatbotSession session)
		{
			try
			{
				int statusId;

				if (intent.Parameters.TryGetValue("customActionId", out var customIdStr) && int.TryParse(customIdStr, out var customStatusId))
				{
					statusId = customStatusId;
				}
				else if (intent.Parameters.TryGetValue("actionType", out var actionTypeStr) && int.TryParse(actionTypeStr, out var actionTypeId))
				{
					statusId = actionTypeId;
				}
				else
				{
					return new ChatbotResponse { Text = "Could not determine status to set. Text HELP for available commands.", Processed = false };
				}

				await _actionLogsService.SetUserActionAsync(session.UserId, session.DepartmentId, statusId);

				var customStates = await _customStateService.GetAllActiveCustomStatesForDepartmentAsync(session.DepartmentId);
				var action = await _actionLogsService.GetLastActionLogForUserAsync(session.UserId, session.DepartmentId);
				var statusName = await _customStateService.GetCustomPersonnelStatusAsync(session.DepartmentId, action);

				return new ChatbotResponse
				{
					Text = $"Status updated to: {statusName?.ButtonText ?? statusId.ToString()}",
					Processed = true
				};
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return new ChatbotResponse { Text = "Error updating status. Please try again.", Processed = false };
			}
		}
	}
}
