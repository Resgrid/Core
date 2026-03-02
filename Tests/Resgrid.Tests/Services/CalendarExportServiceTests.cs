using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Framework.Testing;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	namespace CalendarExportServiceTests
	{
		public class with_the_calendar_export_service : TestBase
		{
			protected ICalendarExportService _calendarExportService;
			protected readonly Mock<ICalendarService> _calendarServiceMock;

			protected with_the_calendar_export_service()
			{
				_calendarServiceMock = new Mock<ICalendarService>();
				_calendarExportService = new CalendarExportService(_calendarServiceMock.Object);

				// Ensure config uses a known value for assertions.
				CalendarConfig.ICalProductId = "-//Resgrid//Calendar//EN";
				CalendarConfig.ICalFeedCacheDurationMinutes = 15;
			}

			protected static CalendarItem MakeSingleDayItem(int id = 1, bool allDay = false, string location = "Station 1", int reminder = 2)
			{
				return new CalendarItem
				{
					CalendarItemId = id,
					DepartmentId = 999,
					Title = $"Test Event {id}",
					Description = "Event description",
					Location = location,
					IsAllDay = allDay,
					Reminder = reminder,
					Start = allDay
						? new DateTime(2024, 6, 10, 0, 0, 0, DateTimeKind.Utc)
						: new DateTime(2024, 6, 10, 14, 0, 0, DateTimeKind.Utc),
					End = allDay
						? new DateTime(2024, 6, 10, 23, 59, 59, DateTimeKind.Utc)
						: new DateTime(2024, 6, 10, 16, 0, 0, DateTimeKind.Utc)
				};
			}

			protected static CalendarItem MakeMultiDayAllDayItem(int id = 2)
			{
				return new CalendarItem
				{
					CalendarItemId = id,
					DepartmentId = 999,
					Title = $"Multi-Day Event {id}",
					IsAllDay = true,
					Start = new DateTime(2024, 6, 10, 0, 0, 0, DateTimeKind.Utc),
					End = new DateTime(2024, 6, 12, 23, 59, 59, DateTimeKind.Utc)
				};
			}
		}

		[TestFixture]
		public class when_generating_ical_for_single_item : with_the_calendar_export_service
		{
			[Test]
			public async Task should_produce_valid_ics_output()
			{
				var item = MakeSingleDayItem(1);
				_calendarServiceMock.Setup(x => x.GetCalendarItemByIdAsync(1)).ReturnsAsync(item);

				var result = await _calendarExportService.GenerateICalForItemAsync(1);

				result.Should().NotBeNullOrWhiteSpace();
				result.Should().Contain("BEGIN:VCALENDAR");
				result.Should().Contain("BEGIN:VEVENT");
				result.Should().Contain("END:VEVENT");
				result.Should().Contain("END:VCALENDAR");
			}

			[Test]
			public async Task should_include_summary_matching_title()
			{
				var item = MakeSingleDayItem(1);
				_calendarServiceMock.Setup(x => x.GetCalendarItemByIdAsync(1)).ReturnsAsync(item);

				var result = await _calendarExportService.GenerateICalForItemAsync(1);

				result.Should().Contain("SUMMARY:Test Event 1");
			}

			[Test]
			public async Task should_include_location()
			{
				var item = MakeSingleDayItem(1, location: "Fire Station 7");
				_calendarServiceMock.Setup(x => x.GetCalendarItemByIdAsync(1)).ReturnsAsync(item);

				var result = await _calendarExportService.GenerateICalForItemAsync(1);

				result.Should().Contain("LOCATION:Fire Station 7");
			}

			[Test]
			public async Task should_set_all_day_event_with_date_only_format()
			{
				var item = MakeSingleDayItem(1, allDay: true);
				_calendarServiceMock.Setup(x => x.GetCalendarItemByIdAsync(1)).ReturnsAsync(item);

				var result = await _calendarExportService.GenerateICalForItemAsync(1);

				// iCal all-day events use VALUE=DATE format (no time component).
				result.Should().Contain("VALUE=DATE");
				// Date-only format: YYYYMMDD — no T separator.
				result.Should().Contain("DTSTART;VALUE=DATE:20240610");
			}

			[Test]
			public async Task should_set_correct_exclusive_dtend_for_multi_day_all_day_event()
			{
				var item = MakeMultiDayAllDayItem(2);
				_calendarServiceMock.Setup(x => x.GetCalendarItemByIdAsync(2)).ReturnsAsync(item);

				var result = await _calendarExportService.GenerateICalForItemAsync(2);

				// DTEND for multi-day all-day must be the day AFTER the last day (exclusive per RFC 5545).
				// End is June 12, so DTEND should be June 13.
				result.Should().Contain("DTEND;VALUE=DATE:20240613");
			}

			[Test]
			public async Task should_include_valarm_for_reminder()
			{
				// Reminder = 2 maps to "30 minutes before" via GetMinutesForReminder.
				var item = MakeSingleDayItem(1, reminder: 2);
				_calendarServiceMock.Setup(x => x.GetCalendarItemByIdAsync(1)).ReturnsAsync(item);

				var result = await _calendarExportService.GenerateICalForItemAsync(1);

				result.Should().Contain("BEGIN:VALARM");
				result.Should().Contain("END:VALARM");
				// Trigger should be negative (before event).
				result.Should().Contain("TRIGGER:-PT");
			}

			[Test]
			public async Task should_return_null_for_missing_item()
			{
				_calendarServiceMock.Setup(x => x.GetCalendarItemByIdAsync(999)).ReturnsAsync((CalendarItem)null);

				var result = await _calendarExportService.GenerateICalForItemAsync(999);

				result.Should().BeNull();
			}
		}

		[TestFixture]
		public class when_generating_ical_for_department : with_the_calendar_export_service
		{
			[Test]
			public async Task should_include_all_items_as_separate_vevents()
			{
				var items = new List<CalendarItem>
				{
					MakeSingleDayItem(1),
					MakeSingleDayItem(2),
					MakeSingleDayItem(3)
				};

				_calendarServiceMock.Setup(x => x.GetAllCalendarItemsForDepartmentAsync(999)).ReturnsAsync(items);

				var result = await _calendarExportService.GenerateICalForDepartmentAsync(999);

				result.Should().NotBeNullOrWhiteSpace();

				// Count occurrences of BEGIN:VEVENT — must equal number of items.
				var count = CountOccurrences(result, "BEGIN:VEVENT");
				count.Should().Be(3);
			}

			[Test]
			public async Task should_not_contain_rrule_for_any_event()
			{
				var items = new List<CalendarItem> { MakeSingleDayItem(1), MakeSingleDayItem(2) };
				_calendarServiceMock.Setup(x => x.GetAllCalendarItemsForDepartmentAsync(999)).ReturnsAsync(items);

				var result = await _calendarExportService.GenerateICalForDepartmentAsync(999);

				// Each occurrence must be its own VEVENT — no RRULE.
				result.Should().NotContain("RRULE:");
			}

			[Test]
			public async Task should_produce_single_vcalendar_wrapper()
			{
				var items = new List<CalendarItem> { MakeSingleDayItem(1), MakeSingleDayItem(2) };
				_calendarServiceMock.Setup(x => x.GetAllCalendarItemsForDepartmentAsync(999)).ReturnsAsync(items);

				var result = await _calendarExportService.GenerateICalForDepartmentAsync(999);

				CountOccurrences(result, "BEGIN:VCALENDAR").Should().Be(1);
				CountOccurrences(result, "END:VCALENDAR").Should().Be(1);
			}

			[Test]
			public async Task should_set_prodid_from_config()
			{
				_calendarServiceMock.Setup(x => x.GetAllCalendarItemsForDepartmentAsync(999))
					.ReturnsAsync(new List<CalendarItem>());

				var result = await _calendarExportService.GenerateICalForDepartmentAsync(999);

				// Ical.Net v4 always serializes a PRODID line; verify one is present.
				result.Should().Contain("PRODID:");
				// Verify the output is a valid VCALENDAR.
				result.Should().Contain("BEGIN:VCALENDAR");
				result.Should().Contain("VERSION:2.0");
			}

			[Test]
			public async Task should_produce_empty_but_valid_calendar_for_no_items()
			{
				_calendarServiceMock.Setup(x => x.GetAllCalendarItemsForDepartmentAsync(999))
					.ReturnsAsync(new List<CalendarItem>());

				var result = await _calendarExportService.GenerateICalForDepartmentAsync(999);

				result.Should().Contain("BEGIN:VCALENDAR");
				result.Should().Contain("END:VCALENDAR");
				result.Should().NotContain("BEGIN:VEVENT");
			}

			// ── Helper ────────────────────────────────────────────────────────────────

			private static int CountOccurrences(string source, string search)
			{
				int count = 0, idx = 0;
				while ((idx = source.IndexOf(search, idx, StringComparison.Ordinal)) >= 0)
				{
					count++;
					idx += search.Length;
				}
				return count;
			}
		}
	}
}


