using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Repository for <see cref="UdfDefinition"/> entities.
	/// </summary>
	public interface IUdfDefinitionRepository : IRepository<UdfDefinition>
	{
		/// <summary>
		/// Gets the currently active UDF definition for the given department and entity type.
		/// </summary>
		Task<UdfDefinition> GetActiveDefinitionByDepartmentAndEntityTypeAsync(int departmentId, int entityType);

		/// <summary>
		/// Gets all versions of UDF definitions for a department and entity type (active and historical).
		/// </summary>
		Task<IEnumerable<UdfDefinition>> GetAllDefinitionVersionsByDepartmentAndEntityTypeAsync(int departmentId, int entityType);

		/// <summary>
		/// Marks all active definitions for the given department + entity type as inactive.
		/// Called before inserting a new version.
		/// </summary>
		Task<bool> DeactivateDefinitionsByDepartmentAndEntityTypeAsync(int departmentId, int entityType, CancellationToken cancellationToken);
	}
}

