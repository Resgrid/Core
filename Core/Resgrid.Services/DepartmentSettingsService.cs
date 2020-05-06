using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;

namespace Resgrid.Services
{
	public class DepartmentSettingsService : IDepartmentSettingsService
	{
		private static string DisableAutoAvailableCacheKey = "DSetAutoAvailable_{0}";
		private static string StripeCustomerCacheKey = "DSetStripeCus_{0}";
		private static TimeSpan LongCacheLength = TimeSpan.FromDays(14);
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

		public void SaveOrUpdateSetting(int departmentId, string setting, DepartmentSettingTypes type)
		{
			var savedSetting = (from set in _departmentSettingsRepository.GetAll()
								where set.DepartmentId == departmentId && set.SettingType == (int)type
								select set).FirstOrDefault();

			if (savedSetting == null)
			{
				DepartmentSetting newSetting = new DepartmentSetting();
				newSetting.DepartmentId = departmentId;
				newSetting.Setting = setting;
				newSetting.SettingType = (int)type;

				_departmentSettingsRepository.SaveOrUpdate(newSetting);
			}
			else
			{
				savedSetting.Setting = setting;
				_departmentSettingsRepository.SaveOrUpdate(savedSetting);
			}
		}

		public void DeleteSetting(int departmentId, DepartmentSettingTypes type)
		{
			var savedSetting = (from set in _departmentSettingsRepository.GetAll()
								where set.DepartmentId == departmentId && set.SettingType == (int)type
								select set).FirstOrDefault();

			if (savedSetting != null)
				_departmentSettingsRepository.DeleteOnSubmit(savedSetting);
		}

		public int? GetBigBoardMapZoomLevelForDepartment(int departmentId)
		{
			var zoomLevel = (from setting in _departmentSettingsRepository.GetAll()
							 where setting.DepartmentId == departmentId &&
									setting.SettingType == (int)DepartmentSettingTypes.BigBoardMapZoomLevel
							 select setting).FirstOrDefault();

			if (zoomLevel != null)
				return int.Parse(zoomLevel.Setting);

			return null;
		}

		public int? GetBigBoardRefreshTimeForDepartment(int departmentId)
		{
			var refresh = (from setting in _departmentSettingsRepository.GetAll()
						   where setting.DepartmentId == departmentId &&
							   setting.SettingType == (int)DepartmentSettingTypes.BigBoardPageRefresh
						   select setting).FirstOrDefault();

			if (refresh != null)
				return int.Parse(refresh.Setting);

			return null;
		}

		public Address GetBigBoardCenterAddressDepartment(int departmentId)
		{
			var center = (from setting in _departmentSettingsRepository.GetAll()
						  where setting.DepartmentId == departmentId &&
							  setting.SettingType == (int)DepartmentSettingTypes.BigBoardMapCenterAddress
						  select setting).FirstOrDefault();

			if (center != null)
				return _addressService.GetAddressById(int.Parse(center.Setting));

			return null;
		}

		public string GetBigBoardCenterGpsCoordinatesDepartment(int departmentId)
		{
			var center = (from setting in _departmentSettingsRepository.GetAll()
						  where setting.DepartmentId == departmentId &&
							  setting.SettingType == (int)DepartmentSettingTypes.BigBoardMapCenterGpsCoordinates
						  select setting).FirstOrDefault();

			if (center != null)
			{
				var newLocation = String.Empty;
				var points = center.Setting.Split(char.Parse(","));

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
							newLocation = points[0] + ",";
						}
					}

					if (!String.IsNullOrWhiteSpace(points[1]))
					{
						if (Framework.LocationHelpers.IsDMSLocation(points[1]))
						{
							newLocation = Framework.LocationHelpers.ConvertDegreeAngleToDouble(points[1]).ToString();
						}
						else
						{
							newLocation = points[1];
						}
					}

				}
				else
				{
					newLocation = center.Setting;
				}

