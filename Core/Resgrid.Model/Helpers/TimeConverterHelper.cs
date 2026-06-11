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
				var ianaTz = TZConvert.WindowsToIana(DateTimeHelpers.ConvertTimeZoneString(timeZone));

				var instant = Instant.FromDateTimeUtc(DateTime.SpecifyKind(timestamp, DateTimeKind.Utc));
				return instant.InZone(DateTimeZoneProviders.Tzdb[ianaTz]).ToDateTimeUnspecified();
			}
			catch (Exception ex)
			{
				var method = new StackTrace().GetFrame(1).GetMethod();
				Framework.Logging.LogError(String.Format("TimeConverter error called from '{0}' of class '{1}' error {2}", method.Name, method.DeclaringType, ex.ToString()));

				return timestamp;
			}
		}

		public static string TimeConverterToString(this DateTime timestamp, Department department)
		{
			//DateTime newTime = timestamp;
			//TimeZoneInfo timeZoneInfo = null;

			try
			{
				//if (!String.IsNullOrEmpty(department.TimeZone))
				//	timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(DateTimeHelpers.ConvertTimeZoneString(department.TimeZone));
				//else
				//	timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");	// Default to Pacific as it's better then UTC

				//if (timeZoneInfo != null)
				//{
				//	newTime = TimeZoneInfo.ConvertTimeFromUtc(timestamp, timeZoneInfo);
				//}

				//return newTime.FormatForDepartment(department);

				string timeZone = "Pacific Standard Time";

				if (!String.IsNullOrEmpty(department.TimeZone))
					timeZone = department.TimeZone;

				var ianaTz = TZConvert.WindowsToIana(timeZone);

				var localTime = Instant.FromDateTimeUtc(DateTime.SpecifyKind(timestamp, DateTimeKind.Utc));   //LocalDateTime.FromDateTime(timestamp);
				//var zonedDateTime = localTime.InZoneLeniently(DateTimeZoneProviders.Tzdb[ianaTz]);
				var zonedDateTime = localTime.InZone(DateTimeZoneProviders.Tzdb[ianaTz]);

				return zonedDateTime.ToDateTimeUnspecified().FormatForDepartment(department);
			}
			catch (Exception ex)
			{
				var method = new StackTrace().GetFrame(1).GetMethod();
				Framework.Logging.LogError(String.Format("TimeConverterToString error called from '{0}' of class '{1}' error {2}", method.Name, method.DeclaringType, ex.ToString()));

				return timestamp.FormatForDepartment(department);
			}
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
				var ianaTz = TZConvert.WindowsToIana(DateTimeHelpers.ConvertTimeZoneString(timeZone));
				var instant = Instant.FromDateTimeUtc(DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc));

				timeSpan = DateTimeZoneProviders.Tzdb[ianaTz].GetUtcOffset(instant).ToTimeSpan();
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
