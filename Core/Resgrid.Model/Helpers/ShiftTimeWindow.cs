using System;
using System.Globalization;

namespace Resgrid.Model.Helpers
{
	/// <summary>
	/// Works out when a shift day actually runs from the shift's StartTime/EndTime strings, in the
	/// department's local time. Shifts store times as free text ("7:00 AM", "19:00"); an overnight
	/// shift ends on the day after its ShiftDay. Pure: no I/O and no department lookup.
	/// </summary>
	public static class ShiftTimeWindow
	{
		private static readonly string[] TimeFormats =
		{
			"h:mm tt", "hh:mm tt", "h:mmtt", "hh:mmtt", "h tt", "htt",
			"H:mm", "HH:mm", "H:mm:ss", "HH:mm:ss", "HHmm"
		};

		/// <summary>Time of day from a shift time string, or null when it can't be read.</summary>
		public static TimeSpan? TryParseTimeOfDay(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;

			var trimmed = value.Trim().ToUpperInvariant();

			if (DateTime.TryParseExact(trimmed, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
				return parsed.TimeOfDay;

			return null;
		}

		/// <summary>
		/// The local [start, end) window of one shift day. A missing or unreadable start time means
		/// midnight (matching the shift notification logic). The end comes from EndTime, rolling to
		/// the next day when it is not after the start; failing that from <paramref name="hours"/>;
		/// failing both the shift is taken to run a full day.
		/// </summary>
		public static (DateTime Start, DateTime End) GetWindow(DateTime shiftDay, string startTime, string endTime, int? hours)
		{
			var start = shiftDay.Date + (TryParseTimeOfDay(startTime) ?? TimeSpan.Zero);
			var endOfDay = TryParseTimeOfDay(endTime);

			DateTime end;
			if (endOfDay.HasValue)
			{
				end = shiftDay.Date + endOfDay.Value;

				if (end <= start)
					end = end.AddDays(1);
			}
			else if (hours.HasValue && hours.Value > 0)
			{
				end = start.AddHours(hours.Value);
			}
			else
			{
				end = start.AddDays(1);
			}

			return (start, end);
		}

		/// <summary>Whether <paramref name="localNow"/> falls inside the shift day's window.</summary>
		public static bool IsActive(DateTime localNow, DateTime shiftDay, string startTime, string endTime, int? hours)
		{
			var window = GetWindow(shiftDay, startTime, endTime, hours);

			return localNow >= window.Start && localNow < window.End;
		}
	}
}
