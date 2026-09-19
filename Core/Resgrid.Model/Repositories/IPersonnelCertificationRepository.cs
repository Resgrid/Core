using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Personnel certification records. The Phase D reads (plan D3) return rows without the file bytes and skip
	/// soft-deleted rows; GetCertificationsByUserAsync is the legacy full read (bytes included, every row).
	/// </summary>
	public interface IPersonnelCertificationRepository : IRepository<PersonnelCertification>
	{
		Task<IEnumerable<PersonnelCertification>> GetCertificationsByUserAsync(string userId);

		/// <summary>Non-deleted records of the department without bytes; optionally narrowed to some users.</summary>
		Task<IEnumerable<PersonnelCertification>> GetForDepartmentAsync(int departmentId, IEnumerable<string> userIds = null);

		/// <summary>Non-deleted typed records of the given types, without bytes.</summary>
		Task<IEnumerable<PersonnelCertification>> GetByTypeIdsAsync(int departmentId, IEnumerable<int> departmentCertificationTypeIds);

		/// <summary>Non-deleted records with an expiry on or before the date, without bytes.</summary>
		Task<IEnumerable<PersonnelCertification>> GetExpiringAsync(int departmentId, DateTime onOrBefore);

		Task<int> CountByTypeIdAsync(int departmentCertificationTypeId);

		/// <summary>Departments holding at least one non-deleted typed record (worker 34 scope).</summary>
		Task<IEnumerable<int>> GetDepartmentIdsWithTypedRecordsAsync();
	}
}
