using System;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Localization;
using Resgrid.Chatbot.Models;
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
				int staffingId;

				if (intent.Parameters.TryGetValue("customStaffingId", out var customIdStr) && int.TryParse(customIdStr, out var customStaffingId))
				{
					staffingId = customStaffingId;
				}
				else if (intent.Parameters.TryGetValue("staffingType", out var staffingTypeStr) && int.TryParse(staffingTypeStr, out var staffingTypeId))
				{
					staffingId = staffingTypeId;
				}
				else if (intent.Parameters.TryGetValue("staffingName", out var staffingNameInput) && !string.IsNullOrWhiteSpace(staffingNameInput))
				{
					// "SET STAFFING TO <name>": resolve against the department's custom staffing states
					// first (the only text form that can reach custom staffing levels), then the
					// standard staffing words.
					var resolved = await ResolveStaffingNameAsync(session.DepartmentId, staffingNameInput);
					if (resolved == null)
						return new ChatbotResponse { Text = ChatbotResources.Get("Staffing_CouldNotDetermine", session.Culture), Processed = false };

					staffingId = resolved.Value;
				}
				else
				{
					return new ChatbotResponse { Text = ChatbotResources.Get("Staffing_CouldNotDetermine", session.Culture), Processed = false };
				}

				await _userStateService.CreateUserState(session.UserId, session.DepartmentId, staffingId);

				var userStaffing = await _userStateService.GetLastUserStateByUserIdAsync(session.UserId);
				var staffingName = await _customStateService.GetCustomPersonnelStaffingAsync(session.DepartmentId, userStaffing);

				return new ChatbotResponse
				{
					Text = ChatbotResources.Get("Staffing_Updated", session.Culture, staffingName?.ButtonText ?? staffingId.ToString()),
					Processed = true
				};
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return new ChatbotResponse { Text = ChatbotResources.Get("Staffing_Error", session.Culture), Processed = false };
			}
		}

		private async Task<int?> ResolveStaffingNameAsync(int departmentId, string staffingName)
		{
			var name = staffingName.Trim().TrimEnd('?', '!', '.', ',');

			var customStates = await _customStateService.GetAllActiveCustomStatesForDepartmentAsync(departmentId);
			var customStaffing = customStates?.FirstOrDefault(x => x.Type == (int)Model.CustomStateTypes.Staffing);
			if (customStaffing != null && !customStaffing.IsDeleted && customStaffing.GetActiveDetails()?.Any() == true)
			{
				var detail = customStaffing.GetActiveDetails()
					.FirstOrDefault(d => string.Equals(d.ButtonText?.Trim(), name, StringComparison.OrdinalIgnoreCase)
						|| string.Equals(d.ButtonText?.Replace(" ", ""), name.Replace(" ", ""), StringComparison.OrdinalIgnoreCase));

				if (detail != null)
					return detail.CustomStateDetailId;
			}

			// Standard staffing words (spaces optional) — UserStateTypes enum values.
			return name.Replace(" ", "").ToLowerInvariant() switch
			{
				"available" => (int)Model.UserStateTypes.Available,
				"delayed" => (int)Model.UserStateTypes.Delayed,
				"unavailable" => (int)Model.UserStateTypes.Unavailable,
				"committed" => (int)Model.UserStateTypes.Committed,
				"onshift" => (int)Model.UserStateTypes.OnShift,
				_ => (int?)null
			};
		}
	}
}
