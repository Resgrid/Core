using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
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

		/// <summary>Scheduled breaks past the timer window (the worker was down) marked missed per pass.</summary>
		public const int MissedCatchUpBatch = 200;

		private readonly IDispatcherBreaksRepository _breaksRepository;
		private readonly IDispatcherPresencesRepository _presencesRepository;
		private readonly IDispatcherQueueItemsRepository _itemsRepository;
		private readonly IShiftDispatchersService _shiftDispatchersService;
		private readonly IDispatcherPresenceService _presenceService;
		private readonly IDispatcherDeskConfigService _configService;
		private readonly IDispatcherDeskNotifier _notifier;
		private readonly Lazy<IDispatcherRoutingService> _routingService;
		private readonly IUnitOfWork _unitOfWork;

		/// <param name="unitOfWork">
		/// Optional: with it, a break start runs its coverage check and commit under a department lock, and a re-plan of the shift day
		/// commits its cancellations and new breaks together. Without it (tests) the writes run one by one.
		/// </param>
		public DispatcherBreaksService(IDispatcherBreaksRepository breaksRepository, IDispatcherPresencesRepository presencesRepository,
			IDispatcherQueueItemsRepository itemsRepository, IShiftDispatchersService shiftDispatchersService, IDispatcherPresenceService presenceService,
			IDispatcherDeskConfigService configService, IDispatcherDeskNotifier notifier, Lazy<IDispatcherRoutingService> routingService,
			IUnitOfWork unitOfWork = null)
		{
			_breaksRepository = breaksRepository;
			_presencesRepository = presencesRepository;
			_itemsRepository = itemsRepository;
			_shiftDispatchersService = shiftDispatchersService;
			_presenceService = presenceService;
			_configService = configService;
			_notifier = notifier;
			_routingService = routingService;
			_unitOfWork = unitOfWork;
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
			DispatcherBreak observed = null;
			if (!string.IsNullOrWhiteSpace(dispatcherBreak.DispatcherBreakId))
			{
				row = await GetBreakAsync(departmentId, dispatcherBreak.DispatcherBreakId);
				if (row == null)
					return Fail(DispatcherDeskErrors.BreakNotFound);

				if (row.State != (int)DispatcherBreakStates.Scheduled || !string.Equals(row.UserId, dispatcherBreak.UserId, StringComparison.OrdinalIgnoreCase))
					return Fail(DispatcherDeskErrors.BreakNotStartable);

				observed = PlanOf(row);
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

			var outsideDuty = window == null || start < window.StartUtc || end > window.EndUtc;

			// A dispatcher planning their own break (not a supervisor) may only place it inside an approved duty window on a shift that
			// allows self-scheduled breaks: the shift the break is moved into decides, not the one it was first planned on.
			if (!await _presenceService.CanSuperviseAsync(departmentId, byUserId))
			{
				if (!string.Equals(byUserId, dispatcherBreak.UserId, StringComparison.OrdinalIgnoreCase) || outsideDuty ||
					!await AllowsSelfScheduledBreaksAsync(departmentId, window.ShiftId))
					return Fail(DispatcherDeskErrors.NotPermitted);
			}

			if (outsideDuty)
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
			else if (!await _breaksRepository.TryUpdateObservedAsync(row, observed, resetPlanAlerts: true, cancellationToken))
			{
				// One conditional write (plan + reminder reset): a break started, cancelled or re-planned since it was read is left alone.
				return Fail(DispatcherDeskErrors.BreakNotStartable);
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
			var minimum = settings != null && settings.MinimumOnDesk > 0 ? settings.MinimumOnDesk : config.EffectiveMinimumAvailable();

			// The day's breaks are read and the new plan inserted under the department's break lock (the one break starts take), so two
			// editors planning the same day at once cannot both plan against "nothing planned yet": the second waits and plans against
			// what the first committed.
			(List<DispatcherBreak> Created, List<DispatcherDeskWarning> Warnings) planned;
			try
			{
				planned = await InTransactionAsync(async () =>
				{
					if (_unitOfWork != null)
						await _breaksRepository.LockDepartmentForBreakStartAsync(departmentId, cancellationToken);

					return await PlanDayUnderLockAsync(departmentId, shiftDayId, replace, byUserId, roster, plan, minimum, now, cancellationToken);
				}, cancellationToken);
			}
			catch (BreakPlanChangedException)
			{
				return DispatcherDeskResult<List<DispatcherBreak>>.Fail(DispatcherDeskErrors.BreakNotStartable, "The day's breaks changed while planning; plan again.");
			}
			catch (TimeoutException)
			{
				return DispatcherDeskResult<List<DispatcherBreak>>.Fail(DispatcherDeskErrors.BreakNotStartable, "Another break is being planned or started on the desk; try again.");
			}

			await _notifier.PublishDeskChangedAsync(departmentId);
			return DispatcherDeskResult<List<DispatcherBreak>>.Ok(planned.Created, planned.Warnings);
		}

		private async Task<(List<DispatcherBreak> Created, List<DispatcherDeskWarning> Warnings)> PlanDayUnderLockAsync(int departmentId, int shiftDayId,
			bool replace, string byUserId, List<DispatcherDutyWindow> roster, List<DispatcherBreakPlanEntry> plan, int minimum, DateTime now,
			CancellationToken cancellationToken)
		{
			var existing = await GetBreaksForShiftDayAsync(departmentId, shiftDayId);

			// With replace, the breaks still to come are cancelled; the replacement is worked out first and committed with the
			// cancellations, so a failed insert never leaves the day with the old plan cancelled and the new one missing.
			var superseded = replace
				? existing.Where(x => x.State == (int)DispatcherBreakStates.Scheduled && x.ScheduledStartOn > now).ToList()
				: new List<DispatcherBreak>();
			existing = existing.Except(superseded).ToList();

			var kept = existing.Where(x => x.State == (int)DispatcherBreakStates.Scheduled || x.State == (int)DispatcherBreakStates.Active ||
										   x.State == (int)DispatcherBreakStates.Completed).ToList();

			// Without replace, only dispatchers who have nothing planned yet get the plan.
			var dispatchers = roster.GroupBy(x => x.UserId, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
				.Where(x => replace || !kept.Any(b => string.Equals(b.UserId, x.UserId, StringComparison.OrdinalIgnoreCase)))
				.ToList();

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

			// In the caller's transaction (under the department lock): the cancellations and the new breaks commit together.
			foreach (var replaced in superseded)
			{
				var observed = PlanOf(replaced);
				replaced.State = (int)DispatcherBreakStates.Cancelled;
				replaced.UpdatedByUserId = byUserId;
				replaced.UpdatedOn = now;

				// A break started, moved or cancelled since it was read: the replacement was worked out around the old one, so stop.
				if (!await _breaksRepository.TryUpdateObservedAsync(replaced, observed, resetPlanAlerts: false, cancellationToken))
					throw new BreakPlanChangedException();
			}

			foreach (var row in created)
				await _breaksRepository.InsertAsync(row, cancellationToken, true);

			return (created, warnings);
		}

		/// <summary>A break the re-plan was about to cancel changed under it; the whole re-plan is rolled back.</summary>
		private sealed class BreakPlanChangedException : Exception { }

		/// <summary>
		/// Runs <paramref name="work"/> in the unit of work's transaction (joining the caller's, or owning one): an exception rolls an
		/// owned transaction back and is rethrown. Without a unit of work (tests) it just runs.
		/// </summary>
		private async Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken)
		{
			if (_unitOfWork == null)
				return await work();

			var owns = _unitOfWork.Transaction == null;
			await _unitOfWork.CreateOrGetConnectionAsync(cancellationToken);
			try
			{
				var result = await work();
				if (owns)
					_unitOfWork.CommitChanges();

				return result;
			}
			catch
			{
				if (owns)
					_unitOfWork.DiscardChanges();

				throw;
			}
		}

		public async Task<DispatcherDeskResult<DispatcherBreak>> StartBreakAsync(int departmentId, string userId, string dispatcherBreakId, int? breakType,
			int? durationMinutes, bool returnWork, string byUserId, CancellationToken cancellationToken = default)
		{
			var bySupervisor = !string.Equals(byUserId, userId, StringComparison.OrdinalIgnoreCase);
			if (bySupervisor && !await _presenceService.CanSuperviseAsync(departmentId, byUserId))
				return Fail(DispatcherDeskErrors.NotPermitted);

			// The coverage decision and the commit of the break run under a department lock, so two dispatchers starting a break at once
			// cannot both pass the minimum against the same desk snapshot.
			(DispatcherDeskResult<DispatcherBreak> Result, List<DispatcherQueueItem> HandBack) started;
			try
			{
				started = await InTransactionAsync(async () =>
				{
					if (_unitOfWork != null)
						await _breaksRepository.LockDepartmentForBreakStartAsync(departmentId, cancellationToken);

					return await StartBreakUnderLockAsync(departmentId, userId, dispatcherBreakId, breakType, durationMinutes, returnWork, byUserId,
						bySupervisor, cancellationToken);
				}, cancellationToken);
			}
			catch (TimeoutException)
			{
				return Fail(DispatcherDeskErrors.BreakNotStartable, "Another break is being started on the desk; try again.");
			}

			var result = started.Result;
			if (!result.Success)
				return result;

			// Only now that the dispatcher is on break: work handed back is offered to someone else, never straight back to them.
			var held = started.HandBack.Count(x => x.State == (int)DispatcherQueueItemStates.Accepted);
			var failed = await HandBackAsync(departmentId, userId, started.HandBack, cancellationToken);

			// "Went back to the queue" only once it all did; anything still held is handed back by the break timer.
			if (held > 0 && failed == 0)
				result.Warnings.Add(new DispatcherDeskWarning { Kind = DispatcherDeskWarningKinds.WorkReturnedToQueue, UserIds = { userId }, Available = held });

			await _notifier.PublishDeskChangedAsync(departmentId, userId);
			return result;
		}

		/// <summary>Returns the dispatcher's work to the queue, one item at a time; answers how many could not be released.</summary>
		private async Task<int> HandBackAsync(int departmentId, string userId, IEnumerable<DispatcherQueueItem> items, CancellationToken cancellationToken)
		{
			var failed = 0;
			foreach (var item in items ?? Enumerable.Empty<DispatcherQueueItem>())
			{
				try
				{
					var released = await _routingService.Value.ReleaseAsync(departmentId, item.DispatcherQueueItemId, userId, cancellationToken);

					// Refused because it is no longer theirs (finished, taken over) is not work they still hold. A lost version race
					// (ItemNotAvailable) proves nothing by itself: the item is checked, and still held by them counts as not handed back.
					if (released != null && !released.Success && released.Error != DispatcherDeskErrors.ItemNotFound &&
						released.Error != DispatcherDeskErrors.NotAssignedToYou &&
						(released.Error != DispatcherDeskErrors.ItemNotAvailable || await StillHeldByAsync(item.DispatcherQueueItemId, userId)))
						failed++;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Returning dispatcher work {item.DispatcherQueueItemId} to the queue for a break");
					failed++;
				}
			}

			return failed;
		}

		private async Task<bool> StillHeldByAsync(string dispatcherQueueItemId, string userId)
		{
			var current = await _itemsRepository.GetItemAsync(dispatcherQueueItemId);
			return current != null && current.State == (int)DispatcherQueueItemStates.Accepted &&
				   string.Equals(current.AssignedUserId, userId, StringComparison.OrdinalIgnoreCase);
		}

		private async Task<(DispatcherDeskResult<DispatcherBreak> Result, List<DispatcherQueueItem> HandBack)> StartBreakUnderLockAsync(int departmentId,
			string userId, string dispatcherBreakId, int? breakType, int? durationMinutes, bool returnWork, string byUserId, bool bySupervisor,
			CancellationToken cancellationToken)
		{
			var nothing = new List<DispatcherQueueItem>();
			(DispatcherDeskResult<DispatcherBreak>, List<DispatcherQueueItem>) Refuse(DispatcherDeskErrors error, string message = null) => (Fail(error, message), nothing);

			var presence = await _presencesRepository.GetForUserAsync(departmentId, userId);
			if (presence == null || !presence.IsSignedIn())
				return Refuse(DispatcherDeskErrors.NotSignedIn);

			if (presence.State == (int)DispatcherPresenceStates.OnBreak || await _breaksRepository.GetActiveForUserAsync(departmentId, userId) != null)
				return Refuse(DispatcherDeskErrors.AlreadyOnBreak);

			var now = UtcNow();
			DispatcherBreak row;
			DispatcherBreak observed = null;
			if (!string.IsNullOrWhiteSpace(dispatcherBreakId))
			{
				row = await GetBreakAsync(departmentId, dispatcherBreakId);
				if (row == null || !string.Equals(row.UserId, userId, StringComparison.OrdinalIgnoreCase))
					return Refuse(DispatcherDeskErrors.BreakNotFound);

				if (row.State != (int)DispatcherBreakStates.Scheduled)
					return Refuse(DispatcherDeskErrors.BreakNotStartable);

				// The plan as read: a supervisor's edit (length, start) saved after this read is not written over by the start.
				observed = PlanOf(row);

				if (durationMinutes.HasValue && durationMinutes.Value > 0 && durationMinutes.Value <= MaxBreakMinutes)
					row.DurationMinutes = durationMinutes.Value;
			}
			else
			{
				var minutes = durationMinutes ?? DefaultAdHocMinutes;
				if (minutes <= 0 || minutes > MaxBreakMinutes)
					return Refuse(DispatcherDeskErrors.InvalidInput);

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
				return Refuse(DispatcherDeskErrors.HoldingWork, held.Count.ToString());

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
					return (DispatcherDeskResult<DispatcherBreak>.Fail(DispatcherDeskErrors.CoverageTooLow, null, new List<DispatcherDeskWarning> { warning }), nothing);

				warnings.Add(warning);
			}

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
			else if (!await _breaksRepository.TryUpdateObservedAsync(row, observed, resetPlanAlerts: false, cancellationToken))
			{
				// Started, cancelled or re-planned since the read: the edited plan is reloaded before it is started.
				return Refuse(DispatcherDeskErrors.BreakNotStartable);
			}

			presence.State = (int)DispatcherPresenceStates.OnBreak;
			presence.ActiveBreakId = row.DispatcherBreakId;
			presence.StateChangedOn = now;
			presence.AwayReason = null;
			await _presencesRepository.SaveOrUpdateAsync(presence, cancellationToken, true);

			return (DispatcherDeskResult<DispatcherBreak>.Ok(row, warnings), mine);
		}

		public Task<DispatcherDeskResult<DispatcherBreak>> EndBreakAsync(int departmentId, string userId, string byUserId, DispatcherBreakEndReasons reason,
			CancellationToken cancellationToken = default) =>
			EndBreakAsync(departmentId, userId, byUserId, reason, null, cancellationToken);

		/// <param name="expectedBreakId">
		/// The break the caller evaluated (the auto-return timer): only that break is ended, and only while it is still active. A
		/// dispatcher who came back and started another break in the meantime keeps the new one.
		/// </param>
		private async Task<DispatcherDeskResult<DispatcherBreak>> EndBreakAsync(int departmentId, string userId, string byUserId, DispatcherBreakEndReasons reason,
			string expectedBreakId, CancellationToken cancellationToken)
		{
			if (reason == DispatcherBreakEndReasons.Supervisor && !await _presenceService.CanSuperviseAsync(departmentId, byUserId))
				return Fail(DispatcherDeskErrors.NotPermitted);

			if (reason == DispatcherBreakEndReasons.Returned && !string.Equals(byUserId, userId, StringComparison.OrdinalIgnoreCase) &&
				!await _presenceService.CanSuperviseAsync(departmentId, byUserId))
				return Fail(DispatcherDeskErrors.NotPermitted);

			DispatcherBreak row;
			if (expectedBreakId != null)
			{
				row = await GetBreakAsync(departmentId, expectedBreakId);
				if (row == null || row.State != (int)DispatcherBreakStates.Active || !string.Equals(row.UserId, userId, StringComparison.OrdinalIgnoreCase))
					return Fail(DispatcherDeskErrors.NotOnBreak);
			}
			else
			{
				row = await _breaksRepository.GetActiveForUserAsync(departmentId, userId);
			}

			var presence = await _presencesRepository.GetForUserAsync(departmentId, userId);
			if (row == null && (presence == null || presence.State != (int)DispatcherPresenceStates.OnBreak))
				return Fail(DispatcherDeskErrors.NotOnBreak);

			// The presence follows the evaluated break only while it is that break the dispatcher is on.
			if (expectedBreakId != null && presence != null && !string.IsNullOrWhiteSpace(presence.ActiveBreakId) &&
				!string.Equals(presence.ActiveBreakId, expectedBreakId, StringComparison.OrdinalIgnoreCase))
				presence = null;

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
				// Only the return-from-break fields, and only while the row is still the session, state and break read above: a sign-off
				// (or a later break) committed meanwhile keeps its own state instead of being written over as Available.
				var self = string.Equals(byUserId, userId, StringComparison.OrdinalIgnoreCase);
				await _presencesRepository.TryReturnFromBreakAsync(presence, now, self ? now : (DateTime?)null, cancellationToken);
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
			var run = new TimerRun(this);
			var changes = 0;
			var rows = (await _breaksRepository.GetForTimersAsync(utcNow.AddMinutes(120), utcNow.AddHours(-MissedAfterHours - 1)) ?? Enumerable.Empty<DispatcherBreak>()).ToList();

			// Catch-up: planned breaks the timer window has already passed by (the worker was down for a day) are still marked missed.
			var overdue = await _breaksRepository.GetOverdueScheduledAsync(utcNow.AddHours(-MissedAfterHours - 1), MissedCatchUpBatch) ?? Enumerable.Empty<DispatcherBreak>();
			rows.AddRange(overdue.Where(x => !rows.Any(r => string.Equals(r.DispatcherBreakId, x.DispatcherBreakId, StringComparison.OrdinalIgnoreCase))).ToList());

			foreach (var row in rows)
			{
				if (cancellationToken.IsCancellationRequested)
					break;

				try
				{
					var config = await run.ConfigAsync(row.DepartmentId);
					if (row.State == (int)DispatcherBreakStates.Scheduled)
						changes += await ProcessScheduledAsync(row, config, run, utcNow, cancellationToken);
					else if (row.State == (int)DispatcherBreakStates.Active)
						changes += await ProcessActiveAsync(row, config, run, utcNow, cancellationToken);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Dispatcher break timer for {row.DispatcherBreakId}");
				}
			}

			return changes;
		}

		/// <summary>Per-pass lookups shared by every break the timer looks at.</summary>
		private sealed class TimerRun
		{
			private readonly DispatcherBreaksService _service;
			private readonly Dictionary<int, DispatcherDeskConfig> _configs = new Dictionary<int, DispatcherDeskConfig>();
			private readonly Dictionary<int, bool> _shiftsOnDesk = new Dictionary<int, bool>();
			private readonly Dictionary<int, List<DispatcherQueueItem>> _openItems = new Dictionary<int, List<DispatcherQueueItem>>();

			public TimerRun(DispatcherBreaksService service) { _service = service; }

			public async Task<DispatcherDeskConfig> ConfigAsync(int departmentId)
			{
				if (!_configs.TryGetValue(departmentId, out var config))
					_configs[departmentId] = config = await _service._configService.GetConfigAsync(departmentId);
				return config;
			}

			/// <summary>
			/// Whether the break's shift still has its desk settings. Breaks are only planned on desk shifts (duty windows come from
			/// enabled settings), and deleting a shift deletes them, so a shift-linked plan without them belongs to a deleted shift.
			/// </summary>
			public async Task<bool> ShiftOnDeskAsync(int departmentId, int shiftId)
			{
				if (!_shiftsOnDesk.TryGetValue(shiftId, out var onDesk))
				{
					var settings = await _service._shiftDispatchersService.GetSettingsForShiftAsync(shiftId);
					_shiftsOnDesk[shiftId] = onDesk = settings != null && settings.DepartmentId == departmentId;
				}

				return onDesk;
			}

			public async Task<List<DispatcherQueueItem>> OpenItemsAsync(int departmentId)
			{
				if (!_openItems.TryGetValue(departmentId, out var items))
					_openItems[departmentId] = items = (await _service._itemsRepository.GetOpenForDepartmentAsync(departmentId) ?? Enumerable.Empty<DispatcherQueueItem>()).ToList();
				return items;
			}
		}

		private async Task<int> ProcessScheduledAsync(DispatcherBreak row, DispatcherDeskConfig config, TimerRun run, DateTime now, CancellationToken cancellationToken)
		{
			if (!row.ScheduledStartOn.HasValue)
				return 0;

			// Every write below is conditional on the plan this pass read: a break moved, started or cancelled since is left alone.
			var observed = PlanOf(row);
			var start = row.ScheduledStartOn.Value;
			var presence = await _presencesRepository.GetForUserAsync(row.DepartmentId, row.UserId);
			var signedIn = presence != null && presence.IsSignedIn();

			// Missed: the planned break is over and the dispatcher is no longer on the desk (or it is a day old).
			if (now > start.AddHours(MissedAfterHours) || now > start.AddMinutes(row.DurationMinutes) && !signedIn)
			{
				row.State = (int)DispatcherBreakStates.Missed;
				row.UpdatedOn = now;
				if (await _breaksRepository.TryUpdateObservedAsync(row, observed, resetPlanAlerts: false, cancellationToken))
				{
					await _notifier.PublishDeskChangedAsync(row.DepartmentId, row.UserId);
					return 1;
				}

				return 0;
			}

			if (!config.Enabled || !signedIn)
				return 0;

			// A plan left over from a deleted shift reminds nobody (the row stays for the record and is marked missed later).
			if (row.ShiftId.HasValue && !await run.ShiftOnDeskAsync(row.DepartmentId, row.ShiftId.Value))
				return 0;

			var reminder = config.EffectiveBreakReminderMinutes();
			if (reminder > 0 && !row.ReminderSentOn.HasValue && start > now && start <= now.AddMinutes(reminder))
			{
				if (await _breaksRepository.TryMarkAlertAsync(observed, nameof(DispatcherBreak.ReminderSentOn), now, cancellationToken) &&
					await DeliverClaimedAlertAsync(row, nameof(DispatcherBreak.ReminderSentOn), now, start.AddMinutes(-reminder),
						() => _notifier.NotifyBreakReminderAsync(row, due: false, cancellationToken)))
					return 1;
			}

			if (!row.DueAlertSentOn.HasValue && start <= now && start > now.AddMinutes(-60))
			{
				if (await _breaksRepository.TryMarkAlertAsync(observed, nameof(DispatcherBreak.DueAlertSentOn), now, cancellationToken) &&
					await DeliverClaimedAlertAsync(row, nameof(DispatcherBreak.DueAlertSentOn), now, start,
						() => _notifier.NotifyBreakReminderAsync(row, due: true, cancellationToken)))
				{
					await _notifier.PublishDeskChangedAsync(row.DepartmentId, row.UserId);
					return 1;
				}
			}

			return 0;
		}

		/// <summary>How long after an alert fell due a claim whose push was not accepted is given back for the next pass to send again.</summary>
		public const int AlertRetryMinutes = 15;

		/// <summary>
		/// Sends an alert this pass has claimed (its stamp column is set). A delivery that did not accept it (no push sent: the provider
		/// refused, failed or the pass was cancelled) gives the claim back so a later pass sends it again, for up to
		/// <see cref="AlertRetryMinutes"/> after <paramref name="dueSinceUtc"/>; after that the alert is left as tried. An accepted one keeps
		/// its stamp and is never sent again.
		/// </summary>
		private async Task<bool> DeliverClaimedAlertAsync(DispatcherBreak row, string column, DateTime stampUtc, DateTime dueSinceUtc, Func<Task<bool>> send)
		{
			bool delivered;
			try
			{
				delivered = await send();
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Dispatcher break alert {column} for {row.DispatcherBreakId}");
				delivered = false;
			}

			if (delivered)
				return true;

			if (stampUtc < dueSinceUtc.AddMinutes(AlertRetryMinutes))
			{
				// The claim is given back whatever happens to the pass's own token: the next pass must be able to retry.
				await _breaksRepository.TryClearAlertAsync(row.DispatcherBreakId, column, stampUtc, CancellationToken.None);
			}
			else
			{
				Logging.LogError($"Dispatcher break alert {column} for {row.DispatcherBreakId} was not delivered within {AlertRetryMinutes} minutes; not retried.");
			}

			return false;
		}

		private async Task<int> ProcessActiveAsync(DispatcherBreak row, DispatcherDeskConfig config, TimerRun run, DateTime now, CancellationToken cancellationToken)
		{
			// Breaks start only in a signed-in session and sign-off ends them; one still active while its dispatcher is signed off is a
			// sign-off's break whose close failed after the session had ended. It is closed as ended by that sign-off.
			var presence = await _presencesRepository.GetForUserAsync(row.DepartmentId, row.UserId);
			if (presence == null || !presence.IsSignedIn())
				return await CloseSignedOffBreakAsync(row, presence, now, cancellationToken) ? 1 : 0;

			var changes = await HandBackLeftoverWorkAsync(row, run, cancellationToken);

			var due = row.ExpectedReturnOn ?? row.StartedOn?.AddMinutes(row.DurationMinutes);
			if (!due.HasValue || due.Value > now)
				return changes;

			// The alert stamps are conditional on the break still being the active one this pass read (not ended since).
			var observed = PlanOf(row);

			if (!row.ReturnAlertSentOn.HasValue && await _breaksRepository.TryMarkAlertAsync(observed, nameof(DispatcherBreak.ReturnAlertSentOn), now, cancellationToken) &&
				await DeliverClaimedAlertAsync(row, nameof(DispatcherBreak.ReturnAlertSentOn), now, due.Value,
					() => _notifier.NotifyBreakOverAsync(row, null, autoReturned: false, cancellationToken)))
			{
				await _notifier.PublishDeskChangedAsync(row.DepartmentId, row.UserId);
				changes++;
			}

			var autoReturn = config.EffectiveAutoReturnAfterMinutes();
			if (autoReturn > 0 && now >= due.Value.AddMinutes(autoReturn))
			{
				// This break, not whichever the dispatcher is on by now.
				var ended = await EndBreakAsync(row.DepartmentId, row.UserId, null, DispatcherBreakEndReasons.AutoReturn, row.DispatcherBreakId, cancellationToken);
				if (ended.Success)
				{
					await _notifier.NotifyBreakOverAsync(row, null, autoReturned: true, cancellationToken);
					if (config.NotifySupervisorsOnOverdueBreak)
						await NotifySupervisorsAsync(row, autoReturned: true, now, cancellationToken);
					changes++;
				}

				return changes;
			}

			var grace = autoReturn > 0 ? Math.Min(OverdueGraceMinutes, autoReturn) : OverdueGraceMinutes;
			if (config.NotifySupervisorsOnOverdueBreak && !row.OverdueAlertSentOn.HasValue && now >= due.Value.AddMinutes(grace) && (autoReturn == 0 || grace < autoReturn))
			{
				if (await _breaksRepository.TryMarkAlertAsync(observed, nameof(DispatcherBreak.OverdueAlertSentOn), now, cancellationToken) &&
					await DeliverClaimedAlertAsync(row, nameof(DispatcherBreak.OverdueAlertSentOn), now, due.Value.AddMinutes(grace),
						() => NotifySupervisorsAsync(row, autoReturned: false, now, cancellationToken)))
				{
					await _notifier.PublishDeskChangedAsync(row.DepartmentId, row.UserId);
					changes++;
				}
			}

			return changes;
		}

		/// <summary>
		/// Closes an active break whose dispatcher is signed off (see <see cref="ProcessActiveAsync"/>), as ended by the sign-off: at the
		/// session's sign-off time when that is known and after the start, otherwise now. Conditional on the break still being active.
		/// </summary>
		private async Task<bool> CloseSignedOffBreakAsync(DispatcherBreak row, DispatcherPresence presence, DateTime now, CancellationToken cancellationToken)
		{
			var signedOffOn = presence?.StateChangedOn;
			row.State = (int)DispatcherBreakStates.Completed;
			row.EndedOn = signedOffOn.HasValue && row.StartedOn.HasValue && signedOffOn.Value >= row.StartedOn.Value && signedOffOn.Value <= now ? signedOffOn.Value : now;
			row.EndReason = (int)DispatcherBreakEndReasons.SignedOff;
			row.UpdatedOn = now;

			if (!await _breaksRepository.TryTransitionAsync(row, (int)DispatcherBreakStates.Active, cancellationToken))
				return false;

			await _notifier.PublishDeskChangedAsync(row.DepartmentId, row.UserId);
			return true;
		}

		/// <summary>
		/// Supervisors only: with nobody to tell, nothing is sent (the dispatcher has their own alert) and the alert counts as handled.
		/// True when it reached at least one supervisor (or there was nobody to tell).
		/// </summary>
		private async Task<bool> NotifySupervisorsAsync(DispatcherBreak row, bool autoReturned, DateTime now, CancellationToken cancellationToken)
		{
			var supervisors = await _presenceService.GetSupervisorUserIdsAsync(row.DepartmentId, now);
			if (supervisors == null || supervisors.Count == 0)
				return true;

			return await _notifier.NotifyBreakOverAsync(row, supervisors, autoReturned, cancellationToken);
		}

		/// <summary>
		/// Work the dispatcher still holds while on this break (a hand-back at the start of the break that failed) goes back to the
		/// queue. A dispatcher on break cannot accept work, so anything they hold is left over.
		/// </summary>
		private async Task<int> HandBackLeftoverWorkAsync(DispatcherBreak row, TimerRun run, CancellationToken cancellationToken)
		{
			var leftover = (await run.OpenItemsAsync(row.DepartmentId))
				.Where(x => x.State == (int)DispatcherQueueItemStates.Accepted && string.Equals(x.AssignedUserId, row.UserId, StringComparison.OrdinalIgnoreCase))
				.ToList();
			if (leftover.Count == 0)
				return 0;

			// Still on this break right now (not back and holding new work).
			var presence = await _presencesRepository.GetForUserAsync(row.DepartmentId, row.UserId);
			if (presence == null || presence.State != (int)DispatcherPresenceStates.OnBreak ||
				!string.IsNullOrWhiteSpace(presence.ActiveBreakId) && !string.Equals(presence.ActiveBreakId, row.DispatcherBreakId, StringComparison.OrdinalIgnoreCase))
				return 0;

			var failed = await HandBackAsync(row.DepartmentId, row.UserId, leftover, cancellationToken);
			if (failed == leftover.Count)
				return 0;

			await _notifier.PublishDeskChangedAsync(row.DepartmentId, row.UserId);
			return leftover.Count - failed;
		}

		/// <summary>The state and plan a write is conditional on (see <see cref="IDispatcherBreaksRepository.TryUpdateObservedAsync"/>).</summary>
		private static DispatcherBreak PlanOf(DispatcherBreak row) => new DispatcherBreak
		{
			DispatcherBreakId = row.DispatcherBreakId,
			State = row.State,
			DurationMinutes = row.DurationMinutes,
			ScheduledStartOn = row.ScheduledStartOn
		};

		private async Task<bool> AllowsSelfScheduledBreaksAsync(int departmentId, int shiftId)
		{
			var settings = await _shiftDispatchersService.GetSettingsForShiftAsync(shiftId);
			return settings != null && settings.DepartmentId == departmentId && settings.IsEnabled && settings.AllowSelfScheduledBreaks;
		}

		private static DispatcherDeskResult<DispatcherBreak> Fail(DispatcherDeskErrors error, string message = null) =>
			DispatcherDeskResult<DispatcherBreak>.Fail(error, message);

		private static string Truncate(string value, int length) =>
			string.IsNullOrEmpty(value) || value.Length <= length ? value : value.Substring(0, length);
	}
}
