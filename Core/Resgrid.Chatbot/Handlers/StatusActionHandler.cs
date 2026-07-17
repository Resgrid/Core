using System;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Localization;
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
				else if (intent.Parameters.TryGetValue("statusName", out var statusNameInput) && !string.IsNullOrWhiteSpace(statusNameInput))
				{
					// "SET STATUS TO <name>": resolve against the department's custom personnel states
					// first (this is the only text form that can reach custom statuses), then the
					// standard status words.
					var resolved = await ResolveStatusNameAsync(session.DepartmentId, statusNameInput);
					if (resolved == null)
						return new ChatbotResponse { Text = ChatbotResources.Get("Status_CouldNotDetermine", session.Culture), Processed = false };

					statusId = resolved.Value;
				}
				else
				{
					return new ChatbotResponse { Text = ChatbotResources.Get("Status_CouldNotDetermine", session.Culture), Processed = false };
				}

				await _actionLogsService.SetUserActionAsync(session.UserId, session.DepartmentId, statusId);

				var customStates = await _customStateService.GetAllActiveCustomStatesForDepartmentAsync(session.DepartmentId);
				var action = await _actionLogsService.GetLastActionLogForUserAsync(session.UserId, session.DepartmentId);
				var statusName = await _customStateService.GetCustomPersonnelStatusAsync(session.DepartmentId, action);

				return new ChatbotResponse
				{
					Text = ChatbotResources.Get("Status_Updated", session.Culture, statusName?.ButtonText ?? statusId.ToString()),
					Processed = true
				};
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return new ChatbotResponse { Text = ChatbotResources.Get("Status_Error", session.Culture), Processed = false };
			}
		}

		private async Task<int?> ResolveStatusNameAsync(int departmentId, string statusName)
		{
			var name = statusName.Trim().TrimEnd('?', '!', '.', ',');

			var customStates = await _customStateService.GetAllActiveCustomStatesForDepartmentAsync(departmentId);
			var customActions = customStates?.FirstOrDefault(x => x.Type == (int)Model.CustomStateTypes.Personnel);
			if (customActions != null && !customActions.IsDeleted && customActions.GetActiveDetails()?.Any() == true)
			{
				var detail = customActions.GetActiveDetails()
					.FirstOrDefault(d => string.Equals(d.ButtonText?.Trim(), name, StringComparison.OrdinalIgnoreCase)
						|| string.Equals(d.ButtonText?.Replace(" ", ""), name.Replace(" ", ""), StringComparison.OrdinalIgnoreCase));

				if (detail != null)
					return detail.CustomStateDetailId;
			}

			// Standard status words (spaces optional).
			return name.Replace(" ", "").ToLowerInvariant() switch
			{
				"responding" => (int)Model.ActionTypes.Responding,
				"notresponding" => (int)Model.ActionTypes.NotResponding,
				"onscene" => (int)Model.ActionTypes.OnScene,
				"standingby" => (int)Model.ActionTypes.StandingBy,
				"available" => (int)Model.ActionTypes.AvailableStation,
				_ => (int?)null
			};
		}
	}
}
