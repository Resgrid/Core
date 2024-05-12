using NodaTime;
using System;
using System.Linq;
using TimeZoneConverter;

namespace Resgrid.Framework
{
	public static class DateTimeHelpers
	{
		/// <summary>
		/// Simple function to set time portion of a datetime to 00:00 of the same day.
		/// </summary>
		/// <param name="dateTime">Datetime to set</param>
		/// <returns>New datetime value with time portion set to 0:00</returns>
		public static DateTime SetToMidnight(this DateTime dateTime)
		{
			return dateTime.Subtract(dateTime.TimeOfDay);
		}

		/// <summary>
		/// Simple function to set time portion of a datetime to 23:59 of the same day.
		/// </summary>
		/// <param name="dateTime">Datetime to set</param>
		/// <returns>New datetime value with time portion set to 23:59</returns>
		public static DateTime SetToEndOfDay(this DateTime dateTime)
		{
			return SetToMinutesBeforeNextMidnight(dateTime, 1);
		}

		/// <summary>
		/// Simple function to set time portion of a datetime to number of minutes passed in less end of day time.
		/// </summary>
		/// <param name="dateTime">Datetime to set</param>
		/// <returns>New datetime value with time portion subtracted (x-minutes) from  set to 23:59</returns>
		public static DateTime SetToMinutesBeforeNextMidnight(this DateTime dateTime, int minutes)
		{
			return dateTime
				.SetToMidnight()
				.AddDays(1)
				.AddMinutes(-minutes);
		}

		public static string IanaToWindows(string ianaZoneId)
		{
			var tzdbSource = NodaTime.TimeZones.TzdbDateTimeZoneSource.Default;
			var mappings = tzdbSource.WindowsMapping.MapZones;
			var item = mappings.FirstOrDefault(x => x.TzdbIds.Contains(ianaZoneId));
			if (item == null) return null;
			return item.WindowsId;
		}

		// This will return the "primary" IANA zone that matches the given windows zone.
		public static string WindowsToIana(string windowsZoneId)
		{
			//var tzdbSource = NodaTime.TimeZones.TzdbDateTimeZoneSource.Default;
			//var tzi = TimeZoneInfo.FindSystemTimeZoneById(windowsZoneId);
			//var aliases = tzdbSource.WindowsMapping.PrimaryMapping[tzi.Id]; //.MapTimeZoneId(tzi);

			//return aliases;
			var ianaTz = TZConvert.WindowsToIana(windowsZoneId);

			return ianaTz;
		}

		public static DateTime GetNextWeekday(DateTime start, DayOfWeek day)
		{
			// https://stackoverflow.com/questions/6346119/datetime-get-next-tuesday
			// The (... + 7) % 7 ensures we end up with a value in the range [0, 6]
			int daysToAdd = ((int)day - (int)start.DayOfWeek + 7) % 7;

			if (daysToAdd == 0)
				daysToAdd = 7;

			return start.AddDays(daysToAdd);
		}

		//For example to find the day for 2nd Friday, February, 2016
		//=>call FindDay(2016, 2, DayOfWeek.Friday, 2)
		// https://stackoverflow.com/questions/5421972/how-to-find-the-3rd-friday-in-a-month-with-c
		public static int FindDay(int year, int month, DayOfWeek day, int occurance)
		{
			if (occurance <= 0 || occurance > 5)
				throw new Exception("Occurance is invalid");

			DateTime firstDayOfMonth = new DateTime(year, month, 1);
			//Substract first day of the month with the required day of the week 
			var daysneeded = (int)day - (int)firstDayOfMonth.DayOfWeek;
			//if it is less than zero we need to get the next week day (add 7 days)
			if (daysneeded < 0) daysneeded = daysneeded + 7;
			//DayOfWeek is zero index based; multiply by the Occurance to get the day
			var resultedDay = (daysneeded + 1) + (7 * (occurance - 1));

			if (resultedDay > (firstDayOfMonth.AddMonths(1) - firstDayOfMonth).Days)
				throw new Exception(String.Format("No {0} occurance(s) of {1} in the required month", occurance, day.ToString()));

			return resultedDay;
		}

