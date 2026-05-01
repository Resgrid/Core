using System;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
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
				else
				{
					return new ChatbotResponse { Text = "Could not determine staffing level to set. Text HELP for available commands.", Processed = false };
				}

				await _userStateService.CreateUserState(session.UserId, session.DepartmentId, staffingId);

				var userStaffing = await _userStateService.GetLastUserStateByUserIdAsync(session.UserId);
				var staffingName = await _customStateService.GetCustomPersonnelStaffingAsync(session.DepartmentId, userStaffing);

				return new ChatbotResponse
				{
					Text = $"Staffing level updated to: {staffingName?.ButtonText ?? staffingId.ToString()}",
					Processed = true
				};
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return new ChatbotResponse { Text = "Error updating staffing. Please try again.", Processed = false };
			}
		}
	}
}
