using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public partial class ShiftsService : IShiftsService
	{
		private readonly IShiftsRepository _shiftsRepository;
		private readonly IShiftPersonRepository _shiftPersonRepository;
		private readonly IShiftDaysRepository _shiftDaysRepository;
		private readonly IShiftGroupsRepository _shiftGroupsRepository;
		private readonly IShiftSignupRepository _shiftSignupRepository;
		private readonly IShiftSignupTradeRepository _shiftSignupTradeRepository;
		private readonly IShiftSignupTradeUserRepository _shiftSignupTradeUserRepository;
		private readonly IShiftSignupTradeUserShiftsRepository _shiftSignupTradeUserShiftsRepository;
		private readonly IShiftStaffingRepository _shiftStaffingRepository;
		private readonly IShiftStaffingPersonRepository _shiftStaffingPersonRepository;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IShiftGroupAssignmentsRepository _shiftGroupAssignmentsRepository;
		private readonly IShiftGroupRolesRepository _shiftGroupRolesRepository;
		private readonly IEventAggregator _eventAggregator;
		private readonly IDepartmentSettingsService _departmentSettingsService;

		public ShiftsService(IShiftsRepository shiftsRepository, IShiftPersonRepository shiftPersonRepository,
			IShiftDaysRepository shiftDaysRepository, IShiftGroupsRepository shiftGroupsRepository,
			IShiftSignupRepository shiftSignupRepository, IShiftSignupTradeRepository shiftSignupTradeRepository, IPersonnelRolesService personnelRolesService,
			IShiftSignupTradeUserRepository shiftSignupTradeUserRepository, IShiftSignupTradeUserShiftsRepository shiftSignupTradeUserShiftsRepository,
			IShiftStaffingRepository shiftStaffingRepository, IShiftStaffingPersonRepository shiftStaffingPersonRepository, IDepartmentsService departmentsService,
			IDepartmentGroupsService departmentGroupsService, IShiftGroupAssignmentsRepository shiftGroupAssignmentsRepository, IShiftGroupRolesRepository shiftGroupRolesRepositor,
			IEventAggregator eventAggregator, IDepartmentSettingsService departmentSettingsService)
		{
			_shiftsRepository = shiftsRepository;
			_shiftPersonRepository = shiftPersonRepository;
			_shiftDaysRepository = shiftDaysRepository;
			_shiftGroupsRepository = shiftGroupsRepository;
			_shiftSignupRepository = shiftSignupRepository;
			_shiftSignupTradeRepository = shiftSignupTradeRepository;
			_personnelRolesService = personnelRolesService;
			_shiftSignupTradeUserRepository = shiftSignupTradeUserRepository;
			_shiftSignupTradeUserShiftsRepository = shiftSignupTradeUserShiftsRepository;
			_shiftStaffingRepository = shiftStaffingRepository;
			_shiftStaffingPersonRepository = shiftStaffingPersonRepository;
			_departmentsService = departmentsService;
			_departmentGroupsService = departmentGroupsService;
			_shiftGroupAssignmentsRepository = shiftGroupAssignmentsRepository;
			_shiftGroupRolesRepository = shiftGroupRolesRepositor;
			_eventAggregator = eventAggregator;
			_departmentSettingsService = departmentSettingsService;
		}

		public async Task<List<Shift>> GetAllShiftsByDepartmentAsync(int departmentId)
		{
			var items = await _shiftsRepository.GetShiftAndDaysByDepartmentIdAsync(departmentId);

			if (items != null && items.Any())
				return items.ToList();

			return new List<Shift>();
		}

		public async Task<Shift> GetShiftByIdAsync(int shiftId)
		{
			var shift = await _shiftsRepository.GetShiftAndDaysByShiftIdAsync(shiftId);

			if (shift != null)
			{
				shift.Personnel = (await _shiftPersonRepository.GetAllShiftPersonsByShiftIdAsync(shiftId)).ToList();
				//shift.Department = await _departmentsService.GetDepartmentByIdAsync(shift.DepartmentId);
				shift.Groups = await GetShiftGroupsForShift(shiftId);
				//shift.Signups = (await _shiftSignupRepository.GetAllShiftSignupsByShiftIdAsync(shiftId)).ToList();
				//shift.Admins = (await _shift

				return shift;
			}

			return null;
		}

		public async Task<Shift> PopulateShiftData(Shift shift, bool getDepartment, bool getPersonnel, bool getGroups,
			bool getSignups, bool getAdmins)
		{
			if (getDepartment && shift.Department == null)
				shift.Department = await _departmentsService.GetDepartmentByIdAsync(shift.DepartmentId);

			// Only load what is missing. This used to replace an already-loaded Personnel list with an empty one, which
			// zeroed the API's PersonnelCount/InShift and made the edit page save the shift with nobody assigned.
			if (getPersonnel && shift.Personnel == null)
				shift.Personnel = (await _shiftPersonRepository.GetAllShiftPersonsByShiftIdAsync(shift.ShiftId) ?? Enumerable.Empty<ShiftPerson>()).ToList();
			else if (shift.Personnel == null)
				shift.Personnel = new List<ShiftPerson>();

			if (getGroups && shift.Groups == null)
				shift.Groups = await GetShiftGroupsForShift(shift.ShiftId);

			if (getSignups && shift.Signups == null)
				shift.Signups = (await _shiftSignupRepository.GetAllShiftSignupsByShiftIdAsync(shift.ShiftId) ?? Enumerable.Empty<ShiftSignup>()).ToList();

			return shift;
		}

		public async Task<Shift> SaveShiftAsync(Shift shift, CancellationToken cancellationToken = default(CancellationToken))
		{
			var saved = await _shiftsRepository.SaveOrUpdateAsync(shift, cancellationToken);

			// The repository cascades one level: a new shift's Groups are written, but each group's Roles are a level
			// deeper and were silently dropped, so new shifts never had role requirements. Write any unsaved ones.
			if (saved?.Groups != null)
			{
				foreach (var group in saved.Groups.Where(x => x != null && x.ShiftGroupId > 0 && x.Roles != null))
				{
					foreach (var role in group.Roles.Where(x => x != null && x.ShiftGroupRoleId == 0))
					{
						role.ShiftGroupId = group.ShiftGroupId;
						await _shiftGroupRolesRepository.SaveOrUpdateAsync(role, cancellationToken, true);
					}
				}
			}

			return saved;
		}

		public async Task<Shift> UpdateShiftAsync(Shift shift, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (shift == null)
				return null;

			return await _shiftsRepository.SaveOrUpdateAsync(shift, cancellationToken, true);
		}

		public async Task<Shift> UpdateShiftStartDayAsync(Shift shift, DateTime startDay, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (shift == null)
				return null;

			shift.StartDay = startDay;

			// firstLevelOnly: Days, Groups, Personnel and Admins are each managed by their own
			// methods. A cascading save would rewrite those child rows from whatever happens to be
			// loaded on this instance, which is not what a StartDay update should touch.
			return await _shiftsRepository.SaveOrUpdateAsync(shift, cancellationToken, true);
		}

		public async Task<List<ShiftGroup>> GetShiftGroupsForShift(int shiftId)
		{
			var groups = await _shiftGroupsRepository.GetShiftGroupsByShiftIdAsync(shiftId);

			if (groups == null)
				return new List<ShiftGroup>();

			if (groups.Any())
			{
				foreach (var shiftGroup in groups)
				{
					shiftGroup.DepartmentGroup = await _departmentGroupsService.GetGroupByIdAsync(shiftGroup.DepartmentGroupId);
					shiftGroup.Assignments = (await _shiftGroupAssignmentsRepository.GetShiftAssignmentsByGroupIdAsync(shiftGroup.ShiftGroupId)).ToList();
					shiftGroup.Roles = (await _shiftGroupRolesRepository.GetShiftGroupRolesByGroupIdAsync(shiftGroup.ShiftGroupId)).ToList();
				}
			}

			return groups.ToList();
		}

		public async Task<bool> UpdateShiftPersonnel(Shift shift, List<ShiftPerson> newPersonnel, CancellationToken cancellationToken = default(CancellationToken))
		{
			var dbShift = await GetShiftByIdAsync(shift.ShiftId);

			if (dbShift == null)
				return false;

			newPersonnel = newPersonnel ?? new List<ShiftPerson>();

			foreach (var shiftPerson in dbShift.Personnel ?? new List<ShiftPerson>())
			{
				await _shiftPersonRepository.DeleteAsync(shiftPerson, cancellationToken);
			}

			foreach (var person in newPersonnel)
			{
				person.ShiftId = shift.ShiftId;
				await _shiftPersonRepository.SaveOrUpdateAsync(person, cancellationToken);
			}

			return true;
		}

		public async Task<bool> UpdateShiftDatesAsync(Shift shift, List<ShiftDay> days, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (shift == null)
				return false;

			// A shift with no days yet deserializes with Days null rather than an empty collection,
			// which is the normal state the first time days are added to a shift.
			var existingDays = shift.Days ?? new List<ShiftDay>();
			days = days ?? new List<ShiftDay>();

			// Adding Days
			foreach (var day in days)
			{
				// Don't re-add days already that are apart of the shift
				if (!existingDays.Any(x => x.Day.Day == day.Day.Day && x.Day.Month == day.Day.Month && x.Day.Year == day.Day.Year))
				{
					day.ShiftId = shift.ShiftId;
					await _shiftDaysRepository.SaveOrUpdateAsync(day, cancellationToken);
				}
			}

			// Removing Days
			var daysToRemove = from sd in existingDays
							   let day = days.FirstOrDefault(x => x.Day.Day == sd.Day.Day && x.Day.Month == sd.Day.Month && x.Day.Year == sd.Day.Year)
							   where day == null
							   select sd;

			if (daysToRemove != null && daysToRemove.Any())
			{
				foreach (var day in daysToRemove)
				{
					await _shiftDaysRepository.DeleteAsync(day, cancellationToken);
				}
			}

			return true;
		}

		public async Task<bool> UpdateShiftGroupsAsync(Shift shift, List<ShiftGroup> groups, CancellationToken cancellationToken = default(CancellationToken))
		{
			var shiftGroups = await GetShiftGroupsForShift(shift.ShiftId);
			foreach (var shiftGroup in shiftGroups)
			{
				await _shiftGroupsRepository.DeleteAsync(shiftGroup, cancellationToken);
			}


			foreach (var group in groups)
			{
				group.ShiftId = shift.ShiftId;
				await _shiftGroupsRepository.InsertAsync(group, cancellationToken);
			}

			return true;
		}

		public async Task<bool> DeleteShift(Shift shift, CancellationToken cancellationToken = default(CancellationToken))
		{
			// Signups cascade with the shift, but a trade elsewhere can still point at one of them as its swap-back day
			// or offer, and those two foreign keys do not cascade, so the delete would fail.
			var signups = await _shiftSignupRepository.GetAllShiftSignupsByShiftIdAsync(shift.ShiftId);

			foreach (var signup in signups ?? Enumerable.Empty<ShiftSignup>())
				await ReleaseTradeReferencesToSignupAsync(signup.ShiftSignupId, cancellationToken);

			return await _shiftsRepository.DeleteAsync(shift, cancellationToken);
		}

		public async Task<bool> DeleteShiftGroupsByGroupIdAsync(int departmentGroupId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var groups = await _shiftGroupsRepository.GetShiftGroupsByGroupIdAsync(departmentGroupId);

			foreach (var group in groups)
			{
				await _shiftGroupsRepository.DeleteAsync(group, cancellationToken);
			}

			return true;
		}

		public async Task<bool> RejectTradeRequestAsync(int shiftTradeId, string userId, string reason, CancellationToken cancellationToken = default(CancellationToken))
		{
			var trade = await GetShiftTradeByIdAsync(shiftTradeId);

			if (trade?.Users == null)
				return false;

			var userTradeRequest = trade.Users.FirstOrDefault(x => x.UserId == userId);

			if (userTradeRequest != null)
			{
				userTradeRequest.Declined = true;
				userTradeRequest.Offered = false;
				userTradeRequest.Reason = reason;

				await _shiftSignupTradeUserRepository.SaveOrUpdateAsync(userTradeRequest, cancellationToken, true);

				return true;
			}

			return false;
		}

		public async Task<bool> ProposeShiftDaysForTradeAsync(int shiftTradeId, string userId, string reason, List<int> signups, CancellationToken cancellationToken = default(CancellationToken))
		{
			var trade = await GetShiftTradeByIdAsync(shiftTradeId);

			if (trade?.Users == null)
				return false;

			var userTradeRequest = trade.Users.FirstOrDefault(x => x.UserId == userId);

			if (userTradeRequest != null)
			{
				userTradeRequest.Reason = reason;
				userTradeRequest.Offered = true;
				userTradeRequest.Declined = false;

				await _shiftSignupTradeUserRepository.SaveOrUpdateAsync(userTradeRequest, cancellationToken, true);

				// A second answer replaces the first rather than piling more offered days onto it.
				if (userTradeRequest.Shifts != null)
				{
					foreach (var previousOffer in userTradeRequest.Shifts.Where(x => x != null).ToList())
						await _shiftSignupTradeUserShiftsRepository.DeleteAsync(previousOffer, cancellationToken);
				}

				if (signups != null && signups.Any())
				{
					var shiftSignups = new List<ShiftSignupTradeUserShift>();
					foreach (var i in signups.Distinct())
					{
						var signup = await GetShiftSignupByIdAsync(i);

						// Only the proposer's own live signups can be offered back, never the day being traded.
						if (signup != null && signup.UserId == userId && signup.IsActive() && signup.ShiftSignupId != trade.SourceShiftSignupId)
						{
							var shift = new ShiftSignupTradeUserShift();
							shift.ShiftSignupTradeUserId = userTradeRequest.ShiftSignupTradeUserId;
							shift.ShiftSignupId = signup.ShiftSignupId;

							shiftSignups.Add(shift);
						}
					}

					if (shiftSignups.Any())
					{
						foreach (var shiftSignup in shiftSignups)
						{
							await _shiftSignupTradeUserShiftsRepository.SaveOrUpdateAsync(shiftSignup, cancellationToken);
						}
					}

				}

				return true;
			}

			return false;
		}

		public async Task<List<Shift>> GetShiftsStartingNextDayAsync(DateTime currentTime)
		{
			var upcomingShifts = new List<Shift>();

			var shifts = await _shiftsRepository.GetUpcomingShiftAndDaysAsync(currentTime);

			if (shifts != null && shifts.Any())
			{
				foreach (var shift in shifts)
				{
					try
					{
						//var shiftData = await PopulateShiftData(shift, true, true, true, true, true);

						if (shift.Days != null && shift.Days.Any())
						{
							if (shift.Department == null)
								shift.Department = await _departmentsService.GetDepartmentByIdAsync(shift.DepartmentId, false);

							var localizedDate = TimeConverterHelper.TimeConverter(currentTime, shift.Department);

							var shiftStart = shift.StartTime;

							if (String.IsNullOrWhiteSpace(shiftStart))
								shiftStart = "12:00 AM";

							var startTime = DateTimeHelpers.ConvertStringTime(shiftStart, localizedDate, shift.Department.Use24HourTime.GetValueOrDefault());

							var shiftDays = from sd in shift.Days
											let shiftDayTime = DateTimeHelpers.ConvertStringTime(shiftStart, sd.Day, shift.Department.Use24HourTime.GetValueOrDefault())
											let nextDayShiftTime = localizedDate.AddDays(1)
											where shiftDayTime == nextDayShiftTime.Within(TimeSpan.FromMinutes(15))
											select sd;

							//List<ShiftDay> shiftDays = new List<ShiftDay>();
							//foreach (var sd in shift.Days)
							//{
							//	var shiftDayTime = DateTimeHelpers.ConvertStringTime(shiftStart, sd.Day, shift.Department.Use24HourTime.GetValueOrDefault());
							//	var nextDayShiftTime = localizedDate.AddDays(1);

							//	if (shiftDayTime == nextDayShiftTime.Within(TimeSpan.FromMinutes(15)))
							//		shiftDays.Add(sd);
							//}

							if (shiftDays.Any())
							{
								var previousShift = from sd in shift.Days
													where sd.Day.ToShortDateString() == startTime.ToShortDateString()
													select sd;

								if (!previousShift.Any())
									upcomingShifts.Add(shift);
							}
						}
					}
					catch (Exception ex)
					{
						Logging.LogException(ex, $"DepartmentId:{shift.DepartmentId}");
					}
				}
			}

			return upcomingShifts;
		}

		public async Task<List<ShiftDay>> GetShiftDaysForDayAsync(DateTime currentTime, int departmentId)
		{
			var shiftDays = new List<ShiftDay>();

			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
			if (department == null)
				return shiftDays;

			var shifts = await GetAllShiftsByDepartmentAsync(departmentId);
			var localNow = currentTime.TimeConverter(department);
			var localDate = localNow.Date;

			// The department-local day's shift days, plus a night shift from the day before that is still running.
			// This used to be "starts within twelve hours of now", which mixed yesterday's and tomorrow's days in.
			foreach (var shift in shifts)
			{
				if (shift.Days == null)
					continue;

				shift.Department = shift.Department ?? department;

				foreach (var day in shift.Days.Where(x => x != null))
				{
					day.Shift = shift;

					if (day.Day.Date == localDate ||
					    (day.Day.Date < localDate && ShiftTimeWindow.IsActive(localNow, day.Day, shift.StartTime, shift.EndTime, shift.Hours)))
						shiftDays.Add(day);
				}
			}

			return shiftDays.OrderBy(x => x.Start).ToList();
		}

		public string GenerateShiftNotificationText(Shift shift)
		{
			if (shift.AssignmentType == (int)ShiftAssignmentTypes.Assigned)
				return string.Format("Assigned shift ({0}) starts tomorrow at {1}", shift.Name, shift.StartTime);
			else
				return string.Format("Shift ({0}) starts tomorrow at {1}", shift.Name, shift.StartTime);
		}

		public string GenerateShiftTradeNotificationText(UserProfile profile, ShiftSignupTrade trade)
		{
			return string.Format("Shift Trade Request From {0} for {1}", profile?.FullName?.AsFirstNameLastName, trade?.SourceShiftSignup?.ShiftDay.ToShortDateString());
		}

		public string GenerateShiftTradeRejectionText(UserProfile profile, ShiftSignupTrade trade)
		{
			return string.Format("{0} Rejected Shift Trade Request for {1}", profile?.FullName?.AsFirstNameLastName, trade?.SourceShiftSignup?.ShiftDay.ToShortDateString());
		}

		public string GenerateShiftTradeProposedText(UserProfile profile, ShiftSignupTrade trade)
		{
			return string.Format("{0} Proposed Shift Trades for {1}", profile?.FullName?.AsFirstNameLastName, trade?.SourceShiftSignup?.ShiftDay.ToShortDateString());
		}

		public string GenerateShiftTradeFilledText(UserProfile tradeProfile, ShiftSignupTrade trade)
		{
			if (trade?.TargetShiftSignup != null)
				return string.Format("{0} accepted trade {1} for {2}", tradeProfile?.FullName?.AsFirstNameLastName, trade.SourceShiftSignup?.ShiftDay.ToShortDateString(), trade.TargetShiftSignup.ShiftDay.ToShortDateString());
			else
				return string.Format("{0} accepted you working {1}", tradeProfile?.FullName?.AsFirstNameLastName, trade?.SourceShiftSignup?.ShiftDay.ToShortDateString());
		}

		public async Task<ShiftDay> GetShiftDayByIdAsync(int shiftDayId)
		{
			return await _shiftDaysRepository.GetShiftDayByIdAsync(shiftDayId);
		}

		public async Task<bool> IsShiftDayFilledAsync(int shiftDayId)
		{
			var schedule = await GetShiftDayScheduleAsync(shiftDayId);

			return schedule == null || schedule.IsFilled();
		}

		public async Task<bool> IsShiftDayFilledWithObjAsync(Shift shift, ShiftDay shiftDay)
		{
			var needs = await GetShiftDayNeedsObjAsync(shift, shiftDay);

			return needs == null || needs.Values.All(x => x.Values.All(v => v <= 0));
		}

		/// <summary>
		/// Needs for a day of an already-loaded shift. Returns null when the shift has no groups (nothing to fill).
		/// Needs are counted against the resolved roster, so assigned staff, pending approvals, single-day removals and
		/// trades are all taken into account, and each person fills one role requirement at most.
		/// </summary>
		public async Task<Dictionary<int, Dictionary<int, int>>> GetShiftDayNeedsObjAsync(Shift shift, ShiftDay shiftDay)
		{
			shift = shift ?? shiftDay?.Shift;

			if (shift == null || shiftDay == null)
				return null;

			if (shift.Groups == null || !shift.Groups.Any())
				return null;

			var schedule = await BuildScheduleForDayAsync(shift, shiftDay);

			return schedule.Needs;
		}

		public async Task<Dictionary<int, Dictionary<int, int>>> GetShiftDayNeedsAsync(int shiftDayId)
		{
			var schedule = await GetShiftDayScheduleAsync(shiftDayId);

			if (schedule?.Shift?.Groups == null || !schedule.Shift.Groups.Any())
				return null;

			return schedule.Needs;
		}

		public async Task<ShiftSignup> SignupForShiftDayAsync(int shiftId, DateTime shiftDay, int departmentGroupId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var shift = await _shiftsRepository.GetByIdAsync(shiftId);

			var signup = new ShiftSignup();
			signup.ShiftId = shiftId;
			signup.ShiftDay = shiftDay;
			signup.SignupTimestamp = DateTime.UtcNow;
			signup.UserId = userId;
			// 0 is how callers say "no group"; storing it breaks the DepartmentGroups foreign key.
			signup.DepartmentGroupId = departmentGroupId > 0 ? departmentGroupId : (int?)null;
			signup.Denied = false;
			signup.ApprovalPending = shift?.RequireApproval == true;

			var saved = await _shiftSignupRepository.SaveOrUpdateAsync(signup, cancellationToken, true);

			if (saved != null && saved.ApprovalPending && shift != null)
				await PublishRosterChangeAsync(shift.DepartmentId, ShiftQueueTypes.SignupPendingApproval, shift.ShiftId, saved.ShiftSignupId, 0, userId);

			return saved;
		}

		public async Task<ShiftSignup> GetShiftSignupByIdAsync(int shiftSignupId)
		{
			var signup = await _shiftSignupRepository.GetByIdAsync(shiftSignupId);

			if (signup != null)
			{
				signup.Trade = await _shiftSignupTradeRepository.GetShiftSignupTradeBySourceShiftSignupIdAsync(signup.ShiftSignupId);

				if (signup.Trade == null)
					signup.Trade = await _shiftSignupTradeRepository.GetShiftSignupTradeByTargetShiftSignupIdAsync(signup.ShiftSignupId);
			}

			return signup;
		}

		/// <summary>
		/// Whether the user is on the day's resolved roster (pending approvals count, so a pending person is not offered
		/// the signup again), optionally only in the given department group.
		/// </summary>
		public async Task<bool> IsUserSignedUpForShiftDayAsync(ShiftDay shiftDay, string userId, int? departmentId)
		{
			if (shiftDay == null || String.IsNullOrWhiteSpace(userId))
				return false;

			var shift = shiftDay.Shift?.Personnel != null ? shiftDay.Shift : await GetShiftByIdAsync(shiftDay.ShiftId);

			if (shift == null)
				return false;

			var schedule = await BuildScheduleForDayAsync(shift, shiftDay);

			return schedule.Roster.Any(x => String.Equals(x.UserId, userId, StringComparison.OrdinalIgnoreCase) &&
			                                (!departmentId.HasValue || x.DepartmentGroupId == departmentId.Value));
		}

		public async Task<List<ShiftSignup>> GetShiftSignpsForShiftDayAsync(int shiftDayId)
		{
			var shiftDay = await _shiftDaysRepository.GetShiftDayByIdAsync(shiftDayId);

			if (shiftDay == null)
				return new List<ShiftSignup>();

			var signups = ((await _shiftSignupRepository.GetAllShiftSignupsByShiftIdAndDateAsync(shiftDay.ShiftId, shiftDay.Day)) ?? Enumerable.Empty<ShiftSignup>()).ToList();

			foreach (var shiftSignup in signups)
			{
				shiftSignup.Trade = await _shiftSignupTradeRepository.GetShiftSignupTradeBySourceShiftSignupIdAsync(shiftSignup.ShiftSignupId);
			}

			return signups;
		}

		public async Task<ShiftDay> GetShiftDayForSignupAsync(int shiftSignupId)
		{
			var shiftSignup = await _shiftSignupRepository.GetByIdAsync(shiftSignupId);

			if (shiftSignup == null)
				return null;

			var shiftDay = ((await _shiftDaysRepository.GetAllShiftDaysByShiftIdAsync(shiftSignup.ShiftId)) ?? Enumerable.Empty<ShiftDay>())
				.FirstOrDefault(x => x.Day.Date == shiftSignup.ShiftDay.Date);

			return shiftDay;
		}

		public async Task<List<ShiftSignup>> GetShiftSignupsForUserAsync(string userId)
		{
			List<ShiftSignup> signups = new List<ShiftSignup>(await _shiftSignupRepository.GetAllShiftSignupsByUserIdAsync(userId));


			// TODO: Need to fix. -SJ
			//var unbalTrades = from trade in _shiftSignupTradeRepository.GetAll()
			//									where trade.UserId == userId
			//									select trade.SourceShiftSignup;

			var unbalTrades = await _shiftSignupTradeRepository.GetTradeRequestsAndSourceShiftsByUserIdAsync(userId);

			// Only trades that took effect put the user on someone else's day; a pick still waiting on a supervisor, or
			// denied, does not.
			if (unbalTrades != null && unbalTrades.Any())
				signups.AddRange(unbalTrades.Where(x => x.IsTradeComplete() && x.SourceShiftSignup != null).Select(x => x.SourceShiftSignup));

			//var trades = from trade in _shiftSignupTradeRepository.GetAll()
			//						 where trade.TargetShiftSignup != null && trade.TargetShiftSignup.UserId == userId
			//						// && !(from s in signups
			//						//			select s.ShiftSignupId).Contains(trade.TargetShiftSignupId.Value) 
			//						 select trade.SourceShiftSignup;

			//if (trades != null && trades.Any())
			//	signups.AddRange(trades);

			//if (signups != null && signups.Any())
			//{
			//	foreach (var shiftSignup in signups)
			//	{
			//		shiftSignup.Trade = await _shiftSignupTradeRepository.GetShiftSignupTradeBySourceShiftSignupIdAsync(shiftSignup.ShiftSignupId);
			//	}
			//}


			if (signups != null && signups.Any())
			{
				foreach (var signup in signups.Where(x => x != null))
				{
					signup.Shift = await GetShiftByIdAsync(signup.ShiftId);

					if (signup.DepartmentGroupId.HasValue)
						signup.Group = await _departmentGroupsService.GetGroupByIdAsync(signup.DepartmentGroupId.Value);

					signup.Trade =
						await _shiftSignupTradeRepository.GetShiftSignupTradeBySourceShiftSignupIdAsync(signup
							.ShiftSignupId);
				}
			}

			return signups.Where(x => x != null && !x.Denied).ToList();
		}

		public async Task<bool> DeleteShiftSignupAsync(ShiftSignup signup, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (signup == null)
				return false;

			// Trades started on this signup cascade away with it; one that uses it as the swap-back day, or an offer that
			// put it up, would otherwise block the delete on a non-cascading foreign key.
			await ReleaseTradeReferencesToSignupAsync(signup.ShiftSignupId, cancellationToken);

			return await _shiftSignupRepository.DeleteAsync(signup, cancellationToken);
		}

		public async Task<ShiftSignupTrade> SaveTradeAsync(ShiftSignupTrade trade, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _shiftSignupTradeRepository.SaveOrUpdateAsync(trade, cancellationToken);
		}

		public async Task<List<ShiftSignupTrade>> GetOpenTradeRequestsForUserAsync(string userId)
		{
			var trades = await _shiftSignupTradeRepository.GetAllOpenTradeRequestsByUserIdAsync(userId);
			var result = new List<ShiftSignupTrade>();

			foreach (var tradeId in (trades ?? Enumerable.Empty<ShiftSignupTrade>()).Select(x => x.ShiftSignupTradeId).Distinct())
			{
				var trade = await GetShiftTradeByIdAsync(tradeId);

				if (trade != null)
					result.Add(trade);
			}

			return result;
		}

		public async Task<ShiftSignupTrade> GetShiftTradeByIdAsync(int shiftTradeId)
		{
			var trade = await _shiftSignupTradeRepository.GetByIdAsync(shiftTradeId);

			// Without this the method throws on an unknown id instead of returning null, so callers
			// have no way to handle a missing trade.
			if (trade == null)
				return null;

			trade.Users = new List<ShiftSignupTradeUser>((await _shiftSignupTradeUserRepository.GetShiftSignupTradeUsersByTradeIdAsync(shiftTradeId)) ?? Enumerable.Empty<ShiftSignupTradeUser>());

			// The notification worker, trade pages and API all read the two signups and their shift; the base
			// GetByIdAsync populates no navigation properties.
			trade.SourceShiftSignup = await _shiftSignupRepository.GetByIdAsync(trade.SourceShiftSignupId);

			if (trade.SourceShiftSignup != null)
				trade.SourceShiftSignup.Shift = await _shiftsRepository.GetByIdAsync(trade.SourceShiftSignup.ShiftId);

			if (trade.TargetShiftSignupId.HasValue)
			{
				trade.TargetShiftSignup = await _shiftSignupRepository.GetByIdAsync(trade.TargetShiftSignupId.Value);

				if (trade.TargetShiftSignup != null)
					trade.TargetShiftSignup.Shift = await _shiftsRepository.GetByIdAsync(trade.TargetShiftSignup.ShiftId);
			}

			return trade;
		}

		public async Task<List<ShiftStaffing>> GetAllShiftStaffingsAsync()
		{
			var items = await _shiftStaffingRepository.GetAllAsync();

			if (items != null && items.Any())
				return items.ToList();

			return new List<ShiftStaffing>();
		}

		public async Task<List<ShiftStaffing>> GetAllShiftStaffingsForDepartmentAsync(int departmentId)
		{
			var items = await _shiftStaffingRepository.GetAllByDepartmentIdAsync(departmentId);

			if (items != null && items.Any())
				return items.ToList();

			return new List<ShiftStaffing>();
		}

		public async Task<ShiftStaffing> GetShiftStaffingByShiftDayAsync(int shiftId, DateTime shiftDay)
		{
			return await _shiftStaffingRepository.GetShiftStaffingByShiftDayAsync(shiftId, shiftDay);
		}

		public async Task<ShiftStaffing> SaveShiftStaffingAsync(ShiftStaffing staffing, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _shiftStaffingRepository.SaveOrUpdateAsync(staffing, cancellationToken);
		}

		public async Task<List<ShiftGroup>> GetShiftGroupsByGroupIdAsync(int departmentGroupId)
		{
			var items = await _shiftGroupsRepository.GetShiftGroupsByGroupIdAsync(departmentGroupId);

			if (items != null && items.Any())
				return items.ToList();

			return new List<ShiftGroup>();
		}


		public async Task<List<ShiftSignup>> GetShiftSignupsByDepartmentGroupIdAndDayAsync(int departmentGroupId, DateTime shiftDayDate)
		{
			var signups = await _shiftSignupRepository.GetAllShiftSignupsByGroupIdAndDateAsync(departmentGroupId, shiftDayDate);
			return signups.ToList();
		}

		public async Task<List<ShiftPerson>> GetShiftPersonsForUserAsync(string userId)
		{
			var persons = await _shiftPersonRepository.GetAllByUserIdAsync(userId);
			if (persons != null)
				return persons.ToList();

			return new List<ShiftPerson>();
		}

		public async Task<List<OnShiftAssignment>> GetOnShiftPersonnelAsync(int departmentId, DateTime utcNow)
		{
			var onShift = new List<OnShiftAssignment>();

			// Built on the resolved roster, so denied and pending signups, single-day removals and completed trades
			// are all applied. One entry per person per running shift.
			foreach (var schedule in await GetActiveShiftDaySchedulesAsync(departmentId, utcNow))
			{
				foreach (var entry in schedule.Roster.Where(x => x.IsOnDuty()))
				{
					if (onShift.Any(x => x.ShiftId == schedule.Shift.ShiftId && String.Equals(x.UserId, entry.UserId, StringComparison.OrdinalIgnoreCase)))
						continue;

					onShift.Add(new OnShiftAssignment
					{
						UserId = entry.UserId,
						ShiftId = schedule.Shift.ShiftId,
						ShiftName = schedule.Shift.Name,
						DepartmentGroupId = entry.DepartmentGroupId
					});
				}
			}

			return onShift;
		}
	}
}
