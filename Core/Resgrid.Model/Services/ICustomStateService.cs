using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	
	public interface ICustomStateService
	{
		/// <summary>
		/// Gets the custom sate by identifier asynchronous.
		/// </summary>
		/// <param name="customStateId">The custom state identifier.</param>
		/// <returns>Task&lt;CustomState&gt;.</returns>
		Task<CustomState> GetCustomSateByIdAsync(int customStateId);
		/// <summary>
		/// Gets all active custom states for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;CustomState&gt;&gt;.</returns>
		Task<List<CustomState>> GetAllActiveCustomStatesForDepartmentAsync(int departmentId);
		/// <summary>
		/// Gets all active unit states for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;CustomState&gt;&gt;.</returns>
		Task<List<CustomState>> GetAllActiveUnitStatesForDepartmentAsync(int departmentId);
		/// <summary>
		/// Gets the active personnel state for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;CustomState&gt;.</returns>
		Task<CustomState> GetActivePersonnelStateForDepartmentAsync(int departmentId);
		/// <summary>
		/// Gets the custom personnel statuses or defaults asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;CustomStateDetail&gt;&gt;.</returns>
		Task<List<CustomStateDetail>> GetCustomPersonnelStatusesOrDefaultsAsync(int departmentId);
		/// <summary>
		/// Gets the active staffing levels for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;CustomState&gt;.</returns>
		Task<CustomState> GetActiveStaffingLevelsForDepartmentAsync(int departmentId);
		/// <summary>
		/// Invalidates the custom state in cache.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		void InvalidateCustomStateInCache(int departmentId);
		/// <summary>
		/// Saves the asynchronous.
		/// </summary>
		/// <param name="customState">State of the custom.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;CustomState&gt;.</returns>
		Task<CustomState> SaveAsync(CustomState customState, CancellationToken cancellationToken = default(CancellationToken));
		/// <summary>
		/// Gets the custom detail by identifier asynchronous.
		/// </summary>
		/// <param name="detailId">The detail identifier.</param>
		/// <returns>Task&lt;CustomStateDetail&gt;.</returns>
		Task<CustomStateDetail> GetCustomDetailByIdAsync(int detailId);
		/// <summary>
		/// Saves the detail asynchronous.
		/// </summary>
		/// <param name="customStateDetail">The custom state detail.</param>
		/// <param name="departmentId">DepartmentId of the update</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;CustomStateDetail&gt;.</returns>
		Task<CustomStateDetail> SaveDetailAsync(CustomStateDetail customStateDetail, int departmentId, CancellationToken cancellationToken = default(CancellationToken));
		/// <summary>
		/// Deletes the asynchronous.
		/// </summary>
		/// <param name="customState">State of the custom.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;CustomState&gt;.</returns>
		Task<CustomState> DeleteAsync(CustomState customState, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Updates the asynchronous.
		/// </summary>
		/// <param name="state">The state.</param>
		/// <param name="details">The details.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;CustomState&gt;.</returns>
		Task<CustomState> UpdateAsync(CustomState state, List<CustomStateDetail> details, CancellationToken cancellationToken = default(CancellationToken));
		/// <summary>
		/// Gets the custom detail for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="detailId">The detail identifier.</param>
		/// <returns>Task&lt;CustomStateDetail&gt;.</returns>
		Task<CustomStateDetail> GetCustomDetailForDepartmentAsync(int departmentId, int detailId);
		/// <summary>
		/// Gets the custom personnel status asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="state">The state.</param>
		/// <returns>Task&lt;CustomStateDetail&gt;.</returns>
		Task<CustomStateDetail> GetCustomPersonnelStatusAsync(int departmentId, ActionLog state);
		/// <summary>
		/// Gets the custom unit state asynchronous.
		/// </summary>
		/// <param name="state">The state.</param>
		/// <returns>Task&lt;CustomStateDetail&gt;.</returns>
		Task<CustomStateDetail> GetCustomUnitStateAsync(UnitState state);

		/// <summary>
		/// Gets all custom states for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;CustomState&gt;&gt;.</returns>
		Task<List<CustomState>> GetAllCustomStatesForDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets the custom personnel staffing asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="state">The state.</param>
		/// <returns>Task&lt;CustomStateDetail&gt;.</returns>
		Task<CustomStateDetail> GetCustomPersonnelStaffingAsync(int departmentId, UserState state);

		/// <summary>
		/// Gets the custom personnel staffing or defaults asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;CustomStateDetail&gt;&gt;.</returns>
		Task<List<CustomStateDetail>> GetCustomPersonnelStaffingsOrDefaultsAsync(int departmentId);

		/// <summary>
		/// Gets the default (system) unit statuses
		/// </summary>
		/// <returns>List of CustomStateDetail</returns>
		List<CustomStateDetail> GetDefaultUnitStatuses();

		/// <summary>
		/// Gets the default (system) personnel statuses
		/// </summary>
		/// <returns>List of CustomStateDetail</returns>
		List<CustomStateDetail> GetDefaultPersonStatuses();

		/// <summary>
		/// Gets the default (system) personnel staffing levels
		/// </summary>
		/// <returns>List of CustomStateDetail</returns>
		List<CustomStateDetail> GetDefaultPersonStaffings();
	}
}
