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

			if (statusToSet < 0)
				statusToSet = isDispatch ? (int)ActionTypes.RespondingToScene : (int)ActionTypes.StandingBy;

			foreach (var userId in shiftUserIds)
			{
				await _actionLogsService.SetUserActionAsync(userId, call.DepartmentId, statusToSet, null, call.CallId, (int)DestinationEntityTypes.Call, cancellationToken);
			}
		}

		private async Task ApplyUnitStatusesAsync(Call call, Department department, IReadOnlyCollection<int> unitIds, bool isDispatch, CancellationToken cancellationToken)
		{
			var defaultStatusToSet = isDispatch
				? await _departmentSettingsService.GetUnitCallDispatchStatusToSetAsync(call.DepartmentId)
				: await _departmentSettingsService.GetUnitCallReleaseStatusToSetAsync(call.DepartmentId);

			if (defaultStatusToSet < 0)
				defaultStatusToSet = isDispatch ? (int)UnitStateTypes.Responding : (int)UnitStateTypes.Released;

			if (!isDispatch)
			{
				unitIds = await GetUnitsToReleaseAsync(call, unitIds);

				if (!unitIds.Any())
					return;
			}

			var resolvedStatuses = await ResolveUnitStatusesAsync(call.DepartmentId, unitIds, defaultStatusToSet, isDispatch);

			var timestamp = DateTime.UtcNow;
			var localTimestamp = department != null ? DateTimeHelpers.GetLocalDateTime(timestamp, department.TimeZone) : timestamp;

			foreach (var unitId in unitIds)
			{
				var statusToSet = resolvedStatuses.TryGetValue(unitId, out var resolvedStatus) ? resolvedStatus : defaultStatusToSet;

				var state = new UnitState
				{
					UnitId = unitId,
					State = statusToSet,
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

		private async Task<Dictionary<int, int>> ResolveUnitStatusesAsync(int departmentId, IReadOnlyCollection<int> unitIds,
			int defaultStatusToSet, bool isDispatch)
		{
			var resolvedStatuses = unitIds.ToDictionary(x => x, _ => defaultStatusToSet);
			var unitTypeOverrides = await _departmentSettingsService.GetUnitCallStatusOverridesByUnitTypeAsync(departmentId);

			if (unitTypeOverrides == null || !unitTypeOverrides.Any())
				return resolvedStatuses;

			var unitTypeOverrideLookup = unitTypeOverrides
				.Where(x => x != null && x.UnitTypeId > 0)
				.GroupBy(x => x.UnitTypeId)
				.ToDictionary(x => x.Key, x => x.Last());

			if (!unitTypeOverrideLookup.Any())
				return resolvedStatuses;

			var units = (await Task.WhenAll(unitIds.Select(x => _unitsService.GetUnitByIdAsync(x))))
				.Where(x => x != null)
				.ToList();

			if (!units.Any())
				return resolvedStatuses;

			var unitTypesByName = new Dictionary<string, UnitType>(StringComparer.OrdinalIgnoreCase);

			foreach (var unitTypeName in units
				.Select(x => x.Type)
				.Where(x => !String.IsNullOrWhiteSpace(x))
				.Distinct(StringComparer.OrdinalIgnoreCase))
			{
				var unitType = await _unitsService.GetUnitTypeByNameAsync(departmentId, unitTypeName);

				if (unitType != null)
					unitTypesByName[unitTypeName] = unitType;
			}

			var customStateDetailIdsByStateId = new Dictionary<int, HashSet<int>>();
			var customStateIds = unitTypesByName.Values
				.Where(x => x.CustomStatesId.HasValue && x.CustomStatesId.Value > 0 && unitTypeOverrideLookup.ContainsKey(x.UnitTypeId))
				.Select(x => x.CustomStatesId.Value)
				.Distinct()
				.ToList();

			foreach (var customStateId in customStateIds)
			{
				var customState = await _customStateService.GetCustomSateByIdAsync(customStateId);
				customStateDetailIdsByStateId[customStateId] = customState != null && !customState.IsDeleted
					? new HashSet<int>(customState.GetActiveDetails().Select(x => x.CustomStateDetailId))
					: new HashSet<int>();
			}

			foreach (var unit in units)
			{
				if (String.IsNullOrWhiteSpace(unit.Type))
					continue;

				if (!unitTypesByName.TryGetValue(unit.Type, out var unitType))
					continue;

				if (!unitTypeOverrideLookup.TryGetValue(unitType.UnitTypeId, out var unitTypeOverride))
					continue;

				var candidateStatus = isDispatch ? unitTypeOverride.DispatchStatus : unitTypeOverride.ReleaseStatus;

				if (candidateStatus < 0 || !unitType.CustomStatesId.HasValue || unitType.CustomStatesId.Value <= 0)
					continue;

				if (customStateDetailIdsByStateId.TryGetValue(unitType.CustomStatesId.Value, out var validStateIds) &&
					validStateIds.Contains(candidateStatus))
					resolvedStatuses[unit.UnitId] = candidateStatus;
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
