using System;
using ProtoBuf;

namespace Resgrid.Model.Reporting
{
	/// <summary>
	/// A single point in a time-bucketed metric series. <see cref="BucketUtc"/> is the start of the
	/// day or month (UTC). Series are dense: every bucket in the requested window is present, with
	/// <see cref="Value"/> = 0 for buckets that had no data.
	/// </summary>
	[ProtoContract]
	public class MetricPoint
	{
		[ProtoMember(1, DataFormat = DataFormat.WellKnown)]
		public DateTime BucketUtc { get; set; }

		[ProtoMember(2)]
		public long Value { get; set; }

		public MetricPoint() { }

		public MetricPoint(DateTime bucketUtc, long value)
		{
			BucketUtc = bucketUtc;
			Value = value;
		}
	}
}
