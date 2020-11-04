using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IShiftGroupAssignmentsRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.ShiftGroupAssignment}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.ShiftGroupAssignment}" />
	public interface IShiftGroupAssignmentsRepository: IRepository<ShiftGroupAssignment>
	{
		/// <summary>
		/// Gets the shift assignments by group identifier asynchronous.
		/// </summary>
		/// <param name="shiftGroupId">The shift group identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;ShiftGroupAssignment&gt;&gt;.</returns>
		Task<IEnumerable<ShiftGroupAssignment>> GetShiftAssignmentsByGroupIdAsync(int shiftGroupId);
	}
}
