using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Services;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Resgrid.Services
{
	/// <summary>
	/// Generates RFC 5545 iCal content from Resgrid CalendarItems.
	/// Each materialized occurrence is emitted as its own VEVENT — no RRULE is used
	/// since the system pre-expands recurrences into individual database rows.
	/// </summary>
	public class CalendarExportService : ICalendarExportService
	{
		private readonly ICalendarService _calendarService;

		public CalendarExportService(ICalendarService calendarService)
		{
			_calendarService = calendarService;
		}

		/// <inheritdoc/>
		public async Task<string> GenerateICalForItemAsync(int calendarItemId)
		{
			var item = await _calendarService.GetCalendarItemByIdAsync(calendarItemId);
			if (item == null)
				return null;

			var calendar = CreateBaseCalendar();
			calendar.Events.Add(MapToCalendarEvent(item));

			return SerializeCalendar(calendar);
		}

		/// <inheritdoc/>
		public async Task<string> GenerateICalForDepartmentAsync(int departmentId)
		{
			var items = await _calendarService.GetAllCalendarItemsForDepartmentAsync(departmentId);

			var calendar = CreateBaseCalendar();

			if (items != null)
			{
				foreach (var item in items.OrderBy(x => x.Start))
					calendar.Events.Add(MapToCalendarEvent(item));
			}

			return SerializeCalendar(calendar);
		}

		// ── Helpers ──────────────────────────────────────────────────────────────────

		private static Calendar CreateBaseCalendar()
		{
			var calendar = new Calendar();
			// Note: Ical.Net v4 manages PRODID internally; CalendarConfig.ICalProductId is
			// retained for documentation and future use if the serializer is replaced.
			calendar.AddProperty("X-WR-CALNAME", "Resgrid Calendar");
			calendar.AddProperty("X-WR-TIMEZONE", "UTC");
			calendar.AddProperty("X-WR-CACHETIME", $"PT{CalendarConfig.ICalFeedCacheDurationMinutes}M");
			return calendar;
		}

		private static CalendarEvent MapToCalendarEvent(CalendarItem item)
		{
			var ev = new CalendarEvent
			{
				Uid = $"resgrid-cal-{item.CalendarItemId}@resgrid",
				Summary = item.Title ?? string.Empty,
				Description = StripHtml(item.Description),
				Location = item.Location,
				IsAllDay = item.IsAllDay
			};

			if (item.IsAllDay)
			{
				// iCal all-day events use DATE (not DATE-TIME).
				// DTEND is exclusive per RFC 5545 §3.6.1, so add one day.
				ev.DtStart = new CalDateTime(item.Start.Year, item.Start.Month, item.Start.Day);
				ev.DtStart.HasTime = false;

				var exclusiveEnd = item.End.Date.AddDays(1);
				ev.DtEnd = new CalDateTime(exclusiveEnd.Year, exclusiveEnd.Month, exclusiveEnd.Day);
				ev.DtEnd.HasTime = false;
			}
			else
			{
				ev.DtStart = new CalDateTime(item.Start, "UTC");
				ev.DtEnd = new CalDateTime(item.End, "UTC");
			}

			// Map reminder to VALARM.
			var reminderMinutes = item.GetMinutesForReminder();
			if (reminderMinutes >= 0)
			{
				var alarm = new Alarm
				{
					Action = AlarmAction.Display,
					Description = item.Title ?? "Reminder",
					Trigger = new Trigger(TimeSpan.FromMinutes(-reminderMinutes))
				};
				ev.Alarms.Add(alarm);
			}

			// Map attendees if loaded.
			if (item.Attendees != null)
			{
				foreach (var attendee in item.Attendees)
				{
					ev.Attendees.Add(new Attendee
					{
						Value = new Uri($"mailto:resgrid-user-{attendee.UserId}@resgrid.invalid"),
						CommonName = attendee.UserId
					});
				}
			}

			return ev;
		}

		private static string SerializeCalendar(Calendar calendar)
		{
			var serializer = new CalendarSerializer();
			return serializer.SerializeToString(calendar);
		}

		private static string StripHtml(string html)
		{
			if (string.IsNullOrWhiteSpace(html))
				return string.Empty;

			// Simple tag stripper — sufficient for DESCRIPTION content.
			return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]*>", string.Empty);
		}
	}
}




