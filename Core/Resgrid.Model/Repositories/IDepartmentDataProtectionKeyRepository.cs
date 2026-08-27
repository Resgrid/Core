using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IDepartmentDataProtectionKeyRepository : IRepository<DepartmentDataProtectionKey>
	{
		/// <summary>The single Active key version for the department, or null when none is provisioned.</summary>
		Task<DepartmentDataProtectionKey> GetActiveByDepartmentIdAsync(int departmentId);

		/// <summary>Resolves the key row an rgdp envelope references by its department key version.</summary>
		Task<DepartmentDataProtectionKey> GetByDepartmentAndVersionAsync(int departmentId, int version);

		/// <summary>All key versions for the department, newest version first.</summary>
		Task<IReadOnlyList<DepartmentDataProtectionKey>> GetAllVersionsByDepartmentIdAsync(int departmentId);
	}
}
