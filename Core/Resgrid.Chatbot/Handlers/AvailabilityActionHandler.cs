using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Localization;
using Resgrid.Chatbot.Models;
using Resgrid.Model;
using Resgrid.Model.Reporting;
using Resgrid.Model.Services;

namespace Resgrid.Chatbot.Handlers
{
	/// <summary>
	/// Answers "who's available/around?" (intent <see cref="ChatbotIntentType.WhoIsAvailable"/>):
	/// personnel whose staffing says they are around (not unavailable) and whose status classifies as
	/// Available, so they can actually be assigned. Availability classification is the canonical
	/// <see cref="AvailabilityMatrix"/> resolution (custom statuses resolve via BaseType).
	/// </summary>
	public class AvailabilityActionHandler : IChatbotActionHandler
	{
		private const int MaxPersonnelToList = 15;

		private readonly IUsersService _usersService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IUserStateService _userStateService;
		private readonly ICustomStateService _customStateService;
		private readonly IPlatformReportingService _platformReportingService;
		private readonly IAuthorizationService _authorizationService;

		public AvailabilityActionHandler(
			IUsersService usersService,
			IActionLogsService actionLogsService,
			IUserStateService userStateService,
			ICustomStateService customStateService,
			IPlatformReportingService platformReportingService,
			IAuthorizationService authorizationService)
		{
			_usersService = usersService;
			_actionLogsService = actionLogsService;
			_userStateService = userStateService;
			_customStateService = customStateService;
			_platformReportingService = platformReportingService;
			_authorizationService = authorizationService;
		}

		public ChatbotIntentType IntentType => ChatbotIntentType.WhoIsAvailable;

		public async Task<ChatbotResponse> HandleAsync(ChatbotMessage message, ChatbotIntent intent, ChatbotSession session)
		{
			var culture = session.Culture;
			try
			{
				if (!await _authorizationService.CanUserViewAllPeopleAsync(session.UserId, session.DepartmentId))
					return new ChatbotResponse { Text = ChatbotResources.Get("Personnel_NoPermission", culture), Processed = false };

				var allUsers = await _usersService.GetUserGroupAndRolesByDepartmentIdInLimitAsync(session.DepartmentId, false, false, false);
				if (allUsers == null || !allUsers.Any())
					return new ChatbotResponse { Text = ChatbotResources.Get("Personnel_None", culture), Processed = true };

				var lastActionLogs = await _actionLogsService.GetLastActionLogsForDepartmentAsync(session.DepartmentId);
				var userStates = await _userStateService.GetLatestStatesForDepartmentAsync(session.DepartmentId);

				var availableLines = new StringBuilder();
				var availableCount = 0;

				foreach (var user in allUsers.OrderBy(u => u.LastName).ThenBy(u => u.FirstName))
				{
					var lastAction = lastActionLogs?.FirstOrDefault(x => x.UserId == user.UserId);
					var state = userStates?.FirstOrDefault(x => x.UserId == user.UserId);

					// Status must classify as Available (able to respond) and staffing must say they are
					// around (anything but an unavailable staffing level).
					var statusClass = lastAction != null
						? await _platformReportingService.ClassifyPersonnelAvailabilityAsync(session.DepartmentId, lastAction.ActionTypeId)
						: AvailabilityClass.Unknown;
					var staffingClass = await ClassifyStaffingAsync(session.DepartmentId, state);

					if (statusClass != AvailabilityClass.Available || staffingClass == AvailabilityClass.Unavailable)
						continue;

					availableCount++;
					if (availableCount > MaxPersonnelToList)
						continue;

					var status = await _customStateService.GetCustomPersonnelStatusAsync(session.DepartmentId, lastAction);
					var staffing = await _customStateService.GetCustomPersonnelStaffingAsync(session.DepartmentId, state);
					var unknown = ChatbotResources.Get("Personnel_Unknown", culture);

					availableLines.AppendLine(ChatbotResources.Get("Avail_Line", culture,
						user.LastName, user.FirstName,
						status?.ButtonText ?? unknown,
						staffing?.ButtonText ?? ChatbotResources.Get("Personnel_NA", culture)));
				}

				if (availableCount == 0)
					return new ChatbotResponse { Text = ChatbotResources.Get("Avail_None", culture), Processed = true };

				var sb = new StringBuilder();
				sb.AppendLine(ChatbotResources.Get("Avail_Header", culture, availableCount));
				sb.AppendLine("----------------------");
				sb.Append(availableLines);

				if (availableCount > MaxPersonnelToList)
					sb.AppendLine(ChatbotResources.Get("Msg_AndMore", culture, availableCount - MaxPersonnelToList));

				return new ChatbotResponse { Text = sb.ToString(), Processed = true };
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return new ChatbotResponse { Text = ChatbotResources.Get("Avail_Error", culture), Processed = false };
			}
		}

		// Staffing availability: UserState.State holds a built-in UserStateTypes value (<= 25 by the same
		// convention CustomStateService uses) or a CustomStateDetailId whose BaseType classifies it.
		private async Task<AvailabilityClass> ClassifyStaffingAsync(int departmentId, UserState state)
		{
			if (state == null)
				return AvailabilityClass.Unknown;

			if (state.State <= 25)
			{
				return (UserStateTypes)state.State switch
				{
					UserStateTypes.Available => AvailabilityClass.Available,
					UserStateTypes.Delayed => AvailabilityClass.Delayed,
					UserStateTypes.Unavailable => AvailabilityClass.Unavailable,
					UserStateTypes.Committed => AvailabilityClass.Committed,
					UserStateTypes.OnShift => AvailabilityClass.Available,
					_ => AvailabilityClass.Unknown
				};
			}

			var detail = await _customStateService.GetCustomDetailForDepartmentAsync(departmentId, state.State);
			if (detail == null)
				return AvailabilityClass.Unknown;

			var baseClassification = AvailabilityMatrix.ForCustomBaseType(detail.BaseType);
			if (baseClassification != AvailabilityClass.Unknown)
				return baseClassification;

			// Staffing state sets predate BaseType and many still use None. Preserve their operational
			// meaning through the standard/custom template labels instead of treating Off Duty as available.
			return Services.CustomStateMatcher.Normalize(detail.ButtonText) switch
			{
				"available" or "onduty" or "oncall" or "onshift" => AvailabilityClass.Available,
				"delayed" or "onbreak" => AvailabilityClass.Delayed,
				"unavailable" or "offduty" or "notavailable" or "away" => AvailabilityClass.Unavailable,
				"committed" or "ontask" => AvailabilityClass.Committed,
				_ => AvailabilityClass.Unknown
			};
		}
	}
}
