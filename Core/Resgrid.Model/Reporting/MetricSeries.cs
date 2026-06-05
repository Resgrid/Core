using System.Collections.Generic;

namespace Resgrid.Model.Reporting
{
	/// <summary>
	/// A named, time-bucketed series (e.g. "calls", "messages", "newUsers"). Points are dense
	/// (zero-filled) and ordered ascending by bucket.
	/// </summary>
	public class MetricSeries
	{
		/// <summary>Stable machine key, e.g. "calls", "messages", "newUsers".</summary>
		public string Key { get; set; }

		public ReportGranularity Granularity { get; set; }

		public List<MetricPoint> Points { get; set; } = new List<MetricPoint>();
	}
}