				return newLocation;
			}

			return null;
		}

		public bool? GetBigBoardHideUnavailableDepartment(int departmentId)
		{
			var center = (from setting in _departmentSettingsRepository.GetAll()
						  where setting.DepartmentId == departmentId &&
							  setting.SettingType == (int)DepartmentSettingTypes.BigBoardHideUnavailable
						  select setting).FirstOrDefault();

			if (center != null)
				return bool.Parse(center.Setting);

			return null;
		}

		public string GetRssKeyForDepartment(int departmentId)
		{
			var key = (from setting in _departmentSettingsRepository.GetAll()
					   where setting.DepartmentId == departmentId &&
						   setting.SettingType == (int)DepartmentSettingTypes.RssFeedKeyForActiveCalls
					   select setting).FirstOrDefault();

			if (key != null)
				return key.Setting;

			return null;
		}

		public int? GetDepartmentIdForRssKey(string key)
		{
			var department = (from setting in _departmentSettingsRepository.GetAll()
							  where setting.Setting == key &&
								  setting.SettingType == (int)DepartmentSettingTypes.RssFeedKeyForActiveCalls
							  select setting).FirstOrDefault();

			if (department != null)
				return department.DepartmentId;

			return null;
		}

		public string GetStripeCustomerIdForDepartment(int departmentId)
		{
			var key = (from setting in _departmentSettingsRepository.GetAll()
					   where setting.DepartmentId == departmentId &&
						   setting.SettingType == (int)DepartmentSettingTypes.StripeCustomerId
					   select setting).FirstOrDefault();

			if (key != null)
				return key.Setting;

			return null;
		}

		public int? GetDepartmentIdForStripeCustomerId(string stripeCustomerId, bool bypassCache = false)
		{
			DepartmentSetting key;

			Func<DepartmentSetting> getSetting = delegate ()
			{
				return _departmentSettingsRepository.GetDepartmentSettingBySettingType(stripeCustomerId, DepartmentSettingTypes.StripeCustomerId);
			};

			if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
			{
				key = _cacheProvider.Retrieve<DepartmentSetting>(string.Format(StripeCustomerCacheKey, stripeCustomerId),
					getSetting, TwoYearCacheLength);
			}
			else
			{
				key = getSetting();
			}

			if (key != null)
				return key.DepartmentId;

			return null;
		}

		public string GetBrainTreeCustomerIdForDepartment(int departmentId)
		{
			var key = (from setting in _departmentSettingsRepository.GetAll()
					   where setting.DepartmentId == departmentId &&
						   setting.SettingType == (int)DepartmentSettingTypes.BrainTreeCustomerId
					   select setting).FirstOrDefault();

			if (key != null)
				return key.Setting;

			return null;
		}

		public int? GetDepartmentIdForBrainTreeCustomerId(string stripeCustomerId)
		{
			var key = (from setting in _departmentSettingsRepository.GetAll()
					   where setting.Setting == stripeCustomerId &&
						   setting.SettingType == (int)DepartmentSettingTypes.BrainTreeCustomerId
					   select setting).FirstOrDefault();

			if (key != null)
				return key.DepartmentId;

			return null;
		}

		public bool IsTestingEnabledForDepartment(int departmentId)
		{
			var testingEnabled = (from setting in _departmentSettingsRepository.GetAll()
								  where setting.DepartmentId == departmentId &&
									  setting.SettingType == (int)DepartmentSettingTypes.TestEnabled
								  select setting).FirstOrDefault();

			if (testingEnabled != null)
				return bool.Parse(testingEnabled.Setting);

			return false;
		}

		public Coordinates GetMapCenterCoordinates(Department department)
		{
			var address = GetBigBoardCenterAddressDepartment(department.DepartmentId);
			var gpsCoordinates = GetBigBoardCenterGpsCoordinatesDepartment(department.DepartmentId);

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
				string coords = _geoLocationProvider.GetLatLonFromAddress(string.Format("{0} {1} {2} {3}", address.Address1,
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
				string coords = _geoLocationProvider.GetLatLonFromAddress(string.Format("{0} {1} {2} {3}", department.Address.Address1,
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

		public bool GetDisableAutoAvailableForDepartment(int departmentId, bool bypassCache = true)
		{
			if (!bypassCache && Config.SystemBehaviorConfig.CacheEnabled)
			{
				Func<string> getSetting = delegate ()
				{
					var actualSetting = (from setting in _departmentSettingsRepository.GetAll()
										 where setting.DepartmentId == departmentId &&
												 setting.SettingType == (int)DepartmentSettingTypes.DisabledAutoAvailable
										 select setting).FirstOrDefault();

					if (actualSetting != null)
						return actualSetting.Setting;
					else
						return "false";
				};

				var cachedValue = _cacheProvider.Retrieve<string>(string.Format(DisableAutoAvailableCacheKey, departmentId),
					getSetting, LongCacheLength);

				return bool.Parse(cachedValue);
			}

			var settingValue = (from setting in _departmentSettingsRepository.GetAll()
								where setting.DepartmentId == departmentId &&
										setting.SettingType == (int)DepartmentSettingTypes.DisabledAutoAvailable
								select setting).FirstOrDefault();

			if (settingValue != null)
				return bool.Parse(settingValue.Setting);

			return false;
		}

		public bool GetDisableAutoAvailableForDepartmentByUserId(string userId)
		{
			var settingValue = _departmentSettingsRepository.GetDepartmentSettingByUserIdType(userId, DepartmentSettingTypes.DisabledAutoAvailable);

			if (settingValue != null)
				return bool.Parse(settingValue.Setting);

			return false;
		}

		public string GetTextToCallNumberForDepartment(int departmentId)
		{
			//var settingValue = (from setting in _departmentSettingsRepository.GetAll()
			//										where setting.DepartmentId == departmentId &&
			//												setting.SettingType == (int)DepartmentSettingTypes.TextToCallNumber
			//										select setting).FirstOrDefault();

			var settingValue = _departmentSettingsRepository.GetDepartmentSettingByIdType(departmentId, DepartmentSettingTypes.TextToCallNumber);

			if (settingValue != null)
				return settingValue.Setting;

			return null;
		}

		public int? GetTextToCallImportFormatForDepartment(int departmentId)
		{
			var settingValue = (from setting in _departmentSettingsRepository.GetAll()
								where setting.DepartmentId == departmentId &&
										setting.SettingType == (int)DepartmentSettingTypes.TextToCallImportFormat
								select setting).FirstOrDefault();

			if (settingValue != null)
				return int.Parse(settingValue.Setting);

			return null;
		}

		public string GetTextToCallSourceNumbersForDepartment(int departmentId)
		{
			var settingValue = (from setting in _departmentSettingsRepository.GetAll()
								where setting.DepartmentId == departmentId &&
									setting.SettingType == (int)DepartmentSettingTypes.TextToCallSourceNumbers
								select setting).FirstOrDefault();

			if (settingValue != null)
				return settingValue.Setting;

			return null;
		}

		public int? GetDepartmentIdByTextToCallNumber(string phoneNumber)
		{
			var settingValue = (from setting in _departmentSettingsRepository.GetAll()
								where setting.Setting.Trim() == phoneNumber &&
										setting.SettingType == (int)DepartmentSettingTypes.TextToCallNumber
								select setting).FirstOrDefault();

			if (settingValue != null)
				return settingValue.DepartmentId;

			return null;
		}

		public bool GetDepartmentIsTextCallImportEnabled(int departmentId)
		{
			var settingValue = (from setting in _departmentSettingsRepository.GetAll()
								where setting.DepartmentId == departmentId &&
									setting.SettingType == (int)DepartmentSettingTypes.EnableTextToCall
								select setting).FirstOrDefault();

			if (settingValue != null)
				return bool.Parse(settingValue.Setting);

			return false;
		}

		public bool GetDepartmentIsTextCommandEnabled(int departmentId)
		{
			var settingValue = (from setting in _departmentSettingsRepository.GetAll()
								where setting.DepartmentId == departmentId &&
										setting.SettingType == (int)DepartmentSettingTypes.EnableTextCommand
								select setting).FirstOrDefault();

			if (settingValue != null)
				return bool.Parse(settingValue.Setting);

			return false;
		}

		public int? GetDepartmentIdForDispatchEmail(string emailAddress)
		{
			var key = (from setting in _departmentSettingsRepository.GetAll()
					   where setting.Setting == emailAddress &&
						   setting.SettingType == (int)DepartmentSettingTypes.InternalDispatchEmail
					   select setting).FirstOrDefault();

			if (key != null)
				return key.DepartmentId;

			return null;
		}

		public string GetDispatchEmailForDepartment(int departmentId)
		{
			var key = (from setting in _departmentSettingsRepository.GetAll()
					   where setting.DepartmentId == departmentId &&
						   setting.SettingType == (int)DepartmentSettingTypes.InternalDispatchEmail
					   select setting).FirstOrDefault();

			if (key != null)
				return key.Setting;

			return null;
		}

		public DateTime GetDepartmentUpdateTimestamp(int departmentId)
		{
			var key = (from setting in _departmentSettingsRepository.GetAll()
					   where setting.DepartmentId == departmentId &&
						   setting.SettingType == (int)DepartmentSettingTypes.UpdateTimestamp
					   select setting).FirstOrDefault();

			if (key != null)
				return DateTime.Parse(key.Setting);

			return DateTime.MinValue;
		}

		public PersonnelSortOrders GetDepartmentPersonnelSortOrder(int departmentId)
		{
			var settingValue = (from setting in _departmentSettingsRepository.GetAll()
								where setting.DepartmentId == departmentId &&
										setting.SettingType == (int)DepartmentSettingTypes.PersonnelSortOrder
								select setting).FirstOrDefault();

			if (settingValue != null)
				return (PersonnelSortOrders)int.Parse(settingValue.Setting);

			return PersonnelSortOrders.Default;
		}

		public UnitSortOrders GetDepartmentUnitsSortOrder(int departmentId)
		{
			var settingValue = (from setting in _departmentSettingsRepository.GetAll()
								where setting.DepartmentId == departmentId &&
										setting.SettingType == (int)DepartmentSettingTypes.UnitsSortOrder
								select setting).FirstOrDefault();

			if (settingValue != null)
				return (UnitSortOrders)int.Parse(settingValue.Setting);

			return UnitSortOrders.Default;
		}

		public CallSortOrders GetDepartmentCallSortOrder(int departmentId)
		{
			var settingValue = (from setting in _departmentSettingsRepository.GetAll()
								where setting.DepartmentId == departmentId &&
										setting.SettingType == (int)DepartmentSettingTypes.CallsSortOrder
								select setting).FirstOrDefault();

			if (settingValue != null)
				return (CallSortOrders)int.Parse(settingValue.Setting);

			return CallSortOrders.Default;
		}

		public List<DepartmentManagerInfo> GetAllDepartmentManagerInfo()
		{
			return _departmentSettingsRepository.GetAllDepartmentManagerInfo();
		}

		public DepartmentManagerInfo GetDepartmentManagerInfoByEmail(string emailAddress)
		{
			return _departmentSettingsRepository.GetDepartmentManagerInfoByEmail(emailAddress);
		}
	}
}
