using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Services
{
	public class CalendarService : ICalendarService
	{
		private readonly ICalendarItemsRepository _calendarItemRepository;
		private readonly IGenericDataRepository<CalendarItemType> _calendarItemTypeRepository;
		private readonly IGenericDataRepository<CalendarItemAttendee> _calendarItemAttendeeRepository;
		private readonly IDepartmentsService _departmentsService;

		public CalendarService(ICalendarItemsRepository calendarItemRepository, IGenericDataRepository<CalendarItemType> calendarItemTypeRepository,
			IGenericDataRepository<CalendarItemAttendee> calendarItemAttendeeRepository, IDepartmentsService departmentsService)
		{
			_calendarItemRepository = calendarItemRepository;
			_calendarItemTypeRepository = calendarItemTypeRepository;
			_calendarItemAttendeeRepository = calendarItemAttendeeRepository;
			_departmentsService = departmentsService;
		}

		public List<CalendarItem> GetAllCalendarItemsForDepartment(int departmentId)
		{
			return _calendarItemRepository.GetAll().Where(x => x.DepartmentId == departmentId && x.IsV2Schedule == true).ToList();
		}

		public List<CalendarItem> GetAllCalendarItemsForDepartment(int departmentId, DateTime startDate)
		{
			return _calendarItemRepository.GetAll().Where(x => x.DepartmentId == departmentId && x.Start >= startDate && x.IsV2Schedule == true).ToList();
		}

		public List<CalendarItem> GetAllCalendarItemsForDepartmentInRange(int departmentId, DateTime startDate, DateTime endDate)
		{
			return _calendarItemRepository.GetAll().Where(x => x.DepartmentId == departmentId && x.Start >= startDate && x.End <= endDate && x.IsV2Schedule == true).ToList();
		}

		public List<CalendarItemType> GetAllCalendarItemTypesForDepartment(int departmentId)
		{
			return _calendarItemTypeRepository.GetAll().Where(x => x.DepartmentId == departmentId).ToList();
		}

		public List<CalendarItem> GetUpcomingCalendarItems(int departmentId, DateTime start)
		{
			var endDate = start.AddDays(7);
			return _calendarItemRepository.GetAll().Where(x => x.DepartmentId == departmentId && x.Start >= start && x.Start <= endDate && x.IsV2Schedule == true).ToList();
		}

		public CalendarItem SaveCalendarItem(CalendarItem calendarItem)
		{
			_calendarItemRepository.SaveOrUpdate(calendarItem);

			return calendarItem;
		}

		public CalendarItem GetCalendarItemById(int calendarItemId)
		{
			return _calendarItemRepository.GetAll().FirstOrDefault(x => x.CalendarItemId == calendarItemId);
		}

		public CalendarItemAttendee GetCalendarAttendeeById(int calendarAttendeeId)
		{
			return _calendarItemAttendeeRepository.GetAll().FirstOrDefault(x => x.CalendarItemAttendeeId == calendarAttendeeId);
		}

		public void DeleteCalendarItemById(int calendarItemId)
		{
			var item = GetCalendarItemById(calendarItemId);

			if (item != null)
				_calendarItemRepository.DeleteOnSubmit(item);
		}

		public void DeleteCalendarAttendeeById(int calendarAttendeeId)
		{
			var item = GetCalendarAttendeeById(calendarAttendeeId);

			if (item != null)
				_calendarItemAttendeeRepository.DeleteOnSubmit(item);
		}

		public CalendarItemType SaveCalendarItemType(CalendarItemType type)
		{
			_calendarItemTypeRepository.SaveOrUpdate(type);

			return type;
		}

		public CalendarItemType GetCalendarItemTypeById(int calendarItemTypeId)
		{
			return _calendarItemTypeRepository.GetAll().FirstOrDefault(x => x.CalendarItemTypeId == calendarItemTypeId);
		}

		public void DeleteCalendarItemType(CalendarItemType type)
		{
			if (type != null)
				_calendarItemTypeRepository.DeleteOnSubmit(type);
		}

		public List<CalendarItem> GetAllV2CalendarItemsForDepartment(int departmentId, DateTime startDate)
		{
			return _calendarItemRepository.GetAllV2CalendarItemsForDepartment(departmentId, startDate);
		}

		public CalendarItem AddNewV2CalendarItem(CalendarItem item, string timeZone)
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

			_calendarItemRepository.SaveOrUpdate(item);
			var recurrenceItems = CreateRecurrenceCalendarItems(item, DateTime.UtcNow);

			if (recurrenceItems != null && recurrenceItems.Count > 0)
			{
				_calendarItemRepository.SaveOrUpdateAll(recurrenceItems);
			}

			return item;
		}

		public CalendarItem UpdateV2CalendarItem(CalendarItem item, string timeZone)
		{
			var calendarItem = _calendarItemRepository.GetAll().FirstOrDefault(x => x.CalendarItemId == item.CalendarItemId);

			if (calendarItem == null)
				return null;

			//calendarItem.Start = item.Start;
			//calendarItem.End = item.End;

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

			_calendarItemRepository.SaveOrUpdate(calendarItem);

			var calendarItems = _calendarItemRepository.GetAll().Where(x => x.RecurrenceId == calendarItem.CalendarItemId.ToString()).ToList();

			if (calendarItems.Any())
			{
				foreach (var calItem in calendarItems)
				{
					calItem.Start = item.Start;
					calItem.End = item.End;
					calItem.RecurrenceId = item.CalendarItemId.ToString();
					calItem.Title = item.Title;
					calItem.IsV2Schedule = true;
					calItem.Reminder = item.Reminder;
					calItem.Location = item.Location;
					calItem.LockEditing = item.LockEditing;
					calItem.Entities = item.Entities;
					calItem.Description = item.Description;
					calItem.RequiredAttendes = item.RequiredAttendes;
					calItem.OptionalAttendes = item.OptionalAttendes;
					calItem.CreatorUserId = item.CreatorUserId;
					calItem.IsAllDay = item.IsAllDay;
					calItem.ItemType = item.ItemType;
					calItem.SignupType = item.SignupType;
					calItem.Public = item.Public;

					_calendarItemRepository.SaveOrUpdate(calItem);
				}
			}

			return calendarItem;
		}

		public List<CalendarItem> GetAllV2CalendarItemRecurrences(int calendarItemId)
		{
			return _calendarItemRepository.GetAllV2CalendarItemRecurrences(calendarItemId.ToString());
		}

		public void DeleteCalendarItemAndRecurrences(int calendarItemId)
		{
			_calendarItemRepository.DeleteCalendarItemAndRecurrences(calendarItemId);
		}

		public List<CalendarItem> CreateRecurrenceCalendarItems(CalendarItem item, DateTime start)
		{
			var calendarItems = new List<CalendarItem>();

			if (!item.RecurrenceEnd.HasValue || item.RecurrenceEnd > DateTime.UtcNow && item.Start >= start)
			{
				var department = _departmentsService.GetDepartmentById(item.DepartmentId, false);
				var currentTime = item.Start.TimeConverter(department);
				DateTime startTimeConverted = item.Start.TimeConverter(department);

				var length = item.GetDifferenceBetweenStartAndEnd();

				if (item.RecurrenceType == (int)RecurrenceTypes.Weekly)
				{
					DateTime weekWorkingDate = currentTime;
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
									var startDate = DateTimeHelpers.GetLocalDateTime(new DateTime(day.Value.Year, day.Value.Month, day.Value.Day,
										startTimeConverted.Hour, startTimeConverted.Minute, startTimeConverted.Second), department.TimeZone);

									var endDate = startDate.Add(length);

									calendarItems.Add(item.CreateRecurranceItem(startDate, endDate, department.TimeZone));
								}
							}
						}

						weekWorkingDate = weekWorkingDate.AddDays(7);
						nextWeek = GetNextWeekValues(item, weekWorkingDate);
						weekProcssed++;
					}
				}
				else if (item.RecurrenceType == (int)RecurrenceTypes.Monthly && item.RepeatOnDay == 0)
				{ // This is a repeat on every 15th of the month
					DateTime monthWorkingDate = currentTime;
					int monthProcessed = 1;

					while (monthProcessed <= 12)
					{
						var monthDate = monthWorkingDate.AddMonths(monthProcessed);

						var startDate = DateTimeHelpers.GetLocalDateTime(new DateTime(monthDate.Year, monthDate.Month, item.RepeatOnMonth,
								startTimeConverted.Hour, startTimeConverted.Minute, startTimeConverted.Second), department.TimeZone);

						var endDate = startDate.Add(length);

						calendarItems.Add(item.CreateRecurranceItem(startDate, endDate, department.TimeZone));

						monthProcessed++;
					}
				}
				else if (item.RecurrenceType == (int)RecurrenceTypes.Monthly && item.RepeatOnDay > 0)
				{ // This is a repeat on every nth day of the month
					DateTime monthWorkingDate = currentTime;
					int monthProcessed = 1;

					while (monthProcessed <= 12)
					{
						var monthDate = monthWorkingDate.AddMonths(monthProcessed);

						var startDateDay = DateTimeHelpers.FindDay(monthDate.Year, monthDate.Month, (DayOfWeek)item.RepeatOnDay,
							item.RepeatOnMonth);

						var startDate = DateTimeHelpers.GetLocalDateTime(new DateTime(monthDate.Year, monthDate.Month, startDateDay,
							startTimeConverted.Hour, startTimeConverted.Minute, startTimeConverted.Second), department.TimeZone);

						var endDate = startDate.Add(length);

						calendarItems.Add(item.CreateRecurranceItem(startDate, endDate, department.TimeZone));

						monthProcessed++;
					}
				}
				else if (item.RecurrenceType == (int)RecurrenceTypes.Yearly)
				{
					DateTime yearlyWorkingDate = currentTime;
					yearlyWorkingDate = yearlyWorkingDate.AddYears(1);

					var startDate = DateTimeHelpers.GetLocalDateTime(new DateTime(yearlyWorkingDate.Year, startTimeConverted.Month, startTimeConverted.Day,
						startTimeConverted.Hour, startTimeConverted.Minute, startTimeConverted.Second), department.TimeZone);

					var endDate = startDate.Add(length);

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

		public List<CalendarItem> GetCalendarItemsForDepartmentInRange(int departmentId, DateTime start)
		{
			var calendarItems = new List<CalendarItem>();

			var items = _calendarItemRepository.GetAllV2CalendarItemsForDepartment(departmentId, start);
			var department = _departmentsService.GetDepartmentById(departmentId, false);
			//var currentDateTime = DateTime.UtcNow.FormatForDepartment()

			foreach (var item in items)
			{
				if (item.RecurrenceType == (int)RecurrenceTypes.Weekly)
				{

				}
			}

			return calendarItems;
		}

		public void SignupForEvent(int calendarEventItemId, string userId, string note, int attendeeType)
		{
			CalendarItemAttendee attendee = GetCalendarItemAttendeeByUser(calendarEventItemId, userId);

			if (attendee == null)
				attendee = new CalendarItemAttendee();

			attendee.CalendarItemId = calendarEventItemId;
			attendee.UserId = userId;
			attendee.Note = note;
			attendee.AttendeeType = attendeeType;
			attendee.Timestamp = DateTime.UtcNow;

			_calendarItemAttendeeRepository.SaveOrUpdate(attendee);
		}

		public CalendarItemAttendee GetCalendarItemAttendeeByUser(int calendarEventItemId, string userId)
		{
			return _calendarItemAttendeeRepository.GetAll().FirstOrDefault(x => x.CalendarItemId == calendarEventItemId && x.UserId == userId);
		}

		public List<CalendarItem> GetCalendarItemsToNotify(DateTime timestamp)
		{
			List<CalendarItem> itemsToNotify = new List<CalendarItem>();
			var calendarItems = _calendarItemRepository.GetAllCalendarItemsToNotify();

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

		public List<CalendarItem> GetV2CalendarItemsToNotify(DateTime timestamp)
		{
			List<CalendarItem> itemsToNotify = new List<CalendarItem>();
			var calendarItems = _calendarItemRepository.GetAllV2CalendarItemsToNotify(timestamp);

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

		public void MarkAsNotified(int calendarItemId)
		{
			var calendarItem = GetCalendarItemById(calendarItemId);

			if (calendarItem != null)
			{
				calendarItem.ReminderSent = true;
				SaveCalendarItem(calendarItem);
			}
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
