using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Queue;

namespace Resgrid.Services
{
	/// <summary>
	/// Day-level scheduling on top of the shift definitions: resolved rosters, open needs, who is on duty now, signups
	/// with supervisor approval, single-day roster edits and trades. The roster rules themselves live in
	/// <see cref="ShiftRosterBuilder"/>; this part loads the data and applies the workflow.
	/// </summary>
	public partial class ShiftsService
	{
		private sealed class ScheduleData
		{
			public Department Department { get; set; }
			public List<Shift> Shifts { get; set; } = new List<Shift>();
			public List<ShiftSignup> Signups { get; set; } = new List<ShiftSignup>();
			public List<ShiftSignupTrade> Trades { get; set; } = new List<ShiftSignupTrade>();
			public Dictionary<string, List<PersonnelRole>> Roles { get; set; } = new Dictionary<string, List<PersonnelRole>>();
		}

		#region Schedules

		public async Task<ShiftDaySchedule> GetShiftDayScheduleAsync(int shiftDayId)
		{
			var day = await _shiftDaysRepository.GetShiftDayByIdAsync(shiftDayId);

			if (day == null)
				return null;

			var shift = await GetShiftByIdAsync(day.ShiftId);

			if (shift == null)
				return null;

			return await BuildScheduleForDayAsync(shift, day);
		}

		public async Task<List<ShiftDaySchedule>> GetShiftDaySchedulesForDateRangeAsync(int departmentId, DateTime startDate, DateTime endDate, int? shiftId = null)
		{
			if (endDate.Date < startDate.Date)
				(startDate, endDate) = (endDate, startDate);

			var data = await LoadScheduleDataAsync(departmentId, startDate.Date, endDate.Date);
			var localNow = data.Department != null ? DateTime.UtcNow.TimeConverter(data.Department) : (DateTime?)null;
			var schedules = new List<ShiftDaySchedule>();

			foreach (var shift in data.Shifts.Where(x => !shiftId.HasValue || x.ShiftId == shiftId.Value))
			{
				foreach (var day in (shift.Days ?? new List<ShiftDay>()).Where(x => x != null && x.Day.Date >= startDate.Date && x.Day.Date <= endDate.Date))
					schedules.Add(BuildSchedule(shift, day, data, localNow));
			}

			return schedules.OrderBy(x => x.Day.Start).ThenBy(x => x.Shift.Name).ToList();
		}

		public async Task<List<ShiftDaySchedule>> GetActiveShiftDaySchedulesAsync(int departmentId, DateTime timestampUtc)
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);

			if (department == null)
				return new List<ShiftDaySchedule>();

			var localNow = timestampUtc.TimeConverter(department);

			// Look back far enough for a long shift (Hours up to 48+) that started days ago.
			var data = await LoadScheduleDataAsync(departmentId, localNow.Date.AddDays(-3), localNow.Date, department);
			var schedules = new List<ShiftDaySchedule>();

			foreach (var shift in data.Shifts)
			{
				foreach (var day in (shift.Days ?? new List<ShiftDay>()).Where(x => x != null &&
					ShiftTimeWindow.IsActive(localNow, x.Day, shift.StartTime, shift.EndTime, shift.Hours)))
				{
					schedules.Add(BuildSchedule(shift, day, data, localNow));
				}
			}

