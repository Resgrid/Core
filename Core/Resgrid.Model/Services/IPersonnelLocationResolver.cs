using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Resolves the freshest usable location per person for a department, merging the
	/// PersonnelLocation document store with ActionLog coordinate fallbacks. Fills the
	/// personnel-side gap next to IUnitLocationSourceResolver.
	/// </summary>
	public interface IPersonnelLocationResolver
	{
		/// <summary>
		/// Latest location per user. Fixes older than <paramref name="maxAgeSeconds"/>
		/// are returned flagged stale (never silently dropped — the caller decides);
		/// 0 = no age limit. Users with no usable fix are absent from the result.
		/// </summary>
		Task<Dictionary<string, ResolvedPersonnelLocation>> GetLatestLocationsAsync(int departmentId, int maxAgeSeconds, DateTime? utcNow = null);
	}
}
