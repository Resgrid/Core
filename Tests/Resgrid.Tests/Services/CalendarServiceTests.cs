using System;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Framework.Testing;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	namespace CalendarServiceTests
	{
		public class with_the_calendar_service : TestBase
		{
			protected ICalendarService _calendarService;

			protected Department _testDepartment;

			protected readonly Mock<IDepartmentsService> _departmentsServiceMock;
			protected readonly Mock<ICalendarItemsRepository> _calendarItemRepositoryMock;
			protected readonly Mock<ICalendarItemTypeRepository> _calendarItemTypeRepositoryMock;
			protected readonly Mock<ICalendarItemAttendeeRepository> _calendarItemAttendeeRepositoryMock;
			protected readonly Mock<ICommunicationService> _communicationServiceMock;
			protected readonly Mock<IUserProfileService> _userProfileServiceMock;
			protected readonly Mock<IDepartmentGroupsService> _departmentGroupsServiceMock;
			protected readonly Mock<IDepartmentSettingsService> _departmentSettingsServiceMock;

			protected with_the_calendar_service()
			{
				_departmentsServiceMock = new Mock<IDepartmentsService>();
				_calendarItemRepositoryMock = new Mock<ICalendarItemsRepository>();
				_calendarItemTypeRepositoryMock = new Mock<ICalendarItemTypeRepository>();
				_calendarItemAttendeeRepositoryMock = new Mock<ICalendarItemAttendeeRepository>();
				_communicationServiceMock = new Mock<ICommunicationService>();
				_userProfileServiceMock = new Mock<IUserProfileService>();
				_departmentGroupsServiceMock = new Mock<IDepartmentGroupsService>();
				_departmentSettingsServiceMock = new Mock<IDepartmentSettingsService>();

				#region Departments
				_testDepartment = new Department()
				{
					DepartmentId = 269,
					Name = "Fire Department",
					Code = "XXX",
					DepartmentType = "Fire",
					ManagingUserId = Guid.NewGuid().ToString(),
					TimeZone = "Eastern Standard Time",
					Use24HourTime = true,
					Address = new Address()
					{
						AddressId = 432,
						Address1 = "555 Main St",
						City = "Parump",
						Country = "United States",
						PostalCode = "85471",
						State = "Nevada"
					},
					CreatedOn = DateTime.Now.AddMonths(-3)
				};
				#endregion Departments

				_departmentsServiceMock.Setup(x => x.GetDepartmentByIdAsync(999, false)).ReturnsAsync(_testDepartment);

				_calendarService = new CalendarService(_calendarItemRepositoryMock.Object, _calendarItemTypeRepositoryMock.Object,
					_calendarItemAttendeeRepositoryMock.Object, _departmentsServiceMock.Object, _communicationServiceMock.Object,
					_userProfileServiceMock.Object, _departmentGroupsServiceMock.Object, _departmentSettingsServiceMock.Object);
			}
		}

		[TestFixture]
		public class when_generating_recurring_calendar_items : with_the_calendar_service
		{
			[Test]
			public async Task should_return_empty_list_for_no_recurrance()
			{
				CalendarItem item = new CalendarItem();
				item.DepartmentId = 999;
				item.Start = new DateTime(2022, 4, 20, 13, 30, 00, DateTimeKind.Utc);
				item.End = new DateTime(2022, 4, 20, 17, 30, 00, DateTimeKind.Utc);
				item.RecurrenceType = (int) RecurrenceTypes.None;

				var now = new DateTime(2022, 4, 20, 7, 17, 33, DateTimeKind.Utc);

				var calendarItems = await _calendarService.CreateRecurrenceCalendarItemsAsync(item, now);

				calendarItems.Should().NotBeNull();
				calendarItems.Should().BeEmpty();
			}

			[Test]
			public async Task should_return_empty_list_for_expired_recurrance()
			{
				CalendarItem item = new CalendarItem();
				item.DepartmentId = 999;
				item.Start = new DateTime(2022, 4, 20, 13, 30, 00, DateTimeKind.Utc);
				item.End = new DateTime(2022, 4, 20, 17, 30, 00, DateTimeKind.Utc);
				item.RecurrenceType = (int)RecurrenceTypes.Weekly;
				item.RecurrenceEnd = new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc);

				var now = new DateTime(2022, 4, 20, 7, 17, 33, DateTimeKind.Utc);

				var calendarItems = await _calendarService.CreateRecurrenceCalendarItemsAsync(item, now);

				calendarItems.Should().NotBeNull();
				calendarItems.Should().BeEmpty();
			}
		}

		[TestFixture]
		public class when_getting_next_week_dates : with_the_calendar_service
		{
			[Test]
			public async Task should_return_array_with_nulls_for_no_days()
			{
				CalendarItem item = new CalendarItem();
				var now = new DateTime(2022, 4, 20, 7, 17, 33, DateTimeKind.Utc);
				var dates = _calendarService.GetNextWeekValues(item, now);

				dates.Should().NotBeNull();
				dates.Length.Should().Be(7);

				dates[0].Should().BeNull();
				dates[1].Should().BeNull();
				dates[2].Should().BeNull();
				dates[3].Should().BeNull();
				dates[4].Should().BeNull();
				dates[5].Should().BeNull();
				dates[6].Should().BeNull();
			}

			[Test]
			public void should_return_for_sunday()
			{
				CalendarItem item = new CalendarItem();
				var now = new DateTime(2022, 4, 20, 7, 17, 33, DateTimeKind.Utc);
				item.Sunday = true;

				var dates = _calendarService.GetNextWeekValues(item, now);

				dates.Should().NotBeNull();
				dates.Length.Should().Be(7);

				dates[0].Should().NotBeNull();
				if (dates[0].HasValue)
				{
					dates[0].Value.Month.Should().Be(4);
					dates[0].Value.Day.Should().Be(24);
					dates[0].Value.Year.Should().Be(2022);
				}
				
				dates[1].Should().BeNull();
				dates[2].Should().BeNull();
				dates[3].Should().BeNull();
				dates[4].Should().BeNull();
				dates[5].Should().BeNull();
				dates[6].Should().BeNull();
			}

			[Test]
			public void should_return_for_monday()
			{
				CalendarItem item = new CalendarItem();
				var now = new DateTime(2022, 4, 20, 7, 17, 33, DateTimeKind.Utc);
				item.Monday = true;

				var dates = _calendarService.GetNextWeekValues(item, now);

				dates.Should().NotBeNull();
				dates.Length.Should().Be(7);

				dates[0].Should().BeNull();

				dates[1].Should().NotBeNull();
				if (dates[1].HasValue)
				{
					dates[1].Value.Month.Should().Be(4);
					dates[1].Value.Day.Should().Be(25);
					dates[1].Value.Year.Should().Be(2022);
				}

				dates[2].Should().BeNull();
				dates[3].Should().BeNull();
				dates[4].Should().BeNull();
				dates[5].Should().BeNull();
				dates[6].Should().BeNull();
			}

			[Test]
			public void should_return_for_tuesday()
			{
				CalendarItem item = new CalendarItem();
				var now = new DateTime(2022, 4, 20, 7, 17, 33, DateTimeKind.Utc);
				item.Tuesday = true;

				var dates = _calendarService.GetNextWeekValues(item, now);

				dates.Should().NotBeNull();
				dates.Length.Should().Be(7);

				dates[0].Should().BeNull();
				dates[1].Should().BeNull();

				dates[2].Should().NotBeNull();
				if (dates[2].HasValue)
				{
					dates[2].Value.Month.Should().Be(4);
					dates[2].Value.Day.Should().Be(26);
					dates[2].Value.Year.Should().Be(2022);
				}

				dates[3].Should().BeNull();
				dates[4].Should().BeNull();
				dates[5].Should().BeNull();
				dates[6].Should().BeNull();
			}

			[Test]
			public void should_return_for_wednesday()
			{
				CalendarItem item = new CalendarItem();
				var now = new DateTime(2022, 4, 20, 7, 17, 33, DateTimeKind.Utc);
				item.Wednesday = true;

				var dates = _calendarService.GetNextWeekValues(item, now);

				dates.Should().NotBeNull();
				dates.Length.Should().Be(7);

				dates[0].Should().BeNull();
				dates[1].Should().BeNull();
				dates[2].Should().BeNull();

				dates[3].Should().NotBeNull();
				if (dates[3].HasValue)
				{
					dates[3].Value.Month.Should().Be(4);
					dates[3].Value.Day.Should().Be(27);
					dates[3].Value.Year.Should().Be(2022);
				}

				dates[4].Should().BeNull();
				dates[5].Should().BeNull();
				dates[6].Should().BeNull();
			}

			[Test]
			public void should_return_for_thursday()
			{
				CalendarItem item = new CalendarItem();
				var now = new DateTime(2022, 4, 20, 7, 17, 33, DateTimeKind.Utc);
				item.Thursday = true;

				var dates = _calendarService.GetNextWeekValues(item, now);

				dates.Should().NotBeNull();
				dates.Length.Should().Be(7);

				dates[0].Should().BeNull();
				dates[1].Should().BeNull();
				dates[2].Should().BeNull();
				dates[3].Should().BeNull();

				dates[4].Should().NotBeNull();
				if (dates[4].HasValue)
				{
					dates[4].Value.Month.Should().Be(4);
					dates[4].Value.Day.Should().Be(21);
					dates[4].Value.Year.Should().Be(2022);
				}

				dates[5].Should().BeNull();
				dates[6].Should().BeNull();
			}

			[Test]
			public void should_return_for_friday()
			{
				CalendarItem item = new CalendarItem();
				var now = new DateTime(2022, 4, 20, 7, 17, 33, DateTimeKind.Utc);
				item.Friday = true;

				var dates = _calendarService.GetNextWeekValues(item, now);

				dates.Should().NotBeNull();
				dates.Length.Should().Be(7);

				dates[0].Should().BeNull();
				dates[1].Should().BeNull();
				dates[2].Should().BeNull();
				dates[3].Should().BeNull();
				dates[4].Should().BeNull();

				dates[5].Should().NotBeNull();
				if (dates[5].HasValue)
				{
					dates[5].Value.Month.Should().Be(4);
					dates[5].Value.Day.Should().Be(22);
					dates[5].Value.Year.Should().Be(2022);
				}

				dates[6].Should().BeNull();
			}

			[Test]
			public void should_return_for_saturday()
			{
				CalendarItem item = new CalendarItem();
				var now = new DateTime(2022, 4, 20, 7, 17, 33, DateTimeKind.Utc);
				item.Saturday = true;

				var dates = _calendarService.GetNextWeekValues(item, now);

				dates.Should().NotBeNull();
				dates.Length.Should().Be(7);

				dates[0].Should().BeNull();
				dates[1].Should().BeNull();
				dates[2].Should().BeNull();
				dates[3].Should().BeNull();
				dates[4].Should().BeNull();
				dates[5].Should().BeNull();

				dates[6].Should().NotBeNull();
				if (dates[6].HasValue)
				{
					dates[6].Value.Month.Should().Be(4);
					dates[6].Value.Day.Should().Be(23);
					dates[6].Value.Year.Should().Be(2022);
				}
			}
		}

		[TestFixture]
		public class when_generating_recurring_calendar_items_weekly : with_the_calendar_service
		{
			[Test]
			public async Task should_get_full_year_for_biweekly_recurrance()
			{
				CalendarItem item = new CalendarItem();
				item.CalendarItemId = 512;
				item.DepartmentId = 999;
				item.Start = new DateTime(2022, 4, 20, 13, 30, 00, DateTimeKind.Utc);
				item.End = new DateTime(2022, 4, 20, 17, 30, 00, DateTimeKind.Utc);
				item.RecurrenceType = (int)RecurrenceTypes.Weekly;
				item.Tuesday = true;
				item.Thursday = true;

				var now = new DateTime(2022, 4, 20, 7, 17, 33, DateTimeKind.Utc);

				var calendarItems = await _calendarService.CreateRecurrenceCalendarItemsAsync(item, now);

				calendarItems.Should().NotBeNull();
				calendarItems.Should().NotBeEmpty();
				calendarItems.Count.Should().Be(104);
			}
		}

		[TestFixture]
		public class when_generating_recurring_calendar_items_monthly : with_the_calendar_service
		{
			[Test]
			public async Task should_get_full_year_for_day_of_month()
			{
				CalendarItem item = new CalendarItem();
				item.CalendarItemId = 512;
				item.DepartmentId = 999;
				item.Start = new DateTime(2022, 4, 20, 17, 30, 00, DateTimeKind.Utc);
				item.End = new DateTime(2022, 4, 20, 21, 30, 00, DateTimeKind.Utc);
				item.RecurrenceType = (int)RecurrenceTypes.Monthly;
				item.RepeatOnMonth = 20;

				var now = new DateTime(2022, 4, 20, 7, 17, 33, DateTimeKind.Utc);

				var calendarItems = await _calendarService.CreateRecurrenceCalendarItemsAsync(item, now);

				calendarItems.Should().NotBeNull();
				calendarItems.Should().NotBeEmpty();
				calendarItems.Count.Should().Be(12);
				calendarItems[6].Start.Day.Should().Be(20);
				calendarItems[6].Start.Month.Should().Be(11);
				calendarItems[6].Start.Year.Should().Be(2022);
				//calendarItems[6].Start.Hour.Should().Be(17);
				//calendarItems[6].Start.Minute.Should().Be(30);

				var start = calendarItems[6].Start.TimeConverter(_testDepartment);
				start.Hour.Should().Be(13);
				start.Minute.Should().Be(30);

				calendarItems[6].End.Day.Should().Be(20);
				calendarItems[6].End.Month.Should().Be(11);
				calendarItems[6].End.Year.Should().Be(2022);
				//calendarItems[6].End.Hour.Should().Be(21);
				//calendarItems[6].End.Minute.Should().Be(30);

				var end = calendarItems[6].End.TimeConverter(_testDepartment);
				end.Hour.Should().Be(17);
				end.Minute.Should().Be(30);
			}

			[Test]
			public async Task should_get_correct_day_of_month()
			{
				CalendarItem item = new CalendarItem();
				item.CalendarItemId = 512;
				item.DepartmentId = 999;
				item.Start = new DateTime(2022, 4, 20, 17, 30, 00, DateTimeKind.Utc);
				item.End = new DateTime(2022, 4, 20, 21, 30, 00, DateTimeKind.Utc);
				item.RecurrenceType = (int)RecurrenceTypes.Monthly;
				item.RepeatOnMonth = (int)RepeatOnMonthTypes.Thrid;
				item.RepeatOnDay = (int)DayOfWeek.Thursday;

				var now = new DateTime(2022, 4, 20, 7, 17, 33, DateTimeKind.Utc);

				var calendarItems = await _calendarService.CreateRecurrenceCalendarItemsAsync(item, now);

				calendarItems.Should().NotBeNull();
				calendarItems.Should().NotBeEmpty();
				calendarItems.Count.Should().Be(12);

				var start = calendarItems[6].Start.TimeConverter(_testDepartment);
				start.Hour.Should().Be(13);
				start.Minute.Should().Be(30);
				start.Day.Should().Be(17);
				start.Month.Should().Be(11);
				start.Year.Should().Be(2022);

				var end = calendarItems[6].End.TimeConverter(_testDepartment);
				end.Hour.Should().Be(17);
				end.Minute.Should().Be(30);
				end.Day.Should().Be(17);
				end.Month.Should().Be(11);
				end.Year.Should().Be(2022);
			}
		}

		[TestFixture]
		public class when_generating_recurring_calendar_items_yearly : with_the_calendar_service
		{
			[Test]
			public async Task should_get_next_year()
			{
				CalendarItem item = new CalendarItem();
				item.CalendarItemId = 512;
				item.DepartmentId = 999;
				item.Start = new DateTime(2022, 4, 20, 17, 30, 00, DateTimeKind.Utc);
				item.End = new DateTime(2022, 4, 20, 21, 30, 00, DateTimeKind.Utc);
				item.RecurrenceType = (int)RecurrenceTypes.Yearly;
				//item.RepeatOnMonth = 20;

				var now = new DateTime(2022, 4, 20, 7, 17, 33, DateTimeKind.Utc);

				var calendarItems = await _calendarService.CreateRecurrenceCalendarItemsAsync(item, now);

				calendarItems.Should().NotBeNull();
				calendarItems.Should().NotBeEmpty();
				calendarItems.Count.Should().Be(1);
				calendarItems[0].Start.Day.Should().Be(20);
				calendarItems[0].Start.Month.Should().Be(4);
				calendarItems[0].Start.Year.Should().Be(2023);

				var start = calendarItems[0].Start.TimeConverter(_testDepartment);
				start.Hour.Should().Be(13);
				start.Minute.Should().Be(30);
			}
		}
	}
}
