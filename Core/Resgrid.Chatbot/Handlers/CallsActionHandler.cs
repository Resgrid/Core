using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Localization;
using Resgrid.Chatbot.Models;
using Resgrid.Framework;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;

namespace Resgrid.Chatbot.Handlers
{
	public class CallsActionHandler : IChatbotActionHandler
	{
		private readonly ICallsService _callsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly ICustomStateService _customStateService;
		private readonly IUserProfileService _userProfileService;
		private readonly IAuthorizationService _authorizationService;

		public CallsActionHandler(
			ICallsService callsService,
			IDepartmentsService departmentsService,
			ICustomStateService customStateService,
			IUserProfileService userProfileService,
			IAuthorizationService authorizationService)
		{
			_callsService = callsService;
			_departmentsService = departmentsService;
			_customStateService = customStateService;
			_userProfileService = userProfileService;
			_authorizationService = authorizationService;
		}

		public ChatbotIntentType IntentType => ChatbotIntentType.ListCalls;

		public async Task<ChatbotResponse> HandleAsync(ChatbotMessage message, ChatbotIntent intent, ChatbotSession session)
		{
			var culture = session.Culture;
			try
			{
				// Layer 2 for a list: every active member may list their department's calls, so the
				// permission is decided once per request against session.DepartmentId. A per-row
				// CanUserViewCallAsync answers the same membership question with two lookups per call.
				if (!await _authorizationService.IsUserValidWithinLimitsAsync(session.UserId, session.DepartmentId))
					return new ChatbotResponse { Text = ChatbotResources.Get("Calls_NoPermission", culture), Processed = false };

				var department = await _departmentsService.GetDepartmentByIdAsync(session.DepartmentId);
				var departmentName = department?.Name ?? ChatbotResources.Get("Common_YourDepartment", culture);
				var activeCalls = await _callsService.GetActiveCallsByDepartmentAsync(session.DepartmentId);

				// The department boundary is the per-row rule (the unread-messages list applies the same one).
				var callList = (activeCalls ?? Enumerable.Empty<Resgrid.Model.Call>())
					.Where(call => call != null && call.DepartmentId == session.DepartmentId)
					.Take(10)
					.ToList();

				if (callList.Count == 0)
					return new ChatbotResponse { Text = ChatbotResources.Get("Calls_NoActive", culture, departmentName), Processed = true };

				var sb = new StringBuilder();
				sb.AppendLine(ChatbotResources.Get("Calls_Header", culture, departmentName));
				sb.AppendLine("----------------------");

				foreach (var call in callList)
				{
					sb.AppendLine(ChatbotResources.Get("Calls_Line", culture, call.CallId, call.Name?.Truncate(25), call.NatureOfCall?.Truncate(40)));
				}

				return new ChatbotResponse { Text = sb.ToString(), Processed = true };
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return new ChatbotResponse { Text = ChatbotResources.Get("Calls_Error", culture), Processed = false };
			}
		}
	}
}
