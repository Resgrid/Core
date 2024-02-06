using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Twilio.TwiML.Voice;

namespace Resgrid.Services
{
	public class CalendarService : ICalendarService
	{
		private readonly ICalendarItemsRepository _calendarItemRepository;
		private readonly ICalendarItemTypeRepository _calendarItemTypeRepository;
		private readonly ICalendarItemAttendeeRepository _calendarItemAttendeeRepository;
		private readonly IDepartmentsService _departmentsService;
		private readonly ICommunicationService _communicationService;
		private readonly IUserProfileService _userProfileService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IDepartmentSettingsService _departmentSettingsService;

		public CalendarService(ICalendarItemsRepository calendarItemRepository, ICalendarItemTypeRepository calendarItemTypeRepository,
			ICalendarItemAttendeeRepository calendarItemAttendeeRepository, IDepartmentsService departmentsService, ICommunicationService communicationService,
			IUserProfileService userProfileService, IDepartmentGroupsService departmentGroupsService, IDepartmentSettingsService departmentSettingsService)
		{
			_calendarItemRepository = calendarItemRepository;
			_calendarItemTypeRepository = calendarItemTypeRepository;
			_calendarItemAttendeeRepository = calendarItemAttendeeRepository;
			_departmentsService = departmentsService;
			_communicationService = communicationService;
			_userProfileService = userProfileService;
			_departmentGroupsService = departmentGroupsService;
			_departmentSettingsService = departmentSettingsService;
		}

		public async Task<List<CalendarItem>> GetAllCalendarItemsForDepartmentAsync(int departmentId)
		{
			return (from ci in await _calendarItemRepository.GetAllCalendarItemsByDepartmentIdAsync(departmentId)
										where ci.IsV2Schedule
										select ci).ToList();
		}

		public async Task<List<CalendarItem>> GetAllCalendarItemsForDepartmentAsync(int departmentId, DateTime startDate)
		{
			return (from ci in await GetAllCalendarItemsForDepartmentAsync(departmentId)
				where ci.Start >= startDate
				select ci).ToList();
		}

		public async Task<List<CalendarItem>> GetAllCalendarItemsForDepartmentInRangeAsync(int departmentId, DateTime startDate, DateTime endDate)
		{
			return (from ci in await GetAllCalendarItemsForDepartmentAsync(departmentId)
				where ci.Start >= startDate && ci.End <= endDate
				select ci).ToList();
		}

		public async Task<List<CalendarItemType>> GetAllCalendarItemTypesForDepartmentAsync(int departmentId)
		{
			var items = await _calendarItemTypeRepository.GetAllByDepartmentIdAsync(departmentId);

			if (items != null && items.Any())
				return items.ToList();

			return new List<CalendarItemType>();
		}

		public async Task<List<CalendarItem>> GetUpcomingCalendarItemsAsync(int departmentId, DateTime start)
		{
			return await GetAllCalendarItemsForDepartmentInRangeAsync(departmentId, start, start.AddDays(7));
		}

		public async Task<CalendarItem> SaveCalendarItemAsync(CalendarItem calendarItem, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _calendarItemRepository.SaveOrUpdateAsync(calendarItem, cancellationToken);
		}

		public async Task<CalendarItem> GetCalendarItemByIdAsync(int calendarItemId)
		{
			return await _calendarItemRepository.GetCalendarItemByIdAsync(calendarItemId);
		}

		public async Task<CalendarItemAttendee> GetCalendarAttendeeByIdAsync(int calendarAttendeeId)
		{
			return await _calendarItemAttendeeRepository.GetByIdAsync(calendarAttendeeId);
		}

		public async Task<bool> DeleteCalendarItemByIdAsync(int calendarItemId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var item = await GetCalendarItemByIdAsync(calendarItemId);

			if (item != null)
				return await _calendarItemRepository.DeleteAsync(item, cancellationToken);

			return false;
		}