		public static DateTime ConvertToUtc(DateTime dateTime, string timeZone)
		{
			//var tzdbSource = NodaTime.TimeZones.TzdbDateTimeZoneSource.Default;
			//var tzi = TimeZoneInfo.FindSystemTimeZoneById(IanaToWindows(timeZone));

			//Instant instant = Instant.FromDateTimeUtc(dateTime);
			//ZonedDateTime zonedDateTime = new ZonedDateTime(instant, DateTimeZoneProviders.Tzdb[timeZone]);

			//LocalDateTime localDateTime = new LocalDateTime(dateTime.Year, dateTime.Month, dateTime.Day, dateTime.Hour, dateTime.Minute, dateTime.Second);
			//ZonedDateTime zonedDateTime = new ZonedDateTime(localDateTime, DateTimeZoneProviders.Tzdb[timeZone]);

			if (!String.IsNullOrWhiteSpace(timeZone))
			{
				var ianaTz = TZConvert.WindowsToIana(timeZone);

				var localTime = LocalDateTime.FromDateTime(dateTime);
				var zonedDateTime = localTime.InZoneStrictly(DateTimeZoneProviders.Tzdb[ianaTz]);

				return zonedDateTime.ToDateTimeUtc();
			}

			return dateTime.ToUniversalTime();
		}

		public static DateTime GetLocalDateTime(DateTime dateTime, string timeZone)
		{
			if (!String.IsNullOrWhiteSpace(timeZone))
			{
				var ianaTz = TZConvert.WindowsToIana(timeZone);

				//var localTime = LocalDateTime.FromDateTime(dateTime);
				var TzdbTZ = DateTimeZoneProviders.Tzdb[ianaTz];
				//var zonedDateTime = localTime.InZoneStrictly(TzdbTZ);

				var instant = Instant.FromDateTimeUtc(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc));
				var result = instant.InZone(TzdbTZ).ToDateTimeUnspecified();
				return result;

				//return zonedDateTime.LocalDateTime.ToDateTimeUnspecified();
			}

			return dateTime;
		}

		public static TimeZoneInfo CreateTimeZoneInfo(string timeZone)
		{
			TimeZoneInfo timeZoneInfo = null;

			if (!String.IsNullOrEmpty(timeZone))
				timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(ConvertTimeZoneString(timeZone));
			else
				timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");

			return timeZoneInfo;
		}

		public static string ConvertTimeZoneString(string timezone)
		{
			if (timezone == "Saskatchewan")
				return "Canada Central Standard Time";
			else if (timezone == "London")
				return "GMT Standard Time";
			else if (timezone == "Adelaide")
				return "Cen. Australia Standard Time";
			else if (timezone == "Brisbane")
				return "E. Australia Standard Time";
			else if (timezone == "Sydney")
				return "AUS Eastern Standard Time";
			else if (timezone == "Newfoundland")
				return "Newfoundland and Labrador Standard Time";
			else if (timezone == "Wellington")
				return "New Zealand Standard Time";
			else if (timezone == "Mexico Standard Time")
				return "Central Standard Time (Mexico)";
			else if (timezone == "Melbourne")
				return "AUS Eastern Standard Time";
			else if (timezone == "Harare")
				return "South Africa Standard Time";
			else if (timezone == "Edinburgh")
				return "GMT Standard Time";
			else if (timezone == "Atlantic Time (Canada)")
				return "Atlantic Standard Time";
			else if (timezone == "Auckland")
				return "New Zealand Standard Time";
			else if (timezone == "Hobart")
				return "Tasmania Standard Time";
			else if (timezone == "Lisbon")
				return "GMT Standard Time";
			else if (timezone == "Perth")
				return "W. Australia Standard Time";

			return timezone;
		}

		public static string MonthToShortString(int month)
		{
			switch (month)
			{
				case 1:
					return "Jan";
				case 2:
					return "Feb";
				case 3:
					return "Mar";
				case 4:
					return "Apr";
				case 5:
					return "May";
				case 6:
					return "Jun";
				case 7:
					return "Jul";
				case 8:
					return "Aug";
				case 9:
					return "Sep";
				case 10:
					return "Oct";
				case 11:
					return "Nov";
				case 12:
					return "Dec";
			}

			return string.Empty;
		}

		public static DateTime ConvertStringTime(string timeIn, DateTime date, bool use24HourTime)
		{
			if (timeIn.Contains(" "))
			{
				var parts = timeIn.Split(char.Parse(" "));

				foreach (var part in parts)
				{
					var tryTime = TryConvertStringTime(part, date, use24HourTime);

					if (tryTime.HasValue)
						return tryTime.Value;
				}
			}

			var time = TryConvertStringTime(timeIn, date, use24HourTime);

			return time.Value;
		}

