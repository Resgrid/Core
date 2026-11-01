using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Dashboard.Widgets.Planning;
using Resgrid.Model.Services;
using Resgrid.Services.Dashboard.Widgets.Planning;
using static Resgrid.Tests.Dashboard.Widgets.CommsPlanningWidgetContexts;

namespace Resgrid.Tests.Dashboard.Widgets
{
	[TestFixture]
	public class CalendarAgendaWidgetProviderTests
	{
		private static CalendarItem Item(int id, int hoursFromNow, int type = 0, int departmentId = DepartmentId, List<CalendarItemAttendee> attendees = null) => new CalendarItem
		{
			CalendarItemId = id, DepartmentId = departmentId, Title = "Item " + id, Description = "private description", Location = "Station 1",
			Start = Now.AddHours(hoursFromNow), End = Now.AddHours(hoursFromNow + 2), ItemType = type, Attendees = attendees ?? new List<CalendarItemAttendee>()
		};

		private static Mock<ICalendarService> Calendar(params CalendarItem[] items)
		{
			var calendar = new Mock<ICalendarService>(MockBehavior.Strict);
			calendar.Setup(x => x.GetUpcomingCalendarItemsAsync(DepartmentId, Now)).ReturnsAsync(items.ToList());
			calendar.Setup(x => x.GetAllCalendarItemTypesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<CalendarItemType>
			{
				new CalendarItemType { CalendarItemTypeId = 3, DepartmentId = DepartmentId, Name = "Training", Color = "#00ff00" }
			});
			calendar.Setup(x => x.GetCheckInsByUserDateRangeAsync(UserId, DepartmentId, It.IsAny<DateTime>(), It.IsAny<DateTime>()))
				.ReturnsAsync(new List<CalendarItemCheckIn> { new CalendarItemCheckIn { CalendarItemId = 1, DepartmentId = DepartmentId, UserId = UserId, CheckInTime = Now } });
			return calendar;
		}

		[Test]
		public async Task Lists_the_window_with_types_and_the_viewers_state()
		{
			var attendees = new List<CalendarItemAttendee> { new CalendarItemAttendee { UserId = UserId, AttendeeType = (int)CalendarItemAttendeeTypes.RSVP } };
			var provider = new CalendarAgendaWidgetProvider(Calendar(Item(2, 30, 3), Item(1, 1, 3, attendees: attendees), Item(9, 24 * 10), Item(5, 2, departmentId: OtherDepartmentId)).Object);

			var data = (CalendarAgendaWidgetData)await provider.GetDataAsync(For("calendar.agenda"), CancellationToken.None);

			data.TotalCount.Should().Be(2, "the 10-day item is outside the default 7 days and another department's item is never listed");
			data.TimeZoneId.Should().Be("America/Los_Angeles");
			data.Items.Select(i => i.CalendarItemId).Should().Equal(1, 2);
			data.Items[0].Should().BeEquivalentTo(new { TypeName = "Training", TypeColor = "#00ff00", MyAttendeeType = (int?)1, CheckedIn = true, CheckedOut = false, Location = "Station 1" });
			data.Items[0].Url.Should().Be("/User/Calendar/View?calendarItemId=1");
		}

		[Test]
		public async Task A_type_filter_naming_another_departments_type_is_ignored()
		{
			var provider = new CalendarAgendaWidgetProvider(Calendar(Item(1, 1, 3), Item(2, 2, 0)).Object);

			var foreign = (CalendarAgendaWidgetData)await provider.GetDataAsync(
				For("calendar.agenda", CommsPlanningWidgetContexts.Settings(("types", new[] { "999" }))), CancellationToken.None);
			var own = (CalendarAgendaWidgetData)await provider.GetDataAsync(
				For("calendar.agenda", CommsPlanningWidgetContexts.Settings(("types", new[] { "3", "999" }))), CancellationToken.None);

			foreign.Items.Should().HaveCount(2);
			own.Items.Should().ContainSingle().Which.CalendarItemId.Should().Be(1);
		}

		[Test]
		public async Task An_empty_window_reads_nothing_else()
		{
			var calendar = new Mock<ICalendarService>(MockBehavior.Strict);
			calendar.Setup(x => x.GetUpcomingCalendarItemsAsync(DepartmentId, Now)).ReturnsAsync(new List<CalendarItem>());
			var provider = new CalendarAgendaWidgetProvider(calendar.Object);

			var data = (CalendarAgendaWidgetData)await provider.GetDataAsync(For("calendar.agenda"), CancellationToken.None);

			data.Items.Should().BeEmpty();
		}
	}
}
