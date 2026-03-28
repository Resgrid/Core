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
			if (string.IsNullOrWhiteSpace(config.CheckInTimerConfigId))
			{
				config.CreatedOn = DateTime.UtcNow;
			}
			else
			{
				var existing = await _configRepository.GetByIdAsync(config.CheckInTimerConfigId);
				if (existing != null && existing.DepartmentId != config.DepartmentId)
					throw new UnauthorizedAccessException("Cannot modify a timer config belonging to another department.");

				config.UpdatedOn = DateTime.UtcNow;
			}

			return await _configRepository.SaveOrUpdateAsync(config, cancellationToken);
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
			if (string.IsNullOrWhiteSpace(ovr.CheckInTimerOverrideId))
			{
				ovr.CreatedOn = DateTime.UtcNow;
			}
			else
			{
				var existing = await _overrideRepository.GetByIdAsync(ovr.CheckInTimerOverrideId);
				if (existing != null && existing.DepartmentId != ovr.DepartmentId)
					throw new UnauthorizedAccessException("Cannot modify a timer override belonging to another department.");

				ovr.UpdatedOn = DateTime.UtcNow;
			}

			return await _overrideRepository.SaveOrUpdateAsync(ovr, cancellationToken);
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

			// Parse call type as int for override matching
			int? callTypeId = null;
			if (!string.IsNullOrWhiteSpace(call.Type) && int.TryParse(call.Type, out int parsedType))
				callTypeId = parsedType;

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

			// Filter timers by ActiveForStates against current dispatched entity states
			resolvedTimers = await FilterTimersByActiveStatesAsync(resolvedTimers, call);
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
					matchingCheckIns = matchingCheckIns.Where(c => c.UnitId == timer.UnitTypeId);

				var latestCheckIn = matchingCheckIns
					.OrderByDescending(c => c.Timestamp)
					.FirstOrDefault();

				var baseTime = latestCheckIn?.Timestamp ?? call.LoggedOn;
				var elapsed = (now - baseTime).TotalMinutes;

				string status;
				if (elapsed < timer.DurationMinutes)
					status = "Green";
				else if (elapsed < timer.DurationMinutes + timer.WarningThresholdMinutes)
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

		private async Task<List<ResolvedCheckInTimer>> FilterTimersByActiveStatesAsync(List<ResolvedCheckInTimer> timers, Call call)
		{
			var timersWithStateFilter = timers.Where(t => !string.IsNullOrWhiteSpace(t.ActiveForStates)).ToList();
			if (!timersWithStateFilter.Any())
				return timers; // No filtering needed

			// Populate call dispatches if not already loaded
			if (call.Dispatches == null || call.UnitDispatches == null)
				call = await _callsService.PopulateCallData(call, true, false, false, false, true, false, false, false, false);

			// Build a set of current personnel action type IDs
			var personnelStates = new Dictionary<string, int>();
			if (call.Dispatches != null)
			{
				foreach (var dispatch in call.Dispatches)
				{
					var lastAction = await _actionLogsService.GetLastActionLogForUserAsync(dispatch.UserId);
					personnelStates[dispatch.UserId] = lastAction?.ActionTypeId ?? (int)ActionTypes.StandingBy;
				}
			}

			// Build a set of current unit state IDs (keyed by UnitId)
			var unitStates = new Dictionary<int, int>();
			if (call.UnitDispatches != null)
			{
				foreach (var unitDispatch in call.UnitDispatches)
				{
					var lastState = await _unitsService.GetLastUnitStateByUnitIdAsync(unitDispatch.UnitId);
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
					// Check unit states
					foreach (var kvp in unitStates)
					{
						// If timer is for a specific UnitType, we'd need to check the unit's type
						// For now, check all dispatched units
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
	}
}