		public async Task<bool> DeleteCalendarAttendeeByIdAsync(int calendarAttendeeId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var item = await GetCalendarAttendeeByIdAsync(calendarAttendeeId);

			if (item != null)
				return await _calendarItemAttendeeRepository.DeleteAsync(item, cancellationToken);

			return false;
		}

		public async Task<CalendarItemType> SaveCalendarItemTypeAsync(CalendarItemType type, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _calendarItemTypeRepository.SaveOrUpdateAsync(type, cancellationToken);
		}

		public async Task<CalendarItemType> GetCalendarItemTypeByIdAsync(int calendarItemTypeId)
		{
			return await _calendarItemTypeRepository.GetByIdAsync(calendarItemTypeId);
		}

		public async Task<bool> DeleteCalendarItemTypeAsync(CalendarItemType type, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (type != null)
				return await _calendarItemTypeRepository.DeleteAsync(type, cancellationToken);

			return false;
		}

		public async Task<CalendarItem> AddNewCalendarItemAsync(CalendarItem item, string timeZone, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (item.CalendarItemId == 0) // We haven't saved yet, thus this isn't UTC
			{
				item.Start = DateTimeHelpers.ConvertToUtc(item.Start, timeZone);
				//item.Start = item.Start.ToUniversalTime();
				item.End = DateTimeHelpers.ConvertToUtc(item.End, timeZone);
				//item.End = item.End.ToUniversalTime();
			}

			item.IsV2Schedule = true;
			item.Description = StringHelpers.SanitizeHtmlInString(item.Description);

			var saved = await SaveCalendarItemAsync(item, cancellationToken);
			var recurrenceItems = await CreateRecurrenceCalendarItemsAsync(saved, DateTime.UtcNow);

			if (recurrenceItems != null && recurrenceItems.Count > 0)
			{
				foreach (var recurrenceItem in recurrenceItems)
				{
					await SaveCalendarItemAsync(recurrenceItem, cancellationToken);
				}
			}

			return item;
		}

