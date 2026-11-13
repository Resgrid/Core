using System;
using System.Collections.Generic;
using ProtoBuf;

namespace Resgrid.Model.Reporting
{
	/// <summary>
	/// Composite, read-only dashboard payload returned in a single call. Contains scalar totals,
	/// dense (zero-filled, UTC) time series, and bounded top-N+other breakdowns.
	///
	/// <see cref="DepartmentId"/> is null for a SYSTEM-WIDE (cross-department) report, which is
	/// produced only for the in-process BackOffice (Resgrid staff). Department-scoped HTTP callers
	/// always receive their own department's data.
	///
	/// Contains counts only (no PII), which is what makes cross-tenant aggregation safe.
	/// </summary>
	/// <remarks>
	/// Cached through <see cref="Providers.ICacheProvider"/> (protobuf-net), so every type in the graph is a
	/// [ProtoContract]. Dates use <see cref="DataFormat.WellKnown"/> so a cache hit keeps <see cref="DateTimeKind.Utc"/>.
	/// </remarks>
	[ProtoContract]
	public class DashboardReport
	{
		/// <summary>The department this report is scoped to; null = system-wide (BackOffice only).</summary>
		[ProtoMember(1)]
		public int? DepartmentId { get; set; }

		[ProtoMember(2, DataFormat = DataFormat.WellKnown)]
		public DateTime StartUtc { get; set; }

		[ProtoMember(3, DataFormat = DataFormat.WellKnown)]
		public DateTime EndUtc { get; set; }

		[ProtoMember(4)]
		public ReportGranularity Granularity { get; set; }

		/// <summary>When this report was generated (UTC).</summary>
		[ProtoMember(5, DataFormat = DataFormat.WellKnown)]
		public DateTime GeneratedUtc { get; set; }

		/// <summary>
		/// IANA/Windows timezone of the requesting context (from the caller's claim), provided so a
		/// UI can label/shift the UTC buckets for display. Aggregation itself is always UTC.
		/// </summary>
		/// <remarks>
		/// Deliberately not a [ProtoMember]: it is set per caller after the report is retrieved, and the cache key
		/// does not include it, so caching it would hand one caller's timezone to the next.
		/// </remarks>
		public string TimeZone { get; set; }

		[ProtoMember(6)]
		public ReportTotals Totals { get; set; } = new ReportTotals();

		[ProtoMember(7)]
		public List<MetricSeries> Series { get; set; } = new List<MetricSeries>();

		[ProtoMember(8)]
		public List<Breakdown> Breakdowns { get; set; } = new List<Breakdown>();
	}
}
