using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// One period a road closure is in effect, in UTC. A closure may have many (a road closed only at night for a week is
	/// seven windows). A missing end means "until further notice". Stored as JSON on <c>RoadClosures.WindowsJson</c>.
	/// </summary>
	public class RoadClosureWindow
	{
		public DateTime StartsOn { get; set; }

		public DateTime? EndsOn { get; set; }

		public bool Contains(DateTime utc)
		{
			return utc >= StartsOn && (!EndsOn.HasValue || utc < EndsOn.Value);
		}
	}

	public static class RoadClosureWindows
	{
		/// <summary>The most windows one closure may hold (a nightly closure for three months is about 90).</summary>
		public const int MaxWindows = 400;

		/// <summary>
		/// Sorted, de-duplicated windows with UTC kinds; drops windows that end before they start. Overlaps are kept: the
		/// closure is simply in effect while any window is.
		/// </summary>
		public static List<RoadClosureWindow> Normalize(IEnumerable<RoadClosureWindow> windows)
		{
			return (windows ?? Enumerable.Empty<RoadClosureWindow>())
				.Where(w => w != null && w.StartsOn != default(DateTime))
				.Select(w => new RoadClosureWindow
				{
					StartsOn = AsUtc(w.StartsOn),
					EndsOn = w.EndsOn.HasValue ? AsUtc(w.EndsOn.Value) : (DateTime?)null
				})
				.Where(w => !w.EndsOn.HasValue || w.EndsOn.Value > w.StartsOn)
				.GroupBy(w => (w.StartsOn, w.EndsOn))
				.Select(g => g.First())
				.OrderBy(w => w.StartsOn)
				.ThenBy(w => w.EndsOn ?? DateTime.MaxValue)
				.Take(MaxWindows)
				.ToList();
		}

		/// <summary>
		/// Where a closure stands at <paramref name="utc"/>. With no windows an enabled closure is in effect until it is
		/// switched off or deleted.
		/// </summary>
		public static RoadClosureStates StateAt(bool isEnabled, IReadOnlyCollection<RoadClosureWindow> windows, DateTime utc)
		{
			if (!isEnabled)
				return RoadClosureStates.Disabled;

			if (windows == null || windows.Count == 0)
				return RoadClosureStates.Active;

			if (windows.Any(w => w.Contains(utc)))
				return RoadClosureStates.Active;

			return windows.Any(w => w.StartsOn > utc) ? RoadClosureStates.Scheduled : RoadClosureStates.Ended;
		}

		/// <summary>The window running at <paramref name="utc"/>, else the next one to start; null when none is left.</summary>
		public static RoadClosureWindow CurrentOrNext(IReadOnlyCollection<RoadClosureWindow> windows, DateTime utc)
		{
			if (windows == null)
				return null;

			return windows.Where(w => w.Contains(utc)).OrderBy(w => w.EndsOn ?? DateTime.MaxValue).FirstOrDefault()
				?? windows.Where(w => w.StartsOn > utc).OrderBy(w => w.StartsOn).FirstOrDefault();
		}

		/// <summary>
		/// Daily windows between two local dates: for each date from <paramref name="firstDate"/> to <paramref name="lastDate"/>
		/// whose weekday is in <paramref name="days"/> (empty = every day), a window from <paramref name="dailyStart"/> to
		/// <paramref name="dailyEnd"/> local time. An end at or before the start runs past midnight into the next day
		/// (22:00–06:00). Each local time is converted with <paramref name="localToUtc"/> (the department's zone), so a window
		/// keeps its wall-clock hours across a daylight-saving change.
		/// </summary>
		public static List<RoadClosureWindow> Repeat(DateTime firstDate, DateTime lastDate, TimeSpan dailyStart, TimeSpan dailyEnd,
			IReadOnlyCollection<DayOfWeek> days, Func<DateTime, DateTime> localToUtc)
		{
			var windows = new List<RoadClosureWindow>();
			if (lastDate.Date < firstDate.Date)
				return windows;

			var toUtc = localToUtc ?? (local => DateTime.SpecifyKind(local, DateTimeKind.Utc));
			var overnight = dailyEnd <= dailyStart;

			for (var date = firstDate.Date; date <= lastDate.Date && windows.Count < MaxWindows; date = date.AddDays(1))
			{
				if (days != null && days.Count > 0 && !days.Contains(date.DayOfWeek))
					continue;

				var localStart = DateTime.SpecifyKind(date + dailyStart, DateTimeKind.Unspecified);
				var localEnd = DateTime.SpecifyKind((overnight ? date.AddDays(1) : date) + dailyEnd, DateTimeKind.Unspecified);

				windows.Add(new RoadClosureWindow { StartsOn = DateTime.SpecifyKind(toUtc(localStart), DateTimeKind.Utc), EndsOn = DateTime.SpecifyKind(toUtc(localEnd), DateTimeKind.Utc) });
			}

			return windows;
		}

		private static DateTime AsUtc(DateTime value)
		{
			return value.Kind == DateTimeKind.Utc ? value : value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
		}
	}
}
