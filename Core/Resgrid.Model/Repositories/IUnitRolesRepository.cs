using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IUnitRolesRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.UnitRole}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.UnitRole}" />
	public interface IUnitRolesRepository: IRepository<UnitRole>
	{
		/// <summary>
		/// Gets all logs by unit identifier asynchronous.
		/// </summary>
		/// <param name="unitId">The unit identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;UnitRole&gt;&gt;.</returns>
		Task<IEnumerable<UnitRole>> GetAllRolesByUnitIdAsync(int unitId);
	}
}
