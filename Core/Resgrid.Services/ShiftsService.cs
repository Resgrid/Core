using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class ShiftsService : IShiftsService
	{
		private readonly IGenericDataRepository<Shift> _shiftsRepository;
		private readonly IGenericDataRepository<ShiftPerson> _shiftPersonRepository;
		private readonly IGenericDataRepository<ShiftDay> _shiftDaysRepository;
		private readonly IGenericDataRepository<ShiftGroup> _shiftGroupsRepository;
		private readonly IGenericDataRepository<ShiftSignup> _shiftSignupRepository;
		private readonly IGenericDataRepository<ShiftSignupTrade> _shiftSignupTradeRepository;
		private readonly IGenericDataRepository<ShiftSignupTradeUser> _shiftSignupTradeUserRepository;
		private readonly IGenericDataRepository<ShiftSignupTradeUserShift> _shiftSignupTradeUserShiftsRepository;
		private readonly IGenericDataRepository<ShiftStaffing> _shiftStaffingRepository;
		private readonly IGenericDataRepository<ShiftStaffingPerson> _shiftStaffingPersonRepository;
		private readonly IPersonnelRolesService _personnelRolesService;

		public ShiftsService(IGenericDataRepository<Shift> shiftsRepository, IGenericDataRepository<ShiftPerson> shiftPersonRepository,
			IGenericDataRepository<ShiftDay> shiftDaysRepository, IGenericDataRepository<ShiftGroup> shiftGroupsRepository,
			IGenericDataRepository<ShiftSignup> shiftSignupRepository, IGenericDataRepository<ShiftSignupTrade> shiftSignupTradeRepository, IPersonnelRolesService personnelRolesService,
			IGenericDataRepository<ShiftSignupTradeUser> shiftSignupTradeUserRepository, IGenericDataRepository<ShiftSignupTradeUserShift> shiftSignupTradeUserShiftsRepository,
			IGenericDataRepository<ShiftStaffing> shiftStaffingRepository, IGenericDataRepository<ShiftStaffingPerson> shiftStaffingPersonRepository)
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

		}

		public List<Shift> GetAllShiftsByDepartment(int departmentId)
		{
			return _shiftsRepository.GetAll().Where(x => x.DepartmentId == departmentId).ToList();
		}

		public Shift GetShiftById(int shiftId)
		{
			return _shiftsRepository.GetAll().FirstOrDefault(x => x.ShiftId == shiftId);
		}

		public Shift SaveShift(Shift shift)
		{
			_shiftsRepository.SaveOrUpdate(shift);
			return shift;
		}

		public void UpdateShiftPersonnel(Shift shift, List<ShiftPerson> newPersonnel)
		{
			var dbShift = GetShiftById(shift.ShiftId);
			_shiftPersonRepository.DeleteAll(dbShift.Personnel.ToList());

			foreach (var person in newPersonnel)
			{
				person.ShiftId = shift.ShiftId;
				_shiftPersonRepository.SaveOrUpdate(person);
			}
		}

		public void UpdateShiftDates(Shift shift, List<ShiftDay> days)
		{
			// Adding Days
			foreach (var day in days)
			{
				// Don't re-add days already that are apart of the shift
				if (!shift.Days.Any(x => x.Day.Day == day.Day.Day && x.Day.Month == day.Day.Month && x.Day.Year == day.Day.Year))
				{
					day.ShiftId = shift.ShiftId;
					_shiftDaysRepository.SaveOrUpdate(day);
				}
			}

			// Removing Days
			var daysToRemove = from sd in shift.Days
												 let day = days.FirstOrDefault(x => x.Day.Day == sd.Day.Day && x.Day.Month == sd.Day.Month && x.Day.Year == sd.Day.Year)
												 where day == null
												 select sd;

			if (daysToRemove != null && daysToRemove.Any())
				_shiftDaysRepository.DeleteAll(daysToRemove.ToList());
		}

		public void UpdateShiftGroups(Shift shift, List<ShiftGroup> groups)
		{
			_shiftGroupsRepository.DeleteAll(shift.Groups.ToList());

			foreach (var group in groups)
			{
				group.ShiftId = shift.ShiftId;
				_shiftGroupsRepository.SaveOrUpdate(group);
			}
		}

		public void DeleteShift(Shift shift)
		{
			_shiftsRepository.DeleteOnSubmit(shift);
		}

		public void DeleteShiftGroupsByGroupId(int departmentGroupId)
		{
			var groups = _shiftGroupsRepository.GetAll().Where(x => x.DepartmentGroupId == departmentGroupId).ToList();

			foreach (var group in groups)
			{
				_shiftGroupsRepository.DeleteOnSubmit(group);
			}
		}

		public void RejectTradeRequest(int shiftTradeId, string userId, string reason)
		{
			var trade = GetShiftTradeById(shiftTradeId);

			var userTradeRequest = trade.Users.FirstOrDefault(x => x.UserId == userId);

			if (userTradeRequest != null)
			{
				userTradeRequest.Declined = true;
				userTradeRequest.Reason = reason;

				_shiftSignupTradeUserRepository.SaveOrUpdate(userTradeRequest);
			}
		}

		public void ProposeShiftDaysForTrade(int shiftTradeId, string userId, string reason, List<int> signups)
		{
			var trade = GetShiftTradeById(shiftTradeId);

			var userTradeRequest = trade.Users.FirstOrDefault(x => x.UserId == userId);

			if (userTradeRequest != null)
			{
				userTradeRequest.Reason = reason;
				userTradeRequest.Offered = true;

				_shiftSignupTradeUserRepository.SaveOrUpdate(userTradeRequest);

				if (signups != null && signups.Any())
				{
					var shiftSignups = new List<ShiftSignupTradeUserShift>();
					foreach (var i in signups)
					{
						var signup = GetShiftSignupById(i);

						if (signup != null)
						{
							var shift = new ShiftSignupTradeUserShift();
							shift.ShiftSignupTradeUserId = userTradeRequest.ShiftSignupTradeUserId;
							shift.ShiftSignupId = signup.ShiftSignupId;

							shiftSignups.Add(shift);
						}
					}

					if (shiftSignups.Any())
						_shiftSignupTradeUserShiftsRepository.SaveOrUpdateAll(shiftSignups);
				}
			}
		}
		
		public List<Shift> GetShiftsStartingNextDay(DateTime currentTime)
		{
			var upcomingShifts = new List<Shift>();

			var shifts = (from s in _shiftsRepository.GetAll()
						 select s).ToList();

			foreach (var shift in shifts)
			{
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

				if (shiftDays.Any())
				{
					var previousShift = from sd in shift.Days
										where sd.Day.ToShortDateString() == startTime.ToShortDateString()
										select sd;

					if (!previousShift.Any())
						upcomingShifts.Add(shift);
				}
			}

			return upcomingShifts;
		}

		public List<ShiftDay> GetShiftDaysForDay(DateTime currentTime, int departmentId)
		{
			var shiftDays = new List<ShiftDay>();

			var shifts = (from s in _shiftsRepository.GetAll()
						  where s.DepartmentId == departmentId
						  select s).ToList();

			foreach (var shift in shifts)
			{
				var localizedDate = TimeConverterHelper.TimeConverter(currentTime, shift.Department);

				var shiftStart = shift.StartTime;

				if (String.IsNullOrWhiteSpace(shiftStart))
					shiftStart = "12:00 AM";

				var startTime = DateTimeHelpers.ConvertStringTime(shiftStart, localizedDate, shift.Department.Use24HourTime.GetValueOrDefault());

				var days = from sd in shift.Days
								let shiftDayTime = DateTimeHelpers.ConvertStringTime(shiftStart, sd.Day, shift.Department.Use24HourTime.GetValueOrDefault())
								let nextDayShiftTime = localizedDate
								where shiftDayTime == nextDayShiftTime.Within(TimeSpan.FromHours(12))
								select sd;

				shiftDays.AddRange(days);
			}

			return shiftDays;
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
			return string.Format("Shift Trade Request From {0} for {1}", profile.FullName.AsFirstNameLastName, trade.SourceShiftSignup.ShiftDay.ToShortDateString());
		}

		public string GenerateShiftTradeRejectionText(UserProfile profile, ShiftSignupTrade trade)
		{
			return string.Format("{0} Rejected Shift Trade Request for {1}", profile.FullName.AsFirstNameLastName, trade.SourceShiftSignup.ShiftDay.ToShortDateString());
		}

		public string GenerateShiftTradeProposedText(UserProfile profile, ShiftSignupTrade trade)
		{
			return string.Format("{0} Proposed Shift Trades for {1}", profile.FullName.AsFirstNameLastName, trade.SourceShiftSignup.ShiftDay.ToShortDateString());
		}

		public string GenerateShiftTradeFilledText(UserProfile tradeProfile, ShiftSignupTrade trade)
		{
			if (trade.TargetShiftSignup != null)
				return string.Format("{0} accepted trade {1} for {2}", tradeProfile.FullName.AsFirstNameLastName, trade.SourceShiftSignup.ShiftDay.ToShortDateString(), trade.TargetShiftSignup.ShiftDay.ToShortDateString());
			else
				return string.Format("{0} accepted you working {1}", tradeProfile.FullName.AsFirstNameLastName, trade.SourceShiftSignup.ShiftDay.ToShortDateString());
		}

		public ShiftDay GetShiftDayById(int shitDayId)
		{
			return _shiftDaysRepository.GetAll().FirstOrDefault(x => x.ShiftDayId == shitDayId);
		}

		public bool IsShiftDayFilled(int shiftDayId)
		{
			bool isFilled = true;
			var shiftGroups = GetShiftDayNeeds(shiftDayId);

			if (shiftGroups == null)
				return true;

			foreach (var group in shiftGroups)
			{
				foreach (var role in group.Value)
				{
					if (role.Value > 0)
						isFilled = false;
				}
			}

			return isFilled;
		}

		public Dictionary<int, Dictionary<int, int>> GetShiftDayNeeds(int shiftDayId)
		{
			var shiftDay = _shiftDaysRepository.GetAll().FirstOrDefault(x => x.ShiftDayId == shiftDayId);
			var shiftGroups = new Dictionary<int, Dictionary<int, int>>();

			if (shiftDay != null)
			{
				if (shiftDay.Shift.AssignmentType == (int)ShiftAssignmentTypes.Assigned)
					return null;

				if (shiftDay.Shift.Groups == null || shiftDay.Shift.Groups.Count() <= 0)
					return null;

				var shiftSignups =
					_shiftSignupRepository.GetAll().Where(x => x.ShiftId == shiftDay.ShiftId && x.ShiftDay.Year == shiftDay.Day.Year &&
															   x.ShiftDay.Month == shiftDay.Day.Month &&
															   x.ShiftDay.Day == shiftDay.Day.Day);


				foreach (var group in shiftDay.Shift.Groups)
				{
					var roleRequirements = new Dictionary<int, int>();

					foreach (var role in group.Roles)
					{
						roleRequirements.Add(role.PersonnelRoleId, role.Required);
					}

					if (shiftSignups != null && shiftSignups.Any())
					{
						var groupSignups = shiftSignups.Where(x => x.DepartmentGroupId == group.DepartmentGroupId);

						foreach (var signup in groupSignups)
						{
							var roles = _personnelRolesService.GetRolesForUser(signup.UserId, group.DepartmentGroup.DepartmentId);
							foreach (var personnelRole in roles)
							{
								if (roleRequirements.ContainsKey(personnelRole.PersonnelRoleId))
									roleRequirements[personnelRole.PersonnelRoleId]--;
							}
						}
					}

					shiftGroups.Add(group.DepartmentGroupId, roleRequirements);
				}
			}

			return shiftGroups;
		}

		public ShiftSignup SignupForShiftDay(int shiftId, DateTime shiftDay, int departmentGroupId, string userId)
		{
			var signup = new ShiftSignup();
			signup.ShiftId = shiftId;
			signup.ShiftDay = shiftDay;
			signup.SignupTimestamp = DateTime.UtcNow;
			signup.UserId = userId;
			signup.DepartmentGroupId = departmentGroupId;
			signup.Denied = false;

			_shiftSignupRepository.SaveOrUpdate(signup);

			return signup;
		}

		public ShiftSignup GetShiftSignupById(int shiftSignupId)
		{
			var signup = _shiftSignupRepository.GetAll().FirstOrDefault(x => x.ShiftSignupId == shiftSignupId);

			if (signup != null)
			{
				signup.Trade =
					_shiftSignupTradeRepository.GetAll().FirstOrDefault(x => x.SourceShiftSignupId == signup.ShiftSignupId);

				if (signup.Trade == null)
					signup.Trade =
					_shiftSignupTradeRepository.GetAll().FirstOrDefault(x => x.TargetShiftSignupId == signup.ShiftSignupId);
			}

			return signup;
		}

		public bool IsUserSignedUpForShiftDay(ShiftDay shiftDay, string userId)
		{
			var signups = GetShiftSignpsForShiftDay(shiftDay.ShiftDayId);

			if (shiftDay.Shift.Personnel != null && shiftDay.Shift.Personnel.Any())
			{
				if (shiftDay.Shift.Personnel.Any(x => x.UserId == userId))
					return true;
			}

			if (signups == null || !signups.Any())
				return false;

			foreach (var shiftSignup in signups)
			{
				if (shiftSignup.UserId == userId)
					return true;

				if (shiftSignup.Trade != null && shiftSignup.Trade.TargetShiftSignup != null &&
				    shiftSignup.Trade.TargetShiftSignup.UserId == userId)
					return true;
			}

			return false;
		}

		public List<ShiftSignup> GetShiftSignpsForShiftDay(int shiftDayId)
		{
			var shiftDay = _shiftDaysRepository.GetAll().FirstOrDefault(x => x.ShiftDayId == shiftDayId);

			var signups = _shiftSignupRepository.GetAll().Where(x => x.ShiftId == shiftDay.ShiftId && x.ShiftDay.Year == shiftDay.Day.Year &&
															   x.ShiftDay.Month == shiftDay.Day.Month &&
															   x.ShiftDay.Day == shiftDay.Day.Day).ToList();

			if (signups != null && signups.Any())
			{
				foreach (var shiftSignup in signups)
				{
					shiftSignup.Trade = _shiftSignupTradeRepository.GetAll().FirstOrDefault(x => x.SourceShiftSignupId == shiftSignup.ShiftSignupId);
				}
			}

			return signups;
		}

		public ShiftDay GetShiftDayForSignup(int shiftSignupId)
		{
			var shiftSignup = _shiftSignupRepository.GetAll().FirstOrDefault(x => x.ShiftSignupId == shiftSignupId);

			var shiftDay = _shiftDaysRepository.GetAll().FirstOrDefault(x => x.ShiftId == shiftSignup.ShiftId && x.Day.Year == shiftSignup.ShiftDay.Year &&
																 x.Day.Month == shiftSignup.ShiftDay.Month &&
																 x.Day.Day == shiftSignup.ShiftDay.Day);

			return shiftDay;
		}

		public List<ShiftSignup> GetShiftSignupsForUser(string userId)
		{
			var signups = _shiftSignupRepository.GetAll().Where(x => x.UserId == userId).ToList();
			var unbalTrades = from trade in _shiftSignupTradeRepository.GetAll()
												where trade.UserId == userId
												select trade.SourceShiftSignup;

			var trades = from trade in _shiftSignupTradeRepository.GetAll()
									 where trade.TargetShiftSignup != null && trade.TargetShiftSignup.UserId == userId
									// && !(from s in signups
									//			select s.ShiftSignupId).Contains(trade.TargetShiftSignupId.Value) 
									 select trade.SourceShiftSignup;

			if (unbalTrades != null && unbalTrades.Any())
				signups.AddRange(unbalTrades);

			if (trades != null && trades.Any())
				signups.AddRange(trades);

			if (signups != null && signups.Any())
			{
				foreach (var shiftSignup in signups)
				{
					shiftSignup.Trade = _shiftSignupTradeRepository.GetAll().FirstOrDefault(x => x.SourceShiftSignupId == shiftSignup.ShiftSignupId);
				}
			}

			return signups;
		}

		public void DeleteShiftSignup(ShiftSignup signup)
		{
			_shiftSignupRepository.DeleteOnSubmit(signup);
		}

		public ShiftSignupTrade SaveTrade(ShiftSignupTrade trade)
		{
			_shiftSignupTradeRepository.SaveOrUpdate(trade);

			return trade;
		}

		public List<ShiftSignupTrade> GetOpenTradeRequestsForUser(string userId)
		{
			var trades = from trade in _shiftSignupTradeRepository.GetAll()
				where (trade.UserId == null || trade.TargetShiftSignupId == null) && trade.Users.Any(x => x.UserId == userId)
				select trade;

			return trades.ToList();
		}

		public ShiftSignupTrade GetShiftTradeById(int shiftTradeId)
		{
			return _shiftSignupTradeRepository.GetAll().FirstOrDefault(x => x.ShiftSignupTradeId == shiftTradeId);
		}

		public List<ShiftStaffing> GetAllShiftStaffings()
		{
			return _shiftStaffingRepository.GetAll().ToList();
		}

		public List<ShiftStaffing> GetAllShiftStaffingsForDepartment(int departmentId)
		{
			return _shiftStaffingRepository.GetAll().Where(x => x.DepartmentId == departmentId).ToList();
		}

		public ShiftStaffing GetShiftStaffingByShiftDay(int shiftId, DateTime shiftDay)
		{
			return _shiftStaffingRepository.GetAll().FirstOrDefault(x => x.ShiftId == shiftId && x.ShiftDay == shiftDay);
		}

		public ShiftStaffing SaveShiftStaffing(ShiftStaffing staffing)
		{
			_shiftStaffingRepository.SaveOrUpdate(staffing);

			return staffing;
		}
	}
}