		public async Task<CalendarItem> UpdateCalendarItemAsync(CalendarItem item, string timeZone, CancellationToken cancellationToken = default(CancellationToken))
		{
			var calendarItem = await GetCalendarItemByIdAsync(item.CalendarItemId);

			if (calendarItem == null)
				return null;

			calendarItem.Start = DateTimeHelpers.ConvertToUtc(item.Start, timeZone);
			calendarItem.End = DateTimeHelpers.ConvertToUtc(item.End, timeZone);

			calendarItem.RecurrenceId = item.RecurrenceId;
			calendarItem.Title = item.Title;
			calendarItem.IsV2Schedule = true;
			calendarItem.Reminder = item.Reminder;
			calendarItem.Location = item.Location;
			calendarItem.LockEditing = item.LockEditing;
			calendarItem.Entities = item.Entities;
			calendarItem.Description = StringHelpers.SanitizeHtmlInString(item.Description);
			calendarItem.RequiredAttendes = item.RequiredAttendes;
			calendarItem.OptionalAttendes = item.OptionalAttendes;
			calendarItem.CreatorUserId = item.CreatorUserId;
			calendarItem.IsAllDay = item.IsAllDay;
			calendarItem.ItemType = item.ItemType;
			calendarItem.SignupType = item.SignupType;
			calendarItem.Public = item.Public;
			calendarItem.StartTimezone = timeZone;
			calendarItem.EndTimezone = timeZone;

			var saved = await SaveCalendarItemAsync(calendarItem, cancellationToken);

			var calendarItems = await _calendarItemRepository.GetCalendarItemsByRecurrenceIdAsync(calendarItem.CalendarItemId);

			if (calendarItems != null && calendarItems.Any())
			{
				foreach (var calItem in calendarItems)
				{
					//calItem.Start = new DateTime(calItem.Start.Year, calItem.Start.Month, calItem.Start.Day, saved.Start.Hour, saved.Start.Minute, 0).ToUniversalTime();

					/* DateTime is stored as UTC in the database. So we need to do the following:
					 * 					 * 1. Convert the start time to the departments time zone
					 * 					 * 2. Adjust the date time with a NEW local date time with the update times from the edited item
					 * 					 * 3. Set the start time to the new UTC time
					 */
					var startDateLocal = DateTimeHelpers.GetLocalDateTime(calItem.Start, timeZone);
					var startDate = DateTimeHelpers.GetLocalDateTime(
										DateTimeHelpers.ConvertToUtc(
											new DateTime(startDateLocal.Year, startDateLocal.Month, startDateLocal.Day, item.Start.Hour, item.Start.Minute, 0), timeZone), timeZone);

					calItem.Start = DateTimeHelpers.ConvertToUtc(startDate, timeZone);

					//calItem.End = new DateTime(calItem.End.Year, calItem.End.Month, calItem.End.Day, saved.End.Hour, saved.End.Minute, 0).ToUniversalTime();

					var endDateLocal = DateTimeHelpers.GetLocalDateTime(calItem.End, timeZone);
					var endDate = DateTimeHelpers.GetLocalDateTime(
										DateTimeHelpers.ConvertToUtc(
											new DateTime(endDateLocal.Year, endDateLocal.Month, endDateLocal.Day, item.End.Hour, item.End.Minute, 0), timeZone), timeZone);

					calItem.End = DateTimeHelpers.ConvertToUtc(endDate, timeZone);

					calItem.RecurrenceId = saved.CalendarItemId.ToString();
					calItem.Title = saved.Title;
					calItem.IsV2Schedule = true;
					calItem.Reminder = saved.Reminder;
					calItem.Location = saved.Location;
					calItem.LockEditing = saved.LockEditing;
					calItem.Entities = saved.Entities;
					calItem.Description = saved.Description;
					calItem.RequiredAttendes = saved.RequiredAttendes;
					calItem.OptionalAttendes = saved.OptionalAttendes;
					calItem.CreatorUserId = saved.CreatorUserId;
					calItem.IsAllDay = saved.IsAllDay;
					calItem.ItemType = saved.ItemType;
					calItem.SignupType = saved.SignupType;
					calItem.Public = saved.Public;
					calendarItem.StartTimezone = timeZone;
					calendarItem.EndTimezone = timeZone;

					var saved2 = await SaveCalendarItemAsync(calItem, cancellationToken);
				}
			}

			return calendarItem;
		}

		public async Task<List<CalendarItem>> GetAllCalendarItemRecurrencesAsync(int calendarItemId)
		{
			var items = await _calendarItemRepository.GetCalendarItemsByRecurrenceIdAsync(calendarItemId);

			if (items != null && items.Any())
				return items.ToList();

			return new List<CalendarItem>();
		}

		public async Task<bool> DeleteCalendarItemAndRecurrences(int calendarItemId, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _calendarItemRepository.DeleteCalendarItemAndRecurrencesAsync(calendarItemId, cancellationToken);
		}

