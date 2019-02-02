using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Calendar;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model.Helpers;
using Resgrid.WebCore.Areas.User.Models.Calendar;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class CalendarController : SecureBaseController
	{
		#region Private Members and Constructors
		private readonly IDepartmentsService _departmentsService;
		private readonly IUsersService _usersService;
		private readonly ICalendarService _calendarService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IGeoLocationProvider _geoLocationProvider;
		private readonly IEventAggregator _eventAggregator;
		private readonly IAuthorizationService _authorizationService;

		public CalendarController(IDepartmentsService departmentsService, IUsersService usersService, ICalendarService calendarService,
			IDepartmentGroupsService departmentGroupsService, IGeoLocationProvider geoLocationProvider, IEventAggregator eventAggregator,
			IAuthorizationService authorizationService)
		{
			_departmentsService = departmentsService;
			_usersService = usersService;
			_calendarService = calendarService;
			_departmentGroupsService = departmentGroupsService;
			_geoLocationProvider = geoLocationProvider;
			_eventAggregator = eventAggregator;
			_authorizationService = authorizationService;
		}
		#endregion Private Members and Constructors

		[HttpGet]
		[Authorize(Policy = ResgridResources.Schedule_View)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public IActionResult Index()
		{
			var model = new IndexView();
			model.Department = _departmentsService.GetDepartmentById(DepartmentId, false);

			if (!String.IsNullOrWhiteSpace(model.Department.TimeZone))
				model.TimeZone = DateTimeHelpers.WindowsToIana(model.Department.TimeZone);
			else
				model.TimeZone = "Etc/UTC";

			model.Types = new List<CalendarItemType>();
			model.Types = _calendarService.GetAllCalendarItemTypesForDepartment(DepartmentId);

			model.UpcomingItems = new List<CalendarItem>();
			model.UpcomingItems = _calendarService.GetUpcomingCalendarItems(DepartmentId, DateTime.UtcNow);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Schedule_Create)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public IActionResult New()
		{
			var model = new NewCalendarEntry();
			model.Item = new CalendarItem();
			model.Types = new List<CalendarItemType>();
			model.Types.Add(new CalendarItemType() {CalendarItemTypeId = 0, Name = "No Type"});
			model.Types.AddRange(_calendarService.GetAllCalendarItemTypesForDepartment(DepartmentId));

			var department = _departmentsService.GetDepartmentById(DepartmentId);
			var currentTime = DateTime.UtcNow.TimeConverter(department);

			model.Item.Start = currentTime.AddHours(3);
			model.Item.End = currentTime.AddHours(4);

			ViewBag.Types = new SelectList(model.Types, "CalendarItemTypeId", "Name");

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Schedule_Create)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public IActionResult New(NewCalendarEntry model)
		{
			if (model.Item.Start > model.Item.End)
			{
				ModelState.AddModelError("Item_End", "End date and time cannot be before start date and time.");
			}

			if ((model.Item.RecurrenceType == (int) RecurrenceTypes.Weekly
			    || model.Item.RecurrenceType == (int) RecurrenceTypes.Monthly
			    || model.Item.RecurrenceType == (int) RecurrenceTypes.Yearly) &&
			    (model.Item.RecurrenceEnd.HasValue && (model.Item.RecurrenceEnd.Value <= model.Item.Start || model.Item.RecurrenceEnd.Value <= model.Item.End)))
			{
				ModelState.AddModelError("Item_End", "End date and time cannot be before start date and time.");
			}

			if (ModelState.IsValid)
			{
				var department = _departmentsService.GetDepartmentById(DepartmentId);

				model.Item.DepartmentId = DepartmentId;
				model.Item.CreatorUserId = UserId;

				_calendarService.AddNewV2CalendarItem(model.Item, department.TimeZone);

				return RedirectToAction("Index");
			}

			model.Types = new List<CalendarItemType>();
			model.Types.Add(new CalendarItemType() { CalendarItemTypeId = 0, Name = "No Type" });
			model.Types.AddRange(_calendarService.GetAllCalendarItemTypesForDepartment(DepartmentId));

			model.Item.Description = StringHelpers.StripHtmlTagsCharArray(model.Item.Description);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Schedule_Update)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public IActionResult Edit(int id)
		{
			var model = new EditCalendarEntry();

			if (!_authorizationService.CanUserModifyCalendarEntry(UserId, id))
				Unauthorized();

			model.Item = _calendarService.GetCalendarItemById(id);
			model.Types = new List<CalendarItemType>();
			model.Types.Add(new CalendarItemType() { CalendarItemTypeId = 0, Name = "No Type" });
			model.Types.AddRange(_calendarService.GetAllCalendarItemTypesForDepartment(DepartmentId));

			var department = _departmentsService.GetDepartmentById(DepartmentId);

			model.StartTime = model.Item.Start.TimeConverter(department);
			model.EndTime = model.Item.End.TimeConverter(department);

			var recurrences = _calendarService.GetAllV2CalendarItemRecurrences(model.Item.CalendarItemId);
			if (recurrences != null && recurrences.Any())
				model.IsRecurrenceParent = true;

			ViewBag.Types = new SelectList(model.Types, "CalendarItemTypeId", "Name");
			model.Item.Description = StringHelpers.StripHtmlTagsCharArray(model.Item.Description);

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Schedule_Update)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public IActionResult Edit(EditCalendarEntry model)
		{
			if (model.Item.Start > model.Item.End)
			{
				ModelState.AddModelError("Item_End", "End date and time cannot be before start date and time.");
			}

			if ((model.Item.RecurrenceType == (int)RecurrenceTypes.Weekly
			     || model.Item.RecurrenceType == (int)RecurrenceTypes.Monthly
			     || model.Item.RecurrenceType == (int)RecurrenceTypes.Yearly) &&
			    (model.Item.RecurrenceEnd.HasValue && (model.Item.RecurrenceEnd.Value <= model.Item.Start || model.Item.RecurrenceEnd.Value <= model.Item.End)))
			{
				ModelState.AddModelError("Item_End", "End date and time cannot be before start date and time.");
			}

			if (ModelState.IsValid)
			{
				var department = _departmentsService.GetDepartmentById(DepartmentId);

				model.Item.Start = model.StartTime;
				model.Item.End = model.EndTime;
				model.Item.DepartmentId = DepartmentId;
				model.Item.CreatorUserId = UserId;

				_calendarService.UpdateV2CalendarItem(model.Item, department.TimeZone);

				return RedirectToAction("Index");
			}

			model.Types = new List<CalendarItemType>();
			model.Types.Add(new CalendarItemType() { CalendarItemTypeId = 0, Name = "No Type" });
			model.Types.AddRange(_calendarService.GetAllCalendarItemTypesForDepartment(DepartmentId));
			ViewBag.Types = new SelectList(model.Types, "CalendarItemTypeId", "Name");

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Schedule_Create)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public JsonResult CreateCalendarItem([FromBody]CalendarItemJson item)
		{
			if (ModelState.IsValid)
			{
				var department = _departmentsService.GetDepartmentById(DepartmentId, false);

				var timeZone = "Etc/UTC";
				if (!String.IsNullOrWhiteSpace(department.TimeZone))
					timeZone = DateTimeHelpers.WindowsToIana(department.TimeZone);

				CalendarItem calendarItem = new CalendarItem();
				calendarItem.DepartmentId = DepartmentId;
				calendarItem.CalendarItemId = item.CalendarItemId;
				calendarItem.Title = item.Title;

				//if (item.Start.Kind == DateTimeKind.Utc)
				//	calendarItem.Start = item.Start.TimeConverter(department);
				//else
				//	calendarItem.Start = item.Start;

				//if (item.End.Kind == DateTimeKind.Utc)
				//	calendarItem.End = item.End.TimeConverter(department);
				//else
				//	calendarItem.End = item.End;

				if (!String.IsNullOrWhiteSpace(item.Start))
					calendarItem.Start = DateTime.Parse(item.Start).ToUniversalTime();
				else
					calendarItem.Start = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0, 0, 0).ToUniversalTime();

				if (!String.IsNullOrWhiteSpace(item.End))
					calendarItem.End = DateTime.Parse(item.End).ToUniversalTime();
				else
					calendarItem.Start = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 23, 59, 59).ToUniversalTime();

				if (!String.IsNullOrWhiteSpace(item.StartTimezone))
					calendarItem.StartTimezone = item.StartTimezone;
				else
					calendarItem.StartTimezone = timeZone;

				if (!String.IsNullOrWhiteSpace(item.EndTimezone))
					calendarItem.EndTimezone = item.EndTimezone;
				else
					calendarItem.EndTimezone = timeZone;

				calendarItem.Description = item.Description;
				calendarItem.RecurrenceId = item.RecurrenceId;
				calendarItem.RecurrenceRule = item.RecurrenceRule;
				calendarItem.RecurrenceException = item.RecurrenceException;
				calendarItem.IsAllDay = item.IsAllDay;
				calendarItem.Location = item.Location;
				calendarItem.SignupType = item.SignupType;
				calendarItem.Reminder = item.Reminder;
				calendarItem.LockEditing = item.LockEditing;
				calendarItem.Entities = item.Entities;
				calendarItem.RequiredAttendes = item.RequiredAttendes;
				calendarItem.OptionalAttendes = item.OptionalAttendes;
				calendarItem.Public = item.Public;
				calendarItem.CreatorUserId = UserId;

				calendarItem.StartTimezone = DateTimeHelpers.WindowsToIana(department.TimeZone);
				calendarItem.EndTimezone = DateTimeHelpers.WindowsToIana(department.TimeZone);

				if (item.ItemType.HasValue)
					calendarItem.ItemType = item.ItemType.Value;

				calendarItem = _calendarService.SaveCalendarItem(calendarItem);

				_eventAggregator.SendMessage<CalendarEventAddedEvent>(new CalendarEventAddedEvent() { DepartmentId = DepartmentId, Item = calendarItem});

				return Json(item);
			}

			return null;
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Schedule_Update)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public JsonResult UpdateCalendarItem(CalendarItemJson item)
		{
			if (ModelState.IsValid)
			{
				var department = _departmentsService.GetDepartmentById(DepartmentId, false);

				var calendarItem = _calendarService.GetCalendarItemById(item.CalendarItemId);

				if (calendarItem != null)
				{
					calendarItem.DepartmentId = DepartmentId;
					calendarItem.CalendarItemId = item.CalendarItemId;
					calendarItem.Title = item.Title;

					//if (item.Start.Kind == DateTimeKind.Utc)
					//	calendarItem.Start = item.Start.TimeConverter(department);
					//else
					//	calendarItem.Start = item.Start;

					//if (item.End.Kind == DateTimeKind.Utc)
					//	calendarItem.End = item.End.TimeConverter(department);
					//else
					//	calendarItem.End = item.End;

					calendarItem.Start = DateTime.Parse(item.Start).ToUniversalTime();
					calendarItem.End = DateTime.Parse(item.End).ToUniversalTime();

					calendarItem.StartTimezone = item.StartTimezone;
					calendarItem.EndTimezone = item.EndTimezone;
					calendarItem.Description = item.Description;
					calendarItem.RecurrenceId = item.RecurrenceId;
					calendarItem.RecurrenceRule = item.RecurrenceRule;
					calendarItem.RecurrenceException = item.RecurrenceException;
					calendarItem.IsAllDay = item.IsAllDay;
					calendarItem.Location = item.Location;
					calendarItem.SignupType = item.SignupType;
					calendarItem.Reminder = item.Reminder;
					calendarItem.LockEditing = item.LockEditing;
					calendarItem.Entities = item.Entities;
					calendarItem.RequiredAttendes = item.RequiredAttendes;
					calendarItem.OptionalAttendes = item.OptionalAttendes;
					calendarItem.CreatorUserId = UserId;
					calendarItem.Public = item.Public;

					calendarItem.StartTimezone = DateTimeHelpers.WindowsToIana(department.TimeZone);
					calendarItem.EndTimezone = DateTimeHelpers.WindowsToIana(department.TimeZone);

					if (item.ItemType.HasValue)
						calendarItem.ItemType = item.ItemType.Value;

					calendarItem = _calendarService.SaveCalendarItem(calendarItem);

					_eventAggregator.SendMessage<CalendarEventUpdatedEvent>(new CalendarEventUpdatedEvent() { DepartmentId = DepartmentId, Item = calendarItem });

					return Json(item);
				}
			}

			return null;
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Schedule_Delete)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public JsonResult DeleteCalendarItem([FromBody]CalendarItemJson item)
		{
			if (ModelState.IsValid)
			{
				var calandarItem = _calendarService.GetCalendarItemById(item.CalendarItemId);

				if (calandarItem == null || calandarItem.DepartmentId != DepartmentId)
					Unauthorized();

				_calendarService.DeleteCalendarItemById(item.CalendarItemId);
			}

			return null;
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Schedule_Delete)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public IActionResult DeleteCalendarItem(int itemId)
		{
			var calandarItem = _calendarService.GetCalendarItemById(itemId);

			if (calandarItem == null || calandarItem.DepartmentId != DepartmentId)
				Unauthorized();

			_calendarService.DeleteCalendarItemById(itemId);
			
			return RedirectToAction("Index");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Schedule_Delete)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public IActionResult DeleteAllCalendarItems(int itemId)
		{
			if (!_authorizationService.CanUserModifyCalendarEntry(UserId, itemId))
				Unauthorized();

			_calendarService.DeleteCalendarItemAndRecurrences(itemId);

			return RedirectToAction("Index");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Schedule_View)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]

		public IActionResult GetDepartmentCalendarItems()
		{
			List<CalendarItemJson> jsonItems = new List<CalendarItemJson>();
			var items = _calendarService.GetAllCalendarItemsForDepartment(DepartmentId);
			var department = _departmentsService.GetDepartmentById(DepartmentId, false);

			foreach (var item in items)
			{
				CalendarItemJson calendarItem = new CalendarItemJson();
				calendarItem.CalendarItemId = item.CalendarItemId;
				calendarItem.Title = item.Title;
				//calendarItem.Start = item.Start;
				//calendarItem.End = item.End;

				calendarItem.Start = item.Start.TimeConverter(department).ToString("O");
				calendarItem.End = item.End.TimeConverter(department).ToString("O");

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
				calendarItem.Public = item.Public;

				if (!String.IsNullOrWhiteSpace(item.RecurrenceId))
				{
					var parent = _calendarService.GetCalendarItemById(int.Parse(item.RecurrenceId));

					if (parent == null)
					{
						calendarItem.RecurrenceId = null;
						calendarItem.RecurrenceRule = null;
						calendarItem.RecurrenceException = null;
					}
				}

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

			return Json(jsonItems);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Schedule_View)]
		public IActionResult GetV2CalendarEntriesForCal(string start, string end)
		{
			var jsonItems = new List<CalendarItemV2Json>();

			var department = _departmentsService.GetDepartmentById(DepartmentId);
			var items = _calendarService.GetAllV2CalendarItemsForDepartment(DepartmentId, DateTime.UtcNow.AddMonths(-6));
			var itemTypes = _calendarService.GetAllCalendarItemTypesForDepartment(DepartmentId);

			foreach (var item in items)
			{
				var jsonItem = new CalendarItemV2Json();
				jsonItem.id = item.CalendarItemId;
				jsonItem.title = item.Title;
				jsonItem.start = item.Start.TimeConverter(department).ToString();
				jsonItem.end = item.End.TimeConverter(department).ToString();
				jsonItem.url = $"{GetBaseUrl()}/User/Calendar/View?calendarItemId={item.CalendarItemId}";

				if (item.ItemType != 0)
				{
					var type = itemTypes.FirstOrDefault(x => x.CalendarItemTypeId == item.ItemType);

					if (type != null)
						jsonItem.backgroundColor = type.Color;
				}

				jsonItems.Add(jsonItem);
			}

			return Json(jsonItems);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Schedule_View)]

		public IActionResult RemoveFromEvent(int id)
		{
			var attendee = _calendarService.GetCalendarAttendeeById(id);

			_calendarService.DeleteCalendarAttendeeById(id);

			return RedirectToAction("View", new { calendarItemId = attendee.CalendarItemId });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Schedule_View)]

		public IActionResult View(int calendarItemId)
		{
			var model = new CalendarItemView();
			model.CalendarItem = _calendarService.GetCalendarItemById(calendarItemId);

			if (model.CalendarItem == null)
				Unauthorized();

			model.CalendarItem.Description = StringHelpers.SanitizeHtmlInString(model.CalendarItem.Description);

			model.UserId = UserId;
			model.Department = _departmentsService.GetDepartmentById(DepartmentId, false);

			var recurrences = _calendarService.GetAllV2CalendarItemRecurrences(calendarItemId);
			if (recurrences != null && recurrences.Any())
				model.IsRecurrenceParent = true;

			model.CanEdit = _authorizationService.CanUserModifyCalendarEntry(UserId, calendarItemId);

			if (model.CalendarItem.DepartmentId != DepartmentId)
				Unauthorized();

			return View(model);
		}

		[Authorize(Policy = ResgridResources.Schedule_View)]
		[HttpPost]
		public IActionResult Signup(CalendarItemView model)
		{
			_calendarService.SignupForEvent(model.CalendarItem.CalendarItemId, UserId, model.Note, (int)CalendarItemAttendeeTypes.RSVP);

			return RedirectToAction("View", new { calendarItemId = model.CalendarItem.CalendarItemId });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Schedule_View)]

		public IActionResult GetDepartmentCalendarItemTypes()
		{
			var jsonItems = new List<CalendarItemTypeJson>();
			var items = _calendarService.GetAllCalendarItemTypesForDepartment(DepartmentId);

			jsonItems.Add(new CalendarItemTypeJson()
			{
				CalendarItemTypeId = "0",
				Color = "#FFF",
				Name = "None"
			});

			foreach (var item in items)
			{
				var calendarItemType = new CalendarItemTypeJson();
				calendarItemType.CalendarItemTypeId = item.CalendarItemTypeId.ToString();
				calendarItemType.Name = item.Name;
				calendarItemType.Color = item.Color;

				jsonItems.Add(calendarItemType);
			}

			return Json(jsonItems);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Schedule_View)]
		public IActionResult Types()
		{
			TypesView model = new TypesView();
			model.Types = _calendarService.GetAllCalendarItemTypesForDepartment(DepartmentId);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Schedule_Create)]
		public IActionResult NewType()
		{
			NewTypeView model = new NewTypeView();
			model.Type = new CalendarItemType();

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Schedule_Create)]
		public IActionResult NewType(NewTypeView model)
		{
			if (_calendarService.GetAllCalendarItemTypesForDepartment(DepartmentId).Any(x => x.Name == model.Type.Name))
				ModelState.AddModelError("", "Type name already exists, please choose another name.");

			if (ModelState.IsValid)
			{
				model.Type.DepartmentId = DepartmentId;
				_calendarService.SaveCalendarItemType(model.Type);

				return RedirectToAction("Types", "Calendar", new { Area = "User" });
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Schedule_Create)]
		public IActionResult DeleteType(int typeId)
		{
			var type = _calendarService.GetCalendarItemTypeById(typeId);

			if (type.DepartmentId != DepartmentId)
				Unauthorized();

			_calendarService.DeleteCalendarItemType(type);

			return RedirectToAction("Types");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Schedule_View)]
		public IActionResult GetDepartmentEnitites()
		{
			List<DepartmentEntitiesJson> items = new List<DepartmentEntitiesJson>();

			var department = _departmentsService.GetDepartmentById(DepartmentId);
			var groups = _departmentGroupsService.GetAllGroupsForDepartment(DepartmentId);

			items.Add(new DepartmentEntitiesJson()
			{
				Id = $"D:{DepartmentId}",
				Type = -1,
				Name = department.Name
			});

			foreach (var group in groups)
			{
				items.Add(new DepartmentEntitiesJson()
				{
					Id = $"G:{group.DepartmentGroupId}",
					Type = group.Type.GetValueOrDefault(),
					Name = group.Name
				});
			}

			return Json(items);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Schedule_View)]
		public IActionResult GetMapDataForItem(int calendarItemId)
		{
			dynamic result = new ExpandoObject();

			var calendarItem = _calendarService.GetCalendarItemById(calendarItemId);

			if (calendarItem.DepartmentId == DepartmentId)
			{
				string locationResult = null;
				try
				{
					locationResult = _geoLocationProvider.GetLatLonFromAddress(calendarItem.Location);
				}
				catch { }

				if (!String.IsNullOrWhiteSpace(locationResult))
				{
					result.wasSuccess = true;
					var latLon = locationResult.Split(char.Parse(","));

					result.centerLat = latLon[0];
					result.centerLon = latLon[1];
				}
				else
				{
					result.wasSuccess = false;
				}
			}
			else
			{
				result.wasSuccess = false;
			}

			return Json(result);
		}
	}
}
