using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Re-notifies dispatched personnel and units until they acknowledge a call (Dispatch Settings, off by default). The call
	/// broadcast registers each person and unit it reached; the sweep (worker 78, every minute) then, for every row that is due:
	/// <list type="bullet">
	/// <item>cancels it when the call is closed or deleted, the recipient's dispatch has been taken off the call, the department
	/// turned re-notification off, or the row is far past its window (a stopped worker does not send stale reminders);</item>
	/// <item>finishes it as acknowledged when the recipient has set any status since the dispatch: a person any action, a unit any
	/// unit status, with or without a destination. Statuses Resgrid applies itself (dispatch automation, schedules, maintenance) do
	/// not count;</item>
	/// <item>otherwise claims the next attempt and re-sends the dispatch on the department's channels (push today), finishing the
	/// row as exhausted on the last one.</item>
	/// </list>
	/// </summary>
	public class DispatchRenotificationService : IDispatchRenotificationService
	{
		/// <summary>Allowance for a client clock running ahead when it stamps a status.</summary>
		private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

		/// <summary>How far past its last scheduled attempt a row may be before the sweep drops it instead of sending.</summary>
		private static readonly TimeSpan StaleGrace = TimeSpan.FromMinutes(5);

		private readonly IDispatchRenotificationsRepository _dispatchRenotificationsRepository;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly ICallsService _callsService;
		private readonly ICommunicationService _communicationService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IUnitStatesRepository _unitStatesRepository;

		public DispatchRenotificationService(IDispatchRenotificationsRepository dispatchRenotificationsRepository, IDepartmentSettingsService departmentSettingsService,
			IDepartmentsService departmentsService, ICallsService callsService, ICommunicationService communicationService, IActionLogsService actionLogsService,
			IUnitStatesRepository unitStatesRepository)
		{
			_dispatchRenotificationsRepository = dispatchRenotificationsRepository;
			_departmentSettingsService = departmentSettingsService;
			_departmentsService = departmentsService;
			_callsService = callsService;
			_communicationService = communicationService;
			_actionLogsService = actionLogsService;
			_unitStatesRepository = unitStatesRepository;
		}

		public async Task RegisterBroadcastAsync(Call call, IEnumerable<DispatchRenotifyRecipient> recipients, DateTime dispatchedOnUtc,
			CancellationToken cancellationToken = default)
		{
			if (call == null || recipients == null || !Config.DispatchConfig.RenotifyEnabled)
				return;

			if (call.IsDeleted || call.State != (int)CallStates.Active)
				return;

			var unique = recipients
				.Where(r => r != null && !string.IsNullOrWhiteSpace(r.RecipientId))
				.GroupBy(r => (r.RecipientType, Key: r.RecipientId.ToUpperInvariant()))
				.Select(g => g.First())
				.ToList();

			if (unique.Count == 0)
				return;

			var config = await _departmentSettingsService.GetDispatchRenotifyConfigAsync(call.DepartmentId);
			if (config == null || !config.Enabled)
				return;

			var now = DateTime.UtcNow;
			var acknowledgeAfter = dispatchedOnUtc == default || dispatchedOnUtc > now ? now : dispatchedOnUtc;
			var nextNotifyOn = now.AddMinutes(config.GetEffectiveIntervalMinutes(Config.DispatchConfig.RenotifyTimingLocked));

			await _dispatchRenotificationsRepository.RegisterAsync(unique.Select(r => new DispatchRenotification
			{
				DispatchRenotificationId = Guid.NewGuid().ToString(),
				DepartmentId = call.DepartmentId,
				CallId = call.CallId,
				RecipientType = (int)r.RecipientType,
				RecipientId = r.RecipientId,
				SourceType = (int)r.SourceType,
				SourceId = r.SourceId,
				Status = (int)DispatchRenotifyStatuses.Pending,
				Attempts = 0,
				AcknowledgeAfter = acknowledgeAfter,
				NextNotifyOn = nextNotifyOn,
				CreatedOn = now
			}).ToList(), cancellationToken);
		}

		public async Task<DispatchRenotifySweepResult> ProcessDueAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
		{
			var result = new DispatchRenotifySweepResult();
			var batchSize = Math.Max(1, Config.DispatchConfig.RenotifySweepBatchSize);

			var due = await _dispatchRenotificationsRepository.GetDueAsync(nowUtc, batchSize, cancellationToken);
			result.Due = due.Count;

			foreach (var callRows in due.GroupBy(r => r.CallId))
			{
				if (cancellationToken.IsCancellationRequested)
					break;

				try
				{
					await ProcessCallAsync(callRows.Key, callRows.ToList(), nowUtc, result, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					break;
				}
				catch (Exception ex)
				{
					// One call's failure never holds up the others; its rows stay due and are retried next minute.
					Logging.LogException(ex, $"Dispatch re-notification sweep failed for call {callRows.Key}.");
				}
			}

			var retentionDays = Math.Max(1, Config.DispatchConfig.RenotifyRetentionDays);
			result.Purged = await _dispatchRenotificationsRepository.PurgeCompletedAsync(nowUtc.AddDays(-retentionDays), batchSize, cancellationToken);

			return result;
		}

		private async Task ProcessCallAsync(int callId, List<DispatchRenotification> rows, DateTime nowUtc, DispatchRenotifySweepResult result,
			CancellationToken cancellationToken)
		{
			var call = await _callsService.GetCallByIdAsync(callId);

			if (call == null || call.IsDeleted || call.State != (int)CallStates.Active || rows.Any(r => r.DepartmentId != call.DepartmentId))
			{
				result.Cancelled += await _dispatchRenotificationsRepository.CancelAsync(rows.Select(r => r.DispatchRenotificationId), nowUtc, cancellationToken);
				return;
			}

			var config = await _departmentSettingsService.GetDispatchRenotifyConfigAsync(call.DepartmentId);
			if (config == null || !config.Enabled)
			{
				result.Cancelled += await _dispatchRenotificationsRepository.CancelAsync(rows.Select(r => r.DispatchRenotificationId), nowUtc, cancellationToken);
				return;
			}

			var timingLocked = Config.DispatchConfig.RenotifyTimingLocked;
			var intervalMinutes = config.GetEffectiveIntervalMinutes(timingLocked);
			var maxRetries = config.GetEffectiveMaxRetries(timingLocked);
			// Push only while the channels are locked, whatever the stored setting says.
			var channels = config.GetEffectiveChannels(Config.DispatchConfig.RenotifyChannelsLocked);

			call = await _callsService.PopulateCallData(call, true, false, false, true, true, true, false, false, false);

			// What the push needs from the call: the department code and the priority's colour and sound.
			call.Department = await _departmentsService.GetDepartmentByIdAsync(call.DepartmentId);
			try
			{
				call.CallPriority = await _callsService.GetCallPrioritiesByIdAsync(call.DepartmentId, call.Priority, false);
			}
			catch { /* Push falls back to the default colour and sound. */ }

			// Text message and voice call from the department's own number, as the dispatch did.
			string departmentNumber = null;
			if ((channels & (DispatchRenotifyChannels.Sms | DispatchRenotifyChannels.Voice)) != DispatchRenotifyChannels.None)
				departmentNumber = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(call.DepartmentId);

			var toCancel = new List<string>();

			foreach (var row in rows)
			{
				if (cancellationToken.IsCancellationRequested)
					break;

				var unitId = 0;
				if (row.RecipientType == (int)DispatchRenotifyRecipientTypes.Unit && !int.TryParse(row.RecipientId, out unitId))
				{
					toCancel.Add(row.DispatchRenotificationId);
					continue;
				}

				if (!IsStillDispatched(call, row) || IsStale(row, nowUtc, intervalMinutes))
				{
					toCancel.Add(row.DispatchRenotificationId);
					continue;
				}

				var acknowledgedOn = row.RecipientType == (int)DispatchRenotifyRecipientTypes.Unit
					? await GetUnitAcknowledgmentAsync(unitId, row.AcknowledgeAfter, nowUtc)
					: await GetUserAcknowledgmentAsync(row.RecipientId, call.DepartmentId, row.AcknowledgeAfter, nowUtc);

				if (acknowledgedOn.HasValue)
				{
					if (await _dispatchRenotificationsRepository.MarkAcknowledgedAsync(row.DispatchRenotificationId, acknowledgedOn.Value, nowUtc, cancellationToken))
						result.Acknowledged++;

					continue;
				}

				var attempt = row.Attempts + 1;

				// Only reachable when the retry count was lowered mid-cycle: the row has had all it now gets.
				if (attempt > maxRetries)
				{
					if (await _dispatchRenotificationsRepository.TryClaimAttemptAsync(row.DispatchRenotificationId, row.Attempts, row.Attempts, null, nowUtc, cancellationToken))
						result.Exhausted++;

					continue;
				}

				var nextNotifyOn = attempt >= maxRetries ? (DateTime?)null : nowUtc.AddMinutes(intervalMinutes);

				// Claim before sending: an overlapping sweep that loses the claim sends nothing.
				if (!await _dispatchRenotificationsRepository.TryClaimAttemptAsync(row.DispatchRenotificationId, row.Attempts, attempt, nextNotifyOn, nowUtc, cancellationToken))
					continue;

				if (!nextNotifyOn.HasValue)
					result.Exhausted++;

				bool sent;
				if (row.RecipientType == (int)DispatchRenotifyRecipientTypes.Unit)
				{
					var dispatch = call.UnitDispatches?.FirstOrDefault(d => d.UnitId == unitId) ?? new CallDispatchUnit { CallId = call.CallId, UnitId = unitId };
					sent = await _communicationService.SendUnitCallRenotificationAsync(call, dispatch, attempt, maxRetries, channels);
				}
				else
				{
					sent = await _communicationService.SendCallRenotificationAsync(call, row.RecipientId, attempt, maxRetries, channels, departmentNumber: departmentNumber);
				}

				if (sent)
					result.Renotified++;
			}

			if (toCancel.Count > 0)
				result.Cancelled += await _dispatchRenotificationsRepository.CancelAsync(toCancel, nowUtc, cancellationToken);
		}

		public async Task<DateTime?> GetUserAcknowledgmentAsync(string userId, int departmentId, DateTime sinceUtc, DateTime nowUtc)
		{
			if (string.IsNullOrWhiteSpace(userId))
				return null;

			var logs = await _actionLogsService.GetAllActionLogsForUserInDateRangeAsync(userId, sinceUtc, nowUtc.Add(ClockSkew));

			return logs?
				.Where(l => l != null && l.DepartmentId == departmentId && l.Timestamp >= sinceUtc && !IsAutomated(l.SetByOrigin))
				.OrderBy(l => l.Timestamp)
				.Select(l => (DateTime?)l.Timestamp)
				.FirstOrDefault();
		}

		public async Task<DateTime?> GetUnitAcknowledgmentAsync(int unitId, DateTime sinceUtc, DateTime nowUtc)
		{
			if (unitId <= 0)
				return null;

			var states = await _unitStatesRepository.GetAllUnitStatesForUnitInDateRangeAsync(unitId, sinceUtc, nowUtc.Add(ClockSkew));

			return states?
				.Where(s => s != null && s.Timestamp >= sinceUtc && !IsAutomated(s.SetByOrigin))
				.OrderBy(s => s.Timestamp)
				.Select(s => (DateTime?)s.Timestamp)
				.FirstOrDefault();
		}

		/// <summary>A status Resgrid applied itself. Rows from before M0260 carry no origin and count as a person's.</summary>
		public static bool IsAutomated(int? setByOrigin)
		{
			return setByOrigin.HasValue && Enum.IsDefined(typeof(StatusSetOrigins), setByOrigin.Value) && ((StatusSetOrigins)setByOrigin.Value).IsAutomated();
		}

		/// <summary>
		/// Whether the recipient is still on the call: a person dispatched by name, or through the group, role or unit they were
		/// reached by; a unit still dispatched. Expects the call's dispatch lists to be loaded.
		/// </summary>
		public static bool IsStillDispatched(Call call, DispatchRenotification row)
		{
			if (row.RecipientType == (int)DispatchRenotifyRecipientTypes.Unit)
				return int.TryParse(row.RecipientId, out var unitId) && (call.UnitDispatches?.Any(d => d.UnitId == unitId) ?? false);

			if (call.Dispatches?.Any(d => string.Equals(d.UserId, row.RecipientId, StringComparison.OrdinalIgnoreCase)) ?? false)
				return true;

			int.TryParse(row.SourceId, out var sourceId);

			switch ((DispatchRenotifySourceTypes)row.SourceType)
			{
				case DispatchRenotifySourceTypes.Group:
					return call.GroupDispatches?.Any(d => d.DepartmentGroupId == sourceId) ?? false;
				case DispatchRenotifySourceTypes.Role:
					return call.RoleDispatches?.Any(d => d.RoleId == sourceId) ?? false;
				case DispatchRenotifySourceTypes.UnitCrew:
				case DispatchRenotifySourceTypes.UnitGroup:
					return call.UnitDispatches?.Any(d => d.UnitId == sourceId) ?? false;
				default:
					// Dispatched by name and no longer on the call's list.
					return false;
			}
		}

		/// <summary>
		/// A row the sweep reaches more than an interval after it fell due (the worker was stopped): drop it rather than send a
		/// reminder for a dispatch that has moved on. Measured from the due time, not the dispatch, because a later broadcast can
		/// restart an old row's cycle.
		/// </summary>
		public static bool IsStale(DispatchRenotification row, DateTime nowUtc, int intervalMinutes)
		{
			return row.NextNotifyOn.HasValue && nowUtc > row.NextNotifyOn.Value + TimeSpan.FromMinutes(intervalMinutes) + StaleGrace;
		}
	}
}
