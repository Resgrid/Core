using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface ICallDispatchUnitRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.CallDispatchUnit}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.CallDispatchUnit}" />
	public interface ICallDispatchUnitRepository: IRepository<CallDispatchUnit>
	{
		/// <summary>
		/// Gets the call unit dispatches by call identifier asynchronous.
		/// </summary>
		/// <param name="callId">The call identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;CallDispatchUnit&gt;&gt;.</returns>
		Task<IEnumerable<CallDispatchUnit>> GetCallUnitDispatchesByCallIdAsync(int callId);

		/// <summary>
		/// Gets the ids of the department's open (active, not deleted) calls the unit is dispatched to.
		/// </summary>
		Task<IEnumerable<int>> GetOpenCallIdsForUnitAsync(int departmentId, int unitId);

		/// <summary>
		/// Gets the unit dispatches of every department call logged in the range (UTC, inclusive).
		/// </summary>
		Task<IEnumerable<CallDispatchUnit>> GetCallUnitDispatchesForCallsInRangeAsync(int departmentId, DateTime startDate, DateTime endDate);

		/// <summary>
		/// Gets the unit dispatches made in the range (UTC, inclusive), with each call's logged and closed times, on department
		/// calls logged from <paramref name="loggedFrom"/>.
		/// </summary>
		Task<IEnumerable<CallDispatchWindow>> GetUnitDispatchWindowsAsync(int departmentId, DateTime startDate, DateTime endDate, DateTime loggedFrom);
	}
}
