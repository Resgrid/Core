using System;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Localization;
using Resgrid.Chatbot.Models;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Chatbot.Handlers
{
	/// <summary>
	/// Marks the user as Responding to a specific call (intent <see cref="ChatbotIntentType.RespondToCall"/>).
	/// Self-write with the call as destination; the call must belong to the active department (anti-IDOR §3).
	/// Not destructive. Responses are localized to the user's culture.
	/// </summary>
	public class RespondToCallHandler : IChatbotActionHandler
	{
		private readonly ICallsService _callsService;
		private readonly IActionLogsService _actionLogsService;

		public RespondToCallHandler(ICallsService callsService, IActionLogsService actionLogsService)
		{
			_callsService = callsService;
			_actionLogsService = actionLogsService;
		}

		public ChatbotIntentType IntentType => ChatbotIntentType.RespondToCall;

		public async Task<ChatbotResponse> HandleAsync(ChatbotMessage message, ChatbotIntent intent, ChatbotSession session)
		{
			var culture = session.Culture;
			try
			{
				if (!intent.Parameters.TryGetValue("callId", out var callIdStr) || !int.TryParse(callIdStr, out var callId))
					return new ChatbotResponse { Text = ChatbotResources.Get("Call_RespondWhich", culture), Processed = false };

				var call = await _callsService.GetCallByIdAsync(callId);
				if (call == null || call.DepartmentId != session.DepartmentId)
					return new ChatbotResponse { Text = ChatbotResources.Get("Call_NotFound", culture, callId), Processed = true };

				await _actionLogsService.SetUserActionAsync(session.UserId, session.DepartmentId, (int)ActionTypes.Responding, string.Empty, call.CallId);

				return new ChatbotResponse { Text = ChatbotResources.Get("Call_Responding", culture, call.CallId, call.Name), Processed = true };
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return new ChatbotResponse { Text = ChatbotResources.Get("Call_ErrorResponding", culture), Processed = false };
			}
		}
	}
}
