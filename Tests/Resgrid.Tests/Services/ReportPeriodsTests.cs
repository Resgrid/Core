using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model.Reporting;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class ReportPeriodsTests
	{
		// The report pickers' default: the whole current year, a second inside each end.
		private static readonly DateTime YearStart = new DateTime(2026, 1, 1, 0, 0, 1);
		private static readonly DateTime YearEnd = new DateTime(2026, 12, 31, 23, 59, 59);

		[Test]
		public void the_default_year_range_crosses_months_and_quarters_and_covers_the_year()
		{
			ReportPeriods.CrossesMonth(YearStart, YearEnd).Should().BeTrue();
			ReportPeriods.CrossesQuarter(YearStart, YearEnd).Should().BeTrue();
			ReportPeriods.CoversFullYear(YearStart, YearEnd).Should().BeTrue();
		}

		[Test]
		public void a_range_inside_one_month_has_no_breakdowns()
		{
			var start = new DateTime(2026, 3, 2, 8, 0, 0);
			var end = new DateTime(2026, 3, 30, 17, 0, 0);

			ReportPeriods.CrossesMonth(start, end).Should().BeFalse();
			ReportPeriods.CrossesQuarter(start, end).Should().BeFalse();
			ReportPeriods.CoversFullYear(start, end).Should().BeFalse();
		}

		[Test]
		public void a_range_ending_at_midnight_on_the_first_does_not_touch_the_next_month()
		{
			var start = new DateTime(2026, 3, 1);
			var end = new DateTime(2026, 4, 1);

			ReportPeriods.CrossesMonth(start, end).Should().BeFalse();
			ReportPeriods.Split(start, end, ReportPeriodGranularity.Month).Should().ContainSingle()
				.Which.IsPartial.Should().BeFalse();
		}

		[Test]
		public void months_inside_one_quarter_cross_months_but_not_quarters()
		{
			var start = new DateTime(2026, 4, 1);
			var end = new DateTime(2026, 6, 30, 23, 59, 59);

			ReportPeriods.CrossesMonth(start, end).Should().BeTrue();
			ReportPeriods.CrossesQuarter(start, end).Should().BeFalse();
		}

		[Test]
		public void a_partial_year_does_not_cover_a_full_year_but_two_spanning_years_can()
		{
			ReportPeriods.CoversFullYear(new DateTime(2026, 1, 2), YearEnd).Should().BeFalse();
			ReportPeriods.CoversFullYear(YearStart, new DateTime(2026, 12, 30)).Should().BeFalse();
			ReportPeriods.CoversFullYear(new DateTime(2025, 6, 1), new DateTime(2026, 6, 1)).Should().BeFalse();
			ReportPeriods.CoversFullYear(new DateTime(2024, 7, 1), new DateTime(2026, 2, 1)).Should().BeTrue();
		}

		[Test]
		public void splitting_into_quarters_numbers_them_and_flags_partial_ends()
		{
			var quarters = ReportPeriods.Split(new DateTime(2025, 11, 15), new DateTime(2026, 7, 10), ReportPeriodGranularity.Quarter);

			quarters.Select(q => (q.Year, q.Number)).Should().Equal((2025, 4), (2026, 1), (2026, 2), (2026, 3));
			quarters.Select(q => q.IsPartial).Should().Equal(true, false, false, true);
			quarters[1].Start.Should().Be(new DateTime(2026, 1, 1));
			quarters[1].End.Should().Be(new DateTime(2026, 4, 1));
		}

		[Test]
		public void splitting_the_default_year_gives_twelve_whole_months_and_one_whole_year()
		{
			var months = ReportPeriods.Split(YearStart, YearEnd, ReportPeriodGranularity.Month);
			months.Should().HaveCount(12);
			months.Should().OnlyContain(m => !m.IsPartial);
			months.Select(m => m.Number).Should().Equal(Enumerable.Range(1, 12));

			var years = ReportPeriods.Split(YearStart, YearEnd, ReportPeriodGranularity.Year);
			years.Should().ContainSingle().Which.IsPartial.Should().BeFalse();
		}

		[Test]
		public void a_period_contains_its_start_but_not_its_end()
		{
			var march = ReportPeriods.Split(new DateTime(2026, 3, 1), new DateTime(2026, 3, 31), ReportPeriodGranularity.Month).Single();

			march.Contains(new DateTime(2026, 3, 1)).Should().BeTrue();
			march.Contains(new DateTime(2026, 3, 31, 23, 59, 59)).Should().BeTrue();
			march.Contains(new DateTime(2026, 4, 1)).Should().BeFalse();
			march.Contains(new DateTime(2026, 2, 28, 23, 59, 59)).Should().BeFalse();
		}
	}
}
