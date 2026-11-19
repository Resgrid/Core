using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Resgrid.Model.Helpers;

namespace Resgrid.Model
{
	/// <summary>
	/// When a <see cref="RecurringCall"/> runs. Pure calculation over the schedule's fields: dates are matched in the department's
	/// time zone and each local time is converted to UTC on its own date, so a run stays at the same wall-clock time across a
	/// daylight-saving change. A local time the clocks skip runs at the first valid time after it; one they repeat runs the first time.
	/// </summary>
	public static class RecurringCallRecurrence
	{
		/// <summary>The default: each call is created a day before it is dispatched.</summary>
		public const int DefaultLeadTimeMinutes = 24 * 60;

		public const int MinLeadTimeMinutes = 60;

		public const int MaxLeadTimeMinutes = 14 * 24 * 60;

		public const int MaxTimesPerDay = 24;

		public const int MaxInterval = 52;

		/// <summary>
		/// How late an occurrence's call may still be created. The scheduled-calls worker dispatches calls due in its last 15
		/// minutes, so a call created later than that would never go out; one missed by more than this is left alone.
		/// </summary>
		public const int MissedGraceMinutes = 10;

		/// <summary>The longest range a calendar or projection asks about, so a bad request cannot walk years of dates.</summary>
		public const int MaxRangeDays = 100;

		/// <summary>Dispatcher mode default: the dispatcher is alerted 15 minutes before an occurrence is due.</summary>
		public const int DefaultManualAlertMinutes = 15;

		public const int MaxManualAlertMinutes = 24 * 60;

		/// <summary>
		/// How far back the worker looks for occurrences nobody created (the worker was down, or creating the call kept failing) to
		/// raise a missed-call alert for them. Older ones are left alone.
		/// </summary>
		public const int MissedLookbackHours = 24;

		/// <summary>How long a dispatcher-mode alert waits for a dispatcher before it is closed as expired.</summary>
		public const int DispatcherAlertExpiryHours = 24;

		/// <summary>Validation error keys (the localization keys the web form shows).</summary>
		public static class Errors
		{
			public const string NameRequired = "ErrorNameRequired";
			public const string TimesRequired = "ErrorTimesRequired";
			public const string TooManyTimes = "ErrorTooManyTimes";
			public const string IntervalInvalid = "ErrorIntervalInvalid";
			public const string FrequencyInvalid = "ErrorFrequencyInvalid";
			public const string DaysRequired = "ErrorDaysRequired";
			public const string MonthlyDayInvalid = "ErrorMonthlyDayInvalid";
			public const string MonthlyWeekdayInvalid = "ErrorMonthlyWeekdayInvalid";
			public const string EndsBeforeStart = "ErrorEndsBeforeStart";
			public const string LeadTimeInvalid = "ErrorLeadTimeInvalid";
			public const string CreationModeInvalid = "ErrorCreationModeInvalid";
			public const string ManualAlertInvalid = "ErrorManualAlertInvalid";
			public const string ContactEmailInvalid = "ErrorContactEmailInvalid";
		}

		/// <summary>The weekday bit for a <see cref="DayOfWeek"/> in <see cref="RecurringCall.DaysOfWeek"/>.</summary>
		public static int DayBit(DayOfWeek day) => 1 << (int)day;