		private static DateTime? TryConvertStringTime(string timeIn, DateTime date, bool use24HourTime)
		{
			if (timeIn.Contains("/"))
				return null;

			try
			{
				string time = "";

				if (use24HourTime)
					time = Convert24HourTo12Hour(timeIn);
				else if (!time.ToUpper().Contains("AM") || !time.ToUpper().Contains("PM"))
					time = Convert24HourTo12Hour(timeIn);
				else
					time = timeIn;

				bool am = time.ToUpper().Contains("AM");
				int hour = 0;
				int minute = 0;

				String[] timeParts = time.Split(char.Parse(":"));

				if (timeParts.Count() == 2)
				{
					if (!String.IsNullOrWhiteSpace(timeParts[0].ToLower().Replace("am", "").Replace("pm", "").Trim()))
						hour = int.Parse(timeParts[0].ToLower().Replace("am", "").Replace("pm", "").Trim());

					if (!String.IsNullOrWhiteSpace(timeParts[1].ToLower().Replace("am", "").Replace("pm", "").Trim()))
						minute = int.Parse(timeParts[1].ToLower().Replace("am", "").Replace("pm", "").Trim());
				}
				else if (timeParts.Count() == 1)
				{
					if (!String.IsNullOrWhiteSpace(timeParts[0].ToLower().Replace("am", "").Replace("pm", "").Trim()))
						hour = int.Parse(timeParts[0].ToLower().Replace("am", "").Replace("pm", "").Trim());

					if (!time.ToUpper().Contains("AM") || !time.ToUpper().Contains("PM"))
						if (hour < 12)
							am = true;
				}

				if (hour > 24)
					hour = 12;

				int adjustedHours = 0;

				if (hour == 12)
				{
					if (!am)
						adjustedHours = 12;
					else
						adjustedHours = 0;
				}
				else
				{
					if (!am)
						adjustedHours = 12 + hour;
					else
						adjustedHours = hour;
				}

				return new DateTime(date.Year, date.Month, date.Day, adjustedHours, minute, 0);
			}
			catch
			{
				// Ignore for now
			}

			return null;
		}

		public static string Convert24HourTo12Hour(string inputTime)
		{
			if (inputTime.ToUpper().Contains("AM") || inputTime.ToUpper().Contains("PM"))
				return inputTime;

			var time = inputTime.Replace(":", "").Trim();

			string timeOfDay = "AM";
			int hour = 0;
			string minute = "";

			if (time.Length == 3)
			{
				hour = int.Parse(time.Substring(0, 1));
				minute = time.Substring(1, 2);
			}
			else if (time.Length == 4)
			{
				hour = int.Parse(time.Substring(0, 2));
				minute = time.Substring(2, 2);
			}

			if (hour > 12)
			{
				timeOfDay = "PM";
				hour = hour - 12;
			}

			if (minute == "0")
				minute = "00";

			return string.Format("{0}:{1} {2}", hour, minute, timeOfDay);
		}

		public static DateTime ConvertKendoCalDate(string date)
		{
			if (String.IsNullOrWhiteSpace(date))
				throw new ArgumentNullException("date");

			var dateParts = date.Split(char.Parse("/"));

			return new DateTime(int.Parse(dateParts[2]), int.Parse(dateParts[0]), int.Parse(dateParts[1]), 0, 0, 0, DateTimeKind.Local);
		}

		public static DateTime UnixTimeStampToDateTime(double unixTimeStamp)
		{
			// Unix timestamp is seconds past epoch
			DateTime dateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
			dateTime = dateTime.AddSeconds(unixTimeStamp).ToLocalTime();
			return dateTime;
		}
	}

	//http://stackoverflow.com/questions/381401/how-do-you-compare-datetime-objects-using-a-specified-tolerance-in-c
	public class DateTimeWithin
	{
		public DateTimeWithin(DateTime dateTime, TimeSpan tolerance)
		{
			DateTime = dateTime;
			Tolerance = tolerance;
		}

		public TimeSpan Tolerance { get; private set; }
		public DateTime DateTime { get; private set; }

		public static bool operator ==(DateTime lhs, DateTimeWithin rhs)
		{
			return (lhs - rhs.DateTime).Duration() <= rhs.Tolerance;
		}

		public static bool operator !=(DateTime lhs, DateTimeWithin rhs)
		{
			return (lhs - rhs.DateTime).Duration() > rhs.Tolerance;
		}

		public static bool operator ==(DateTimeWithin lhs, DateTime rhs)
		{
			return rhs == lhs;
		}

		public static bool operator !=(DateTimeWithin lhs, DateTime rhs)
		{
			return rhs != lhs;
		}
	}

	public static class DateTimeTolerance
	{
		private static TimeSpan _defaultTolerance = TimeSpan.FromSeconds(10);
		public static void SetDefault(TimeSpan tolerance)
		{
			_defaultTolerance = tolerance;
		}

		public static DateTimeWithin Within(this DateTime dateTime, TimeSpan tolerance)
		{
			return new DateTimeWithin(dateTime, tolerance);
		}

		public static DateTimeWithin Within(this DateTime dateTime)
		{
			return new DateTimeWithin(dateTime, _defaultTolerance);
		}
	}
}
