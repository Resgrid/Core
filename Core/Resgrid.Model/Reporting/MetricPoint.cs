using System;

namespace Resgrid.Model.Reporting
{
	/// <summary>
	/// A single point in a time-bucketed metric series. <see cref="BucketUtc"/> is the start of the
	/// day or month (UTC). Series are dense: every bucket in the requested window is present, with
	/// <see cref="Value"/> = 0 for buckets that had no data.
	/// </summary>
	public class MetricPoint
	{
		public DateTime BucketUtc { get; set; }

		public long Value { get; set; }

		public MetricPoint() { }

		public MetricPoint(DateTime bucketUtc, long value)
		{
			BucketUtc = bucketUtc;
			Value = value;
		}
	}
}
