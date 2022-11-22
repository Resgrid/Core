using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Interface IUnitsService
	/// </summary>
	public interface IUnitsService
	{
		/// <summary>
		/// Gets all asynchronous.
		/// </summary>
		/// <returns>Task&lt;List&lt;Unit&gt;&gt;.</returns>
		Task<List<Unit>> GetAllAsync();

		/// <summary>
		/// Saves the unit asynchronous.
		/// </summary>
		/// <param name="unit">The unit.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;Unit&gt;.</returns>
		Task<Unit> SaveUnitAsync(Unit unit, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Saves the unit log asynchronous.
		/// </summary>
		/// <param name="unitLog">The unit log.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;UnitLog&gt;.</returns>
		Task<UnitLog> SaveUnitLogAsync(UnitLog unitLog,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the units for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;Unit&gt;&gt;.</returns>
		Task<List<Unit>> GetUnitsForDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets the units for department unlimited asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;Unit&gt;&gt;.</returns>
		Task<List<Unit>> GetUnitsForDepartmentUnlimitedAsync(int departmentId);

		/// <summary>
		/// Gets the unit by identifier asynchronous.
		/// </summary>
		/// <param name="unitId">The unit identifier.</param>
		/// <returns>Task&lt;Unit&gt;.</returns>
		Task<Unit> GetUnitByIdAsync(int unitId);

		/// <summary>
		/// Deletes the unit asynchronous.
		/// </summary>
		/// <param name="unitId">The unit identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteUnitAsync(int unitId, CancellationToken cancellationToken = default(CancellationToken));

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
		/// Gets all latest status for units by department identifier asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;UnitState&gt;&gt;.</returns>
		Task<List<UnitState>> GetAllLatestStatusForUnitsByDepartmentIdAsync(int departmentId);

		/// <summary>
		/// Gets all states for unit asynchronous.
		/// </summary>
		/// <param name="unitId">The unit identifier.</param>
		/// <returns>Task&lt;List&lt;UnitState&gt;&gt;.</returns>
		Task<List<UnitState>> GetAllStatesForUnitAsync(int unitId);

		/// <summary>
		/// Gets the unit state by identifier asynchronous.
		/// </summary>
		/// <param name="unitStateId">The unit state identifier.</param>
		/// <returns>Task&lt;UnitState&gt;.</returns>
		Task<UnitState> GetUnitStateByIdAsync(int unitStateId);

		/// <summary>
		/// Gets the unit types for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;UnitType&gt;&gt;.</returns>
		Task<List<UnitType>> GetUnitTypesForDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets the unit by name department identifier asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="name">The name.</param>
		/// <returns>Task&lt;Unit&gt;.</returns>
		Task<Unit> GetUnitByNameDepartmentIdAsync(int departmentId, string name);

		/// <summary>
		/// Gets the unit type by identifier asynchronous.
		/// </summary>
		/// <param name="unitTypeId">The unit type identifier.</param>
		/// <returns>Task&lt;UnitType&gt;.</returns>
		Task<UnitType> GetUnitTypeByIdAsync(int unitTypeId);

		/// <summary>
		/// Adds the unit type asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="name">The name.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;UnitType&gt;.</returns>
		Task<UnitType> AddUnitTypeAsync(int departmentId, string name, int? mapIconType,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the unit type by name asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="type">The type.</param>
		/// <returns>Task&lt;UnitType&gt;.</returns>
		Task<UnitType> GetUnitTypeByNameAsync(int departmentId, string type);

		/// <summary>
		/// Adds the unit type asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="name">The name.</param>
		/// <param name="customStatesId">The custom states identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;UnitType&gt;.</returns>
		Task<UnitType> AddUnitTypeAsync(int departmentId, string name, int customStatesId, int? mapIconType,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Saves the unit type asynchronous.
		/// </summary>
		/// <param name="unitType">Type of the unit.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;UnitType&gt;.</returns>
		Task<UnitType> SaveUnitTypeAsync(UnitType unitType,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Deletes the unit type asynchronous.
		/// </summary>
		/// <param name="unitTypeId">The unit type identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteUnitTypeAsync(int unitTypeId,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Sets the unit state asynchronous.
		/// </summary>
		/// <param name="unitId">The unit identifier.</param>
		/// <param name="unitStateType">Type of the unit state.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;UnitState&gt;.</returns>
		Task<UnitState> SetUnitStateAsync(int unitId, int unitStateType, int departmentId,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Sets the unit state asynchronous.
		/// </summary>
		/// <param name="state">The state.</param>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;UnitState&gt;.</returns>
		Task<UnitState> SetUnitStateAsync(UnitState state, int departmentId,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the logs for unit asynchronous.
		/// </summary>
		/// <param name="unitId">The unit identifier.</param>
		/// <returns>Task&lt;List&lt;UnitLog&gt;&gt;.</returns>
		Task<List<UnitLog>> GetLogsForUnitAsync(int unitId);

		/// <summary>
		/// Gets the roles for unit asynchronous.
		/// </summary>
		/// <param name="unitId">The unit identifier.</param>
		/// <returns>Task&lt;List&lt;UnitRole&gt;&gt;.</returns>
		Task<List<UnitRole>> GetRolesForUnitAsync(int unitId);

		/// <summary>
		/// Gets the role by identifier asynchronous.
		/// </summary>
		/// <param name="unitRoleId">The unit role identifier.</param>
		/// <returns>Task&lt;UnitRole&gt;.</returns>
		Task<UnitRole> GetRoleByIdAsync(int unitRoleId);

		/// <summary>
		/// Sets the roles for unit asynchronous.
		/// </summary>
		/// <param name="unitId">The unit identifier.</param>
		/// <param name="roles">The roles.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;List&lt;UnitRole&gt;&gt;.</returns>
		Task<List<UnitRole>> SetRolesForUnitAsync(int unitId, List<UnitRole> roles,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Clears the roles for unit asynchronous.
		/// </summary>
		/// <param name="unitId">The unit identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> ClearRolesForUnitAsync(int unitId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Clears the group for units asynchronous.
		/// </summary>
		/// <param name="departmentGroupId">The department group identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> ClearGroupForUnitsAsync(int departmentGroupId,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Adds the unit state role for event asynchronous.
		/// </summary>
		/// <param name="unitStateId">The unit state identifier.</param>
		/// <param name="userId">The user identifier.</param>
		/// <param name="roleId">The role identifier.</param>
		/// <param name="unitName">Name of the unit.</param>
		/// <param name="timestamp">The timestamp.</param>
		/// <param name="roleName">Name of the role.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;UnitStateRole&gt;.</returns>
		Task<UnitStateRole> AddUnitStateRoleForEventAsync(int unitStateId, string userId, int roleId, string unitName,
			DateTime timestamp, string roleName = "Unknown",
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Adds all unit state roles asynchronous.
		/// </summary>
		/// <param name="roles">The roles.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> AddAllUnitStateRolesAsync(List<UnitStateRole> roles,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Deletes the states for unit asynchronous.
		/// </summary>
		/// <param name="unitId">The unit identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteStatesForUnitAsync(int unitId,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets all units for type asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="type">The type.</param>
		/// <returns>Task&lt;List&lt;Unit&gt;&gt;.</returns>
		Task<List<Unit>> GetAllUnitsForTypeAsync(int departmentId, string type);

		/// <summary>
		/// Gets all units for group asynchronous.
		/// </summary>
		/// <param name="groupId">The group identifier.</param>
		/// <returns>Task&lt;List&lt;Unit&gt;&gt;.</returns>
		Task<List<Unit>> GetAllUnitsForGroupAsync(int groupId);

		/// <summary>
		/// Gets the unit states for call asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="callId">The call identifier.</param>
		/// <returns>Task&lt;List&lt;UnitState&gt;&gt;.</returns>
		Task<List<UnitState>> GetUnitStatesForCallAsync(int departmentId, int callId);

		/// <summary>
		/// Adds the unit location asynchronous.
		/// </summary>
		/// <param name="location">The location.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;UnitLocation&gt;.</returns>
		Task<UnitsLocation> AddUnitLocationAsync(UnitsLocation location, int departmentId,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the latest unit location asynchronous.
		/// </summary>
		/// <param name="unitId">The unit identifier.</param>
		/// <param name="timestamp">The timestamp.</param>
		/// <returns>Task&lt;UnitLocation&gt;.</returns>
		Task<UnitsLocation> GetLatestUnitLocationAsync(int unitId, DateTime? timestamp = null);

		/// <summary>
		/// Gets the current roles for unit asynchronous.
		/// </summary>
		/// <param name="unitId">The unit identifier.</param>
		/// <returns>Task&lt;List&lt;UnitStateRole&gt;&gt;.</returns>
		Task<List<UnitStateRole>> GetCurrentRolesForUnitAsync(int unitId);

		/// <summary>
		/// Gets the active roles for unit asynchronous.
		/// </summary>
		/// <param name="unitId">The unit identifier.</param>
		/// <returns>Task&lt;List&lt;UnitActiveRole&gt;&gt;.</returns>
		Task<List<UnitActiveRole>> GetActiveRolesForUnitAsync(int unitId);

		Task<UnitActiveRole> SaveActiveRoleAsync(UnitActiveRole role, CancellationToken cancellationToken = default(CancellationToken));

		Task<bool> DeleteActiveRolesForUnitAsync(int unitId, CancellationToken cancellationToken = default(CancellationToken));

		Task<List<UnitActiveRole>> GetAllActiveRolesForUnitsByDepartmentIdAsync(int departmentId);

		Task<List<UnitsLocation>> GetLatestUnitLocationsAsync(int departmentId);
	}
}
