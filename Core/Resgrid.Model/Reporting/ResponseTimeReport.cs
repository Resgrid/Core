using System;
using System.Collections.Generic;
using ProtoBuf;

namespace Resgrid.Model.Reporting
{
	/// <summary>
	/// Response-time analytics for the call lifecycle, reported as 90th-percentile (the NFPA standard)
	/// plus mean, with compliance against configurable NFPA thresholds
	/// (1710 career / 1720 volunteer travel, 1221 dispatch/alarm handling).
	/// </summary>
	/// <remarks>
	/// Cached through <see cref="Providers.ICacheProvider"/> (protobuf-net), so every type in the graph is a
	/// [ProtoContract]. Dates use <see cref="DataFormat.WellKnown"/> so a cache hit keeps <see cref="DateTimeKind.Utc"/>
	/// (the default BCL format drops the kind, which would serialize without the "Z" a cache miss carries).
	/// </remarks>
	[ProtoContract]
	public class ResponseTimeReport
	{
		[ProtoMember(1)]
		public int? DepartmentId { get; set; }

		[ProtoMember(2, DataFormat = DataFormat.WellKnown)]
		public DateTime StartUtc { get; set; }

		[ProtoMember(3, DataFormat = DataFormat.WellKnown)]
		public DateTime EndUtc { get; set; }

		[ProtoMember(4, DataFormat = DataFormat.WellKnown)]
		public DateTime GeneratedUtc { get; set; }

		/// <summary>One entry per lifecycle phase (alarm handling, turnout, travel, total response).</summary>
		[ProtoMember(5)]
		public List<ResponseTimeMetric> Metrics { get; set; } = new List<ResponseTimeMetric>();

		/// <summary>Optional groupings of the total-response metric (e.g. by call type / station).</summary>
		[ProtoMember(6)]
		public List<ResponseTimeBreakdown> Breakdowns { get; set; } = new List<ResponseTimeBreakdown>();
	}

	[ProtoContract]
	public class ResponseTimeMetric
	{
		/// <summary>e.g. "alarmHandling", "turnout", "travel", "totalResponse".</summary>
		[ProtoMember(1)]
		public string Key { get; set; }

		[ProtoMember(2)]
		public double MeanSeconds { get; set; }

		[ProtoMember(3)]
		public double P50Seconds { get; set; }

		[ProtoMember(4)]
		public double P90Seconds { get; set; }

		[ProtoMember(5)]
		public long SampleCount { get; set; }

		/// <summary>Configured NFPA threshold for this phase, if any.</summary>
		[ProtoMember(6)]
		public double? ThresholdSeconds { get; set; }

		/// <summary>Percent of samples within <see cref="ThresholdSeconds"/>, if a threshold is set.</summary>
		[ProtoMember(7)]
		public double? CompliancePercent { get; set; }
	}

	[ProtoContract]
	public class ResponseTimeBreakdown
	{
		/// <summary>e.g. "byCallType", "byStation".</summary>
		[ProtoMember(1)]
		public string Key { get; set; }

		[ProtoMember(2)]
		public List<ResponseTimeBreakdownItem> Items { get; set; } = new List<ResponseTimeBreakdownItem>();
	}

	[ProtoContract]
	public class ResponseTimeBreakdownItem
	{
		[ProtoMember(1)]
		public string Label { get; set; }

		[ProtoMember(2)]
		public int? Id { get; set; }

		[ProtoMember(3)]
		public double P90Seconds { get; set; }

		[ProtoMember(4)]
		public long SampleCount { get; set; }
	}
}
