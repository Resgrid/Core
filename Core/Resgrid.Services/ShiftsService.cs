using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class ShiftsService : IShiftsService
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

		public ShiftsService(IShiftsRepository shiftsRepository, IShiftPersonRepository shiftPersonRepository,
			IShiftDaysRepository shiftDaysRepository, IShiftGroupsRepository shiftGroupsRepository,
			IShiftSignupRepository shiftSignupRepository, IShiftSignupTradeRepository shiftSignupTradeRepository, IPersonnelRolesService personnelRolesService,
			IShiftSignupTradeUserRepository shiftSignupTradeUserRepository, IShiftSignupTradeUserShiftsRepository shiftSignupTradeUserShiftsRepository,
			IShiftStaffingRepository shiftStaffingRepository, IShiftStaffingPersonRepository shiftStaffingPersonRepository, IDepartmentsService departmentsService,
			IDepartmentGroupsService departmentGroupsService, IShiftGroupAssignmentsRepository shiftGroupAssignmentsRepository, IShiftGroupRolesRepository shiftGroupRolesRepositor)
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

			if (getPersonnel && shift.Personnel == null)
				shift.Personnel = (await _shiftPersonRepository.GetAllShiftPersonsByShiftIdAsync(shift.ShiftId)).ToList();
			else
				shift.Personnel = new List<ShiftPerson>();

			if (getGroups && shift.Groups == null)
				shift.Groups = await GetShiftGroupsForShift(shift.ShiftId);

			if (getSignups && shift.Signups == null)
				shift.Signups = (await _shiftSignupRepository.GetAllShiftSignupsByShiftIdAsync(shift.ShiftId)).ToList();

			return shift;
		}

		public async Task<Shift> SaveShiftAsync(Shift shift, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _shiftsRepository.SaveOrUpdateAsync(shift, cancellationToken);
		}

		public async Task<List<ShiftGroup>> GetShiftGroupsForShift(int shiftId)
		{
			var groups = await _shiftGroupsRepository.GetShiftGroupsByShiftIdAsync(shiftId);

			if (groups != null && groups.Any())
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

			foreach (var shiftPerson in dbShift.Personnel)
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
			// Adding Days
			foreach (var day in days)
			{
				// Don't re-add days already that are apart of the shift
				if (!shift.Days.Any(x => x.Day.Day == day.Day.Day && x.Day.Month == day.Day.Month && x.Day.Year == day.Day.Year))
				{
					day.ShiftId = shift.ShiftId;
					await _shiftDaysRepository.SaveOrUpdateAsync(day, cancellationToken);
				}
			}

			// Removing Days
			var daysToRemove = from sd in shift.Days
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

			var userTradeRequest = trade.Users.FirstOrDefault(x => x.UserId == userId);

			if (userTradeRequest != null)
			{
				userTradeRequest.Declined = true;
				userTradeRequest.Reason = reason;

				await _shiftSignupTradeUserRepository.SaveOrUpdateAsync(userTradeRequest, cancellationToken);
			}

			return true;
		}

		public async Task<bool> ProposeShiftDaysForTradeAsync(int shiftTradeId, string userId, string reason, List<int> signups, CancellationToken cancellationToken = default(CancellationToken))
		{
			var trade = await GetShiftTradeByIdAsync(shiftTradeId);

			var userTradeRequest = trade.Users.FirstOrDefault(x => x.UserId == userId);

			if (userTradeRequest != null)
			{
				userTradeRequest.Reason = reason;
				userTradeRequest.Offered = true;

				await _shiftSignupTradeUserRepository.SaveOrUpdateAsync(userTradeRequest, cancellationToken);

				if (signups != null && signups.Any())
				{
					var shiftSignups = new List<ShiftSignupTradeUserShift>();
					foreach (var i in signups)
					{
						var signup = await GetShiftSignupByIdAsync(i);

						if (signup != null)
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

			var shifts = await _shiftsRepository.GetAllShiftAndDaysAsync();

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

			var shifts = await _shiftsRepository.GetAllByDepartmentIdAsync(departmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);

			foreach (var shift in shifts)
			{
				shift.Days = new List<ShiftDay>(await _shiftDaysRepository.GetAllShiftDaysByShiftIdAsync(shift.ShiftId));

				var localizedDate = currentTime.TimeConverter(department);

				var shiftStart = shift.StartTime;

				if (String.IsNullOrWhiteSpace(shiftStart))
					shiftStart = "12:00 AM";

				var startTime = DateTimeHelpers.ConvertStringTime(shiftStart, localizedDate, department.Use24HourTime.GetValueOrDefault());

				var days = from sd in shift.Days
						   let shiftDayTime = DateTimeHelpers.ConvertStringTime(shiftStart, sd.Day, department.Use24HourTime.GetValueOrDefault())
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

		public async Task<ShiftDay> GetShiftDayByIdAsync(int shiftDayId)
		{
			return await _shiftDaysRepository.GetShiftDayByIdAsync(shiftDayId);
		}

		public async Task<bool> IsShiftDayFilledAsync(int shiftDayId)
		{
			bool isFilled = true;
			var shiftGroups = await GetShiftDayNeedsAsync(shiftDayId);

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

		public async Task<bool> IsShiftDayFilledWithObjAsync(Shift shift, ShiftDay shiftDay)
		{
			bool isFilled = true;
			var shiftGroups = await GetShiftDayNeedsObjAsync(shift, shiftDay);

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

		public async Task<Dictionary<int, Dictionary<int, int>>> GetShiftDayNeedsObjAsync(Shift shift, ShiftDay shiftDay)
		{
			//var shiftDay = await _shiftDaysRepository.GetShiftDayByIdAsync(shiftDayId);
			var shiftGroups = new Dictionary<int, Dictionary<int, int>>();

			if (shiftDay != null)
			{
				if (shiftDay.Shift.AssignmentType == (int)ShiftAssignmentTypes.Assigned)
					return null;

				//shiftDay.Shift.Groups = (await _shiftGroupsRepository.GetShiftGroupsByShiftIdAsync(shiftDay.ShiftId)).ToList();
				if (shiftDay.Shift.Groups == null || shiftDay.Shift.Groups.Count() <= 0)
					return null;

				var shiftSignups =
					(await _shiftSignupRepository.GetAllShiftSignupsByShiftIdAndDateAsync(shiftDay.ShiftId,
						shiftDay.Day)).ToList();


				foreach (var group in shiftDay.Shift.Groups)
				{
					var roleRequirements = new Dictionary<int, int>();

					if (group.Roles != null && group.Roles.Any())
					{
						foreach (var role in group.Roles)
						{
							roleRequirements.Add(role.PersonnelRoleId, role.Required);
						}
					}

					if (shiftSignups != null && shiftSignups.Any())
					{
						var groupSignups = shiftSignups.Where(x => x.DepartmentGroupId == group.DepartmentGroupId);

						foreach (var signup in groupSignups)
						{
							var roles = await _personnelRolesService.GetRolesForUserAsync(signup.UserId, shiftDay.Shift.DepartmentId);
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

		public async Task<Dictionary<int, Dictionary<int, int>>> GetShiftDayNeedsAsync(int shiftDayId)
		{
			var shiftDay = await _shiftDaysRepository.GetShiftDayByIdAsync(shiftDayId);
			var shiftGroups = new Dictionary<int, Dictionary<int, int>>();

			if (shiftDay != null)
			{
				if (shiftDay.Shift.AssignmentType == (int)ShiftAssignmentTypes.Assigned)
					return null;

				shiftDay.Shift.Groups = (await _shiftGroupsRepository.GetShiftGroupsByShiftIdAsync(shiftDay.ShiftId)).ToList();
				if (shiftDay.Shift.Groups == null || shiftDay.Shift.Groups.Count() <= 0)
					return null;

				var shiftSignups =
					(await _shiftSignupRepository.GetAllShiftSignupsByShiftIdAndDateAsync(shiftDay.ShiftId,
						shiftDay.Day)).ToList();


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
							var roles = await _personnelRolesService.GetRolesForUserAsync(signup.UserId, shiftDay.Shift.DepartmentId);
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

		public async Task<ShiftSignup> SignupForShiftDayAsync(int shiftId, DateTime shiftDay, int departmentGroupId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var signup = new ShiftSignup();
			signup.ShiftId = shiftId;
			signup.ShiftDay = shiftDay;
			signup.SignupTimestamp = DateTime.UtcNow;
			signup.UserId = userId;
			signup.DepartmentGroupId = departmentGroupId;
			signup.Denied = false;

			return await _shiftSignupRepository.SaveOrUpdateAsync(signup, cancellationToken);
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

		public async Task<bool> IsUserSignedUpForShiftDayAsync(ShiftDay shiftDay, string userId, int? departmentId)
		{
			var signups = await GetShiftSignpsForShiftDayAsync(shiftDay.ShiftDayId);

			if (shiftDay.Shift.Personnel != null && shiftDay.Shift.Personnel.Any())
			{
				if (shiftDay.Shift.Personnel.Any(x => x.UserId == userId))
					return true;
			}

			if (signups == null || !signups.Any())
				return false;

			foreach (var shiftSignup in signups)
			{
				if (departmentId.HasValue)
				{
					if (shiftSignup.UserId == userId && shiftSignup.DepartmentGroupId == departmentId.Value)
						return true;
				}
				else
				{
					if (shiftSignup.UserId == userId)
						return true;
				}

				if (shiftSignup.Trade != null && shiftSignup.Trade.TargetShiftSignup != null &&
					shiftSignup.Trade.TargetShiftSignup.UserId == userId)
					return true;
			}

			return false;
		}

		public async Task<List<ShiftSignup>> GetShiftSignpsForShiftDayAsync(int shiftDayId)
		{
			var shiftDay = await _shiftDaysRepository.GetShiftDayByIdAsync(shiftDayId);

			var signups = (await _shiftSignupRepository.GetAllShiftSignupsByShiftIdAsync(shiftDay.ShiftId)).Where(x => x.ShiftDay.Year == shiftDay.Day.Year &&
																													  x.ShiftDay.Month == shiftDay.Day.Month &&
																													  x.ShiftDay.Day == shiftDay.Day.Day).ToList();

			if (signups != null && signups.Any())
			{
				foreach (var shiftSignup in signups)
				{
					shiftSignup.Trade = await _shiftSignupTradeRepository.GetShiftSignupTradeBySourceShiftSignupIdAsync(shiftSignup.ShiftSignupId);
				}
			}

			return signups;
		}

		public async Task<ShiftDay> GetShiftDayForSignupAsync(int shiftSignupId)
		{
			var shiftSignup = await _shiftSignupRepository.GetByIdAsync(shiftSignupId);

			var shiftDay = (await _shiftDaysRepository.GetAllShiftDaysByShiftIdAsync(shiftSignup.ShiftId)).FirstOrDefault(x => x.Day.Year == shiftSignup.ShiftDay.Year &&
																													   x.Day.Month == shiftSignup.ShiftDay.Month &&
																													   x.Day.Day == shiftSignup.ShiftDay.Day);
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

			if (unbalTrades != null && unbalTrades.Any())
				signups.AddRange(unbalTrades.Select(x => x.SourceShiftSignup));

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
				foreach (var signup in signups)
				{
					signup.Shift = await GetShiftByIdAsync(signup.ShiftId);

					if (signup.DepartmentGroupId.HasValue)
						signup.Group = await _departmentGroupsService.GetGroupByIdAsync(signup.DepartmentGroupId.Value);

					signup.Trade =
						await _shiftSignupTradeRepository.GetShiftSignupTradeBySourceShiftSignupIdAsync(signup
							.ShiftSignupId);
				}
			}

			return signups.ToList();
		}

		public async Task<bool> DeleteShiftSignupAsync(ShiftSignup signup, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _shiftSignupRepository.DeleteAsync(signup, cancellationToken);
		}

		public async Task<ShiftSignupTrade> SaveTradeAsync(ShiftSignupTrade trade, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _shiftSignupTradeRepository.SaveOrUpdateAsync(trade, cancellationToken);
		}

		public async Task<List<ShiftSignupTrade>> GetOpenTradeRequestsForUserAsync(string userId)
		{
			var trades = await _shiftSignupTradeRepository.GetAllOpenTradeRequestsByUserIdAsync(userId);
			return trades.ToList();
		}

		public async Task<ShiftSignupTrade> GetShiftTradeByIdAsync(int shiftTradeId)
		{
			var trade = await _shiftSignupTradeRepository.GetByIdAsync(shiftTradeId);
			trade.Users = new List<ShiftSignupTradeUser>(await _shiftSignupTradeUserRepository.GetShiftSignupTradeUsersByTradeIdAsync(shiftTradeId));

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
	}
}
