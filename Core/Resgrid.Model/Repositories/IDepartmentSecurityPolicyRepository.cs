using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Repository for <see cref="DepartmentSecurityPolicy"/> entities.
	/// </summary>
	public interface IDepartmentSecurityPolicyRepository : IRepository<DepartmentSecurityPolicy>
	{
		/// <summary>Returns the security policy for a given department, or null if none is configured.</summary>
		Task<DepartmentSecurityPolicy> GetByDepartmentIdAsync(int departmentId);

		/// <summary>
		/// The stored policy read inside the caller's unit-of-work transaction with an update lock, so two concurrent
		/// changes to one department's policy are compared and versioned one after the other. Requires an open transaction.
		/// </summary>
		Task<DepartmentSecurityPolicy> GetByDepartmentIdForUpdateAsync(int departmentId, System.Threading.CancellationToken cancellationToken = default);

		/// <summary>Advances MfaPolicyVersion by one in the caller's transaction and returns the new value.</summary>
		Task<long> IncrementMfaPolicyVersionAsync(int departmentId, System.Threading.CancellationToken cancellationToken = default);
	}
}

