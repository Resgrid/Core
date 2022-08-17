using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.Version3.Models.Shifts;


namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Operations to perform against shifts in a department
	/// </summary>
	[Route("api/v{version:ApiVersion}/[controller]")]
	[Produces("application/json")]
	[ApiVersion("3.0")]
	[ApiExplorerSettings(GroupName = "v3")]
	public class ShiftsController : V3AuthenticatedApiControllerbase
	{
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

		/// <summary>
		/// Get's all the shifts in a department
		/// </summary>
		/// <returns>List of ShiftResult objects.</returns>
		[HttpGet("GetShifts")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<List<ShiftResult>>> GetShifts()
		{
			var results = new List<ShiftResult>();

			var shifts = await _shiftsService.GetAllShiftsByDepartmentAsync(DepartmentId);
		
			foreach (var s in shifts)
			{
				var shiftData = await _shiftsService.PopulateShiftData(s, true, true, true, false, false);

				var shift = new ShiftResult();
				shift.Id = shiftData.ShiftId;
				shift.Name = shiftData.Name;
				shift.Code = shiftData.Code;
				shift.Color = shiftData.Color;
				shift.SType = shiftData.ScheduleType;
				shift.AType = shiftData.AssignmentType;

				if (shiftData.Personnel != null)
					shift.PCount = shiftData.Personnel.Count;

				if (shiftData.Groups != null)
					shift.GCount = shiftData.Groups.Count;

				var nextDay = shiftData.GetNextShiftDayforDateTime(DateTime.UtcNow);

				if (nextDay != null)
				{
					shift.NextDay = nextDay.Day.ToString("O");
					shift.NextDayId = nextDay.ShiftDayId;
				}

				if (s.Personnel != null && shiftData.Personnel.Any(x => x.UserId == UserId))
					shift.InShift = true;

				results.Add(shift);
			}
			
			return Ok(results);
		}

		/// <summary>
		/// Get's all the shifts in a department
		/// </summary>
		/// <returns>List of ShiftResult objects.</returns>
		[HttpGet("GetShift")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		public async Task<ActionResult> GetShift(int id)
		{
			var shift = await _shiftsService.GetShiftByIdAsync(id);

			if (shift != null)
				if (shift.DepartmentId != DepartmentId)
					return Unauthorized();

			return Ok(shift);
		}

		/// <summary>
		/// Get's all the shift days for today
		/// </summary>
		/// <returns>List of ShiftDayResult objects.</returns>
		[HttpGet("GetTodaysShifts")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<List<ShiftDayResult>>> GetTodaysShifts()
		{
			var result = new List<ShiftDayResult>();
			var days = await _shiftsService.GetShiftDaysForDayAsync(DateTime.UtcNow, DepartmentId);

			foreach (var shiftDay in days)
			{
				shiftDay.Shift = await _shiftsService.GetShiftByIdAsync(shiftDay.ShiftId);
				var dayResult = new ShiftDayResult();

				dayResult.ShiftDayId = shiftDay.ShiftDayId;
				dayResult.ShiftId = shiftDay.ShiftId;
				dayResult.ShiftName = shiftDay.Shift.Name;
				dayResult.ShitDay = shiftDay.Day;
				dayResult.Start = shiftDay.Start;
				dayResult.End = shiftDay.End;
				dayResult.ShiftType = shiftDay.Shift.AssignmentType;

				var needs = await _shiftsService.GetShiftDayNeedsAsync(shiftDay.ShiftDayId);
				var personnelRoles = await _personnelRolesService.GetAllRolesForUsersInDepartmentAsync(DepartmentId);
				var signups = await _shiftsService.GetShiftSignpsForShiftDayAsync(shiftDay.ShiftDayId);

				dayResult.SignedUp = await _shiftsService.IsUserSignedUpForShiftDayAsync(shiftDay, UserId, null);

				dayResult.Signups = new List<ShiftDaySignupResult>();
				foreach (var signup in signups)
				{
					if (!signup.Denied)
					{
						var signupResult = new ShiftDaySignupResult();
						signupResult.UserId = signup.UserId;
						signupResult.Roles = personnelRoles[signup.UserId].Select(x => x.PersonnelRoleId).ToList();

						dayResult.Signups.Add(signupResult);
					}

				}

				if (needs != null && needs.Any())
				{
					dayResult.Needs = new List<ShiftDayGroupNeedsResult>();
					foreach (var need in needs.Keys)
					{
						var dayNeed = new ShiftDayGroupNeedsResult();
						dayNeed.GroupId = need;

						dayNeed.GroupNeeds = new List<ShiftDayGroupRoleNeedsResult>();
						foreach (var dNeed in needs[need])
						{
							var groupNeed = new ShiftDayGroupRoleNeedsResult();
							groupNeed.RoleId = dNeed.Key;
							groupNeed.Needed = dNeed.Value;

							dayNeed.GroupNeeds.Add(groupNeed);
						}

						dayResult.Needs.Add(dayNeed);
					}
				}

				result.Add(dayResult);
			}

			return Ok(result);
		}

		/// <summary>
		/// Get's all the shifts in a department
		/// </summary>
		/// <returns>List of ShiftResult objects.</returns>
		[HttpGet("GetShiftDay")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ShiftDayResult>> GetShiftDay(int id)
		{
			var result = new ShiftDayResult();

			var shiftDay = await _shiftsService.GetShiftDayByIdAsync(id);

			result.ShiftDayId = id;
			result.ShiftId = shiftDay.ShiftId;
			result.ShiftName = shiftDay.Shift.Name;
			result.ShitDay = shiftDay.Day;
			result.Start = shiftDay.Start;
			result.End = shiftDay.End;
			result.ShiftType = shiftDay.Shift.AssignmentType;

			var needs = await _shiftsService.GetShiftDayNeedsAsync(id);
			var personnelRoles = await _personnelRolesService.GetAllRolesForUsersInDepartmentAsync(DepartmentId);
			var signups = await _shiftsService.GetShiftSignpsForShiftDayAsync(id);

			result.SignedUp = await _shiftsService.IsUserSignedUpForShiftDayAsync(shiftDay, UserId, null);

			result.Signups = new List<ShiftDaySignupResult>();
			foreach (var signup in signups)
			{
				if (!signup.Denied)
				{ 
					var signupResult = new ShiftDaySignupResult();
					signupResult.UserId = signup.UserId;
					signupResult.Roles = personnelRoles[signup.UserId].Select(x => x.PersonnelRoleId).ToList();

					result.Signups.Add(signupResult);
				}

			}

			if (needs != null && needs.Any())
			{
				result.Needs = new List<ShiftDayGroupNeedsResult>();
				foreach (var need in needs.Keys)
				{
					var dayNeed = new ShiftDayGroupNeedsResult();
					dayNeed.GroupId = need;

					dayNeed.GroupNeeds = new List<ShiftDayGroupRoleNeedsResult>();
					foreach (var dNeed in needs[need])
					{
						var groupNeed = new ShiftDayGroupRoleNeedsResult();
						groupNeed.RoleId = dNeed.Key;
						groupNeed.Needed = dNeed.Value;

						dayNeed.GroupNeeds.Add(groupNeed);
					}

					result.Needs.Add(dayNeed);
				}
			}

			return Ok(result);
		}

		[HttpPost("SignupForShiftDay")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult> SignupForShiftDay(ShiftDaySignupInput input)
		{
			var shiftDay = await _shiftsService.GetShiftDayByIdAsync(input.ShiftDayId);

			if (shiftDay == null)
				return NotFound();

			if (shiftDay.Shift != null && shiftDay.Shift.DepartmentId != DepartmentId)
				return Unauthorized();

			var signup = await _shiftsService.SignupForShiftDayAsync(shiftDay.ShiftId, shiftDay.Day, input.GroupId, UserId);

			return CreatedAtAction(nameof(SignupForShiftDay), new { id = signup.ShiftSignupId }, signup);
		}
	}
}
