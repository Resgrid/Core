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
			DateTime newTime = timestamp;
			TimeZoneInfo timeZoneInfo = null;

			// If department is null we gotta just bail
			if (department == null)
				return timestamp;

			try
			{
				if (!String.IsNullOrEmpty(department.TimeZone))
					timeZoneInfo =
						TZConvert.GetTimeZoneInfo(
							DateTimeHelpers.ConvertTimeZoneString(department
								.TimeZone)); // TimeZoneInfo.FindSystemTimeZoneById(DateTimeHelpers.ConvertTimeZoneString(department.TimeZone));
				else
					timeZoneInfo = TZConvert.GetTimeZoneInfo("Pacific Standard Time");// TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");	// Default to Pacific as it's better then UTC

				if (timeZoneInfo != null)
				{
					newTime = TimeZoneInfo.ConvertTimeFromUtc(timestamp, timeZoneInfo);
				}
				return newTime;
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
			TimeZoneInfo timeZoneInfo = null;
			TimeSpan timeSpan;

			try
			{
				if (!String.IsNullOrEmpty(department.TimeZone))
					timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(DateTimeHelpers.ConvertTimeZoneString(department.TimeZone));
				else
					timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(DateTimeHelpers.WindowsToIana("Pacific Standard Time"));    // Default to Pacific as it's better then UTC

				timeSpan = timeZoneInfo.BaseUtcOffset;
				var currentDateTime = DateTime.UtcNow.TimeConverter(department);

				if (timeZoneInfo.GetAdjustmentRules() != null && timeZoneInfo.GetAdjustmentRules().Any())
					timeSpan = timeZoneInfo.GetAdjustmentRules().Where(timeZoneAdjustment => timeZoneAdjustment.DateStart <= currentDateTime && timeZoneAdjustment.DateEnd >= currentDateTime).Aggregate(timeSpan, (current, timeZoneAdjustment) => current + timeZoneAdjustment.DaylightDelta);
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
