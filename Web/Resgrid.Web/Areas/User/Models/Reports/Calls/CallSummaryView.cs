using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Resgrid.Model;
using Resgrid.Model.Reporting;

namespace Resgrid.Web.Areas.User.Models.Reports.Calls
{
	public class CallSummaryView
	{
		public DateTime RunOn { get; set; }
		public Department Department { get; set; }
		public DateTime Start { get; set; }
		public DateTime End { get; set; }

		/// <summary>The group the report is filtered to (with the groups beneath it); null for the whole department.</summary>
		public int? GroupId { get; set; }
		public string GroupName { get; set; }

		/// <summary>Calls per call type, most first.</summary>
		public List<Tuple<string, int>> CallTypeCount { get; set; }

		/// <summary>Calls per <see cref="CallStates"/> value.</summary>
		public List<Tuple<int, int>> CallStateCount { get; set; }

		/// <summary>Calls per assigned group, largest first; calls no group could be decided for count under "unassigned".</summary>
		public List<Tuple<string, int>> CallGroupCount { get; set; } = new List<Tuple<string, int>>();

		public int TotalCalls { get; set; }

		public CallSummaryStats Overall { get; set; } = new CallSummaryStats();

		/// <summary>Every call type in the report, most calls first; chart colors follow this order.</summary>
		public List<string> TypeOrder { get; set; } = new List<string>();

		/// <summary>Monthly, quarterly and yearly breakdowns, only those the range calls for, yearly first.</summary>
		public List<CallSummaryPeriodSection> PeriodSections { get; set; } = new List<CallSummaryPeriodSection>();

		public List<CallSummary> CallSummaries { get; set; }
	}

	public class CallSummary
	{
		public int CallId { get; set; }
		public string Number { get; set; }
		public string Name { get; set; }
		public string Type { get; set; }
		public DateTime LoggedOn { get; set; }
		public DateTime? ClosedOn { get; set; }
		public DateTime? FirstOnSceneTime { get; set; }
		public int UnitsCount { get; set; }
		public int PersonnelCount { get; set; }

		/// <summary>The call's logged time in department-local time, for placing it in a month, quarter or year.</summary>
		public DateTime LoggedOnLocal { get; set; }
		public string GroupName { get; set; }
		public CallGroupAssignmentMethods GroupMethod { get; set; }

		public string GetCallLength()
		{
			if (ClosedOn.HasValue)
				return (ClosedOn.Value - LoggedOn).ToString(@"d\d\:h\h\:m\m\:s\s", System.Globalization.CultureInfo.InvariantCulture);

			return "Open";
		}

		public string GetOnSceneTime()
		{
			if (FirstOnSceneTime.HasValue)
				return (FirstOnSceneTime.Value - LoggedOn).ToString(@"d\d\:h\h\:m\m\:s\s", System.Globalization.CultureInfo.InvariantCulture);

			return "N/A";
		}
	}

	/// <summary>Totals and averages over a set of calls.</summary>
	public class CallSummaryStats
	{
		public int Calls { get; set; }
		public int Closed { get; set; }
		public TimeSpan? AverageOnScene { get; set; }
		public TimeSpan? AverageCallLength { get; set; }
		public int UnitResponses { get; set; }
		public int PersonnelResponses { get; set; }

		public static CallSummaryStats From(IReadOnlyCollection<CallSummary> calls)
		{
			var stats = new CallSummaryStats { Calls = calls.Count };

			var lengths = calls.Where(c => c.ClosedOn.HasValue && c.ClosedOn.Value >= c.LoggedOn)
				.Select(c => (c.ClosedOn.Value - c.LoggedOn).Ticks).ToList();
			var onScene = calls.Where(c => c.FirstOnSceneTime.HasValue && c.FirstOnSceneTime.Value >= c.LoggedOn)
				.Select(c => (c.FirstOnSceneTime.Value - c.LoggedOn).Ticks).ToList();

			stats.Closed = calls.Count(c => c.ClosedOn.HasValue);
			stats.AverageCallLength = lengths.Count > 0 ? TimeSpan.FromTicks((long)lengths.Average()) : (TimeSpan?)null;
			stats.AverageOnScene = onScene.Count > 0 ? TimeSpan.FromTicks((long)onScene.Average()) : (TimeSpan?)null;
			stats.UnitResponses = calls.Sum(c => c.UnitsCount);
			stats.PersonnelResponses = calls.Sum(c => c.PersonnelCount);

			return stats;
		}

		public int Open => Calls - Closed;

		/// <summary>An average for a summary cell: "8m 05s", "1h 26m", "2d 3h".</summary>
		public static string Format(TimeSpan? value)
		{
			if (!value.HasValue)
				return "N/A";

			var span = value.Value;
			if (span.TotalDays >= 1)
				return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}d {1}h", (int)span.TotalDays, span.Hours);

			if (span.TotalHours >= 1)
				return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}h {1:00}m", (int)span.TotalHours, span.Minutes);

			return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}m {1:00}s", (int)span.TotalMinutes, span.Seconds);
		}
	}

	/// <summary>One breakdown of the report range: its months, quarters or years.</summary>
	public class CallSummaryPeriodSection
	{
		public ReportPeriodGranularity Granularity { get; set; }
		public List<CallSummaryPeriod> Periods { get; set; } = new List<CallSummaryPeriod>();
	}

	public class CallSummaryPeriod
	{
		public ReportPeriod Period { get; set; }
		public string Label { get; set; }
		public CallSummaryStats Stats { get; set; }

		/// <summary>Calls from the start of the period's calendar year (or of the report, if later) through the period.</summary>
		public int YearToDate { get; set; }

		/// <summary>Calls per call type in the period; types with none are left out.</summary>
		public Dictionary<string, int> TypeCounts { get; set; } = new Dictionary<string, int>();

		/// <summary>The department's defined call types with no call in the period.</summary>
		public List<string> TypesWithNoCalls { get; set; } = new List<string>();
	}
}
