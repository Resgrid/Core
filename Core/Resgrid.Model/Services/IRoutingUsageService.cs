using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Daily counts of the calls Resgrid makes to each routing provider (Valhalla, Mapbox, Google, OSRM), kept in Redis so
	/// every web, API and worker node adds to the same totals. Shows how much a hosted provider's quota is used and what the
	/// Mapbox fallback costs. The straight-line estimate is not counted.
	/// </summary>
	public interface IRoutingUsageService
	{
		/// <summary>Counts one provider call. Never throws; a cache outage loses the count, not the route.</summary>
		/// <param name="elements">Origin-destination pairs, for a matrix call that was sent.</param>
		/// <param name="departmentAccount">True when the call used a department's own account (billed to the department).</param>
		Task RecordAsync(string provider, RoutingUsageOperation operation, RoutingUsageOutcome outcome, int elements = 0, bool departmentAccount = false);

		/// <summary>
		/// The counted days between the two UTC dates (inclusive), one row per provider and operation with any calls. Throws
		/// <see cref="InvalidOperationException"/> when the cache holding the counters is not reachable or any counter read
		/// fails, rather than reporting the counters it could not read as zero.
		/// </summary>
		Task<List<RoutingUsageDay>> GetUsageAsync(DateTime fromDayUtc, DateTime toDayUtc, CancellationToken cancellationToken = default);
	}
}