		/// <summary>Parses one "H:mm" / "HH:mm" time of day (24-hour).</summary>
		public static bool TryParseTime(string text, out TimeSpan time)
		{
			time = TimeSpan.Zero;

			if (string.IsNullOrWhiteSpace(text))
				return false;

			if (DateTime.TryParseExact(text.Trim(), new[] { "H:mm", "HH:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
			{
				time = new TimeSpan(parsed.Hour, parsed.Minute, 0);
				return true;
			}

			return false;
		}

		/// <summary>The valid times in a stored <see cref="RecurringCall.Times"/> value, earliest first, without duplicates.</summary>
		public static List<TimeSpan> ParseTimes(string times)
		{
			var result = new List<TimeSpan>();

			if (string.IsNullOrWhiteSpace(times))
				return result;

			foreach (var part in times.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
			{
				if (TryParseTime(part, out var time) && !result.Contains(time))
					result.Add(time);
			}

			result.Sort();
			return result;
		}

		/// <summary>The stored form of a set of times ("08:00,13:00").</summary>
		public static string FormatTimes(IEnumerable<TimeSpan> times)
		{
			return string.Join(",", (times ?? Enumerable.Empty<TimeSpan>()).Distinct().OrderBy(x => x)
				.Select(x => x.ToString(@"hh\:mm", CultureInfo.InvariantCulture)));
		}

		/// <summary>Everything wrong with a schedule, as <see cref="Errors"/> keys; empty when it can be saved.</summary>
		public static List<string> Validate(RecurringCall series)
		{
			var errors = new List<string>();

			if (series == null)
			{
				errors.Add(Errors.FrequencyInvalid);
				return errors;
			}

			if (string.IsNullOrWhiteSpace(series.Name))
				errors.Add(Errors.NameRequired);

			var times = ParseTimes(series.Times);
			if (times.Count == 0)
				errors.Add(Errors.TimesRequired);
			else if (times.Count > MaxTimesPerDay)
				errors.Add(Errors.TooManyTimes);

			if (series.RepeatInterval < 1 || series.RepeatInterval > MaxInterval)
				errors.Add(Errors.IntervalInvalid);

			switch ((RecurringCallFrequencies)series.Frequency)
			{
				case RecurringCallFrequencies.Daily:
					break;
				case RecurringCallFrequencies.Weekly:
					if ((series.DaysOfWeek & 127) == 0)
						errors.Add(Errors.DaysRequired);
					break;
				case RecurringCallFrequencies.Monthly:
					if (series.MonthlyWeek == 0)
					{
						if (series.MonthlyDay < 1 || series.MonthlyDay > 31)
							errors.Add(Errors.MonthlyDayInvalid);
					}
					else if (series.MonthlyWeek < 1 || series.MonthlyWeek > 5 || series.MonthlyWeekday < 0 || series.MonthlyWeekday > 6)
					{
						errors.Add(Errors.MonthlyWeekdayInvalid);
					}
					break;
				default:
					errors.Add(Errors.FrequencyInvalid);
					break;
			}

			if (series.EndsOn.HasValue && series.EndsOn.Value.Date < series.StartsOn.Date)
				errors.Add(Errors.EndsBeforeStart);

			if (series.CreationMode != (int)RecurringCallCreationModes.Automatic && series.CreationMode != (int)RecurringCallCreationModes.Dispatcher)
				errors.Add(Errors.CreationModeInvalid);
			else if (series.IsDispatcherMode)
			{
				// The lead time does not apply: nothing is created ahead of time.
				if (series.ManualAlertMinutes < 0 || series.ManualAlertMinutes > MaxManualAlertMinutes)
					errors.Add(Errors.ManualAlertInvalid);
			}
			else if (series.LeadTimeMinutes < MinLeadTimeMinutes || series.LeadTimeMinutes > MaxLeadTimeMinutes)
				errors.Add(Errors.LeadTimeInvalid);

			if (!string.IsNullOrWhiteSpace(series.ScheduleContactEmail) && !IsPlausibleEmail(series.ScheduleContactEmail))
				errors.Add(Errors.ContactEmailInvalid);

			return errors;
		}

		/// <summary>
		/// What the New Call Form Fields policy checks, for a schedule (web form and v4 API alike). Fields a schedule has no input for
		/// are unsupported, so the policy never demands them.
		/// </summary>
		public static NewCallFieldValues FieldValues(RecurringCall series)
		{
			return new NewCallFieldValues
			{
				Note = series.Notes,
				Address = series.Address,
				Geolocation = series.GeoLocationData,
				ContactName = series.ContactName,
				ContactInfo = series.ContactNumber,
				ExternalId = series.ExternalIdentifier,
				IncidentId = series.IncidentNumber,
				ReferenceId = series.ReferenceNumber,
				MapPage = series.MapPage,
				DestinationPoiId = series.DestinationPoiId,
				HasDispatchList = series.HasDispatchTargets,
				// Every call it creates has a dispatch time.
				DispatchOn = series.StartsOn
			}.Unsupported(NewCallFieldKeys.What3Words, NewCallFieldKeys.PlusCode, NewCallFieldKeys.IndoorLocation, NewCallFieldKeys.Protocols,
				NewCallFieldKeys.LinkedCall, NewCallFieldKeys.PrimaryContact, NewCallFieldKeys.AdditionalContacts, NewCallFieldKeys.Part2Consent,
				NewCallFieldKeys.PendingCall);
		}

		private static bool IsPlausibleEmail(string value)
		{
			var text = value.Trim();
			var at = text.IndexOf('@');

			return text.Length <= 256 && at > 0 && at == text.LastIndexOf('@') && at < text.Length - 3 && text.IndexOf('.', at) > at + 1 &&
				   !text.Any(char.IsWhiteSpace);
		}

		/// <summary>Whether the schedule runs on a local date (its times aside).</summary>
		public static bool OccursOn(RecurringCall series, DateTime localDate)
		{
			if (series == null)
				return false;

			var date = localDate.Date;
			var start = series.StartsOn.Date;
			var interval = Math.Max(1, series.RepeatInterval);

			if (date < start || (series.EndsOn.HasValue && date > series.EndsOn.Value.Date))
				return false;

			switch ((RecurringCallFrequencies)series.Frequency)
			{
				case RecurringCallFrequencies.Daily:
					return (date - start).Days % interval == 0;

				case RecurringCallFrequencies.Weekly:
				{
					if ((series.DaysOfWeek & DayBit(date.DayOfWeek)) == 0)
						return false;

					// Weeks start on Sunday; the week holding the start date is week 0.
					var startWeek = start.AddDays(-(int)start.DayOfWeek);
					var thisWeek = date.AddDays(-(int)date.DayOfWeek);
					return ((thisWeek - startWeek).Days / 7) % interval == 0;
				}

				case RecurringCallFrequencies.Monthly:
				{
					var months = (date.Year - start.Year) * 12 + date.Month - start.Month;
					if (months < 0 || months % interval != 0)
						return false;

					var daysInMonth = DateTime.DaysInMonth(date.Year, date.Month);

					if (series.MonthlyWeek == 0)
						return series.MonthlyDay >= 1 && date.Day == Math.Min(series.MonthlyDay, daysInMonth);

					if ((int)date.DayOfWeek != series.MonthlyWeekday)
						return false;

					if (series.MonthlyWeek >= 5)
						return date.Day + 7 > daysInMonth;

					return (date.Day - 1) / 7 + 1 == series.MonthlyWeek;
				}

				default:
					return false;
			}
		}

		/// <summary>The local date-times it runs at from <paramref name="fromLocalDate"/> to <paramref name="toLocalDate"/> (both dates inclusive).</summary>
		public static IEnumerable<DateTime> LocalOccurrences(RecurringCall series, DateTime fromLocalDate, DateTime toLocalDate)
		{
			var times = ParseTimes(series?.Times);
			if (series == null || times.Count == 0)
				yield break;

			var from = fromLocalDate.Date;
			var to = toLocalDate.Date;

			if (from < series.StartsOn.Date)
				from = series.StartsOn.Date;

			if (series.EndsOn.HasValue && to > series.EndsOn.Value.Date)
				to = series.EndsOn.Value.Date;

			for (var date = from; date <= to; date = date.AddDays(1))
			{
				if (!OccursOn(series, date))
					continue;

				foreach (var time in times)
					yield return DateTime.SpecifyKind(date.Add(time), DateTimeKind.Unspecified);
			}
		}

		/// <summary>
		/// When it runs (UTC) from <paramref name="fromUtc"/> (inclusive) to <paramref name="toUtc"/> (exclusive), earliest first,
		/// with the local times taken in the department's time zone. At most <see cref="MaxRangeDays"/> are walked.
		/// </summary>
		public static List<DateTime> UtcOccurrences(RecurringCall series, Department department, DateTime fromUtc, DateTime toUtc)
		{
			var result = new List<DateTime>();

			if (series == null || toUtc <= fromUtc)
				return result;

			if ((toUtc - fromUtc).TotalDays > MaxRangeDays)
				toUtc = fromUtc.AddDays(MaxRangeDays);

			// One day either side covers every offset a zone can have from UTC.
			var fromLocal = ToLocal(fromUtc, department).Date.AddDays(-1);
			var toLocal = ToLocal(toUtc, department).Date.AddDays(1);
			var seen = new HashSet<DateTime>();

			foreach (var local in LocalOccurrences(series, fromLocal, toLocal))
			{
				var utc = ToUtc(local, department);

				if (utc >= fromUtc && utc < toUtc && seen.Add(utc))
					result.Add(utc);
			}

			result.Sort();
			return result;
		}

		/// <summary>
		/// The first time it runs at or after <paramref name="afterUtc"/>, looking as far ahead as the longest gap the schedule can
		/// have (every <see cref="MaxInterval"/> months, a little over four years); null when it never runs again.
		/// </summary>
		public static DateTime? NextOccurrenceUtc(RecurringCall series, Department department, DateTime afterUtc)
		{
			if (series == null || !IsSupportedInstant(afterUtc))
				return null;

			// Gaps can exceed one window (every 52 weeks, every 52 months), so walk window by window, as many as the schedule's
			// longest gap (its interval plus one more period, for a day of the month a short month does not have) needs.
			var interval = Math.Clamp(series.RepeatInterval, 1, MaxInterval);
			var horizonDays = (RecurringCallFrequencies)series.Frequency switch
			{
				RecurringCallFrequencies.Monthly => (interval + 1) * 31,
				RecurringCallFrequencies.Weekly => (interval + 1) * 7,
				_ => interval + 1
			};
			var windows = horizonDays / MaxRangeDays + 2;

			var from = afterUtc;
			for (var i = 0; i < windows && IsSupportedInstant(from); i++)
			{
				var to = from.AddDays(MaxRangeDays);
				var next = UtcOccurrences(series, department, from, to).FirstOrDefault();

				if (next != default(DateTime))
					return next;

				if (series.EndsOn.HasValue && ToLocal(to, department).Date > series.EndsOn.Value.Date)
					return null;

				from = to;
			}

			return null;
		}

		/// <summary>The earliest instant the occurrence and calendar code is asked about (date arithmetic around it cannot underflow).</summary>
		public static readonly DateTime MinSupportedUtc = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

		/// <summary>The latest instant the occurrence and calendar code is asked about (date arithmetic around it cannot overflow).</summary>
		public static readonly DateTime MaxSupportedUtc = new DateTime(9000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

		/// <summary>
		/// Whether an instant is one the occurrence and calendar code can work with: well inside <see cref="DateTime"/>'s range, so
		/// the days and months it adds or takes away around it never fall off either end. Request values outside it are rejected.
		/// </summary>
		public static bool IsSupportedInstant(DateTime utc) => utc >= MinSupportedUtc && utc <= MaxSupportedUtc;

		/// <summary>
		/// A local (department) time in UTC, resolved in the department's zone as everything else in Resgrid is (Pacific when it
		/// has none); daylight-saving gaps and overlaps resolve instead of throwing. No department: the value is taken as UTC.
		/// </summary>
		public static DateTime ToUtc(DateTime local, Department department)
		{
			if (department == null)
				return DateTime.SpecifyKind(local, DateTimeKind.Utc);

			return DateTime.SpecifyKind(DateTime.SpecifyKind(local, DateTimeKind.Unspecified).DepartmentLocalToUtc(department), DateTimeKind.Utc);
		}

		/// <summary>A UTC time in the department's local time (see <see cref="ToUtc"/>).</summary>
		public static DateTime ToLocal(DateTime utc, Department department)
		{
			if (department == null)
				return DateTime.SpecifyKind(utc, DateTimeKind.Unspecified);

			return DateTime.SpecifyKind(DateTime.SpecifyKind(utc, DateTimeKind.Utc).TimeConverter(department), DateTimeKind.Unspecified);
		}
	}
}
