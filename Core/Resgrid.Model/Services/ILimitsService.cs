using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Interface ILimitsService
	/// </summary>
	public interface ILimitsService
	{
		/// <summary>
		/// Validates the department is within limits asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> ValidateDepartmentIsWithinLimitsAsync(int departmentId);

		/// <summary>
		/// Determines whether this instance [can department add new user asynchronous] the specified department identifier.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> CanDepartmentAddNewUserAsync(int departmentId);

		/// <summary>
		/// Determines whether this instance [can department add new group] the specified department identifier.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> CanDepartmentAddNewGroup(int departmentId);

		/// <summary>
		/// Determines whether this instance [can department add new role] the specified department identifier.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns><c>true</c> if this instance [can department add new role] the specified department identifier; otherwise, <c>false</c>.</returns>
		bool CanDepartmentAddNewRole(int departmentId);

		/// <summary>
		/// Determines whether this instance [can department add new unit] the specified department identifier.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> CanDepartmentAddNewUnit(int departmentId);

		/// <summary>
		/// Gets the personnel limit for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="plan">The plan.</param>
		/// <returns>Task&lt;System.Int32&gt;.</returns>
		Task<int> GetPersonnelLimitForDepartmentAsync(int departmentId, Plan plan = null);

		/// <summary>
		/// Gets the groups limit for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="plan">The plan.</param>
		/// <returns>Task&lt;System.Int32&gt;.</returns>
		Task<int> GetGroupsLimitForDepartmentAsync(int departmentId, Plan plan = null);

		/// <summary>
		/// Gets the roles limit for department.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>System.Int32.</returns>
		int GetRolesLimitForDepartment(int departmentId);

		/// <summary>
		/// Gets the units limit for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="plan">The plan.</param>
		/// <returns>Task&lt;System.Int32&gt;.</returns>
		Task<int> GetUnitsLimitForDepartmentAsync(int departmentId, Plan plan = null);

		/// <summary>
		/// Determines whether this instance [can department provision number asynchronous] the specified department identifier.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> CanDepartmentProvisionNumberAsync(int departmentId);

		/// <summary>
		/// Determines whether this instance [can department use voice asynchronous] the specified department identifier.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> CanDepartmentUseVoiceAsync(int departmentId);

		/// <summary>
		/// Determines whether this instance [can department use links asynchronous] the specified department identifier.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> CanDepartmentUseLinksAsync(int departmentId);

		/// <summary>
		/// Determines whether this instance [can department create orders asynchronous] the specified department identifier.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> CanDepartmentCreateOrdersAsync(int departmentId);

		Task<DepartmentLimits> GetLimitsForEntityPlanWithFallbackAsync(int departmentId, bool bypassCache = false);

		Task<bool> InvalidateDepartmentsEntityLimitsCache(int departmentId);
	}
}
