using System;
using System.Collections.Generic;
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
	/// Answers "who's on call X?", "who's in route to the fire?" and "who's on scene?" (intent
	/// <see cref="ChatbotIntentType.CallResponders"/>): personnel and units whose CURRENT status is tied
	/// to the call (ActionLog/UnitState DestinationId) and whose base state means en-route or on-scene.
	/// The "mode" parameter narrows the buckets: "enroute", "onscene" or "all" (both).
	/// </summary>
	public class CallRespondersActionHandler : IChatbotActionHandler
	{
		private enum ResponderBucket
		{
			None,
			EnRoute,
			OnScene
		}

		private readonly ICallsService _callsService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IUnitsService _unitsService;
		private readonly ICustomStateService _customStateService;
		private readonly IUserProfileService _userProfileService;
		private readonly IAuthorizationService _authorizationService;

		public CallRespondersActionHandler(
			ICallsService callsService,
			IActionLogsService actionLogsService,
			IUnitsService unitsService,
			ICustomStateService customStateService,
			IUserProfileService userProfileService,
			IAuthorizationService authorizationService)
		{
			_callsService = callsService;
			_actionLogsService = actionLogsService;
			_unitsService = unitsService;
			_customStateService = customStateService;
			_userProfileService = userProfileService;
			_authorizationService = authorizationService;
		}

		public ChatbotIntentType IntentType => ChatbotIntentType.CallResponders;

		public async Task<ChatbotResponse> HandleAsync(ChatbotMessage message, ChatbotIntent intent, ChatbotSession session)
		{
			var culture = session.Culture;
			try
			{
				intent.Parameters.TryGetValue("mode", out var mode);
				mode = string.IsNullOrWhiteSpace(mode) ? "all" : mode.Trim().ToLowerInvariant();

				var call = await ResolveCallAsync(intent, session.DepartmentId);
				if (call == null)
					return new ChatbotResponse { Text = ChatbotResources.Get("CallResp_Specify", culture), Processed = false };

				if (!await _authorizationService.CanUserViewCallAsync(session.UserId, call.CallId))
					return new ChatbotResponse { Text = ChatbotResources.Get("CallDetail_NoPermission", culture), Processed = false };

				var callLabel = string.IsNullOrWhiteSpace(call.Number) ? call.Name : $"{call.Number} {call.Name}";

				// Latest status per person tied to this call.
				var actionLogs = await _actionLogsService.GetActionLogsForCallAsync(session.DepartmentId, call.CallId);
				var latestPerUser = (actionLogs ?? new List<ActionLog>())
					.GroupBy(x => x.UserId)
					.Select(g => g.OrderByDescending(x => x.Timestamp).First())
					.ToList();

				var personnelLines = new StringBuilder();
				var personnelCount = 0;
				if (latestPerUser.Count > 0)
				{
					var profiles = await _userProfileService.GetSelectedUserProfilesAsync(latestPerUser.Select(x => x.UserId).Distinct().ToList());
					foreach (var log in latestPerUser)
					{
						var bucket = await ClassifyPersonnelAsync(session.DepartmentId, log);
						if (!BucketMatches(bucket, mode))
							continue;

						var profile = profiles?.FirstOrDefault(p => p.UserId == log.UserId);
						var name = profile?.FullName?.AsFirstNameLastName ?? ChatbotResources.Get("Personnel_Unknown", culture);
						var status = await _customStateService.GetCustomPersonnelStatusAsync(session.DepartmentId, log);
						personnelLines.AppendLine(ChatbotResources.Get("CallResp_Line", culture, name, status?.ButtonText ?? ChatbotResources.Get("Personnel_Unknown", culture)));
						personnelCount++;
					}
				}

				// Latest status per unit tied to this call.
				var unitStates = await _unitsService.GetUnitStatesForCallAsync(session.DepartmentId, call.CallId);
				var latestPerUnit = (unitStates ?? new List<UnitState>())
					.GroupBy(x => x.UnitId)
					.Select(g => g.OrderByDescending(x => x.Timestamp).First())
					.ToList();

				var unitLines = new StringBuilder();
				var unitCount = 0;
				if (latestPerUnit.Count > 0)
				{
					// Unit navigation may not be populated on the call-scoped state query; a department
					// unit map backfills names (and the DepartmentId that custom-state resolution needs).
					var departmentUnits = await _unitsService.GetUnitsForDepartmentAsync(session.DepartmentId);
					foreach (var unitState in latestPerUnit)
					{
						var bucket = await ClassifyUnitAsync(session.DepartmentId, unitState.State);
						if (!BucketMatches(bucket, mode))
							continue;

						if (unitState.Unit == null)
							unitState.Unit = departmentUnits?.FirstOrDefault(u => u.UnitId == unitState.UnitId);

						var status = await _customStateService.GetCustomUnitStateAsync(unitState);
						var name = unitState.Unit?.Name ?? ChatbotResources.Get("Personnel_Unknown", culture);
						unitLines.AppendLine(ChatbotResources.Get("CallResp_Line", culture, name, status?.ButtonText ?? ChatbotResources.Get("Personnel_Unknown", culture)));
						unitCount++;
					}
				}

				if (personnelCount == 0 && unitCount == 0)
					return new ChatbotResponse { Text = ChatbotResources.Get("CallResp_None", culture, callLabel), Processed = true };

				var headerKey = mode switch
				{
					"enroute" => "CallResp_HeaderEnroute",
					"onscene" => "CallResp_HeaderOnScene",
					_ => "CallResp_HeaderAll"
				};

				var sb = new StringBuilder();
				sb.AppendLine(ChatbotResources.Get(headerKey, culture, callLabel));
				sb.AppendLine("----------------------");

				if (personnelCount > 0)
				{
					sb.AppendLine(ChatbotResources.Get("Section_Personnel", culture));
					sb.Append(personnelLines);
				}

				if (unitCount > 0)
				{
					sb.AppendLine(ChatbotResources.Get("Section_Units", culture));
					sb.Append(unitLines);
				}

				return new ChatbotResponse { Text = sb.ToString(), Processed = true };
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return new ChatbotResponse { Text = ChatbotResources.Get("CallResp_Error", culture), Processed = false };
			}
		}

		private async Task<Call> ResolveCallAsync(ChatbotIntent intent, int departmentId)
		{
			intent.Parameters.TryGetValue("callRef", out var reference);
			if (string.IsNullOrWhiteSpace(reference))
				intent.Parameters.TryGetValue("callId", out reference);

			// "who's on call?" leaves a bare filler word behind; treat it as no reference.
			var cleaned = reference?.Trim();
			if (string.Equals(cleaned, "call", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(cleaned, "the call", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(cleaned, "scene", StringComparison.OrdinalIgnoreCase))
				cleaned = null;

			if (!string.IsNullOrWhiteSpace(cleaned))
				return await Services.CallReferenceResolver.ResolveAsync(_callsService, departmentId, cleaned);

			// No reference: when exactly one call is active it is unambiguous.
			var activeCalls = await _callsService.GetActiveCallsByDepartmentAsync(departmentId);
			return activeCalls?.Count == 1 ? activeCalls[0] : null;
		}

		private static bool BucketMatches(ResponderBucket bucket, string mode)
		{
			return mode switch
			{
				"enroute" => bucket == ResponderBucket.EnRoute,
				"onscene" => bucket == ResponderBucket.OnScene,
				_ => bucket != ResponderBucket.None
			};
		}

		private async Task<ResponderBucket> ClassifyPersonnelAsync(int departmentId, ActionLog log)
		{
			if (log == null)
				return ResponderBucket.None;

			if (log.ActionTypeId <= 25)
			{
				return (ActionTypes)log.ActionTypeId switch
				{
					ActionTypes.Responding => ResponderBucket.EnRoute,
					ActionTypes.RespondingToScene => ResponderBucket.EnRoute,
					ActionTypes.RespondingToStation => ResponderBucket.EnRoute,
					ActionTypes.OnScene => ResponderBucket.OnScene,
					_ => ResponderBucket.None
				};
			}

			var detail = await _customStateService.GetCustomDetailForDepartmentAsync(departmentId, log.ActionTypeId);
			return BucketForBaseType(detail?.BaseType);
		}

		private async Task<ResponderBucket> ClassifyUnitAsync(int departmentId, int state)
		{
			if (state <= 25)
			{
				return (UnitStateTypes)state switch
				{
					UnitStateTypes.Responding => ResponderBucket.EnRoute,
					UnitStateTypes.Enroute => ResponderBucket.EnRoute,
					UnitStateTypes.OnScene => ResponderBucket.OnScene,
					UnitStateTypes.Staging => ResponderBucket.OnScene,
					_ => ResponderBucket.None
				};
			}

			var detail = await _customStateService.GetCustomDetailForDepartmentAsync(departmentId, state);
			return BucketForBaseType(detail?.BaseType);
		}

		private static ResponderBucket BucketForBaseType(int? baseType)
		{
			if (!baseType.HasValue)
				return ResponderBucket.None;

			return (ActionBaseTypes)baseType.Value switch
			{
				ActionBaseTypes.Responding => ResponderBucket.EnRoute,
				ActionBaseTypes.Enroute => ResponderBucket.EnRoute,
				ActionBaseTypes.Transporting => ResponderBucket.EnRoute,
				ActionBaseTypes.OnScene => ResponderBucket.OnScene,
				ActionBaseTypes.MadeContact => ResponderBucket.OnScene,
				ActionBaseTypes.AtPatient => ResponderBucket.OnScene,
				ActionBaseTypes.Staging => ResponderBucket.OnScene,
				ActionBaseTypes.Searching => ResponderBucket.OnScene,
				_ => ResponderBucket.None
			};
		}
	}
}
