using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
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
		private static string PaddleCustomerCacheKey = "DSetPaddleCus_{0}";
		private static string BigBoardCenterGps = "DSetBBCenterGps_{0}";
		private static string StaffingSupressInfo = "DSetStaffingSupress_{0}";
		private static string ModuleSettingsCacheKey = "DSetModuleSettings_{0}";
		private static string TtsLanguageCacheKey = "DSetTtsLanguage_{0}";
		private static string PersonnelOnUnitSetUnitStatusCacheKey = "DSetPersonnelOnUnitSetUnitStatus_{0}";
		private static string ModernNotificationsCacheKey = "DSetModernNotifications_{0}";
		private static string ForceChatbotSecurityPinCacheKey = "DSetForceChatbotSecurityPin_{0}";
		private static string HardwareTrackingStaleAfterSecondsCacheKey = "DSetHardwareTrackingStale_{0}";
		private static string HardwareTrackingMobileFallbackCacheKey = "DSetHardwareTrackingFallback_{0}";
		private static string HardwareTrackingRetentionDaysCacheKey = "DSetHardwareTrackingRetention_{0}";
		private static string DispatchRecommendationModeCacheKey = "DSetDispatchRecMode_{0}";
		private static string DispatchRecommendationAutoDispatchCacheKey = "DSetDispatchRecAuto_{0}";
		private static string DispatchRecommendationConfigCacheKey = "DSetDispatchRecConfig_{0}";
		private static string NewCallFieldPolicyCacheKey = "DSetNewCallFieldPolicy_{0}";
		private static string UnitStatusThresholdsCacheKey = "DSetUnitStatusThresholds_{0}";
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

			DepartmentSetting result;

			if (savedSetting == null)
			{
				DepartmentSetting newSetting = new DepartmentSetting();
				newSetting.DepartmentId = departmentId;
				newSetting.Setting = setting;
				newSetting.SettingType = (int)type;

				result = await _departmentSettingsRepository.SaveOrUpdateAsync(newSetting, cancellationToken);
			}
			else
			{
				savedSetting.Setting = setting;
				result = await _departmentSettingsRepository.SaveOrUpdateAsync(savedSetting, cancellationToken);
			}

			// Invalidate after the write commits, never before: dropping the key first lets a
			// concurrent reader miss, re-read the pre-write value from the database and store
			// it again, where it then survives for the full cache TTL. A throwing write skips
			// this and leaves the still-correct cached value in place. Mirrors DeleteSettingAsync.
			await InvalidateSettingCacheAsync(departmentId, type);

			return result;
		}

		public async Task<bool> DeleteSettingAsync(int departmentId, DepartmentSettingTypes type, CancellationToken cancellationToken = default(CancellationToken))
		{
			var savedSetting = await GetSettingByDepartmentIdType(departmentId, type);

			if (savedSetting != null)
			{
				var deleted = await _departmentSettingsRepository.DeleteAsync(savedSetting, cancellationToken);
				if (deleted)
					await InvalidateSettingCacheAsync(departmentId, type);

				return deleted;
			}

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

		public async Task<Coordinates> SaveMapCenterCoordinatesAsync(int departmentId, string latitude, string longitude, Address address,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			// Operator-supplied coordinates are authoritative. Someone who dropped a pin on the exact
			// spot they want their boards centred on must never have it moved by a geocoder.
			if (!String.IsNullOrWhiteSpace(latitude) && !String.IsNullOrWhiteSpace(longitude))
			{
				var sanitizedLatitude = StringHelpers.SanitizeCoordinatesString(latitude);
				var sanitizedLongitude = StringHelpers.SanitizeCoordinatesString(longitude);

				await SaveOrUpdateSettingAsync(departmentId, $"{sanitizedLatitude},{sanitizedLongitude}",
					DepartmentSettingTypes.BigBoardMapCenterGpsCoordinates, cancellationToken);

				if (double.TryParse(sanitizedLatitude, out var storedLatitude) && double.TryParse(sanitizedLongitude, out var storedLongitude))
					return new Coordinates { Latitude = storedLatitude, Longitude = storedLongitude };

				return null;
			}

			// Only one of the two filled in is an operator mid-edit, not an instruction to geocode.
			// Leaving the stored value alone is the safe reading.
			if (!String.IsNullOrWhiteSpace(latitude) || !String.IsNullOrWhiteSpace(longitude))
				return null;

			if (address == null || String.IsNullOrWhiteSpace(address.Address1))
				return null;

			var geocoded = await GeocodeAddressAsync(address);

			if (geocoded == null)
				return null;

			await SaveOrUpdateSettingAsync(departmentId,
				$"{geocoded.Latitude.Value.ToString(CultureInfo.InvariantCulture)},{geocoded.Longitude.Value.ToString(CultureInfo.InvariantCulture)}",
				DepartmentSettingTypes.BigBoardMapCenterGpsCoordinates, cancellationToken);

			return geocoded;
		}

		/// <summary>
		/// Geocodes an address into coordinates, or null when the provider could not resolve it.
		/// Failures are not fatal anywhere this is used -- the caller falls back to its own default.
		/// </summary>
		private async Task<Coordinates> GeocodeAddressAsync(Address address)
		{
			try
			{
				var result = await _geoLocationProvider.GetLatLonFromAddress(
					$"{address.Address1} {address.City} {address.State} {address.PostalCode}");

				if (String.IsNullOrWhiteSpace(result))
					return null;

				var parts = result.Split(char.Parse(","));

				if (parts.Length != 2)
					return null;

				if (double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var latitude) &&
					double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var longitude))
					return new Coordinates { Latitude = latitude, Longitude = longitude };
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"{nameof(GeocodeAddressAsync)} failed resolving a department address.");
			}

			return null;
		}

		public async Task<Coordinates> GetMapCenterCoordinatesAsync(Department department)
		{
			if (department == null)
				return new Coordinates() { Latitude = 39.14086268299356, Longitude = -119.7583809782715 };

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

		public async Task<string> GetTtsLanguageForDepartmentAsync(int departmentId)
		{
			async Task<string> getSetting()
			{
				var settingValue = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.TtsLanguage);

				if (settingValue != null && EspeakVoiceCatalog.TryNormalizeIdentifier(settingValue.Setting, out var normalizedSetting))
					return normalizedSetting;

				return GetDefaultTtsLanguage();
			}

			if (Config.SystemBehaviorConfig.CacheEnabled)
			{
				return await _cacheProvider.RetrieveAsync(string.Format(TtsLanguageCacheKey, departmentId),
					getSetting, LongCacheLength);
			}

			return await getSetting();
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

		public async Task<bool> GetMappingUseMapboxOverrideAsync(int departmentId)
		{
			var settingValue = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.MappingUseMapboxOverride);

			if (settingValue != null && bool.TryParse(settingValue.Setting, out bool enabled))
				return enabled;

			return false;
		}

		public async Task<string> GetMappingMapboxStyleUrlAsync(int departmentId)
		{
			var settingValue = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.MappingMapboxStyleUrl);

			return settingValue?.Setting;
		}

		public async Task<string> GetMappingMapboxAccessTokenAsync(int departmentId)
		{
			var settingValue = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.MappingMapboxAccessToken);

			return settingValue?.Setting;
		}

		public async Task<ResolvedMapConfig> GetMapConfigForDepartmentAsync(int departmentId, string key = null)
		{
			if (departmentId > 0 && await GetMappingUseMapboxOverrideAsync(departmentId))
			{
				var styleUrl = await GetMappingMapboxStyleUrlAsync(departmentId);
				var accessToken = await GetMappingMapboxAccessTokenAsync(departmentId);

				if (MappingConfig.TryCreateMapboxConfig(styleUrl, accessToken, true, out var mapConfig))
					return mapConfig;
			}

			return MappingConfig.GetMapConfig(key);
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
				var cachedValue = await _cacheProvider.RetrieveAsync<DepartmentModuleSettings>(string.Format(ModuleSettingsCacheKey, departmentId),
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

		public async Task<int> GetUnitCallDispatchStatusToSetAsync(int departmentId)
		{
			var settingValue = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.UnitCallDispatchStatusToSet);

			if (settingValue != null && int.TryParse(settingValue.Setting, out var stateToSet) &&
				Enum.IsDefined(typeof(UnitStateTypes), stateToSet))
				return stateToSet;

			return -1;
		}

		public async Task<int> GetUnitCallReleaseStatusToSetAsync(int departmentId)
		{
			var settingValue = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.UnitCallReleaseStatusToSet);

			if (settingValue != null && int.TryParse(settingValue.Setting, out var stateToSet) &&
				Enum.IsDefined(typeof(UnitStateTypes), stateToSet))
				return stateToSet;

			return -1;
		}

		public async Task<List<UnitTypeCallStatusOverride>> GetUnitCallStatusOverridesByUnitTypeAsync(int departmentId)
		{
			var settingValue = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.UnitCallStatusOverridesByUnitType);

			if (settingValue != null && !String.IsNullOrWhiteSpace(settingValue.Setting))
			{
				var setting = ObjectSerialization.Deserialize<UnitTypeCallStatusOverrideSetting>(settingValue.Setting);

				if (setting?.Overrides != null)
					return setting.Overrides
						.Where(x => x != null && x.UnitTypeId > 0)
						.GroupBy(x => x.UnitTypeId)
						.Select(x => x.Last())
						.ToList();
			}

			return new List<UnitTypeCallStatusOverride>();
		}

		public async Task<DepartmentSetting> SetUnitCallStatusOverridesByUnitTypeAsync(int departmentId,
			List<UnitTypeCallStatusOverride> overrides, CancellationToken cancellationToken = default(CancellationToken))
		{
			var setting = new UnitTypeCallStatusOverrideSetting
			{
				Overrides = overrides?
					.Where(x => x != null && x.UnitTypeId > 0)
					.GroupBy(x => x.UnitTypeId)
					.Select(x => x.Last())
					.ToList() ?? new List<UnitTypeCallStatusOverride>()
			};

			return await SaveOrUpdateSettingAsync(departmentId, ObjectSerialization.Serialize(setting),
				DepartmentSettingTypes.UnitCallStatusOverridesByUnitType, cancellationToken);
		}

		public async Task<DispatchRecommendationModes> GetDispatchRecommendationModeAsync(int departmentId, bool bypassCache = false)
		{
			async Task<string> getSetting()
			{
				var s = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.DispatchRecommendationMode);
				return s?.Setting ?? ((int)DispatchRecommendationModes.Off).ToString();
			}

			string value;
			if (Config.SystemBehaviorConfig.CacheEnabled && !bypassCache)
				value = await _cacheProvider.RetrieveAsync<string>(string.Format(DispatchRecommendationModeCacheKey, departmentId), getSetting, LongCacheLength);
			else
				value = await getSetting();

			if (int.TryParse(value, out var mode) && Enum.IsDefined(typeof(DispatchRecommendationModes), mode))
				return (DispatchRecommendationModes)mode;

			return DispatchRecommendationModes.Off;
		}

		public async Task<DepartmentSetting> SetDispatchRecommendationModeAsync(int departmentId, DispatchRecommendationModes mode, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await SaveOrUpdateSettingAsync(departmentId, ((int)mode).ToString(), DepartmentSettingTypes.DispatchRecommendationMode, cancellationToken);
		}

		public async Task<bool> GetDispatchRecommendationAutoDispatchAsync(int departmentId, bool bypassCache = false)
		{
			async Task<string> getSetting()
			{
				var s = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.DispatchRecommendationAutoDispatch);
				return s?.Setting ?? "false";
			}

			string value;
			if (Config.SystemBehaviorConfig.CacheEnabled && !bypassCache)
				value = await _cacheProvider.RetrieveAsync<string>(string.Format(DispatchRecommendationAutoDispatchCacheKey, departmentId), getSetting, LongCacheLength);
			else
				value = await getSetting();

			return bool.TryParse(value, out var enabled) && enabled;
		}

		public async Task<DepartmentSetting> SetDispatchRecommendationAutoDispatchAsync(int departmentId, bool enabled, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await SaveOrUpdateSettingAsync(departmentId, enabled.ToString(), DepartmentSettingTypes.DispatchRecommendationAutoDispatch, cancellationToken);
		}

		public async Task<UnitStatusThresholds> GetUnitStatusThresholdsAsync(int departmentId, bool bypassCache = false)
		{
			async Task<string> getSetting()
			{
				var setting = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.UnitStatusThresholds);
				return setting?.Setting ?? string.Empty;
			}

			string value;
			if (Config.SystemBehaviorConfig.CacheEnabled && !bypassCache)
				value = await _cacheProvider.RetrieveAsync<string>(string.Format(UnitStatusThresholdsCacheKey, departmentId), getSetting, LongCacheLength);
			else
				value = await getSetting();

			if (!String.IsNullOrWhiteSpace(value))
			{
				try
				{
					var thresholds = ObjectSerialization.Deserialize<UnitStatusThresholds>(value);

					if (thresholds != null)
						return thresholds.Normalize();
				}
				catch (Exception)
				{
					// A corrupt blob falls back to "no highlighting", which is the pre-feature behaviour
					// and can never make the board misleading.
				}
			}

			return new UnitStatusThresholds();
		}

		public async Task<UnitStatusThresholds> SaveUnitStatusThresholdsAsync(int departmentId, UnitStatusThresholds thresholds,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			var normalized = (thresholds ?? new UnitStatusThresholds()).Normalize();

			if (normalized.IsEmpty)
				await DeleteSettingAsync(departmentId, DepartmentSettingTypes.UnitStatusThresholds, cancellationToken);
			else
				await SaveOrUpdateSettingAsync(departmentId, ObjectSerialization.Serialize(normalized),
					DepartmentSettingTypes.UnitStatusThresholds, cancellationToken);

			return normalized;
		}

		public async Task<NewCallFieldPolicy> GetNewCallFieldPolicyAsync(int departmentId, bool bypassCache = false)
		{
			async Task<string> getSetting()
			{
				var setting = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.NewCallFieldPolicy);
				return setting?.Setting ?? string.Empty;
			}

			string value;
			if (Config.SystemBehaviorConfig.CacheEnabled && !bypassCache)
				value = await _cacheProvider.RetrieveAsync<string>(string.Format(NewCallFieldPolicyCacheKey, departmentId), getSetting, LongCacheLength);
			else
				value = await getSetting();

			if (!String.IsNullOrWhiteSpace(value))
			{
				try
				{
					var policy = ObjectSerialization.Deserialize<NewCallFieldPolicy>(value);

					if (policy != null)
						return policy.Normalize();
				}
				catch (Exception)
				{
					// A corrupt blob must never stop a department creating calls; fall back to stock
					// behaviour (everything visible, nothing required).
				}
			}

			return new NewCallFieldPolicy();
		}

		public async Task<NewCallFieldPolicy> SaveNewCallFieldPolicyAsync(int departmentId, NewCallFieldPolicy policy,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			var normalized = (policy ?? new NewCallFieldPolicy()).Normalize();

			// Nothing worth storing means stock behaviour; drop the setting rather than persisting an
			// empty blob that later readers have to interpret.
			if (normalized.IsEmpty)
				await DeleteSettingAsync(departmentId, DepartmentSettingTypes.NewCallFieldPolicy, cancellationToken);
			else
				await SaveOrUpdateSettingAsync(departmentId, ObjectSerialization.Serialize(normalized),
					DepartmentSettingTypes.NewCallFieldPolicy, cancellationToken);

			return normalized;
		}

		public async Task<DispatchRecommendationConfig> GetDispatchRecommendationConfigAsync(int departmentId, bool bypassCache = false)
		{
			async Task<string> getSetting()
			{
				var s = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.DispatchRecommendationConfig);
				return s?.Setting ?? string.Empty;
			}

			string value;
			if (Config.SystemBehaviorConfig.CacheEnabled && !bypassCache)
				value = await _cacheProvider.RetrieveAsync<string>(string.Format(DispatchRecommendationConfigCacheKey, departmentId), getSetting, LongCacheLength);
			else
				value = await getSetting();

			if (!String.IsNullOrWhiteSpace(value))
			{
				try
				{
					var config = ObjectSerialization.Deserialize<DispatchRecommendationConfig>(value);

					if (config != null)
						return ClampDispatchRecommendationConfig(config);
				}
				catch (Exception)
				{
					// A corrupt setting blob falls back to defaults rather than breaking dispatch.
				}
			}

			return new DispatchRecommendationConfig();
		}

		/// <summary>
		/// Bounds the tuning values on the way out, the way retention days are bounded in
		/// GetHardwareTrackingLocationRetentionDaysAsync. Clamping on read rather than on
		/// save also covers values already stored and any writer other than the settings
		/// page. Zero keeps its "no limit" meaning for the age and radius knobs.
		/// </summary>
		private static DispatchRecommendationConfig ClampDispatchRecommendationConfig(DispatchRecommendationConfig config)
		{
			config.MaxLocationAgeSeconds = ClampToRange(config.MaxLocationAgeSeconds, DispatchRecommendationConfig.MaximumLocationAgeSeconds);
			config.PersonnelMaxLocationAgeSeconds = ClampToRange(config.PersonnelMaxLocationAgeSeconds, DispatchRecommendationConfig.MaximumLocationAgeSeconds);
			config.MaxRadiusMeters = ClampToRange(config.MaxRadiusMeters, DispatchRecommendationConfig.MaximumRadiusMeters);
			config.RestPeriodMinutes = ClampToRange(config.RestPeriodMinutes, DispatchRecommendationConfig.MaximumRestPeriodMinutes);

			config.EtaShortlistSize = config.EtaShortlistSize > 0
				? Math.Min(config.EtaShortlistSize, DispatchRecommendationConfig.MaximumEtaShortlistSize)
				: DispatchRecommendationConfig.DefaultEtaShortlistSize;

			if (config.UnitMinimumStaffingLevel < 0)
				config.UnitMinimumStaffingLevel = 0;

			return config;
		}

		private static int ClampToRange(int value, int maximum)
		{
			if (value <= 0)
				return 0;

			return Math.Min(value, maximum);
		}

		public async Task<DepartmentSetting> SetDispatchRecommendationConfigAsync(int departmentId, DispatchRecommendationConfig config, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (config == null)
				config = new DispatchRecommendationConfig();

			return await SaveOrUpdateSettingAsync(departmentId, ObjectSerialization.Serialize(config),
				DepartmentSettingTypes.DispatchRecommendationConfig, cancellationToken);
		}

		public async Task<bool> GetPersonnelOnUnitSetUnitStatusAsync(int departmentId, bool bypassCache = false)
		{
			async Task<string> getSetting()
			{
				var s = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.PersonnelOnUnitSetUnitStatus);
				return s?.Setting ?? "false";
			}

			if (Config.SystemBehaviorConfig.CacheEnabled && !bypassCache)
			{
				var cachedValue = await _cacheProvider.RetrieveAsync<string>(string.Format(PersonnelOnUnitSetUnitStatusCacheKey, departmentId), getSetting, LongCacheLength);
				return bool.Parse(cachedValue);
			}

			return bool.Parse(await getSetting());
		}

		public async Task<DepartmentSetting> SetDepartmentModuleSettingsAsync(int departmentId, DepartmentModuleSettings settings, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await SaveOrUpdateSettingAsync(departmentId, ObjectSerialization.Serialize(settings),
				DepartmentSettingTypes.ModuleSettings, cancellationToken);
		}

		public async Task<int> GetRequire2FAForAdminsAsync(int departmentId)
		{
			var settingValue = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.Require2FAForAdmins);

			if (settingValue != null && int.TryParse(settingValue.Setting, out var scope))
				return scope;

			return 0; // Disabled by default
		}

		public async Task<string> GetPaddleCustomerIdForDepartmentAsync(int departmentId)
		{
			var settingValue = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.PaddleCustomerId);

			if (settingValue != null)
				return settingValue.Setting;

			return String.Empty;
		}

		public async Task<int?> GetDepartmentIdForPaddleCustomerIdAsync(string paddleCustomerId, bool bypassCache = false)
		{
			DepartmentSetting key;

			async Task<DepartmentSetting> getSetting()
			{
				return await _departmentSettingsRepository.GetDepartmentSettingBySettingTypeAsync(paddleCustomerId, DepartmentSettingTypes.PaddleCustomerId);
			}

			if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
			{
				key = await _cacheProvider.RetrieveAsync<DepartmentSetting>(string.Format(PaddleCustomerCacheKey, paddleCustomerId),
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

		public async Task<bool> GetCheckInTimersAutoEnableForNewCallsAsync(int departmentId)
		{
			var s = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.CheckInTimersAutoEnableForNewCalls);

			if (s != null && bool.TryParse(s.Setting, out bool result))
				return result;

			return false;
		}

		public async Task<DepartmentSetting> GetSettingByTypeAsync(int departmentId, DepartmentSettingTypes type)
		{
			return await GetSettingByDepartmentIdType(departmentId, type);
		}

		public async Task<bool> GetModernNotificationsEnabledAsync(int departmentId, bool bypassCache = false)
		{
			async Task<string> getSetting()
			{
				var s = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.EnableModernNotifications);
				return s?.Setting ?? "false";
			}

			if (Config.SystemBehaviorConfig.CacheEnabled && !bypassCache)
			{
				var cachedValue = await _cacheProvider.RetrieveAsync<string>(string.Format(ModernNotificationsCacheKey, departmentId), getSetting, LongCacheLength);
				return bool.Parse(cachedValue);
			}

			return bool.Parse(await getSetting());
		}

		public async Task<bool> GetForceChatbotSecurityPinAsync(int departmentId, bool bypassCache = false)
		{
			async Task<string> getSetting()
			{
				var s = await GetSettingByDepartmentIdType(departmentId, DepartmentSettingTypes.ForceChatbotSecurityPin);
				return s?.Setting ?? "false";
			}

			if (Config.SystemBehaviorConfig.CacheEnabled && !bypassCache)
			{
				var cachedValue = await _cacheProvider.RetrieveAsync<string>(string.Format(ForceChatbotSecurityPinCacheKey, departmentId), getSetting, LongCacheLength);
				return bool.Parse(cachedValue);
			}

			return bool.Parse(await getSetting());
		}

		public async Task<int> GetHardwareTrackingStaleAfterSecondsAsync(int departmentId, bool bypassCache = false)
		{
			async Task<string> getSetting()
			{
				var setting = await GetSettingByDepartmentIdType(
					departmentId,
					DepartmentSettingTypes.HardwareTrackingStaleAfterSeconds);
				return setting?.Setting ?? "180";
			}

			var value = Config.SystemBehaviorConfig.CacheEnabled && !bypassCache
				? await _cacheProvider.RetrieveAsync(
					string.Format(HardwareTrackingStaleAfterSecondsCacheKey, departmentId),
					getSetting,
					LongCacheLength)
				: await getSetting();

			return int.TryParse(value, out var seconds) ? Math.Max(1, seconds) : 180;
		}

		public async Task<bool> GetHardwareTrackingMobileFallbackEnabledAsync(int departmentId, bool bypassCache = false)
		{
			async Task<string> getSetting()
			{
				var setting = await GetSettingByDepartmentIdType(
					departmentId,
					DepartmentSettingTypes.HardwareTrackingMobileFallbackEnabled);
				return setting?.Setting ?? "true";
			}

			var value = Config.SystemBehaviorConfig.CacheEnabled && !bypassCache
				? await _cacheProvider.RetrieveAsync(
					string.Format(HardwareTrackingMobileFallbackCacheKey, departmentId),
					getSetting,
					LongCacheLength)
				: await getSetting();

			return !bool.TryParse(value, out var enabled) || enabled;
		}

		public async Task<int> GetHardwareTrackingLocationRetentionDaysAsync(int departmentId, bool bypassCache = false)
		{
			async Task<string> getSetting()
			{
				var setting = await GetSettingByDepartmentIdType(
					departmentId,
					DepartmentSettingTypes.HardwareTrackingLocationRetentionDays);
				return setting?.Setting ?? UnitTrackingConfig.DefaultLocationRetentionDays.ToString();
			}

			var value = Config.SystemBehaviorConfig.CacheEnabled && !bypassCache
				? await _cacheProvider.RetrieveAsync(
					string.Format(HardwareTrackingRetentionDaysCacheKey, departmentId),
					getSetting,
					LongCacheLength)
				: await getSetting();

			var retentionDays = int.TryParse(value, out var parsed)
				? parsed
				: UnitTrackingConfig.DefaultLocationRetentionDays;

			return Math.Min(
				UnitTrackingConfig.MaximumLocationRetentionDays,
				Math.Max(UnitTrackingConfig.MinimumLocationRetentionDays, retentionDays));
		}

		private async Task InvalidateSettingCacheAsync(int departmentId, DepartmentSettingTypes type)
		{
			string cacheKey = null;

			switch (type)
			{
				case DepartmentSettingTypes.BigBoardMapCenterGpsCoordinates:
					cacheKey = string.Format(BigBoardCenterGps, departmentId);
					break;
				case DepartmentSettingTypes.DisabledAutoAvailable:
					cacheKey = string.Format(DisableAutoAvailableCacheKey, departmentId);
					break;
				case DepartmentSettingTypes.StaffingSuppressStaffingLevels:
					cacheKey = string.Format(StaffingSupressInfo, departmentId);
					break;
				case DepartmentSettingTypes.ModuleSettings:
					cacheKey = string.Format(ModuleSettingsCacheKey, departmentId);
					break;
				case DepartmentSettingTypes.TtsLanguage:
					cacheKey = string.Format(TtsLanguageCacheKey, departmentId);
					break;
				case DepartmentSettingTypes.PersonnelOnUnitSetUnitStatus:
					cacheKey = string.Format(PersonnelOnUnitSetUnitStatusCacheKey, departmentId);
					break;
				case DepartmentSettingTypes.EnableModernNotifications:
					cacheKey = string.Format(ModernNotificationsCacheKey, departmentId);
					break;
				case DepartmentSettingTypes.ForceChatbotSecurityPin:
					cacheKey = string.Format(ForceChatbotSecurityPinCacheKey, departmentId);
					break;
				case DepartmentSettingTypes.HardwareTrackingStaleAfterSeconds:
					cacheKey = string.Format(HardwareTrackingStaleAfterSecondsCacheKey, departmentId);
					break;
				case DepartmentSettingTypes.HardwareTrackingMobileFallbackEnabled:
					cacheKey = string.Format(HardwareTrackingMobileFallbackCacheKey, departmentId);
					break;
				case DepartmentSettingTypes.HardwareTrackingLocationRetentionDays:
					cacheKey = string.Format(HardwareTrackingRetentionDaysCacheKey, departmentId);
					break;
				case DepartmentSettingTypes.DispatchRecommendationMode:
					cacheKey = string.Format(DispatchRecommendationModeCacheKey, departmentId);
					break;
				case DepartmentSettingTypes.DispatchRecommendationAutoDispatch:
					cacheKey = string.Format(DispatchRecommendationAutoDispatchCacheKey, departmentId);
					break;
				case DepartmentSettingTypes.DispatchRecommendationConfig:
					cacheKey = string.Format(DispatchRecommendationConfigCacheKey, departmentId);
					break;
				case DepartmentSettingTypes.NewCallFieldPolicy:
					cacheKey = string.Format(NewCallFieldPolicyCacheKey, departmentId);
					break;
				case DepartmentSettingTypes.UnitStatusThresholds:
					cacheKey = string.Format(UnitStatusThresholdsCacheKey, departmentId);
					break;
			}

			if (!string.IsNullOrWhiteSpace(cacheKey))
				await _cacheProvider.RemoveAsync(cacheKey);
		}

		private static string GetDefaultTtsLanguage()
		{
			if (EspeakVoiceCatalog.TryNormalizeIdentifier(TtsConfig.DefaultVoice, out var normalizedVoice))
				return normalizedVoice;

			if (!string.IsNullOrWhiteSpace(TtsConfig.DefaultVoice))
				return TtsConfig.DefaultVoice.Trim();

			return EspeakVoiceCatalog.DefaultIdentifier;
		}
	}
}
