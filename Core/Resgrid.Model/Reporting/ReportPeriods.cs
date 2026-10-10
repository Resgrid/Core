using System;
using System.Collections.Generic;

namespace Resgrid.Model.Reporting
{
	public enum ReportPeriodGranularity
	{
		Month = 1,
		Quarter = 2,
		Year = 3
	}

	/// <summary>One calendar month, quarter or year a report range touches, in department-local time.</summary>
	public class ReportPeriod
	{
		public ReportPeriodGranularity Granularity { get; set; }

		/// <summary>Calendar start of the period (inclusive), not clipped to the report range.</summary>
		public DateTime Start { get; set; }

		/// <summary>Calendar end of the period (exclusive), not clipped to the report range.</summary>
		public DateTime End { get; set; }

		public int Year { get; set; }

		/// <summary>Month 1-12, quarter 1-4, or the year again for a yearly period.</summary>
		public int Number { get; set; }

		/// <summary>The report range covers only part of the period.</summary>
		public bool IsPartial { get; set; }

		public bool Contains(DateTime local) => local >= Start && local < End;
	}

	/// <summary>
	/// Calendar breakdowns of a department-local report range. Coverage is judged by date, not time of day, so the
	/// report pickers' "Jan 1 00:00:01 to Dec 31 23:59:59" default counts as the whole year, and a range ending exactly
	/// at midnight ends the day before (it does not touch the next month).
	/// </summary>
	public static class ReportPeriods
	{
		/// <summary>The last instant the range covers: an end at exactly midnight belongs to the previous day.</summary>
		public static DateTime InclusiveEnd(DateTime start, DateTime end)
		{
			if (end > start && end.TimeOfDay == TimeSpan.Zero)
				return end.AddTicks(-1);

			return end < start ? start : end;
		}

		public static bool CrossesMonth(DateTime start, DateTime end)
		{
			return MonthIndex(start) != MonthIndex(InclusiveEnd(start, end));
		}

		public static bool CrossesQuarter(DateTime start, DateTime end)
		{
			return QuarterIndex(start) != QuarterIndex(InclusiveEnd(start, end));
		}

		/// <summary>True when at least one calendar year lies entirely inside the range.</summary>
		public static bool CoversFullYear(DateTime start, DateTime end)
		{
			var last = InclusiveEnd(start, end).Date;

			for (var year = start.Year; year <= last.Year; year++)
			{
				if (start.Date <= new DateTime(year, 1, 1) && last >= new DateTime(year, 12, 31))
					return true;
			}

			return false;
		}

		/// <summary>Every calendar period of the granularity the range touches, in order.</summary>
		public static List<ReportPeriod> Split(DateTime start, DateTime end, ReportPeriodGranularity granularity)
		{
			var periods = new List<ReportPeriod>();
			var last = InclusiveEnd(start, end);

			var periodStart = PeriodStart(start, granularity);
			while (periodStart <= last)
			{
				var periodEnd = granularity == ReportPeriodGranularity.Month ? periodStart.AddMonths(1)
					: granularity == ReportPeriodGranularity.Quarter ? periodStart.AddMonths(3)
					: periodStart.AddYears(1);

				periods.Add(new ReportPeriod
				{
					Granularity = granularity,
					Start = periodStart,
					End = periodEnd,
					Year = periodStart.Year,
					Number = granularity == ReportPeriodGranularity.Month ? periodStart.Month
						: granularity == ReportPeriodGranularity.Quarter ? ((periodStart.Month - 1) / 3) + 1
						: periodStart.Year,
					IsPartial = start.Date > periodStart || last.Date < periodEnd.AddDays(-1)
				});

				periodStart = periodEnd;
			}

			return periods;
		}

		private static DateTime PeriodStart(DateTime value, ReportPeriodGranularity granularity)
		{
			switch (granularity)
			{
				case ReportPeriodGranularity.Month:
					return new DateTime(value.Year, value.Month, 1);
				case ReportPeriodGranularity.Quarter:
					return new DateTime(value.Year, (((value.Month - 1) / 3) * 3) + 1, 1);
				default:
					return new DateTime(value.Year, 1, 1);
			}
		}

		private static int MonthIndex(DateTime value) => (value.Year * 12) + value.Month - 1;

		private static int QuarterIndex(DateTime value) => (value.Year * 4) + ((value.Month - 1) / 3);
	}
}
