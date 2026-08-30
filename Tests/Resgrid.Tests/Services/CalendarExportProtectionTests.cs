using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Moq;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// The iCal feed is fetched by a calendar application holding a revocable token and NO Protected
	/// Data Grant — there is no step-up available to it, ever. So for a protected department an
	/// entry keeps its shape (when it starts, how long, its reminder) and loses its content. What
	/// must never happen is ciphertext arriving in someone's phone calendar.
	/// </summary>
	[TestFixture]
	public class CalendarExportProtectionTests
	{
		private static string Export(CalendarItem item)
		{
			var calendarService = new Mock<ICalendarService>();
			calendarService.Setup(x => x.GetCalendarItemByIdAsync(item.CalendarItemId)).ReturnsAsync(item);

			var service = new CalendarExportService(calendarService.Object);
			return service.GenerateICalForItemAsync(item.CalendarItemId).GetAwaiter().GetResult();
		}

		private static CalendarItem EnvelopedItem() => new CalendarItem
		{
			CalendarItemId = 42,
			DepartmentId = 7,
			Title = "rgdp:1:1:title==",
			Description = "rgdp:1:1:description==",
			Location = "rgdp:1:1:location==",
			Start = new DateTime(2026, 9, 1, 18, 0, 0, DateTimeKind.Utc),
			End = new DateTime(2026, 9, 1, 20, 0, 0, DateTimeKind.Utc)
		};

		[Test]
		public void An_enveloped_entry_never_reaches_the_feed_as_ciphertext()
		{
			var feed = Export(EnvelopedItem());

			feed.Should().NotContain("rgdp:", "a calendar application would render the envelope verbatim");
			feed.Should().NotContain("title==");
			feed.Should().NotContain("location==");
		}

		[Test]
		public void The_entry_still_appears_so_the_calendar_lays_out()
		{
			var feed = Export(EnvelopedItem());

			feed.Should().Contain("BEGIN:VEVENT");
			feed.Should().Contain("resgrid-cal-42@resgrid",
				"the member has to see that something is scheduled, then open Resgrid for the detail");
			feed.Should().Contain("20260901T180000Z", "the scheduling columns are not cataloged");
		}

		[Test]
		public void An_unprotected_entry_is_untouched()
		{
			var item = EnvelopedItem();
			item.Title = "Station 2 drill";
			item.Description = "Ladder evolutions";
			item.Location = "Training grounds";

			var feed = Export(item);

			feed.Should().Contain("Station 2 drill");
			feed.Should().Contain("Training grounds");
		}
	}
}
