using System;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Localization;
using Resgrid.Chatbot.Models;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;

namespace Resgrid.Chatbot.Handlers
{
	public class MyStatusActionHandler : IChatbotActionHandler
	{
		private readonly IActionLogsService _actionLogsService;
		private readonly IUserStateService _userStateService;
		private readonly ICustomStateService _customStateService;
		private readonly IUserProfileService _userProfileService;
		private readonly IDepartmentsService _departmentsService;

		public MyStatusActionHandler(
			IActionLogsService actionLogsService,
			IUserStateService userStateService,
			ICustomStateService customStateService,
			IUserProfileService userProfileService,
			IDepartmentsService departmentsService)
		{
			_actionLogsService = actionLogsService;
			_userStateService = userStateService;
			_customStateService = customStateService;
			_userProfileService = userProfileService;
			_departmentsService = departmentsService;
		}

		public ChatbotIntentType IntentType => ChatbotIntentType.GetMyStatus;

		public async Task<ChatbotResponse> HandleAsync(ChatbotMessage message, ChatbotIntent intent, ChatbotSession session)
		{
			var culture = session.Culture;
			try
			{
				var profile = await _userProfileService.GetProfileByUserIdAsync(session.UserId);
				var department = await _departmentsService.GetDepartmentByIdAsync(session.DepartmentId);
				var userStatus = await _actionLogsService.GetLastActionLogForUserAsync(session.UserId);
				var userStaffing = await _userStateService.GetLastUserStateByUserIdAsync(session.UserId);

				var customStatus = await _customStateService.GetCustomPersonnelStatusAsync(session.DepartmentId, userStatus);
				var customStaffing = await _customStateService.GetCustomPersonnelStaffingAsync(session.DepartmentId, userStaffing);

				var timeStr = DateTime.UtcNow.TimeConverterToString(department);
				var unknown = ChatbotResources.Get("Personnel_Unknown", culture);
				var response = ChatbotResources.Get("MyStatus_Summary", culture,
					profile?.FullName?.AsFirstNameLastName ?? "User",
					timeStr,
					customStatus?.ButtonText ?? unknown,
					customStaffing?.ButtonText ?? unknown);

				return new ChatbotResponse { Text = response, Processed = true };
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return new ChatbotResponse { Text = ChatbotResources.Get("MyStatus_Error", culture), Processed = false };
			}
		}
	}
}
