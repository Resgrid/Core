using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Resgrid.Model.Checklists;

namespace Resgrid.Services
{
	/// <summary>Local calendar recurrence with UTC period identities. No content or protected-read dependency.</summary>
	public static class ChecklistRecurrence
	{
		public static int[] ParseTimes(string value)
		{
			var parts = (value ?? "").Split(',', StringSplitOptions.TrimEntries);
			if (parts.Length < 1 || parts.Length > 8) throw new ChecklistException(400, "ScheduleValidation");
			var times = new List<int>();
			foreach (var part in parts)
			{
				if (!TimeOnly.TryParseExact(part, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) throw new ChecklistException(400, "ScheduleValidation");
				times.Add(time.Hour * 60 + time.Minute);
			}
			return times.Distinct().OrderBy(t => t).ToArray();
		}
		public static TimeZoneInfo Zone(string id)
		{
			try { return TimeZoneInfo.FindSystemTimeZoneById(id ?? ""); }
			catch (Exception ex) when (ex is TimeZoneNotFoundException || ex is InvalidTimeZoneException || ex is ArgumentException) { throw new ChecklistException(400, "ScheduleValidation"); }
		}
		public static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
		public static DateTime ToUtc(DateTime local, TimeZoneInfo zone)
		{
			local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
			// A skipped clock time moves to the first valid minute; a repeated time occurs once, at its earlier instant.
			while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
			return zone.IsAmbiguousTime(local) ? new DateTimeOffset(local, zone.GetAmbiguousTimeOffsets(local).Max()).UtcDateTime : TimeZoneInfo.ConvertTimeToUtc(local, zone);
		}
		public static IReadOnlyList<DateTime> Expand(ChecklistSchedule schedule, DateTime fromUtc, DateTime untilUtc, IEnumerable<DateTime> workshiftStarts = null)
		{
			fromUtc = Utc(fromUtc); untilUtc = Utc(untilUtc);
			if (untilUtc <= fromUtc) return Array.Empty<DateTime>();
			if (untilUtc - fromUtc > TimeSpan.FromDays(8)) throw new ArgumentOutOfRangeException(nameof(untilUtc));
			var zone = Zone(schedule.TimeZoneId); var result = new SortedSet<DateTime>();
			var frequency = (ChecklistScheduleFrequency)schedule.Frequency;
			if (frequency == ChecklistScheduleFrequency.OnDemand) return Array.Empty<DateTime>();
			if (frequency == ChecklistScheduleFrequency.PerShift && !string.IsNullOrEmpty(schedule.WorkshiftId))
			{
				foreach (var start in workshiftStarts ?? Array.Empty<DateTime>())
				{
					var utc = Utc(start); var date = TimeZoneInfo.ConvertTimeFromUtc(utc, zone).Date;
					if (utc >= fromUtc && utc < untilUtc && date >= schedule.StartDate.Date && (!schedule.EndDate.HasValue || date <= schedule.EndDate.Value.Date)) result.Add(utc);
				}
				return result.ToArray();
			}
			var minutes = schedule.ClockMinutes.Split(',').Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray();
			var last = TimeZoneInfo.ConvertTimeFromUtc(untilUtc, zone).Date.AddDays(1);
			for (var date = TimeZoneInfo.ConvertTimeFromUtc(fromUtc, zone).Date.AddDays(-1); date <= last; date = date.AddDays(1))
			{
				if (date < schedule.StartDate.Date || schedule.EndDate.HasValue && date > schedule.EndDate.Value.Date) continue;
				var day = Math.Min(schedule.DayOfMonth, DateTime.DaysInMonth(date.Year, date.Month));
				var monthDelta = (date.Month - schedule.MonthOfYear + 12) % 12;
				var matches = frequency switch
				{
					ChecklistScheduleFrequency.Daily or ChecklistScheduleFrequency.PerShift => true,
					ChecklistScheduleFrequency.Weekly => (schedule.Weekdays & (1 << (int)date.DayOfWeek)) != 0,
					ChecklistScheduleFrequency.Monthly => date.Day == day,
					ChecklistScheduleFrequency.Quarterly => date.Day == day && monthDelta % 3 == 0,
					ChecklistScheduleFrequency.SemiAnnual => date.Day == day && monthDelta % 6 == 0,
					ChecklistScheduleFrequency.Annual => date.Day == day && monthDelta == 0,
					_ => throw new ChecklistException(400, "ScheduleValidation")
				};
				if (!matches) continue;
				foreach (var minute in minutes)
				{
					var utc = ToUtc(date.AddMinutes(minute), zone);
					if (utc >= fromUtc && utc < untilUtc) result.Add(utc);
				}
			}
			return result.ToArray();
		}
	}
}
