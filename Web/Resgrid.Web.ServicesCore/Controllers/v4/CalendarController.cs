using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using Microsoft.AspNetCore.Authorization;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Controllers.Version3.Models.Calendar;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Calendar;
using CalendarItem = Resgrid.Model.CalendarItem;
using CalendarItemAttendee = Resgrid.Model.CalendarItemAttendee;
using CalendarItemType = Resgrid.Model.CalendarItemType;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Mobile or Tablet Device specific operations
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class CalendarController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly ICalendarService _calendarService;
		private readonly IDepartmentsService _departmentsService;

		public CalendarController(ICalendarService calendarService, IDepartmentsService departmentsService)
		{
			_calendarService = calendarService;
			_departmentsService = departmentsService;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Gets the department calendar items.
		/// </summary>
		/// <returns>ActionResult&lt;List&lt;CalendarItem&gt;&gt;.</returns>
		[HttpGet("GetDepartmentCalendarItems")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Schedule_View)]
		public async Task<ActionResult<GetAllCalendarItemResult>> GetDepartmentCalendarItems()
		{
			var result = new GetAllCalendarItemResult();
			result.Data = new List<GetAllCalendarItemResultData>();

			var items = await _calendarService.GetAllCalendarItemsForDepartmentAsync(DepartmentId);
			var types = await _calendarService.GetAllCalendarItemTypesForDepartmentAsync(DepartmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var presonnelNames = await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId);

			if (items != null && items.Any())
			{
				items = items.OrderBy(x => x.Start).ToList();
				foreach (var item in items)
				{
					if (item.ItemType > 0)
					{
						var itemType = types.FirstOrDefault(x => x.CalendarItemTypeId == item.ItemType);
						result.Data.Add(ConvertCalendarItemData(item, department, UserId, itemType, presonnelNames));
					}
					else
					{
						result.Data.Add(ConvertCalendarItemData(item, department, UserId, null, presonnelNames));
					}
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
		/// Gets the department calendar items in range.
		/// </summary>
		/// <param name="start">The start.</param>
		/// <param name="end">The end.</param>
		/// <returns>ActionResult&lt;List&lt;CalendarItem&gt;&gt;.</returns>
		[HttpGet("GetDepartmentCalendarItemsInRange")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Schedule_View)]
		public async Task<ActionResult<GetAllCalendarItemResult>> GetDepartmentCalendarItemsInRange(DateTime start, DateTime end)
		{
			var result = new GetAllCalendarItemResult();
			result.Data = new List<GetAllCalendarItemResultData>();

			var items = await _calendarService.GetAllCalendarItemsForDepartmentInRangeAsync(DepartmentId, start.SetToMidnight(), end.SetToEndOfDay());
			var types = await _calendarService.GetAllCalendarItemTypesForDepartmentAsync(DepartmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var presonnelNames = await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId);
			
			if (items != null && items.Any())
			{
				items = items.OrderBy(x => x.Start).ToList();
				foreach (var item in items)
				{
					if (item.ItemType > 0)
					{
						var itemType = types.FirstOrDefault(x => x.CalendarItemTypeId == item.ItemType);
						result.Data.Add(ConvertCalendarItemData(item, department, UserId, itemType, presonnelNames));
					}
					else
					{
						result.Data.Add(ConvertCalendarItemData(item, department, UserId, null, presonnelNames));
					}
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
		/// Gets the calendar item.
		/// </summary>
		/// <param name="id">The identifier.</param>
		/// <returns>ActionResult&lt;CalendarItem&gt;.</returns>
		[HttpGet("GetCalendarItem")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Schedule_View)]
		public async Task<ActionResult<GetCalendarItemResult>> GetCalendarItem(int id)
		{
			var result = new GetCalendarItemResult();
			var item = await _calendarService.GetCalendarItemByIdAsync(id);
			var types = await _calendarService.GetAllCalendarItemTypesForDepartmentAsync(DepartmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var presonnelNames = await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId);
			
			if (item != null)
			{
				if (item.DepartmentId != DepartmentId)
					return Unauthorized();

				if (item.ItemType > 0)
				{
					var itemType = types.FirstOrDefault(x => x.CalendarItemTypeId == item.ItemType);
					result.Data = ConvertCalendarItemData(item, department, UserId, itemType, presonnelNames);
				}
				else
				{
					result.Data = ConvertCalendarItemData(item, department, UserId, null, presonnelNames);
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
		/// Gets the department calendar item types.
		/// </summary>
		/// <returns>ActionResult&lt;List&lt;CalendarItemType&gt;&gt;.</returns>
		[HttpGet("GetDepartmentCalendarItemTypes")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Schedule_View)]
		public async Task<ActionResult<GetAllCalendarItemTypesResult>> GetDepartmentCalendarItemTypes()
		{
			var result = new GetAllCalendarItemTypesResult();
			result.Data = new List<GetAllCalendarItemTypesResultData>();

			var items = await _calendarService.GetAllCalendarItemTypesForDepartmentAsync(DepartmentId);

			result.Data.Add(new GetAllCalendarItemTypesResultData()
			{
				CalendarItemTypeId = "0",
				Color = "#EEE",
				Name = "None"
			});

			foreach (var item in items)
			{
				result.Data.Add(ConvertGetAllCalendarItemTypesResultData(item));
			}

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		///
		/// </summary>
		/// <param name="input"></param>
		/// <returns></returns>
		[HttpPost("SetCalendarAttendingStatus")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Schedule_View)]
		public async Task<ActionResult<SetCalendarAttendingResult>> SetCalendarAttendingStatus(CalendarItemAttendInput input)
		{
			var result = new SetCalendarAttendingResult();

			if (string.IsNullOrWhiteSpace(input.CalendarEventId))
				return NotFound();

			var calendarItem = await _calendarService.GetCalendarItemByIdAsync(int.Parse(input.CalendarEventId));

			if (calendarItem == null)
				return NotFound();

			if (calendarItem.DepartmentId != DepartmentId)
				return Unauthorized();

			var signupResult = await _calendarService.SignupForEvent(int.Parse(input.CalendarEventId), UserId, input.Note, input.Type);

			if (signupResult == null)
			{
				result.Id = "";
				result.PageSize = 0;
				result.Status = ResponseHelper.Failure;
			}
			else
			{
				result.Id = signupResult.CalendarItemAttendeeId.ToString();
				result.PageSize = 0;
				result.Status = ResponseHelper.Created;
			}

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		public static GetAllCalendarItemResultData ConvertCalendarItemData(CalendarItem item, Department department, string currentUserId, CalendarItemType type, List<PersonName> personnelNames)
		{
			var calendarItem = new GetAllCalendarItemResultData();
			calendarItem.CalendarItemId = item.CalendarItemId.ToString();
			calendarItem.Title = item.Title;
			calendarItem.Start = item.Start.TimeConverter(department);
			calendarItem.StartUtc = item.Start;
			calendarItem.End = item.End.TimeConverter(department);
			calendarItem.EndUtc = item.End;
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

			if (type != null)
			{
				calendarItem.TypeName = type.Name;
				calendarItem.TypeColor = type.Color;
			}
			else
			{
				calendarItem.TypeColor = "#1e90ff";
			}

			if (!String.IsNullOrWhiteSpace(item.CreatorUserId))
				calendarItem.CreatorUserId = item.CreatorUserId.ToString();

			if (department.IsUserAnAdmin(currentUserId))
				calendarItem.IsAdminOrCreator = true;
			else if (!String.IsNullOrWhiteSpace(item.CreatorUserId) && item.CreatorUserId == currentUserId)
				calendarItem.IsAdminOrCreator = true;
			else
				calendarItem.IsAdminOrCreator = false;

			if (item.Attendees == null || !item.Attendees.Any())
				calendarItem.Attending = false;
			else
			{
				calendarItem.Attending = item.Attendees.Any(x => x.UserId == currentUserId);
				calendarItem.Attendees = new List<CalendarItemResultAttendeeData>();

				foreach (var attendee in item.Attendees)
				{
					CalendarItemResultAttendeeData attend = new CalendarItemResultAttendeeData();
					attend.CalendarItemId = attendee.CalendarItemId.ToString();
					attend.UserId = attendee.UserId;
					attend.AttendeeType = attendee.AttendeeType;
					attend.Note = attendee.Note;
					attend.Timestamp = attendee.Timestamp.TimeConverter(department);

					if (personnelNames != null && personnelNames.Any())
					{
						var name = personnelNames.FirstOrDefault(x => x.UserId == attendee.UserId);
						if (name != null)
							attend.Name = name.Name;
					}

					calendarItem.Attendees.Add(attend);
				}
			}

			return calendarItem;
		}

		public static GetAllCalendarItemTypesResultData ConvertGetAllCalendarItemTypesResultData(CalendarItemType type)
		{
			var calendarItemType = new GetAllCalendarItemTypesResultData();
			calendarItemType.CalendarItemTypeId = type.CalendarItemTypeId.ToString();
			calendarItemType.Name = type.Name;
			calendarItemType.Color = type.Color;

			return calendarItemType;
		}
	}
}
