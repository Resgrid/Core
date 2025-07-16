using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class DepartmentSettingsService : IDepartmentSettingsService
	{
		private static string DisableAutoAvailableCacheKey = "DSetAutoAvailable_{0}";
		private static string StripeCustomerCacheKey = "DSetStripeCus_{0}";
		private static string BigBoardCenterGps = "DSetBBCenterGps_{0}";
		private static string StaffingSupressInfo = "DSetStaffingSupress_{0}";
		private static TimeSpan LongCacheLength = TimeSpan.FromDays(14);
		private static TimeSpan ThatsNotLongThisIsLongCacheLength = TimeSpan.FromDays(365);
		private static TimeSpan TwoYearCacheLength = TimeSpan.FromDays(730);

		private readonly IDepartmentSettingsRepository _departmentSettingsRepository;
		private readonly IAddressService _addressService;
		private readonly IGeoLocationProvider _geoLocationProvider;
		private readonly ICacheProvider _cacheProvider;

		public DepartmentSettingsService(IDepartmentSettingsRepository departmentSettingsRepository, IAddressService addressService,
			IGeoLocationProvider geoLocationProvider, ICacheProvider cacheProvider)
		{
			_departmentSettingsRepository = departmentSettingsRepository;
			_addressService = addressService;
			_geoLocationProvider = geoLocationProvider;
			_cacheProvider = cacheProvider;
		}

		public async Task<DepartmentSetting> SaveOrUpdateSettingAsync(int departmentId, string setting, DepartmentSettingTypes type, CancellationToken cancellationToken = default(CancellationToken))
		{
			var savedSetting = await GetSettingByDepartmentIdType(departmentId, type);

			if (savedSetting == null)
			{
				DepartmentSetting newSetting = new DepartmentSetting();
				newSetting.DepartmentId = departmentId;
				newSetting.Setting = setting;
				newSetting.SettingType = (int)type;

				return await _departmentSettingsRepository.SaveOrUpdateAsync(newSetting, cancellationToken);
			}
			else
			{
				// Clear out Cache
				switch (type)
				{
					case DepartmentSettingTypes.BigBoardMapCenterGpsCoordinates:
						_cacheProvider.Remove(string.Format(BigBoardCenterGps, departmentId));
						break;
					case DepartmentSettingTypes.DisabledAutoAvailable:
						_cacheProvider.Remove(string.Format(DisableAutoAvailableCacheKey, departmentId));
						break;
					case DepartmentSettingTypes.StaffingSuppressStaffingLevels:
						_cacheProvider.Remove(string.Format(StaffingSupressInfo, departmentId));
						break;
				}

				savedSetting.Setting = setting;
				return await _departmentSettingsRepository.SaveOrUpdateAsync(savedSetting, cancellationToken);
			}

			return null;
		}

		public async Task<bool> DeleteSettingAsync(int departmentId, DepartmentSettingTypes type, CancellationToken cancellationToken = default(CancellationToken))
		{
			var savedSetting = await GetSettingByDepartmentIdType(departmentId, type);

			if (savedSetting != null)
				return await _departmentSettingsRepository.DeleteAsync(savedSetting, cancellationToken);

			return false;
		}

		public async Task<int?> GetBigBoardMapZoomLevelForDepartmentAsync(int departmentId)
		{
			var settingValue = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.BigBoardMapZoomLevel);

			if (settingValue != null)
				return int.Parse(settingValue.Setting);

			return null;
		}

		public async Task<int?> GetBigBoardRefreshTimeForDepartmentAsync(int departmentId)
		{
			var settingValue = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.BigBoardPageRefresh);

			if (settingValue != null)
				return int.Parse(settingValue.Setting);

			return null;
		}

		public async Task<Address> GetBigBoardCenterAddressDepartmentAsync(int departmentId)
		{
			var settingValue = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.BigBoardMapCenterAddress);

			if (settingValue != null)
				return await _addressService.GetAddressByIdAsync(int.Parse(settingValue.Setting));

			return null;
		}

		public async Task<string> GetBigBoardCenterGpsCoordinatesDepartmentAsync(int departmentId)
		{
			string location;

			async Task<string> getLocation()
			{
				var center = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.BigBoardMapCenterGpsCoordinates);

				if (center != null)
				{
					var newLocation = String.Empty;
					var points = center.Setting.Split(char.Parse(","));

					try
					{
						if (points.Length == 2)
						{
							if (!String.IsNullOrWhiteSpace(points[0]))
							{
								if (Framework.LocationHelpers.IsDMSLocation(points[0]))
								{
									newLocation = Framework.LocationHelpers.ConvertDegreeAngleToDouble(points[0]).ToString() + ",";
								}
								else
								{
									newLocation = LocationHelpers.StripNonDecimalCharacters(points[0]) + ",";
								}
							}

							if (!String.IsNullOrWhiteSpace(points[1]))
							{
								if (Framework.LocationHelpers.IsDMSLocation(points[1]))
								{
									newLocation = newLocation + Framework.LocationHelpers.ConvertDegreeAngleToDouble(points[1]).ToString();
								}
								else
								{
									newLocation = newLocation + LocationHelpers.StripNonDecimalCharacters(points[1]);
								}
							}

						}
						else
						{
							newLocation = center.Setting;
						}
					}
					catch (Exception ex)
					{
						newLocation = "0,0";
					}

					return newLocation;
				}

				return null;
			}

			if (Config.SystemBehaviorConfig.CacheEnabled)
			{
				return await _cacheProvider.RetrieveAsync<string>(string.Format(BigBoardCenterGps, departmentId),
					getLocation, TwoYearCacheLength);
			}
			else
			{
				return await getLocation();
			}
		}

		public async Task<bool?> GetBigBoardHideUnavailableDepartmentAsync(int departmentId)
		{
			var settingValue = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.BigBoardHideUnavailable);

			if (settingValue != null)
				return bool.Parse(settingValue.Setting);

			return null;
		}

		public async Task<string> GetRssKeyForDepartmentAsync(int departmentId)
		{
			var settingValue = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.RssFeedKeyForActiveCalls);

			if (settingValue != null)
				return settingValue.Setting;

			return null;
		}

		public async Task<int?> GetDepartmentIdForRssKeyAsync(string key)
		{
			var department = await GetSettingBySettingTypeAsync(key, DepartmentSettingTypes.RssFeedKeyForActiveCalls);

			if (department != null)
				return department.DepartmentId;

			return null;
		}

		public async Task<string> GetStripeCustomerIdForDepartmentAsync(int departmentId)
		{
			var settingValue = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.StripeCustomerId);

			if (settingValue != null)
				return settingValue.Setting;

			return String.Empty;
		}

		public async Task<int?> GetDepartmentIdForStripeCustomerIdAsync(string stripeCustomerId, bool bypassCache = false)
		{
			DepartmentSetting key;

			async Task<DepartmentSetting> getSetting()
			{
				return await _departmentSettingsRepository.GetDepartmentSettingBySettingTypeAsync(stripeCustomerId, DepartmentSettingTypes.StripeCustomerId);
			}

			if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
			{
				key = await _cacheProvider.RetrieveAsync<DepartmentSetting>(string.Format(StripeCustomerCacheKey, stripeCustomerId),
					getSetting, TwoYearCacheLength);
			}
			else
			{
				key = await getSetting();
			}

			if (key != null)
				return key.DepartmentId;

			return null;
		}

		public async Task<string> GetBrainTreeCustomerIdForDepartmentAsync(int departmentId)
		{
			var settingValue = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.BrainTreeCustomerId);

			if (settingValue != null)
				return settingValue.Setting;

			return null;
		}

		public async Task<int?> GetDepartmentIdForBrainTreeCustomerIdAsync(string stripeCustomerId)
		{
			var key = await _departmentSettingsRepository.GetDepartmentSettingBySettingTypeAsync(stripeCustomerId, DepartmentSettingTypes.BrainTreeCustomerId);

			if (key != null)
				return key.DepartmentId;

			return null;
		}

		public async Task<bool> IsTestingEnabledForDepartmentAsync(int departmentId)
		{
			var settingValue = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.TestEnabled);

			if (settingValue != null)
				return bool.Parse(settingValue.Setting);

			return false;
		}

		public async Task<Coordinates> GetMapCenterCoordinatesAsync(Department department)
		{
			var address = await GetBigBoardCenterAddressDepartmentAsync(department.DepartmentId);
			var gpsCoordinates = await GetBigBoardCenterGpsCoordinatesDepartmentAsync(department.DepartmentId);

			var coordinates = new Coordinates();

			if (!String.IsNullOrWhiteSpace(gpsCoordinates))
			{
				string[] gpscoords = gpsCoordinates.Split(char.Parse(","));

				if (gpscoords.Count() == 2)
				{
					double newLat;
					double newLon;
					if (double.TryParse(gpscoords[0], out newLat) && double.TryParse(gpscoords[1], out newLon))
					{
						coordinates.Latitude = newLat;
						coordinates.Longitude = newLon;
					}
				}
			}

			if (!coordinates.Latitude.HasValue && !coordinates.Longitude.HasValue && address != null)
			{
				string coords = await _geoLocationProvider.GetLatLonFromAddress(string.Format("{0} {1} {2} {3}", address.Address1,
																																address.City, address.State, address.PostalCode));

				if (!String.IsNullOrEmpty(coords))
				{
					double newLat;
					double newLon;
					var coordinatesArr = coords.Split(char.Parse(","));
					if (double.TryParse(coordinatesArr[0], out newLat) && double.TryParse(coordinatesArr[1], out newLon))
					{
						coordinates.Latitude = newLat;
						coordinates.Longitude = newLon;
					}
				}
			}

			if (!coordinates.Latitude.HasValue && !coordinates.Longitude.HasValue && department.Address != null)
			{
				string coords = await _geoLocationProvider.GetLatLonFromAddress(string.Format("{0} {1} {2} {3}", department.Address.Address1,
																																department.Address.City,
																																department.Address.State,
																																department.Address.PostalCode));

				if (!String.IsNullOrEmpty(coords))
				{
					double newLat;
					double newLon;
					var coordinatesArr = coords.Split(char.Parse(","));
					if (double.TryParse(coordinatesArr[0], out newLat) && double.TryParse(coordinatesArr[1], out newLon))
					{
						coordinates.Latitude = newLat;
						coordinates.Longitude = newLon;
					}
				}
			}

			if (!coordinates.Latitude.HasValue || !coordinates.Longitude.HasValue)
			{
				coordinates.Latitude = 39.14086268299356;
				coordinates.Longitude = -119.7583809782715;
			}

			return coordinates;
		}

		public async Task<bool> GetDisableAutoAvailableForDepartmentAsync(int departmentId, bool bypassCache = true)
		{
			async Task<string> getSetting()
			{
				var actualSetting = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.DisabledAutoAvailable);

				if (actualSetting != null)
					return actualSetting.Setting;
				else
					return "false";
			}

			if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
			{
				var cachedValue = await _cacheProvider.RetrieveAsync<string>(string.Format(DisableAutoAvailableCacheKey, departmentId),
					getSetting, LongCacheLength);

				return bool.Parse(cachedValue);
			}

			return bool.Parse(await getSetting());
		}

		public async Task<bool> GetDisableAutoAvailableForDepartmentByUserIdAsync(string userId)
		{
			var settingValue = await GetSettingBySettingTypeAsync(userId, DepartmentSettingTypes.DisabledAutoAvailable);

			if (settingValue != null)
				return bool.Parse(settingValue.Setting);

			return false;
		}

		public async Task<string> GetTextToCallNumberForDepartmentAsync(int departmentId)
		{
			var settingValue = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.TextToCallNumber);

			if (settingValue != null)
				return settingValue.Setting;

			return null;
		}

		public async Task<int?> GetTextToCallImportFormatForDepartmentAsync(int departmentId)
		{
			var settingValue = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.TextToCallImportFormat);

			if (settingValue != null)
				return int.Parse(settingValue.Setting);

			return null;
		}

		public async Task<string> GetTextToCallSourceNumbersForDepartmentAsync(int departmentId)
		{
			var settingValue = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.TextToCallSourceNumbers);

			if (settingValue != null)
				return settingValue.Setting;

			return null;
		}

		public async Task<int?> GetDepartmentIdByTextToCallNumberAsync(string phoneNumber)
		{
			var settingValue = await GetSettingBySettingTypeAsync(phoneNumber, DepartmentSettingTypes.TextToCallNumber);

			if (settingValue != null)
				return settingValue.DepartmentId;

			return null;
		}

		public async Task<bool> GetDepartmentIsTextCallImportEnabledAsync(int departmentId)
		{
			var settingValue = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.EnableTextToCall);

			if (settingValue != null)
				return bool.Parse(settingValue.Setting);

			return false;
		}

		public async Task<bool> GetDepartmentIsTextCommandEnabledAsync(int departmentId)
		{
			var settingValue = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.EnableTextCommand);

			if (settingValue != null)
				return bool.Parse(settingValue.Setting);

			return false;
		}

		public async Task<int?> GetDepartmentIdForDispatchEmailAsync(string emailAddress)
		{
			var settingValue = await GetSettingBySettingTypeAsync(emailAddress, DepartmentSettingTypes.InternalDispatchEmail);

			if (settingValue != null)
				return settingValue.DepartmentId;

			return null;
		}

		public async Task<string> GetDispatchEmailForDepartmentAsync(int departmentId)
		{
			var settingValue = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.InternalDispatchEmail);

			if (settingValue != null)
				return settingValue.Setting;

			return null;
		}

		public async Task<DateTime> GetDepartmentUpdateTimestampAsync(int departmentId)
		{
			var settingValue = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.UpdateTimestamp);

			if (settingValue != null)
				return DateTime.Parse(settingValue.Setting);

			return DateTime.MinValue;
		}

		public async Task<PersonnelSortOrders> GetDepartmentPersonnelSortOrderAsync(int departmentId)
		{
			var settingValue = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.PersonnelSortOrder);

			if (settingValue != null)
				return (PersonnelSortOrders)int.Parse(settingValue.Setting);

			return PersonnelSortOrders.Default;
		}

		public async Task<UnitSortOrders> GetDepartmentUnitsSortOrderAsync(int departmentId)
		{
			var settingValue = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.UnitsSortOrder);

			if (settingValue != null)
				return (UnitSortOrders)int.Parse(settingValue.Setting);

			return UnitSortOrders.Default;
		}

		public async Task<CallSortOrders> GetDepartmentCallSortOrderAsync(int departmentId)
		{
			var settingValue = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.CallsSortOrder);

			if (settingValue != null)
				return (CallSortOrders)int.Parse(settingValue.Setting);

			return CallSortOrders.Default;
		}

		public async Task<List<DepartmentManagerInfo>> GetAllDepartmentManagerInfoAsync()
		{
			return await _departmentSettingsRepository.GetAllDepartmentManagerInfoAsync();
		}

		public async Task<DepartmentManagerInfo> GetDepartmentManagerInfoByEmailAsync(string emailAddress)
		{
			return await _departmentSettingsRepository.GetDepartmentManagerInfoByEmailAsync(emailAddress);
		}

		public async Task<List<PersonnelListStatusOrder>> GetDepartmentPersonnelListStatusSortOrderAsync(int departmentId)
		{
			var settingValue = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.PersonnelListStatusSortOrder);

			if (settingValue != null)
			{
				var setting = ObjectSerialization.Deserialize<PersonnelListStatusOrderSetting>(settingValue.Setting);

				if (setting != null)
					return setting.Orders;
			}
			return null;
		}

		public async Task<DepartmentSetting> SetDepartmentPersonnelListStatusSortOrderAsync(int departmentId, List<PersonnelListStatusOrder> orders, CancellationToken cancellationToken = default(CancellationToken))
		{
			var setting = new PersonnelListStatusOrderSetting();
			setting.Orders = orders;

			return await SaveOrUpdateSettingAsync(departmentId, ObjectSerialization.Serialize(setting),
				DepartmentSettingTypes.PersonnelListStatusSortOrder, cancellationToken);
		}

		#region Shift Group Dispatch Settings
		public async Task<bool> GetDispatchShiftInsteadOfGroupAsync(int departmentId)
		{
			var settingValue = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.DispatchShiftInsteadOfGroup);

			if (settingValue != null)
				return bool.Parse(settingValue.Setting);

			return false;
		}

		public async Task<bool> GetAutoSetStatusForShiftDispatchPersonnelAsync(int departmentId)
		{
			var settingValue = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.AutoSetStatusForShiftDispatchPersonnel);

			if (settingValue != null)
				return bool.Parse(settingValue.Setting);

			return false;
		}

		public async Task<int> GetShiftCallDispatchPersonnelStatusToSetAsync(int departmentId)
		{
			var settingValue = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.ShiftCallDispatchPersonnelStatusToSet);

			if (settingValue != null)
				return int.Parse(settingValue.Setting);

			return -1;
		}

		public async Task<int> GetShiftCallReleasePersonnelStatusToSetAsync(int departmentId)
		{
			var settingValue = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.ShiftCallReleasePersonnelStatusToSet);

			if (settingValue != null)
				return int.Parse(settingValue.Setting);

			return -1;
		}

		public async Task<bool> GetAllowSignupsForMultipleShiftGroupsAsync(int departmentId)
		{
			var settingValue = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.AllowSignupsForMultipleShiftGroups);

			if (settingValue != null)
				return bool.Parse(settingValue.Setting);

			return false;
		}
		#endregion Shift Group Dispatch Settings

		#region Department Mapping Settings
		public async Task<int> GetMappingPersonnelLocationTTLAsync(int departmentId)
		{
			var settingValue = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.MappingPersonnelLocationTTL);

			if (settingValue != null)
				return int.Parse(settingValue.Setting);

			return 0;
		}

		public async Task<int> GetMappingUnitLocationTTLAsync(int departmentId)
		{
			var settingValue = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.MappingUnitLocationTTL);

			if (settingValue != null)
				return int.Parse(settingValue.Setting);

			return 0;
		}

		public async Task<bool> GetMappingPersonnelAllowStatusWithNoLocationToOverwriteAsync(int departmentId)
		{
			var settingValue = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.MappingPersonnelAllowStatusWithNoLocationToOverwrite);

			if (settingValue != null)
				return bool.Parse(settingValue.Setting);

			return false;
		}

		public async Task<bool> GetMappingUnitAllowStatusWithNoLocationToOverwriteAsync(int departmentId)
		{
			var settingValue = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.MappingUnitAllowStatusWithNoLocationToOverwrite);

			if (settingValue != null)
				return bool.Parse(settingValue.Setting);

			return false;
		}
		#endregion Department Mapping Settings

		private async Task<DepartmentSetting> GetSettingByDepartmentIdType(int departmentId, DepartmentSettingTypes settingType)
		{
			return await _departmentSettingsRepository.GetDepartmentSettingByIdTypeAsync(departmentId, settingType);
		}

		private async Task<DepartmentSetting> GetSettingBySettingTypeAsync(string setting, DepartmentSettingTypes settingType)
		{
			return await _departmentSettingsRepository.GetDepartmentSettingBySettingTypeAsync(setting, settingType);
		}

		public async Task<DepartmentSuppressStaffingInfo> GetDepartmentStaffingSuppressInfoAsync(int departmentId, bool bypassCache = false)
		{
			async Task<DepartmentSuppressStaffingInfo> getSetting()
			{
				var actualSetting = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.StaffingSuppressStaffingLevels);

				if (actualSetting != null)
				{
					var setting = ObjectSerialization.Deserialize<DepartmentSuppressStaffingInfo>(actualSetting.Setting);

					if (setting != null)
						return setting;
					else
						return new DepartmentSuppressStaffingInfo();
				}

				return new DepartmentSuppressStaffingInfo();
			}

			if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
			{
				var cachedValue = await _cacheProvider.RetrieveAsync<DepartmentSuppressStaffingInfo>(string.Format(StaffingSupressInfo, departmentId),
					getSetting, ThatsNotLongThisIsLongCacheLength);

				return cachedValue;
			}

			return await getSetting();
		}

		public async Task<DepartmentModuleSettings> GetDepartmentModuleSettingsAsync(int departmentId, bool bypassCache = false)
		{
			async Task<DepartmentModuleSettings> getSetting()
			{
				var actualSetting = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.ModuleSettings);

				if (actualSetting != null)
				{
					var setting = ObjectSerialization.Deserialize<DepartmentModuleSettings>(actualSetting.Setting);

					if (setting != null)
						return setting;
					else
						return new DepartmentModuleSettings();
				}

				return new DepartmentModuleSettings();
			}

			if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
			{
				var cachedValue = await _cacheProvider.RetrieveAsync<DepartmentModuleSettings>(string.Format(StaffingSupressInfo, departmentId),
					getSetting, ThatsNotLongThisIsLongCacheLength);

				return cachedValue;
			}

			return await getSetting();
		}

		public async Task<bool> GetUnitDispatchAlsoDispatchToAssignedPersonnelAsync(int departmentId)
		{
			var settingValue = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.UnitDispatchAlsoDispatchToAssignedPersonnel);

			if (settingValue != null)
				return bool.Parse(settingValue.Setting);

			return false;
		}

		public async Task<bool> GetUnitDispatchAlsoDispatchToGroupAsync(int departmentId)
		{
			var settingValue = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.UnitDispatchAlsoDispatchToGroup);

			if (settingValue != null)
				return bool.Parse(settingValue.Setting);

			return false;
		}

		public async Task<DepartmentSetting> SetDepartmentModuleSettingsAsync(int departmentId, DepartmentModuleSettings settings, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await SaveOrUpdateSettingAsync(departmentId, ObjectSerialization.Serialize(settings),
				DepartmentSettingTypes.ModuleSettings, cancellationToken);
		}
	}
}
