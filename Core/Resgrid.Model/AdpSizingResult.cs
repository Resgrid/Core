using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// Result of the read-only ADP sizing scan (plan section 18.2): per-table row counts and a
	/// P50–P90 duration range with the projected number of overnight windows — never a single
	/// false-precision number. Contains counts only; the scan touches no plaintext content.
	/// </summary>
	public sealed class AdpSizingResult
	{
		public int DepartmentId { get; set; }

		public DateTime ScannedOnUtc { get; set; }

		/// <summary>Department-owned rows per cataloged table.</summary>
		public Dictionary<string, long> TableRowCounts { get; set; } = new Dictionary<string, long>();

		public long TotalRows { get; set; }

		/// <summary>Benchmark throughput (rows/second) the estimate was computed with.</summary>
		public int BenchmarkRowsPerSecond { get; set; }

		public int EstimatedP50Minutes { get; set; }

		public int EstimatedP90Minutes { get; set; }

		/// <summary>Projected number of nightly windows at the given window length, from the P90 estimate.</summary>
		public int ProjectedNights { get; set; }
	}
}
