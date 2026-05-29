using System;
using System.Text;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Models;
using Resgrid.Framework;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;

namespace Resgrid.Chatbot.Handlers
{
	public class CallDetailActionHandler : IChatbotActionHandler
	{
		private readonly ICallsService _callsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IAuthorizationService _authorizationService;

		public CallDetailActionHandler(ICallsService callsService, IDepartmentsService departmentsService, IAuthorizationService authorizationService)
		{
			_callsService = callsService;
			_departmentsService = departmentsService;
			_authorizationService = authorizationService;
		}

		public ChatbotIntentType IntentType => ChatbotIntentType.GetCallDetail;

		public async Task<ChatbotResponse> HandleAsync(ChatbotMessage message, ChatbotIntent intent, ChatbotSession session)
		{
			try
			{
				if (!intent.Parameters.TryGetValue("callId", out var callIdStr) || !int.TryParse(callIdStr, out var callId))
				{
					return new ChatbotResponse { Text = "Please specify a call number, e.g., C1445", Processed = false };
				}

				var call = await _callsService.GetCallByIdAsync(callId);

				// Tenant isolation (anti-IDOR): a call that doesn't exist OR belongs to another
				// department must be indistinguishable so call ids can't be enumerated across tenants.
				if (call == null || call.DepartmentId != session.DepartmentId)
				{
					return new ChatbotResponse { Text = $"Call #{callId} not found.", Processed = true };
				}

				// Authorization: the call is in the user's department, but they still need view permission.
				if (!await _authorizationService.CanUserViewCallAsync(session.UserId, call.CallId))
				{
					return new ChatbotResponse { Text = "You don't have permission to view this call.", Processed = false };
				}

				var department = await _departmentsService.GetDepartmentByIdAsync(session.DepartmentId);
				var sb = new StringBuilder();
				sb.AppendLine($"Call #{call.CallId}: {call.Name}");
				sb.AppendLine($"Priority: {call.GetPriorityText()}");
				sb.AppendLine($"Nature: {call.NatureOfCall?.Truncate(100)}");
				if (!string.IsNullOrWhiteSpace(call.Address))
					sb.AppendLine($"Address: {call.Address}");
				sb.AppendLine($"Logged: {call.LoggedOn.TimeConverterToString(department)}");

				return new ChatbotResponse { Text = sb.ToString(), Processed = true };
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return new ChatbotResponse { Text = "Error retrieving call details.", Processed = false };
			}
		}
	}
}
