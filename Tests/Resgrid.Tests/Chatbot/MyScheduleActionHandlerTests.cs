using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Chatbot.Handlers;
using Resgrid.Chatbot.Models;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Tests.Chatbot
{
	[TestFixture]
	public class MyScheduleActionHandlerTests
	{
		[Test]
		public async Task HandleAsync_WithLocalizedDate_ParsesAndFormatsUsingSessionCulture()
		{
			// Arrange
			var shifts = new Mock<IShiftsService>();
			shifts.Setup(x => x.GetShiftDaysForDayAsync(It.IsAny<DateTime>(), 1)).ReturnsAsync(new List<ShiftDay>());
			var calendar = new Mock<ICalendarService>();
			calendar.Setup(x => x.GetAllCalendarItemsForDepartmentInRangeAsync(1, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
				.ReturnsAsync(new List<CalendarItem>());
			var departments = CreateDepartmentsService();
			var handler = new MyScheduleActionHandler(shifts.Object, calendar.Object, departments.Object);
			var intent = new ChatbotIntent { Type = ChatbotIntentType.MySchedule };
			intent.Parameters["day"] = "22/7/2030";
			var session = new ChatbotSession { UserId = "user-1", DepartmentId = 1, Culture = "es" };
			var expectedLabel = new DateTime(2030, 7, 22).ToString("ddd M/d", CultureInfo.GetCultureInfo("es"));

			// Act
			var response = await handler.HandleAsync(new ChatbotMessage { Text = "mi horario" }, intent, session);

			// Assert
			response.Processed.Should().BeTrue();
			response.Text.Should().Contain(expectedLabel);
		}

		[Test]
		public async Task HandleAsync_WithLocalizedWeekday_FormatsShiftAndEventTimesUsingSessionCulture()
		{
			// Arrange
			var originalCulture = CultureInfo.CurrentCulture;
			CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
			try
			{
				var sessionCulture = CultureInfo.GetCultureInfo("de");
				var shifts = new Mock<IShiftsService>();
				DateTime targetDate = default;
				shifts.Setup(x => x.GetShiftDaysForDayAsync(It.IsAny<DateTime>(), 1))
					.ReturnsAsync((DateTime day, int _) =>
					{
						targetDate = day.Date;
						return new List<ShiftDay>
						{
							new ShiftDay
							{
								ShiftId = 5,
								Day = targetDate,
								Shift = new Shift { ShiftId = 5, Name = "Tagdienst", StartTime = "13:30", EndTime = "15:00" }
							}
						};
					});
				shifts.Setup(x => x.GetShiftPersonsForUserAsync("user-1")).ReturnsAsync(new List<ShiftPerson>
				{
					new ShiftPerson { ShiftId = 5, UserId = "user-1" }
				});
				shifts.Setup(x => x.GetShiftSignupsForUserAsync("user-1")).ReturnsAsync(new List<ShiftSignup>());

				var calendar = new Mock<ICalendarService>();
				calendar.Setup(x => x.GetAllCalendarItemsForDepartmentInRangeAsync(1, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
					.ReturnsAsync((int _, DateTime windowStart, DateTime _) => new List<CalendarItem>
					{
						new CalendarItem { CalendarItemId = 10, Start = windowStart.AddDays(1).AddHours(16).AddMinutes(45), Title = "Besprechung" }
					});
				calendar.Setup(x => x.GetCalendarItemAttendeeByUserAsync(10, "user-1"))
					.ReturnsAsync(new CalendarItemAttendee { AttendeeType = (int)CalendarItemAttendeeTypes.RSVP });

				var handler = new MyScheduleActionHandler(shifts.Object, calendar.Object, CreateDepartmentsService().Object);
				var intent = new ChatbotIntent { Type = ChatbotIntentType.MySchedule };
				intent.Parameters["day"] = "Montag";
				var session = new ChatbotSession { UserId = "user-1", DepartmentId = 1, Culture = "de" };

				// Act
				var response = await handler.HandleAsync(new ChatbotMessage { Text = "mein Dienstplan" }, intent, session);

				// Assert
				response.Processed.Should().BeTrue();
				response.Text.Should().Contain(targetDate.ToString("ddd M/d", sessionCulture));
				response.Text.Should().Contain(targetDate.AddHours(13).AddMinutes(30).ToString("t", sessionCulture));
				response.Text.Should().Contain(targetDate.AddHours(16).AddMinutes(45).ToString("t", sessionCulture));
			}
			finally
			{
				CultureInfo.CurrentCulture = originalCulture;
			}
		}

		private static Mock<IDepartmentsService> CreateDepartmentsService()
		{
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(x => x.GetDepartmentByIdAsync(1, true)).ReturnsAsync(new Department
			{
				DepartmentId = 1,
				TimeZone = "UTC"
			});
			return departments;
		}
	}
}
