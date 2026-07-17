using System;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Localization;
using Resgrid.Chatbot.Models;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Chatbot.Handlers
{
	public class StatusActionHandler : IChatbotActionHandler
	{
		private readonly IActionLogsService _actionLogsService;
		private readonly ICustomStateService _customStateService;
		private readonly IChatbotDepartmentConfigService _departmentConfigService;

		public StatusActionHandler(IActionLogsService actionLogsService, ICustomStateService customStateService,
			IChatbotDepartmentConfigService departmentConfigService = null)
		{
			_actionLogsService = actionLogsService;
			_customStateService = customStateService;
			_departmentConfigService = departmentConfigService;
		}

		public ChatbotIntentType IntentType => ChatbotIntentType.SetStatus;

		public async Task<ChatbotResponse> HandleAsync(ChatbotMessage message, ChatbotIntent intent, ChatbotSession session)
		{
			try
			{
				CustomStateDetail resolvedStatus = null;

				if (intent.Parameters.TryGetValue("customActionId", out var customIdStr) && int.TryParse(customIdStr, out var customStatusId))
				{
					var statuses = await GetAvailableStatusesAsync(session.DepartmentId);
					resolvedStatus = Services.CustomStateMatcher.FindById(statuses, customStatusId);
				}
				else if (intent.Parameters.TryGetValue("actionType", out var actionTypeStr) && int.TryParse(actionTypeStr, out var actionTypeId))
				{
					resolvedStatus = await ResolveStatusIdAsync(session.DepartmentId, actionTypeId);
				}
				else if (intent.Parameters.TryGetValue("statusName", out var statusNameInput) && !string.IsNullOrWhiteSpace(statusNameInput))
				{
					resolvedStatus = await ResolveStatusNameAsync(session.DepartmentId, statusNameInput);
				}

				if (resolvedStatus == null)
					return new ChatbotResponse { Text = ChatbotResources.Get("Status_CouldNotDetermine", session.Culture), Processed = false };

				var statusId = resolvedStatus.CustomStateDetailId;

				var confirmed = intent.Parameters.TryGetValue("__confirmed", out var confirmFlag) && confirmFlag == "true";
				var config = _departmentConfigService == null
					? null
					: await _departmentConfigService.GetConfigAsync(session.DepartmentId);
				if (config?.RequireConfirmationForStatusChange == true && !confirmed)
				{
					session.State = ChatbotDialogState.AwaitingConfirmation;
					session.PendingIntent = ChatbotIntentType.SetStatus;
					session.Context.Clear();
					session.Context["actionType"] = statusId.ToString();
					return new ChatbotResponse
					{
						Text = ChatbotResources.Get("Unit_SetConfirm", session.Culture, "your status", resolvedStatus.ButtonText),
						Processed = true
					};
				}

				await _actionLogsService.SetUserActionAsync(session.UserId, session.DepartmentId, statusId);

				return new ChatbotResponse
				{
					Text = ChatbotResources.Get("Status_Updated", session.Culture, resolvedStatus.ButtonText),
					Processed = true
				};
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return new ChatbotResponse { Text = ChatbotResources.Get("Status_Error", session.Culture), Processed = false };
			}
		}

		private async Task<CustomStateDetail> ResolveStatusNameAsync(int departmentId, string statusName)
		{
			var name = statusName.Trim().TrimEnd('?', '!', '.', ',');
			var statuses = await GetAvailableStatusesAsync(departmentId);
			var match = Services.CustomStateMatcher.FindBySelection(statuses, name)
				?? Services.CustomStateMatcher.FindByName(statuses, name);
			if (match != null)
				return match;

			if (int.TryParse(name, out var selection))
			{
				var legacyId = selection switch
				{
					1 => (int)Model.ActionTypes.Responding,
					2 => (int)Model.ActionTypes.NotResponding,
					3 => (int)Model.ActionTypes.OnScene,
					4 => (int)Model.ActionTypes.StandingBy,
					_ => -1
				};
				if (legacyId >= 0)
					return await ResolveStatusIdAsync(departmentId, legacyId, statuses);
			}

			var baseType = Services.CustomStateMatcher.PersonnelBaseTypeFor(name);
			if (baseType.HasValue)
			{
				match = Services.CustomStateMatcher.FindByBaseType(statuses, baseType.Value);
				if (match == null && baseType == Model.ActionBaseTypes.Standby)
					match = Services.CustomStateMatcher.FindByBaseType(statuses, Model.ActionBaseTypes.Available);
			}

			if (match != null)
				return match;

			var fallback = FallbackStatusForName(name);
			return fallback != null && (statuses == null || statuses.Count == 0
				|| statuses.All(x => x.CustomStateDetailId <= 25))
				? fallback
				: null;
		}

		private async Task<CustomStateDetail> ResolveStatusIdAsync(int departmentId, int statusId,
			System.Collections.Generic.List<CustomStateDetail> statuses = null)
		{
			statuses ??= await GetAvailableStatusesAsync(departmentId);
			var match = Services.CustomStateMatcher.FindById(statuses, statusId);
			if (match != null)
				return match;

			var fallback = FallbackStatus(statusId);
			if (fallback == null)
				return null;

			match = Services.CustomStateMatcher.FindByName(statuses, fallback.ButtonText);
			var baseType = Services.CustomStateMatcher.PersonnelBaseTypeFor(fallback.ButtonText);
			if (match == null && baseType.HasValue)
				match = Services.CustomStateMatcher.FindByBaseType(statuses, baseType.Value);

			return match ?? ((statuses == null || statuses.Count == 0) ? fallback : null);
		}

		private async Task<System.Collections.Generic.List<CustomStateDetail>> GetAvailableStatusesAsync(int departmentId)
			=> _customStateService == null
				? null
				: await _customStateService.GetCustomPersonnelStatusesOrDefaultsAsync(departmentId);

		private static CustomStateDetail FallbackStatusForName(string name)
		{
			return Services.CustomStateMatcher.Normalize(name) switch
			{
				"responding" => FallbackStatus((int)Model.ActionTypes.Responding),
				"notresponding" => FallbackStatus((int)Model.ActionTypes.NotResponding),
				"onscene" => FallbackStatus((int)Model.ActionTypes.OnScene),
				"standingby" => FallbackStatus((int)Model.ActionTypes.StandingBy),
				"available" => FallbackStatus((int)Model.ActionTypes.AvailableStation),
				_ => null
			};
		}

		private static CustomStateDetail FallbackStatus(int statusId)
		{
			var name = statusId switch
			{
				(int)Model.ActionTypes.StandingBy => "Standing By",
				(int)Model.ActionTypes.NotResponding => "Not Responding",
				(int)Model.ActionTypes.Responding => "Responding",
				(int)Model.ActionTypes.OnScene => "On Scene",
				(int)Model.ActionTypes.AvailableStation => "Available Station",
				(int)Model.ActionTypes.RespondingToStation => "Responding to Station",
				(int)Model.ActionTypes.RespondingToScene => "Responding to Scene",
				(int)Model.ActionTypes.OnUnit => "On Unit",
				_ => null
			};

			return name == null ? null : new CustomStateDetail { CustomStateDetailId = statusId, ButtonText = name };
		}
	}
}
