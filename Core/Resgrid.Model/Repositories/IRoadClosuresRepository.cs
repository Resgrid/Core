using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Road closures (RoadClosures, M0314).</summary>
	public interface IRoadClosuresRepository : IRepository<RoadClosure>
	{
		Task<List<RoadClosure>> GetForDepartmentAsync(int departmentId);

		Task<RoadClosure> GetByIdForDepartmentAsync(string roadClosureId, int departmentId);

		/// <summary>
		/// Switches one of the department's closures on or off, writing only IsEnabled, UpdatedByUserId and UpdatedOn (never
		/// the rest of the row). Returns the rows changed: 0 when the closure is not the department's.
		/// </summary>
		Task<int> SetEnabledAsync(int departmentId, string roadClosureId, bool isEnabled, string userId, DateTime updatedOn, CancellationToken cancellationToken);
	}
}
