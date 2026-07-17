using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Resgrid.Chatbot.Interfaces;
using Resgrid.Chatbot.Localization;
using Resgrid.Chatbot.Models;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;

namespace Resgrid.Chatbot.Handlers
{
	/// <summary>
	/// Answers "what's my schedule?" / "my schedule for 7/22" (intent <see cref="ChatbotIntentType.MySchedule"/>):
	/// the user's shifts (assigned or signed up) plus calendar events they RSVP'd to, for the requested day
	/// (default today) in the department's local time.
	/// </summary>
	public class MyScheduleActionHandler : IChatbotActionHandler
	{
		private readonly IShiftsService _shiftsService;
		private readonly ICalendarService _calendarService;
		private readonly IDepartmentsService _departmentsService;

		public MyScheduleActionHandler(
			IShiftsService shiftsService,
			ICalendarService calendarService,
			IDepartmentsService departmentsService)
		{
			_shiftsService = shiftsService;
			_calendarService = calendarService;
			_departmentsService = departmentsService;
		}

		public ChatbotIntentType IntentType => ChatbotIntentType.MySchedule;

		public async Task<ChatbotResponse> HandleAsync(ChatbotMessage message, ChatbotIntent intent, ChatbotSession session)
		{
			var culture = session.Culture;
			try
			{
				var department = await _departmentsService.GetDepartmentByIdAsync(session.DepartmentId);
				var nowLocal = DateTime.UtcNow.TimeConverter(department);

				intent.Parameters.TryGetValue("day", out var dayText);
				var targetDate = ParseDay(dayText, nowLocal);
				if (targetDate == null)
					return new ChatbotResponse { Text = ChatbotResources.Get("Sched_BadDate", culture), Processed = false };

				var dateLabel = targetDate.Value.ToString("ddd M/d", CultureInfo.InvariantCulture);
				var lines = new List<string>();

				// Shifts: shift days on the target date the user is assigned to (shift personnel) or has
				// signed up for.
				var shiftDays = await _shiftsService.GetShiftDaysForDayAsync(targetDate.Value, session.DepartmentId);
				if (shiftDays != null && shiftDays.Count > 0)
				{
					var assignedShiftIds = (await _shiftsService.GetShiftPersonsForUserAsync(session.UserId) ?? new List<ShiftPerson>())
						.Select(p => p.ShiftId)
						.ToHashSet();
					var signups = (await _shiftsService.GetShiftSignupsForUserAsync(session.UserId) ?? new List<ShiftSignup>())
						.Where(s => !s.Denied && s.ShiftDay.Date == targetDate.Value.Date)
						.ToList();

					foreach (var shiftDay in shiftDays.Where(d => d.Day.Date == targetDate.Value.Date))
					{
						var onThisDay = assignedShiftIds.Contains(shiftDay.ShiftId)
							|| signups.Any(s => s.ShiftId == shiftDay.ShiftId);
						if (!onThisDay)
							continue;

						var shift = shiftDay.Shift ?? await _shiftsService.GetShiftByIdAsync(shiftDay.ShiftId);
						var times = $"{shiftDay.Start:t} - {shiftDay.End:t}";
						lines.Add(ChatbotResources.Get("Sched_ShiftLine", culture, shift?.Name ?? $"Shift {shiftDay.ShiftId}", times));
					}
				}

				// Calendar: events on the target date the user RSVP'd to (yes/required/maybe — anything
				// but Not Attending). Events are stored UTC; comparison happens in department local time,
				// so the fetch window pads a day each side.
				var windowStartUtc = targetDate.Value.Date.AddDays(-1);
				var windowEndUtc = targetDate.Value.Date.AddDays(2);
				var events = await _calendarService.GetAllCalendarItemsForDepartmentInRangeAsync(session.DepartmentId, windowStartUtc, windowEndUtc);

				foreach (var item in (events ?? new List<CalendarItem>()).OrderBy(i => i.Start))
				{
					var startLocal = item.Start.TimeConverter(department);
					if (startLocal.Date != targetDate.Value.Date)
						continue;

					var attendee = await _calendarService.GetCalendarItemAttendeeByUserAsync(item.CalendarItemId, session.UserId);
					if (attendee == null || attendee.AttendeeType == (int)CalendarItemAttendeeTypes.NotAttending)
						continue;

					var timeText = item.IsAllDay ? ChatbotResources.Get("Sched_AllDay", culture) : startLocal.ToString("t");
					lines.Add(ChatbotResources.Get("Sched_EventLine", culture, timeText, item.Title?.Truncate(50)));
				}

				if (lines.Count == 0)
					return new ChatbotResponse { Text = ChatbotResources.Get("Sched_None", culture, dateLabel), Processed = true };

				var sb = new StringBuilder();
				sb.AppendLine(ChatbotResources.Get("Sched_Header", culture, dateLabel));
				sb.AppendLine("----------------------");
				foreach (var line in lines)
					sb.AppendLine(line);

				return new ChatbotResponse { Text = sb.ToString(), Processed = true };
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return new ChatbotResponse { Text = ChatbotResources.Get("Sched_Error", culture), Processed = false };
			}
		}

		// Accepts: empty (today), "today", "tomorrow", a weekday name (next occurrence, today included),
		// or a date ("7/22", "7/22/2026", "2026-07-22"). Returns null when unparseable.
		private static DateTime? ParseDay(string dayText, DateTime nowLocal)
		{
			if (string.IsNullOrWhiteSpace(dayText))
				return nowLocal.Date;

			var text = dayText.Trim().TrimEnd('?', '!', '.', ',').ToLowerInvariant();

			if (text == "today")
				return nowLocal.Date;
			if (text == "tomorrow")
				return nowLocal.Date.AddDays(1);

			foreach (DayOfWeek dow in Enum.GetValues(typeof(DayOfWeek)))
			{
				var name = dow.ToString().ToLowerInvariant();
				if (text == name || text == name.Substring(0, 3))
				{
					var daysAhead = ((int)dow - (int)nowLocal.DayOfWeek + 7) % 7;
					return nowLocal.Date.AddDays(daysAhead);
				}
			}

			if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
			{
				// "7/22" parses with the current year; a date months in the past most likely means next
				// year (people ask about upcoming days).
				if (parsed.Date < nowLocal.Date.AddMonths(-1) && !text.Any(char.IsLetter) && text.Count(c => c == '/') == 1)
					parsed = parsed.AddYears(1);
				return parsed.Date;
			}

			return null;
		}
	}
}
