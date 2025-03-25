using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Web;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.Version3.Models.Calendar;
using CalendarItem = Resgrid.Web.Services.Controllers.Version3.Models.Calendar.CalendarItem;
using CalendarItemAttendee = Resgrid.Web.Services.Controllers.Version3.Models.Calendar.CalendarItemAttendee;
using CalendarItemType = Resgrid.Web.Services.Controllers.Version3.Models.Calendar.CalendarItemType;
using Resgrid.Framework;
using Resgrid.Model.Helpers;
using System.Globalization;
using System.Linq;
using System.Net.Mime;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;


namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Used to interact with the calendar system
	/// Implements the <see cref="Resgrid.Web.Services.Controllers.Version3.V3AuthenticatedApiControllerbase" />
	/// </summary>
	/// <seealso cref="Resgrid.Web.Services.Controllers.Version3.V3AuthenticatedApiControllerbase" />
	[Produces("application/json")]
	[Route("api/v{version:ApiVersion}/[controller]")]
	[ApiVersion("3.0")]
	[ApiExplorerSettings(GroupName = "v3")]
	public class CalendarController : V3AuthenticatedApiControllerbase
	{
		private readonly ICalendarService _calendarService;
		private readonly IDepartmentsService _departmentsService;

		public CalendarController(ICalendarService calendarService, IDepartmentsService departmentsService)
		{
			_calendarService = calendarService;
			_departmentsService = departmentsService;
		}

		/// <summary>
		/// Gets the department calendar items.
		/// </summary>
		/// <returns>ActionResult&lt;List&lt;CalendarItem&gt;&gt;.</returns>
		[HttpGet("GetDepartmentCalendarItems")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Produces(typeof(List<CalendarItem>))]
		[SwaggerResponse(200, Type = typeof(List<CalendarItem>))]
		public async Task<ActionResult<List<CalendarItem>>> GetDepartmentCalendarItems()
		{
			List<CalendarItem> jsonItems = new List<CalendarItem>();
			List<Model.CalendarItem> items = null;

			items = await _calendarService.GetAllCalendarItemsForDepartmentAsync(DepartmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			foreach (var item in items)
			{
				CalendarItem calendarItem = new CalendarItem();
				calendarItem.CalendarItemId = item.CalendarItemId;
				calendarItem.Title = item.Title;
				calendarItem.Start = item.Start.TimeConverter(department);
				calendarItem.End = item.End.TimeConverter(department);
				calendarItem.StartTimezone = item.StartTimezone;
				calendarItem.EndTimezone = item.EndTimezone;
				calendarItem.Description = item.Description;
				calendarItem.RecurrenceId = item.RecurrenceId;
				calendarItem.RecurrenceRule = item.RecurrenceRule;
				calendarItem.RecurrenceException = item.RecurrenceException;
				calendarItem.IsAllDay = item.IsAllDay;
				calendarItem.ItemType = item.ItemType;
				calendarItem.Location = item.Location;
				calendarItem.SignupType = item.SignupType;
				calendarItem.Reminder = item.Reminder;
				calendarItem.LockEditing = item.LockEditing;
				calendarItem.Entities = item.Entities;
				calendarItem.RequiredAttendes = item.RequiredAttendes;
				calendarItem.OptionalAttendes = item.OptionalAttendes;

				if (!String.IsNullOrWhiteSpace(item.CreatorUserId))
					calendarItem.CreatorUserId = item.CreatorUserId.ToString();

				if (department.IsUserAnAdmin(UserId))
					calendarItem.IsAdminOrCreator = true;
				else if (!String.IsNullOrWhiteSpace(item.CreatorUserId) && item.CreatorUserId == UserId)
					calendarItem.IsAdminOrCreator = true;
				else
					calendarItem.IsAdminOrCreator = false;


				jsonItems.Add(calendarItem);
			}

			return Ok(jsonItems);
		}


		/// <summary>
		/// Gets the department calendar items in range.
		/// </summary>
		/// <param name="start">The start.</param>
		/// <param name="end">The end.</param>
		/// <returns>ActionResult&lt;List&lt;CalendarItem&gt;&gt;.</returns>
		[HttpGet("GetDepartmentCalendarItemsInRange")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status500InternalServerError)]
		[Produces(typeof(List<CalendarItem>))]
		[SwaggerResponse(200, Type = typeof(List<CalendarItem>))]
		public async Task<ActionResult<List<CalendarItem>>> GetDepartmentCalendarItemsInRange(DateTime start, DateTime end)
		{
			List<CalendarItem> jsonItems = new List<CalendarItem>();
			List<Model.CalendarItem> items = null;

			items = await _calendarService.GetAllCalendarItemsForDepartmentInRangeAsync(DepartmentId, start.SetToMidnight(), end.SetToEndOfDay());
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			foreach (var item in items)
			{
				CalendarItem calendarItem = new CalendarItem();
				calendarItem.CalendarItemId = item.CalendarItemId;
				calendarItem.Title = item.Title;
				calendarItem.Start = item.Start.TimeConverter(department);
				calendarItem.End = item.End.TimeConverter(department);
				calendarItem.StartTimezone = item.StartTimezone;
				calendarItem.EndTimezone = item.EndTimezone;
				calendarItem.Description = item.Description;
				calendarItem.RecurrenceId = item.RecurrenceId;
				calendarItem.RecurrenceRule = item.RecurrenceRule;
				calendarItem.RecurrenceException = item.RecurrenceException;
				calendarItem.IsAllDay = item.IsAllDay;
				calendarItem.ItemType = item.ItemType;
				calendarItem.Location = item.Location;
				calendarItem.SignupType = item.SignupType;
				calendarItem.Reminder = item.Reminder;
				calendarItem.LockEditing = item.LockEditing;
				calendarItem.Entities = item.Entities;
				calendarItem.RequiredAttendes = item.RequiredAttendes;
				calendarItem.OptionalAttendes = item.OptionalAttendes;

				if (!String.IsNullOrWhiteSpace(item.CreatorUserId))
					calendarItem.CreatorUserId = item.CreatorUserId.ToString();

				if (department.IsUserAnAdmin(UserId))
					calendarItem.IsAdminOrCreator = true;
				else if (!String.IsNullOrWhiteSpace(item.CreatorUserId) && item.CreatorUserId == UserId)
					calendarItem.IsAdminOrCreator = true;
				else
					calendarItem.IsAdminOrCreator = false;


				jsonItems.Add(calendarItem);
			}

			return jsonItems;
		}

		
		/// <summary>
		/// Gets the calendar items specified in the date range.
		/// </summary>
		/// <param name="startDate">Must be in MM/dd/yyyy HH:mm:ss zzz format</param>
		/// <param name="endDate">Must be in MM/dd/yyyy HH:mm:ss zzz format</param>
		/// <returns></returns>
		[HttpGet("GetAllDepartmentCalendarItemsInRange")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Produces(typeof(List<CalendarItem>))]
		[SwaggerResponse(200, Type = typeof(List<CalendarItem>))]
		public async Task<ActionResult<List<CalendarItem>>> GetAllDepartmentCalendarItemsInRange(string startDate, string endDate)
		{
			List<CalendarItem> jsonItems = new List<CalendarItem>();
			List<Model.CalendarItem> items = null;
			
			var startDateString = HttpUtility.UrlDecode(startDate);
			var endDateString = HttpUtility.UrlDecode(endDate);

			var start = DateTime.ParseExact(startDateString, "MM/dd/yyyy HH:mm:ss zzz", CultureInfo.InvariantCulture);
			var end = DateTime.ParseExact(endDateString, "MM/dd/yyyy HH:mm:ss zzz", CultureInfo.InvariantCulture);

			items = await _calendarService.GetAllCalendarItemsForDepartmentInRangeAsync(DepartmentId, start.SetToMidnight(), end.SetToEndOfDay());
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			foreach (var item in items)
			{
				CalendarItem calendarItem = new CalendarItem();
				calendarItem.CalendarItemId = item.CalendarItemId;
				calendarItem.Title = item.Title;
				calendarItem.Start = item.Start.TimeConverter(department);
				calendarItem.End = item.End.TimeConverter(department);
				calendarItem.StartTimezone = item.StartTimezone;
				calendarItem.EndTimezone = item.EndTimezone;
				calendarItem.Description = item.Description;
				calendarItem.RecurrenceId = item.RecurrenceId;
				calendarItem.RecurrenceRule = item.RecurrenceRule;
				calendarItem.RecurrenceException = item.RecurrenceException;
				calendarItem.IsAllDay = item.IsAllDay;
				calendarItem.ItemType = item.ItemType;
				calendarItem.Location = item.Location;
				calendarItem.SignupType = item.SignupType;
				calendarItem.Reminder = item.Reminder;
				calendarItem.LockEditing = item.LockEditing;
				calendarItem.Entities = item.Entities;
				calendarItem.RequiredAttendes = item.RequiredAttendes;
				calendarItem.OptionalAttendes = item.OptionalAttendes;

				if (!String.IsNullOrWhiteSpace(item.CreatorUserId))
					calendarItem.CreatorUserId = item.CreatorUserId.ToString();

				if (department.IsUserAnAdmin(UserId))
					calendarItem.IsAdminOrCreator = true;
				else if (!String.IsNullOrWhiteSpace(item.CreatorUserId) && item.CreatorUserId == UserId)
					calendarItem.IsAdminOrCreator = true;
				else
					calendarItem.IsAdminOrCreator = false;


				jsonItems.Add(calendarItem);
			}

			return Ok(jsonItems);
		}

		/// <summary>
		/// Gets the calendar item.
		/// </summary>
		/// <param name="id">The identifier.</param>
		/// <returns>ActionResult&lt;CalendarItem&gt;.</returns>
		[HttpGet("GetCalendarItem")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Produces(typeof(CalendarItem))]
		[SwaggerResponse(200, Type = typeof(CalendarItem))]
		public async Task<ActionResult<CalendarItem>> GetCalendarItem(int id)
		{
			var calendarItem = new CalendarItem();
			var item = await _calendarService.GetCalendarItemByIdAsync(id);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			if (item == null || department == null)
				return NotFound();

			calendarItem.CalendarItemId = item.CalendarItemId;
			calendarItem.Title = item.Title;
			calendarItem.Start = item.Start.TimeConverter(department);
			calendarItem.End = item.End.TimeConverter(department);
			calendarItem.StartTimezone = item.StartTimezone;
			calendarItem.EndTimezone = item.EndTimezone;
			calendarItem.Description = item.Description;
			calendarItem.RecurrenceId = item.RecurrenceId;
			calendarItem.RecurrenceRule = item.RecurrenceRule;
			calendarItem.RecurrenceException = item.RecurrenceException;
			calendarItem.IsAllDay = item.IsAllDay;
			calendarItem.ItemType = item.ItemType;
			calendarItem.Location = item.Location;
			calendarItem.SignupType = item.SignupType;
			calendarItem.Reminder = item.Reminder;
			calendarItem.LockEditing = item.LockEditing;
			calendarItem.Entities = item.Entities;
			calendarItem.RequiredAttendes = item.RequiredAttendes;
			calendarItem.OptionalAttendes = item.OptionalAttendes;

			if (!String.IsNullOrWhiteSpace(item.CreatorUserId))
				calendarItem.CreatorUserId = item.CreatorUserId.ToString();

			if (department.IsUserAnAdmin(UserId))
				calendarItem.IsAdminOrCreator = true;
			else if (!String.IsNullOrWhiteSpace(item.CreatorUserId) && item.CreatorUserId == UserId)
				calendarItem.IsAdminOrCreator = true;
			else
				calendarItem.IsAdminOrCreator = false;

			if (item.Attendees == null || !item.Attendees.Any())
				calendarItem.Attending = false;
			else
			{
				calendarItem.Attending = item.Attendees.Any(x => x.UserId == UserId);
				calendarItem.Attendees = new List<CalendarItemAttendee>();

				foreach (var attendee in item.Attendees)
				{
					CalendarItemAttendee attend = new CalendarItemAttendee();
					attend.CalendarItemId = attendee.CalendarItemId;
					attend.UserId = attendee.UserId;
					attend.AttendeeType = attendee.AttendeeType;
					attend.Note = attendee.Note;

					calendarItem.Attendees.Add(attend);
				}
			}

			return calendarItem;
		}

		/// <summary>
		/// Gets the department calendar item types.
		/// </summary>
		/// <returns>ActionResult&lt;List&lt;CalendarItemType&gt;&gt;.</returns>
		[HttpGet("GetDepartmentCalendarItemTypes")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Produces(typeof(List<CalendarItem>))]
		[SwaggerResponse(200, Type = typeof(List<CalendarItem>))]
		public async Task<ActionResult<List<CalendarItemType>>> GetDepartmentCalendarItemTypes()
		{
			var jsonItems = new List<CalendarItemType>();
			var items = await _calendarService.GetAllCalendarItemTypesForDepartmentAsync(DepartmentId);

			jsonItems.Add(new CalendarItemType()
			{
				CalendarItemTypeId = "0",
				Color = "#EEE",
				Name = "None"
			});

			foreach (var item in items)
			{
				var calendarItemType = new CalendarItemType();
				calendarItemType.CalendarItemTypeId = item.CalendarItemTypeId.ToString();
				calendarItemType.Name = item.Name;
				calendarItemType.Color = item.Color;

				jsonItems.Add(calendarItemType);
			}

			return Ok(jsonItems);
		}

		[HttpPost("SetCalendarAttendingStatus")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		public async Task<ActionResult> SetCalendarAttendingStatus(CalendarItemAttendInput input)
		{
			if (String.IsNullOrWhiteSpace(input.CalendarEventId))
				return NotFound();
			
			var calendarItem = await _calendarService.GetCalendarItemByIdAsync(int.Parse(input.CalendarEventId));

			if (calendarItem == null)
				return NotFound();

			if (calendarItem.DepartmentId != DepartmentId)
				return Unauthorized();

			var result = await _calendarService.SignupForEvent(int.Parse(input.CalendarEventId), UserId, input.Note, input.Type);

			return CreatedAtAction(nameof(SetCalendarAttendingStatus), new { id = result.CalendarItemAttendeeId }, result);
		}
	}
}
