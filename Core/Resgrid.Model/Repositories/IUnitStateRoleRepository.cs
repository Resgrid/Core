using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IUnitStateRoleRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.UnitStateRole}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.UnitStateRole}" />
	public interface IUnitStateRoleRepository: IRepository<UnitStateRole>
	{
		/// <summary>
		/// Gets the current roles for unit asynchronous.
		/// </summary>
		/// <param name="unitId">The unit identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;UnitStateRole&gt;&gt;.</returns>
		Task<IEnumerable<UnitStateRole>> GetCurrentRolesForUnitAsync(int unitId);
	}
}
