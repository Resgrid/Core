using System.Net;
using System.Net.Http;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using System;
using System.Collections.Generic;
using System.Text;
using System.Web.Http;
using System.Web.Http.Cors;
using Resgrid.Web.Services.Controllers.Version3.Models.StaffingSchedules;
using System.Globalization;
using Resgrid.Web.Services.Helpers;

namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Operations to perform against user statuses and their actions
	/// </summary>
	[EnableCors(origins: "*", headers: "*", methods: "GET,POST,PUT,DELETE,OPTIONS")]
	public class StaffingSchedulesController : V3AuthenticatedApiControllerbase
	{
		private readonly IUsersService _usersService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUserStateService _userStateService;
		private readonly IUserProfileService _userProfileService;
		private readonly IAuthorizationService _authorizationService;
		private readonly IScheduledTasksService _scheduledTasksService;

		public StaffingSchedulesController(
			IUsersService usersService,
			IDepartmentsService departmentsService,
			IUserStateService userStateService,
			IUserProfileService userProfileService,
			IAuthorizationService authorizationService,
			IScheduledTasksService scheduledTasksService)
		{
			_usersService = usersService;
			_departmentsService = departmentsService;
			_userStateService = userStateService;
			_userProfileService = userProfileService;
			_authorizationService = authorizationService;
			_scheduledTasksService = scheduledTasksService;
		}

		/// <summary>
		/// Gets the current staffing level (state) for the user
		/// </summary>
		/// <returns>StateResult object with the users current staffing level</returns>
		public List<StaffingScheduleResult> GetStaffingSchedules()
		{
			var results = new List<StaffingScheduleResult>();

			var department = _departmentsService.GetDepartmentById(DepartmentId, false);
			var tasks = _scheduledTasksService.GetScheduledStaffingTasksForUser(UserId);

			foreach (var task in tasks)
			{
				var st = new StaffingScheduleResult();
				st.Id = task.ScheduledTaskId;
				if (task.ScheduleType == (int)ScheduleTypes.Weekly)
					st.Typ = "Weekly";
				else
					st.Typ = "Date";

				if (task.SpecifcDate.HasValue)
					st.Spc = TimeConverterHelper.TimeConverter(task.SpecifcDate.Value, department);

				st.Act = task.Active;

				var days = new StringBuilder();

				if (task.Monday)
					days.Append("M");

				if (task.Tuesday)
					days.Append("T");

				if (task.Wednesday)
					days.Append("W");

				if (task.Thursday)
					days.Append("Th");

				if (task.Friday)
					days.Append("F");

				if (task.Saturday)
					days.Append("S");

				if (task.Sunday)
					days.Append("Su");

				days.Append(" @ " + task.Time);

				if (task.ScheduleType == (int)ScheduleTypes.Weekly)
					st.Dow = days.ToString();

				st.Data = task.Data;

				results.Add(st);
			}

			return results;
		}

		/// <summary>
		/// Toggles and Scheduled Staffing Level Change (Enabling or Disabling It)
		/// </summary>
		/// <param name="toggleInput">StateInput object with the Staffing to toggle and it's value.</param>
		/// <returns>Returns HttpStatusCode Created if successful, BadRequest otherwise.</returns>
		[AcceptVerbs("PUT")]
		public HttpResponseMessage ToggleStaffingSchedule(ToggleStaffingScheduleInput toggleInput)
		{
			if (this.ModelState.IsValid)
			{
				try
				{
					var staffingSchedule = _scheduledTasksService.GetScheduledTaskById(toggleInput.Id);

					if (staffingSchedule == null)
						throw HttpStatusCode.NotFound.AsException();

					if (staffingSchedule.UserId != UserId)
						throw HttpStatusCode.Unauthorized.AsException();

					staffingSchedule.Active = toggleInput.Act;
					_scheduledTasksService.SaveScheduledTask(staffingSchedule);

					return Request.CreateResponse(HttpStatusCode.Created);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
					throw HttpStatusCode.InternalServerError.AsException();
				}
			}

			throw HttpStatusCode.BadRequest.AsException();
		}

		/// <summary>
		/// Toggles and Scheduled Staffing Level Change (Enabling or Disabling It)
		/// </summary>
		/// <param name="toggleInput">StateInput object with the Staffing to toggle and it's value.</param>
		/// <returns>Returns HttpStatusCode Created if successful, BadRequest otherwise.</returns>
		[AcceptVerbs("POST")]
		public HttpResponseMessage CreateStaffingSchedule(StaffingScheduleInput staffingScheduleInput)
		{
			if (this.ModelState.IsValid)
			{
				try
				{
					var task = new ScheduledTask();
					task.UserId = UserId;

					if (staffingScheduleInput.Typ == 1)
					{
						var department = _departmentsService.GetDepartmentById(DepartmentId);
						DateTime inputDate = DateTime.Parse(staffingScheduleInput.Spd);
						
						task.ScheduleType = (int)ScheduleTypes.SpecifcDateTime;

						string[] timeParts = new List<string>().ToArray();
						//if (staffingScheduleInput.Spt.Contains("AM") || staffingScheduleInput.Spt.Contains("PM"))
						//{
							timeParts = staffingScheduleInput.Spt.Split(char.Parse(":"));
						//}
						//else
						//{
						//	var timeFromInput = DateTime.ParseExact(staffingScheduleInput.Spt, "H:m", null, DateTimeStyles.None);
						//	string time12Hour = timeFromInput.ToString("t", CultureInfo.CreateSpecificCulture("en-us"));
						//	timeParts = time12Hour.Split(char.Parse(":"));
						//}

						var dateOffset = new DateTimeOffset(inputDate.Year, inputDate.Month, inputDate.Day, int.Parse(timeParts[0]), int.Parse(timeParts[1]), 0, TimeConverterHelper.GetOffsetForDepartment(department));

						task.SpecifcDate = dateOffset.UtcDateTime;
					}
					else
					{
						task.ScheduleType = (int)ScheduleTypes.Weekly;

						if (staffingScheduleInput.Spt.Contains("AM") || staffingScheduleInput.Spt.Contains("PM"))
						{
							task.Time = staffingScheduleInput.Spt;
						}
						else
						{
							var timeFromInput = DateTime.ParseExact(staffingScheduleInput.Spt, "H:m", null, DateTimeStyles.None);
							string time12Hour = timeFromInput.ToString("t", CultureInfo.CreateSpecificCulture("en-us"));

							task.Time = time12Hour;
						}
					}

					task.Sunday = staffingScheduleInput.Sun;
					task.Monday = staffingScheduleInput.Mon;
					task.Tuesday = staffingScheduleInput.Tue;
					task.Wednesday = staffingScheduleInput.Wed;
					task.Thursday = staffingScheduleInput.Thu;
					task.Friday = staffingScheduleInput.Fri;
					task.Saturday = staffingScheduleInput.Sat;
					task.AddedOn = DateTime.UtcNow;
					task.Active = true;
					task.Data = staffingScheduleInput.Ste;
					task.TaskType = (int)TaskTypes.UserStaffingLevel;
					task.Note = staffingScheduleInput.Not;

					_scheduledTasksService.SaveScheduledTask(task);

					return Request.CreateResponse(HttpStatusCode.Created);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
					throw HttpStatusCode.InternalServerError.AsException();
				}
			}

			throw HttpStatusCode.BadRequest.AsException();
		}

		[AcceptVerbs("DELETE")]
		public HttpResponseMessage DeleteStaffingSchedule(int staffingSecheduleId)
		{
			if (this.ModelState.IsValid)
			{
				try
				{
					var staffingSchedule = _scheduledTasksService.GetScheduledTaskById(staffingSecheduleId);

					if (staffingSchedule == null)
						throw HttpStatusCode.NotFound.AsException();

					if (staffingSchedule.UserId != UserId)
						throw HttpStatusCode.Unauthorized.AsException();

					_scheduledTasksService.DeleteScheduledTask(staffingSecheduleId);

					return Request.CreateResponse(HttpStatusCode.OK);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
					throw HttpStatusCode.InternalServerError.AsException();
				}
			}

			throw HttpStatusCode.BadRequest.AsException();
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
