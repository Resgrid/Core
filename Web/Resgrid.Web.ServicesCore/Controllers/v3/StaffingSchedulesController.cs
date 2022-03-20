using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using System;
using System.Collections.Generic;
using System.Text;
using Resgrid.Web.Services.Controllers.Version3.Models.StaffingSchedules;
using System.Globalization;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;


namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Operations to perform against user statuses and their actions
	/// </summary>
	[Route("api/v{version:ApiVersion}/[controller]")]
	[Produces("application/json")]
	[ApiVersion("3.0")]
	[ApiExplorerSettings(GroupName = "v3")]
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
		[HttpGet("GetStaffingSchedules")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<List<StaffingScheduleResult>>> GetStaffingSchedules()
		{
			var results = new List<StaffingScheduleResult>();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var tasks = await _scheduledTasksService.GetScheduledStaffingTasksForUserAsync(UserId);

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

			return Ok(results);
		}

		/// <summary>
		/// Toggles and Scheduled Staffing Level Change (Enabling or Disabling It)
		/// </summary>
		/// <param name="toggleInput">StateInput object with the Staffing to toggle and it's value.</param>
		/// <returns>Returns HttpStatusCode Created if successful, BadRequest otherwise.</returns>
		[HttpPut("ToggleStaffingSchedule")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		public async Task<ActionResult> ToggleStaffingSchedule(ToggleStaffingScheduleInput toggleInput, CancellationToken cancellationToken)
		{
			if (this.ModelState.IsValid)
			{
				try
				{
					var staffingSchedule = await _scheduledTasksService.GetScheduledTaskByIdAsync(toggleInput.Id);

					if (staffingSchedule == null)
						return NotFound();

					if (staffingSchedule.UserId != UserId)
						return Unauthorized();

					staffingSchedule.Active = toggleInput.Act;
					var task = await _scheduledTasksService.SaveScheduledTaskAsync(staffingSchedule);

					return CreatedAtAction(nameof(ToggleStaffingSchedule), new { id = task.IdValue }, task);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
					return BadRequest();
				}
			}

			return BadRequest();
		}

		/// <summary>
		/// Toggles and Scheduled Staffing Level Change (Enabling or Disabling It)
		/// </summary>
		/// <param name="toggleInput">StateInput object with the Staffing to toggle and it's value.</param>
		/// <returns>Returns HttpStatusCode Created if successful, BadRequest otherwise.</returns>
		[HttpPost("CreateStaffingSchedule")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		public async Task<ActionResult> CreateStaffingSchedule(StaffingScheduleInput staffingScheduleInput, CancellationToken cancellationToken)
		{
			if (this.ModelState.IsValid)
			{
				try
				{
					var task = new ScheduledTask();
					task.UserId = UserId;

					if (staffingScheduleInput.Typ == 1)
					{
						var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
						DateTime inputDate = DateTime.Parse(staffingScheduleInput.Spd);
						
						task.ScheduleType = (int)ScheduleTypes.SpecifcDateTime;

						string[] timeParts = new List<string>().ToArray();
						timeParts = staffingScheduleInput.Spt.Split(char.Parse(":"));
						
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

					var saved = await _scheduledTasksService.SaveScheduledTaskAsync(task, cancellationToken);

					return CreatedAtAction(nameof(CreateStaffingSchedule), new { id = saved.IdValue }, saved);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
					return BadRequest();
				}
			}

			return BadRequest();
		}

		[HttpDelete("DeleteStaffingSchedule")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult> DeleteStaffingSchedule(int staffingSecheduleId)
		{
			if (this.ModelState.IsValid)
			{
				try
				{
					var staffingSchedule = await _scheduledTasksService.GetScheduledTaskByIdAsync(staffingSecheduleId);

					if (staffingSchedule == null)
						return NotFound();

					if (staffingSchedule.UserId != UserId)
						return Unauthorized();

					await _scheduledTasksService.DeleteScheduledTask(staffingSecheduleId);

					return Ok();
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
					return BadRequest();
				}
			}

			return BadRequest();
		}
	}
}
