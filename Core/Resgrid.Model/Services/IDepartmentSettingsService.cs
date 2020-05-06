using System;
using System.Collections.Generic;

namespace Resgrid.Model.Services
{
	public interface IDepartmentSettingsService
	{
		void SaveOrUpdateSetting(int departmentId, string setting, DepartmentSettingTypes type);
		int? GetBigBoardMapZoomLevelForDepartment(int departmentId);
		int? GetBigBoardRefreshTimeForDepartment(int departmentId);
		Address GetBigBoardCenterAddressDepartment(int departmentId);
		string GetBigBoardCenterGpsCoordinatesDepartment(int departmentId);
		bool? GetBigBoardHideUnavailableDepartment(int departmentId);
		int? GetDepartmentIdForRssKey(string key);
		string GetRssKeyForDepartment(int departmentId);
		string GetStripeCustomerIdForDepartment(int departmentId);
		int? GetDepartmentIdForStripeCustomerId(string stripeCustomerId, bool bypassCache = false);
		bool IsTestingEnabledForDepartment(int departmentId);
		Coordinates GetMapCenterCoordinates(Department department);
		bool GetDisableAutoAvailableForDepartment(int departmentId, bool bypassCache = true);
		string GetTextToCallNumberForDepartment(int departmentId);
		int? GetTextToCallImportFormatForDepartment(int departmentId);
		int? GetDepartmentIdByTextToCallNumber(string phoneNumber);
		string GetTextToCallSourceNumbersForDepartment(int departmentId);
		bool GetDepartmentIsTextCallImportEnabled(int departmentId);
		bool GetDepartmentIsTextCommandEnabled(int departmentId);
		void DeleteSetting(int departmentId, DepartmentSettingTypes type);
		int? GetDepartmentIdForDispatchEmail(string emailAddress);
		string GetDispatchEmailForDepartment(int departmentId);
		bool GetDisableAutoAvailableForDepartmentByUserId(string userId);
		DateTime GetDepartmentUpdateTimestamp(int departmentId);
		string GetBrainTreeCustomerIdForDepartment(int departmentId);
		int? GetDepartmentIdForBrainTreeCustomerId(string stripeCustomerId);
		PersonnelSortOrders GetDepartmentPersonnelSortOrder(int departmentId);
		UnitSortOrders GetDepartmentUnitsSortOrder(int departmentId);
		CallSortOrders GetDepartmentCallSortOrder(int departmentId);
		List<DepartmentManagerInfo> GetAllDepartmentManagerInfo();
		DepartmentManagerInfo GetDepartmentManagerInfoByEmail(string emailAddress);
	}
}
