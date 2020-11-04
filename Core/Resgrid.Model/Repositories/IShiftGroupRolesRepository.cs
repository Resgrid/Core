using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IShiftGroupRolesRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.ShiftGroupRole}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.ShiftGroupRole}" />
	public interface IShiftGroupRolesRepository: IRepository<ShiftGroupRole>
	{
		/// <summary>
		/// Gets the shift group roles by group identifier asynchronous.
		/// </summary>
		/// <param name="shiftGroupId">The shift group identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;ShiftGroupRole&gt;&gt;.</returns>
		Task<IEnumerable<ShiftGroupRole>> GetShiftGroupRolesByGroupIdAsync(int shiftGroupId);
	}
}
