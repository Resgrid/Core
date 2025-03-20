using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Threading.Tasks;
using Resgrid.Web.Services.Models.v4.Forms;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.UnitRoles;
using Resgrid.Model;
using Resgrid.Web.Helpers;
using Resgrid.Web.Services.Controllers.Version3.Models.Shifts;
using Resgrid.Web.Services.Models.v4.Shifts;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Unit roles
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class ShiftsController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly IShiftsService _shiftsService;
		private readonly IUsersService _usersService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IDepartmentGroupsService _departmentGroupsService;

		public ShiftsController(
			IShiftsService shiftsService,
			IUsersService usersService,
			IDepartmentsService departmentsService,
			IUserProfileService userProfileService,
			IPersonnelRolesService personnelRolesService,
			IDepartmentGroupsService departmentGroupsService
		)
		{
			_usersService = usersService;
			_shiftsService = shiftsService;
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
			_personnelRolesService = personnelRolesService;
			_departmentGroupsService = departmentGroupsService;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Gets all the shifts in a department
		/// </summary>
		/// <returns>List of ShiftResult objects.</returns>
		[HttpGet("GetShifts")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<ActionResult<ShiftsResult>> GetShifts()
		{
			var result = new ShiftsResult();
			var shifts = await _shiftsService.GetAllShiftsByDepartmentAsync(DepartmentId);

			if (shifts != null && shifts.Any())
			{
				foreach (var s in shifts)
				{
					var shiftData = await _shiftsService.PopulateShiftData(s, true, true, true, false, false);

					result.Data.Add(ConvertShiftsResultData(shiftData, UserId));
				}

				result.PageSize = result.Data.Count;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// Gets all the shifts in a department
		/// </summary>
		/// <returns>ShiftResult</returns>
		[HttpGet("GetShift")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<ActionResult<GetShiftResult>> GetShift(int id)
		{
			var result = new GetShiftResult();
			var shift = await _shiftsService.GetShiftByIdAsync(id);

			if (shift != null)
			{
				if (shift.DepartmentId != DepartmentId)
					return Unauthorized();

				var shiftData = await _shiftsService.PopulateShiftData(shift, true, true, true, false, false);

				result.Data = ConvertShiftsResultData(shiftData, UserId);
				result.PageSize = 1;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// Gets all the shift days for today
		/// </summary>
		/// <returns>List of ShiftDayResult objects.</returns>
		[HttpGet("GetTodaysShifts")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<ActionResult<ShiftDaysResult>> GetTodaysShifts()
		{
			var result = new ShiftDaysResult();
			var days = await _shiftsService.GetShiftDaysForDayAsync(DateTime.UtcNow, DepartmentId);

			if (days != null && days.Any())
			{
				foreach (var shiftDay in days)
				{
					shiftDay.Shift = await _shiftsService.GetShiftByIdAsync(shiftDay.ShiftId);
					var needs = await _shiftsService.GetShiftDayNeedsAsync(shiftDay.ShiftDayId);
					var personnelRoles =
						await _personnelRolesService.GetAllRolesForUsersInDepartmentAsync(DepartmentId);
					var signups = await _shiftsService.GetShiftSignpsForShiftDayAsync(shiftDay.ShiftDayId);
					var isUserSignedUp = await _shiftsService.IsUserSignedUpForShiftDayAsync(shiftDay, UserId, null);
					var departmentGroups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);
					var departmentRoles = await _personnelRolesService.GetRolesForDepartmentAsync(DepartmentId);
					
					result.Data.Add(ConvertShiftDayResultData(shiftDay, needs, personnelRoles, signups, isUserSignedUp, departmentGroups, departmentRoles));
				}

				result.PageSize = 1;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// Gets all the shifts in a department
		/// </summary>
		/// <returns>List of ShiftResult objects.</returns>
		[HttpGet("GetShiftDay")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<ActionResult<GetShiftDayResult>> GetShiftDay(int id)
		{
			var result = new GetShiftDayResult();
			var shiftDay = await _shiftsService.GetShiftDayByIdAsync(id);

			if (shiftDay != null)
			{
				var shift = await _shiftsService.GetShiftByIdAsync(shiftDay.ShiftId);
				shiftDay.Shift = shift;

				if (shift.DepartmentId != DepartmentId)
					return Unauthorized();

				var needs = await _shiftsService.GetShiftDayNeedsAsync(id);
				var personnelRoles = await _personnelRolesService.GetAllRolesForUsersInDepartmentAsync(DepartmentId);
				var signups = await _shiftsService.GetShiftSignpsForShiftDayAsync(id);
				var isUserSignedUp = await _shiftsService.IsUserSignedUpForShiftDayAsync(shiftDay, UserId, null);
				var departmentGroups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);
				var departmentRoles = await _personnelRolesService.GetRolesForDepartmentAsync(DepartmentId);

				result.Data = ConvertShiftDayResultData(shiftDay, needs, personnelRoles, signups, isUserSignedUp, departmentGroups, departmentRoles);
				result.PageSize = 1;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		[HttpPost("SignupForShiftDay")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<ActionResult<SignupShiftDayResult>> SignupForShiftDay(ShiftDaySignupInput input)
		{
			var result = new SignupShiftDayResult();
			var shiftDay = await _shiftsService.GetShiftDayByIdAsync(input.ShiftDayId);

			if (shiftDay == null)
				return NotFound();

			if (shiftDay.Shift != null && shiftDay.Shift.DepartmentId != DepartmentId)
				return Unauthorized();

			var signup = await _shiftsService.SignupForShiftDayAsync(shiftDay.ShiftId, shiftDay.Day, input.GroupId, UserId);

			if (signup != null)
			{
				result.Id = signup.ShiftSignupId.ToString();
				result.PageSize = 0;
				result.Status = ResponseHelper.Created;
			}
			else
			{
				result.Id = "";
				result.PageSize = 0;
				result.Status = ResponseHelper.Failure;
			}

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		public static ShiftsResultData ConvertShiftsResultData(Shift shiftData, string currentUserId)
		{
			var shift = new ShiftsResultData();
			shift.ShiftId = shiftData.ShiftId.ToString();
			shift.Name = shiftData.Name;
			shift.Code = shiftData.Code;
			shift.Color = shiftData.Color;
			shift.ScheduleType = shiftData.ScheduleType;
			shift.AssignmentType = shiftData.AssignmentType;

			if (shiftData.Personnel != null)
				shift.PersonnelCount = shiftData.Personnel.Count;

			if (shiftData.Groups != null)
				shift.GroupCount = shiftData.Groups.Count;

			var nextDay = shiftData.GetNextShiftDayforDateTime(DateTime.UtcNow);

			if (nextDay != null)
			{
				shift.NextDay = nextDay.Day.ToString("O");
				shift.NextDayId = nextDay.ShiftDayId.ToString();
			}

			if (shiftData.Personnel != null && shiftData.Personnel.Any(x => x.UserId == currentUserId))
				shift.InShift = true;

			if (shiftData.Days != null && shiftData.Days.Any())
			{
				shift.Days = new List<ShiftDayResultData>();
				foreach (var shiftDay in shiftData.Days)
				{
					var dayResult = new ShiftDayResultData();
					dayResult.ShiftDayId = shiftDay.ShiftDayId.ToString();
					dayResult.ShiftId = shiftDay.ShiftId.ToString(); ;
					dayResult.ShiftName = shiftData.Name;
					dayResult.ShiftDay = shiftDay.Day;
					dayResult.Start = shiftDay.Start;
					dayResult.End = shiftDay.End;
					dayResult.ShiftType = shiftData.AssignmentType;
					dayResult.SignedUp = shift.InShift;
					
					shift.Days.Add(dayResult);
				}
			}

			return shift;
		}
		
		public static ShiftDayResultData ConvertShiftDayResultData(ShiftDay shiftDay, Dictionary<int, Dictionary<int, int>> needs,
			Dictionary<string, List<PersonnelRole>> personnelRoles, List<ShiftSignup> signups, bool isUserSignedUp,
			List<DepartmentGroup> groups, List<PersonnelRole> roles)
		{
			var dayResult = new ShiftDayResultData();

			dayResult.ShiftDayId = shiftDay.ShiftDayId.ToString();
			dayResult.ShiftId = shiftDay.ShiftId.ToString();;
			dayResult.ShiftName = shiftDay.Shift.Name;
			dayResult.ShiftDay = shiftDay.Day;
			dayResult.Start = shiftDay.Start;
			dayResult.End = shiftDay.End;
			dayResult.ShiftType = shiftDay.Shift.AssignmentType;
			dayResult.SignedUp = isUserSignedUp;

			dayResult.Signups = new List<ShiftDaySignupResultData>();
			foreach (var signup in signups)
			{
				if (!signup.Denied)
				{
					var signupResult = new ShiftDaySignupResultData();
					signupResult.UserId = signup.UserId;
					signupResult.Roles = personnelRoles[signup.UserId].Select(x => x.PersonnelRoleId).ToList();

					dayResult.Signups.Add(signupResult);
				}

			}

			if (needs != null && needs.Any())
			{
				dayResult.Needs = new List<ShiftDayGroupNeedsResultData>();
				foreach (var need in needs.Keys)
				{
					var dayNeed = new ShiftDayGroupNeedsResultData();
					dayNeed.GroupId = need.ToString();

					var group = groups.FirstOrDefault(x => x.DepartmentGroupId == need);

					if (group != null)
						dayNeed.GroupName = group.Name;
					
					dayNeed.GroupNeeds = new List<ShiftDayGroupRoleNeedsResultData>();
					foreach (var dNeed in needs[need])
					{
						var groupNeed = new ShiftDayGroupRoleNeedsResultData();
						groupNeed.RoleId = dNeed.Key.ToString();
						
						var role = roles.FirstOrDefault(x => x.PersonnelRoleId == dNeed.Key);

						if (role != null)
							groupNeed.RoleName = role.Name;
						
						groupNeed.Needed = dNeed.Value;

						dayNeed.GroupNeeds.Add(groupNeed);
					}

					dayResult.Needs.Add(dayNeed);
				}
			}

			return dayResult;
		}
	}
}
