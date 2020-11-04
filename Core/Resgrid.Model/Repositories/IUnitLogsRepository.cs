using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IUnitLogsRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.UnitLog}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.UnitLog}" />
	public interface IUnitLogsRepository: IRepository<UnitLog>
	{
		/// <summary>
		/// Gets all logs by unit identifier asynchronous.
		/// </summary>
		/// <param name="unitId">The unit identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;UnitLog&gt;&gt;.</returns>
		Task<IEnumerable<UnitLog>> GetAllLogsByUnitIdAsync(int unitId);
	}
}
