using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;
using Resgrid.Model.Services;

namespace Resgrid.Chatbot.Handlers
{
	public class PersonnelActionHandler : IChatbotActionHandler
	{
		private readonly IDepartmentsService _departmentsService;
		private readonly IUsersService _usersService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IUserStateService _userStateService;
		private readonly ICustomStateService _customStateService;
		private readonly IAuthorizationService _authorizationService;

		public PersonnelActionHandler(
			IDepartmentsService departmentsService,
			IUsersService usersService,
			IActionLogsService actionLogsService,
			IUserStateService userStateService,
			ICustomStateService customStateService,
			IAuthorizationService authorizationService)
		{
			_departmentsService = departmentsService;
			_usersService = usersService;
			_actionLogsService = actionLogsService;
			_userStateService = userStateService;
			_customStateService = customStateService;
			_authorizationService = authorizationService;
		}

		public ChatbotIntentType IntentType => ChatbotIntentType.PersonnelLookup;

		public async Task<ChatbotResponse> HandleAsync(ChatbotMessage message, ChatbotIntent intent, ChatbotSession session)
		{
			try
			{
				// Authorization: only users permitted to view the roster may list personnel.
				if (!await _authorizationService.CanUserViewAllPeopleAsync(session.UserId, session.DepartmentId))
				{
					return new ChatbotResponse { Text = "You don't have permission to view personnel for your department.", Processed = false };
				}

				var allUsers = await _usersService.GetUserGroupAndRolesByDepartmentIdInLimitAsync(session.DepartmentId, false, false, false);
				var lastActionLogs = await _actionLogsService.GetLastActionLogsForDepartmentAsync(session.DepartmentId);
				var userStates = await _userStateService.GetLatestStatesForDepartmentAsync(session.DepartmentId);

				if (allUsers == null || !allUsers.Any())
				{
					return new ChatbotResponse { Text = "No personnel found for your department.", Processed = true };
				}

				var sb = new StringBuilder();
				sb.AppendLine("Personnel Status:");
				sb.AppendLine("----------------------");

				var userList = allUsers.Take(15).ToList();
				foreach (var user in userList)
				{
					var lastAction = lastActionLogs.FirstOrDefault(x => x.UserId == user.UserId);
					var state = userStates.FirstOrDefault(x => x.UserId == user.UserId);
					var status = await _customStateService.GetCustomPersonnelStatusAsync(session.DepartmentId, lastAction);
					var staffing = await _customStateService.GetCustomPersonnelStaffingAsync(session.DepartmentId, state);

					sb.AppendLine($"{user.LastName}, {user.FirstName}: {status?.ButtonText ?? "Unknown"} / {staffing?.ButtonText ?? "N/A"}");
				}

				return new ChatbotResponse { Text = sb.ToString(), Processed = true };
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return new ChatbotResponse { Text = "Error retrieving personnel.", Processed = false };
			}
		}
	}
}
