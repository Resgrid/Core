using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Repository for aggregated daily feature flag evaluation counts: one row per (flag, department, day), written by the
	/// usage flush that each host runs over its in-memory counters.
	/// </summary>
	public interface IFeatureFlagUsageRepository : IRepository<FeatureFlagUsage>
	{
		/// <summary>
		/// Adds the usage's counts to the (FeatureFlagId, DepartmentId, UsageDate) row, creating it when absent. Atomic, so
		/// several processes flushing the same day's counters at once add up rather than overwrite or duplicate the row.
		/// </summary>
		Task AddUsageAsync(FeatureFlagUsage usage, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>The flag's usage rows with a UsageDate between the two dates (inclusive), oldest first.</summary>
		Task<IEnumerable<FeatureFlagUsage>> GetUsageForFlagAsync(int featureFlagId, DateTime fromDate, DateTime toDate);
	}
}
