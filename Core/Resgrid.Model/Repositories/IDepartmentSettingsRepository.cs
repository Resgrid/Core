using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IDepartmentSettingsRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.DepartmentSetting}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.DepartmentSetting}" />
	public interface IDepartmentSettingsRepository: IRepository<DepartmentSetting>
	{
		/// <summary>
		/// Gets the department setting by user identifier type asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <param name="type">The type.</param>
		/// <returns>Task&lt;DepartmentSetting&gt;.</returns>
		Task<DepartmentSetting> GetDepartmentSettingByUserIdTypeAsync(string userId, DepartmentSettingTypes type);

		/// <summary>
		/// Gets the department setting by identifier type asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="type">The type.</param>
		/// <returns>Task&lt;DepartmentSetting&gt;.</returns>
		Task<DepartmentSetting> GetDepartmentSettingByIdTypeAsync(int departmentId, DepartmentSettingTypes type);

		/// <summary>
		/// Gets the type of the department setting by setting.
		/// </summary>
		/// <param name="setting">The setting.</param>
		/// <param name="type">The type.</param>
		/// <returns>Task&lt;DepartmentSetting&gt;.</returns>
		Task<DepartmentSetting> GetDepartmentSettingBySettingTypeAsync(string setting, DepartmentSettingTypes type);

		/// <summary>
		/// Gets all department manager information asynchronous.
		/// </summary>
		/// <returns>Task&lt;List&lt;DepartmentManagerInfo&gt;&gt;.</returns>
		Task<List<DepartmentManagerInfo>> GetAllDepartmentManagerInfoAsync();

		/// <summary>
		/// Gets the department manager information by email asynchronous.
		/// </summary>
		/// <param name="emailAddress">The email address.</param>
		/// <returns>Task&lt;DepartmentManagerInfo&gt;.</returns>
		Task<DepartmentManagerInfo> GetDepartmentManagerInfoByEmailAsync(string emailAddress);
	}
}
