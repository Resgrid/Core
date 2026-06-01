using System;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Localization;
using Resgrid.Chatbot.Models;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Chatbot.Handlers
{
	/// <summary>
	/// Sets a unit's status (intent <see cref="ChatbotIntentType.SetUnitStatus"/>). Shared-resource mutation
	/// → requires confirmation (security addendum §5). Unit resolved within the department (anti-IDOR §3),
	/// status must be a department Unit custom state, and <see cref="IAuthorizationService.CanUserModifyUnitAsync"/>
	/// is re-checked on both passes (§2). Responses are localized to the user's culture.
	/// </summary>
	public class SetUnitStatusHandler : IChatbotActionHandler
	{
		private readonly IUnitsService _unitsService;
		private readonly ICustomStateService _customStateService;
		private readonly IAuthorizationService _authorizationService;

		public SetUnitStatusHandler(IUnitsService unitsService, ICustomStateService customStateService, IAuthorizationService authorizationService)
		{
			_unitsService = unitsService;
			_customStateService = customStateService;
			_authorizationService = authorizationService;
		}

		public ChatbotIntentType IntentType => ChatbotIntentType.SetUnitStatus;

		public async Task<ChatbotResponse> HandleAsync(ChatbotMessage message, ChatbotIntent intent, ChatbotSession session)
		{
			var culture = session.Culture;
			try
			{
				intent.Parameters.TryGetValue("unitName", out var unitName);
				intent.Parameters.TryGetValue("status", out var statusName);

				if (string.IsNullOrWhiteSpace(unitName) || string.IsNullOrWhiteSpace(statusName))
					return new ChatbotResponse { Text = ChatbotResources.Get("Unit_SetUsage", culture), Processed = false };

				var unitStatuses = await _unitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync(session.DepartmentId);
				var unitNameLower = unitName.Trim().ToLowerInvariant();
				var unit = (unitStatuses?.FirstOrDefault(u => u.Unit?.Name != null && u.Unit.Name.ToLowerInvariant() == unitNameLower)
					?? unitStatuses?.FirstOrDefault(u => u.Unit?.Name != null && u.Unit.Name.ToLowerInvariant().Contains(unitNameLower)))?.Unit;

				if (unit == null)
					return new ChatbotResponse { Text = ChatbotResources.Get("Unit_NotFound", culture, unitName), Processed = true };

				if (!await _authorizationService.CanUserModifyUnitAsync(session.UserId, unit.UnitId))
					return new ChatbotResponse { Text = ChatbotResources.Get("Unit_NoPermission", culture), Processed = false };

				var customStates = await _customStateService.GetAllActiveCustomStatesForDepartmentAsync(session.DepartmentId);
				var statusLower = statusName.Trim().ToLowerInvariant();
				var matchedState = customStates?
					.Where(s => s.Type == (int)CustomStateTypes.Unit)
					.SelectMany(s => s.GetActiveDetails())
					.FirstOrDefault(d => d.ButtonText != null && d.ButtonText.ToLowerInvariant() == statusLower);

				if (matchedState == null)
					return new ChatbotResponse { Text = ChatbotResources.Get("Unit_UnknownStatus", culture, statusName), Processed = true };

				var confirmed = intent.Parameters.TryGetValue("__confirmed", out var confirmFlag) && confirmFlag == "true";
				if (!confirmed)
				{
					session.State = ChatbotDialogState.AwaitingConfirmation;
					session.PendingIntent = ChatbotIntentType.SetUnitStatus;
					session.Context["unitName"] = unit.Name;
					session.Context["status"] = matchedState.ButtonText;
					return new ChatbotResponse
					{
						Text = ChatbotResources.Get("Unit_SetConfirm", culture, unit.Name, matchedState.ButtonText),
						Processed = true
					};
				}

				await _unitsService.SetUnitStateAsync(unit.UnitId, matchedState.CustomStateDetailId, session.DepartmentId);

				return new ChatbotResponse { Text = ChatbotResources.Get("Unit_SetDone", culture, unit.Name, matchedState.ButtonText), Processed = true };
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return new ChatbotResponse { Text = ChatbotResources.Get("Unit_ErrorSet", culture), Processed = false };
			}
		}
	}
}
