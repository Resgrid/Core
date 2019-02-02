using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Http.Cors;
using System.Web.Mvc;
using Newtonsoft.Json;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.Version3.Models.Shifts;
using Resgrid.Web.Services.Helpers;

namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Operations to perform against shifts in a department
	/// </summary>
	[EnableCors(origins: "*", headers: "*", methods: "GET,POST,PUT,DELETE,OPTIONS")]
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
		[System.Web.Http.AcceptVerbs("GET")]
		public List<ShiftResult> GetShifts()
		{
			var results = new List<ShiftResult>();

			var shifts = _shiftsService.GetAllShiftsByDepartment(DepartmentId);
		
			foreach (var s in shifts)
			{
				var shift = new ShiftResult();
				shift.Id = s.ShiftId;
				shift.Name = s.Name;
				shift.Code = s.Code;
				shift.Color = s.Color;
				shift.SType = s.ScheduleType;
				shift.AType = s.AssignmentType;

				if (s.Personnel != null)
					shift.PCount = s.Personnel.Count;

				if (s.Groups != null)
					shift.GCount = s.Groups.Count;

				var nextDay = s.GetNextShiftDayforDateTime(DateTime.UtcNow);

				if (nextDay != null)
				{
					shift.NextDay = nextDay.Day.ToString("O");
					shift.NextDayId = nextDay.ShiftDayId;
				}

				if (s.Personnel != null && s.Personnel.Any(x => x.UserId == UserId))
					shift.InShift = true;

				results.Add(shift);
			}
			
			return results;
		}

		/// <summary>
		/// Get's all the shifts in a department
		/// </summary>
		/// <returns>List of ShiftResult objects.</returns>
		[System.Web.Http.AcceptVerbs("GET")]
		public HttpResponseMessage GetShift(int id)
		{
			var shift = _shiftsService.GetShiftById(id);

			if (shift != null)
				if (shift.DepartmentId != DepartmentId)
					return Request.CreateResponse(HttpStatusCode.Unauthorized);

			return new HttpResponseMessage()
			{
				Content = new StringContent(JsonConvert.SerializeObject(shift), Encoding.UTF8, "application/json")
			};
		}

		/// <summary>
		/// Get's all the shift days for today
		/// </summary>
		/// <returns>List of ShiftDayResult objects.</returns>
		[System.Web.Http.AcceptVerbs("GET")]
		public List<ShiftDayResult> GetTodaysShifts()
		{
			var result = new List<ShiftDayResult>();
			var days = _shiftsService.GetShiftDaysForDay(DateTime.UtcNow, DepartmentId);

			foreach (var shiftDay in days)
			{
				var dayResult = new ShiftDayResult();

				dayResult.ShiftDayId = shiftDay.ShiftDayId;
				dayResult.ShiftId = shiftDay.ShiftId;
				dayResult.ShiftName = shiftDay.Shift.Name;
				dayResult.ShitDay = shiftDay.Day;
				dayResult.Start = shiftDay.Start;
				dayResult.End = shiftDay.End;
				dayResult.ShiftType = shiftDay.Shift.AssignmentType;

				var needs = _shiftsService.GetShiftDayNeeds(shiftDay.ShiftDayId);
				var personnelRoles = _personnelRolesService.GetAllRolesForUsersInDepartment(DepartmentId);
				var signups = _shiftsService.GetShiftSignpsForShiftDay(shiftDay.ShiftDayId);

				dayResult.SignedUp = _shiftsService.IsUserSignedUpForShiftDay(shiftDay, UserId);

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

			return result;
		}

		/// <summary>
		/// Get's all the shifts in a department
		/// </summary>
		/// <returns>List of ShiftResult objects.</returns>
		[System.Web.Http.AcceptVerbs("GET")]
		public ShiftDayResult GetShiftDay(int id)
		{
			var result = new ShiftDayResult();

			var shiftDay = _shiftsService.GetShiftDayById(id);

			result.ShiftDayId = id;
			result.ShiftId = shiftDay.ShiftId;
			result.ShiftName = shiftDay.Shift.Name;
			result.ShitDay = shiftDay.Day;
			result.Start = shiftDay.Start;
			result.End = shiftDay.End;
			result.ShiftType = shiftDay.Shift.AssignmentType;

			var needs = _shiftsService.GetShiftDayNeeds(id);
			var personnelRoles = _personnelRolesService.GetAllRolesForUsersInDepartment(DepartmentId);
			var signups = _shiftsService.GetShiftSignpsForShiftDay(id);

			result.SignedUp = _shiftsService.IsUserSignedUpForShiftDay(shiftDay, UserId);

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

			return result;
		}

		[AcceptVerbs("POST")]
		public HttpResponseMessage SignupForShiftDay(ShiftDaySignupInput input)
		{
			var shiftDay = _shiftsService.GetShiftDayById(input.ShiftDayId);

			if (shiftDay == null)
				throw HttpStatusCode.NotFound.AsException();

			if (shiftDay.Shift != null && shiftDay.Shift.DepartmentId != DepartmentId)
				throw HttpStatusCode.Unauthorized.AsException();

			_shiftsService.SignupForShiftDay(shiftDay.ShiftId, shiftDay.Day, input.GroupId, UserId);

			return Request.CreateResponse(HttpStatusCode.Created);
		}

		public HttpResponseMessage Options()
		{
			var response = new HttpResponseMessage();
			response.StatusCode = HttpStatusCode.OK;
			response.Headers.Add("Access-Control-Allow-Origin", "*");
			response.Headers.Add("Access-Control-Request-Headers", "*");
			response.Headers.Add("Access-Control-Allow-Methods", "GET,POST,PUT,DELETE,OPTIONS");

			return response;
		}
	}
}