		public async Task<List<CalendarItem>> CreateRecurrenceCalendarItemsAsync(CalendarItem item, DateTime start)
		{
			var calendarItems = new List<CalendarItem>();

			if (!item.RecurrenceEnd.HasValue || item.RecurrenceEnd > DateTime.UtcNow && item.Start >= start)
			{
				var department = await _departmentsService.GetDepartmentByIdAsync(item.DepartmentId, false);

				/* We need to convert the start time to the departments time zone so we can properly calculate the recurrences */
				DateTime startTimeConverted = item.Start.TimeConverter(department);

				var length = item.GetDifferenceBetweenStartAndEnd();

				if (item.RecurrenceType == (int)RecurrenceTypes.Weekly)
				{ // Day of week (i.e. every Tuesday and Thursday)
					DateTime weekWorkingDate = startTimeConverted;
					int weekProcssed = 1;

					var nextWeek = GetNextWeekValues(item, weekWorkingDate);
					while (weekProcssed <= 52)
					{
						foreach (var day in nextWeek)
						{
							if (day != null)
							{
								if (calendarItems.All(x => x.Start.Date != day.Value.Date))
								{
									/* Yea seems strange, but we need to try and retain the original time zone of the start time
									 * so the time is correct when we convert it back to UTC */
									var startDate = DateTimeHelpers.GetLocalDateTime(
										DateTimeHelpers.ConvertToUtc(
											new DateTime(day.Value.Year, day.Value.Month, day.Value.Day, startTimeConverted.Hour, startTimeConverted.Minute, 0), department.TimeZone), department.TimeZone);

									var endDate = startDate.Add(length);

									if (!item.RecurrenceEnd.HasValue || DateTimeHelpers.ConvertToUtc(startDate, department.TimeZone) < item.RecurrenceEnd)
										calendarItems.Add(item.CreateRecurranceItem(startDate, endDate, department.TimeZone));
								}
							}
						}

						weekWorkingDate = weekWorkingDate.AddDays(7);
						nextWeek = GetNextWeekValues(item, weekWorkingDate);
						weekProcssed++;
					}
				}
				else if (item.RecurrenceType == (int)RecurrenceTypes.Monthly && item.RepeatOnWeek == 0)
				{ // This is a repeat on every 15th of the month
					DateTime monthWorkingDate = startTimeConverted;
					int monthProcessed = 1;

					while (monthProcessed <= 12)
					{
						var monthDate = monthWorkingDate.AddMonths(monthProcessed);

						var startDate = DateTimeHelpers.GetLocalDateTime(
							DateTimeHelpers.ConvertToUtc(new DateTime(monthDate.Year, monthDate.Month, item.RepeatOnDay,
								startTimeConverted.Hour, startTimeConverted.Minute, 0), department.TimeZone), department.TimeZone);

						var endDate = startDate.Add(length);

						if (!item.RecurrenceEnd.HasValue || DateTimeHelpers.ConvertToUtc(startDate, department.TimeZone) < item.RecurrenceEnd)
							calendarItems.Add(item.CreateRecurranceItem(startDate, endDate, department.TimeZone));

						monthProcessed++;
					}
				}
				else if (item.RecurrenceType == (int)RecurrenceTypes.Monthly && item.RepeatOnWeek > 0)
				{ // This is a repeat on every nth friday of the month
					DateTime monthWorkingDate = startTimeConverted;
					int monthProcessed = 1;

					while (monthProcessed <= 12)
					{
						var monthDate = monthWorkingDate.AddMonths(monthProcessed);

						var startDateDay = DateTimeHelpers.FindDay(monthDate.Year, monthDate.Month, (DayOfWeek)item.RepeatOnDay,
							item.RepeatOnWeek);

						var startDate = DateTimeHelpers.GetLocalDateTime(DateTimeHelpers.ConvertToUtc(new DateTime(monthDate.Year, monthDate.Month, startDateDay,
							startTimeConverted.Hour, startTimeConverted.Minute, 0), department.TimeZone), department.TimeZone);

						var endDate = startDate.Add(length);

						if (!item.RecurrenceEnd.HasValue || DateTimeHelpers.ConvertToUtc(startDate, department.TimeZone) < item.RecurrenceEnd)
							calendarItems.Add(item.CreateRecurranceItem(startDate, endDate, department.TimeZone));

						monthProcessed++;
					}
				}
				else if (item.RecurrenceType == (int)RecurrenceTypes.Yearly)
				{
					DateTime yearlyWorkingDate = startTimeConverted;
					yearlyWorkingDate = yearlyWorkingDate.AddYears(1);

					var startDate = DateTimeHelpers.GetLocalDateTime(DateTimeHelpers.ConvertToUtc(new DateTime(yearlyWorkingDate.Year, item.Start.Month, item.Start.Day,
						startTimeConverted.Hour, startTimeConverted.Minute, 0), department.TimeZone), department.TimeZone);

					var endDate = startDate.Add(length);

					if (!item.RecurrenceEnd.HasValue || DateTimeHelpers.ConvertToUtc(startDate, department.TimeZone) < item.RecurrenceEnd)
						calendarItems.Add(item.CreateRecurranceItem(startDate, endDate, department.TimeZone));
				}
			}

			return calendarItems;
		}

