using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// A department's reusable response packages (conditional run cards): named requirement sets that decision graph
	/// outcomes and fixed alarm levels reference, with their rows and "any of" targets.
	/// </summary>
	public interface IResponsePackagesService
	{
		/// <summary>Every package of the department with its rows and targets, retired ones included. Cached.</summary>
		Task<List<ResponsePackage>> GetPackagesForDepartmentAsync(int departmentId, bool bypassCache = false);

		/// <summary>One package of the department, or null when it is not the department's.</summary>
		Task<ResponsePackage> GetPackageByIdAsync(int departmentId, int responsePackageId, bool bypassCache = false);

		/// <summary>
		/// Saves the package header and replaces its rows and targets to match. Throws ArgumentException with the problems
		/// (an id the department does not own, a row with no targets, a substitute pointing at the package itself).
		/// </summary>
		Task<ResponsePackage> SavePackageAsync(ResponsePackage package, string userId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Where the package is used: run card levels, decision outcomes, and other packages' substitute rows.</summary>
		Task<List<string>> GetPackageUsageAsync(int departmentId, int responsePackageId);

		/// <summary>
		/// Deletes an unused package. Throws InvalidOperationException listing where it is used, because removing a package a
		/// card relies on would silently stop that card from proposing anything.
		/// </summary>
		Task<bool> DeletePackageAsync(int departmentId, int responsePackageId, CancellationToken cancellationToken = default(CancellationToken));

		Task InvalidatePackagesCacheAsync(int departmentId);
	}
}
