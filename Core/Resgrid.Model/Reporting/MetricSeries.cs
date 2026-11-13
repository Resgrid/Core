using System.Collections.Generic;
using ProtoBuf;

namespace Resgrid.Model.Reporting
{
	/// <summary>
	/// A named, time-bucketed series (e.g. "calls", "messages", "newUsers"). Points are dense
	/// (zero-filled) and ordered ascending by bucket.
	/// </summary>
	[ProtoContract]
	public class MetricSeries
	{
		/// <summary>Stable machine key, e.g. "calls", "messages", "newUsers".</summary>
		[ProtoMember(1)]
		public string Key { get; set; }

		[ProtoMember(2)]
		public ReportGranularity Granularity { get; set; }

		[ProtoMember(3)]
		public List<MetricPoint> Points { get; set; } = new List<MetricPoint>();
	}
}
