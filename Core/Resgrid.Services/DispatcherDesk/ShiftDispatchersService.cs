using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Npgsql;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// The dispatch roster of shifts (M0324): settings per shift, standing and per-day dispatchers, and the duty windows the desk
	/// routes on. Authorization (who may edit a roster) is the caller's: controllers check the shift management scope.
	/// </summary>
	public class ShiftDispatchersService : IShiftDispatchersService
	{
		/// <summary>How far back a running shift day can have started (the longest shift the UI allows is a few days).</summary>
		private const int LookBackDays = 7;

		private readonly IShiftDispatchSettingsRepository _settingsRepository;
		private readonly IShiftDispatchersRepository _dispatchersRepository;
		private readonly IDispatcherBreaksRepository _breaksRepository;
		private readonly IShiftsService _shiftsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDispatchAccessService _dispatchAccessService;
		private readonly IEventAggregator _eventAggregator;
		private readonly IUnitOfWork _unitOfWork;

		public ShiftDispatchersService(IShiftDispatchSettingsRepository settingsRepository, IShiftDispatchersRepository dispatchersRepository,
			IDispatcherBreaksRepository breaksRepository, IShiftsService shiftsService, IDepartmentsService departmentsService,
			IDispatchAccessService dispatchAccessService, IEventAggregator eventAggregator, IUnitOfWork unitOfWork = null)
		{
			_unitOfWork = unitOfWork;
			_settingsRepository = settingsRepository;
			_dispatchersRepository = dispatchersRepository;
			_breaksRepository = breaksRepository;
			_shiftsService = shiftsService;
			_departmentsService = departmentsService;
			_dispatchAccessService = dispatchAccessService;
			_eventAggregator = eventAggregator;
		}

		/// <summary>Clock, replaceable in tests.</summary>
		public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

		public Task<ShiftDispatchSetting> GetSettingsForShiftAsync(int shiftId) => _settingsRepository.GetByShiftIdAsync(shiftId);

		public async Task<List<ShiftDispatchSetting>> GetSettingsForDepartmentAsync(int departmentId) =>
			(await _settingsRepository.GetByDepartmentIdAsync(departmentId) ?? Enumerable.Empty<ShiftDispatchSetting>()).ToList();

		public async Task<DispatcherDeskResult<ShiftDispatchSetting>> SaveSettingsAsync(int departmentId, ShiftDispatchSetting settings, string userId,
			CancellationToken cancellationToken = default)
		{
			if (settings == null)
				return DispatcherDeskResult<ShiftDispatchSetting>.Fail(DispatcherDeskErrors.InvalidInput);

			var shift = await _shiftsService.GetShiftByIdAsync(settings.ShiftId);
			if (shift == null || shift.DepartmentId != departmentId)
				return DispatcherDeskResult<ShiftDispatchSetting>.Fail(DispatcherDeskErrors.ShiftNotFound);

			var row = await _settingsRepository.GetByShiftIdAsync(settings.ShiftId) ?? new ShiftDispatchSetting
			{
				DepartmentId = departmentId,
				ShiftId = settings.ShiftId
			};

			row.IsEnabled = settings.IsEnabled;
			row.IsDispatchOnly = settings.IsDispatchOnly;
			row.RequiredDispatchers = Math.Max(0, Math.Min(settings.RequiredDispatchers, 100));
			row.MinimumOnDesk = Math.Max(0, Math.Min(settings.MinimumOnDesk, 100));
			row.AllowSelfScheduledBreaks = settings.AllowSelfScheduledBreaks;
			row.SetBreakPlan(settings.GetBreakPlan()?.Take(10).Select(x => new DispatcherBreakPlanEntry
			{
				BreakType = Enum.IsDefined(typeof(DispatcherBreakTypes), x.BreakType) ? x.BreakType : (int)DispatcherBreakTypes.Break,
				DurationMinutes = Math.Min(x.DurationMinutes, 240),
				TargetOffsetMinutes = x.TargetOffsetMinutes.HasValue ? Math.Max(0, x.TargetOffsetMinutes.Value) : (int?)null
			}));
			row.UpdatedByUserId = userId;
			row.UpdatedOn = UtcNow();

			var saved = await _settingsRepository.SaveOrUpdateAsync(row, cancellationToken, true);
			await PublishDeskChangedAsync(departmentId);

			return DispatcherDeskResult<ShiftDispatchSetting>.Ok(saved);
		}

		public async Task<List<ShiftDispatcher>> GetRosterForShiftAsync(int shiftId) =>
			(await _dispatchersRepository.GetByShiftIdAsync(shiftId) ?? Enumerable.Empty<ShiftDispatcher>()).ToList();

		public Task<DispatcherDeskResult<List<ShiftDispatcher>>> SetStandingRosterAsync(int departmentId, int shiftId,
			IEnumerable<(string UserId, bool IsSupervisor)> dispatchers, string byUserId, CancellationToken cancellationToken = default)
		{
			var wanted = Distinct(dispatchers);
			return SaveStandingRosterAsync(departmentId, shiftId, wanted.Select(x => x.UserId), (stored, eligible) => wanted, byUserId, cancellationToken);
		}

		public Task<DispatcherDeskResult<List<ShiftDispatcher>>> UpdateStandingRosterAsync(int departmentId, int shiftId,
			IEnumerable<(string UserId, bool IsSupervisor)> previous, IEnumerable<(string UserId, bool IsSupervisor)> wanted, string byUserId,
			CancellationToken cancellationToken = default)
		{
			var was = Distinct(previous);
			var now = Distinct(wanted);
			return SaveStandingRosterAsync(departmentId, shiftId, now.Select(x => x.UserId),
				(stored, eligible) => MergeStandingRoster(stored, was, now, eligible), byUserId, cancellationToken);
		}

		/// <summary>
		/// The standing roster after an editor's changes (see <see cref="UpdateStandingRosterAsync"/>): the stored rows, less the
		/// people the editor took off and the ones not shown who can no longer dispatch, plus the people it put on; supervisor
		/// flags as the editor set them where it changed them, as stored otherwise.
		/// </summary>
		internal static List<(string UserId, bool IsSupervisor)> MergeStandingRoster(IEnumerable<ShiftDispatcher> stored,
			List<(string UserId, bool IsSupervisor)> previous, List<(string UserId, bool IsSupervisor)> wanted, Func<string, bool> isEligible)
		{
			var was = previous.ToDictionary(x => x.UserId, x => x.IsSupervisor, StringComparer.OrdinalIgnoreCase);
			var now = wanted.ToDictionary(x => x.UserId, x => x.IsSupervisor, StringComparer.OrdinalIgnoreCase);
			var result = new List<(string UserId, bool IsSupervisor)>();

			foreach (var row in stored.Where(x => !x.ShiftDayId.HasValue && x.IsActive()))
			{
				if (result.Any(x => string.Equals(x.UserId, row.UserId, StringComparison.OrdinalIgnoreCase)))
					continue;

				if (now.TryGetValue(row.UserId, out var supervisor))
				{
					// Kept by the editor: its flag only where it changed it (someone else may have changed it since).
					var changed = !was.TryGetValue(row.UserId, out var before) || before != supervisor;
					result.Add((row.UserId, changed ? supervisor : row.IsSupervisor));
				}
				else if (was.ContainsKey(row.UserId))
				{
					// Taken off by the editor.
				}
				else if (isEligible(row.UserId))
				{
					// Not on the editor's page (added since, or not shown): stays.
					result.Add((row.UserId, row.IsSupervisor));
				}
			}

			foreach (var person in wanted.Where(x => !result.Any(r => string.Equals(r.UserId, x.UserId, StringComparison.OrdinalIgnoreCase)) &&
													 !was.ContainsKey(x.UserId)))
				result.Add(person);

			return result;
		}

		/// <summary>
		/// Writes the standing roster <paramref name="desiredFrom"/> builds from the stored rows, in one transaction: a failed
		/// write leaves the roster as it was, and the roster event goes out after the commit. Two editors saving the same new
		/// dispatcher at once meet the standing-roster unique index; the loser rereads and saves again.
		/// </summary>
		private async Task<DispatcherDeskResult<List<ShiftDispatcher>>> SaveStandingRosterAsync(int departmentId, int shiftId, IEnumerable<string> requested,
			Func<List<ShiftDispatcher>, Func<string, bool>, List<(string UserId, bool IsSupervisor)>> desiredFrom, string byUserId,
			CancellationToken cancellationToken)
		{
			var shift = await _shiftsService.GetShiftByIdAsync(shiftId);
			if (shift == null || shift.DepartmentId != departmentId)
				return DispatcherDeskResult<List<ShiftDispatcher>>.Fail(DispatcherDeskErrors.ShiftNotFound);

			// Eligibility is read before the transaction opens (it reads other tables through the same unit of work).
			var before = await GetStandingRowsAsync(shiftId);
			var eligible = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
			foreach (var userId in before.Select(x => x.UserId).Concat(requested ?? Enumerable.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase))
				eligible[userId] = await IsEligibleDispatcherAsync(departmentId, userId);

			// Someone added by another editor since the read above was checked when they were added.
			bool isEligible(string userId) => !eligible.TryGetValue(userId, out var allowed) || allowed;

			for (var attempt = 1; ; attempt++)
			{
				var owns = _unitOfWork != null && _unitOfWork.Transaction == null;
				if (_unitOfWork != null)
					await _unitOfWork.CreateOrGetConnectionAsync(cancellationToken);

				try
				{
					var existing = await GetStandingRowsAsync(shiftId);
					var wanted = desiredFrom(existing, isEligible);

					// Only people joining are checked: someone already on the roster who lost the permission is kept when the
					// caller keeps them (the editor shows them; sign-in and routing refuse them).
					var refused = wanted.FirstOrDefault(x => !existing.Any(e => string.Equals(e.UserId, x.UserId, StringComparison.OrdinalIgnoreCase)) &&
															 !isEligible(x.UserId));
					if (refused.UserId != null)
					{
						if (owns)
							_unitOfWork.DiscardChanges();
						return DispatcherDeskResult<List<ShiftDispatcher>>.Fail(DispatcherDeskErrors.NotADispatcher, refused.UserId);
					}

					var result = await WriteStandingRosterAsync(departmentId, shiftId, existing, wanted, byUserId, cancellationToken);

					if (owns)
						_unitOfWork.CommitChanges();

					await PublishDeskChangedAsync(departmentId);
					return DispatcherDeskResult<List<ShiftDispatcher>>.Ok(result);
				}
				catch (Exception ex) when (attempt == 1 && (owns || _unitOfWork == null) && IsUniqueViolation(ex))
				{
					if (owns)
						_unitOfWork.DiscardChanges();
				}
				catch
				{
					if (owns)
						_unitOfWork.DiscardChanges();
					throw;
				}
			}
		}

		private async Task<List<ShiftDispatcher>> WriteStandingRosterAsync(int departmentId, int shiftId, List<ShiftDispatcher> existing,
			List<(string UserId, bool IsSupervisor)> wanted, string byUserId, CancellationToken cancellationToken)
		{
			var now = UtcNow();

			foreach (var row in existing.Where(x => !wanted.Any(w => string.Equals(w.UserId, x.UserId, StringComparison.OrdinalIgnoreCase))))
				await _dispatchersRepository.DeleteAsync(row, cancellationToken);

			var result = new List<ShiftDispatcher>();
			foreach (var person in wanted)
			{
				var row = existing.FirstOrDefault(x => string.Equals(x.UserId, person.UserId, StringComparison.OrdinalIgnoreCase));
				if (row == null)
				{
					row = new ShiftDispatcher
					{
						DepartmentId = departmentId,
						ShiftId = shiftId,
						UserId = person.UserId,
						Source = (int)ShiftDispatcherSources.Assigned,
						Status = (int)ShiftDispatcherStatuses.Active,
						IsSupervisor = person.IsSupervisor,
						AddedByUserId = byUserId,
						AddedOn = now
					};
					result.Add(await _dispatchersRepository.SaveOrUpdateAsync(row, cancellationToken, true));
				}
				else
				{
					if (row.IsSupervisor != person.IsSupervisor || row.Status != (int)ShiftDispatcherStatuses.Active)
					{
						row.IsSupervisor = person.IsSupervisor;
						row.Status = (int)ShiftDispatcherStatuses.Active;
						row = await _dispatchersRepository.SaveOrUpdateAsync(row, cancellationToken, true);
					}

					result.Add(row);
				}
			}

			return result;
		}

		private async Task<List<ShiftDispatcher>> GetStandingRowsAsync(int shiftId) =>
			(await _dispatchersRepository.GetByShiftIdAsync(shiftId) ?? Enumerable.Empty<ShiftDispatcher>()).Where(x => !x.ShiftDayId.HasValue).ToList();

		private static List<(string UserId, bool IsSupervisor)> Distinct(IEnumerable<(string UserId, bool IsSupervisor)> people) =>
			(people ?? Enumerable.Empty<(string UserId, bool IsSupervisor)>())
			.Where(x => !string.IsNullOrWhiteSpace(x.UserId))
			.GroupBy(x => x.UserId, StringComparer.OrdinalIgnoreCase)
			.Select(g => (UserId: g.Key, IsSupervisor: g.Any(x => x.IsSupervisor)))
			.ToList();

		private static bool IsUniqueViolation(Exception ex)
		{
			for (var current = ex; current != null; current = current.InnerException)
			{
				if (current is PostgresException postgres)
					return postgres.SqlState == "23505";

				if (current is SqlException sql)
					return sql.Number == 2601 || sql.Number == 2627;
			}

			return false;
		}

		public async Task<DispatcherDeskResult<ShiftDispatcher>> AssignToDayAsync(int departmentId, int shiftDayId, string userId, bool isSupervisor,
			string byUserId, CancellationToken cancellationToken = default)
		{
			var (day, settings, error) = await LoadDayAsync(departmentId, shiftDayId);
			if (error != DispatcherDeskErrors.None)
				return DispatcherDeskResult<ShiftDispatcher>.Fail(error);

			if (!await IsEligibleDispatcherAsync(departmentId, userId))
				return DispatcherDeskResult<ShiftDispatcher>.Fail(DispatcherDeskErrors.NotADispatcher);

			var row = await GetDayRowAsync(day, userId) ?? new ShiftDispatcher
			{
				DepartmentId = departmentId,
				ShiftId = day.ShiftId,
				ShiftDayId = day.ShiftDayId,
				UserId = userId,
				AddedOn = UtcNow()
			};

			row.Source = (int)ShiftDispatcherSources.Assigned;
			row.Status = (int)ShiftDispatcherStatuses.Active;
			row.IsSupervisor = isSupervisor;
			row.AddedByUserId = byUserId;
			row.ReviewedByUserId = byUserId;
			row.ReviewedOn = UtcNow();

			var saved = await _dispatchersRepository.SaveOrUpdateAsync(row, cancellationToken, true);
			await PublishDeskChangedAsync(departmentId, userId);

			return DispatcherDeskResult<ShiftDispatcher>.Ok(saved);
		}

		public async Task<DispatcherDeskResult<ShiftDispatcher>> SignupForDayAsync(int departmentId, int shiftDayId, string userId,
			CancellationToken cancellationToken = default)
		{
			var (day, settings, error) = await LoadDayAsync(departmentId, shiftDayId);
			if (error != DispatcherDeskErrors.None)
				return DispatcherDeskResult<ShiftDispatcher>.Fail(error);

			// Only a Signup shift takes self-signups; an Assigned shift's roster is the shift manager's.
			if (day.AssignmentType != (int)ShiftAssignmentTypes.Signup)
				return DispatcherDeskResult<ShiftDispatcher>.Fail(DispatcherDeskErrors.NotPermitted);

			if (!await IsEligibleDispatcherAsync(departmentId, userId))
				return DispatcherDeskResult<ShiftDispatcher>.Fail(DispatcherDeskErrors.NotADispatcher);

			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
			var (_, endLocal) = ShiftTimeWindow.GetWindow(day.Day, day.StartTime, day.EndTime, day.Hours);
			if (DispatcherDutyCalculator.ToUtc(endLocal, department) <= UtcNow())
				return DispatcherDeskResult<ShiftDispatcher>.Fail(DispatcherDeskErrors.InvalidInput, "The shift day is over.");

			var roster = await GetDayRosterAsync(departmentId, shiftDayId);
			if (roster.Any(x => string.Equals(x.UserId, userId, StringComparison.OrdinalIgnoreCase)))
				return DispatcherDeskResult<ShiftDispatcher>.Fail(DispatcherDeskErrors.AlreadyOnRoster);

			var row = await GetDayRowAsync(day, userId) ?? new ShiftDispatcher
			{
				DepartmentId = departmentId,
				ShiftId = day.ShiftId,
				ShiftDayId = day.ShiftDayId,
				UserId = userId
			};

			row.Source = (int)ShiftDispatcherSources.Signup;
			row.Status = day.RequireApproval.GetValueOrDefault() ? (int)ShiftDispatcherStatuses.PendingApproval : (int)ShiftDispatcherStatuses.Active;
			row.IsSupervisor = false;
			row.AddedByUserId = userId;
			row.AddedOn = UtcNow();
			row.ReviewedByUserId = null;
			row.ReviewedOn = null;

			var saved = await _dispatchersRepository.SaveOrUpdateAsync(row, cancellationToken, true);
			await PublishDeskChangedAsync(departmentId, userId);

			return DispatcherDeskResult<ShiftDispatcher>.Ok(saved);
		}

		public Task<DispatcherDeskResult<bool>> RemoveFromDayAsync(int departmentId, int shiftDayId, string userId, string byUserId, string note,
			CancellationToken cancellationToken = default) =>
			RemoveFromDayAsync(departmentId, shiftDayId, userId, byUserId, note, null, cancellationToken);

		public async Task<DispatcherDeskResult<bool>> RemoveFromDayAsync(int departmentId, int shiftDayId, string userId, string byUserId, string note,
			int? requiredSource, CancellationToken cancellationToken = default)
		{
			var (day, _, error) = await LoadDayAsync(departmentId, shiftDayId, requireEnabled: false);
			if (error != DispatcherDeskErrors.None)
				return DispatcherDeskResult<bool>.Fail(error);

			// The denial and the break cancellations commit together under the department's break lock (the one day plans and break
			// starts take): a plan cannot read the roster before the denial and insert a break after the cleanup, and a re-assignment
			// and its breaks land only after this removal, so the cleanup cancels the breaks of the roster entry it denies and no others.
			var owns = _unitOfWork != null && _unitOfWork.Transaction == null;
			if (_unitOfWork != null)
				await _unitOfWork.CreateOrGetConnectionAsync(cancellationToken);

			try
			{
				if (_unitOfWork != null)
					await _breaksRepository.LockDepartmentForBreakStartAsync(departmentId, cancellationToken);

				var removed = await RemoveFromDayUnderLockAsync(departmentId, day, shiftDayId, userId, byUserId, note, requiredSource, cancellationToken);
				if (owns)
				{
					if (removed.Success)
						_unitOfWork.CommitChanges();
					else
						_unitOfWork.DiscardChanges();
				}

				if (removed.Success && removed.Value)
					await PublishDeskChangedAsync(departmentId, userId);

				return removed;
			}
			catch (TimeoutException) when (owns || _unitOfWork == null)
			{
				if (owns)
					_unitOfWork.DiscardChanges();

				return DispatcherDeskResult<bool>.Fail(DispatcherDeskErrors.BreakNotStartable, "Breaks are being planned or started on the desk; try again.");
			}
			catch
			{
				if (owns)
					_unitOfWork.DiscardChanges();
				throw;
			}
		}

		private async Task<DispatcherDeskResult<bool>> RemoveFromDayUnderLockAsync(int departmentId, ShiftDayTimesRow day, int shiftDayId, string userId,
			string byUserId, string note, int? requiredSource, CancellationToken cancellationToken)
		{
			var now = UtcNow();
			var row = await GetDayRowAsync(day, userId);

			// A withdrawal checked the row's source on a roster read earlier; the row written here must still be that kind (a
			// manager may have turned the signup into an assignment since).
			if (requiredSource.HasValue && (row == null || row.Source != requiredSource.Value))
				return DispatcherDeskResult<bool>.Fail(DispatcherDeskErrors.NotPermitted);

			if (row == null)
			{
				var standing = (await _dispatchersRepository.GetByShiftIdAsync(day.ShiftId) ?? Enumerable.Empty<ShiftDispatcher>())
					.Any(x => !x.ShiftDayId.HasValue && x.IsActive() && string.Equals(x.UserId, userId, StringComparison.OrdinalIgnoreCase));

				if (!standing)
					return DispatcherDeskResult<bool>.Ok(false);

				// Same rule as the responder roster: a denied row for the day keeps a standing dispatcher off it.
				row = new ShiftDispatcher
				{
					DepartmentId = departmentId,
					ShiftId = day.ShiftId,
					ShiftDayId = day.ShiftDayId,
					UserId = userId,
					Source = (int)ShiftDispatcherSources.Assigned,
					AddedByUserId = byUserId,
					AddedOn = now
				};
			}

			row.Status = (int)ShiftDispatcherStatuses.Denied;
			row.ReviewedByUserId = byUserId;
			row.ReviewedOn = now;
			row.Note = Truncate(note, 500);

			if (requiredSource.HasValue)
			{
				// The withdrawal's write is conditional on the source checked above: a manager who turned the signup into an assignment
				// between that read and this write keeps the row, and the dispatcher's planned breaks are not cancelled.
				if (!await _dispatchersRepository.TryUpdateReviewWithSourceAsync(row, requiredSource.Value, cancellationToken))
					return DispatcherDeskResult<bool>.Fail(DispatcherDeskErrors.NotPermitted);
			}
			else
			{
				await _dispatchersRepository.SaveOrUpdateAsync(row, cancellationToken, true);
			}

			foreach (var planned in (await _breaksRepository.GetForShiftDayAsync(shiftDayId) ?? Enumerable.Empty<DispatcherBreak>())
				.Where(x => string.Equals(x.UserId, userId, StringComparison.OrdinalIgnoreCase) && x.State == (int)DispatcherBreakStates.Scheduled))
			{
				planned.State = (int)DispatcherBreakStates.Cancelled;
				planned.UpdatedByUserId = byUserId;
				planned.UpdatedOn = now;
				await _breaksRepository.TryTransitionAsync(planned, (int)DispatcherBreakStates.Scheduled, cancellationToken);
			}

			return DispatcherDeskResult<bool>.Ok(true);
		}

		public async Task<DispatcherDeskResult<ShiftDispatcher>> ReviewSignupAsync(int departmentId, string shiftDispatcherId, bool approve, string byUserId,
			string note, CancellationToken cancellationToken = default)
		{
			var row = await _dispatchersRepository.GetByIdAsync(shiftDispatcherId);
			if (row == null || row.DepartmentId != departmentId)
				return DispatcherDeskResult<ShiftDispatcher>.Fail(DispatcherDeskErrors.ItemNotFound);

			if (row.Status != (int)ShiftDispatcherStatuses.PendingApproval)
				return DispatcherDeskResult<ShiftDispatcher>.Fail(DispatcherDeskErrors.ItemNotAvailable);

			row.Status = approve ? (int)ShiftDispatcherStatuses.Active : (int)ShiftDispatcherStatuses.Denied;
			row.ReviewedByUserId = byUserId;
			row.ReviewedOn = UtcNow();
			row.Note = Truncate(note, 500);

			var saved = await _dispatchersRepository.SaveOrUpdateAsync(row, cancellationToken, true);
			await PublishDeskChangedAsync(departmentId, row.UserId);

			return DispatcherDeskResult<ShiftDispatcher>.Ok(saved);
		}

		public async Task<List<DispatcherDutyWindow>> GetDutyWindowsAsync(int departmentId, DateTime fromUtc, DateTime toUtc)
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
			if (department == null)
				return new List<DispatcherDutyWindow>();

			var enabled = (await GetSettingsForDepartmentAsync(departmentId)).Where(x => x.IsEnabled).Select(x => x.ShiftId).Distinct().ToList();
			if (enabled.Count == 0)
				return new List<DispatcherDutyWindow>();

			var fromLocal = fromUtc.TimeConverter(department).Date.AddDays(-LookBackDays);
			var toLocal = toUtc.TimeConverter(department).Date;

			var days = (await _dispatchersRepository.GetShiftDayTimesAsync(departmentId, enabled, fromLocal, toLocal) ?? Enumerable.Empty<ShiftDayTimesRow>()).ToList();
			if (days.Count == 0)
				return new List<DispatcherDutyWindow>();

			var roster = (await _dispatchersRepository.GetByDepartmentIdAsync(departmentId) ?? Enumerable.Empty<ShiftDispatcher>())
				.Where(x => enabled.Contains(x.ShiftId)).ToList();

			return DispatcherDutyCalculator.Build(days, roster, department).Where(x => x.EndUtc > fromUtc && x.StartUtc < toUtc).ToList();
		}

		public async Task<List<DispatcherDutyWindow>> GetDayRosterAsync(int departmentId, int shiftDayId)
		{
			var day = await _dispatchersRepository.GetShiftDayTimesByIdAsync(shiftDayId);
			if (day == null)
				return new List<DispatcherDutyWindow>();

			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
			var roster = await GetRosterForShiftAsync(day.ShiftId);

			if (roster.Any(x => x.DepartmentId != departmentId))
				return new List<DispatcherDutyWindow>();

			return DispatcherDutyCalculator.Build(new[] { day }, roster, department);
		}

		public async Task<List<DispatcherDutyWindow>> GetOnDutyAsync(int departmentId, DateTime utcNow)
		{
			var windows = await GetDutyWindowsAsync(departmentId, utcNow, utcNow.AddMinutes(1));
			return DispatcherDutyCalculator.OnDuty(windows, utcNow);
		}

		public async Task<DispatcherDutyWindow> GetDutyForUserAsync(int departmentId, string userId, DateTime utcNow)
		{
			return (await GetOnDutyAsync(departmentId, utcNow)).FirstOrDefault(x => string.Equals(x.UserId, userId, StringComparison.OrdinalIgnoreCase));
		}

		public async Task<bool> IsEligibleDispatcherAsync(int departmentId, string userId)
		{
			if (string.IsNullOrWhiteSpace(userId))
				return false;

			var member = await _departmentsService.GetDepartmentMemberAsync(userId, departmentId, false);
			if (member == null || member.IsDeleted || member.IsDisabled.GetValueOrDefault())
				return false;

			return await _dispatchAccessService.CanUseDispatchAsync(departmentId, userId);
		}

		public async Task DeleteForShiftAsync(int shiftId, CancellationToken cancellationToken = default)
		{
			var settings = await _settingsRepository.GetByShiftIdAsync(shiftId);

			// Planned breaks keep their shift id for the record; with the roster gone nobody is on duty for them, so the worker
			// marks them missed when their time passes.
			await _dispatchersRepository.DeleteByShiftIdAsync(shiftId, cancellationToken);
			await _settingsRepository.DeleteByShiftIdAsync(shiftId, cancellationToken);

			if (settings != null)
				await PublishDeskChangedAsync(settings.DepartmentId);
		}

		private async Task<(ShiftDayTimesRow Day, ShiftDispatchSetting Settings, DispatcherDeskErrors Error)> LoadDayAsync(int departmentId, int shiftDayId,
			bool requireEnabled = true)
		{
			var day = await _dispatchersRepository.GetShiftDayTimesByIdAsync(shiftDayId);
			if (day == null)
				return (null, null, DispatcherDeskErrors.ShiftNotFound);

			var shift = await _shiftsService.GetShiftByIdAsync(day.ShiftId);
			if (shift == null || shift.DepartmentId != departmentId)
				return (null, null, DispatcherDeskErrors.ShiftNotFound);

			var settings = await _settingsRepository.GetByShiftIdAsync(day.ShiftId);
			if (requireEnabled && (settings == null || !settings.IsEnabled))
				return (day, settings, DispatcherDeskErrors.DispatchersNotEnabled);

			return (day, settings, DispatcherDeskErrors.None);
		}

		private async Task<ShiftDispatcher> GetDayRowAsync(ShiftDayTimesRow day, string userId)
		{
			return (await _dispatchersRepository.GetByShiftIdAsync(day.ShiftId) ?? Enumerable.Empty<ShiftDispatcher>())
				.Where(x => x.ShiftDayId == day.ShiftDayId && string.Equals(x.UserId, userId, StringComparison.OrdinalIgnoreCase))
				.OrderByDescending(x => x.AddedOn)
				.FirstOrDefault();
		}

		private async Task PublishDeskChangedAsync(int departmentId, string userId = null)
		{
			try
			{
				await _eventAggregator.SendMessageAsync(new DispatcherDeskUpdatedEvent
				{
					DepartmentId = departmentId,
					UserId = userId ?? string.Empty,
					Payload = "{\"userId\":\"" + (userId ?? string.Empty) + "\",\"state\":\"roster\"}"
				});
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Dispatcher desk roster event");
			}
		}

		private static string Truncate(string value, int length) =>
			string.IsNullOrEmpty(value) || value.Length <= length ? value : value.Substring(0, length);
	}
}
