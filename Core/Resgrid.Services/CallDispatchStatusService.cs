using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class CallDispatchStatusService : ICallDispatchStatusService
	{
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IShiftsService _shiftsService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IUnitsService _unitsService;
		private readonly ICustomStateService _customStateService;
		private readonly ICallsRepository _callsRepository;

		public CallDispatchStatusService(
			IDepartmentSettingsService departmentSettingsService,
			IDepartmentsService departmentsService,
			IShiftsService shiftsService,
			IActionLogsService actionLogsService,
			IUnitsService unitsService,
			ICustomStateService customStateService,
			ICallsRepository callsRepository)
		{
			_departmentSettingsService = departmentSettingsService;
			_departmentsService = departmentsService;
			_shiftsService = shiftsService;
			_actionLogsService = actionLogsService;
			_unitsService = unitsService;
			_customStateService = customStateService;
			_callsRepository = callsRepository;
		}

		public async Task ApplyDispatchStatusesAsync(Call call, IEnumerable<int> groupIds = null, IEnumerable<int> unitIds = null, CancellationToken cancellationToken = default(CancellationToken))
		{
			await ApplyStatusesAsync(call, groupIds, unitIds, true, cancellationToken);
		}

		public async Task ApplyReleaseStatusesAsync(Call call, IEnumerable<int> groupIds = null, IEnumerable<int> unitIds = null, CancellationToken cancellationToken = default(CancellationToken))
		{
			await ApplyStatusesAsync(call, groupIds, unitIds, false, cancellationToken);
		}

		private async Task ApplyStatusesAsync(Call call, IEnumerable<int> groupIds, IEnumerable<int> unitIds, bool isDispatch, CancellationToken cancellationToken)
		{
			if (call == null)
				throw new ArgumentNullException(nameof(call));

			var resolvedGroupIds = GetDistinctIds(groupIds, call.GroupDispatches?.Select(x => x.DepartmentGroupId));
			var resolvedUnitIds = GetDistinctIds(unitIds, call.UnitDispatches?.Select(x => x.UnitId));

			if (!resolvedGroupIds.Any() && !resolvedUnitIds.Any())
				return;

			var department = await _departmentsService.GetDepartmentByIdAsync(call.DepartmentId);

			// These are the department's dispatch statuses, applied by Resgrid: the member who dispatched or closed the call
			// stays the actor, and the origin says nobody pressed the button (M0260).
			using (StatusWriteActor.BeginAutomation(StatusSetOrigins.DispatchAutomation))
			{
				if (resolvedGroupIds.Any())
					await ApplyPersonnelStatusesAsync(call, department, resolvedGroupIds, isDispatch, cancellationToken);

				if (resolvedUnitIds.Any())
					await ApplyUnitStatusesAsync(call, department, resolvedUnitIds, isDispatch, cancellationToken);
			}
		}

		private async Task ApplyPersonnelStatusesAsync(Call call, Department department, IReadOnlyCollection<int> groupIds, bool isDispatch, CancellationToken cancellationToken)
		{
			var dispatchShiftInsteadOfGroup = await _departmentSettingsService.GetDispatchShiftInsteadOfGroupAsync(call.DepartmentId);
			var autoSetStatusForShiftPersonnel = await _departmentSettingsService.GetAutoSetStatusForShiftDispatchPersonnelAsync(call.DepartmentId);

			if (!dispatchShiftInsteadOfGroup || !autoSetStatusForShiftPersonnel)
				return;

			var shiftUserIds = await GetShiftUserIdsAsync(call, department, groupIds);
			if (!shiftUserIds.Any())
				return;

			var statusToSet = isDispatch
				? await _departmentSettingsService.GetShiftCallDispatchPersonnelStatusToSetAsync(call.DepartmentId)
				: await _departmentSettingsService.GetShiftCallReleasePersonnelStatusToSetAsync(call.DepartmentId);

			// Default: the department's own Responding / Available status when it has a personnel status set (a built-in
			// value would sit outside it), else the built-in one.
			if (statusToSet < 0)
				statusToSet = (isDispatch
					? await _customStateService.ResolvePersonnelStatusAsync(call.DepartmentId, ActionBaseTypes.Responding, DestinationEntityTypes.Call)
					: await _customStateService.ResolvePersonnelStatusAsync(call.DepartmentId, ActionBaseTypes.Available))
					?? (isDispatch ? (int)ActionTypes.RespondingToScene : (int)ActionTypes.StandingBy);

			foreach (var userId in shiftUserIds)
			{
				await _actionLogsService.SetUserActionAsync(userId, call.DepartmentId, statusToSet, null, call.CallId, (int)DestinationEntityTypes.Call, cancellationToken);
			}
		}

		private async Task ApplyUnitStatusesAsync(Call call, Department department, IReadOnlyCollection<int> unitIds, bool isDispatch, CancellationToken cancellationToken)
		{
			// What the department asked for, resolved per unit against the unit's own status set (StatusTargetResolver):
			// a unit whose type uses a custom set gets that set's option, never a built-in value outside it.
			var target = isDispatch
				? await _departmentSettingsService.GetUnitCallDispatchStatusTargetAsync(call.DepartmentId)
				: await _departmentSettingsService.GetUnitCallReleaseStatusTargetAsync(call.DepartmentId);

			if (!isDispatch)
			{
				unitIds = await GetUnitsToReleaseAsync(call, unitIds);

				if (!unitIds.Any())
					return;
			}

			var resolvedStatuses = await ResolveUnitStatusesAsync(call.DepartmentId, unitIds, target, isDispatch);

			var timestamp = DateTime.UtcNow;
			var localTimestamp = department != null ? DateTimeHelpers.GetLocalDateTime(timestamp, department.TimeZone) : timestamp;

			foreach (var unitId in unitIds)
			{
				if (!resolvedStatuses.TryGetValue(unitId, out var statusToSet) || !statusToSet.HasValue)
				{
					Logging.LogDebug($"Call {call.CallId} {(isDispatch ? "dispatch" : "release")}: unit {unitId} keeps its status (left unchanged, or its status set has no matching option).");
					continue;
				}

				var state = new UnitState
				{
					UnitId = unitId,
					State = statusToSet.Value,
					Timestamp = timestamp,
					LocalTimestamp = localTimestamp,
					DestinationId = call.CallId,
					DestinationType = (int)DestinationEntityTypes.Call
				};

				await _unitsService.SetUnitStateAsync(state, call.DepartmentId, cancellationToken, autoGenerated: true);
			}
		}

		/// <summary>
		/// The units a release should move: only those still working this call. A unit already back in service, taken out of
		/// service (or made unavailable, or put on a break) by someone, or now on another open call keeps the status it has:
		/// releasing it would put an out-of-service unit back in the dispatchable pool, or pull a unit off its next call.
		/// </summary>
		private async Task<IReadOnlyCollection<int>> GetUnitsToReleaseAsync(Call call, IReadOnlyCollection<int> unitIds)
		{
			var baseTypes = CallStatusLinkage.BuildUnitBaseTypeMap(await _customStateService.GetAllCustomStatesForDepartmentAsync(call.DepartmentId));
			var toRelease = new List<int>();

			foreach (var unitId in unitIds)
			{
				var current = await _unitsService.GetLastUnitStateByUnitIdAsync(unitId);

				if (current == null)
				{
					toRelease.Add(unitId);
					continue;
				}

				if (UnitCallInvolvement.KeepsStatusOnRelease(current.State, baseTypes))
				{
					Logging.LogDebug($"Call {call.CallId} release: unit {unitId} keeps status {current.State} (back in service or taken out of service).");
					continue;
				}

				if (current.DestinationType == (int)DestinationEntityTypes.Call && current.DestinationId.HasValue &&
					current.DestinationId.Value != call.CallId && await IsOpenCallAsync(call.DepartmentId, current.DestinationId.Value))
				{
					Logging.LogDebug($"Call {call.CallId} release: unit {unitId} keeps status {current.State} (working call {current.DestinationId.Value}).");
					continue;
				}

				toRelease.Add(unitId);
			}

			return toRelease;
		}

		private async Task<bool> IsOpenCallAsync(int departmentId, int callId)
		{
			var call = await _callsRepository.GetByIdAsync(callId);

			return call != null && call.DepartmentId == departmentId && !call.IsDeleted && call.State == (int)CallStates.Active;
		}

		/// <summary>
		/// The status each unit gets, or null to leave it as it is. A unit type's override (a specific option of its own
		/// set) wins; otherwise the department-wide target is resolved against the unit's status set: the unit type's custom
		/// set, or the built-in statuses for a type without one (and for a unit whose type can't be found).
		/// </summary>
		private async Task<Dictionary<int, int?>> ResolveUnitStatusesAsync(int departmentId, IReadOnlyCollection<int> unitIds,
			StatusTarget target, bool isDispatch)
		{
			var defaultChain = isDispatch ? StatusTargetResolver.DispatchDefaultChain : StatusTargetResolver.ReleaseDefaultChain;
			var builtInDefault = isDispatch ? (int)UnitStateTypes.Responding : (int)UnitStateTypes.Released;

			var unitTypeOverrides = await _departmentSettingsService.GetUnitCallStatusOverridesByUnitTypeAsync(departmentId) ?? new List<UnitTypeCallStatusOverride>();
			var unitTypeOverrideLookup = unitTypeOverrides
				.Where(x => x != null && x.UnitTypeId > 0)
				.GroupBy(x => x.UnitTypeId)
				.ToDictionary(x => x.Key, x => x.Last());

			var units = (await Task.WhenAll(unitIds.Select(x => _unitsService.GetUnitByIdAsync(x))))
				.Where(x => x != null)
				.ToDictionary(x => x.UnitId);

			var unitTypesByName = new Dictionary<string, UnitType>(StringComparer.OrdinalIgnoreCase);

			foreach (var unitTypeName in units.Values
				.Select(x => x.Type)
				.Where(x => !String.IsNullOrWhiteSpace(x))
				.Distinct(StringComparer.OrdinalIgnoreCase))
			{
				var unitType = await _unitsService.GetUnitTypeByNameAsync(departmentId, unitTypeName);

				if (unitType != null)
					unitTypesByName[unitTypeName] = unitType;
			}

			var setsById = new Dictionary<int, CustomState>();
			foreach (var customStateId in unitTypesByName.Values
				.Where(x => x.CustomStatesId.HasValue && x.CustomStatesId.Value > 0)
				.Select(x => x.CustomStatesId.Value)
				.Distinct())
			{
				var customState = await _customStateService.GetCustomSateByIdAsync(customStateId);
				setsById[customStateId] = customState != null && !customState.IsDeleted ? customState : null;
			}

			var resolvedStatuses = new Dictionary<int, int?>();

			foreach (var unitId in unitIds)
			{
				UnitType unitType = null;
				if (units.TryGetValue(unitId, out var unit) && !String.IsNullOrWhiteSpace(unit.Type))
					unitTypesByName.TryGetValue(unit.Type, out unitType);

				CustomState set = null;
				if (unitType?.CustomStatesId != null && unitType.CustomStatesId.Value > 0)
					setsById.TryGetValue(unitType.CustomStatesId.Value, out set);

				if (set != null && unitTypeOverrideLookup.TryGetValue(unitType.UnitTypeId, out var unitTypeOverride))
				{
					var candidateStatus = isDispatch ? unitTypeOverride.DispatchStatus : unitTypeOverride.ReleaseStatus;

					if (candidateStatus >= 0 && set.GetActiveDetails().Any(x => x.CustomStateDetailId == candidateStatus))
					{
						resolvedStatuses[unitId] = candidateStatus;
						continue;
					}
				}

				resolvedStatuses[unitId] = StatusTargetResolver.ResolveUnit(target, set, defaultChain, builtInDefault).Value;
			}

			return resolvedStatuses;
		}

		private async Task<HashSet<string>> GetShiftUserIdsAsync(Call call, Department department, IReadOnlyCollection<int> groupIds)
		{
			// Whoever is on duty for each group at dispatch time: the resolved shift roster (assigned staff, approved
			// signups, single-day edits and trades), so a night shift is still covered after midnight.
			var onDuty = await _shiftsService.GetOnDutyUserIdsForGroupsAsync(call.DepartmentId, groupIds, GetReferenceDate(call));

			return new HashSet<string>((onDuty ?? new Dictionary<int, List<string>>()).Values.Where(x => x != null).SelectMany(x => x)
				.Where(x => !String.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);
		}

		private static List<int> GetDistinctIds(IEnumerable<int> primaryIds, IEnumerable<int> fallbackIds)
		{
			return (primaryIds ?? fallbackIds ?? Enumerable.Empty<int>()).Distinct().ToList();
		}

		private static DateTime GetReferenceDate(Call call)
		{
			if (call.LastDispatchedOn.HasValue)
				return call.LastDispatchedOn.Value;

			if (call.DispatchOn.HasValue)
				return call.DispatchOn.Value;

			if (call.LoggedOn != default(DateTime))
				return call.LoggedOn;

			return DateTime.UtcNow;
		}
	}
}
