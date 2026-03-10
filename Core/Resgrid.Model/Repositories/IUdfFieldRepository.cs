using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Repository for <see cref="UdfField"/> entities.
	/// </summary>
	public interface IUdfFieldRepository : IRepository<UdfField>
	{
		/// <summary>
		/// Gets all fields belonging to the specified UDF definition, ordered by SortOrder.
		/// </summary>
		Task<IEnumerable<UdfField>> GetFieldsByDefinitionIdAsync(string definitionId);
	}
}

