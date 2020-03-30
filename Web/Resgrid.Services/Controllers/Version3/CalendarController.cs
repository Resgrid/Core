using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web;
using System.Web.Http.Cors;
using System.Web.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.Version3.Models.Calendar;
using CalendarItem = Resgrid.Web.Services.Controllers.Version3.Models.Calendar.CalendarItem;
using CalendarItemAttendee = Resgrid.Web.Services.Controllers.Version3.Models.Calendar.CalendarItemAttendee;
using CalendarItemType = Resgrid.Web.Services.Controllers.Version3.Models.Calendar.CalendarItemType;
using Resgrid.Framework;
using Resgrid.Model.Helpers;
using System.Globalization;
using Resgrid.Web.Services.Helpers;

namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Used to interact with the calendar system
	/// </summary>
	public class CalendarController : V3AuthenticatedApiControllerbase
	{
		private readonly ICalendarService _calendarService;
		private readonly IDepartmentsService _departmentsService;

		public CalendarController(ICalendarService calendarService, IDepartmentsService departmentsService)
		{
			_calendarService = calendarService;
			_departmentsService = departmentsService;
		}

		[AcceptVerbs("GET")]
		public List<CalendarItem> GetDepartmentCalendarItems()
		{
			List<CalendarItem> jsonItems = new List<CalendarItem>();
			List<Model.CalendarItem> items = null;

			items = _calendarService.GetAllCalendarItemsForDepartment(DepartmentId);
			var department = _departmentsService.GetDepartmentById(DepartmentId, false);

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

		[AcceptVerbs("GET")]
		[System.Web.Http.Description.ApiExplorerSettings(IgnoreApi = true)]
		public List<CalendarItem> GetDepartmentCalendarItemsInRange(DateTime start, DateTime end)
		{
			List<CalendarItem> jsonItems = new List<CalendarItem>();
			List<Model.CalendarItem> items = null;

			items = _calendarService.GetAllCalendarItemsForDepartmentInRange(DepartmentId, start.SetToMidnight(), end.SetToEndOfDay());
			var department = _departmentsService.GetDepartmentById(DepartmentId, false);

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

		[AcceptVerbs("GET")]
		/// <summary>
		/// Gets the calendar items specified in the date range.
		/// </summary>
		/// <param name="startDate">Must be in MM/dd/yyyy HH:mm:ss zzz format</param>
		/// <param name="endDate">Must be in MM/dd/yyyy HH:mm:ss zzz format</param>
		/// <returns></returns>
		public List<CalendarItem> GetAllDepartmentCalendarItemsInRange(string startDate, string endDate)
		{
			List<CalendarItem> jsonItems = new List<CalendarItem>();
			List<Model.CalendarItem> items = null;
			
			var startDateString = HttpUtility.UrlDecode(startDate);
			var endDateString = HttpUtility.UrlDecode(endDate);

			var start = DateTime.ParseExact(startDateString, "MM/dd/yyyy HH:mm:ss zzz", CultureInfo.InvariantCulture);
			var end = DateTime.ParseExact(endDateString, "MM/dd/yyyy HH:mm:ss zzz", CultureInfo.InvariantCulture);

			items = _calendarService.GetAllCalendarItemsForDepartmentInRange(DepartmentId, start.SetToMidnight(), end.SetToEndOfDay());
			var department = _departmentsService.GetDepartmentById(DepartmentId, false);

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

		[AcceptVerbs("GET")]
		public CalendarItem GetCalendarItem(int id)
		{
			var calendarItem = new CalendarItem();
			var item = _calendarService.GetCalendarItemById(id);
			var department = _departmentsService.GetDepartmentById(DepartmentId, false);

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

		[AcceptVerbs("GET")]
		public List<CalendarItemType> GetDepartmentCalendarItemTypes()
		{
			var jsonItems = new List<CalendarItemType>();
			var items = _calendarService.GetAllCalendarItemTypesForDepartment(DepartmentId);

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

			return jsonItems;
		}

		[AcceptVerbs("POST")]
		public HttpResponseMessage SetCalendarAttendingStatus(CalendarItemAttendInput input)
		{
			var calendarItem = _calendarService.GetCalendarItemById(input.CalId);

			if (calendarItem == null)
				throw HttpStatusCode.NotFound.AsException();

			if (calendarItem.DepartmentId != DepartmentId)
				throw HttpStatusCode.Unauthorized.AsException();

			_calendarService.SignupForEvent(input.CalId, UserId, input.Note, input.Type);

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
