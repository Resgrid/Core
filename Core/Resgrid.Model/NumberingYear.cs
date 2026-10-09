using System;

namespace Resgrid.Model
{
	/// <summary>Which calendar year names a numbering year that does not start on January 1.</summary>
	public enum NumberingYearLabel
	{
		/// <summary>The year it ends in, the usual fiscal-year convention: a year starting 1 November 2026 is 2027.</summary>
		EndYear = 0,

		/// <summary>The year it starts in: a year starting 1 November 2026 is 2026.</summary>
		StartYear = 1
	}

	/// <summary>
	/// The day a department's numbering year starts: January 1, the calendar year, unless it numbers by a fiscal year (its own,
	/// or a county's or state's it reports to) that starts on another month and day. Call numbering (setting 115) and record
	/// numbering (setting 72) each carry their own, so calls can follow the calendar while reports follow a fiscal year.
	/// Dates are department-local.
	/// </summary>
	public sealed class NumberingYearStart
	{
		public static readonly NumberingYearStart Calendar = new NumberingYearStart(1, 1, NumberingYearLabel.EndYear);

		/// <summary>
		/// An unset (0) month or day reads as January or the 1st, so a department that never saved one numbers by the calendar
		/// year; so does a saved date that no longer validates.
		/// </summary>
		public NumberingYearStart(int month, int day, NumberingYearLabel label)
		{
			month = month <= 0 ? 1 : month;
			day = day <= 0 ? 1 : day;
			if (!IsValid(month, day))
			{
				month = 1;
				day = 1;
			}

			Month = month;
			Day = day;
			Label = label == NumberingYearLabel.StartYear ? NumberingYearLabel.StartYear : NumberingYearLabel.EndYear;
		}

		public int Month { get; }

		public int Day { get; }

		public NumberingYearLabel Label { get; }

		public bool IsCalendarYear => Month == 1 && Day == 1;

		/// <summary>A month and day every year has. February 29 is refused, so the year starts on the same date every year.</summary>
		public static bool IsValid(int month, int day)
		{
			return month >= 1 && month <= 12 && day >= 1 && day <= DateTime.DaysInMonth(2001, month);
		}

		/// <summary>Local midnight of the day the numbering year containing <paramref name="localDate"/> started.</summary>
		public DateTime StartOf(DateTime localDate)
		{
			var start = new DateTime(localDate.Year, Month, Day, 0, 0, 0, DateTimeKind.Unspecified);
			return localDate < start ? start.AddYears(-1) : start;
		}

		/// <summary>Local midnight of the day the numbering year named <paramref name="year"/> starts.</summary>
		public DateTime StartOfYear(int year)
		{
			return new DateTime(NamedByStartYear ? year : year - 1, Month, Day, 0, 0, 0, DateTimeKind.Unspecified);
		}

		/// <summary>
		/// The year naming the numbering year that contains <paramref name="localDate"/>: the calendar year when it starts on
		/// January 1, otherwise the year it ends or starts in, as <see cref="Label"/> says.
		/// </summary>
		public int YearOf(DateTime localDate)
		{
			var startYear = StartOf(localDate).Year;
			return NamedByStartYear ? startYear : startYear + 1;
		}

		private bool NamedByStartYear => IsCalendarYear || Label == NumberingYearLabel.StartYear;
	}
}
