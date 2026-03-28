using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using Microsoft.AspNetCore.Authorization;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Controllers.Version3.Models.Calendar;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Calendar;
using Resgrid.Web.ServicesCore.Helpers;
using CalendarItem = Resgrid.Model.CalendarItem;
using CalendarItemAttendee = Resgrid.Model.CalendarItemAttendee;
using CalendarItemType = Resgrid.Model.CalendarItemType;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

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
		private readonly IAuthorizationService _authorizationService;
		private readonly IEventAggregator _eventAggregator;
		private readonly IUserProfileService _userProfileService;

		public CalendarController(ICalendarService calendarService, IDepartmentsService departmentsService,
			IAuthorizationService authorizationService, IEventAggregator eventAggregator,
			IUserProfileService userProfileService)
		{
			_calendarService = calendarService;
			_departmentsService = departmentsService;
			_authorizationService = authorizationService;
			_eventAggregator = eventAggregator;
			_userProfileService = userProfileService;
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

		/// <summary>
		/// Checks in to a calendar event
		/// </summary>
		[HttpPost("SetCalendarCheckIn")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[Authorize(Policy = ResgridResources.Schedule_View)]
		public async Task<ActionResult<SetCalendarCheckInResult>> SetCalendarCheckIn([FromBody] CalendarCheckInInput input, CancellationToken cancellationToken)
		{
			var result = new SetCalendarCheckInResult();

			if (input == null || input.CalendarEventId <= 0)
				return BadRequest();

			var targetUserId = !string.IsNullOrWhiteSpace(input.UserId) && input.UserId != UserId ? input.UserId : UserId;
			var isAdminCheckIn = targetUserId != UserId;

			if (isAdminCheckIn)
			{
				if (!await _authorizationService.CanUserAdminCheckInCalendarEventAsync(UserId, input.CalendarEventId, targetUserId))
					return Unauthorized();
			}
			else
			{
				if (!await _authorizationService.CanUserCheckInToCalendarEventAsync(UserId, input.CalendarEventId))
					return Unauthorized();
			}

			var checkIn = await _calendarService.CheckInToEventAsync(input.CalendarEventId, targetUserId, input.Note,
				isAdminCheckIn ? UserId : null, input.Latitude, input.Longitude, cancellationToken);

			if (checkIn == null)
			{
				result.Id = "";
				result.PageSize = 0;
				result.Status = ResponseHelper.Failure;
			}
			else
			{
				result.Id = checkIn.CalendarItemCheckInId;
				result.PageSize = 1;
				result.Status = ResponseHelper.Success;

				var auditEvent = new AuditEvent();
				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.Type = isAdminCheckIn ? AuditLogTypes.CalendarAdminCheckInPerformed : AuditLogTypes.CalendarCheckInPerformed;
				auditEvent.After = checkIn.CloneJsonToString();
				auditEvent.Successful = true;
				auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
				auditEvent.ServerName = Environment.MachineName;
				auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);
			}

			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Checks out from a calendar event
		/// </summary>
		[HttpPost("SetCalendarCheckOut")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[Authorize(Policy = ResgridResources.Schedule_View)]
		public async Task<ActionResult<SetCalendarCheckInResult>> SetCalendarCheckOut([FromBody] CalendarCheckOutInput input, CancellationToken cancellationToken)
		{
			var result = new SetCalendarCheckInResult();

			if (input == null || input.CalendarEventId <= 0)
				return BadRequest();

			var targetUserId = !string.IsNullOrWhiteSpace(input.UserId) && input.UserId != UserId ? input.UserId : UserId;
			var isAdminCheckOut = targetUserId != UserId;

			if (isAdminCheckOut)
			{
				if (!await _authorizationService.CanUserAdminCheckInCalendarEventAsync(UserId, input.CalendarEventId, targetUserId))
					return Unauthorized();
			}
			else
			{
				if (!await _authorizationService.CanUserCheckInToCalendarEventAsync(UserId, input.CalendarEventId))
					return Unauthorized();
			}

			var checkIn = await _calendarService.CheckOutFromEventAsync(input.CalendarEventId, targetUserId,
				input.Note, isAdminCheckOut ? UserId : null, input.Latitude, input.Longitude, cancellationToken);

			if (checkIn == null)
			{
				result.Id = "";
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}
			else
			{
				result.Id = checkIn.CalendarItemCheckInId;
				result.PageSize = 1;
				result.Status = ResponseHelper.Success;

				var auditEvent = new AuditEvent();
				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.Type = AuditLogTypes.CalendarCheckOutPerformed;
				auditEvent.After = checkIn.CloneJsonToString();
				auditEvent.Successful = true;
				auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
				auditEvent.ServerName = Environment.MachineName;
				auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);
			}

			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Updates check-in times for a calendar event
		/// </summary>
		[HttpPost("UpdateCalendarCheckIn")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[Authorize(Policy = ResgridResources.Schedule_Update)]
		public async Task<ActionResult<SetCalendarCheckInResult>> UpdateCalendarCheckIn([FromBody] CalendarCheckInUpdateInput input, CancellationToken cancellationToken)
		{
			var result = new SetCalendarCheckInResult();

			if (input == null || string.IsNullOrWhiteSpace(input.CheckInId))
				return BadRequest();

			if (!await _authorizationService.CanUserEditCalendarCheckInAsync(UserId, input.CheckInId))
				return Unauthorized();

			var existing = await _calendarService.GetCheckInByIdAsync(input.CheckInId);
			var beforeJson = existing?.CloneJsonToString();

			var checkIn = await _calendarService.UpdateCheckInTimesAsync(input.CheckInId, input.CheckInTime,
				input.CheckOutTime, input.CheckInNote, input.CheckOutNote, cancellationToken);

			if (checkIn == null)
			{
				result.Id = "";
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}
			else
			{
				result.Id = checkIn.CalendarItemCheckInId;
				result.PageSize = 1;
				result.Status = ResponseHelper.Success;

				var auditEvent = new AuditEvent();
				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.Type = AuditLogTypes.CalendarCheckInUpdated;
				auditEvent.Before = beforeJson;
				auditEvent.After = checkIn.CloneJsonToString();
				auditEvent.Successful = true;
				auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
				auditEvent.ServerName = Environment.MachineName;
				auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);
			}

			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Gets all check-ins for a calendar item
		/// </summary>
		[HttpGet("GetCalendarItemCheckIns")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[Authorize(Policy = ResgridResources.Schedule_View)]
		public async Task<ActionResult<GetCalendarCheckInsResult>> GetCalendarItemCheckIns(int calendarItemId)
		{
			var result = new GetCalendarCheckInsResult();
			result.Data = new List<CalendarCheckInResultData>();

			if (!await _authorizationService.CanUserViewCalendarCheckInsAsync(UserId, calendarItemId))
				return Unauthorized();

			var checkIns = await _calendarService.GetCheckInsByCalendarItemAsync(calendarItemId);
			var personnelNames = await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId);

			foreach (var checkIn in checkIns)
			{
				var data = new CalendarCheckInResultData
				{
					CheckInId = checkIn.CalendarItemCheckInId,
					CalendarItemId = checkIn.CalendarItemId,
					UserId = checkIn.UserId,
					CheckInTime = checkIn.CheckInTime,
					CheckOutTime = checkIn.CheckOutTime,
					DurationSeconds = checkIn.GetDuration()?.TotalSeconds,
					IsManualOverride = checkIn.IsManualOverride,
					CheckInNote = checkIn.CheckInNote,
					CheckOutNote = checkIn.CheckOutNote,
					CheckInLatitude = checkIn.CheckInLatitude,
					CheckInLongitude = checkIn.CheckInLongitude,
					CheckOutLatitude = checkIn.CheckOutLatitude,
					CheckOutLongitude = checkIn.CheckOutLongitude
				};

				var name = personnelNames?.FirstOrDefault(x => x.UserId == checkIn.UserId);
				if (name != null)
					data.Name = name.Name;

				if (!string.IsNullOrWhiteSpace(checkIn.CheckInByUserId))
				{
					var adminName = personnelNames?.FirstOrDefault(x => x.UserId == checkIn.CheckInByUserId);
					if (adminName != null)
						data.CheckInByName = adminName.Name;
				}

				if (!string.IsNullOrWhiteSpace(checkIn.CheckOutByUserId))
				{
					var outName = personnelNames?.FirstOrDefault(x => x.UserId == checkIn.CheckOutByUserId);
					if (outName != null)
						data.CheckOutByName = outName.Name;
				}

				result.Data.Add(data);
			}

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Deletes a calendar check-in record
		/// </summary>
		[HttpDelete("DeleteCalendarCheckIn")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[Authorize(Policy = ResgridResources.Schedule_Delete)]
		public async Task<ActionResult<SetCalendarCheckInResult>> DeleteCalendarCheckIn(string checkInId, CancellationToken cancellationToken)
		{
			var result = new SetCalendarCheckInResult();

			if (string.IsNullOrWhiteSpace(checkInId))
				return BadRequest();

			if (!await _authorizationService.CanUserDeleteCalendarCheckInAsync(UserId, checkInId))
				return Unauthorized();

			var existing = await _calendarService.GetCheckInByIdAsync(checkInId);
			var beforeJson = existing?.CloneJsonToString();

			var deleted = await _calendarService.DeleteCheckInAsync(checkInId, cancellationToken);

			result.Id = checkInId;
			result.PageSize = deleted ? 1 : 0;
			result.Status = deleted ? ResponseHelper.Success : ResponseHelper.NotFound;

			if (deleted)
			{
				var auditEvent = new AuditEvent();
				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.Type = AuditLogTypes.CalendarCheckInDeleted;
				auditEvent.Before = beforeJson;
				auditEvent.Successful = true;
				auditEvent.IpAddress = IpAddressHelper.GetRequestIP(Request, true);
				auditEvent.ServerName = Environment.MachineName;
				auditEvent.UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);
			}

			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		public static GetAllCalendarItemResultData ConvertCalendarItemData(CalendarItem item, Department department, string currentUserId, CalendarItemType type, List<PersonName> personnelNames)
		{
			var calendarItem = new GetAllCalendarItemResultData();
			calendarItem.CalendarItemId = item.CalendarItemId.ToString();
			calendarItem.Title = item.Title;

			// All-day and multi-day events are date-only concepts. The Kendo scheduler sends End as
			// an exclusive next-day midnight boundary (e.g. 3/5 all-day → End stored as 3/6 00:00 UTC).
			// Subtract one day so the API returns the actual inclusive end date to the client.
			// item.Start and item.End are stored as UTC; convert to the department's local timezone for display.
			if (item.IsAllDay || item.IsMultiDay)
			{
				var localStart = item.Start.TimeConverter(department);
				calendarItem.Start = localStart.Date;
				calendarItem.StartUtc = item.Start;

				var localEnd = item.End.TimeConverter(department);
				calendarItem.End = localEnd.TimeOfDay == TimeSpan.Zero
					? localEnd.Date.AddDays(-1)
					: localEnd.Date;
				calendarItem.EndUtc = item.End;
			}
			else
			{
				calendarItem.Start = item.Start.TimeConverter(department);
				calendarItem.StartUtc = item.Start;
				calendarItem.End = item.End.TimeConverter(department);
				calendarItem.EndUtc = item.End;
			}
			calendarItem.StartTimezone = item.StartTimezone;
			calendarItem.EndTimezone = item.EndTimezone;
			calendarItem.Description = item.Description;
			calendarItem.RecurrenceId = item.RecurrenceId;
			calendarItem.RecurrenceRule = item.RecurrenceRule;
			calendarItem.RecurrenceException = item.RecurrenceException;
			calendarItem.IsAllDay = item.IsAllDay;
			calendarItem.IsMultiDay = item.IsMultiDay;
			calendarItem.ItemType = item.ItemType;
			calendarItem.Location = item.Location;
			calendarItem.SignupType = item.SignupType;
			calendarItem.CheckInType = item.CheckInType;
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
