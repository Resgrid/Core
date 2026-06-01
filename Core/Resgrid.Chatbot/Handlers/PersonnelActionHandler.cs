using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Localization;
using Resgrid.Chatbot.Models;
using Resgrid.Model.Services;

namespace Resgrid.Chatbot.Handlers
{
	/// <summary>
	/// Lists/searches department personnel and their status (intent <see cref="ChatbotIntentType.PersonnelLookup"/>).
	/// Requires <see cref="IAuthorizationService.CanUserViewAllPeopleAsync"/> (§2). Responses are localized to
	/// the user's culture; personnel names and custom-state button text are data and rendered as-is.
	/// </summary>
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
			var culture = session.Culture;
			try
			{
				if (!await _authorizationService.CanUserViewAllPeopleAsync(session.UserId, session.DepartmentId))
					return new ChatbotResponse { Text = ChatbotResources.Get("Personnel_NoPermission", culture), Processed = false };

				var allUsers = await _usersService.GetUserGroupAndRolesByDepartmentIdInLimitAsync(session.DepartmentId, false, false, false);
				var lastActionLogs = await _actionLogsService.GetLastActionLogsForDepartmentAsync(session.DepartmentId);
				var userStates = await _userStateService.GetLatestStatesForDepartmentAsync(session.DepartmentId);

				if (allUsers == null || !allUsers.Any())
					return new ChatbotResponse { Text = ChatbotResources.Get("Personnel_None", culture), Processed = true };

				// Optional search filter: "who is John", "where is captain smith" supply a query.
				intent.Parameters.TryGetValue("query", out var query);
				var hasQuery = !string.IsNullOrWhiteSpace(query);

				var filteredUsers = allUsers.AsEnumerable();
				if (hasQuery)
				{
					var q = query.Trim().ToLowerInvariant();
					filteredUsers = allUsers.Where(u =>
						(u.FirstName?.ToLowerInvariant().Contains(q) == true) ||
						(u.LastName?.ToLowerInvariant().Contains(q) == true) ||
						(u.Name?.ToLowerInvariant().Contains(q) == true));
				}

				var userList = filteredUsers
					.OrderBy(u => u.LastName)
					.ThenBy(u => u.FirstName)
					.Take(15)
					.ToList();

				if (userList.Count == 0)
					return new ChatbotResponse { Text = ChatbotResources.Get("Personnel_NoMatch", culture, query), Processed = true };

				var sb = new StringBuilder();
				sb.AppendLine(hasQuery
					? ChatbotResources.Get("Personnel_HeaderQuery", culture, query)
					: ChatbotResources.Get("Personnel_Header", culture));
				sb.AppendLine("----------------------");

				foreach (var user in userList)
				{
					var lastAction = lastActionLogs.FirstOrDefault(x => x.UserId == user.UserId);
					var state = userStates.FirstOrDefault(x => x.UserId == user.UserId);
					var status = await _customStateService.GetCustomPersonnelStatusAsync(session.DepartmentId, lastAction);
					var staffing = await _customStateService.GetCustomPersonnelStaffingAsync(session.DepartmentId, state);

					var statusText = status?.ButtonText ?? ChatbotResources.Get("Personnel_Unknown", culture);
					var staffingText = staffing?.ButtonText ?? ChatbotResources.Get("Personnel_NA", culture);

					sb.AppendLine(ChatbotResources.Get("Personnel_Line", culture, user.LastName, user.FirstName, statusText, staffingText));
				}

				return new ChatbotResponse { Text = sb.ToString(), Processed = true };
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return new ChatbotResponse { Text = ChatbotResources.Get("Personnel_Error", culture), Processed = false };
			}
		}
	}
}
