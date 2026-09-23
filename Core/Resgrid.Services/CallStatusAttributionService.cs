using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Server-side call attribution for statuses (write) and call-record inference (read); rules in
	/// <see cref="CallStatusAttribution"/>. Depends on repositories and the custom state service only, so the units and
	/// action log services can take it without a construction cycle.
	/// </summary>
	public class CallStatusAttributionService : ICallStatusAttributionService
	{
		private readonly ICallsRepository _callsRepository;
		private readonly ICallDispatchUnitRepository _callDispatchUnitRepository;
		private readonly ICallDispatchesRepository _callDispatchesRepository;
		private readonly ICallDispatchGroupRepository _callDispatchGroupRepository;
		private readonly ICallDispatchRoleRepository _callDispatchRoleRepository;
		private readonly IDepartmentGroupMembersRepository _departmentGroupMembersRepository;
		private readonly IPersonnelRoleUsersRepository _personnelRoleUsersRepository;
		private readonly IUnitStatesRepository _unitStatesRepository;
		private readonly IActionLogsRepository _actionLogsRepository;
		private readonly IUnitsRepository _unitsRepository;
		private readonly ICustomStateService _customStateService;

		public CallStatusAttributionService(ICallsRepository callsRepository, ICallDispatchUnitRepository callDispatchUnitRepository,
			ICallDispatchesRepository callDispatchesRepository, ICallDispatchGroupRepository callDispatchGroupRepository,
			ICallDispatchRoleRepository callDispatchRoleRepository, IDepartmentGroupMembersRepository departmentGroupMembersRepository,
			IPersonnelRoleUsersRepository personnelRoleUsersRepository, IUnitStatesRepository unitStatesRepository,
			IActionLogsRepository actionLogsRepository, IUnitsRepository unitsRepository, ICustomStateService customStateService)
		{
			_callsRepository = callsRepository;
			_callDispatchUnitRepository = callDispatchUnitRepository;
			_callDispatchesRepository = callDispatchesRepository;
			_callDispatchGroupRepository = callDispatchGroupRepository;
			_callDispatchRoleRepository = callDispatchRoleRepository;
			_departmentGroupMembersRepository = departmentGroupMembersRepository;
			_personnelRoleUsersRepository = personnelRoleUsersRepository;
			_unitStatesRepository = unitStatesRepository;
			_actionLogsRepository = actionLogsRepository;
			_unitsRepository = unitsRepository;
			_customStateService = customStateService;
		}

		#region Write-time attribution
		public async Task AttributeUnitStateAsync(UnitState state, UnitState previousState, int departmentId)
		{
			if (state == null)
				return;

			if (state.DestinationId.HasValue && state.DestinationId.Value > 0)
			{
				state.DestinationSource ??= (int)StatusDestinationSources.Explicit;
				return;
			}

			try
			{
				// Carry-forward and dispatch read the unit's state now; a replayed offline state is placed by the read-time walk.
				if (!CallStatusAttribution.IsLiveStatus(state.Timestamp, DateTime.UtcNow))
					return;

				var baseTypes = await GetBaseTypesAsync(departmentId, CustomStateTypes.Unit);
				var previous = previousState != null && previousState.UnitId == state.UnitId ? previousState : null;
				var previousCallId = previous != null ? CallStatusLinkage.LinkedCallId(previous.DestinationId, previous.DestinationType) : null;
				var previousIsClearing = previous == null || CallStatusLinkage.IsClearingUnitState(previous.State, baseTypes);

				if (previousCallId.HasValue && !previousIsClearing &&
					CallStatusAttribution.CanCarryForward(previousCallId, previousIsClearing, await IsOpenCallAsync(departmentId, previousCallId.Value)))
				{
					Link(state, previousCallId.Value, StatusDestinationSources.CarryForward);
					return;
				}

				if (CallStatusLinkage.IsClearingUnitState(state.State, baseTypes))
					return;

				var openCalls = await _callDispatchUnitRepository.GetOpenCallIdsForUnitAsync(departmentId, state.UnitId);
				var callId = CallStatusAttribution.PickDispatchCall(openCalls, previousIsClearing ? previousCallId : null);

				if (callId.HasValue)
					Link(state, callId.Value, StatusDestinationSources.Dispatch);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Call attribution failed for a state of unit {state.UnitId}; it is saved without a destination.");
			}
		}

		public async Task AttributeActionLogAsync(ActionLog actionLog, ActionLog previousActionLog)
		{
			if (actionLog == null)
				return;

			if (actionLog.DestinationId.HasValue && actionLog.DestinationId.Value > 0)
			{
				actionLog.DestinationSource ??= (int)StatusDestinationSources.Explicit;
				return;
			}

			try
			{
				// A person placed on a unit by a unit status goes wherever that unit status went.
				if (actionLog.UnitStateId.HasValue && actionLog.UnitStateId.Value > 0)
				{
					var unitState = await _unitStatesRepository.GetUnitStateByUnitStateIdAsync(actionLog.UnitStateId.Value);
					var unitCallId = unitState != null ? CallStatusLinkage.LinkedCallId(unitState.DestinationId, unitState.DestinationType) : null;

					if (unitCallId.HasValue)
					{
						Link(actionLog, unitCallId.Value, StatusDestinationSources.Unit);
						return;
					}
				}

				// Carry-forward and dispatch read the person's state now; a replayed offline status is placed by the read-time walk.
				if (!CallStatusAttribution.IsLiveStatus(actionLog.Timestamp, DateTime.UtcNow))
					return;

				var departmentId = actionLog.DepartmentId;
				var baseTypes = await GetBaseTypesAsync(departmentId, CustomStateTypes.Personnel);
				var previous = previousActionLog != null && previousActionLog.UserId == actionLog.UserId && previousActionLog.DepartmentId == departmentId
					? previousActionLog : null;
				var previousCallId = previous != null ? CallStatusLinkage.LinkedCallId(previous.DestinationId, previous.DestinationType) : null;
				var previousIsClearing = previous == null || CallStatusLinkage.IsClearingPersonnelStatus(previous.ActionTypeId, baseTypes);

				if (previousCallId.HasValue && !previousIsClearing &&
					CallStatusAttribution.CanCarryForward(previousCallId, previousIsClearing, await IsOpenCallAsync(departmentId, previousCallId.Value)))
				{
					Link(actionLog, previousCallId.Value, StatusDestinationSources.CarryForward);
					return;
				}

				if (string.IsNullOrWhiteSpace(actionLog.UserId) || CallStatusLinkage.IsClearingPersonnelStatus(actionLog.ActionTypeId, baseTypes))
					return;

				var openCalls = await _callDispatchesRepository.GetOpenCallIdsForUserAsync(departmentId, actionLog.UserId);
				var callId = CallStatusAttribution.PickDispatchCall(openCalls, previousIsClearing ? previousCallId : null);

				if (callId.HasValue)
					Link(actionLog, callId.Value, StatusDestinationSources.Dispatch);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Call attribution failed for a status of user {actionLog.UserId}; it is saved without a destination.");
			}
		}
		#endregion Write-time attribution

		#region Read-time inference
		public async Task<List<UnitState>> GetInferredUnitStatesForCallAsync(int departmentId, int callId)
		{
			var inferred = new List<UnitState>();

			var call = await GetDepartmentCallAsync(departmentId, callId);
			if (call == null)
				return inferred;

			var dispatches = (await _callDispatchUnitRepository.GetCallUnitDispatchesByCallIdAsync(callId))?.ToList() ?? new List<CallDispatchUnit>();
			if (dispatches.Count == 0)
				return inferred;

			var end = CallEnd(call);
			var subjects = EarliestPerUnit(dispatches).Select(x => (x.UnitId, Start: DispatchStart(x.DispatchedOn, call))).Where(x => x.Start <= end).ToList();
			if (subjects.Count == 0)
				return inferred;

			var (statusLookup, baseTypes) = await GetStatusRulesAsync(departmentId, CustomStateTypes.Unit);
			var windowsByUnit = await GetUnitDispatchWindowsAsync(departmentId, subjects.Min(x => x.Start), end);

			foreach (var subject in subjects)
			{
				// Another dispatch that overlaps this one is walked from its own start, so the states are read from there.
				var others = OtherDispatches(windowsByUnit, subject.UnitId, call.CallId, subject.Start);
				var from = others != null ? Earliest(subject.Start, others.Min(x => x.Start)) : subject.Start;

				var states = await _unitStatesRepository.GetAllUnitStatesForUnitInDateRangeAsync(subject.UnitId, from, end);
				inferred.AddRange(CallStatusAttribution.InferUnitStates(call.CallId, subject.Start, end, states,
					s => CallStatusLinkage.IsClearingUnitState(s.State, baseTypes), statusLookup, others));
			}

			await PopulateUnitsAsync(departmentId, inferred);

			return inferred;
		}

		public async Task<List<ActionLog>> GetInferredActionLogsForCallAsync(int departmentId, int callId)
		{
			var call = await GetDepartmentCallAsync(departmentId, callId);
			if (call == null)
				return new List<ActionLog>();

			var groupDispatches = (await _callDispatchGroupRepository.GetAllCallDispatchGroupByCallIdAsync(callId))?.ToList() ?? new List<CallDispatchGroup>();
			var roleDispatches = (await _callDispatchRoleRepository.GetCallRoleDispatchesByCallIdAsync(callId))?.ToList() ?? new List<CallDispatchRole>();

			var groupMembers = await GetGroupMembersAsync(departmentId, groupDispatches.Select(x => x.DepartmentGroupId));
			var roleMembers = new Dictionary<int, List<string>>();
			foreach (var roleId in roleDispatches.Select(x => x.RoleId).Distinct())
				roleMembers[roleId] = ((await _personnelRoleUsersRepository.GetAllMembersOfRoleAsync(roleId)) ?? Enumerable.Empty<PersonnelRoleUser>())
					.Where(x => x.DepartmentId == departmentId).Select(x => x.UserId).ToList();

			var subjects = BuildPersonnelSubjects(call, await _callDispatchesRepository.GetCallDispatchesByCallIdAsync(callId), groupDispatches, roleDispatches, groupMembers, roleMembers);
			if (subjects.Count == 0)
				return new List<ActionLog>();

			var end = CallEnd(call);
			var windowStart = subjects.Values.Min(x => x.Start);
			if (windowStart > end)
				return new List<ActionLog>();

			var (statusLookup, baseTypes) = await GetStatusRulesAsync(departmentId, CustomStateTypes.Personnel);
			var windowsByUser = await GetPersonnelDispatchWindowsAsync(departmentId, windowStart, end);
			var logsFrom = Earliest(windowStart, EarliestDispatch(windowsByUser, subjects.Keys));
			var logsByUser = GroupByUser(departmentId, await _actionLogsRepository.GetAllActionLogsInDateRangeAsync(departmentId, logsFrom, end));

			return InferPersonnel(call.CallId, end, subjects, logsByUser, baseTypes, statusLookup, windowsByUser);
		}

		public async Task<Dictionary<int, List<ActionLog>>> GetActionLogsForCallsAsync(int departmentId, IReadOnlyCollection<Call> calls)
		{
			var result = new Dictionary<int, List<ActionLog>>();
			var departmentCalls = calls?.Where(x => x != null && x.DepartmentId == departmentId).GroupBy(x => x.CallId).Select(g => g.First()).ToList() ?? new List<Call>();
			if (departmentCalls.Count == 0)
				return result;

			foreach (var call in departmentCalls)
				result[call.CallId] = new List<ActionLog>();

			var loggedFrom = departmentCalls.Min(x => x.LoggedOn);
			var loggedTo = departmentCalls.Max(x => x.LoggedOn);

			var direct = ((await _callDispatchesRepository.GetCallDispatchesForCallsInRangeAsync(departmentId, loggedFrom, loggedTo)) ?? Enumerable.Empty<CallDispatch>())
				.Where(x => x != null && result.ContainsKey(x.CallId)).ToList();
			var groupDispatches = ((await _callDispatchGroupRepository.GetCallDispatchGroupsForCallsInRangeAsync(departmentId, loggedFrom, loggedTo)) ?? Enumerable.Empty<CallDispatchGroup>())
				.Where(x => x != null && result.ContainsKey(x.CallId)).ToList();
			var roleDispatches = ((await _callDispatchRoleRepository.GetCallDispatchRolesForCallsInRangeAsync(departmentId, loggedFrom, loggedTo)) ?? Enumerable.Empty<CallDispatchRole>())
				.Where(x => x != null && result.ContainsKey(x.CallId)).ToList();

			var groupMembers = await GetGroupMembersAsync(departmentId, groupDispatches.Select(x => x.DepartmentGroupId));
			var roleMembers = roleDispatches.Count == 0 ? new Dictionary<int, List<string>>()
				: ((await _personnelRoleUsersRepository.GetAllRoleUsersForDepartmentAsync(departmentId)) ?? Enumerable.Empty<PersonnelRoleUser>())
					.Where(x => x != null && x.DepartmentId == departmentId)
					.GroupBy(x => x.PersonnelRoleId)
					.ToDictionary(g => g.Key, g => g.Select(x => x.UserId).ToList());

			// Statuses can trail a call (a status set against it after it closed), hence the extra day on the read window.
			var windowStart = loggedFrom;
			var windowEnd = departmentCalls.Max(CallEnd).AddDays(1);

			var (statusLookup, baseTypes) = await GetStatusRulesAsync(departmentId, CustomStateTypes.Personnel);
			var windowsByUser = await GetPersonnelDispatchWindowsAsync(departmentId, windowStart, windowEnd);
			var logsFrom = Earliest(windowStart, EarliestDispatch(windowsByUser, windowsByUser.Keys));

			var logs = ((await _actionLogsRepository.GetAllActionLogsInDateRangeAsync(departmentId, logsFrom, windowEnd)) ?? Enumerable.Empty<ActionLog>())
				.Where(x => x != null && x.DepartmentId == departmentId).ToList();

			foreach (var log in logs)
			{
				var linkedCallId = CallStatusLinkage.LinkedCallId(log.DestinationId, log.DestinationType);
				if (linkedCallId.HasValue && result.TryGetValue(linkedCallId.Value, out var callLogs) && log.BelongsToCall(statusLookup))
					callLogs.Add(log);
			}

			var logsByUser = GroupByUser(departmentId, logs);
			foreach (var call in departmentCalls)
			{
				var subjects = BuildPersonnelSubjects(call, direct.Where(x => x.CallId == call.CallId), groupDispatches.Where(x => x.CallId == call.CallId),
					roleDispatches.Where(x => x.CallId == call.CallId), groupMembers, roleMembers);
				if (subjects.Count == 0)
					continue;

				result[call.CallId].AddRange(InferPersonnel(call.CallId, CallEnd(call), subjects, logsByUser, baseTypes, statusLookup, windowsByUser));
			}

			return result;
		}

		public async Task<Dictionary<int, List<CallDispatchUnit>>> GetUnitDispatchesForCallsAsync(int departmentId, IReadOnlyCollection<Call> calls)
		{
			var result = new Dictionary<int, List<CallDispatchUnit>>();
			var departmentCalls = calls?.Where(x => x != null && x.DepartmentId == departmentId).ToList() ?? new List<Call>();
			if (departmentCalls.Count == 0)
				return result;

			foreach (var call in departmentCalls)
				result[call.CallId] = new List<CallDispatchUnit>();

			var dispatches = await _callDispatchUnitRepository.GetCallUnitDispatchesForCallsInRangeAsync(departmentId,
				departmentCalls.Min(x => x.LoggedOn), departmentCalls.Max(x => x.LoggedOn)) ?? Enumerable.Empty<CallDispatchUnit>();

			foreach (var dispatch in dispatches.Where(x => x != null && result.ContainsKey(x.CallId)))
				result[dispatch.CallId].Add(dispatch);

			return result;
		}

		public async Task<Dictionary<int, List<UnitState>>> GetUnitStatesForCallsAsync(int departmentId, IReadOnlyCollection<Call> calls)
		{
			var result = new Dictionary<int, List<UnitState>>();
			var departmentCalls = calls?.Where(x => x != null && x.DepartmentId == departmentId).GroupBy(x => x.CallId).Select(g => g.First()).ToList() ?? new List<Call>();
			if (departmentCalls.Count == 0)
				return result;

			foreach (var call in departmentCalls)
				result[call.CallId] = new List<UnitState>();

			var dispatchesByCall = await GetUnitDispatchesForCallsAsync(departmentId, departmentCalls);
			var units = (await _unitsRepository.GetAllUnitsByDepartmentIdAsync(departmentId))?.ToList() ?? new List<Unit>();

			// States can trail a call (a status set against it after it closed), hence the extra day on the read window.
			var windowStart = departmentCalls.Min(x => x.LoggedOn);
			var windowEnd = departmentCalls.Max(CallEnd).AddDays(1);

			var (statusLookup, baseTypes) = await GetStatusRulesAsync(departmentId, CustomStateTypes.Unit);
			var windowsByUnit = await GetUnitDispatchWindowsAsync(departmentId, windowStart, windowEnd);

			var statesByUnit = new Dictionary<int, List<UnitState>>();
			foreach (var unit in units)
			{
				var statesFrom = Earliest(windowStart, EarliestDispatch(windowsByUnit, new[] { unit.UnitId }));
				var states = (await _unitStatesRepository.GetAllUnitStatesForUnitInDateRangeAsync(unit.UnitId, statesFrom, windowEnd))?.Where(x => x != null).ToList() ?? new List<UnitState>();
				foreach (var state in states)
					state.Unit ??= unit;

				statesByUnit[unit.UnitId] = states;

				foreach (var state in states)
				{
					var linkedCallId = CallStatusLinkage.LinkedCallId(state.DestinationId, state.DestinationType);
					if (linkedCallId.HasValue && result.TryGetValue(linkedCallId.Value, out var callStates) && state.BelongsToCall(statusLookup))
						callStates.Add(state);
				}
			}

			foreach (var call in departmentCalls)
			{
				var end = CallEnd(call);

				foreach (var dispatch in EarliestPerUnit(dispatchesByCall[call.CallId]))
				{
					if (!statesByUnit.TryGetValue(dispatch.UnitId, out var states))
						continue;

					var start = DispatchStart(dispatch.DispatchedOn, call);
					if (start > end)
						continue;

					result[call.CallId].AddRange(CallStatusAttribution.InferUnitStates(call.CallId, start, end, states,
						s => CallStatusLinkage.IsClearingUnitState(s.State, baseTypes), statusLookup, OtherDispatches(windowsByUnit, dispatch.UnitId, call.CallId, start)));
				}
			}

			return result;
		}
		#endregion Read-time inference

		#region Helpers
		/// <summary>
		/// The people a call was dispatched to, with when and how: individually (count from the dispatch) or through a paged
		/// group or role (count from their first engaging status). Someone reached both ways counts as dispatched individually.
		/// </summary>
		private static Dictionary<string, (DateTime Start, bool RequireEngagement)> BuildPersonnelSubjects(Call call, IEnumerable<CallDispatch> dispatches,
			IEnumerable<CallDispatchGroup> groupDispatches, IEnumerable<CallDispatchRole> roleDispatches,
			IReadOnlyDictionary<int, List<string>> groupMembers, IReadOnlyDictionary<int, List<string>> roleMembers)
		{
			var subjects = new Dictionary<string, (DateTime Start, bool RequireEngagement)>(StringComparer.OrdinalIgnoreCase);
			void Add(string userId, DateTime dispatchedOn, bool requireEngagement)
			{
				if (string.IsNullOrWhiteSpace(userId))
					return;

				var start = DispatchStart(dispatchedOn, call);
				if (subjects.TryGetValue(userId, out var existing))
					subjects[userId] = (existing.Start < start ? existing.Start : start, existing.RequireEngagement && requireEngagement);
				else
					subjects[userId] = (start, requireEngagement);
			}

			foreach (var dispatch in dispatches ?? Enumerable.Empty<CallDispatch>())
				if (dispatch != null)
					Add(dispatch.UserId, dispatch.DispatchedOn, false);

			foreach (var groupDispatch in groupDispatches ?? Enumerable.Empty<CallDispatchGroup>())
				if (groupDispatch != null && groupMembers.TryGetValue(groupDispatch.DepartmentGroupId, out var members))
					foreach (var member in members)
						Add(member, groupDispatch.DispatchedOn, true);

			foreach (var roleDispatch in roleDispatches ?? Enumerable.Empty<CallDispatchRole>())
				if (roleDispatch != null && roleMembers.TryGetValue(roleDispatch.RoleId, out var members))
					foreach (var member in members)
						Add(member, roleDispatch.DispatchedOn, true);

			return subjects;
		}

		private static List<ActionLog> InferPersonnel(int callId, DateTime end, Dictionary<string, (DateTime Start, bool RequireEngagement)> subjects,
			IReadOnlyDictionary<string, List<ActionLog>> logsByUser, IReadOnlyDictionary<int, int> baseTypes,
			IReadOnlyDictionary<int, CustomStateDetail> statusLookup, Dictionary<string, List<CallDispatchWindow>> windowsByUser)
		{
			var inferred = new List<ActionLog>();
			foreach (var subject in subjects)
			{
				if (subject.Value.Start > end || !logsByUser.TryGetValue(subject.Key, out var logs))
					continue;

				inferred.AddRange(CallStatusAttribution.InferActionLogs(callId, subject.Value.Start, end, subject.Value.RequireEngagement, logs,
					l => CallStatusLinkage.IsClearingPersonnelStatus(l.ActionTypeId, baseTypes), statusLookup,
					OtherDispatches(windowsByUser, subject.Key, callId, subject.Value.Start)));
			}

			return inferred;
		}

		private static Dictionary<string, List<ActionLog>> GroupByUser(int departmentId, IEnumerable<ActionLog> logs)
		{
			return (logs ?? Enumerable.Empty<ActionLog>())
				.Where(x => x != null && !string.IsNullOrWhiteSpace(x.UserId) && x.DepartmentId == departmentId)
				.GroupBy(x => x.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
		}

		private async Task<Dictionary<int, List<string>>> GetGroupMembersAsync(int departmentId, IEnumerable<int> groupIds)
		{
			var members = new Dictionary<int, List<string>>();
			foreach (var groupId in (groupIds ?? Enumerable.Empty<int>()).Distinct())
				members[groupId] = ((await _departmentGroupMembersRepository.GetAllGroupMembersByGroupIdAsync(groupId)) ?? Enumerable.Empty<DepartmentGroupMember>())
					.Where(x => x != null && x.DepartmentId == departmentId).Select(x => x.UserId).ToList();

			return members;
		}

		private static void Link(UnitState state, int callId, StatusDestinationSources source)
		{
			state.DestinationId = callId;
			state.DestinationType = (int)DestinationEntityTypes.Call;
			state.DestinationSource = (int)source;
		}

		private static void Link(ActionLog actionLog, int callId, StatusDestinationSources source)
		{
			actionLog.DestinationId = callId;
			actionLog.DestinationType = (int)DestinationEntityTypes.Call;
			actionLog.DestinationSource = (int)source;
		}

		private async Task<Dictionary<int, int>> GetBaseTypesAsync(int departmentId, CustomStateTypes type)
		{
			var customStates = await _customStateService.GetAllCustomStatesForDepartmentAsync(departmentId);

			return CallStatusLinkage.BuildBaseTypeMap(customStates, type);
		}

		/// <summary>
		/// One status kind's raw-status lookup (built-in and custom statuses, for <see cref="CallStatusLinkage.BelongsToCall(int?, CustomStateDetail)"/>)
		/// and custom base-type map (for the clearing rules).
		/// </summary>
		private async Task<(Dictionary<int, CustomStateDetail> StatusLookup, Dictionary<int, int> BaseTypes)> GetStatusRulesAsync(int departmentId, CustomStateTypes type)
		{
			var customStates = await _customStateService.GetAllCustomStatesForDepartmentAsync(departmentId);
			var builtIn = type == CustomStateTypes.Unit ? _customStateService.GetDefaultUnitStatuses() : _customStateService.GetDefaultPersonStatuses();

			return (CallStatusLinkage.BuildStatusLookup(builtIn, customStates, type), CallStatusLinkage.BuildBaseTypeMap(customStates, type));
		}

		/// <summary>
		/// Each unit's dispatches made from <see cref="CallStatusAttribution.OverlapLookback"/> before <paramref name="from"/>
		/// to <paramref name="to"/>, the ones that can overlap a dispatch in that window.
		/// </summary>
		private async Task<Dictionary<int, List<CallDispatchWindow>>> GetUnitDispatchWindowsAsync(int departmentId, DateTime from, DateTime to)
		{
			var dispatchedFrom = from.Subtract(CallStatusAttribution.OverlapLookback);
			var windows = await _callDispatchUnitRepository.GetUnitDispatchWindowsAsync(departmentId, dispatchedFrom, to,
				dispatchedFrom.Subtract(CallStatusAttribution.MaxOverlapCallAge));

			return (windows ?? Enumerable.Empty<CallDispatchWindow>()).Where(x => x != null)
				.GroupBy(x => x.UnitId).ToDictionary(g => g.Key, g => g.ToList());
		}

		/// <summary>The personnel equivalent of <see cref="GetUnitDispatchWindowsAsync"/>, by user.</summary>
		private async Task<Dictionary<string, List<CallDispatchWindow>>> GetPersonnelDispatchWindowsAsync(int departmentId, DateTime from, DateTime to)
		{
			var dispatchedFrom = from.Subtract(CallStatusAttribution.OverlapLookback);
			var windows = await _callDispatchesRepository.GetPersonnelDispatchWindowsAsync(departmentId, dispatchedFrom, to,
				dispatchedFrom.Subtract(CallStatusAttribution.MaxOverlapCallAge));

			return (windows ?? Enumerable.Empty<CallDispatchWindow>()).Where(x => x != null && !string.IsNullOrWhiteSpace(x.UserId))
				.GroupBy(x => x.UserId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
		}

		/// <summary>
		/// The unit's/person's dispatches to calls other than this one, made from <see cref="CallStatusAttribution.OverlapLookback"/>
		/// before their dispatch to it (<paramref name="start"/>); null when there are none.
		/// </summary>
		private static List<CallDispatchSpan> OtherDispatches<TKey>(Dictionary<TKey, List<CallDispatchWindow>> windows, TKey key, int callId, DateTime start)
		{
			if (!windows.TryGetValue(key, out var subjectWindows))
				return null;

			var from = start.Subtract(CallStatusAttribution.OverlapLookback);
			var others = CallStatusAttribution.DispatchSpans(subjectWindows.Where(x => x.CallId != callId), DateTime.UtcNow)
				.Where(x => x.Start >= from).ToList();

			return others.Count > 0 ? others : null;
		}

		/// <summary>The earliest dispatch among the given units'/people's windows, or null when they have none.</summary>
		private static DateTime? EarliestDispatch<TKey>(Dictionary<TKey, List<CallDispatchWindow>> windows, IEnumerable<TKey> keys)
		{
			DateTime? earliest = null;
			foreach (var key in keys)
			{
				if (!windows.TryGetValue(key, out var subjectWindows))
					continue;

				foreach (var span in CallStatusAttribution.DispatchSpans(subjectWindows, DateTime.UtcNow))
					earliest = earliest.HasValue && earliest.Value <= span.Start ? earliest : span.Start;
			}

			return earliest;
		}

		private static DateTime Earliest(DateTime value, DateTime? other)
		{
			return other.HasValue && other.Value < value ? other.Value : value;
		}

		private async Task<bool> IsOpenCallAsync(int departmentId, int callId)
		{
			var call = await GetDepartmentCallAsync(departmentId, callId);

			return call != null && call.State == (int)CallStates.Active;
		}

		private async Task<Call> GetDepartmentCallAsync(int departmentId, int callId)
		{
			if (callId <= 0)
				return null;

			var call = await _callsRepository.GetByIdAsync(callId);

			return call != null && call.DepartmentId == departmentId && !call.IsDeleted ? call : null;
		}

		private static DateTime CallEnd(Call call)
		{
			return call.ClosedOn ?? DateTime.UtcNow;
		}

		private static DateTime DispatchStart(DateTime dispatchedOn, Call call)
		{
			return dispatchedOn == default(DateTime) ? call.LoggedOn : dispatchedOn;
		}

		private static IEnumerable<CallDispatchUnit> EarliestPerUnit(IEnumerable<CallDispatchUnit> dispatches)
		{
			return (dispatches ?? Enumerable.Empty<CallDispatchUnit>()).Where(x => x != null)
				.GroupBy(x => x.UnitId).Select(g => g.OrderBy(x => x.DispatchedOn).First());
		}

		private async Task PopulateUnitsAsync(int departmentId, List<UnitState> states)
		{
			if (states.All(x => x.Unit != null))
				return;

			var units = (await _unitsRepository.GetAllUnitsByDepartmentIdAsync(departmentId))?.ToDictionary(x => x.UnitId) ?? new Dictionary<int, Unit>();
			foreach (var state in states.Where(x => x.Unit == null))
			{
				if (units.TryGetValue(state.UnitId, out var unit))
					state.Unit = unit;
			}
		}
		#endregion Helpers
	}
}
