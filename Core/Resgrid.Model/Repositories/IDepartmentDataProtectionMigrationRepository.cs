using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IDepartmentDataProtectionMigrationRepository : IRepository<DepartmentDataProtectionMigration>
	{
		/// <summary>All cursor rows for the department's in-flight run of the given kind (CompletedOn null).</summary>
		Task<IReadOnlyList<DepartmentDataProtectionMigration>> GetActiveByDepartmentIdAsync(int departmentId,
			DepartmentDataProtectionMigrationKind kind);

		/// <summary>The department's cursor row for one table of an in-flight run, or null.</summary>
		Task<DepartmentDataProtectionMigration> GetActiveByDepartmentAndTableAsync(int departmentId,
			DepartmentDataProtectionMigrationKind kind, string targetTable);
	}
}
