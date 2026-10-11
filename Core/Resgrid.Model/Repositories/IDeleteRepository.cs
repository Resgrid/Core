using Resgrid.Model.Identity;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IDeleteRepository
	/// </summary>
	public interface IDeleteRepository
	{
		/// <summary>
		/// Deletes a Department
		/// </summary>
		/// <returns>Boolean, true if successful</returns>
		Task<bool> DeleteDepartmentAndUsersAsync(int departmentId);

		/// <summary>
		/// Departments that no longer exist but still own rows in the tables department deletion purges without a foreign key
		/// to Departments: departments deleted before those tables were purged. RMS rows keep their own sweep.
		/// </summary>
		Task<List<int>> GetDeletedDepartmentIdsWithLeftoversAsync(CancellationToken cancellationToken = default);

		/// <summary>Purges one deleted department's leftover rows in a single transaction, unless a legal hold or a running ADP
		/// migration keeps them.</summary>
		Task<DeletedDepartmentPurgeOutcome> PurgeDeletedDepartmentLeftoversAsync(int departmentId, CancellationToken cancellationToken = default);
	}
}
