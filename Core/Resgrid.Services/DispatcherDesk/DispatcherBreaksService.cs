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
	/// Dispatcher breaks (M0324, plan D11). A break is a length: whenever it starts, the dispatcher is due back
	/// <see cref="DispatcherBreak.DurationMinutes"/> later. Planning warns about overlap and coverage but never refuses it
	/// (only a dispatcher's own breaks may not overlap); starting a break can be refused below the minimum when the department
	/// says so, unless a supervisor starts it.
	/// </summary>
	public class DispatcherBreaksService : IDispatcherBreaksService
	{
		public const int MaxBreakMinutes = 240;
		public const int DefaultAdHocMinutes = 15;

		/// <summary>Minutes past the expected return before supervisors are told (unless auto return comes sooner).</summary>
		public const int OverdueGraceMinutes = 5;

		/// <summary>A planned break this far past its start, never taken, is missed whatever the duty says.</summary>
		public const int MissedAfterHours = 24;

		private readonly IDispatcherBreaksRepository _breaksRepository;
		private readonly IDispatcherPresencesRepository _presencesRepository;
		private readonly IDispatcherQueueItemsRepository _itemsRepository;
		private readonly IShiftDispatchersService _shiftDispatchersService;
		private readonly IDispatcherPresenceService _presenceService;
		private readonly IDispatcherDeskConfigService _configService;
		private readonly IDispatcherDeskNotifier _notifier;
		private readonly Lazy<IDispatcherRoutingService> _routingService;

		public DispatcherBreaksService(IDispatcherBreaksRepository breaksRepository, IDispatcherPresencesRepository presencesRepository,
			IDispatcherQueueItemsRepository itemsRepository, IShiftDispatchersService shiftDispatchersService, IDispatcherPresenceService presenceService,
			IDispatcherDeskConfigService configService, IDispatcherDeskNotifier notifier, Lazy<IDispatcherRoutingService> routingService)
		{
			_breaksRepository = breaksRepository;
			_presencesRepository = presencesRepository;
			_itemsRepository = itemsRepository;
			_shiftDispatchersService = shiftDispatchersService;
			_presenceService = presenceService;
			_configService = configService;
			_notifier = notifier;
			_routingService = routingService;
		}

		public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

		public async Task<List<DispatcherBreak>> GetBreaksAsync(int departmentId, DateTime fromUtc, DateTime toUtc) =>
			(await _breaksRepository.GetForDepartmentInRangeAsync(departmentId, fromUtc, toUtc) ?? Enumerable.Empty<DispatcherBreak>())
			.OrderBy(x => x.StartedOn ?? x.ScheduledStartOn).ToList();

		public async Task<List<DispatcherBreak>> GetBreaksForShiftDayAsync(int departmentId, int shiftDayId) =>
			(await _breaksRepository.GetForShiftDayAsync(shiftDayId) ?? Enumerable.Empty<DispatcherBreak>())
			.Where(x => x.DepartmentId == departmentId).OrderBy(x => x.StartedOn ?? x.ScheduledStartOn).ToList();

		public async Task<List<DispatcherBreak>> GetOpenBreaksForUserAsync(int departmentId, string userId, DateTime utcNow) =>
			(await _breaksRepository.GetOpenForUserAsync(departmentId, userId, utcNow.AddHours(-12), utcNow.AddHours(24)) ?? Enumerable.Empty<DispatcherBreak>())
			.OrderByDescending(x => x.State == (int)DispatcherBreakStates.Active).ThenBy(x => x.ScheduledStartOn).ToList();

		public async Task<DispatcherBreak> GetBreakAsync(int departmentId, string dispatcherBreakId)
		{
			if (string.IsNullOrWhiteSpace(dispatcherBreakId))
				return null;

			var row = await _breaksRepository.GetByIdAsync(dispatcherBreakId);
			return row != null && row.DepartmentId == departmentId ? row : null;
		}

		public async Task<DispatcherDeskResult<DispatcherBreak>> SaveBreakAsync(int departmentId, DispatcherBreak dispatcherBreak, string byUserId,
			CancellationToken cancellationToken = default)
		{
			if (dispatcherBreak == null || string.IsNullOrWhiteSpace(dispatcherBreak.UserId) || !dispatcherBreak.ScheduledStartOn.HasValue ||
				dispatcherBreak.DurationMinutes <= 0 || dispatcherBreak.DurationMinutes > MaxBreakMinutes)
				return Fail(DispatcherDeskErrors.InvalidInput);

			var now = UtcNow();
			var start = DateTime.SpecifyKind(dispatcherBreak.ScheduledStartOn.Value, DateTimeKind.Utc);
			var end = start.AddMinutes(dispatcherBreak.DurationMinutes);

			DispatcherBreak row;
			if (!string.IsNullOrWhiteSpace(dispatcherBreak.DispatcherBreakId))
			{
				row = await GetBreakAsync(departmentId, dispatcherBreak.DispatcherBreakId);
				if (row == null)
					return Fail(DispatcherDeskErrors.BreakNotFound);

				if (row.State != (int)DispatcherBreakStates.Scheduled || !string.Equals(row.UserId, dispatcherBreak.UserId, StringComparison.OrdinalIgnoreCase))
					return Fail(DispatcherDeskErrors.BreakNotStartable);
			}
			else
			{
				row = new DispatcherBreak
				{
					DepartmentId = departmentId,
					UserId = dispatcherBreak.UserId,
					State = (int)DispatcherBreakStates.Scheduled,
					CreatedByUserId = byUserId,
					CreatedOn = now
				};
			}

			var warnings = new List<DispatcherDeskWarning>();

			// The duty window the break falls in (it keeps the shift day for the planner and the timeline).
			var windows = (await _shiftDispatchersService.GetDutyWindowsAsync(departmentId, start.AddHours(-1), end.AddHours(1)))
				.Where(x => !x.IsPending && string.Equals(x.UserId, dispatcherBreak.UserId, StringComparison.OrdinalIgnoreCase)).ToList();
			var window = dispatcherBreak.ShiftDayId.HasValue
				? windows.FirstOrDefault(x => x.ShiftDayId == dispatcherBreak.ShiftDayId.Value)
				: windows.FirstOrDefault(x => x.StartUtc <= start && x.EndUtc > start) ?? windows.FirstOrDefault(x => x.StartUtc < end && x.EndUtc > start);

			if (window == null || start < window.StartUtc || end > window.EndUtc)
				warnings.Add(new DispatcherDeskWarning { Kind = DispatcherDeskWarningKinds.OutsideDuty, StartUtc = start, EndUtc = end, UserIds = { dispatcherBreak.UserId } });

			var own = (await _breaksRepository.GetForDepartmentInRangeAsync(departmentId, start.AddDays(-1), end.AddDays(1)) ?? Enumerable.Empty<DispatcherBreak>())
				.Where(x => string.Equals(x.UserId, dispatcherBreak.UserId, StringComparison.OrdinalIgnoreCase) &&
							!string.Equals(x.DispatcherBreakId, row.DispatcherBreakId, StringComparison.OrdinalIgnoreCase))
				.Select(x => x.CoverageWindow(now))
				.Where(x => x.HasValue)
				.Any(x => x.Value.Start < end && x.Value.End > start);
			if (own)
				return Fail(DispatcherDeskErrors.BreakOverlaps);

			row.BreakType = Enum.IsDefined(typeof(DispatcherBreakTypes), dispatcherBreak.BreakType) ? dispatcherBreak.BreakType : (int)DispatcherBreakTypes.Break;
			row.ScheduledStartOn = start;
			row.DurationMinutes = dispatcherBreak.DurationMinutes;
			row.Note = Truncate(dispatcherBreak.Note, 500);
			row.ShiftId = window?.ShiftId ?? dispatcherBreak.ShiftId;
			row.ShiftDayId = window?.ShiftDayId ?? dispatcherBreak.ShiftDayId;
			row.UpdatedByUserId = byUserId;
			row.UpdatedOn = now;

			// A re-planned break reminds again.
			row.ReminderSentOn = null;
			row.DueAlertSentOn = null;

			if (string.IsNullOrWhiteSpace(row.DispatcherBreakId))
			{
				row.DispatcherBreakId = Guid.NewGuid().ToString();
				await _breaksRepository.InsertAsync(row, cancellationToken, true);
			}
			else if (!await _breaksRepository.TryTransitionAsync(row, (int)DispatcherBreakStates.Scheduled, cancellationToken))
			{
				return Fail(DispatcherDeskErrors.BreakNotStartable);
			}
			else
			{
				// TryTransition writes the plan; the alert stamps reset through a plain save (rare path).
				await _breaksRepository.SaveOrUpdateAsync(row, cancellationToken, true);
			}

			var coverage = await GetCoverageAsync(departmentId, start, end, 5);
			warnings.AddRange(coverage.Warnings.Where(x => x.Kind != DispatcherDeskWarningKinds.BreaksOverlap ||
														   x.UserIds.Contains(dispatcherBreak.UserId, StringComparer.OrdinalIgnoreCase)));

			await _notifier.PublishDeskChangedAsync(departmentId, dispatcherBreak.UserId);
			return DispatcherDeskResult<DispatcherBreak>.Ok(row, warnings);
		}

		public async Task<DispatcherDeskResult<bool>> CancelBreakAsync(int departmentId, string dispatcherBreakId, string byUserId,
			CancellationToken cancellationToken = default)
		{
			var row = await GetBreakAsync(departmentId, dispatcherBreakId);
			if (row == null)
				return DispatcherDeskResult<bool>.Fail(DispatcherDeskErrors.BreakNotFound);

			if (row.State != (int)DispatcherBreakStates.Scheduled)
				return DispatcherDeskResult<bool>.Fail(DispatcherDeskErrors.BreakNotStartable);

			row.State = (int)DispatcherBreakStates.Cancelled;
			row.UpdatedByUserId = byUserId;
			row.UpdatedOn = UtcNow();

			if (!await _breaksRepository.TryTransitionAsync(row, (int)DispatcherBreakStates.Scheduled, cancellationToken))
				return DispatcherDeskResult<bool>.Fail(DispatcherDeskErrors.BreakNotStartable);

			await _notifier.PublishDeskChangedAsync(departmentId, row.UserId);
			return DispatcherDeskResult<bool>.Ok(true);
		}

		public async Task<DispatcherDeskResult<List<DispatcherBreak>>> PlanBreaksAsync(int departmentId, int shiftDayId, bool replace, string byUserId,
			CancellationToken cancellationToken = default)
		{
			var roster = (await _shiftDispatchersService.GetDayRosterAsync(departmentId, shiftDayId)).Where(x => !x.IsPending).ToList();
			if (roster.Count == 0)
				return DispatcherDeskResult<List<DispatcherBreak>>.Fail(DispatcherDeskErrors.ShiftNotFound);

			var config = await _configService.GetConfigAsync(departmentId);
			var settings = await _shiftDispatchersService.GetSettingsForShiftAsync(roster[0].ShiftId);
			var plan = settings?.GetBreakPlan();
			if (plan == null || plan.Count == 0)
				plan = config.GetDefaultBreakPlan();
			if (plan == null || plan.Count == 0)
				return DispatcherDeskResult<List<DispatcherBreak>>.Fail(DispatcherDeskErrors.InvalidInput, "No break plan.");

			var now = UtcNow();
			var existing = await GetBreaksForShiftDayAsync(departmentId, shiftDayId);

			if (replace)
			{
				foreach (var planned in existing.Where(x => x.State == (int)DispatcherBreakStates.Scheduled && x.ScheduledStartOn > now).ToList())
				{
					planned.State = (int)DispatcherBreakStates.Cancelled;
					planned.UpdatedByUserId = byUserId;
					planned.UpdatedOn = now;
					if (await _breaksRepository.TryTransitionAsync(planned, (int)DispatcherBreakStates.Scheduled, cancellationToken))
						existing.Remove(planned);
				}
			}

			var kept = existing.Where(x => x.State == (int)DispatcherBreakStates.Scheduled || x.State == (int)DispatcherBreakStates.Active ||
										   x.State == (int)DispatcherBreakStates.Completed).ToList();

			// Without replace, only dispatchers who have nothing planned yet get the plan.
			var dispatchers = roster.GroupBy(x => x.UserId, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
				.Where(x => replace || !kept.Any(b => string.Equals(b.UserId, x.UserId, StringComparison.OrdinalIgnoreCase)))
				.ToList();

			var minimum = settings != null && settings.MinimumOnDesk > 0 ? settings.MinimumOnDesk : config.EffectiveMinimumAvailable();
			var occupied = kept.Select(x => (Break: x, Window: x.CoverageWindow(now))).Where(x => x.Window.HasValue)
				.Select(x => (x.Break.UserId, x.Window.Value.Start, x.Window.Value.End)).ToList();

			// Every dispatcher on the day counts towards coverage, including those who already have breaks.
			var everyone = roster.GroupBy(x => x.UserId, StringComparer.OrdinalIgnoreCase).Count();
			var allowedMinimum = Math.Max(0, minimum - (everyone - dispatchers.Count));
			// Planning part-way through the day places the breaks still to come from now on (a target already past is moved up,
			// not dropped): the first is at least a few minutes out so nobody is told to go on break this instant.
			var placed = DispatcherBreakPlanner.Plan(dispatchers, plan, occupied, allowedMinimum, now.AddMinutes(DispatcherBreakPlanner.StepMinutes));

			var created = new List<DispatcherBreak>();
			var warnings = new List<DispatcherDeskWarning>();
			foreach (var entry in placed.Where(x => x.StartUtc > now))
			{
				var row = new DispatcherBreak
				{
					DispatcherBreakId = Guid.NewGuid().ToString(),
					DepartmentId = departmentId,
					UserId = entry.UserId,
					ShiftId = entry.ShiftId,
					ShiftDayId = entry.ShiftDayId,
					BreakType = entry.BreakType,
					State = (int)DispatcherBreakStates.Scheduled,
					ScheduledStartOn = entry.StartUtc,
					DurationMinutes = entry.DurationMinutes,
					CreatedByUserId = byUserId,
					CreatedOn = now,
					UpdatedByUserId = byUserId,
					UpdatedOn = now
				};

				await _breaksRepository.InsertAsync(row, cancellationToken, true);
				created.Add(row);

				if (entry.BreaksMinimum)
					warnings.Add(new DispatcherDeskWarning
					{
						Kind = DispatcherDeskWarningKinds.CoverageBelowMinimum,
						StartUtc = entry.StartUtc,
						EndUtc = entry.StartUtc.AddMinutes(entry.DurationMinutes),
						UserIds = { entry.UserId },
						Minimum = minimum
					});
			}

			await _notifier.PublishDeskChangedAsync(departmentId);
			return DispatcherDeskResult<List<DispatcherBreak>>.Ok(created, warnings);
		}

		public async Task<DispatcherDeskResult<DispatcherBreak>> StartBreakAsync(int departmentId, string userId, string dispatcherBreakId, int? breakType,
			int? durationMinutes, bool returnWork, string byUserId, CancellationToken cancellationToken = default)
		{
			var bySupervisor = !string.Equals(byUserId, userId, StringComparison.OrdinalIgnoreCase);
			if (bySupervisor && !await _presenceService.CanSuperviseAsync(departmentId, byUserId))
				return Fail(DispatcherDeskErrors.NotPermitted);

			var presence = await _presencesRepository.GetForUserAsync(departmentId, userId);
			if (presence == null || !presence.IsSignedIn())
				return Fail(DispatcherDeskErrors.NotSignedIn);

			if (presence.State == (int)DispatcherPresenceStates.OnBreak || await _breaksRepository.GetActiveForUserAsync(departmentId, userId) != null)
				return Fail(DispatcherDeskErrors.AlreadyOnBreak);

			var now = UtcNow();
			DispatcherBreak row;
			if (!string.IsNullOrWhiteSpace(dispatcherBreakId))
			{
				row = await GetBreakAsync(departmentId, dispatcherBreakId);
				if (row == null || !string.Equals(row.UserId, userId, StringComparison.OrdinalIgnoreCase))
					return Fail(DispatcherDeskErrors.BreakNotFound);

				if (row.State != (int)DispatcherBreakStates.Scheduled)
					return Fail(DispatcherDeskErrors.BreakNotStartable);

				if (durationMinutes.HasValue && durationMinutes.Value > 0 && durationMinutes.Value <= MaxBreakMinutes)
					row.DurationMinutes = durationMinutes.Value;
			}
			else
			{
				var minutes = durationMinutes ?? DefaultAdHocMinutes;
				if (minutes <= 0 || minutes > MaxBreakMinutes)
					return Fail(DispatcherDeskErrors.InvalidInput);

				row = new DispatcherBreak
				{
					DepartmentId = departmentId,
					UserId = userId,
					BreakType = breakType.HasValue && Enum.IsDefined(typeof(DispatcherBreakTypes), breakType.Value) ? breakType.Value : (int)DispatcherBreakTypes.Break,
					State = (int)DispatcherBreakStates.Scheduled,
					DurationMinutes = minutes,
					ShiftId = presence.DutyShiftId,
					ShiftDayId = presence.DutyShiftDayId,
					CreatedByUserId = byUserId,
					CreatedOn = now
				};
			}

			var warnings = new List<DispatcherDeskWarning>();

			// Work the dispatcher holds.
			var mine = (await _itemsRepository.GetOpenForDepartmentAsync(departmentId) ?? Enumerable.Empty<DispatcherQueueItem>())
				.Where(x => string.Equals(x.AssignedUserId, userId, StringComparison.OrdinalIgnoreCase)).ToList();
			var held = mine.Where(x => x.State == (int)DispatcherQueueItemStates.Accepted).ToList();
			if (held.Count > 0 && !returnWork)
				return Fail(DispatcherDeskErrors.HoldingWork, held.Count.ToString());

			// Coverage: the dispatcher stops counting as available.
			var config = await _configService.GetConfigAsync(departmentId);
			var desk = await _presenceService.GetDeskAsync(departmentId, now);
			var member = desk.Members.FirstOrDefault(x => string.Equals(x.UserId, userId, StringComparison.OrdinalIgnoreCase));
			var settings = member?.Duty != null ? await _shiftDispatchersService.GetSettingsForShiftAsync(member.Duty.ShiftId) : null;
			var minimum = settings != null && settings.MinimumOnDesk > 0 ? settings.MinimumOnDesk : config.EffectiveMinimumAvailable();
			var countsNow = member != null && member.IsOnDuty && (member.DisplayState == DispatcherDisplayStates.Available || member.DisplayState == DispatcherDisplayStates.Busy);
			var availableAfter = desk.AvailableCount - (countsNow ? 1 : 0);

			if (minimum > 0 && availableAfter < minimum)
			{
				var warning = new DispatcherDeskWarning
				{
					Kind = availableAfter <= 0 ? DispatcherDeskWarningKinds.NoCoverage : DispatcherDeskWarningKinds.CoverageBelowMinimum,
					StartUtc = now,
					EndUtc = now.AddMinutes(row.DurationMinutes),
					UserIds = { userId },
					Available = Math.Max(0, availableAfter),
					Minimum = minimum
				};

				var supervisorOverride = bySupervisor || await _presenceService.CanSuperviseAsync(departmentId, byUserId);
				if (config.PreventBreakBelowMinimum && !supervisorOverride)
					return DispatcherDeskResult<DispatcherBreak>.Fail(DispatcherDeskErrors.CoverageTooLow, null, new List<DispatcherDeskWarning> { warning });

				warnings.Add(warning);
			}

			if (held.Count > 0)
				warnings.Add(new DispatcherDeskWarning { Kind = DispatcherDeskWarningKinds.WorkReturnedToQueue, UserIds = { userId }, Available = held.Count });

			if (row.ScheduledStartOn.HasValue)
			{
				var difference = (int)Math.Round((now - row.ScheduledStartOn.Value).TotalMinutes);
				if (Math.Abs(difference) >= 5)
					warnings.Add(new DispatcherDeskWarning
					{
						Kind = difference > 0 ? DispatcherDeskWarningKinds.StartedLate : DispatcherDeskWarningKinds.StartedEarly,
						Minutes = Math.Abs(difference),
						UserIds = { userId }
					});
			}

			row.State = (int)DispatcherBreakStates.Active;
			row.StartedOn = now;
			row.ExpectedReturnOn = now.AddMinutes(row.DurationMinutes);
			row.UpdatedByUserId = byUserId;
			row.UpdatedOn = now;

			if (string.IsNullOrWhiteSpace(row.DispatcherBreakId))
			{
				row.DispatcherBreakId = Guid.NewGuid().ToString();
				await _breaksRepository.InsertAsync(row, cancellationToken, true);
			}
			else if (!await _breaksRepository.TryTransitionAsync(row, (int)DispatcherBreakStates.Scheduled, cancellationToken))
			{
				return Fail(DispatcherDeskErrors.BreakNotStartable);
			}

			presence.State = (int)DispatcherPresenceStates.OnBreak;
			presence.ActiveBreakId = row.DispatcherBreakId;
			presence.StateChangedOn = now;
			presence.AwayReason = null;
			await _presencesRepository.SaveOrUpdateAsync(presence, cancellationToken, true);

			// Only now that the dispatcher is on break: work handed back is offered to someone else, never straight back to them.
			foreach (var item in mine)
				await _routingService.Value.ReleaseAsync(departmentId, item.DispatcherQueueItemId, userId, cancellationToken);

			await _notifier.PublishDeskChangedAsync(departmentId, userId);
			return DispatcherDeskResult<DispatcherBreak>.Ok(row, warnings);
		}

		public async Task<DispatcherDeskResult<DispatcherBreak>> EndBreakAsync(int departmentId, string userId, string byUserId, DispatcherBreakEndReasons reason,
			CancellationToken cancellationToken = default)
		{
			if (reason == DispatcherBreakEndReasons.Supervisor && !await _presenceService.CanSuperviseAsync(departmentId, byUserId))
				return Fail(DispatcherDeskErrors.NotPermitted);

			if (reason == DispatcherBreakEndReasons.Returned && !string.Equals(byUserId, userId, StringComparison.OrdinalIgnoreCase) &&
				!await _presenceService.CanSuperviseAsync(departmentId, byUserId))
				return Fail(DispatcherDeskErrors.NotPermitted);

			var row = await _breaksRepository.GetActiveForUserAsync(departmentId, userId);
			var presence = await _presencesRepository.GetForUserAsync(departmentId, userId);
			if (row == null && (presence == null || presence.State != (int)DispatcherPresenceStates.OnBreak))
				return Fail(DispatcherDeskErrors.NotOnBreak);

			var now = UtcNow();
			if (row != null)
			{
				row.State = (int)DispatcherBreakStates.Completed;
				row.EndedOn = now;
				row.EndReason = (int)reason;
				row.EndedByUserId = byUserId;
				row.UpdatedByUserId = byUserId;
				row.UpdatedOn = now;

				if (!await _breaksRepository.TryTransitionAsync(row, (int)DispatcherBreakStates.Active, cancellationToken))
					return Fail(DispatcherDeskErrors.NotOnBreak);
			}

			if (presence != null && presence.IsSignedIn())
			{
				presence.State = (int)DispatcherPresenceStates.Available;
				presence.ActiveBreakId = null;
				presence.StateChangedOn = now;
				presence.MissedOffers = 0;
				if (string.Equals(byUserId, userId, StringComparison.OrdinalIgnoreCase))
					presence.LastSeenOn = now;
				await _presencesRepository.SaveOrUpdateAsync(presence, cancellationToken, true);
			}

			await _notifier.PublishDeskChangedAsync(departmentId, userId);

			try
			{
				await _routingService.Value.RouteDepartmentAsync(departmentId, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Dispatcher routing after a break ended");
			}

			return DispatcherDeskResult<DispatcherBreak>.Ok(row);
		}

		public async Task<DispatcherCoverage> GetCoverageAsync(int departmentId, DateTime fromUtc, DateTime toUtc, int slotMinutes = 5)
		{
			var now = UtcNow();
			var config = await _configService.GetConfigAsync(departmentId);
			var duty = await _shiftDispatchersService.GetDutyWindowsAsync(departmentId, fromUtc, toUtc);

			// Dispatchers on unscheduled duty count from when they signed in until their chosen end (or now, if open-ended).
			foreach (var presence in (await _presencesRepository.GetByDepartmentIdAsync(departmentId) ?? Enumerable.Empty<DispatcherPresence>())
				.Where(x => x.IsSignedIn() && x.DutyMode == (int)DispatcherDutyModes.Unscheduled && x.SignedInOn.HasValue))
			{
				var end = presence.DutyEndsOn ?? (now > toUtc ? now : toUtc);
				if (presence.SignedInOn.Value >= toUtc || end <= fromUtc)
					continue;

				duty.Add(new DispatcherDutyWindow
				{
					UserId = presence.UserId,
					ShiftName = null,
					Day = presence.SignedInOn.Value.Date,
					StartUtc = presence.SignedInOn.Value,
					EndUtc = end
				});
			}

			var breaks = (await _breaksRepository.GetForDepartmentInRangeAsync(departmentId, fromUtc.AddHours(-6), toUtc) ?? Enumerable.Empty<DispatcherBreak>())
				.Where(x => x.State != (int)DispatcherBreakStates.Cancelled)
				.ToList();

			var minimums = (await _shiftDispatchersService.GetSettingsForDepartmentAsync(departmentId))
				.Where(x => x.MinimumOnDesk > 0).ToDictionary(x => x.ShiftId, x => x.MinimumOnDesk);
			var departmentMinimum = config.EffectiveMinimumAvailable();

			return DispatcherCoverageCalculator.Build(fromUtc, toUtc, slotMinutes, duty, breaks,
				covering => covering.Select(x => minimums.TryGetValue(x.ShiftId, out var m) ? m : departmentMinimum).DefaultIfEmpty(departmentMinimum).Max(),
				now);
		}

		public async Task<int> ProcessBreakTimersAsync(DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var configs = new Dictionary<int, DispatcherDeskConfig>();
			async Task<DispatcherDeskConfig> configFor(int departmentId)
			{
				if (!configs.TryGetValue(departmentId, out var config))
					configs[departmentId] = config = await _configService.GetConfigAsync(departmentId);
				return config;
			}

			var changes = 0;
			var rows = (await _breaksRepository.GetForTimersAsync(utcNow.AddMinutes(120), utcNow.AddHours(-MissedAfterHours - 1)) ?? Enumerable.Empty<DispatcherBreak>()).ToList();

			foreach (var row in rows)
			{
				if (cancellationToken.IsCancellationRequested)
					break;

				try
				{
					var config = await configFor(row.DepartmentId);
					if (row.State == (int)DispatcherBreakStates.Scheduled)
						changes += await ProcessScheduledAsync(row, config, utcNow, cancellationToken);
					else if (row.State == (int)DispatcherBreakStates.Active)
						changes += await ProcessActiveAsync(row, config, utcNow, cancellationToken);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Dispatcher break timer for {row.DispatcherBreakId}");
				}
			}

			return changes;
		}

		private async Task<int> ProcessScheduledAsync(DispatcherBreak row, DispatcherDeskConfig config, DateTime now, CancellationToken cancellationToken)
		{
			if (!row.ScheduledStartOn.HasValue)
				return 0;

			var start = row.ScheduledStartOn.Value;
			var presence = await _presencesRepository.GetForUserAsync(row.DepartmentId, row.UserId);
			var signedIn = presence != null && presence.IsSignedIn();

			// Missed: the planned break is over and the dispatcher is no longer on the desk (or it is a day old).
			if (now > start.AddHours(MissedAfterHours) || now > start.AddMinutes(row.DurationMinutes) && !signedIn)
			{
				row.State = (int)DispatcherBreakStates.Missed;
				row.UpdatedOn = now;
				if (await _breaksRepository.TryTransitionAsync(row, (int)DispatcherBreakStates.Scheduled, cancellationToken))
				{
					await _notifier.PublishDeskChangedAsync(row.DepartmentId, row.UserId);
					return 1;
				}

				return 0;
			}

			if (!config.Enabled || !signedIn)
				return 0;

			var reminder = config.EffectiveBreakReminderMinutes();
			if (reminder > 0 && !row.ReminderSentOn.HasValue && start > now && start <= now.AddMinutes(reminder))
			{
				if (await _breaksRepository.TryMarkAlertAsync(row.DispatcherBreakId, "ReminderSentOn", now, cancellationToken))
				{
					await _notifier.NotifyBreakReminderAsync(row, due: false, cancellationToken);
					return 1;
				}
			}

			if (!row.DueAlertSentOn.HasValue && start <= now && start > now.AddMinutes(-60))
			{
				if (await _breaksRepository.TryMarkAlertAsync(row.DispatcherBreakId, "DueAlertSentOn", now, cancellationToken))
				{
					await _notifier.NotifyBreakReminderAsync(row, due: true, cancellationToken);
					await _notifier.PublishDeskChangedAsync(row.DepartmentId, row.UserId);
					return 1;
				}
			}

			return 0;
		}

		private async Task<int> ProcessActiveAsync(DispatcherBreak row, DispatcherDeskConfig config, DateTime now, CancellationToken cancellationToken)
		{
			var due = row.ExpectedReturnOn ?? row.StartedOn?.AddMinutes(row.DurationMinutes);
			if (!due.HasValue || due.Value > now)
				return 0;

			var changes = 0;

			if (!row.ReturnAlertSentOn.HasValue && await _breaksRepository.TryMarkAlertAsync(row.DispatcherBreakId, "ReturnAlertSentOn", now, cancellationToken))
			{
				await _notifier.NotifyBreakOverAsync(row, null, autoReturned: false, cancellationToken);
				await _notifier.PublishDeskChangedAsync(row.DepartmentId, row.UserId);
				changes++;
			}

			var autoReturn = config.EffectiveAutoReturnAfterMinutes();
			if (autoReturn > 0 && now >= due.Value.AddMinutes(autoReturn))
			{
				var ended = await EndBreakAsync(row.DepartmentId, row.UserId, null, DispatcherBreakEndReasons.AutoReturn, cancellationToken);
				if (ended.Success)
				{
					await _notifier.NotifyBreakOverAsync(row, null, autoReturned: true, cancellationToken);
					if (config.NotifySupervisorsOnOverdueBreak)
						await _notifier.NotifyBreakOverAsync(row, await _presenceService.GetSupervisorUserIdsAsync(row.DepartmentId, now), autoReturned: true, cancellationToken);
					changes++;
				}

				return changes;
			}

			var grace = autoReturn > 0 ? Math.Min(OverdueGraceMinutes, autoReturn) : OverdueGraceMinutes;
			if (config.NotifySupervisorsOnOverdueBreak && !row.OverdueAlertSentOn.HasValue && now >= due.Value.AddMinutes(grace) && (autoReturn == 0 || grace < autoReturn))
			{
				if (await _breaksRepository.TryMarkAlertAsync(row.DispatcherBreakId, "OverdueAlertSentOn", now, cancellationToken))
				{
					await _notifier.NotifyBreakOverAsync(row, await _presenceService.GetSupervisorUserIdsAsync(row.DepartmentId, now), autoReturned: false, cancellationToken);
					await _notifier.PublishDeskChangedAsync(row.DepartmentId, row.UserId);
					changes++;
				}
			}

			return changes;
		}

		private static DispatcherDeskResult<DispatcherBreak> Fail(DispatcherDeskErrors error, string message = null) =>
			DispatcherDeskResult<DispatcherBreak>.Fail(error, message);

		private static string Truncate(string value, int length) =>
			string.IsNullOrEmpty(value) || value.Length <= length ? value : value.Substring(0, length);
	}
}
