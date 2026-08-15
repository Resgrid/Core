using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;

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
		/// Gets the department's new-call field policy: which built-in fields the call form shows and
		/// which it requires. Returns an empty policy (everything visible, nothing required) when the
		/// department has not configured one, which is how Resgrid behaved before the setting existed.
		/// </summary>
		/// <summary>
		/// Gets how long a unit may sit in a status before the board highlights it. Returns an empty set
		/// (no highlighting) when the department has not configured any, which is the pre-feature
		/// behaviour.
		/// </summary>
		Task<UnitStatusThresholds> GetUnitStatusThresholdsAsync(int departmentId, bool bypassCache = false);

		/// <summary>
		/// Saves the department's time-in-status thresholds, returning the normalised set that was stored.
		/// </summary>
		Task<UnitStatusThresholds> SaveUnitStatusThresholdsAsync(int departmentId, UnitStatusThresholds thresholds,
			CancellationToken cancellationToken = default(CancellationToken));

		Task<NewCallFieldPolicy> GetNewCallFieldPolicyAsync(int departmentId, bool bypassCache = false);

		/// <summary>
		/// Saves the department's new-call field policy, returning the normalised policy that was stored.
		/// </summary>
		Task<NewCallFieldPolicy> SaveNewCallFieldPolicyAsync(int departmentId, NewCallFieldPolicy policy,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the map center coordinates asynchronous.
		/// </summary>
		/// <param name="department">The department.</param>
		/// <returns>Task&lt;Coordinates&gt;.</returns>
		Task<Coordinates> GetMapCenterCoordinatesAsync(Department department);

		/// <summary>
		/// Persists the department's default map center.
		/// </summary>
		/// <remarks>
		/// Supplied coordinates always win and are stored verbatim. When both are blank the department's
		/// own address is geocoded and the result stored instead, so a department that never touches
		/// these fields still gets its maps centred on itself rather than on the system default. An
		/// operator who has set coordinates by hand is never overwritten by a geocode.
		/// </remarks>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="latitude">Operator-supplied latitude, or null/blank to geocode the address.</param>
		/// <param name="longitude">Operator-supplied longitude, or null/blank to geocode the address.</param>
		/// <param name="address">The department's address, used only when no coordinates were supplied.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>The coordinates that were stored, or null when nothing could be resolved.</returns>
		Task<Coordinates> SaveMapCenterCoordinatesAsync(int departmentId, string latitude, string longitude, Address address,
			CancellationToken cancellationToken = default(CancellationToken));

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

		Task<string> GetTtsLanguageForDepartmentAsync(int departmentId);

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

		Task<bool> GetMappingUseMapboxOverrideAsync(int departmentId);

		Task<string> GetMappingMapboxStyleUrlAsync(int departmentId);

		Task<string> GetMappingMapboxAccessTokenAsync(int departmentId);

		Task<ResolvedMapConfig> GetMapConfigForDepartmentAsync(int departmentId, string key = null);

		Task<DepartmentModuleSettings> GetDepartmentModuleSettingsAsync(int departmentId, bool bypassCache = false);

		Task<bool> GetUnitDispatchAlsoDispatchToAssignedPersonnelAsync(int departmentId);

		Task<bool> GetUnitDispatchAlsoDispatchToGroupAsync(int departmentId);

		Task<int> GetUnitCallDispatchStatusToSetAsync(int departmentId);

		Task<int> GetUnitCallReleaseStatusToSetAsync(int departmentId);

		Task<List<UnitTypeCallStatusOverride>> GetUnitCallStatusOverridesByUnitTypeAsync(int departmentId);

		Task<DepartmentSetting> SetUnitCallStatusOverridesByUnitTypeAsync(int departmentId,
			List<UnitTypeCallStatusOverride> overrides, CancellationToken cancellationToken = default(CancellationToken));

		Task<bool> GetPersonnelOnUnitSetUnitStatusAsync(int departmentId, bool bypassCache = false);

		/// <summary>Department-wide dispatch recommendation mode (Off / StationBased / ClosestUnit). Cached.</summary>
		Task<DispatchRecommendationModes> GetDispatchRecommendationModeAsync(int departmentId, bool bypassCache = false);

		Task<DepartmentSetting> SetDispatchRecommendationModeAsync(int departmentId, DispatchRecommendationModes mode, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>True = matched run cards auto-dispatch; false = recommendations pre-populate for dispatcher review. Cached.</summary>
		Task<bool> GetDispatchRecommendationAutoDispatchAsync(int departmentId, bool bypassCache = false);

		Task<DepartmentSetting> SetDispatchRecommendationAutoDispatchAsync(int departmentId, bool enabled, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Engine tuning (location age/radius, ETA re-rank, rest period, staffing gate, move-up). Never null. Cached.</summary>
		Task<DispatchRecommendationConfig> GetDispatchRecommendationConfigAsync(int departmentId, bool bypassCache = false);

		Task<DepartmentSetting> SetDispatchRecommendationConfigAsync(int departmentId, DispatchRecommendationConfig config, CancellationToken cancellationToken = default(CancellationToken));

		Task<DepartmentSetting> SetDepartmentModuleSettingsAsync(int departmentId, DepartmentModuleSettings settings, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the 2FA enforcement scope for department admins.
		/// Returns 0 = disabled, 1 = dept admins + managing user, 2 = dept admins + managing user + group admins.
		/// </summary>
		Task<int> GetRequire2FAForAdminsAsync(int departmentId);

		Task<int?> GetDepartmentIdForPaddleCustomerIdAsync(string paddleCustomerId, bool bypassCache = false);

		Task<string> GetPaddleCustomerIdForDepartmentAsync(int departmentId);

		Task<bool> GetCheckInTimersAutoEnableForNewCallsAsync(int departmentId);

		/// <summary>
		/// Gets a department setting by type. Returns null if the setting does not exist.
		/// </summary>
		Task<DepartmentSetting> GetSettingByTypeAsync(int departmentId, DepartmentSettingTypes type);

		Task<bool> GetModernNotificationsEnabledAsync(int departmentId, bool bypassCache = false);

		/// <summary>
		/// True when the department forces every member to use their security PIN for dangerous
		/// chatbot/SMS actions (overrides the per-user opt-in).
		/// </summary>
		Task<bool> GetForceChatbotSecurityPinAsync(int departmentId, bool bypassCache = false);

		Task<int> GetHardwareTrackingStaleAfterSecondsAsync(int departmentId, bool bypassCache = false);

		Task<bool> GetHardwareTrackingMobileFallbackEnabledAsync(int departmentId, bool bypassCache = false);

		Task<int> GetHardwareTrackingLocationRetentionDaysAsync(int departmentId, bool bypassCache = false);
	}
}
