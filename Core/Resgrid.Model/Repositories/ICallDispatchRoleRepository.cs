using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface ICallDispatchRoleRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.CallDispatchRole}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.CallDispatchRole}" />
	public interface ICallDispatchRoleRepository: IRepository<CallDispatchRole>
	{
		/// <summary>
		/// Gets the call role dispatches by call identifier asynchronous.
		/// </summary>
		/// <param name="callId">The call identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;CallDispatchRole&gt;&gt;.</returns>
		Task<IEnumerable<CallDispatchRole>> GetCallRoleDispatchesByCallIdAsync(int callId);

		/// <summary>
		/// Gets the role dispatches of every department call logged in the range (UTC, inclusive).
		/// </summary>
		Task<IEnumerable<CallDispatchRole>> GetCallDispatchRolesForCallsInRangeAsync(int departmentId, System.DateTime startDate, System.DateTime endDate);
	}
}
