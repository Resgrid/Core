using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Repository for <see cref="UdfFieldValue"/> entities.
	/// </summary>
	public interface IUdfFieldValueRepository : IRepository<UdfFieldValue>
	{
		/// <summary>
		/// Gets all field values for a specific entity pinned to a given definition version.
		/// </summary>
		Task<IEnumerable<UdfFieldValue>> GetFieldValuesByEntityAsync(int entityType, string entityId, string definitionId);

		/// <summary>
		/// Deletes all field values for an entity under a specific definition version.
		/// Used when re-saving values to replace the set cleanly.
		/// </summary>
		Task<bool> DeleteFieldValuesByEntityAndDefinitionAsync(int entityType, string entityId, string definitionId, CancellationToken cancellationToken);
	}
}

