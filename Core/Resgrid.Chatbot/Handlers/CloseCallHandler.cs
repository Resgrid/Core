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
	/// Closes a call (intent <see cref="ChatbotIntentType.CloseCall"/>). Destructive → requires confirmation
	/// (security addendum §5): the first pass restates the target and parks the session in
	/// <see cref="ChatbotDialogState.AwaitingConfirmation"/>; the ingress re-dispatches with "__confirmed"
	/// on YES. Ownership, the Call_Update right MVC and v4 require (<see cref="IAuthorizationService.CanUserCreateCallAsync"/>
	/// evaluates the same Create Call row ClaimsLogic.AddCallClaims derives it from) and
	/// <see cref="IAuthorizationService.CanUserCloseCallAsync"/> are re-checked on both passes (§2/§3). A call run under
	/// an active incident command is refused on both passes too (<see cref="ICallClosureService"/>). Responses are
	/// localized to the user's culture.
	/// </summary>
	public class CloseCallHandler : IChatbotActionHandler
	{
		private readonly ICallsService _callsService;
		private readonly IAuthorizationService _authorizationService;
		private readonly ICallClosureService _callClosureService;

		public CloseCallHandler(ICallsService callsService, IAuthorizationService authorizationService, ICallClosureService callClosureService)
		{
			_callsService = callsService;
			_authorizationService = authorizationService;
			_callClosureService = callClosureService;
		}

		public ChatbotIntentType IntentType => ChatbotIntentType.CloseCall;

		public async Task<ChatbotResponse> HandleAsync(ChatbotMessage message, ChatbotIntent intent, ChatbotSession session)
		{
			var culture = session.Culture;
			try
			{
				if (!intent.Parameters.TryGetValue("callId", out var callIdStr) || !int.TryParse(callIdStr, out var callId))
					return new ChatbotResponse { Text = ChatbotResources.Get("Call_CloseWhich", culture), Processed = false };

				var call = await _callsService.GetCallByIdAsync(callId);
				if (call == null || call.DepartmentId != session.DepartmentId)
					return new ChatbotResponse { Text = ChatbotResources.Get("Call_NotFound", culture, callId), Processed = true };

				if (!await _authorizationService.CanUserCreateCallAsync(session.UserId, session.DepartmentId) ||
					!await _authorizationService.CanUserCloseCallAsync(session.UserId, call.CallId, session.DepartmentId))
					return new ChatbotResponse { Text = ChatbotResources.Get("Call_NoClosePermission", culture), Processed = false };

				if (call.State == (int)CallStates.Closed)
					return new ChatbotResponse { Text = ChatbotResources.Get("Call_AlreadyClosed", culture, call.CallId), Processed = true };

				if (await _callClosureService.GetBlockingIncidentCommandAsync(session.DepartmentId, call.CallId) != null)
					return new ChatbotResponse { Text = ChatbotResources.Get("Call_CloseBlockedByIncidentCommand", culture, call.CallId), Processed = false };

				var confirmed = intent.Parameters.TryGetValue("__confirmed", out var confirmFlag) && confirmFlag == "true";
				if (!confirmed)
				{
					session.State = ChatbotDialogState.AwaitingConfirmation;
					session.PendingIntent = ChatbotIntentType.CloseCall;
					session.Context["callId"] = call.CallId.ToString();
					return new ChatbotResponse
					{
						Text = ChatbotResources.Get("Call_CloseConfirm", culture, call.CallId, call.Name),
						Processed = true
					};
				}

				call.State = (int)CallStates.Closed;
				call.ClosedOn = DateTime.UtcNow;
				call.ClosedByUserId = session.UserId;
				await _callsService.SaveCallAsync(call);

				return new ChatbotResponse { Text = ChatbotResources.Get("Call_Closed", culture, call.CallId, call.Name), Processed = true };
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return new ChatbotResponse { Text = ChatbotResources.Get("Call_ErrorClosing", culture), Processed = false };
			}
		}
	}
}
