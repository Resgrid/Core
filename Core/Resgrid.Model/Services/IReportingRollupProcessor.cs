using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Computes the pre-aggregated daily reporting rollups (response times, volume, utilization,
	/// participation) and writes them to the rollup store. Run as a nightly background job and via the
	/// admin backfill command. Because it is a batch job (not the latency-bounded request path) it may
	/// iterate departments and materialize a day's rows to compute percentiles.
	/// </summary>
	public interface IReportingRollupProcessor
	{
		/// <summary>Computes and upserts the rollup rows for one department for one UTC day.</summary>
		Task<int> RunDailyRollupForDepartmentAsync(int departmentId, DateTime dayUtc, CancellationToken cancellationToken = default);

		/// <summary>
		/// Computes per-department rollups for every supplied department for one UTC day, plus a
		/// system-wide (DepartmentId null) aggregate row from the combined samples.
		/// </summary>
		Task<int> RunDailyRollupForAllAsync(DateTime dayUtc, IEnumerable<int> departmentIds, CancellationToken cancellationToken = default);
	}
}
