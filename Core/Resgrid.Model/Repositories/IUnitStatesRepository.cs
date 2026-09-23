using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IUnitStatesRepository
	/// Implements the <see cref="UnitState" />
	/// </summary>
	/// <seealso cref="UnitState" />
	public interface IUnitStatesRepository: IRepository<UnitState>
	{
		/// <summary>
		/// Gets all states by unit identifier asynchronous.
		/// </summary>
		/// <param name="unitId">The unit identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;UnitState&gt;&gt;.</returns>
		Task<IEnumerable<UnitState>> GetAllStatesByUnitIdAsync(int unitId);

		/// <summary>
		/// Gets the last unit state by unit identifier asynchronous.
		/// </summary>
		/// <param name="unitId">The unit identifier.</param>
		/// <returns>Task&lt;UnitState&gt;.</returns>
		Task<UnitState> GetLastUnitStateByUnitIdAsync(int unitId);

		/// <summary>
		/// Gets the last unit state before identifier asynchronous.
		/// </summary>
		/// <param name="unitId">The unit identifier.</param>
		/// <param name="unitStateId">The unit state identifier.</param>
		/// <returns>Task&lt;UnitState&gt;.</returns>
		Task<UnitState> GetLastUnitStateBeforeIdAsync(int unitId, int unitStateId);

		/// <summary>
		/// Gets every state of the department's units whose destination is the call: rows explicitly typed as a
		/// call destination plus legacy rows with no destination type. The caller decides how to treat the
		/// untyped rows.
		/// </summary>
		/// <param name="departmentId">The department that owns the call.</param>
		/// <param name="callId">The call identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;UnitState&gt;&gt;.</returns>
		Task<IEnumerable<UnitState>> GetAllStatesByCallIdAsync(int departmentId, int callId);

		/// <summary>
		/// Gets the latest unit states for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;UnitState&gt;&gt;.</returns>
		Task<IEnumerable<UnitState>> GetLatestUnitStatesForDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets the unit state by unit state identifier asynchronous.
		/// </summary>
		/// <param name="unitStateId">The unit state identifier.</param>
		/// <returns>Task&lt;UnitState&gt;.</returns>
		Task<UnitState> GetUnitStateByUnitStateIdAsync(int unitStateId);

		/// <summary>
		/// Gets the unit states by unit identifier and start and end dates asynchronous.
		/// </summary>
		/// <param name="unitId">The unit identifier</param>
		/// <param name="startDate">Start date for the timestamp</param>
		/// <param name="endDate">End date for the timestamp</param>
		/// <returns></returns>
		Task<IEnumerable<UnitState>> GetAllUnitStatesForUnitInDateRangeAsync(int unitId, DateTime startDate,
			DateTime endDate);
	}
}
