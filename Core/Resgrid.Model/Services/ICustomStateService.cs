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
		/// The personnel status Resgrid sets for a meaning: the first active option of the department's personnel statuses
		/// with that base type (by order), or the built-in status with that meaning for a department on the built-in
		/// statuses. Null when the department's statuses have no option with that meaning.
		/// </summary>
		Task<int?> ResolvePersonnelStatusAsync(int departmentId, ActionBaseTypes baseType, DestinationEntityTypes destination = DestinationEntityTypes.None);
		/// <summary>
		/// The staffing level for a meaning: the first active level of the department's own staffing levels with that base type
		/// (by order), or the built-in level with that meaning (Available..On Shift). Null when there is none (for example Off
		/// Duty on the built-in levels).
		/// </summary>
		Task<int?> ResolvePersonnelStaffingAsync(int departmentId, StaffingBaseTypes baseType);
		/// <summary>
		/// The status a daily status reset sets everyone to. <paramref name="target"/> is the reset's stored value: "b{n}" is a
		/// meaning, resolved to the department's status with it; a plain value saved before meanings is kept while it is still
		/// one of the department's statuses, otherwise it follows its meaning; when no status has that meaning, a status
		/// without a meaning matched by name (<see cref="StatusNameMatch"/>) stands in, and when none does it is kept as the
		/// reset wrote it before meanings. Null when nothing matches (nobody is reset): a meaning the department has no
		/// status for, or a plain value that is neither built-in nor one of the department's options.
		/// </summary>
		Task<int?> ResolveStatusResetAsync(int departmentId, string target);
		/// <summary>The staffing level a daily staffing reset sets everyone to; the same rules as <see cref="ResolveStatusResetAsync"/>.</summary>
		Task<int?> ResolveStaffingResetAsync(int departmentId, string target);
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

		/// <summary>Department Import quiet overload: the side effects named by <paramref name="effects"/> are skipped or deferred to the import engine.</summary>
		Task<CustomState> SaveAsync(CustomState customState, Onboarding.ImportEffectPolicy effects, CancellationToken cancellationToken);
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

		/// <summary>Department Import quiet overload: the side effects named by <paramref name="effects"/> are skipped or deferred to the import engine.</summary>
		Task<CustomStateDetail> SaveDetailAsync(CustomStateDetail customStateDetail, int departmentId, Onboarding.ImportEffectPolicy effects, CancellationToken cancellationToken);
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
		/// The status shown for a person with no status (or none inside the auto-available hour): the first Available
		/// option of the department's own staff statuses, or the built-in Standing By for a department on the built-in ones.
		/// The detail's CustomStateDetailId is the status id (0 for Standing By).
		/// </summary>
		Task<CustomStateDetail> GetDefaultPersonnelStatusAsync(int departmentId);
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
