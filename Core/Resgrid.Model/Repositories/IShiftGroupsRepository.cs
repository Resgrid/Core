using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IShiftGroupsRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.ShiftGroup}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.ShiftGroup}" />
	public interface IShiftGroupsRepository: IRepository<ShiftGroup>
	{
		/// <summary>
		/// Gets the shift staffing by shift day asynchronous.
		/// </summary>
		/// <param name="departmentGroupId">The department group identifier.</param>
		/// <returns>Task&lt;ShiftGroup&gt;.</returns>
		Task<IEnumerable<ShiftGroup>> GetShiftGroupsByGroupIdAsync(int departmentGroupId);

		/// <summary>
		/// Gets the shift groups by shift identifier asynchronous.
		/// </summary>
		/// <param name="shiftId">The shift identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;ShiftGroup&gt;&gt;.</returns>
		Task<IEnumerable<ShiftGroup>> GetShiftGroupsByShiftIdAsync(int shiftId);
	}
}