		public DateTime?[] GetNextWeekValues(CalendarItem item, DateTime currentDate)
		{
			DateTime?[] dates = new DateTime?[7];

			if (item.Sunday)
				dates[0] = DateTimeHelpers.GetNextWeekday(currentDate, DayOfWeek.Sunday);
			else
				dates[0] = null;

			if (item.Monday)
				dates[1] = DateTimeHelpers.GetNextWeekday(currentDate, DayOfWeek.Monday);
			else
				dates[1] = null;

			if (item.Tuesday)
				dates[2] = DateTimeHelpers.GetNextWeekday(currentDate, DayOfWeek.Tuesday);
			else
				dates[2] = null;

			if (item.Wednesday)
				dates[3] = DateTimeHelpers.GetNextWeekday(currentDate, DayOfWeek.Wednesday);
			else
				dates[3] = null;

			if (item.Thursday)
				dates[4] = DateTimeHelpers.GetNextWeekday(currentDate, DayOfWeek.Thursday);
			else
				dates[4] = null;

			if (item.Friday)
				dates[5] = DateTimeHelpers.GetNextWeekday(currentDate, DayOfWeek.Friday);
			else
				dates[5] = null;

			if (item.Saturday)
				dates[6] = DateTimeHelpers.GetNextWeekday(currentDate, DayOfWeek.Saturday);
			else
				dates[6] = null;

			return dates;
		}

		public async Task<List<CalendarItem>> GetCalendarItemsForDepartmentInRangeAsync(int departmentId, DateTime start)
		{
			var calendarItems = new List<CalendarItem>();

			var items = await GetAllCalendarItemsForDepartmentAsync(departmentId, start);
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
			//var currentDateTime = DateTime.UtcNow.FormatForDepartment()

			foreach (var item in items)
			{
				if (item.RecurrenceType == (int)RecurrenceTypes.Weekly)
				{

				}
			}

			return calendarItems;
		}

		public async Task<CalendarItemAttendee> SignupForEvent(int calendarEventItemId, string userId, string note, int attendeeType, CancellationToken cancellationToken = default(CancellationToken))
		{
			CalendarItemAttendee attendee = await GetCalendarItemAttendeeByUserAsync(calendarEventItemId, userId);

			if (attendee == null)
				attendee = new CalendarItemAttendee();

			attendee.CalendarItemId = calendarEventItemId;
			attendee.UserId = userId;
			attendee.Note = note;
			attendee.AttendeeType = attendeeType;
			attendee.Timestamp = DateTime.UtcNow;

			return await _calendarItemAttendeeRepository.SaveOrUpdateAsync(attendee, cancellationToken);
		}

		public async Task<CalendarItemAttendee> GetCalendarItemAttendeeByUserAsync(int calendarEventItemId, string userId)
		{
			return await _calendarItemAttendeeRepository.GetCalendarItemAttendeeByUserAsync(calendarEventItemId, userId);
		}

