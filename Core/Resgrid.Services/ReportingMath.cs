using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Services
{
	/// <summary>
	/// Pure, dependency-free aggregation helpers for reporting (percentiles, mean). Kept separate from
	/// the data layer so it is fully unit-testable. Percentiles use linear interpolation between the
	/// closest ranks (the same method as SQL <c>PERCENTILE_CONT</c>).
	/// </summary>
	public static class ReportingMath
	{
		/// <summary>
		/// Computes count, sum, min, max, P50 and P90 over the samples. Returns a zeroed summary for an
		/// empty/null input. <paramref name="samples"/> is materialized and sorted internally.
		/// </summary>
		public static SampleSummary Summarize(IEnumerable<double> samples)
		{
			var sorted = (samples ?? Enumerable.Empty<double>()).OrderBy(x => x).ToList();
			if (sorted.Count == 0)
				return new SampleSummary();

			return new SampleSummary
			{
				Count = sorted.Count,
				Sum = sorted.Sum(),
				Min = sorted[0],
				Max = sorted[sorted.Count - 1],
				P50 = PercentileSorted(sorted, 0.50),
				P90 = PercentileSorted(sorted, 0.90)
			};
		}

		/// <summary>Percentile (0..1) using linear interpolation; <paramref name="sorted"/> must be ascending.</summary>
		public static double PercentileSorted(IReadOnlyList<double> sorted, double percentile)
		{
			if (sorted == null || sorted.Count == 0)
				return 0d;
			if (sorted.Count == 1)
				return sorted[0];

			percentile = Math.Min(1d, Math.Max(0d, percentile));
			var rank = percentile * (sorted.Count - 1);
			var low = (int)Math.Floor(rank);
			var high = (int)Math.Ceiling(rank);
			if (low == high)
				return sorted[low];

			var weight = rank - low;
			return sorted[low] + (sorted[high] - sorted[low]) * weight;
		}

		/// <summary>
		/// Computes committed vs. total in-service seconds from an ascending sequence of classified unit
		/// states. Each state runs until the next state's timestamp (the last runs to
		/// <paramref name="windowEndUtc"/>). Total in-service time starts at the first state. Pure and
		/// unit-testable; the caller pre-classifies each state's committed flag via the availability matrix.
		/// </summary>
		public static (double committedSeconds, double totalSeconds) UtilizationSeconds(
			IReadOnlyList<(DateTime Timestamp, bool Committed)> orderedStates, DateTime windowEndUtc)
		{
			if (orderedStates == null || orderedStates.Count == 0)
				return (0d, 0d);

			double committed = 0d, total = 0d;
			for (var i = 0; i < orderedStates.Count; i++)
			{
				var start = orderedStates[i].Timestamp;
				var end = i + 1 < orderedStates.Count ? orderedStates[i + 1].Timestamp : windowEndUtc;
				if (end <= start)
					continue;

				var duration = (end - start).TotalSeconds;
				total += duration;
				if (orderedStates[i].Committed)
					committed += duration;
			}

			return (committed, total);
		}

		public class SampleSummary
		{
			public long Count { get; set; }
			public double Sum { get; set; }
			public double Min { get; set; }
			public double Max { get; set; }
			public double P50 { get; set; }
			public double P90 { get; set; }

			public double Mean => Count > 0 ? Sum / Count : 0d;
		}
	}
}
