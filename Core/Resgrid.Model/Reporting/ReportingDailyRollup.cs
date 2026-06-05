using System;

namespace Resgrid.Model.Reporting
{
	/// <summary>
	/// A pre-aggregated daily rollup row backing the heavy analytics (response times, UHU,
	/// participation) so those reports stay fast regardless of source-table size. Written by the
	/// rollup worker and read by the reporting service. A null <see cref="DepartmentId"/> is a
	/// system-wide aggregate row. Maps to the <c>ReportingDailyRollup</c> table.
	/// </summary>
	public class ReportingDailyRollup
	{
		public long ReportingDailyRollupId { get; set; }

		/// <summary>Department this rollup is for; null = system-wide.</summary>
		public int? DepartmentId { get; set; }

		/// <summary>The day this rollup covers (UTC, midnight).</summary>
		public DateTime BucketDateUtc { get; set; }

		/// <summary>What is measured, e.g. "TurnoutSeconds", "Uhu", "ResponseRate".</summary>
		public string Metric { get; set; }

		/// <summary>Optional sub-grouping key (e.g. call type, unit id, station id); null = overall.</summary>
		public string Dimension { get; set; }

		/// <summary>Number of items/events aggregated into this row.</summary>
		public long ItemCount { get; set; }

		/// <summary>Sum of the measured value (mean = SumValue / ItemCount).</summary>
		public decimal? SumValue { get; set; }

		public decimal? MinValue { get; set; }
		public decimal? MaxValue { get; set; }

		/// <summary>50th percentile of the measured value.</summary>
		public decimal? P50 { get; set; }

		/// <summary>90th percentile of the measured value (the NFPA reporting standard).</summary>
		public decimal? P90 { get; set; }

		public DateTime CreatedOnUtc { get; set; }
	}
}