		public async Task<bool> NotifyNewCalendarItemAsync(CalendarItem calendarItem)
		{
			if (calendarItem != null && !String.IsNullOrWhiteSpace(calendarItem.Entities))
			{
				var items = calendarItem.Entities.Split(char.Parse(","));

				var message = String.Empty;
				var title = String.Empty;
				var profiles = await _userProfileService.GetAllProfilesForDepartmentAsync(calendarItem.DepartmentId);
				var departmentNumber = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(calendarItem.DepartmentId);
				var department = await _departmentsService.GetDepartmentByIdAsync(calendarItem.DepartmentId, false);

				var adjustedDateTime = calendarItem.Start.TimeConverter(department);
				title = $"New: {calendarItem.Title}";

				if (String.IsNullOrWhiteSpace(calendarItem.Location))
					message = $"on {adjustedDateTime.ToShortDateString()} - {adjustedDateTime.ToShortTimeString()}";
				else
					message = $"on {adjustedDateTime.ToShortDateString()} - {adjustedDateTime.ToShortTimeString()} at {calendarItem.Location}";

				if (ConfigHelper.CanTransmit(department.DepartmentId))
				{
					if (items.Any(x => x.StartsWith("D:")))
					{
						// Notify the entire department
						foreach (var profile in profiles)
						{
							await _communicationService.SendCalendarAsync(profile.Key, calendarItem.DepartmentId, message, departmentNumber, title, profile.Value);
						}
					}
					else
					{
						var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(calendarItem.DepartmentId);
						foreach (var val in items)
						{
							int groupId = 0;
							if (int.TryParse(val.Replace("G:", ""), out groupId))
							{
								var group = groups.FirstOrDefault(x => x.DepartmentGroupId == groupId);

								if (group != null)
								{
									foreach (var member in group.Members)
									{
										if (profiles.ContainsKey(member.UserId))
											await _communicationService.SendNotificationAsync(member.UserId, calendarItem.DepartmentId, message, departmentNumber, title, profiles[member.UserId]);
										else
											await _communicationService.SendNotificationAsync(member.UserId, calendarItem.DepartmentId, message, departmentNumber, title, null);
									}
								}
							}
						}
					}
				}


				return true;
			}

			return false;
		}

		public async Task<List<CalendarItem>> GetCalendarItemsToNotifyAsync(DateTime timestamp)
		{
			List<CalendarItem> itemsToNotify = new List<CalendarItem>();
			var calendarItems = await _calendarItemRepository.GetAllCalendarItemsToNotifyAsync(timestamp);

			foreach (var calendarItem in calendarItems)
			{
				//var convertedTime = DateTimeHelpers.ConvertToUtc(calendarItem.Start, calendarItem.StartTimezone);
				int minutesToAdjust = calendarItem.GetMinutesForReminder();
				var convertedTime = calendarItem.Start.AddMinutes(-minutesToAdjust);

				//var localizedDate = TimeConverterHelper.TimeConverter(currentTime, training.Department);
				//var setToNotify = new DateTime(localizedDate.Year, localizedDate.Month, localizedDate.Day, 10, 0, 0, 0);

				if (timestamp == convertedTime.Within(TimeSpan.FromMinutes(30)))
					itemsToNotify.Add(calendarItem);
			}


			return itemsToNotify;
		}

		public async Task<bool> MarkAsNotifiedAsync(int calendarItemId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var calendarItem = await GetCalendarItemByIdAsync(calendarItemId);

			if (calendarItem != null)
			{
				calendarItem.ReminderSent = true;
				await SaveCalendarItemAsync(calendarItem, cancellationToken);
				return true;
			}

			return false;
		}

		public int NotificationTypeToMinutes(int notificationType)
		{
			switch (notificationType)
			{
				case 1:
					return 0;
				case 2:
					return 5;
				case 3:
					return 10;
				case 4:
					return 15;
				case 5:
					return 30;
				case 6:
					return 60;
				case 7:
					return 120;
				case 8:
					return 180;
				case 9:
					return 240;
				case 10:
					return 300;
				case 11:
					return 360;
				case 12:
					return 420;
				case 13:
					return 480;
				case 14:
					return 540;
				case 15:
					return 600;
				case 16:
					return 660;
				case 17:
					return 720;
				case 18:
					return 1080;
				case 19:
					return 1440;
				case 20:
					return 2880;
				case 21:
					return 4320;
				case 22:
					return 5760;
				case 23:
					return 10080;
				case 24:
					return 20160;
			}

			return -1;
		}
	}
}
