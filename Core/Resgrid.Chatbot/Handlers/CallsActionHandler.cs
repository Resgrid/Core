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

		public CallsActionHandler(
			ICallsService callsService,
			IDepartmentsService departmentsService,
			ICustomStateService customStateService,
			IUserProfileService userProfileService)
		{
			_callsService = callsService;
			_departmentsService = departmentsService;
			_customStateService = customStateService;
			_userProfileService = userProfileService;
		}

		public ChatbotIntentType IntentType => ChatbotIntentType.ListCalls;

		public async Task<ChatbotResponse> HandleAsync(ChatbotMessage message, ChatbotIntent intent, ChatbotSession session)
		{
			var culture = session.Culture;
			try
			{
				var department = await _departmentsService.GetDepartmentByIdAsync(session.DepartmentId);
				var departmentName = department?.Name ?? ChatbotResources.Get("Common_YourDepartment", culture);
				var activeCalls = await _callsService.GetActiveCallsByDepartmentAsync(session.DepartmentId);

				if (activeCalls == null || !activeCalls.Any())
					return new ChatbotResponse { Text = ChatbotResources.Get("Calls_NoActive", culture, departmentName), Processed = true };

				var callList = activeCalls.Take(10).ToList();
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
