using System;
using System.Collections.Generic;

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
	public class DashboardReport
	{
		/// <summary>The department this report is scoped to; null = system-wide (BackOffice only).</summary>
		public int? DepartmentId { get; set; }

		public DateTime StartUtc { get; set; }
		public DateTime EndUtc { get; set; }
		public ReportGranularity Granularity { get; set; }

		/// <summary>When this report was generated (UTC).</summary>
		public DateTime GeneratedUtc { get; set; }

		/// <summary>
		/// IANA/Windows timezone of the requesting context (from the caller's claim), provided so a
		/// UI can label/shift the UTC buckets for display. Aggregation itself is always UTC.
		/// </summary>
		public string TimeZone { get; set; }

		public ReportTotals Totals { get; set; } = new ReportTotals();

		public List<MetricSeries> Series { get; set; } = new List<MetricSeries>();

		public List<Breakdown> Breakdowns { get; set; } = new List<Breakdown>();
	}
}
