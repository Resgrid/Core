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
	}
}

