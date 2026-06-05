using System;
using System.Collections.Generic;

namespace Resgrid.Model.Reporting
{
	/// <summary>
	/// Response-time analytics for the call lifecycle, reported as 90th-percentile (the NFPA standard)
	/// plus mean, with compliance against configurable NFPA thresholds
	/// (1710 career / 1720 volunteer travel, 1221 dispatch/alarm handling).
	/// </summary>
	public class ResponseTimeReport
	{
		public int? DepartmentId { get; set; }
		public DateTime StartUtc { get; set; }
		public DateTime EndUtc { get; set; }
		public DateTime GeneratedUtc { get; set; }

		/// <summary>One entry per lifecycle phase (alarm handling, turnout, travel, total response).</summary>
		public List<ResponseTimeMetric> Metrics { get; set; } = new List<ResponseTimeMetric>();

		/// <summary>Optional groupings of the total-response metric (e.g. by call type / station).</summary>
		public List<ResponseTimeBreakdown> Breakdowns { get; set; } = new List<ResponseTimeBreakdown>();
	}

	public class ResponseTimeMetric
	{
		/// <summary>e.g. "alarmHandling", "turnout", "travel", "totalResponse".</summary>
		public string Key { get; set; }

		public double MeanSeconds { get; set; }
		public double P50Seconds { get; set; }
		public double P90Seconds { get; set; }
		public long SampleCount { get; set; }

		/// <summary>Configured NFPA threshold for this phase, if any.</summary>
		public double? ThresholdSeconds { get; set; }

		/// <summary>Percent of samples within <see cref="ThresholdSeconds"/>, if a threshold is set.</summary>
		public double? CompliancePercent { get; set; }
	}

	public class ResponseTimeBreakdown
	{
		/// <summary>e.g. "byCallType", "byStation".</summary>
		public string Key { get; set; }

		public List<ResponseTimeBreakdownItem> Items { get; set; } = new List<ResponseTimeBreakdownItem>();
	}

	public class ResponseTimeBreakdownItem
	{
		public string Label { get; set; }
		public int? Id { get; set; }
		public double P90Seconds { get; set; }
		public long SampleCount { get; set; }
	}
}
