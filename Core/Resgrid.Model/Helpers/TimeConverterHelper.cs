using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using NodaTime;
using Resgrid.Framework;
using TimeZoneConverter;

namespace Resgrid.Model.Helpers
{
	public static class TimeConverterHelper
	{
		public static DateTime TimeConverter(this DateTime timestamp, Department department)
		{
			// If department is null we gotta just bail
			if (department == null)
				return timestamp;

			try
			{
				string timeZone = "Pacific Standard Time"; // Default to Pacific as it's better then UTC

				if (!String.IsNullOrEmpty(department.TimeZone))
					timeZone = department.TimeZone;

				// Resolve via NodaTime's embedded IANA database instead of TimeZoneInfo /
				// TZConvert.GetTimeZoneInfo. The hardened (DHI) container ships without ICU and
				// runs in globalization-invariant mode, where TimeZoneInfo cannot map a Windows
				// zone id and throws TimeZoneNotFoundException. NodaTime carries its own tzdb and
				// needs neither ICU nor the OS /usr/share/zoneinfo files. Mirrors TimeConverterToString.
				var id = DateTimeHelpers.ConvertTimeZoneString(timeZone);
                var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(id) ?? DateTimeZoneProviders.Tzdb[TZConvert.WindowsToIana(id)];

				var instant = Instant.FromDateTimeUtc(DateTime.SpecifyKind(timestamp, DateTimeKind.Utc));
				return instant.InZone(zone).ToDateTimeUnspecified();
			}
			catch (Exception ex)
			{
				var method = new StackTrace().GetFrame(1).GetMethod();
				Framework.Logging.LogError(String.Format("TimeConverter error called from '{0}' of class '{1}' error {2}", method.Name, method.DeclaringType, ex.ToString()));

				return timestamp;
			}
		}

		/// <summary>
		/// The inverse of <see cref="TimeConverter"/>: a department-local wall-clock value to its UTC instant, resolved in the same
		/// zone (Pacific when the department has none). DST gaps and overlaps resolve leniently, like the MVC DepartmentTime input
		/// path, so a typed 02:30 on the spring-forward day still saves. A value already marked UTC keeps its instant.
		/// </summary>
		public static DateTime DepartmentLocalToUtc(this DateTime local, Department department)
		{
			if (local.Kind == DateTimeKind.Utc) return local;
			if (local.Kind == DateTimeKind.Local) return local.ToUniversalTime();
			var timeZone = string.IsNullOrEmpty(department?.TimeZone) ? "Pacific Standard Time" : department.TimeZone;
			var id = DateTimeHelpers.ConvertTimeZoneString(timeZone);
			var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(id) ?? DateTimeZoneProviders.Tzdb[TZConvert.WindowsToIana(id)];
			return LocalDateTime.FromDateTime(local).InZoneLeniently(zone).ToDateTimeUtc();
		}

		public static string TimeConverterToString(this DateTime timestamp, Department department)
        {
            department ??= new Department();
            return timestamp.TimeConverter(department).FormatForDepartment(department);
        }

		public static string FormatForDepartment(this DateTime timestamp, Department department, bool dropSeconds = false)
		{
			if (department.Use24HourTime.HasValue && department.Use24HourTime.Value)
				if (dropSeconds)
					return timestamp.ToString("MM/dd/yyyy HHmm", CultureInfo.InvariantCulture);
				else
					return timestamp.ToString("MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
			else
				if (dropSeconds)
				return timestamp.ToString("MM/dd/yyyy h:mm tt", CultureInfo.InvariantCulture);
			else
				return timestamp.ToString("MM/dd/yyyy h:mm:ss tt", CultureInfo.InvariantCulture);
		}

		public static TimeSpan GetOffsetForDepartment(Department department)
		{
			TimeSpan timeSpan;

			try
			{
				string timeZone = "Pacific Standard Time"; // Default to Pacific as it's better then UTC

				if (!String.IsNullOrEmpty(department.TimeZone))
					timeZone = department.TimeZone;

				// NodaTime tzdb (no ICU / OS tzdata dependency, unlike TimeZoneInfo). GetUtcOffset
				// already folds the active DST rule into the returned offset for the given instant.
				var id = DateTimeHelpers.ConvertTimeZoneString(timeZone);
                var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(id) ?? DateTimeZoneProviders.Tzdb[TZConvert.WindowsToIana(id)];
				var instant = Instant.FromDateTimeUtc(DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc));

				timeSpan = zone.GetUtcOffset(instant).ToTimeSpan();
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				timeSpan = new TimeSpan(-7, 0, 0);
			}

			return timeSpan;
		}
	}
}
