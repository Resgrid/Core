using System;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Localization;
using Resgrid.Chatbot.Models;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;

namespace Resgrid.Chatbot.Handlers
{
	/// <summary>
	/// Creates a new active call from a free-text description (intent <see cref="ChatbotIntentType.DispatchCall"/>).
	/// Destructive → requires confirmation (security addendum §5); <see cref="IAuthorizationService.CanUserCreateCallAsync"/>
	/// is re-checked on both passes (§2). The call uses the department's default priority. Responses are
	/// localized to the user's culture.
	/// </summary>
	public class DispatchCallHandler : IChatbotActionHandler
	{
		private readonly ICallsService _callsService;
		private readonly IAuthorizationService _authorizationService;
		private readonly IChatbotDepartmentConfigService _departmentConfigService;

		public DispatchCallHandler(ICallsService callsService, IAuthorizationService authorizationService,
			IChatbotDepartmentConfigService departmentConfigService = null)
		{
			_callsService = callsService;
			_authorizationService = authorizationService;
			_departmentConfigService = departmentConfigService;
		}

		public ChatbotIntentType IntentType => ChatbotIntentType.DispatchCall;

		public async Task<ChatbotResponse> HandleAsync(ChatbotMessage message, ChatbotIntent intent, ChatbotSession session)
		{
			var culture = session.Culture;
			try
			{
				var config = _departmentConfigService == null
					? null
					: await _departmentConfigService.GetConfigAsync(session.DepartmentId);
				if (config != null && !config.AllowDispatchViaChatbot)
					return new ChatbotResponse
					{
						Text = "Creating calls through the chatbot is disabled for this department.",
						Processed = false
					};

				if (!await _authorizationService.CanUserCreateCallAsync(session.UserId, session.DepartmentId))
					return new ChatbotResponse { Text = ChatbotResources.Get("Call_NoCreatePermission", culture), Processed = false };

				intent.Parameters.TryGetValue("description", out var description);
				if (string.IsNullOrWhiteSpace(description))
					return new ChatbotResponse { Text = ChatbotResources.Get("Call_DescribePrompt", culture), Processed = false };

				description = description.Trim();

				var confirmed = intent.Parameters.TryGetValue("__confirmed", out var confirmFlag) && confirmFlag == "true";
				if (!confirmed)
				{
					session.State = ChatbotDialogState.AwaitingConfirmation;
					session.PendingIntent = ChatbotIntentType.DispatchCall;
					session.Context["description"] = description;
					return new ChatbotResponse
					{
						Text = ChatbotResources.Get("Call_DispatchConfirm", culture, description.Truncate(120)),
						Processed = true
					};
				}

				var priorities = await _callsService.GetActiveCallPrioritiesForDepartmentAsync(session.DepartmentId);
				var priority = priorities?.FirstOrDefault(p => p.IsDefault) ?? priorities?.FirstOrDefault();
				if (priority == null)
					Framework.Logging.LogError($"DispatchCallHandler: No active call priorities for department {session.DepartmentId}; falling back to {nameof(CallPriority.Low)}.");

				var call = new Call
				{
					Name = description.Truncate(100),
					NatureOfCall = description.Truncate(4000),
					LoggedOn = DateTime.UtcNow,
					ReportingUserId = session.UserId,
					DepartmentId = session.DepartmentId,
					Priority = priority?.DepartmentCallPriorityId ?? (int)CallPriority.Low,
					State = (int)CallStates.Active,
					CallSource = (int)CallSources.Chatbot
				};

				var saved = await _callsService.SaveCallAsync(call);

				return new ChatbotResponse { Text = ChatbotResources.Get("Call_Created", culture, saved.CallId, saved.Name), Processed = true };
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return new ChatbotResponse { Text = ChatbotResources.Get("Call_ErrorCreating", culture), Processed = false };
			}
		}
	}
}
