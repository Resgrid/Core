using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Localization;
using Resgrid.Chatbot.Models;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Chatbot.Handlers
{
	/// <summary>
	/// Answers "who got dispatched to the medical?" (intent <see cref="ChatbotIntentType.CallDispatched"/>):
	/// the call's recorded dispatch lists — personnel, groups, roles and units — regardless of what those
	/// resources' current statuses are (unlike <see cref="CallRespondersActionHandler"/>).
	/// </summary>
	public class CallDispatchedActionHandler : IChatbotActionHandler
	{
		private const int MaxLinesPerSection = 15;

		private readonly ICallsService _callsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IAuthorizationService _authorizationService;

		public CallDispatchedActionHandler(
			ICallsService callsService,
			IUserProfileService userProfileService,
			IAuthorizationService authorizationService)
		{
			_callsService = callsService;
			_userProfileService = userProfileService;
			_authorizationService = authorizationService;
		}

		public ChatbotIntentType IntentType => ChatbotIntentType.CallDispatched;

		public async Task<ChatbotResponse> HandleAsync(ChatbotMessage message, ChatbotIntent intent, ChatbotSession session)
		{
			var culture = session.Culture;
			try
			{
				intent.Parameters.TryGetValue("callRef", out var reference);
				if (string.IsNullOrWhiteSpace(reference))
					intent.Parameters.TryGetValue("callId", out reference);

				Call call;
				if (!string.IsNullOrWhiteSpace(reference))
				{
					call = await Services.CallReferenceResolver.ResolveAsync(_callsService, session.DepartmentId, reference);
					if (call == null)
						return new ChatbotResponse { Text = ChatbotResources.Get("Call_NoMatch", culture, reference), Processed = true };
				}
				else
				{
					var activeCalls = await _callsService.GetActiveCallsByDepartmentAsync(session.DepartmentId);
					if (activeCalls?.Count == 1)
						call = activeCalls[0];
					else
						return new ChatbotResponse { Text = ChatbotResources.Get("CallResp_Specify", culture), Processed = false };
				}

				if (!await _authorizationService.CanUserViewCallAsync(session.UserId, call.CallId))
					return new ChatbotResponse { Text = ChatbotResources.Get("CallDetail_NoPermission", culture), Processed = false };

				call = await _callsService.PopulateCallData(call, true, false, false, true, true, true, false, false, false);

				var callLabel = string.IsNullOrWhiteSpace(call.Number) ? call.Name : $"{call.Number} {call.Name}";
				var sb = new StringBuilder();
				sb.AppendLine(ChatbotResources.Get("CallDisp_Header", culture, callLabel));
				sb.AppendLine("----------------------");

				var any = false;

				if (call.Dispatches != null && call.Dispatches.Any())
				{
					any = true;
					sb.AppendLine(ChatbotResources.Get("Section_Personnel", culture));

					var userIds = call.Dispatches.Select(d => d.UserId).Distinct().ToList();
					var profiles = await _userProfileService.GetSelectedUserProfilesAsync(userIds);
					foreach (var userId in userIds.Take(MaxLinesPerSection))
					{
						var profile = profiles?.FirstOrDefault(p => p.UserId == userId);
						sb.AppendLine(profile?.FullName?.AsFirstNameLastName ?? ChatbotResources.Get("Personnel_Unknown", culture));
					}

					if (userIds.Count > MaxLinesPerSection)
						sb.AppendLine(ChatbotResources.Get("Msg_AndMore", culture, userIds.Count - MaxLinesPerSection));
				}

				if (call.GroupDispatches != null && call.GroupDispatches.Any())
				{
					any = true;
					sb.AppendLine(ChatbotResources.Get("Section_Groups", culture));
					foreach (var groupDispatch in call.GroupDispatches.Take(MaxLinesPerSection))
						sb.AppendLine(groupDispatch.Group?.Name ?? $"Group {groupDispatch.DepartmentGroupId}");
				}

				if (call.RoleDispatches != null && call.RoleDispatches.Any())
				{
					any = true;
					sb.AppendLine(ChatbotResources.Get("Section_Roles", culture));
					foreach (var roleDispatch in call.RoleDispatches.Take(MaxLinesPerSection))
						sb.AppendLine(roleDispatch.Role?.Name ?? $"Role {roleDispatch.RoleId}");
				}

				if (call.UnitDispatches != null && call.UnitDispatches.Any())
				{
					any = true;
					sb.AppendLine(ChatbotResources.Get("Section_Units", culture));
					foreach (var unitDispatch in call.UnitDispatches.Take(MaxLinesPerSection))
						sb.AppendLine(unitDispatch.Unit?.Name ?? $"Unit {unitDispatch.UnitId}");
				}

				if (!any)
					return new ChatbotResponse { Text = ChatbotResources.Get("CallDisp_None", culture, callLabel), Processed = true };

				return new ChatbotResponse { Text = sb.ToString(), Processed = true };
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return new ChatbotResponse { Text = ChatbotResources.Get("CallDisp_Error", culture), Processed = false };
			}
		}
	}
}
