using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IUnitActiveRolesRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.UnitActiveRole}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.UnitActiveRole}" />
	public interface IUnitActiveRolesRepository : IRepository<UnitActiveRole>
	{
		/// <summary>
		/// Gets all logs by unit identifier asynchronous.
		/// </summary>
		/// <param name="unitId">The unit identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;UnitRole&gt;&gt;.</returns>
		Task<IEnumerable<UnitActiveRole>> GetActiveRolesByUnitIdAsync(int unitId);

		Task<bool> DeleteActiveRolesByUnitIdAsync(int unitId, CancellationToken cancellationToken);

		Task<IEnumerable<UnitActiveRole>> GetAllActiveRolesForUnitsByDepartmentIdAsync(int departmentId);
	}
}
