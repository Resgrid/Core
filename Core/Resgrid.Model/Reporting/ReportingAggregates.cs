using System;

namespace Resgrid.Model.Reporting
{
	// Lightweight projection types that aggregate SQL queries land into (Dapper). These are
	// repository-facing only and are not part of the public dashboard contract. They deliberately
	// carry ONLY aggregates (never materialized rows) so latency is independent of row/tenant count.

	// Aliases use GroupKey/Bucket/Total (not Key/Count) to avoid reserved-word collisions across
	// both SQL Server and PostgreSQL. Dapper maps result columns to these properties by name.

	/// <summary>An integer-keyed grouped count (e.g. by priority, by call state, by raw status id).</summary>
	public class CountByKeyResult
	{
		public int GroupKey { get; set; }
		public long Total { get; set; }
	}

	/// <summary>A string-keyed grouped count (e.g. calls grouped by their <c>Type</c> string).</summary>
	public class CountByStringKeyResult
	{
		public string GroupKey { get; set; }
		public long Total { get; set; }
	}

	/// <summary>A time-bucketed grouped count. <see cref="Bucket"/> is the start of the day/month (UTC).</summary>
	public class CountByDateBucketResult
	{
		public DateTime Bucket { get; set; }
		public long Total { get; set; }
	}
}
