using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Localization;
using Resgrid.Chatbot.Models;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Chatbot.Handlers
{
	public class StaffingActionHandler : IChatbotActionHandler
	{
		private readonly IUserStateService _userStateService;
		private readonly ICustomStateService _customStateService;

		public StaffingActionHandler(IUserStateService userStateService, ICustomStateService customStateService)
		{
			_userStateService = userStateService;
			_customStateService = customStateService;
		}

		public ChatbotIntentType IntentType => ChatbotIntentType.SetStaffing;

		public async Task<ChatbotResponse> HandleAsync(ChatbotMessage message, ChatbotIntent intent, ChatbotSession session)
		{
			try
			{
				CustomStateDetail resolvedStaffing = null;

				if (intent.Parameters.TryGetValue("customStaffingId", out var customIdRaw)
					&& int.TryParse(customIdRaw, out var customId))
				{
					resolvedStaffing = Services.CustomStateMatcher.FindById(
						await GetAvailableStaffingAsync(session.DepartmentId), customId);
				}
				else if (intent.Parameters.TryGetValue("staffingType", out var staffingTypeRaw)
					&& int.TryParse(staffingTypeRaw, out var staffingType))
				{
					resolvedStaffing = await ResolveStaffingIdAsync(session.DepartmentId, staffingType);
				}
				else if (intent.Parameters.TryGetValue("staffingName", out var staffingName)
					&& !string.IsNullOrWhiteSpace(staffingName))
				{
					resolvedStaffing = await ResolveStaffingNameAsync(session.DepartmentId, staffingName);
				}

				if (resolvedStaffing == null)
					return new ChatbotResponse
					{
						Text = ChatbotResources.Get("Staffing_CouldNotDetermine", session.Culture),
						Processed = false
					};

				await _userStateService.CreateUserState(session.UserId, session.DepartmentId,
					resolvedStaffing.CustomStateDetailId);

				return new ChatbotResponse
				{
					Text = ChatbotResources.Get("Staffing_Updated", session.Culture, resolvedStaffing.ButtonText),
					Processed = true
				};
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return new ChatbotResponse
				{
					Text = ChatbotResources.Get("Staffing_Error", session.Culture),
					Processed = false
				};
			}
		}

		private async Task<CustomStateDetail> ResolveStaffingNameAsync(int departmentId, string staffingName)
		{
			var name = staffingName.Trim().TrimEnd('?', '!', '.', ',');
			var levels = await GetAvailableStaffingAsync(departmentId);
			var match = Services.CustomStateMatcher.FindBySelection(levels, name)
				?? Services.CustomStateMatcher.FindByName(levels, name);
			if (match != null)
				return match;

			var builtInId = Services.CustomStateMatcher.Normalize(name) switch
			{
				"available" => (int)UserStateTypes.Available,
				"delayed" => (int)UserStateTypes.Delayed,
				"unavailable" => (int)UserStateTypes.Unavailable,
				"committed" => (int)UserStateTypes.Committed,
				"onshift" => (int)UserStateTypes.OnShift,
				_ => -1
			};

			return builtInId >= 0
				? await ResolveStaffingIdAsync(departmentId, builtInId, levels)
				: null;
		}

		private async Task<CustomStateDetail> ResolveStaffingIdAsync(int departmentId, int staffingId,
			List<CustomStateDetail> levels = null)
		{
			levels ??= await GetAvailableStaffingAsync(departmentId);
			var match = Services.CustomStateMatcher.FindById(levels, staffingId);
			if (match != null)
				return match;

			var fallback = FallbackStaffing(staffingId);
			if (fallback == null)
				return null;

			match = Services.CustomStateMatcher.FindByName(levels, fallback.ButtonText);
			if (match != null)
				return match;

			return levels == null || levels.Count == 0 ? fallback : null;
		}

		private async Task<List<CustomStateDetail>> GetAvailableStaffingAsync(int departmentId)
			=> _customStateService == null
				? null
				: await _customStateService.GetCustomPersonnelStaffingsOrDefaultsAsync(departmentId);

		private static CustomStateDetail FallbackStaffing(int staffingId)
		{
			var name = staffingId switch
			{
				(int)UserStateTypes.Available => "Available",
				(int)UserStateTypes.Delayed => "Delayed",
				(int)UserStateTypes.Unavailable => "Unavailable",
				(int)UserStateTypes.Committed => "Committed",
				(int)UserStateTypes.OnShift => "On Shift",
				_ => null
			};

			return name == null
				? null
				: new CustomStateDetail { CustomStateDetailId = staffingId, ButtonText = name };
		}
	}
}