			return schedules;
		}

		public async Task<List<ShiftDaySchedule>> GetShiftDaysStartingWithinDayAsync(DateTime timestampUtc)
		{
			var schedules = new List<ShiftDaySchedule>();

			// The upcoming query covers two UTC calendar days from its reference; asking from today and tomorrow covers
			// "the next 24 hours local" for every time zone.
			var shifts = new Dictionary<int, Shift>();
			foreach (var reference in new[] { timestampUtc, timestampUtc.AddDays(1) })
			{
				foreach (var shift in (await _shiftsRepository.GetUpcomingShiftAndDaysAsync(reference)) ?? Enumerable.Empty<Shift>())
				{
					if (shift != null && !shifts.ContainsKey(shift.ShiftId))
						shifts.Add(shift.ShiftId, shift);
				}
			}

			foreach (var shift in shifts.Values)
			{
				try
				{
					shift.Department = shift.Department ?? await _departmentsService.GetDepartmentByIdAsync(shift.DepartmentId, false);

					if (shift.Department == null || shift.Days == null)
						continue;

					var localNow = timestampUtc.TimeConverter(shift.Department);

					foreach (var day in shift.Days.Where(x => x != null))
					{
						day.Shift = shift;

						if (day.Start > localNow && day.Start <= localNow.AddHours(24))
							schedules.Add(await BuildScheduleForDayAsync(shift, day));
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"DepartmentId:{shift.DepartmentId} ShiftId:{shift.ShiftId}");
				}
			}

			return schedules;
		}

		public async Task<List<string>> GetOnDutyUserIdsForGroupAsync(int departmentId, int departmentGroupId, DateTime timestampUtc)
		{
			var onDuty = await GetOnDutyUserIdsForGroupsAsync(departmentId, new[] { departmentGroupId }, timestampUtc);

			return onDuty.TryGetValue(departmentGroupId, out var userIds) ? userIds : new List<string>();
		}

		public async Task<Dictionary<int, List<string>>> GetOnDutyUserIdsForGroupsAsync(int departmentId, IEnumerable<int> departmentGroupIds, DateTime timestampUtc)
		{
			var groupIds = (departmentGroupIds ?? Enumerable.Empty<int>()).Distinct().ToList();
			var result = groupIds.ToDictionary(x => x, x => new List<string>());

			if (!groupIds.Any())
				return result;

			var onDutyEntries = (await GetActiveShiftDaySchedulesAsync(departmentId, timestampUtc))
				.SelectMany(x => x.Roster)
				.Where(x => x.IsOnDuty())
				.ToList();

			if (!onDutyEntries.Any())
				return result;

			foreach (var groupId in groupIds)
			{
				// Standing-roster people placed on a shift without a group cover their own group.
				var members = onDutyEntries.Any(x => !x.DepartmentGroupId.HasValue)
					? (await _departmentGroupsService.GetAllMembersForGroupAsync(groupId) ?? new List<DepartmentGroupMember>()).Select(x => x.UserId)
					: Enumerable.Empty<string>();
				result[groupId] = ShiftRosterGroups.Select(groupId, onDutyEntries, members);
			}

			return result;
		}

		#endregion Schedules

		#region Signups and single-day edits

		public async Task<ShiftActionResult<ShiftSignup>> SignupUserForShiftDayAsync(int shiftDayId, int? departmentGroupId, string userId,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			var schedule = await GetShiftDayScheduleAsync(shiftDayId);

			if (schedule == null)
				return ShiftActionResult<ShiftSignup>.Fail(ShiftActionErrors.NotFound);

			if (IsOver(schedule))
				return ShiftActionResult<ShiftSignup>.Fail(ShiftActionErrors.DayInPast);

			var groupId = NormalizeGroupId(departmentGroupId);
			var groupCheck = ValidateGroup(schedule.Shift, groupId, requireGroupWhenShiftHasGroups: true);

			if (groupCheck != ShiftActionErrors.None)
				return ShiftActionResult<ShiftSignup>.Fail(groupCheck);

			if (!schedule.Shift.Groups.Any() && schedule.Shift.AssignmentType != (int)ShiftAssignmentTypes.Signup)
				return ShiftActionResult<ShiftSignup>.Fail(ShiftActionErrors.InvalidRequest);

			var userEntries = schedule.Roster.Where(x => SameUser(x.UserId, userId)).ToList();

			if (userEntries.Any())
			{
				var allowMultipleGroups = await _departmentSettingsService.GetAllowSignupsForMultipleShiftGroupsAsync(schedule.Shift.DepartmentId);

				if (!allowMultipleGroups || userEntries.Any(x => x.DepartmentGroupId == groupId))
					return ShiftActionResult<ShiftSignup>.Fail(ShiftActionErrors.AlreadySignedUp);
			}

			var signup = new ShiftSignup
			{
				ShiftId = schedule.Shift.ShiftId,
				ShiftDay = schedule.Day.Day,
				SignupTimestamp = DateTime.UtcNow,
				UserId = userId,
				DepartmentGroupId = groupId,
				Denied = false,
				ApprovalPending = schedule.Shift.RequireApproval == true
			};

			signup = await _shiftSignupRepository.SaveOrUpdateAsync(signup, cancellationToken, true);

			if (signup.ApprovalPending)
				await PublishRosterChangeAsync(schedule.Shift.DepartmentId, ShiftQueueTypes.SignupPendingApproval, schedule.Shift.ShiftId, signup.ShiftSignupId, 0, userId);

			return ShiftActionResult<ShiftSignup>.Ok(signup);
		}

		public async Task<ShiftActionResult<ShiftSignup>> AssignUserToShiftDayAsync(int shiftDayId, string userId, int? departmentGroupId, string assignedByUserId,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			if (String.IsNullOrWhiteSpace(userId))
				return ShiftActionResult<ShiftSignup>.Fail(ShiftActionErrors.InvalidRequest);

			var schedule = await GetShiftDayScheduleAsync(shiftDayId);

			if (schedule == null)
				return ShiftActionResult<ShiftSignup>.Fail(ShiftActionErrors.NotFound);

			if (IsOver(schedule))
				return ShiftActionResult<ShiftSignup>.Fail(ShiftActionErrors.DayInPast);

			var groupId = NormalizeGroupId(departmentGroupId);
			var groupCheck = ValidateGroup(schedule.Shift, groupId, requireGroupWhenShiftHasGroups: false);

			if (groupCheck != ShiftActionErrors.None)
				return ShiftActionResult<ShiftSignup>.Fail(groupCheck);

			var member = await _departmentsService.GetDepartmentMemberAsync(userId, schedule.Shift.DepartmentId);

			if (member == null || member.IsDeleted)
				return ShiftActionResult<ShiftSignup>.Fail(ShiftActionErrors.NotAllowed);

			if (schedule.Roster.Any(x => SameUser(x.UserId, userId) && x.DepartmentGroupId == groupId && x.IsOnDuty()))
				return ShiftActionResult<ShiftSignup>.Fail(ShiftActionErrors.AlreadyOnRoster);

			var now = DateTime.UtcNow;
			var userSignups = schedule.Signups.Where(x => SameUser(x.UserId, userId)).ToList();

			// Prefer reviving the person's own record for the day: approve a pending signup for the same group, or undo an
			// earlier removal, rather than stacking up duplicate rows.
			var signup = userSignups.FirstOrDefault(x => x.ApprovalPending && !x.Denied && x.DepartmentGroupId == groupId)
			             ?? userSignups.FirstOrDefault(x => x.Denied);

			if (signup == null)
			{
				signup = new ShiftSignup
				{
					ShiftId = schedule.Shift.ShiftId,
					ShiftDay = schedule.Day.Day,
					SignupTimestamp = now,
					UserId = userId
				};
			}

			signup.DepartmentGroupId = groupId;
			signup.Denied = false;
			signup.ApprovalPending = false;
			signup.AssignedByUserId = assignedByUserId;
			signup.ReviewedByUserId = assignedByUserId;
			signup.ReviewedOn = now;
			signup.ReviewNote = null;

			signup = await _shiftSignupRepository.SaveOrUpdateAsync(signup, cancellationToken, true);

			await PublishRosterChangeAsync(schedule.Shift.DepartmentId, ShiftQueueTypes.DayAssigned, schedule.Shift.ShiftId, signup.ShiftSignupId, 0, assignedByUserId);

			return ShiftActionResult<ShiftSignup>.Ok(signup);
		}

		public async Task<ShiftActionResult<bool>> RemoveUserFromShiftDayAsync(int shiftDayId, string userId, string removedByUserId, string note,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			var schedule = await GetShiftDayScheduleAsync(shiftDayId);

			if (schedule == null)
				return ShiftActionResult<bool>.Fail(ShiftActionErrors.NotFound);

			var entries = schedule.Roster.Where(x => SameUser(x.UserId, userId)).ToList();

			if (!entries.Any())
				return ShiftActionResult<bool>.Fail(ShiftActionErrors.NotOnShift);

			var now = DateTime.UtcNow;
			ShiftSignup marker = null;

			foreach (var entry in entries)
			{
				if (entry.ShiftSignupId.HasValue)
				{
					// The slot's signup is marked denied rather than deleted so the person can see what happened. For a
					// trade replacement this is the original owner's signup: the slot is left open, not handed back.
					var signup = schedule.Signups.FirstOrDefault(x => x.ShiftSignupId == entry.ShiftSignupId.Value);

					if (signup == null)
						continue;

					MarkRemoved(signup, removedByUserId, note, now);
					marker = await _shiftSignupRepository.SaveOrUpdateAsync(signup, cancellationToken, true);

					await CloseOpenTradesForSignupAsync(signup.ShiftSignupId, schedule.Trades, removedByUserId, note, now, cancellationToken);
				}
				else
				{
					// Standing roster: a denied signup for just this day takes them off it and leaves the rest of the month.
					var exclusion = new ShiftSignup
					{
						ShiftId = schedule.Shift.ShiftId,
						ShiftDay = schedule.Day.Day,
						SignupTimestamp = now,
						UserId = entry.UserId,
						DepartmentGroupId = entry.DepartmentGroupId,
						AssignedByUserId = removedByUserId
					};

					MarkRemoved(exclusion, removedByUserId, note, now);
					marker = await _shiftSignupRepository.SaveOrUpdateAsync(exclusion, cancellationToken, true);
				}
			}

			await PublishRosterChangeAsync(schedule.Shift.DepartmentId, ShiftQueueTypes.DayRemoved, schedule.Shift.ShiftId, marker?.ShiftSignupId ?? 0, 0, removedByUserId);

			return ShiftActionResult<bool>.Ok(true);
		}

		public async Task<ShiftActionResult<ShiftSignup>> ReviewShiftSignupAsync(int shiftSignupId, bool approve, string reviewerUserId, string note,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			var signup = await _shiftSignupRepository.GetByIdAsync(shiftSignupId);

			if (signup == null)
				return ShiftActionResult<ShiftSignup>.Fail(ShiftActionErrors.NotFound);

			if (!signup.ApprovalPending || signup.Denied)
				return ShiftActionResult<ShiftSignup>.Fail(ShiftActionErrors.NotPending);

			var shift = await _shiftsRepository.GetByIdAsync(signup.ShiftId);

			signup.ApprovalPending = false;
			signup.Denied = !approve;
			signup.ReviewedByUserId = reviewerUserId;
			signup.ReviewedOn = DateTime.UtcNow;
			signup.ReviewNote = note;

			signup = await _shiftSignupRepository.SaveOrUpdateAsync(signup, cancellationToken, true);

			if (shift != null)
				await PublishRosterChangeAsync(shift.DepartmentId, ShiftQueueTypes.SignupReviewed, shift.ShiftId, signup.ShiftSignupId, 0, reviewerUserId);

			return ShiftActionResult<ShiftSignup>.Ok(signup);
		}

		public async Task<List<ShiftSignup>> GetPendingShiftSignupsAsync(int departmentId)
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);

			if (department == null)
				return new List<ShiftSignup>();

			var localToday = DateTime.UtcNow.TimeConverter(department).Date;
			var signups = await _shiftSignupRepository.GetShiftSignupsByDepartmentIdAndDateRangeAsync(departmentId, localToday.AddDays(-1), localToday.AddYears(2));
			var pending = (signups ?? Enumerable.Empty<ShiftSignup>()).Where(x => x.ApprovalPending && !x.Denied).ToList();

			if (!pending.Any())
				return pending;

			var shifts = (await GetAllShiftsByDepartmentAsync(departmentId)).ToDictionary(x => x.ShiftId);
			var groups = (await _departmentGroupsService.GetAllGroupsForDepartmentAsync(departmentId) ?? new List<DepartmentGroup>()).ToDictionary(x => x.DepartmentGroupId);

			foreach (var signup in pending)
			{
				if (shifts.TryGetValue(signup.ShiftId, out var shift))
					signup.Shift = shift;

				if (signup.DepartmentGroupId.HasValue && groups.TryGetValue(signup.DepartmentGroupId.Value, out var group))
					signup.Group = group;
			}

			return pending.Where(x => x.Shift != null).OrderBy(x => x.ShiftDay).ToList();
		}

		#endregion Signups and single-day edits

		#region Trades

		public async Task<ShiftActionResult<ShiftSignupTrade>> RequestTradeAsync(int shiftDayId, string userId, List<string> userIds, string note,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			var schedule = await GetShiftDayScheduleAsync(shiftDayId);

			if (schedule == null)
				return ShiftActionResult<ShiftSignupTrade>.Fail(ShiftActionErrors.NotFound);

			var entry = schedule.Roster.FirstOrDefault(x => SameUser(x.UserId, userId) && x.IsOnDuty());

			if (entry == null)
				return ShiftActionResult<ShiftSignupTrade>.Fail(ShiftActionErrors.NotOnShift);

			// A slot someone traded to you is still backed by their signup; trading it on again is not supported.
			if (entry.Source == ShiftRosterSources.Trade)
				return ShiftActionResult<ShiftSignupTrade>.Fail(ShiftActionErrors.InvalidRequest);

			var signupId = entry.ShiftSignupId;

			if (!signupId.HasValue)
			{
				if (IsOver(schedule))
					return ShiftActionResult<ShiftSignupTrade>.Fail(ShiftActionErrors.DayInPast);

				if (!NormalizeUserIds(userIds, userId).Any())
					return ShiftActionResult<ShiftSignupTrade>.Fail(ShiftActionErrors.NoUsers);

				// People on the standing roster have no row for a single day. Give them one, in the group they are
				// scheduled for, so the day can be traded without touching the rest of the month.
				var slot = await _shiftSignupRepository.SaveOrUpdateAsync(new ShiftSignup
				{
					ShiftId = schedule.Shift.ShiftId,
					ShiftDay = schedule.Day.Day,
					SignupTimestamp = DateTime.UtcNow,
					UserId = entry.UserId,
					DepartmentGroupId = entry.DepartmentGroupId
				}, cancellationToken, true);

				signupId = slot.ShiftSignupId;
			}

			return await RequestTradeForSignupAsync(signupId.Value, userId, userIds, note, cancellationToken);
		}

		public async Task<ShiftActionResult<ShiftSignupTrade>> RequestTradeForSignupAsync(int shiftSignupId, string userId, List<string> userIds, string note,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			var signup = await GetShiftSignupByIdAsync(shiftSignupId);

			if (signup == null)
				return ShiftActionResult<ShiftSignupTrade>.Fail(ShiftActionErrors.NotFound);

			if (!SameUser(signup.UserId, userId))
				return ShiftActionResult<ShiftSignupTrade>.Fail(ShiftActionErrors.NotAllowed);

			if (!signup.IsActive())
				return ShiftActionResult<ShiftSignupTrade>.Fail(ShiftActionErrors.InvalidRequest);

			var shift = await GetShiftByIdAsync(signup.ShiftId);

			if (shift == null)
				return ShiftActionResult<ShiftSignupTrade>.Fail(ShiftActionErrors.NotFound);

			shift.Department = shift.Department ?? await _departmentsService.GetDepartmentByIdAsync(shift.DepartmentId, false);

			if (IsOver(new ShiftDay { Day = signup.ShiftDay, Shift = shift, ShiftId = shift.ShiftId }, shift.Department))
				return ShiftActionResult<ShiftSignupTrade>.Fail(ShiftActionErrors.DayInPast);

			var existing = await _shiftSignupTradeRepository.GetShiftSignupTradeBySourceShiftSignupIdAsync(shiftSignupId);

			if (existing != null && !existing.Denied)
				return ShiftActionResult<ShiftSignupTrade>.Fail(ShiftActionErrors.TradeExists);

			// A day this user took as a swap-back is worked by the other person now.
			var swappedAway = await _shiftSignupTradeRepository.GetShiftSignupTradeByTargetShiftSignupIdAsync(shiftSignupId);

			if (swappedAway != null && swappedAway.IsTradeComplete())
				return ShiftActionResult<ShiftSignupTrade>.Fail(ShiftActionErrors.NotOnShift);

			var invited = NormalizeUserIds(userIds, userId);

			if (!invited.Any())
				return ShiftActionResult<ShiftSignupTrade>.Fail(ShiftActionErrors.NoUsers);

			var members = await _departmentsService.GetAllMembersForDepartmentAsync(shift.DepartmentId) ?? new List<DepartmentMember>();
			var memberIds = new HashSet<string>(members.Where(x => !x.IsDeleted).Select(x => x.UserId), StringComparer.OrdinalIgnoreCase);

			if (invited.Any(x => !memberIds.Contains(x)))
				return ShiftActionResult<ShiftSignupTrade>.Fail(ShiftActionErrors.NotAllowed);

			var trade = new ShiftSignupTrade
			{
				SourceShiftSignupId = signup.ShiftSignupId,
				Note = note,
				Users = invited.Select(x => new ShiftSignupTradeUser { UserId = x }).ToList()
			};

			trade = await _shiftSignupTradeRepository.SaveOrUpdateAsync(trade, cancellationToken);

			await PublishAsync(shift.DepartmentId, number => _eventAggregator.SendMessage<ShiftTradeRequestedEvent>(new ShiftTradeRequestedEvent
			{
				DepartmentId = shift.DepartmentId,
				DepartmentNumber = number,
				ShiftSignupTradeId = trade.ShiftSignupTradeId
			}));

			return ShiftActionResult<ShiftSignupTrade>.Ok(trade);
		}

		public async Task<ShiftActionResult<ShiftSignupTrade>> RespondToTradeAsync(int shiftSignupTradeId, string userId, bool accept, string note, List<int> offeredShiftSignupIds,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			var trade = await GetShiftTradeByIdAsync(shiftSignupTradeId);

			if (trade?.SourceShiftSignup?.Shift == null)
				return ShiftActionResult<ShiftSignupTrade>.Fail(ShiftActionErrors.NotFound);

			if (trade.Users == null || !trade.Users.Any(x => SameUser(x.UserId, userId)))
				return ShiftActionResult<ShiftSignupTrade>.Fail(ShiftActionErrors.NotAllowed);

			if (trade.HasSelection() && !trade.Denied)
				return ShiftActionResult<ShiftSignupTrade>.Fail(ShiftActionErrors.InvalidRequest);

			var departmentId = trade.SourceShiftSignup.Shift.DepartmentId;

			if (!accept)
			{
				// Nothing was saved (the participant row went away): report that rather than an answer nobody recorded.
				if (!await RejectTradeRequestAsync(shiftSignupTradeId, userId, note, cancellationToken))
					return ShiftActionResult<ShiftSignupTrade>.Fail(ShiftActionErrors.NotAllowed);

				await PublishAsync(departmentId, number => _eventAggregator.SendMessage<ShiftTradeRejectedEvent>(new ShiftTradeRejectedEvent
				{
					DepartmentId = departmentId,
					DepartmentNumber = number,
					ShiftSignupTradeId = shiftSignupTradeId,
					UserId = userId
				}));

				return ShiftActionResult<ShiftSignupTrade>.Ok(await GetShiftTradeByIdAsync(shiftSignupTradeId));
			}

			var offers = (offeredShiftSignupIds ?? new List<int>()).Distinct().ToList();

			foreach (var offerId in offers)
			{
				var offered = await _shiftSignupRepository.GetByIdAsync(offerId);

				if (offered == null || !SameUser(offered.UserId, userId) || !offered.IsActive() || offered.ShiftSignupId == trade.SourceShiftSignupId)
					return ShiftActionResult<ShiftSignupTrade>.Fail(ShiftActionErrors.InvalidOffer);
			}

			if (!await ProposeShiftDaysForTradeAsync(shiftSignupTradeId, userId, note, offers, cancellationToken))
				return ShiftActionResult<ShiftSignupTrade>.Fail(ShiftActionErrors.NotAllowed);

			await PublishAsync(departmentId, number => _eventAggregator.SendMessage<ShiftTradeProposedEvent>(new ShiftTradeProposedEvent
			{
				DepartmentId = departmentId,
				DepartmentNumber = number,
				ShiftSignupTradeId = shiftSignupTradeId,
				UserId = userId
			}));

			return ShiftActionResult<ShiftSignupTrade>.Ok(await GetShiftTradeByIdAsync(shiftSignupTradeId));
		}

		public async Task<ShiftActionResult<ShiftSignupTrade>> FinishTradeAsync(int shiftSignupTradeId, string requesterUserId, string acceptedUserId, int? targetShiftSignupId,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			var trade = await GetShiftTradeByIdAsync(shiftSignupTradeId);

			if (trade?.SourceShiftSignup?.Shift == null)
				return ShiftActionResult<ShiftSignupTrade>.Fail(ShiftActionErrors.NotFound);

			if (!SameUser(trade.SourceShiftSignup.UserId, requesterUserId))
				return ShiftActionResult<ShiftSignupTrade>.Fail(ShiftActionErrors.NotAllowed);

			// Once picked, the choice stands unless a supervisor denied it, in which case another offer can be picked.
			if (trade.HasSelection() && !trade.Denied)
				return ShiftActionResult<ShiftSignupTrade>.Fail(ShiftActionErrors.InvalidRequest);

			var offeredUsers = (trade.Users ?? new List<ShiftSignupTradeUser>()).Where(x => x.Offered && !x.Declined).ToList();
			string takerUserId;

			if (targetShiftSignupId.HasValue && targetShiftSignupId.Value > 0)
			{
				// Only a signup a participant actually put up for this trade can be taken as the swap-back day.
				var offeringUser = offeredUsers.FirstOrDefault(x => x.Shifts != null && x.Shifts.Any(y => y.ShiftSignupId == targetShiftSignupId.Value));
				var target = offeringUser != null ? await _shiftSignupRepository.GetByIdAsync(targetShiftSignupId.Value) : null;

				if (target == null || !target.IsActive() || !SameUser(target.UserId, offeringUser.UserId))
					return ShiftActionResult<ShiftSignupTrade>.Fail(ShiftActionErrors.InvalidOffer);

				trade.TargetShiftSignupId = target.ShiftSignupId;
				trade.UserId = null;
				takerUserId = target.UserId;
			}
			else if (!String.IsNullOrWhiteSpace(acceptedUserId))
			{
				var accepted = offeredUsers.FirstOrDefault(x => SameUser(x.UserId, acceptedUserId));

				if (accepted == null)
					return ShiftActionResult<ShiftSignupTrade>.Fail(ShiftActionErrors.InvalidOffer);

				trade.UserId = accepted.UserId;
				trade.TargetShiftSignupId = null;
				takerUserId = accepted.UserId;
			}
			else
			{
				return ShiftActionResult<ShiftSignupTrade>.Fail(ShiftActionErrors.InvalidRequest);
			}

			var shift = trade.SourceShiftSignup.Shift;

			trade.Denied = false;
			trade.ApprovalPending = shift.RequireApproval == true;

			await _shiftSignupTradeRepository.SaveOrUpdateAsync(trade, cancellationToken, true);

			if (trade.ApprovalPending)
			{
				await PublishRosterChangeAsync(shift.DepartmentId, ShiftQueueTypes.TradePendingApproval, shift.ShiftId, trade.SourceShiftSignupId, trade.ShiftSignupTradeId, requesterUserId);
			}
			else
			{
				await PublishAsync(shift.DepartmentId, number => _eventAggregator.SendMessage<ShiftTradeFilledEvent>(new ShiftTradeFilledEvent
				{
					DepartmentId = shift.DepartmentId,
					DepartmentNumber = number,
					ShiftSignupTradeId = trade.ShiftSignupTradeId,
					UserId = takerUserId
				}));
			}

			return ShiftActionResult<ShiftSignupTrade>.Ok(await GetShiftTradeByIdAsync(shiftSignupTradeId));
		}

		public async Task<ShiftActionResult<bool>> CancelTradeAsync(int shiftSignupTradeId, string requesterUserId,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			var trade = await GetShiftTradeByIdAsync(shiftSignupTradeId);

			if (trade?.SourceShiftSignup == null)
				return ShiftActionResult<bool>.Fail(ShiftActionErrors.NotFound);

			if (!SameUser(trade.SourceShiftSignup.UserId, requesterUserId))
				return ShiftActionResult<bool>.Fail(ShiftActionErrors.NotAllowed);

			if (trade.IsTradeComplete())
				return ShiftActionResult<bool>.Fail(ShiftActionErrors.InvalidRequest);

			// Trade users and their offered days cascade with the trade.
			await _shiftSignupTradeRepository.DeleteAsync(trade, cancellationToken);

			return ShiftActionResult<bool>.Ok(true);
		}

		public async Task<ShiftActionResult<ShiftSignupTrade>> ReviewTradeAsync(int shiftSignupTradeId, bool approve, string reviewerUserId, string note,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			var trade = await GetShiftTradeByIdAsync(shiftSignupTradeId);

			if (trade?.SourceShiftSignup?.Shift == null)
				return ShiftActionResult<ShiftSignupTrade>.Fail(ShiftActionErrors.NotFound);

			if (!trade.ApprovalPending || trade.Denied)
				return ShiftActionResult<ShiftSignupTrade>.Fail(ShiftActionErrors.NotPending);

			trade.ApprovalPending = false;
			trade.Denied = !approve;
			trade.ReviewedByUserId = reviewerUserId;
			trade.ReviewedOn = DateTime.UtcNow;
			trade.ReviewNote = note;

			await _shiftSignupTradeRepository.SaveOrUpdateAsync(trade, cancellationToken, true);

			var shift = trade.SourceShiftSignup.Shift;
			await PublishRosterChangeAsync(shift.DepartmentId, ShiftQueueTypes.TradeReviewed, shift.ShiftId, trade.SourceShiftSignupId, trade.ShiftSignupTradeId, reviewerUserId);

			return ShiftActionResult<ShiftSignupTrade>.Ok(await GetShiftTradeByIdAsync(shiftSignupTradeId));
		}

		public async Task<List<ShiftSignupTrade>> GetPendingTradesAsync(int departmentId)
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);

			if (department == null)
				return new List<ShiftSignupTrade>();

			var localToday = DateTime.UtcNow.TimeConverter(department).Date;
			var trades = await _shiftSignupTradeRepository.GetShiftSignupTradesByDepartmentIdAsync(departmentId, localToday.AddDays(-1));

			return await LoadTradesAsync((trades ?? Enumerable.Empty<ShiftSignupTrade>()).Where(x => x.ApprovalPending && !x.Denied).Select(x => x.ShiftSignupTradeId));
		}

		public async Task<List<ShiftSignupTrade>> GetTradesForUserAsync(int departmentId, string userId)
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);

			if (department == null)
				return new List<ShiftSignupTrade>();

			var localToday = DateTime.UtcNow.TimeConverter(department).Date;
			var departmentTrades = await _shiftSignupTradeRepository.GetShiftSignupTradesByDepartmentIdAsync(departmentId, localToday.AddDays(-1)) ?? Enumerable.Empty<ShiftSignupTrade>();
			var incoming = await _shiftSignupTradeRepository.GetAllOpenTradeRequestsByUserIdAsync(userId) ?? Enumerable.Empty<ShiftSignupTrade>();

			var ids = departmentTrades.Where(x => x.SourceShiftSignup != null && SameUser(x.SourceShiftSignup.UserId, userId)).Select(x => x.ShiftSignupTradeId)
				.Concat(incoming.Select(x => x.ShiftSignupTradeId));

			var trades = await LoadTradesAsync(ids);

			return trades.Where(x => x.SourceShiftSignup?.Shift != null && x.SourceShiftSignup.Shift.DepartmentId == departmentId).ToList();
		}

		#endregion Trades

		#region Helpers

		private async Task<ScheduleData> LoadScheduleDataAsync(int departmentId, DateTime startDate, DateTime endDate, Department department = null)
		{
			var data = new ScheduleData();

			data.Department = department ?? await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
			data.Shifts = await GetAllShiftsByDepartmentAsync(departmentId);
			data.Signups = ((await _shiftSignupRepository.GetShiftSignupsByDepartmentIdAndDateRangeAsync(departmentId, startDate.Date, endDate.Date.AddDays(1)))
				?? Enumerable.Empty<ShiftSignup>()).ToList();
			data.Trades = ((await _shiftSignupTradeRepository.GetShiftSignupTradesByDepartmentIdAsync(departmentId, startDate.Date))
				?? Enumerable.Empty<ShiftSignupTrade>()).ToList();
			data.Roles = await _personnelRolesService.GetAllRolesForUsersInDepartmentAsync(departmentId) ?? new Dictionary<string, List<PersonnelRole>>();

			foreach (var shift in data.Shifts)
			{
				shift.Department = shift.Department ?? data.Department;
				shift.Personnel = shift.Personnel ?? new List<ShiftPerson>();
				shift.Groups = shift.Groups ?? new List<ShiftGroup>();
			}

			return data;
		}

		private async Task<ShiftDaySchedule> BuildScheduleForDayAsync(Shift shift, ShiftDay day)
		{
			var department = shift.Department ?? await _departmentsService.GetDepartmentByIdAsync(shift.DepartmentId, false);
			shift.Department = department;
			shift.Personnel = shift.Personnel ?? new List<ShiftPerson>();
			shift.Groups = shift.Groups ?? new List<ShiftGroup>();

			var data = new ScheduleData
			{
				Department = department,
				Signups = ((await _shiftSignupRepository.GetAllShiftSignupsByShiftIdAndDateAsync(shift.ShiftId, day.Day)) ?? Enumerable.Empty<ShiftSignup>()).ToList(),
				Trades = ((await _shiftSignupTradeRepository.GetShiftSignupTradesByDepartmentIdAsync(shift.DepartmentId, day.Day.Date)) ?? Enumerable.Empty<ShiftSignupTrade>()).ToList(),
				Roles = await _personnelRolesService.GetAllRolesForUsersInDepartmentAsync(shift.DepartmentId) ?? new Dictionary<string, List<PersonnelRole>>()
			};

			var localNow = department != null ? DateTime.UtcNow.TimeConverter(department) : (DateTime?)null;

			return BuildSchedule(shift, day, data, localNow);
		}

		private static ShiftDaySchedule BuildSchedule(Shift shift, ShiftDay day, ScheduleData data, DateTime? localNow)
		{
			day.Shift = shift;

			var date = day.Day.Date;
			var signups = data.Signups.Where(x => x.ShiftId == shift.ShiftId && x.ShiftDay.Date == date).ToList();
			var signupIds = new HashSet<int>(signups.Select(x => x.ShiftSignupId));
			var trades = data.Trades.Where(x => signupIds.Contains(x.SourceShiftSignupId) ||
			                                    (x.TargetShiftSignupId.HasValue && signupIds.Contains(x.TargetShiftSignupId.Value))).ToList();

			var roster = ShiftRosterBuilder.Build(shift, date, signups, trades);

			return new ShiftDaySchedule
			{
				Day = day,
				Shift = shift,
				Signups = signups,
				Trades = trades,
				Roster = roster,
				Needs = ShiftRosterBuilder.CalculateNeeds(shift, roster, data.Roles),
				IsActive = localNow.HasValue && ShiftTimeWindow.IsActive(localNow.Value, day.Day, shift.StartTime, shift.EndTime, shift.Hours)
			};
		}

		private async Task<List<ShiftSignupTrade>> LoadTradesAsync(IEnumerable<int> tradeIds)
		{
			var trades = new List<ShiftSignupTrade>();

			foreach (var tradeId in tradeIds.Distinct())
			{
				var trade = await GetShiftTradeByIdAsync(tradeId);

				if (trade != null)
					trades.Add(trade);
			}

			return trades.OrderBy(x => x.SourceShiftSignup?.ShiftDay).ToList();
		}

		/// <summary>
		/// Clears the two non-cascading references to a signup: a trade using it as the swap-back day goes back to
		/// having no pick, and any offer that put it up is withdrawn.
		/// </summary>
		private async Task ReleaseTradeReferencesToSignupAsync(int shiftSignupId, CancellationToken cancellationToken)
		{
			for (var guard = 0; guard < 25; guard++)
			{
				var targeting = await _shiftSignupTradeRepository.GetShiftSignupTradeByTargetShiftSignupIdAsync(shiftSignupId);

				if (targeting == null)
					break;

				targeting.TargetShiftSignupId = null;
				targeting.ApprovalPending = false;
				await _shiftSignupTradeRepository.SaveOrUpdateAsync(targeting, cancellationToken, true);
			}

			var offers = await _shiftSignupTradeUserShiftsRepository.GetShiftSignupTradeUserShiftsBySignupIdAsync(shiftSignupId);

			foreach (var offer in offers ?? Enumerable.Empty<ShiftSignupTradeUserShift>())
				await _shiftSignupTradeUserShiftsRepository.DeleteAsync(offer, cancellationToken);
		}

		/// <summary>
		/// A person taken off a day can no longer hand that day to someone else, so an open or pending trade on the
		/// slot is closed as denied.
		/// </summary>
		private async Task CloseOpenTradesForSignupAsync(int shiftSignupId, IEnumerable<ShiftSignupTrade> trades, string userId, string note, DateTime now,
			CancellationToken cancellationToken)
		{
			foreach (var trade in trades.Where(x => x.SourceShiftSignupId == shiftSignupId && !x.Denied && !x.IsTradeComplete()))
			{
				trade.ApprovalPending = false;
				trade.Denied = true;
				trade.ReviewedByUserId = userId;
				trade.ReviewedOn = now;
				trade.ReviewNote = note;

				await _shiftSignupTradeRepository.SaveOrUpdateAsync(trade, cancellationToken, true);
			}
		}

		private static void MarkRemoved(ShiftSignup signup, string userId, string note, DateTime now)
		{
			signup.Denied = true;
			signup.ApprovalPending = false;
			signup.ReviewedByUserId = userId;
			signup.ReviewedOn = now;
			signup.ReviewNote = note;
		}

		private static bool IsOver(ShiftDaySchedule schedule)
		{
			return IsOver(schedule.Day, schedule.Shift.Department);
		}

		private static bool IsOver(ShiftDay day, Department department)
		{
			if (department == null)
				return false;

			return DateTime.UtcNow.TimeConverter(department) >= day.End;
		}

		private static int? NormalizeGroupId(int? departmentGroupId)
		{
			return departmentGroupId.HasValue && departmentGroupId.Value > 0 ? departmentGroupId : null;
		}

		private static ShiftActionErrors ValidateGroup(Shift shift, int? groupId, bool requireGroupWhenShiftHasGroups)
		{
			var groups = shift.Groups ?? new List<ShiftGroup>();

			if (groupId.HasValue)
				return groups.Any(x => x.DepartmentGroupId == groupId.Value) ? ShiftActionErrors.None : ShiftActionErrors.InvalidGroup;

			if (requireGroupWhenShiftHasGroups && groups.Any())
				return ShiftActionErrors.InvalidGroup;

			return ShiftActionErrors.None;
		}

		private static List<string> NormalizeUserIds(IEnumerable<string> userIds, string excludeUserId)
		{
			return (userIds ?? Enumerable.Empty<string>())
				.Where(x => !String.IsNullOrWhiteSpace(x) && !SameUser(x, excludeUserId))
				.Select(x => x.Trim())
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
		}

		private static bool SameUser(string a, string b)
		{
			return String.Equals(a, b, StringComparison.OrdinalIgnoreCase);
		}

		private Task PublishRosterChangeAsync(int departmentId, ShiftQueueTypes type, int shiftId, int shiftSignupId, int shiftSignupTradeId, string userId)
		{
			return PublishAsync(departmentId, number => _eventAggregator.SendMessage<ShiftRosterChangedEvent>(new ShiftRosterChangedEvent
			{
				DepartmentId = departmentId,
				DepartmentNumber = number,
				ChangeType = type,
				ShiftId = shiftId,
				ShiftSignupId = shiftSignupId,
				ShiftSignupTradeId = shiftSignupTradeId,
				UserId = userId
			}));
		}

		/// <summary>
		/// Notifications are best effort: the roster change has already been saved, so a failure to queue a
		/// notification is logged rather than surfaced as a failed action.
		/// </summary>
		private async Task PublishAsync(int departmentId, Action<string> send)
		{
			try
			{
				var number = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(departmentId);
				send(number);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"DepartmentId:{departmentId}");
			}
		}

		#endregion Helpers
	}
}
