using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Config;
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
		private readonly IUserProfileService _userProfileService;
		private readonly IPermissionsService _permissionsService;
		private readonly IPersonnelRolesService _personnelRolesService;

		public CalendarController(IDepartmentsService departmentsService, IUsersService usersService, ICalendarService calendarService,
			IDepartmentGroupsService departmentGroupsService, IGeoLocationProvider geoLocationProvider, IEventAggregator eventAggregator,
			IAuthorizationService authorizationService, IUserProfileService userProfileService,
			IPermissionsService permissionsService, IPersonnelRolesService personnelRolesService)
		{
			_departmentsService = departmentsService;
			_usersService = usersService;
			_calendarService = calendarService;
			_departmentGroupsService = departmentGroupsService;
			_geoLocationProvider = geoLocationProvider;
			_eventAggregator = eventAggregator;
			_authorizationService = authorizationService;
			_userProfileService = userProfileService;
			_permissionsService = permissionsService;
			_personnelRolesService = personnelRolesService;
		}
		#endregion Private Members and Constructors

		[HttpGet]
		[Authorize(Policy = ResgridResources.Schedule_View)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<IActionResult> Index()
		{
			var model = new IndexView();
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			if (!String.IsNullOrWhiteSpace(model.Department.TimeZone))
				model.TimeZone = DateTimeHelpers.WindowsToIana(model.Department.TimeZone);
			else
				model.TimeZone = "Etc/UTC";

			model.Types = new List<CalendarItemType>();
			model.Types = await _calendarService.GetAllCalendarItemTypesForDepartmentAsync(DepartmentId);

			model.UpcomingItems = new List<CalendarItem>();
			model.UpcomingItems = await _calendarService.GetUpcomingCalendarItemsAsync(DepartmentId, DateTime.UtcNow);

			// Check calendar sync permission
			var calSyncPermission = await _permissionsService.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.UseCalendarSync);
			var department = model.Department;
			var isAdmin = department.IsUserAnAdmin(UserId);
			var group = await _departmentGroupsService.GetGroupForUserAsync(UserId, DepartmentId);
			var isGroupAdmin = group != null && group.IsUserGroupAdmin(UserId);
			var roles = await _personnelRolesService.GetRolesForUserAsync(UserId, DepartmentId);
			model.CanUseCalendarSync = _permissionsService.IsUserAllowed(calSyncPermission, isAdmin, isGroupAdmin, roles);

			// Populate calendar sync token for the subscribe panel.
			if (model.CanUseCalendarSync)
			{
				var profile = await _userProfileService.GetProfileByUserIdAsync(UserId);
				if (profile != null && !String.IsNullOrWhiteSpace(profile.CalendarSyncToken))
				{
					model.CalendarSyncToken = profile.CalendarSyncToken;
					var feedToken = await _calendarService.GetCalendarFeedTokenAsync(DepartmentId, UserId);
					model.CalendarSubscriptionUrl = $"{SystemBehaviorConfig.ResgridApiBaseUrl}/api/v4/CalendarExport/CalendarFeed/{feedToken}";
				}
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Schedule_Create)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<IActionResult> New()
		{
			var model = new NewCalendarEntry();
			model.Item = new CalendarItem();
			model.Types = new List<CalendarItemType>();
			model.Types.Add(new CalendarItemType() {CalendarItemTypeId = 0, Name = "No Type"});
			model.Types.AddRange(await _calendarService.GetAllCalendarItemTypesForDepartmentAsync(DepartmentId));

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var currentTime = DateTime.UtcNow.TimeConverter(department);

			model.Item.Start = currentTime.AddHours(3);
			model.Item.End = currentTime.AddHours(4);

			ViewBag.Types = new SelectList(model.Types, "CalendarItemTypeId", "Name");

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Schedule_Create)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<IActionResult> New(NewCalendarEntry model, CancellationToken cancellationToken)
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

			model.Types = new List<CalendarItemType>();
			model.Types.Add(new CalendarItemType() {CalendarItemTypeId = 0, Name = "No Type"});
			model.Types.AddRange(await _calendarService.GetAllCalendarItemTypesForDepartmentAsync(DepartmentId));
			ViewBag.Types = new SelectList(model.Types, "CalendarItemTypeId", "Name");

			if (ModelState.IsValid)
			{
				var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

				model.Item.DepartmentId = DepartmentId;
				model.Item.CreatorUserId = UserId;
				model.Item.Entities = model.entities;

				if (model.Item.RecurrenceType == 2)
				{
					model.Item.RepeatOnWeek = model.WeekdayOccurrence;
					model.Item.RepeatOnDay = model.WeekdayDayOfWeek;
				}

				var calendarItem = await _calendarService.AddNewCalendarItemAsync(model.Item, department.TimeZone, cancellationToken);

				if (calendarItem != null)
				{
					if (model.Item.SignupType == (int)CalendarItemSignupTypes.None && !string.IsNullOrWhiteSpace(model.entities))
					{
						// None signup: add selected entities as direct attendees and notify only them
						var newUserIds = await AddEntitiesAsAttendeesAsync(calendarItem, model.entities, new HashSet<string>(), cancellationToken);
						if (newUserIds.Any())
						{
							try { await _calendarService.NotifyUsersAboutCalendarItemAsync(calendarItem, newUserIds); } catch { }
						}
					}
					else
					{
						// RSVP mode: notify entities (groups/department) via existing mechanism
						try { await _calendarService.NotifyNewCalendarItemAsync(calendarItem); } catch { }
					}
				}

				return RedirectToAction("Index");
			}

			model.Item.Description = StringHelpers.StripHtmlTagsCharArray(model.Item.Description);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Schedule_Update)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<IActionResult> Edit(int id)
		{
			var model = new EditCalendarEntry();

			if (!await _authorizationService.CanUserModifyCalendarEntryAsync(UserId, id))
				return Unauthorized();

			model.Item = await _calendarService.GetCalendarItemByIdAsync(id);
			model.Types = new List<CalendarItemType>();
			model.Types.Add(new CalendarItemType() { CalendarItemTypeId = 0, Name = "No Type" });
			model.Types.AddRange(await _calendarService.GetAllCalendarItemTypesForDepartmentAsync(DepartmentId));

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			// All-day events are stored as UTC (00:00:00 ? 23:59:59 local converted to UTC).
			// Convert back to local time first, then take just the date for the date-only picker.
			if (model.Item.IsAllDay)
			{
				model.StartTime = model.Item.Start.TimeConverter(department).Date;
				model.EndTime   = model.Item.End.TimeConverter(department).Date;
			}
			else
			{
				model.StartTime = model.Item.Start.TimeConverter(department);
				model.EndTime   = model.Item.End.TimeConverter(department);
			}

			if (model.Item.RecurrenceEnd.HasValue)
				model.RecurrenceEndLocal = model.Item.RecurrenceEnd.Value.TimeConverter(department);

			var recurrences = await _calendarService.GetAllCalendarItemRecurrencesAsync(model.Item.CalendarItemId);
			if (recurrences != null && recurrences.Any())
				model.IsRecurrenceParent = true;

			ViewBag.Types = new SelectList(model.Types, "CalendarItemTypeId", "Name");
			model.Item.Description = StringHelpers.StripHtmlTagsCharArray(model.Item.Description);

			if (model.Item.RecurrenceType == 2 && model.Item.RepeatOnWeek > 0)
			{
				model.WeekdayOccurrence = model.Item.RepeatOnWeek;
				model.WeekdayDayOfWeek = model.Item.RepeatOnDay;
			}

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Schedule_Update)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<IActionResult> Edit(EditCalendarEntry model, CancellationToken cancellationToken)
		{
			if (model.StartTime > model.EndTime)
			{
				ModelState.AddModelError("Item_End", "End date and time cannot be before start date and time.");
			}

			if ((model.Item.RecurrenceType == (int)RecurrenceTypes.Weekly
			     || model.Item.RecurrenceType == (int)RecurrenceTypes.Monthly
			     || model.Item.RecurrenceType == (int)RecurrenceTypes.Yearly) &&
			    (model.RecurrenceEndLocal.HasValue && (model.RecurrenceEndLocal.Value <= model.StartTime || model.RecurrenceEndLocal.Value <= model.EndTime)))
			{
				ModelState.AddModelError("Item_End", "End date and time cannot be before start date and time.");
			}

			if (ModelState.IsValid)
			{
				var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

				// Snapshot existing attendees before update so we can diff for notifications
				var existingItem = await _calendarService.GetCalendarItemByIdAsync(model.Item.CalendarItemId);
				var existingAttendeeIds = new HashSet<string>();
				if (existingItem?.Attendees != null)
				{
					foreach (var a in existingItem.Attendees)
						existingAttendeeIds.Add(a.UserId);
				}

				model.Item.Start = model.StartTime;
				model.Item.End = model.EndTime;
				model.Item.RecurrenceEnd = model.RecurrenceEndLocal;
				model.Item.DepartmentId = DepartmentId;
				model.Item.CreatorUserId = UserId;
				model.Item.Entities = model.entities;

				if (model.Item.RecurrenceType == 2)
				{
					model.Item.RepeatOnWeek = model.WeekdayOccurrence;
					model.Item.RepeatOnDay = model.WeekdayDayOfWeek;
				}

				await _calendarService.UpdateCalendarItemAsync(model.Item, department.TimeZone, cancellationToken);

				// Add new attendees from entities and notify only the newly added ones
				// Skip notifications for past events (bookkeeping after the fact)
				if (model.Item.SignupType == (int)CalendarItemSignupTypes.None && !string.IsNullOrWhiteSpace(model.entities))
				{
					var updatedItem = await _calendarService.GetCalendarItemByIdAsync(model.Item.CalendarItemId);
					var newUserIds = await AddEntitiesAsAttendeesAsync(updatedItem, model.entities, existingAttendeeIds, cancellationToken);
					if (newUserIds.Any() && updatedItem.End > DateTime.UtcNow)
					{
						try { await _calendarService.NotifyUsersAboutCalendarItemAsync(updatedItem, newUserIds); } catch { }
					}
				}

				return RedirectToAction("Index");
			}

			model.Types = new List<CalendarItemType>();
			model.Types.Add(new CalendarItemType() { CalendarItemTypeId = 0, Name = "No Type" });
			model.Types.AddRange(await _calendarService.GetAllCalendarItemTypesForDepartmentAsync(DepartmentId));
			ViewBag.Types = new SelectList(model.Types, "CalendarItemTypeId", "Name");

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Schedule_Create)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<JsonResult> CreateCalendarItem([FromBody]CalendarItemJson item, CancellationToken cancellationToken)
		{
			if (ModelState.IsValid)
			{
				var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

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
					calendarItem.Start = DateTimeHelpers.ConvertToUtc(DateTime.Parse(item.Start), timeZone);
				else
					calendarItem.Start = DateTimeHelpers.ConvertToUtc(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 0, 0, 0), timeZone);

				if (!String.IsNullOrWhiteSpace(item.End))
					calendarItem.End = DateTimeHelpers.ConvertToUtc(DateTime.Parse(item.End), timeZone);
				else
					calendarItem.End = DateTimeHelpers.ConvertToUtc(new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, 23, 59, 59), timeZone);

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

				calendarItem = await _calendarService.SaveCalendarItemAsync(calendarItem, cancellationToken);

				_eventAggregator.SendMessage<CalendarEventAddedEvent>(new CalendarEventAddedEvent() { DepartmentId = DepartmentId, Item = calendarItem});

				return Json(item);
			}

			return null;
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Schedule_Update)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<JsonResult> UpdateCalendarItem(CalendarItemJson item, CancellationToken cancellationToken)
		{
			if (ModelState.IsValid)
			{
				var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

				var calendarItem = await _calendarService.GetCalendarItemByIdAsync(item.CalendarItemId);

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

					var updateTimeZone = "Etc/UTC";
					if (!String.IsNullOrWhiteSpace(department.TimeZone))
						updateTimeZone = DateTimeHelpers.WindowsToIana(department.TimeZone);

					calendarItem.Start = DateTimeHelpers.ConvertToUtc(DateTime.Parse(item.Start), updateTimeZone);
					calendarItem.End = DateTimeHelpers.ConvertToUtc(DateTime.Parse(item.End), updateTimeZone);

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

					calendarItem = await _calendarService.SaveCalendarItemAsync(calendarItem, cancellationToken);

					_eventAggregator.SendMessage<CalendarEventUpdatedEvent>(new CalendarEventUpdatedEvent() { DepartmentId = DepartmentId, Item = calendarItem });

					return Json(item);
				}
			}

			return null;
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Schedule_Delete)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<IActionResult> DeleteCalendarItem([FromBody]CalendarItemJson item, CancellationToken cancellationToken)
		{
			if (ModelState.IsValid)
			{
				var calandarItem = await _calendarService.GetCalendarItemByIdAsync(item.CalendarItemId);

				if (calandarItem == null || calandarItem.DepartmentId != DepartmentId)
					return Unauthorized();

				await _calendarService.DeleteCalendarItemByIdAsync(item.CalendarItemId, cancellationToken);
			}

			return null;
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Schedule_Delete)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<IActionResult> DeleteCalendarItem(int itemId, CancellationToken cancellationToken)
		{
			var calandarItem = await _calendarService.GetCalendarItemByIdAsync(itemId);

			if (calandarItem == null || calandarItem.DepartmentId != DepartmentId)
				return Unauthorized();

			await _calendarService.DeleteCalendarItemByIdAsync(itemId, cancellationToken);

			return RedirectToAction("Index");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Schedule_Delete)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<IActionResult> DeleteAllCalendarItems(int itemId, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserModifyCalendarEntryAsync(UserId, itemId))
				return Unauthorized();

			await _calendarService.DeleteCalendarItemAndRecurrences(itemId, cancellationToken);

			return RedirectToAction("Index");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Schedule_View)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]

		public async Task<IActionResult> GetDepartmentCalendarItems()
		{
			List<CalendarItemJson> jsonItems = new List<CalendarItemJson>();
			var items = await _calendarService.GetAllCalendarItemsForDepartmentAsync(DepartmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

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
					var parent = await _calendarService.GetCalendarItemByIdAsync(int.Parse(item.RecurrenceId));

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
		public async Task<IActionResult> GetV2CalendarEntriesForCal(string start, string end)
		{
			var jsonItems = new List<CalendarItemV2Json>();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var items = await _calendarService.GetAllCalendarItemsForDepartmentAsync(DepartmentId, DateTime.UtcNow.AddMonths(-6));
			var itemTypes = await _calendarService.GetAllCalendarItemTypesForDepartmentAsync(DepartmentId);

			foreach (var item in items)
			{
				var jsonItem = new CalendarItemV2Json();
				jsonItem.id = item.CalendarItemId;
				jsonItem.title = item.Title;
				jsonItem.allDay = item.IsAllDay;

				if (item.IsAllDay)
				{
					// All-day events are stored as UTC. Convert back to the department's local time,
					// then use date-only strings. FullCalendar requires an exclusive end date so add 1 day.
					var localStart = item.Start.TimeConverter(department);
					var localEnd   = item.End.TimeConverter(department);
					jsonItem.start = localStart.ToString("yyyy-MM-dd");
					jsonItem.end   = localEnd.Date.AddDays(1).ToString("yyyy-MM-dd");
				}
				else
				{
					jsonItem.start = item.Start.TimeConverter(department).ToString("O");
					jsonItem.end = item.End.TimeConverter(department).ToString("O");
				}

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

		public async Task<IActionResult> RemoveFromEvent(int id, CancellationToken cancellationToken)
		{
			var attendee = await _calendarService.GetCalendarAttendeeByIdAsync(id);

			await _calendarService.DeleteCalendarAttendeeByIdAsync(id, cancellationToken);

			return RedirectToAction("View", new { calendarItemId = attendee.CalendarItemId });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Schedule_View)]

		public async Task<IActionResult> View(int calendarItemId)
		{
			var model = new CalendarItemView();
			model.CalendarItem = await _calendarService.GetCalendarItemByIdAsync(calendarItemId);

			if (model.CalendarItem == null)
				return Unauthorized();

			if (model.CalendarItem.DepartmentId != DepartmentId)
				return Unauthorized();

			model.CalendarItem.Description = StringHelpers.SanitizeHtmlInString(model.CalendarItem.Description);

			model.UserId = UserId;
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			var recurrences = await _calendarService.GetAllCalendarItemRecurrencesAsync(calendarItemId);
			if (recurrences != null && recurrences.Any())
				model.IsRecurrenceParent = true;

			model.CanEdit = await _authorizationService.CanUserModifyCalendarEntryAsync(UserId, calendarItemId);

			model.ExportIcsUrl = $"{SystemBehaviorConfig.ResgridApiBaseUrl}/api/v4/CalendarExport/ExportICalFile?calendarItemId={calendarItemId}";

			// Check-in attendance data
			model.UserCheckIn = await _calendarService.GetCheckInByCalendarItemAndUserAsync(calendarItemId, UserId);
			model.CheckIns = await _calendarService.GetCheckInsByCalendarItemAsync(calendarItemId);
			model.PersonnelNames = await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId);
			model.IsAdmin = model.Department.IsUserAnAdmin(UserId);

			// Check if user is event creator or group admin (for admin check-in buttons)
			if (!model.IsAdmin)
			{
				if (!string.IsNullOrWhiteSpace(model.CalendarItem.CreatorUserId) && model.CalendarItem.CreatorUserId == UserId)
					model.IsAdmin = true;
				else
				{
					var group = await _departmentGroupsService.GetGroupForUserAsync(UserId, DepartmentId);
					if (group != null && group.IsUserGroupAdmin(UserId))
						model.IsAdmin = true;
				}
			}

			return View(model);
		}

		[Authorize(Policy = ResgridResources.Schedule_View)]
		[HttpPost]
		public async Task<IActionResult> Signup(CalendarItemView model, CancellationToken cancellationToken)
		{
			await _calendarService.SignupForEvent(model.CalendarItem.CalendarItemId, UserId, model.Note, (int)CalendarItemAttendeeTypes.RSVP, cancellationToken);

			return RedirectToAction("View", new { calendarItemId = model.CalendarItem.CalendarItemId });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Schedule_View)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<IActionResult> SignupSheet(int calendarItemId, int rows = 0)
		{
			var calendarItem = await _calendarService.GetCalendarItemByIdAsync(calendarItemId);
			if (calendarItem == null || calendarItem.DepartmentId != DepartmentId)
				return Unauthorized();

			if (!await _authorizationService.CanUserModifyCalendarEntryAsync(UserId, calendarItemId))
				return Unauthorized();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var personnelNames = await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId);

			var model = new SignupSheetView();
			model.CalendarItem = calendarItem;
			model.Department = department;
			model.PersonnelNames = personnelNames;
			model.TotalRows = rows > 0 ? rows : 25;

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Schedule_View)]

		public async Task<IActionResult> GetDepartmentCalendarItemTypes()
		{
			var jsonItems = new List<CalendarItemTypeJson>();
			var items = await _calendarService.GetAllCalendarItemTypesForDepartmentAsync(DepartmentId);

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
		public async Task<IActionResult> Types()
		{
			TypesView model = new TypesView();
			model.Types = await _calendarService.GetAllCalendarItemTypesForDepartmentAsync(DepartmentId);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Schedule_Create)]
		public async Task<IActionResult> NewType()
		{
			NewTypeView model = new NewTypeView();
			model.Type = new CalendarItemType();

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Schedule_Create)]
		public async Task<IActionResult> NewType(NewTypeView model, CancellationToken cancellationToken)
		{
			if ((await _calendarService.GetAllCalendarItemTypesForDepartmentAsync(DepartmentId)).Any(x => x.Name == model.Type.Name))
				ModelState.AddModelError("", "Type name already exists, please choose another name.");

			if (ModelState.IsValid)
			{
				model.Type.DepartmentId = DepartmentId;
				await _calendarService.SaveCalendarItemTypeAsync(model.Type, cancellationToken);

				return RedirectToAction("Types", "Calendar", new { Area = "User" });
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Schedule_Create)]
		public async Task<IActionResult> EditType(string id)
		{
			if (String.IsNullOrWhiteSpace(id))
				return BadRequest();

			if (!int.TryParse(id, out int parsedId))
				return BadRequest();

			var calendarItemType = await _calendarService.GetCalendarItemTypeByIdAsync(parsedId);

			if (calendarItemType == null)
				return NotFound();

			if (calendarItemType.DepartmentId != DepartmentId)
				return Unauthorized();

			NewTypeView model = new NewTypeView();
			model.Type = calendarItemType;

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Schedule_Create)]
		public async Task<IActionResult> EditType(NewTypeView model, CancellationToken cancellationToken)
		{
			if (ModelState.IsValid)
			{
				var savedType = await _calendarService.GetCalendarItemTypeByIdAsync(model.Type.CalendarItemTypeId);

				if (savedType == null)
					return Unauthorized();

				if (savedType.DepartmentId != DepartmentId)
					return Unauthorized();

				savedType.Name = model.Type.Name;
				savedType.Color = model.Type.Color;
				await _calendarService.SaveCalendarItemTypeAsync(savedType, cancellationToken);

				return RedirectToAction("Types", "Calendar", new { Area = "User" });
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Schedule_Create)]
		public async Task<IActionResult> DeleteType(int typeId, CancellationToken cancellationToken)
		{
			var type = await _calendarService.GetCalendarItemTypeByIdAsync(typeId);

			if (type is null)
				return NotFound();

			if (type.DepartmentId != DepartmentId)
				return Unauthorized();

			await _calendarService.DeleteCalendarItemTypeAsync(type, cancellationToken);

			return RedirectToAction("Types");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Schedule_View)]
		public async Task<IActionResult> GetDepartmentEnitites()
		{
			List<DepartmentEntitiesJson> items = new List<DepartmentEntitiesJson>();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);

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
		public async Task<IActionResult> GetMapDataForItem(int calendarItemId)
		{
			dynamic result = new ExpandoObject();

			var calendarItem = await _calendarService.GetCalendarItemByIdAsync(calendarItemId);

			if (calendarItem.DepartmentId == DepartmentId && !String.IsNullOrWhiteSpace(calendarItem.Location))
			{
				string locationResult = null;
				try
				{
					locationResult = await _geoLocationProvider.GetLatLonFromAddress(calendarItem.Location);
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

		/// <summary>
		/// Activates external calendar sync for the current user. Generates a new CalendarSyncToken
		/// on their profile and redirects back to the Index view where the subscription URL is shown.
		/// </summary>
		[HttpPost]
		[Authorize(Policy = ResgridResources.Schedule_View)]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> ActivateCalendarSync(CancellationToken cancellationToken)
		{
			var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.UseCalendarSync);
			var dept = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var grp = await _departmentGroupsService.GetGroupForUserAsync(UserId, DepartmentId);
			var roles = await _personnelRolesService.GetRolesForUserAsync(UserId, DepartmentId);
			if (!_permissionsService.IsUserAllowed(permission, dept.IsUserAnAdmin(UserId), grp != null && grp.IsUserGroupAdmin(UserId), roles))
				return Unauthorized();

			await _calendarService.ActivateCalendarSyncAsync(DepartmentId, UserId, cancellationToken);
			return RedirectToAction("Index");
		}

		/// <summary>
		/// Regenerates the external calendar sync token, invalidating any previously issued
		/// subscription URLs, then redirects back to the Index view.
		/// </summary>
		[HttpPost]
		[Authorize(Policy = ResgridResources.Schedule_View)]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> RegenerateCalendarSync(CancellationToken cancellationToken)
		{
			var permission = await _permissionsService.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.UseCalendarSync);
			var dept = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var grp = await _departmentGroupsService.GetGroupForUserAsync(UserId, DepartmentId);
			var roles = await _personnelRolesService.GetRolesForUserAsync(UserId, DepartmentId);
			if (!_permissionsService.IsUserAllowed(permission, dept.IsUserAnAdmin(UserId), grp != null && grp.IsUserGroupAdmin(UserId), roles))
				return Unauthorized();

			await _calendarService.RegenerateCalendarSyncAsync(DepartmentId, UserId, cancellationToken);
			return RedirectToAction("Index");
		}

		/// <summary>
		/// Admin action: regenerates calendar sync tokens for ALL users in the department who have one provisioned.
		/// </summary>
		[HttpPost]
		[Authorize(Policy = ResgridResources.Department_Update)]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> RegenerateAllCalendarSyncTokens(CancellationToken cancellationToken)
		{
			var members = await _departmentsService.GetAllMembersForDepartmentAsync(DepartmentId);
			if (members != null)
			{
				foreach (var member in members)
				{
					var profile = await _userProfileService.GetProfileByUserIdAsync(member.UserId);
					if (profile != null && !string.IsNullOrWhiteSpace(profile.CalendarSyncToken))
					{
						await _calendarService.RegenerateCalendarSyncAsync(DepartmentId, member.UserId, cancellationToken);
					}
				}
			}

			return RedirectToAction("Api", "Department");
		}

		// -- Check-In Attendance -------------------------------------------------------

		[HttpPost]
		[Authorize(Policy = ResgridResources.Schedule_View)]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> CheckIn(int calendarItemId, string checkInNote, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserCheckInToCalendarEventAsync(UserId, calendarItemId))
				return Unauthorized();

			var checkIn = await _calendarService.CheckInToEventAsync(calendarItemId, UserId, checkInNote, cancellationToken: cancellationToken);

			if (checkIn != null)
			{
				var auditEvent = new AuditEvent();
				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.Type = AuditLogTypes.CalendarCheckInPerformed;
				auditEvent.After = checkIn.CloneJsonToString();
				auditEvent.Successful = true;
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);
			}

			return RedirectToAction("View", new { calendarItemId = calendarItemId });
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Schedule_View)]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> CheckOut(int calendarItemId, string checkOutNote, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserCheckInToCalendarEventAsync(UserId, calendarItemId))
				return Unauthorized();

			var checkIn = await _calendarService.CheckOutFromEventAsync(calendarItemId, UserId, checkOutNote, cancellationToken: cancellationToken);

			if (checkIn != null)
			{
				var auditEvent = new AuditEvent();
				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.Type = AuditLogTypes.CalendarCheckOutPerformed;
				auditEvent.After = checkIn.CloneJsonToString();
				auditEvent.Successful = true;
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);
			}

			return RedirectToAction("View", new { calendarItemId = calendarItemId });
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Schedule_View)]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> AdminCheckIn(int calendarItemId, string userId, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserAdminCheckInCalendarEventAsync(UserId, calendarItemId, userId))
				return Unauthorized();

			var checkIn = await _calendarService.CheckInToEventAsync(calendarItemId, userId, null, UserId, cancellationToken: cancellationToken);

			if (checkIn != null)
			{
				var auditEvent = new AuditEvent();
				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.Type = AuditLogTypes.CalendarAdminCheckInPerformed;
				auditEvent.After = checkIn.CloneJsonToString();
				auditEvent.Successful = true;
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);
			}

			return RedirectToAction("View", new { calendarItemId = calendarItemId });
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Schedule_View)]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> AdminCheckOut(int calendarItemId, string userId, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserAdminCheckInCalendarEventAsync(UserId, calendarItemId, userId))
				return Unauthorized();

			var checkIn = await _calendarService.CheckOutFromEventAsync(calendarItemId, userId, null, UserId, cancellationToken: cancellationToken);

			if (checkIn != null)
			{
				var auditEvent = new AuditEvent();
				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.Type = AuditLogTypes.CalendarCheckOutPerformed;
				auditEvent.After = checkIn.CloneJsonToString();
				auditEvent.Successful = true;
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);
			}

			return RedirectToAction("View", new { calendarItemId = calendarItemId });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Schedule_Update)]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<IActionResult> EditCheckIn(string checkInId)
		{
			if (!await _authorizationService.CanUserEditCalendarCheckInAsync(UserId, checkInId))
				return Unauthorized();

			var checkIn = await _calendarService.GetCheckInByIdAsync(checkInId);
			if (checkIn == null)
				return NotFound();

			var model = new EditCalendarCheckInView();
			model.CheckIn = checkIn;

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Schedule_Update)]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> EditCheckIn(EditCalendarCheckInView model, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserEditCalendarCheckInAsync(UserId, model.CheckIn.CalendarItemCheckInId))
				return Unauthorized();

			var existing = await _calendarService.GetCheckInByIdAsync(model.CheckIn.CalendarItemCheckInId);
			var beforeJson = existing?.CloneJsonToString();

			var updated = await _calendarService.UpdateCheckInTimesAsync(
				model.CheckIn.CalendarItemCheckInId,
				model.CheckIn.CheckInTime,
				model.CheckIn.CheckOutTime,
				model.CheckIn.CheckInNote,
				model.CheckIn.CheckOutNote,
				cancellationToken);

			if (updated != null)
			{
				var auditEvent = new AuditEvent();
				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.Type = AuditLogTypes.CalendarCheckInUpdated;
				auditEvent.Before = beforeJson;
				auditEvent.After = updated.CloneJsonToString();
				auditEvent.Successful = true;
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				return RedirectToAction("View", new { calendarItemId = updated.CalendarItemId });
			}

			return NotFound();
		}

		// -- Helpers ------------------------------------------------------------------

		/// <summary>
		/// Resolves entity strings (D: for department, G:123 for groups) into individual users,
		/// creates attendee records for any user not already attending, and returns the list of
		/// newly added user IDs (so only they can be notified).
		/// </summary>
		private async Task<List<string>> AddEntitiesAsAttendeesAsync(CalendarItem calendarItem, string entities,
			HashSet<string> existingAttendeeUserIds, CancellationToken cancellationToken)
		{
			var newlyAdded = new List<string>();
			if (string.IsNullOrWhiteSpace(entities))
				return newlyAdded;

			var items = entities.Split(',');
			var processedUserIds = new HashSet<string>();

			foreach (var val in items)
			{
				if (val.StartsWith("D:"))
				{
					var personnelNames = await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(calendarItem.DepartmentId);
					if (personnelNames != null)
					{
						foreach (var person in personnelNames)
						{
							if (processedUserIds.Add(person.UserId) && !existingAttendeeUserIds.Contains(person.UserId))
							{
								await _calendarService.SignupForEvent(calendarItem.CalendarItemId, person.UserId, null,
									(int)CalendarItemAttendeeTypes.Required, cancellationToken);
								newlyAdded.Add(person.UserId);
							}
						}
					}
				}
				else if (val.StartsWith("G:"))
				{
					int groupId;
					if (int.TryParse(val.Replace("G:", ""), out groupId))
					{
						var group = await _departmentGroupsService.GetGroupByIdAsync(groupId);
						if (group != null && group.DepartmentId == calendarItem.DepartmentId && group.Members != null)
						{
							foreach (var member in group.Members)
							{
								if (processedUserIds.Add(member.UserId) && !existingAttendeeUserIds.Contains(member.UserId))
								{
									await _calendarService.SignupForEvent(calendarItem.CalendarItemId, member.UserId, null,
										(int)CalendarItemAttendeeTypes.Required, cancellationToken);
									newlyAdded.Add(member.UserId);
								}
							}
						}
					}
				}
			}

			return newlyAdded;
		}
	}
}
