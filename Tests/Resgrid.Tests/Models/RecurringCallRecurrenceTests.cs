using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Models
{
	/// <summary>When a recurring call (M0319) runs: patterns, local times across daylight saving, bounds and validation.</summary>
	[TestFixture]
	public class RecurringCallRecurrenceTests
	{
		private static readonly Department Eastern = new Department { DepartmentId = 1, TimeZone = "Eastern Standard Time" };

		private static DateTime Utc(int y, int m, int d, int h, int min = 0) => new DateTime(y, m, d, h, min, 0, DateTimeKind.Utc);

		private static RecurringCall Weekly(RecurringCallDays days, string times, DateTime startsOn, int interval = 1)
		{
			return new RecurringCall
			{
				Name = "IFT",
				Frequency = (int)RecurringCallFrequencies.Weekly,
				RepeatInterval = interval,
				DaysOfWeek = (int)days,
				Times = times,
				StartsOn = startsOn,
				LeadTimeMinutes = RecurringCallRecurrence.DefaultLeadTimeMinutes
			};
		}

		[Test]
		public void Tuesday_and_Thursday_three_times_a_day_runs_at_the_local_times()
		{
			// The customer example: interfacility transfers every Tuesday and Thursday, morning, afternoon and evening.
			var series = Weekly(RecurringCallDays.Tuesday | RecurringCallDays.Thursday, "08:00,13:00,19:00", new DateTime(2026, 10, 26));

			var runs = RecurringCallRecurrence.UtcOccurrences(series, Eastern, Utc(2026, 10, 25, 0), Utc(2026, 11, 1, 0));

			// Eastern Daylight Time (UTC-4) that week.
			runs.Should().Equal(
				Utc(2026, 10, 27, 12), Utc(2026, 10, 27, 17), Utc(2026, 10, 27, 23),
				Utc(2026, 10, 29, 12), Utc(2026, 10, 29, 17), Utc(2026, 10, 29, 23));
		}

		[Test]
		public void A_run_keeps_its_wall_clock_time_after_daylight_saving_ends()
		{
			var series = Weekly(RecurringCallDays.Tuesday, "08:00,19:00", new DateTime(2026, 10, 1));

			// US daylight saving ends Sunday 1 November 2026: 08:00 EST is 13:00 UTC, and 19:00 EST is midnight UTC.
			var runs = RecurringCallRecurrence.UtcOccurrences(series, Eastern, Utc(2026, 11, 2, 0), Utc(2026, 11, 5, 0));

			runs.Should().Equal(Utc(2026, 11, 3, 13), Utc(2026, 11, 4, 0));
		}

		[Test]
		public void A_time_the_clocks_skip_runs_at_the_first_valid_time_after_it()
		{
			var series = new RecurringCall
			{
				Name = "Nightly",
				Frequency = (int)RecurringCallFrequencies.Daily,
				RepeatInterval = 1,
				Times = "02:30",
				StartsOn = new DateTime(2026, 3, 1),
				LeadTimeMinutes = 60
			};

			// 02:30 does not exist on 8 March 2026 (clocks go 02:00 -> 03:00 EDT): it runs at 03:30 EDT, 07:30 UTC.
			var runs = RecurringCallRecurrence.UtcOccurrences(series, Eastern, Utc(2026, 3, 8, 0), Utc(2026, 3, 9, 0));

			runs.Should().Equal(Utc(2026, 3, 8, 7, 30));
		}

		[Test]
		public void Every_other_week_counts_weeks_from_the_start_week()
		{
			var series = Weekly(RecurringCallDays.Tuesday, "10:00", new DateTime(2026, 11, 2), interval: 2);

			var runs = RecurringCallRecurrence.UtcOccurrences(series, Eastern, Utc(2026, 11, 1, 0), Utc(2026, 12, 1, 0));

			// Weeks of 1 Nov (start week), 15 Nov and 29 Nov; not 8 Nov or 22 Nov.
			runs.Select(x => x.Day).Should().Equal(3, 17);
			runs.Should().HaveCount(2);
		}

		[Test]
		public void Every_third_day_counts_from_the_start_date()
		{
			var series = new RecurringCall { Name = "x", Frequency = (int)RecurringCallFrequencies.Daily, RepeatInterval = 3, Times = "12:00", StartsOn = new DateTime(2026, 11, 1), LeadTimeMinutes = 60 };

			var local = RecurringCallRecurrence.LocalOccurrences(series, new DateTime(2026, 11, 1), new DateTime(2026, 11, 10)).ToList();

			local.Select(x => x.Day).Should().Equal(1, 4, 7, 10);
		}

		[Test]
		public void Monthly_on_the_31st_runs_on_the_last_day_of_a_shorter_month()
		{
			var series = new RecurringCall { Name = "x", Frequency = (int)RecurringCallFrequencies.Monthly, RepeatInterval = 1, MonthlyDay = 31, Times = "09:00", StartsOn = new DateTime(2027, 1, 1), LeadTimeMinutes = 60 };

			var local = RecurringCallRecurrence.LocalOccurrences(series, new DateTime(2027, 1, 1), new DateTime(2027, 4, 30)).ToList();

			local.Select(x => x.Date).Should().Equal(new DateTime(2027, 1, 31), new DateTime(2027, 2, 28), new DateTime(2027, 3, 31), new DateTime(2027, 4, 30));
		}

		[Test]
		public void Monthly_on_the_second_tuesday_and_on_the_last_friday()
		{
			var second = new RecurringCall { Name = "x", Frequency = (int)RecurringCallFrequencies.Monthly, RepeatInterval = 1, MonthlyWeek = 2, MonthlyWeekday = (int)DayOfWeek.Tuesday, Times = "09:00", StartsOn = new DateTime(2026, 12, 1), LeadTimeMinutes = 60 };
			var last = new RecurringCall { Name = "x", Frequency = (int)RecurringCallFrequencies.Monthly, RepeatInterval = 1, MonthlyWeek = 5, MonthlyWeekday = (int)DayOfWeek.Friday, Times = "09:00", StartsOn = new DateTime(2026, 12, 1), LeadTimeMinutes = 60 };

			RecurringCallRecurrence.LocalOccurrences(second, new DateTime(2026, 12, 1), new DateTime(2026, 12, 31)).Select(x => x.Date)
				.Should().Equal(new DateTime(2026, 12, 8));
			RecurringCallRecurrence.LocalOccurrences(last, new DateTime(2026, 12, 1), new DateTime(2026, 12, 31)).Select(x => x.Date)
				.Should().Equal(new DateTime(2026, 12, 25));
		}

		[Test]
		public void Nothing_runs_before_the_start_date_or_after_the_end_date()
		{
			var series = Weekly(RecurringCallDays.Tuesday, "08:00", new DateTime(2026, 11, 10));
			series.EndsOn = new DateTime(2026, 11, 24);

			var local = RecurringCallRecurrence.LocalOccurrences(series, new DateTime(2026, 11, 1), new DateTime(2026, 12, 31)).ToList();

			// The end date is inclusive.
			local.Select(x => x.Day).Should().Equal(10, 17, 24);
			RecurringCallRecurrence.NextOccurrenceUtc(series, Eastern, Utc(2026, 11, 25, 0)).Should().BeNull();
		}

		[Test]
		public void Next_occurrence_finds_a_run_more_than_one_window_ahead()
		{
			var series = new RecurringCall { Name = "x", Frequency = (int)RecurringCallFrequencies.Monthly, RepeatInterval = 6, MonthlyDay = 1, Times = "09:00", StartsOn = new DateTime(2026, 1, 1), LeadTimeMinutes = 60 };

			// Runs 1 Jan and 1 Jul: from 2 Jan the next is 1 July 2026, 09:00 EDT = 13:00 UTC.
			RecurringCallRecurrence.NextOccurrenceUtc(series, Eastern, Utc(2026, 1, 2, 0)).Should().Be(Utc(2026, 7, 1, 13));
		}

		[Test]
		public void Times_are_parsed_sorted_and_deduplicated_and_bad_ones_are_dropped()
		{
			RecurringCallRecurrence.ParseTimes("19:00, 8:00,13:00,08:00,25:00,abc")
				.Should().Equal(new TimeSpan(8, 0, 0), new TimeSpan(13, 0, 0), new TimeSpan(19, 0, 0));
			RecurringCallRecurrence.FormatTimes(RecurringCallRecurrence.ParseTimes("19:00,8:00")).Should().Be("08:00,19:00");
		}

		[Test]
		public void Validation_reports_what_a_schedule_is_missing()
		{
			var series = Weekly(RecurringCallDays.None, "", new DateTime(2026, 11, 10));
			series.Name = " ";
			series.EndsOn = new DateTime(2026, 11, 1);
			series.LeadTimeMinutes = 5;

			RecurringCallRecurrence.Validate(series).Should().BeEquivalentTo(new[]
			{
				RecurringCallRecurrence.Errors.NameRequired, RecurringCallRecurrence.Errors.TimesRequired, RecurringCallRecurrence.Errors.DaysRequired,
				RecurringCallRecurrence.Errors.EndsBeforeStart, RecurringCallRecurrence.Errors.LeadTimeInvalid
			});

			RecurringCallRecurrence.Validate(Weekly(RecurringCallDays.Tuesday, "08:00", new DateTime(2026, 11, 10))).Should().BeEmpty();
		}

		[Test]
		public void Monthly_validation_checks_the_day_or_the_weekday()
		{
			var byDay = new RecurringCall { Name = "x", Frequency = (int)RecurringCallFrequencies.Monthly, RepeatInterval = 1, MonthlyDay = 0, Times = "09:00", StartsOn = new DateTime(2026, 1, 1), LeadTimeMinutes = 60 };
			var byWeekday = new RecurringCall { Name = "x", Frequency = (int)RecurringCallFrequencies.Monthly, RepeatInterval = 1, MonthlyWeek = 6, MonthlyWeekday = 2, Times = "09:00", StartsOn = new DateTime(2026, 1, 1), LeadTimeMinutes = 60 };

			RecurringCallRecurrence.Validate(byDay).Should().Equal(RecurringCallRecurrence.Errors.MonthlyDayInvalid);
			RecurringCallRecurrence.Validate(byWeekday).Should().Equal(RecurringCallRecurrence.Errors.MonthlyWeekdayInvalid);
		}

		[Test]
		public void A_department_with_no_time_zone_uses_pacific_like_the_rest_of_resgrid()
		{
			var series = Weekly(RecurringCallDays.Tuesday, "08:00", new DateTime(2026, 11, 1));

			// 08:00 PST (UTC-8) on Tuesday 17 November 2026.
			RecurringCallRecurrence.UtcOccurrences(series, new Department { DepartmentId = 2 }, Utc(2026, 11, 17, 0), Utc(2026, 11, 18, 0))
				.Should().Equal(Utc(2026, 11, 17, 16));
		}
	}
}
