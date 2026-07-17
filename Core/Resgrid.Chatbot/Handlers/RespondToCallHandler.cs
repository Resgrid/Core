using System;
using System.Collections.Generic;
using System.Linq;
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
		private static readonly TimeSpan RecentDispatchWindow = TimeSpan.FromDays(1);

		private readonly ICallsService _callsService;
		private readonly IActionLogsService _actionLogsService;
		private readonly ICustomStateService _customStateService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IPersonnelRolesService _personnelRolesService;

		public RespondToCallHandler(ICallsService callsService, IActionLogsService actionLogsService,
			ICustomStateService customStateService = null, IDepartmentGroupsService departmentGroupsService = null,
			IPersonnelRolesService personnelRolesService = null)
		{
			_callsService = callsService;
			_actionLogsService = actionLogsService;
			_customStateService = customStateService;
			_departmentGroupsService = departmentGroupsService;
			_personnelRolesService = personnelRolesService;
		}

		public ChatbotIntentType IntentType => ChatbotIntentType.RespondToCall;

		public async Task<ChatbotResponse> HandleAsync(ChatbotMessage message, ChatbotIntent intent, ChatbotSession session)
		{
			var culture = session.Culture;
			try
			{
				// The reference can be a raw call id ("1445"/"C1445"), a call number ("26-1"), or
				// responder shorthand ("fire") matched against active calls — see CallReferenceResolver.
				intent.Parameters.TryGetValue("callId", out var reference);
				if (string.IsNullOrWhiteSpace(reference))
					intent.Parameters.TryGetValue("callRef", out reference);

				Call call;
				if (string.IsNullOrWhiteSpace(reference))
					call = await ResolveMostRecentDispatchAsync(session.UserId, session.DepartmentId);
				else
				{
					call = await Services.CallReferenceResolver.ResolveAsync(_callsService, session.DepartmentId, reference);
				}
				if (call == null)
				{
					if (string.IsNullOrWhiteSpace(reference))
						return new ChatbotResponse { Text = ChatbotResources.Get("Call_RespondWhich", culture), Processed = false };

					return new ChatbotResponse { Text = ChatbotResources.Get("Call_NoMatch", culture, reference), Processed = true };
				}

				intent.Parameters.TryGetValue("response", out var responseValue);
				var isNegative = IsNegativeResponse(responseValue);
				var status = await ResolveResponseStatusAsync(session.DepartmentId, isNegative);
				var statusId = status?.CustomStateDetailId
					?? (isNegative ? (int)ActionTypes.NotResponding : (int)ActionTypes.Responding);

				await _actionLogsService.SetUserActionAsync(session.UserId, session.DepartmentId, statusId,
					string.Empty, call.CallId, (int)DestinationEntityTypes.Call);

				var responseText = isNegative
					? ChatbotResources.Get("Status_Updated", culture,
						$"{status?.ButtonText ?? "Not Responding"} for Call #{call.CallId} - {call.Name}")
					: ChatbotResources.Get("Call_Responding", culture, call.CallId, call.Name);

				return new ChatbotResponse { Text = responseText, Processed = true };
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return new ChatbotResponse { Text = ChatbotResources.Get("Call_ErrorResponding", culture), Processed = false };
			}
		}

		private async Task<Call> ResolveMostRecentDispatchAsync(string userId, int departmentId)
		{
			var cutoff = DateTime.UtcNow.Subtract(RecentDispatchWindow);
			var candidates = new List<(Call Call, DateTime DispatchedOn)>();
			var groupMembership = new Dictionary<int, bool>();
			HashSet<int> userRoleIds = null;
			var activeCalls = await _callsService.GetActiveCallsByDepartmentAsync(departmentId);

			foreach (var activeCall in (activeCalls ?? new List<Call>())
				.OrderByDescending(x => x.LastDispatchedOn ?? x.DispatchOn ?? x.LoggedOn))
			{
				var call = await _callsService.PopulateCallData(activeCall,
					getDispatches: true,
					getAttachments: false,
					getNotes: false,
					getGroupDispatches: true,
					getUnitDispatches: false,
					getRoleDispatches: true,
					getProtocols: false,
					getReferences: false,
					getContacts: false);

				var dispatch = call?.Dispatches?
					.Where(x => string.Equals(x.UserId, userId, StringComparison.OrdinalIgnoreCase))
					.OrderByDescending(x => x.LastDispatchedOn ?? x.DispatchedOn)
					.FirstOrDefault();
				DateTime? dispatchedOn = dispatch == null
					? null
					: ResolveDispatchTimestamp(dispatch.LastDispatchedOn, dispatch.DispatchedOn, call);

				if (dispatch == null && _departmentGroupsService != null)
				{
					foreach (var groupDispatch in call?.GroupDispatches ?? new List<CallDispatchGroup>())
					{
						if (!groupMembership.TryGetValue(groupDispatch.DepartmentGroupId, out var isMember))
						{
							var members = await _departmentGroupsService.GetAllMembersForGroupAsync(groupDispatch.DepartmentGroupId);
							isMember = members?.Any(x => string.Equals(x.UserId, userId, StringComparison.OrdinalIgnoreCase)) == true;
							groupMembership[groupDispatch.DepartmentGroupId] = isMember;
						}

						if (isMember)
							dispatchedOn = Max(dispatchedOn,
								ResolveDispatchTimestamp(groupDispatch.LastDispatchedOn, groupDispatch.DispatchedOn, call));
					}
				}

				if (dispatch == null && _personnelRolesService != null && call?.RoleDispatches?.Any() == true)
				{
					if (userRoleIds == null)
					{
						var userRoles = await _personnelRolesService.GetRolesForUserAsync(userId, departmentId);
						userRoleIds = new HashSet<int>((userRoles ?? new List<PersonnelRole>()).Select(x => x.PersonnelRoleId));
					}

					foreach (var roleDispatch in call.RoleDispatches.Where(x => userRoleIds.Contains(x.RoleId)))
						dispatchedOn = Max(dispatchedOn,
							ResolveDispatchTimestamp(roleDispatch.LastDispatchedOn, roleDispatch.DispatchedOn, call));
				}

				if (dispatchedOn.HasValue && dispatchedOn.Value >= cutoff)
					candidates.Add((call, dispatchedOn.Value));
			}

			return candidates.OrderByDescending(x => x.DispatchedOn).Select(x => x.Call).FirstOrDefault();
		}

		private static DateTime ResolveDispatchTimestamp(DateTime? lastDispatchedOn, DateTime dispatchedOn, Call call)
		{
			return lastDispatchedOn
				?? (dispatchedOn == default(DateTime) ? (DateTime?)null : dispatchedOn)
				?? call.LastDispatchedOn
				?? call.DispatchOn
				?? call.LoggedOn;
		}

		private static DateTime Max(DateTime? current, DateTime candidate)
		{
			return !current.HasValue || candidate > current.Value ? candidate : current.Value;
		}

		private async Task<CustomStateDetail> ResolveResponseStatusAsync(int departmentId, bool isNegative)
		{
			var statuses = _customStateService == null
				? null
				: await _customStateService.GetCustomPersonnelStatusesOrDefaultsAsync(departmentId);
			var callStatuses = statuses?
				.Where(x => x != null && (x.DetailType == (int)CustomStateDetailTypes.None || x.DetailType.SupportsCalls()))
				.ToList();

			if (isNegative)
			{
				return Services.CustomStateMatcher.FindByName(callStatuses, "Not Responding")
					?? Services.CustomStateMatcher.FindByBaseType(callStatuses, ActionBaseTypes.NotResponding);
			}

			return Services.CustomStateMatcher.FindByName(callStatuses, "Responding to Scene", "Responding", "En Route")
				?? Services.CustomStateMatcher.FindByBaseType(callStatuses, ActionBaseTypes.Responding, ActionBaseTypes.Enroute);
		}

		private static bool IsNegativeResponse(string response)
		{
			var normalized = Services.CustomStateMatcher.Normalize(response);
			return normalized == "no" || normalized == "notresponding" || normalized == "notgoing";
		}
	}
}
