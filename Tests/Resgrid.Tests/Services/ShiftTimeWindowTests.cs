using System;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model.Helpers;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class ShiftTimeWindowTests
	{
		private static readonly DateTime Day = new DateTime(2026, 9, 24);

		[TestCase("7:00 AM", 7, 0)]
		[TestCase("07:00 am", 7, 0)]
		[TestCase("12:00 AM", 0, 0)]
		[TestCase("12:30 PM", 12, 30)]
		[TestCase("7:15PM", 19, 15)]
		[TestCase("19:00", 19, 0)]
		[TestCase("0700", 7, 0)]
		public void parses_twelve_and_twenty_four_hour_times(string value, int hour, int minute)
		{
			ShiftTimeWindow.TryParseTimeOfDay(value).Should().Be(new TimeSpan(hour, minute, 0));
		}

		[TestCase(null)]
		[TestCase("")]
		[TestCase("not a time")]
		[TestCase("25:00")]
		public void unreadable_times_are_null(string value)
		{
			ShiftTimeWindow.TryParseTimeOfDay(value).Should().BeNull();
		}

		[Test]
		public void a_day_shift_runs_from_start_to_end_on_the_same_day()
		{
			var window = ShiftTimeWindow.GetWindow(Day, "7:00 AM", "7:00 PM", null);

			window.Start.Should().Be(Day.AddHours(7));
			window.End.Should().Be(Day.AddHours(19));
		}

		[Test]
		public void an_overnight_shift_ends_the_next_morning()
		{
			var window = ShiftTimeWindow.GetWindow(Day, "19:00", "07:00", null);

			window.Start.Should().Be(Day.AddHours(19));
			window.End.Should().Be(Day.AddDays(1).AddHours(7));
		}

		[Test]
		public void hours_are_used_when_there_is_no_end_time()
		{
			var window = ShiftTimeWindow.GetWindow(Day, "08:00", null, 10);

			window.End.Should().Be(Day.AddHours(18));
		}

		[Test]
		public void no_end_and_no_hours_runs_a_full_day_from_midnight_when_start_is_missing()
		{
			var window = ShiftTimeWindow.GetWindow(Day, null, null, null);

			window.Start.Should().Be(Day);
			window.End.Should().Be(Day.AddDays(1));
		}

		[Test]
		public void yesterdays_overnight_shift_is_still_active_after_midnight()
		{
			var yesterday = Day.AddDays(-1);

			ShiftTimeWindow.IsActive(Day.AddHours(3), yesterday, "7:00 PM", "7:00 AM", null).Should().BeTrue();
			ShiftTimeWindow.IsActive(Day.AddHours(7), yesterday, "7:00 PM", "7:00 AM", null).Should().BeFalse("the end is exclusive");
		}

		[Test]
		public void a_shift_is_not_active_before_it_starts()
		{
			ShiftTimeWindow.IsActive(Day.AddHours(6).AddMinutes(59), Day, "7:00 AM", "7:00 PM", null).Should().BeFalse();
			ShiftTimeWindow.IsActive(Day.AddHours(7), Day, "7:00 AM", "7:00 PM", null).Should().BeTrue();
		}
	}
}
