using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IDepartmentSettingsService
	{
		/// <summary>
		/// Saves the or update setting asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="setting">The setting.</param>
		/// <param name="type">The type.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;DepartmentSetting&gt;.</returns>
		Task<DepartmentSetting> SaveOrUpdateSettingAsync(int departmentId, string setting, DepartmentSettingTypes type, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the big board map zoom level for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;System.Nullable&lt;System.Int32&gt;&gt;.</returns>
		Task<int?> GetBigBoardMapZoomLevelForDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets the big board refresh time for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;System.Nullable&lt;System.Int32&gt;&gt;.</returns>
		Task<int?> GetBigBoardRefreshTimeForDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets the big board center address department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;Address&gt;.</returns>
		Task<Address> GetBigBoardCenterAddressDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets the big board center GPS coordinates department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;System.String&gt;.</returns>
		Task<string> GetBigBoardCenterGpsCoordinatesDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets the big board hide unavailable department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;System.Nullable&lt;System.Boolean&gt;&gt;.</returns>
		Task<bool?> GetBigBoardHideUnavailableDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets the department identifier for RSS key asynchronous.
		/// </summary>
		/// <param name="key">The key.</param>
		/// <returns>Task&lt;System.Nullable&lt;System.Int32&gt;&gt;.</returns>
		Task<int?> GetDepartmentIdForRssKeyAsync(string key);

		/// <summary>
		/// Gets the RSS key for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;System.String&gt;.</returns>
		Task<string> GetRssKeyForDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets the stripe customer identifier for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;System.String&gt;.</returns>
		Task<string> GetStripeCustomerIdForDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets the department identifier for stripe customer identifier asynchronous.
		/// </summary>
		/// <param name="stripeCustomerId">The stripe customer identifier.</param>
		/// <param name="bypassCache">if set to <c>true</c> [bypass cache].</param>
		/// <returns>Task&lt;System.Nullable&lt;System.Int32&gt;&gt;.</returns>
		Task<int?> GetDepartmentIdForStripeCustomerIdAsync(string stripeCustomerId, bool bypassCache = false);

		/// <summary>
		/// Determines whether [is testing enabled for department asynchronous] [the specified department identifier].
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> IsTestingEnabledForDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets the map center coordinates asynchronous.
		/// </summary>
		/// <param name="department">The department.</param>
		/// <returns>Task&lt;Coordinates&gt;.</returns>
		Task<Coordinates> GetMapCenterCoordinatesAsync(Department department);

		/// <summary>
		/// Gets the disable automatic available for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="bypassCache">if set to <c>true</c> [bypass cache].</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> GetDisableAutoAvailableForDepartmentAsync(int departmentId, bool bypassCache = true);

		/// <summary>
		/// Gets the text to call number for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;System.String&gt;.</returns>
		Task<string> GetTextToCallNumberForDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets the text to call import format for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;System.Nullable&lt;System.Int32&gt;&gt;.</returns>
		Task<int?> GetTextToCallImportFormatForDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets the department identifier by text to call number asynchronous.
		/// </summary>
		/// <param name="phoneNumber">The phone number.</param>
		/// <returns>Task&lt;System.Nullable&lt;System.Int32&gt;&gt;.</returns>
		Task<int?> GetDepartmentIdByTextToCallNumberAsync(string phoneNumber);

		/// <summary>
		/// Gets the text to call source numbers for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;System.String&gt;.</returns>
		Task<string> GetTextToCallSourceNumbersForDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets the department is text call import enabled asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> GetDepartmentIsTextCallImportEnabledAsync(int departmentId);

		/// <summary>
		/// Gets the department is text command enabled asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> GetDepartmentIsTextCommandEnabledAsync(int departmentId);

		/// <summary>
		/// Deletes the setting asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="type">The type.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteSettingAsync(int departmentId, DepartmentSettingTypes type, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the department identifier for dispatch email asynchronous.
		/// </summary>
		/// <param name="emailAddress">The email address.</param>
		/// <returns>Task&lt;System.Nullable&lt;System.Int32&gt;&gt;.</returns>
		Task<int?> GetDepartmentIdForDispatchEmailAsync(string emailAddress);

		/// <summary>
		/// Gets the dispatch email for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;System.String&gt;.</returns>
		Task<string> GetDispatchEmailForDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets the disable automatic available for department by user identifier asynchronous.
		/// </summary>
		/// <param name="userId">The user identifier.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> GetDisableAutoAvailableForDepartmentByUserIdAsync(string userId);

		/// <summary>
		/// Gets the department update timestamp asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;DateTime&gt;.</returns>
		Task<DateTime> GetDepartmentUpdateTimestampAsync(int departmentId);

		/// <summary>
		/// Gets the brain tree customer identifier for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;System.String&gt;.</returns>
		Task<string> GetBrainTreeCustomerIdForDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets the department identifier for brain tree customer identifier asynchronous.
		/// </summary>
		/// <param name="stripeCustomerId">The stripe customer identifier.</param>
		/// <returns>Task&lt;System.Nullable&lt;System.Int32&gt;&gt;.</returns>
		Task<int?> GetDepartmentIdForBrainTreeCustomerIdAsync(string stripeCustomerId);

		/// <summary>
		/// Gets the department personnel sort order asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;PersonnelSortOrders&gt;.</returns>
		Task<PersonnelSortOrders> GetDepartmentPersonnelSortOrderAsync(int departmentId);

		/// <summary>
		/// Gets the department units sort order asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;UnitSortOrders&gt;.</returns>
		Task<UnitSortOrders> GetDepartmentUnitsSortOrderAsync(int departmentId);

		/// <summary>
		/// Gets the department call sort order asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;CallSortOrders&gt;.</returns>
		Task<CallSortOrders> GetDepartmentCallSortOrderAsync(int departmentId);

		/// <summary>
		/// Gets all department manager information asynchronous.
		/// </summary>
		/// <returns>Task&lt;List&lt;DepartmentManagerInfo&gt;&gt;.</returns>
		Task<List<DepartmentManagerInfo>> GetAllDepartmentManagerInfoAsync();

		/// <summary>
		/// Gets the department manager information by email asynchronous.
		/// </summary>
		/// <param name="emailAddress">The email address.</param>
		/// <returns>Task&lt;List&lt;DepartmentManagerInfo&gt;&gt;.</returns>
		Task<DepartmentManagerInfo> GetDepartmentManagerInfoByEmailAsync(string emailAddress);

		/// <summary>
		/// Gets the department personnel list status sort order asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;PersonnelListStatusOrder&gt;&gt;.</returns>
		Task<List<PersonnelListStatusOrder>> GetDepartmentPersonnelListStatusSortOrderAsync(int departmentId);

		/// <summary>
		/// Sets the department personnel list status sort order asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="orders">The orders.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;DepartmentSetting&gt;.</returns>
		Task<DepartmentSetting> SetDepartmentPersonnelListStatusSortOrderAsync(int departmentId, List<PersonnelListStatusOrder> orders, CancellationToken cancellationToken = default(CancellationToken));

		Task<bool> GetDispatchShiftInsteadOfGroupAsync(int departmentId);

		Task<bool> GetAutoSetStatusForShiftDispatchPersonnelAsync(int departmentId);

		Task<int> GetShiftCallDispatchPersonnelStatusToSetAsync(int departmentId);

		Task<int> GetShiftCallReleasePersonnelStatusToSetAsync(int departmentId);

		Task<bool> GetAllowSignupsForMultipleShiftGroupsAsync(int departmentId);

		Task<DepartmentSuppressStaffingInfo> GetDepartmentStaffingSuppressInfoAsync(int departmentId, bool bypassCache = false);

		Task<int> GetMappingPersonnelLocationTTLAsync(int departmentId);

		Task<int> GetMappingUnitLocationTTLAsync(int departmentId);

		Task<bool> GetMappingPersonnelAllowStatusWithNoLocationToOverwriteAsync(int departmentId);

		Task<bool> GetMappingUnitAllowStatusWithNoLocationToOverwriteAsync(int departmentId);

		Task<DepartmentModuleSettings> GetDepartmentModuleSettingsAsync(int departmentId, bool bypassCache = false);

		Task<bool> GetUnitDispatchAlsoDispatchToAssignedPersonnelAsync(int departmentId);

		Task<bool> GetUnitDispatchAlsoDispatchToGroupAsync(int departmentId);

		Task<DepartmentSetting> SetDepartmentModuleSettingsAsync(int departmentId, DepartmentModuleSettings settings, CancellationToken cancellationToken = default(CancellationToken));
	}
}
