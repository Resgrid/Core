using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Department certification-type catalog. Phase D (plan D3) adds the code lookups and the active-by-scope read;
	/// GetAllByDepartmentIdAsync keeps returning every row including soft-deleted ones, so callers filter IsDeleted.
	/// </summary>
	public interface IDepartmentCertificationTypeRepository : IRepository<DepartmentCertificationType>
	{
		Task<DepartmentCertificationType> GetByCodeAsync(int departmentId, string code);
		Task<IEnumerable<DepartmentCertificationType>> GetByCodesAsync(int departmentId, IEnumerable<string> codes);
		/// <summary>Active, non-deleted types for the department, optionally narrowed to one scope (<see cref="CertificationAppliesTo"/>).</summary>
		Task<IEnumerable<DepartmentCertificationType>> GetActiveForDepartmentAsync(int departmentId, int? appliesTo = null);
	}
}
