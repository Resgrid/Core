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

		public CheckInTimerService(
			ICheckInTimerConfigRepository configRepository,
			ICheckInTimerOverrideRepository overrideRepository,
			ICheckInRecordRepository recordRepository)
		{
			_configRepository = configRepository;
			_overrideRepository = overrideRepository;
			_recordRepository = recordRepository;
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
				config.CreatedOn = DateTime.UtcNow;
			else
				config.UpdatedOn = DateTime.UtcNow;

			return await _configRepository.SaveOrUpdateAsync(config, cancellationToken);
		}

		public async Task<bool> DeleteTimerConfigAsync(string configId, CancellationToken cancellationToken = default)
		{
			var config = await _configRepository.GetByIdAsync(configId);
			if (config == null)
				return false;

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
				ovr.CreatedOn = DateTime.UtcNow;
			else
				ovr.UpdatedOn = DateTime.UtcNow;

			return await _overrideRepository.SaveOrUpdateAsync(ovr, cancellationToken);
		}

		public async Task<bool> DeleteTimerOverrideAsync(string overrideId, CancellationToken cancellationToken = default)
		{
			var ovr = await _overrideRepository.GetByIdAsync(overrideId);
			if (ovr == null)
				return false;

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
				var key = $"{def.TimerTargetType}_{def.UnitTypeId}";
				resolved[key] = new ResolvedCheckInTimer
				{
					TargetType = def.TimerTargetType,
					UnitTypeId = def.UnitTypeId,
					DurationMinutes = def.DurationMinutes,
					WarningThresholdMinutes = def.WarningThresholdMinutes,
					IsFromOverride = false
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
				var key = $"{o.TimerTargetType}_{o.UnitTypeId}";

				// Only apply if this is the best scoring override for this key
				if (!resolved.ContainsKey(key) || !resolved[key].IsFromOverride)
				{
					resolved[key] = new ResolvedCheckInTimer
					{
						TargetType = o.TimerTargetType,
						UnitTypeId = o.UnitTypeId,
						DurationMinutes = o.DurationMinutes,
						WarningThresholdMinutes = o.WarningThresholdMinutes,
						IsFromOverride = true
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

			var checkIns = await _recordRepository.GetByCallIdAsync(call.CallId);
			var checkInList = checkIns?.ToList() ?? new List<CheckInRecord>();

			var statuses = new List<CheckInTimerStatus>();
			var now = DateTime.UtcNow;

			foreach (var timer in resolvedTimers)
			{
				// Find the latest check-in matching this timer target
				var relevantCheckIns = checkInList
					.Where(c => c.CheckInType == timer.TargetType)
					.OrderByDescending(c => c.Timestamp)
					.FirstOrDefault();

				var baseTime = relevantCheckIns?.Timestamp ?? call.LoggedOn;
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
					UnitId = timer.UnitTypeId.HasValue ? timer.UnitTypeId : null,
					LastCheckIn = relevantCheckIns?.Timestamp,
					DurationMinutes = timer.DurationMinutes,
					WarningThresholdMinutes = timer.WarningThresholdMinutes,
					ElapsedMinutes = Math.Round(elapsed, 1),
					Status = status
				});
			}

			return statuses;
		}

		#endregion Timer Status Computation
	}
}
