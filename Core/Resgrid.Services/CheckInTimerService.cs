using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class CheckInTimerService : ICheckInTimerService
	{
		private readonly ICheckInTimerConfigRepository _configRepository;
		private readonly ICheckInTimerOverrideRepository _overrideRepository;
		private readonly ICheckInRecordRepository _recordRepository;
		private readonly IActionLogsService _actionLogsService;
		private readonly IUnitsService _unitsService;
		private readonly ICallsService _callsService;

		public CheckInTimerService(
			ICheckInTimerConfigRepository configRepository,
			ICheckInTimerOverrideRepository overrideRepository,
			ICheckInRecordRepository recordRepository,
			IActionLogsService actionLogsService,
			IUnitsService unitsService,
			ICallsService callsService)
		{
			_configRepository = configRepository;
			_overrideRepository = overrideRepository;
			_recordRepository = recordRepository;
			_actionLogsService = actionLogsService;
			_unitsService = unitsService;
			_callsService = callsService;
		}
		#region Configuration CRUD

		public async Task<List<CheckInTimerConfig>> GetTimerConfigsForDepartmentAsync(int departmentId)
		{
			var configs = await _configRepository.GetByDepartmentIdAsync(departmentId);
			return configs?.ToList() ?? new List<CheckInTimerConfig>();
		}

		public async Task<CheckInTimerConfig> SaveTimerConfigAsync(CheckInTimerConfig config, CancellationToken cancellationToken = default)
		{
			ValidateTimerValues(config.TimerTargetType, config.DurationMinutes, config.WarningThresholdMinutes);

			// Unit type only applies to unit-type timers; clear it for other targets so it
			// can't create per-unit-type rows the resolver and unique-target lookup would mismatch
			if (config.TimerTargetType != (int)CheckInTimerTargetType.UnitType)
				config.UnitTypeId = null;

			// Only one config may exist per (department, target type, unit type) —
			// enforced by the UQ_CheckInTimerConfigs_Dept_Target_Unit unique index
			var existingForTarget = await _configRepository.GetByDepartmentAndTargetAsync(
				config.DepartmentId, config.TimerTargetType, config.UnitTypeId);

			if (string.IsNullOrWhiteSpace(config.CheckInTimerConfigId))
			{
				if (existingForTarget != null)
				{
					// Saving a "new" config for a target that already has one — update the
					// existing row instead of inserting a duplicate
					config.CheckInTimerConfigId = existingForTarget.CheckInTimerConfigId;
					config.CreatedOn = existingForTarget.CreatedOn;
					config.CreatedByUserId = existingForTarget.CreatedByUserId;
					config.UpdatedOn = DateTime.UtcNow;
				}
				else
				{
					config.CreatedOn = DateTime.UtcNow;
				}
			}
			else
			{
				var existing = await _configRepository.GetByIdAsync(config.CheckInTimerConfigId);
				if (existing != null && existing.DepartmentId != config.DepartmentId)
					throw new UnauthorizedAccessException("Cannot modify a timer config belonging to another department.");

				if (existingForTarget != null && existingForTarget.CheckInTimerConfigId != config.CheckInTimerConfigId)
					throw new InvalidOperationException("A check-in timer configuration already exists for this target type and unit type.");

				if (existing != null)
					config.CreatedOn = existing.CreatedOn;

				config.UpdatedOn = DateTime.UtcNow;
			}

			var isInsert = string.IsNullOrWhiteSpace(config.CheckInTimerConfigId);

			try
			{
				return await _configRepository.SaveOrUpdateAsync(config, cancellationToken);
			}
			catch (Exception) when (isInsert)
			{
				// Two concurrent saves can both pass the lookup above and race the unique
				// target index; the loser lands here — adopt the winner's row and update it
				var winner = await _configRepository.GetByDepartmentAndTargetAsync(
					config.DepartmentId, config.TimerTargetType, config.UnitTypeId);
				if (winner == null)
					throw;

				config.CheckInTimerConfigId = winner.CheckInTimerConfigId;
				config.CreatedOn = winner.CreatedOn;
				config.CreatedByUserId = winner.CreatedByUserId;
				config.UpdatedOn = DateTime.UtcNow;
				return await _configRepository.SaveOrUpdateAsync(config, cancellationToken);
			}
		}

		public async Task<bool> DeleteTimerConfigAsync(string configId, int departmentId, CancellationToken cancellationToken = default)
		{
			var config = await _configRepository.GetByIdAsync(configId);
			if (config == null)
				return false;

			if (config.DepartmentId != departmentId)
				throw new UnauthorizedAccessException("Cannot delete a timer config belonging to another department.");

			return await _configRepository.DeleteAsync(config, cancellationToken);
		}

		#endregion Configuration CRUD

		#region Override CRUD

		public async Task<List<CheckInTimerOverride>> GetTimerOverridesForDepartmentAsync(int departmentId)
		{
			var overrides = await _overrideRepository.GetByDepartmentIdAsync(departmentId);
			return overrides?.ToList() ?? new List<CheckInTimerOverride>();
		}

		public async Task<CheckInTimerOverride> SaveTimerOverrideAsync(CheckInTimerOverride ovr, CancellationToken cancellationToken = default)
		{
			ValidateTimerValues(ovr.TimerTargetType, ovr.DurationMinutes, ovr.WarningThresholdMinutes);

			// Unit type only applies to unit-type timers; clear it for other targets so it
			// can't create per-unit-type rows the resolver and unique-target lookup would mismatch
			if (ovr.TimerTargetType != (int)CheckInTimerTargetType.UnitType)
				ovr.UnitTypeId = null;

			// Only one override may exist per (department, call type, call priority, target type,
			// unit type) — enforced by the UQ_CheckInTimerOverrides_Dept_Call_Target_Unit unique index
			var departmentOverrides = await _overrideRepository.GetByDepartmentIdAsync(ovr.DepartmentId);
			var existingForTarget = departmentOverrides?.FirstOrDefault(o =>
				o.CallTypeId == ovr.CallTypeId &&
				o.CallPriority == ovr.CallPriority &&
				o.TimerTargetType == ovr.TimerTargetType &&
				o.UnitTypeId == ovr.UnitTypeId);

			if (string.IsNullOrWhiteSpace(ovr.CheckInTimerOverrideId))
			{
				if (existingForTarget != null)
				{
					// Saving a "new" override for a target that already has one — update the
					// existing row instead of inserting a duplicate
					ovr.CheckInTimerOverrideId = existingForTarget.CheckInTimerOverrideId;
					ovr.CreatedOn = existingForTarget.CreatedOn;
					ovr.CreatedByUserId = existingForTarget.CreatedByUserId;
					ovr.UpdatedOn = DateTime.UtcNow;
				}
				else
				{
					ovr.CreatedOn = DateTime.UtcNow;
				}
			}
			else
			{
				var existing = await _overrideRepository.GetByIdAsync(ovr.CheckInTimerOverrideId);
				if (existing != null && existing.DepartmentId != ovr.DepartmentId)
					throw new UnauthorizedAccessException("Cannot modify a timer override belonging to another department.");

				if (existingForTarget != null && existingForTarget.CheckInTimerOverrideId != ovr.CheckInTimerOverrideId)
					throw new InvalidOperationException("A check-in timer override already exists for this call type, priority, target type and unit type.");

				if (existing != null)
					ovr.CreatedOn = existing.CreatedOn;

				ovr.UpdatedOn = DateTime.UtcNow;
			}

			var isInsert = string.IsNullOrWhiteSpace(ovr.CheckInTimerOverrideId);

			try
			{
				return await _overrideRepository.SaveOrUpdateAsync(ovr, cancellationToken);
			}
			catch (Exception) when (isInsert)
			{
				// Two concurrent saves can both pass the lookup above and race the unique
				// target index; the loser lands here — adopt the winner's row and update it
				var currentOverrides = await _overrideRepository.GetByDepartmentIdAsync(ovr.DepartmentId);
				var winner = currentOverrides?.FirstOrDefault(o =>
					o.CallTypeId == ovr.CallTypeId &&
					o.CallPriority == ovr.CallPriority &&
					o.TimerTargetType == ovr.TimerTargetType &&
					o.UnitTypeId == ovr.UnitTypeId);
				if (winner == null)
					throw;

				ovr.CheckInTimerOverrideId = winner.CheckInTimerOverrideId;
				ovr.CreatedOn = winner.CreatedOn;
				ovr.CreatedByUserId = winner.CreatedByUserId;
				ovr.UpdatedOn = DateTime.UtcNow;
				return await _overrideRepository.SaveOrUpdateAsync(ovr, cancellationToken);
			}
		}

		public async Task<bool> DeleteTimerOverrideAsync(string overrideId, int departmentId, CancellationToken cancellationToken = default)
		{
			var ovr = await _overrideRepository.GetByIdAsync(overrideId);
			if (ovr == null)
				return false;

			if (ovr.DepartmentId != departmentId)
				throw new UnauthorizedAccessException("Cannot delete a timer override belonging to another department.");

			return await _overrideRepository.DeleteAsync(ovr, cancellationToken);
		}

		#endregion Override CRUD

		#region Timer Resolution

		public async Task<List<ResolvedCheckInTimer>> ResolveAllTimersForCallAsync(Call call)
		{
			if (call == null || !call.CheckInTimersEnabled)
				return new List<ResolvedCheckInTimer>();

			var defaults = await _configRepository.GetByDepartmentIdAsync(call.DepartmentId);
			var defaultList = defaults?.Where(c => c.IsEnabled).ToList() ?? new List<CheckInTimerConfig>();

			// Resolve the call's type to a CallTypeId for override matching. Call.Type stores
			// the type NAME (see call creation in CallsController), so a numeric parse only
			// covers legacy data — otherwise look the id up from the department's call types.
			int? callTypeId = null;
			if (!string.IsNullOrWhiteSpace(call.Type))
			{
				if (int.TryParse(call.Type, out int parsedType))
				{
					callTypeId = parsedType;
				}
				else
				{
					var callTypes = await _callsService.GetCallTypesForDepartmentAsync(call.DepartmentId);
					callTypeId = callTypes?.FirstOrDefault(t => string.Equals(t.Type, call.Type, StringComparison.OrdinalIgnoreCase))?.CallTypeId;
				}
			}

			var overrides = await _overrideRepository.GetMatchingOverridesAsync(call.DepartmentId, callTypeId, call.Priority);
			var overrideList = overrides?.ToList() ?? new List<CheckInTimerOverride>();

			var resolved = new Dictionary<string, ResolvedCheckInTimer>();

			// First, populate from defaults
			foreach (var def in defaultList)
			{
				var targetId = def.UnitTypeId?.ToString();
				var key = $"{def.TimerTargetType}_{def.UnitTypeId}_{targetId}";
				resolved[key] = new ResolvedCheckInTimer
				{
					TargetType = def.TimerTargetType,
					UnitTypeId = def.UnitTypeId,
					TargetEntityId = targetId,
					DurationMinutes = def.DurationMinutes,
					WarningThresholdMinutes = def.WarningThresholdMinutes,
					IsFromOverride = false,
					ActiveForStates = def.ActiveForStates
				};
			}

			// Then, apply overrides with scoring: type+priority=3, type-only=2, priority-only=1
			var scoredOverrides = overrideList
				.Select(o => new
				{
					Override = o,
					Score = (o.CallTypeId.HasValue && o.CallPriority.HasValue) ? 3
						: o.CallTypeId.HasValue ? 2
						: o.CallPriority.HasValue ? 1
						: 0
				})
				.OrderByDescending(x => x.Score)
				.ToList();

			foreach (var scored in scoredOverrides)
			{
				var o = scored.Override;
				if (!o.IsEnabled)
					continue;

				var targetId = o.UnitTypeId?.ToString();
				var key = $"{o.TimerTargetType}_{o.UnitTypeId}_{targetId}";

				// Only apply if this is the best scoring override for this key
				if (!resolved.ContainsKey(key) || !resolved[key].IsFromOverride)
				{
					resolved[key] = new ResolvedCheckInTimer
					{
						TargetType = o.TimerTargetType,
						UnitTypeId = o.UnitTypeId,
						TargetEntityId = targetId,
						DurationMinutes = o.DurationMinutes,
						WarningThresholdMinutes = o.WarningThresholdMinutes,
						IsFromOverride = true,
						ActiveForStates = o.ActiveForStates
					};
				}
			}

			return resolved.Values.ToList();
		}

		#endregion Timer Resolution

		#region Check-in Operations

		public async Task<CheckInRecord> PerformCheckInAsync(CheckInRecord record, CancellationToken cancellationToken = default)
		{
			record.Timestamp = DateTime.UtcNow;
			return await _recordRepository.SaveOrUpdateAsync(record, cancellationToken);
		}

		public async Task<List<CheckInRecord>> GetCheckInsForCallAsync(int callId)
		{
			var records = await _recordRepository.GetByCallIdAsync(callId);
			return records?.ToList() ?? new List<CheckInRecord>();
		}

		public async Task<CheckInRecord> GetLastCheckInAsync(int callId, string userId, int? unitId)
		{
			if (unitId.HasValue)
				return await _recordRepository.GetLastCheckInForUnitOnCallAsync(callId, unitId.Value);

			return await _recordRepository.GetLastCheckInForUserOnCallAsync(callId, userId);
		}

		#endregion Check-in Operations

		#region Timer Status Computation

		public async Task<List<CheckInTimerStatus>> GetActiveTimerStatusesForCallAsync(Call call)
		{
			if (call == null || !call.CheckInTimersEnabled || call.State != (int)CallStates.Active)
				return new List<CheckInTimerStatus>();

			var resolvedTimers = await ResolveAllTimersForCallAsync(call);
			if (!resolvedTimers.Any())
				return new List<CheckInTimerStatus>();

			// Unit-type-scoped timers match check-ins and unit states by the unit's TYPE, but
			// check-ins and dispatches carry UnitIds. Unit.Type stores the type name, so build
			// a UnitId -> UnitTypeId map once when any timer is unit-type-scoped.
			Dictionary<int, int?> unitTypeIdByUnitId = null;
			if (resolvedTimers.Any(t => t.UnitTypeId.HasValue))
				unitTypeIdByUnitId = await GetUnitTypeIdsByUnitIdAsync(call.DepartmentId);

			// Filter timers by ActiveForStates against current dispatched entity states
			resolvedTimers = await FilterTimersByActiveStatesAsync(resolvedTimers, call, unitTypeIdByUnitId);
			if (!resolvedTimers.Any())
				return new List<CheckInTimerStatus>();

			var checkIns = await _recordRepository.GetByCallIdAsync(call.CallId);
			var checkInList = checkIns?.ToList() ?? new List<CheckInRecord>();

			var statuses = new List<CheckInTimerStatus>();
			var now = DateTime.UtcNow;

			foreach (var timer in resolvedTimers)
			{
				// Find the latest check-in matching this timer's type and concrete target
				var matchingCheckIns = checkInList
					.Where(c => c.CheckInType == timer.TargetType);

				if (timer.UnitTypeId.HasValue)
					matchingCheckIns = matchingCheckIns.Where(c =>
						c.UnitId.HasValue &&
						unitTypeIdByUnitId != null &&
						unitTypeIdByUnitId.TryGetValue(c.UnitId.Value, out var checkInUnitTypeId) &&
						checkInUnitTypeId == timer.UnitTypeId);

				var latestCheckIn = matchingCheckIns
					.OrderByDescending(c => c.Timestamp)
					.FirstOrDefault();

				var baseTime = latestCheckIn?.Timestamp ?? call.LoggedOn;
				var elapsed = (now - baseTime).TotalMinutes;
				var minutesRemaining = timer.DurationMinutes - elapsed;

				// Same semantics as the per-user and per-personnel endpoints: warn when within
				// the threshold of the deadline, critical once the check-in is due
				string status;
				if (minutesRemaining > timer.WarningThresholdMinutes)
					status = "Green";
				else if (minutesRemaining > 0)
					status = "Warning";
				else
					status = "Critical";

				statuses.Add(new CheckInTimerStatus
				{
					TargetType = timer.TargetType,
					TargetEntityId = timer.TargetEntityId,
					TargetName = timer.TargetName ?? ((CheckInTimerTargetType)timer.TargetType).ToString(),
					UnitId = latestCheckIn?.UnitId,
					LastCheckIn = latestCheckIn?.Timestamp,
					DurationMinutes = timer.DurationMinutes,
					WarningThresholdMinutes = timer.WarningThresholdMinutes,
					ElapsedMinutes = Math.Round(elapsed, 1),
					Status = status
				});
			}

			return statuses;
		}

		#endregion Timer Status Computation

		#region State Filtering

		private async Task<List<ResolvedCheckInTimer>> FilterTimersByActiveStatesAsync(List<ResolvedCheckInTimer> timers, Call call, Dictionary<int, int?> unitTypeIdByUnitId)
		{
			var timersWithStateFilter = timers.Where(t => !string.IsNullOrWhiteSpace(t.ActiveForStates)).ToList();
			if (!timersWithStateFilter.Any())
				return timers; // No filtering needed

			// Populate call dispatches if not already loaded
			if (call.Dispatches == null || call.UnitDispatches == null)
				call = await _callsService.PopulateCallData(call, true, false, false, false, true, false, false, false, false);

			// Build a set of current personnel action type IDs (one batch query instead of
			// one last-action query per dispatched user)
			var personnelStates = new Dictionary<string, int>();
			if (call.Dispatches != null && call.Dispatches.Any())
			{
				var lastActionLogs = await _actionLogsService.GetLastActionLogsForDepartmentAsync(call.DepartmentId);
				var lastActionByUser = (lastActionLogs ?? new List<ActionLog>())
					.GroupBy(a => a.UserId)
					.ToDictionary(g => g.Key, g => g.First());

				foreach (var dispatch in call.Dispatches)
				{
					lastActionByUser.TryGetValue(dispatch.UserId, out var lastAction);
					personnelStates[dispatch.UserId] = lastAction?.ActionTypeId ?? (int)ActionTypes.StandingBy;
				}
			}

			// Build a set of current unit state IDs keyed by UnitId (one batch query instead
			// of one last-state query per dispatched unit)
			var unitStates = new Dictionary<int, int>();
			if (call.UnitDispatches != null && call.UnitDispatches.Any())
			{
				var latestUnitStates = await _unitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync(call.DepartmentId);
				var lastStateByUnit = (latestUnitStates ?? new List<UnitState>())
					.GroupBy(s => s.UnitId)
					.ToDictionary(g => g.Key, g => g.First());

				foreach (var unitDispatch in call.UnitDispatches)
				{
					lastStateByUnit.TryGetValue(unitDispatch.UnitId, out var lastState);
					unitStates[unitDispatch.UnitId] = lastState?.State ?? (int)UnitStateTypes.Available;
				}
			}

			var result = new List<ResolvedCheckInTimer>();
			foreach (var timer in timers)
			{
				if (string.IsNullOrWhiteSpace(timer.ActiveForStates))
				{
					result.Add(timer); // No filter = always active
					continue;
				}

				var allowedStates = ParseActiveForStates(timer.ActiveForStates);

				bool anyEntityMatches = false;

				if (timer.TargetType == (int)CheckInTimerTargetType.UnitType)
				{
					// Check unit states; a timer scoped to a specific unit type only considers
					// dispatched units of that type
					foreach (var kvp in unitStates)
					{
						if (timer.UnitTypeId.HasValue &&
							(unitTypeIdByUnitId == null ||
							 !unitTypeIdByUnitId.TryGetValue(kvp.Key, out var dispatchedUnitTypeId) ||
							 dispatchedUnitTypeId != timer.UnitTypeId))
							continue;

						if (allowedStates.Contains(kvp.Value))
						{
							anyEntityMatches = true;
							break;
						}
					}
				}
				else
				{
					// Personnel-based timers (Personnel, IC, PAR, Hazmat, SectorRotation, Rehab)
					foreach (var kvp in personnelStates)
					{
						if (allowedStates.Contains(kvp.Value))
						{
							anyEntityMatches = true;
							break;
						}
					}
				}

				if (anyEntityMatches)
					result.Add(timer);
			}

			return result;
		}

		/// <summary>
		/// Maps every unit in the department to its UnitTypeId. Unit.Type stores the unit
		/// type NAME, so the id is resolved against the department's unit types; units with
		/// no (or an unknown) type map to null.
		/// </summary>
		private async Task<Dictionary<int, int?>> GetUnitTypeIdsByUnitIdAsync(int departmentId)
		{
			var units = await _unitsService.GetUnitsForDepartmentAsync(departmentId) ?? new List<Unit>();
			var unitTypes = await _unitsService.GetUnitTypesForDepartmentAsync(departmentId) ?? new List<UnitType>();

			var typeIdByName = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
			foreach (var unitType in unitTypes)
			{
				if (!string.IsNullOrWhiteSpace(unitType.Type) && !typeIdByName.ContainsKey(unitType.Type))
					typeIdByName.Add(unitType.Type, unitType.UnitTypeId);
			}

			var map = new Dictionary<int, int?>();
			foreach (var unit in units)
			{
				int? unitTypeId = null;
				if (!string.IsNullOrWhiteSpace(unit.Type) && typeIdByName.TryGetValue(unit.Type, out var resolved))
					unitTypeId = resolved;

				map[unit.UnitId] = unitTypeId;
			}

			return map;
		}

		private static void ValidateTimerValues(int timerTargetType, int durationMinutes, int warningThresholdMinutes)
		{
			if (!Enum.IsDefined(typeof(CheckInTimerTargetType), timerTargetType))
				throw new InvalidOperationException("Invalid check-in timer target type.");

			if (durationMinutes < 1)
				throw new InvalidOperationException("Check-in timer duration must be at least 1 minute.");

			if (warningThresholdMinutes < 1)
				throw new InvalidOperationException("Check-in timer warning threshold must be at least 1 minute.");

			if (warningThresholdMinutes >= durationMinutes)
				throw new InvalidOperationException("Check-in timer warning threshold must be less than the duration.");
		}

		private static HashSet<int> ParseActiveForStates(string activeForStates)
		{
			var states = new HashSet<int>();
			if (string.IsNullOrWhiteSpace(activeForStates))
				return states;

			foreach (var part in activeForStates.Split(',', StringSplitOptions.RemoveEmptyEntries))
			{
				if (int.TryParse(part.Trim(), out int stateId))
					states.Add(stateId);
			}

			return states;
		}

		#endregion State Filtering

		#region Per-User Active Call Check-In Summaries (Endpoint 1)

		/// <inheritdoc/>
		public async Task<List<UserCallCheckInSummary>> GetUserActiveCallCheckInSummariesAsync(string userId, int departmentId)
		{
			if (string.IsNullOrWhiteSpace(userId))
				return new List<UserCallCheckInSummary>();

			// Single optimised query: joins Calls + CallDispatches, filtered by
			// user, department, CheckInTimersEnabled=true, State=Active (0).
			var activeCalls = await _callsService.GetActiveCallsWithCheckInTimersForUserAsync(userId, departmentId);
			if (activeCalls == null || !activeCalls.Any())
				return new List<UserCallCheckInSummary>();

			var summaries = new List<UserCallCheckInSummary>();
			var now = DateTime.UtcNow;

			foreach (var call in activeCalls)
			{
				// Resolve the configured timers for this call (handles overrides).
				var resolvedTimers = await ResolveAllTimersForCallAsync(call);

				// Find the first personnel-type timer (TargetType == Personnel = 0).
				var personnelTimer = resolvedTimers
					.FirstOrDefault(t => t.TargetType == (int)CheckInTimerTargetType.Personnel);

				if (personnelTimer == null)
				{
					// No personnel timer active for this call – include the call but
					// mark it clearly so the client can inform the user.
					summaries.Add(new UserCallCheckInSummary
					{
						CallId = call.CallId,
						CallName = call.Name,
						CallNumber = call.Number,
						CallStartedOn = call.LoggedOn,
						HasPersonnelTimer = false,
						Status = "NoTimer"
					});
					continue;
				}

				// Only personnel-type check-ins reset the personnel timer — same semantics as
				// GetCallPersonnelCheckInStatusesAsync. The raw last-check-in query is
				// type-agnostic, so filter the call's records here.
				var callCheckIns = (await _recordRepository.GetByCallIdAsync(call.CallId))?.ToList()
								   ?? new List<CheckInRecord>();
				var lastCheckIn = callCheckIns
					.Where(r => r.CheckInType == (int)CheckInTimerTargetType.Personnel && r.UserId == userId)
					.OrderByDescending(r => r.Timestamp)
					.FirstOrDefault();

				// Baseline is the last check-in timestamp OR the call start time if
				// the user has never checked in.
				var baseTime = lastCheckIn?.Timestamp ?? call.LoggedOn;
				var elapsed = (now - baseTime).TotalMinutes;
				var minutesRemaining = personnelTimer.DurationMinutes - elapsed;

				string status;
				if (minutesRemaining > personnelTimer.WarningThresholdMinutes)
					status = "Green";
				else if (minutesRemaining > 0)
					status = "Warning";
				else
					status = "Critical";

				summaries.Add(new UserCallCheckInSummary
				{
					CallId = call.CallId,
					CallName = call.Name,
					CallNumber = call.Number,
					CallStartedOn = call.LoggedOn,
					HasPersonnelTimer = true,
					DurationMinutes = personnelTimer.DurationMinutes,
					WarningThresholdMinutes = personnelTimer.WarningThresholdMinutes,
					LastCheckIn = lastCheckIn?.Timestamp,
					NeedsCheckIn = minutesRemaining <= 0,
					MinutesRemaining = Math.Round(minutesRemaining, 1),
					Status = status
				});
			}

			return summaries;
		}

		#endregion Per-User Active Call Check-In Summaries

		#region Call Personnel Check-In Statuses (Endpoint 2)

		/// <inheritdoc/>
		public async Task<List<PersonnelCallCheckInStatus>> GetCallPersonnelCheckInStatusesAsync(Call call)
		{
			if (call == null || !call.CheckInTimersEnabled || call.State != (int)CallStates.Active)
				return new List<PersonnelCallCheckInStatus>();

			// Resolve the personnel timer for this call.
			var resolvedTimers = await ResolveAllTimersForCallAsync(call);
			var personnelTimer = resolvedTimers
				.FirstOrDefault(t => t.TargetType == (int)CheckInTimerTargetType.Personnel);

			// If no personnel timer is configured there's nothing to report.
			if (personnelTimer == null)
				return new List<PersonnelCallCheckInStatus>();

			// Load dispatched personnel (single query).
			if (call.Dispatches == null)
				call = await _callsService.PopulateCallData(call, true, false, false, false, false, false, false, false, false);

			var dispatches = call.Dispatches?.ToList() ?? new List<CallDispatch>();
			if (!dispatches.Any())
				return new List<PersonnelCallCheckInStatus>();

			// Load ALL check-in records for the call at once (single query), then
			// keep only the most-recent personnel-type record per user in memory.
			// This avoids N separate "last check-in" queries.
			var allCheckIns = (await _recordRepository.GetByCallIdAsync(call.CallId))?.ToList()
							  ?? new List<CheckInRecord>();

			var latestByUser = allCheckIns
				.Where(r => r.CheckInType == (int)CheckInTimerTargetType.Personnel)
				.GroupBy(r => r.UserId)
				.ToDictionary(
					g => g.Key,
					g => g.OrderByDescending(r => r.Timestamp).First());

			var now = DateTime.UtcNow;
			var statuses = new List<PersonnelCallCheckInStatus>();

			foreach (var dispatch in dispatches)
			{
				latestByUser.TryGetValue(dispatch.UserId, out var lastRecord);

				var baseTime = lastRecord?.Timestamp ?? call.LoggedOn;
				var elapsed = (now - baseTime).TotalMinutes;
				var minutesRemaining = personnelTimer.DurationMinutes - elapsed;

				string status;
				if (minutesRemaining > personnelTimer.WarningThresholdMinutes)
					status = "Green";
				else if (minutesRemaining > 0)
					status = "Warning";
				else
					status = "Critical";

				statuses.Add(new PersonnelCallCheckInStatus
				{
					UserId = dispatch.UserId,
					// FullName is intentionally left null here; the controller enriches
					// it from IUserProfileService to avoid pulling that dependency into
					// this service.
					FullName = null,
					LastCheckIn = lastRecord?.Timestamp,
					NeedsCheckIn = minutesRemaining <= 0,
					MinutesRemaining = Math.Round(minutesRemaining, 1),
					Status = status,
					DurationMinutes = personnelTimer.DurationMinutes,
					WarningThresholdMinutes = personnelTimer.WarningThresholdMinutes
				});
			}

			return statuses;
		}

		#endregion Call Personnel Check-In Statuses
	}
}
