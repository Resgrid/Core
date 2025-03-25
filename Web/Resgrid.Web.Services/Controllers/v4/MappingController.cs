using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using System.Threading.Tasks;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.CallPriorities;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Providers;
using System;
using Resgrid.Web.Services.Models.v4.Mapping;
using Resgrid.Web.Services.Models.v4.Roles;
using GeoJSON.Net;
using System.Collections.Generic;
using Newtonsoft.Json;
using GeoJSON.Net.Feature;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Mapping operations
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class MappingController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly IUsersService _usersService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IUserStateService _userStateService;
		private readonly IUnitsService _unitsService;
		private readonly ICallsService _callsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly ICustomStateService _customStateService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IGeoLocationProvider _geoLocationProvider;
		private readonly IMappingService _mappingService;
		private readonly Model.Services.IAuthorizationService _authorizationService;

		public MappingController(
			IUsersService usersService,
			IActionLogsService actionLogsService,
			IDepartmentsService departmentsService,
			IUserProfileService userProfileService,
			IUserStateService userStateService,
			IUnitsService unitsService,
			ICallsService callsService,
			IDepartmentGroupsService departmentGroupsService,
			IPersonnelRolesService personnelRolesService,
			ICustomStateService customStateService,
			IDepartmentSettingsService departmentSettingsService,
			IGeoLocationProvider geoLocationProvider,
			IMappingService mappingService,
			Model.Services.IAuthorizationService authorizationService
			)
		{
			_usersService = usersService;
			_actionLogsService = actionLogsService;
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
			_userStateService = userStateService;
			_unitsService = unitsService;
			_callsService = callsService;
			_departmentGroupsService = departmentGroupsService;
			_personnelRolesService = personnelRolesService;
			_customStateService = customStateService;
			_departmentSettingsService = departmentSettingsService;
			_geoLocationProvider = geoLocationProvider;
			_mappingService = mappingService;
			_authorizationService = authorizationService;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Data to center the map and it's default location plus marker information for displaying makers on the map.
		/// </summary>
		/// <returns>GetMapDataResult object</returns>
		[HttpGet("GetMapDataAndMarkers")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<GetMapDataResult>> GetMapDataAndMarkers()
		{
			var result = new GetMapDataResult();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var stations = await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(DepartmentId);

			var address = await _departmentSettingsService.GetBigBoardCenterAddressDepartmentAsync(DepartmentId);
			var gpsCoordinates = await _departmentSettingsService.GetBigBoardCenterGpsCoordinatesDepartmentAsync(DepartmentId);
			var calls = await _callsService.GetActiveCallsByDepartmentAsync(DepartmentId);
			var units = await _unitsService.GetUnitsForDepartmentAsync(DepartmentId);
			var unitStates = await _unitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync(DepartmentId);
			var unitLocations = await _unitsService.GetLatestUnitLocationsAsync(DepartmentId);
			var unitTypes = await _unitsService.GetUnitTypesForDepartmentAsync(DepartmentId);
			var callTypes = await _callsService.GetCallTypesForDepartmentAsync(DepartmentId);

			var personnelStates = await _actionLogsService.GetLastActionLogsForDepartmentAsync(DepartmentId);
			var personnelNames = await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId);
			var personnelLocations = await _usersService.GetLatestLocationsForDepartmentPersonnelAsync(DepartmentId);

			var personnelLocationTTL = await _departmentSettingsService.GetMappingPersonnelLocationTTLAsync(DepartmentId);
			var unitLocationTTL = await _departmentSettingsService.GetMappingUnitLocationTTLAsync(DepartmentId);
			var personnelAllowStatusWithNoLocationToOverwrite = await _departmentSettingsService.GetMappingPersonnelAllowStatusWithNoLocationToOverwriteAsync(DepartmentId);
			var unitAllowStatusWithNoLocationToOverwrite = await _departmentSettingsService.GetMappingUnitAllowStatusWithNoLocationToOverwriteAsync(DepartmentId);

			string weatherUnits = "";
			double? centerLat = null;
			double? centerLon = null;

			if (address != null && !String.IsNullOrWhiteSpace(address.Country))
			{
				if (address.Country == "Canada")
					weatherUnits = "ca";
				else if (address.Country == "United Kingdom")
					weatherUnits = "uk";
				else if (address.Country == "Australia")
					weatherUnits = "uk";
				else
					weatherUnits = "us";
			}
			else if (department.Address != null && !String.IsNullOrWhiteSpace(department.Address.Country))
			{
				if (department.Address.Country == "Canada")
					weatherUnits = "ca";
				else if (department.Address.Country == "United Kingdom")
					weatherUnits = "uk";
				else if (department.Address.Country == "Australia")
					weatherUnits = "uk";
				else
					weatherUnits = "us";
			}

			if (!String.IsNullOrWhiteSpace(gpsCoordinates))
			{
				string[] coordinates = gpsCoordinates.Split(char.Parse(","));

				if (coordinates.Count() == 2)
				{
					double newLat;
					double newLon;
					if (double.TryParse(coordinates[0], out newLat) && double.TryParse(coordinates[1], out newLon))
					{
						centerLat = newLat;
						centerLon = newLon;
					}
				}
			}

			if (!centerLat.HasValue && !centerLon.HasValue && address != null)
			{
				string coordinates = await _geoLocationProvider.GetLatLonFromAddress(string.Format("{0} {1} {2} {3}", address.Address1,
																		address.City, address.State, address.PostalCode));

				if (!String.IsNullOrEmpty(coordinates))
				{
					double newLat;
					double newLon;
					var coordinatesArr = coordinates.Split(char.Parse(","));
					if (double.TryParse(coordinatesArr[0], out newLat) && double.TryParse(coordinatesArr[1], out newLon))
					{
						centerLat = newLat;
						centerLon = newLon;
					}
				}
			}

			if (!centerLat.HasValue && !centerLon.HasValue && department.Address != null)
			{
				string coordinates = await _geoLocationProvider.GetLatLonFromAddress(string.Format("{0} {1} {2} {3}", department.Address.Address1,
																		department.Address.City,
																		department.Address.State,
																		department.Address.PostalCode));

				if (!String.IsNullOrEmpty(coordinates))
				{
					double newLat;
					double newLon;
					var coordinatesArr = coordinates.Split(char.Parse(","));
					if (double.TryParse(coordinatesArr[0], out newLat) && double.TryParse(coordinatesArr[1], out newLon))
					{
						centerLat = newLat;
						centerLon = newLon;
					}
				}
			}

			if (!centerLat.HasValue || !centerLon.HasValue)
			{
				centerLat = 39.14086268299356;
				centerLon = -119.7583809782715;
			}

			var zoomLevel = await _departmentSettingsService.GetBigBoardMapZoomLevelForDepartmentAsync(department.DepartmentId);


			result.Data.CenterLat = centerLat.Value;
			result.Data.CenterLon = centerLon.Value;
			result.Data.ZoomLevel = zoomLevel.HasValue ? zoomLevel.Value : 9;

			if (stations != null && stations.Any())
			{
				foreach (var station in stations)
				{
					MapMakerInfoData info = new MapMakerInfoData();
					info.Id = $"s{station.DepartmentGroupId}";
					info.ImagePath = "Station";
					info.Title = station.Name;
					info.InfoWindowContent = station.Name;
					info.Type = 2;

					try
					{
						if (station.Address != null)
						{
							string coordinates = await _geoLocationProvider.GetLatLonFromAddress(string.Format("{0} {1} {2} {3}", station.Address.Address1,
																				station.Address.City,
																				station.Address.State,
																				station.Address.PostalCode));

							if (!String.IsNullOrEmpty(coordinates))
							{
								info.Latitude = double.Parse(coordinates.Split(char.Parse(","))[0]);
								info.Longitude = double.Parse(coordinates.Split(char.Parse(","))[1]);

								result.Data.MapMakerInfos.Add(info);
							}
						}
						else if (!String.IsNullOrWhiteSpace(station.Latitude) && !String.IsNullOrWhiteSpace(station.Longitude))
						{
							info.Latitude = double.Parse(station.Latitude);
							info.Longitude = double.Parse(station.Longitude);

							result.Data.MapMakerInfos.Add(info);
						}
					}
					catch { }
				}
			}

			if (calls != null && calls.Any())
			{
				foreach (var call in calls)
				{
					MapMakerInfoData info = new MapMakerInfoData();
					info.ImagePath = "Call";
					info.Id = $"c{call.CallId}";
					info.Title = call.Name;
					info.InfoWindowContent = call.NatureOfCall;
					info.Type = 0;

					try
					{
						if (callTypes != null && callTypes.Count > 0 && !String.IsNullOrWhiteSpace(call.Type))
						{
							var type = callTypes.FirstOrDefault(x => x.Type == call.Type);

						if (type != null && type.MapIconType.HasValue)
							info.ImagePath = MapIcons.ConvertTypeToName((MapIconTypes)type.MapIconType.Value);
					}
						//	if (type != null && type.MapIconType.HasValue)
						//		info.ImagePath = ((MapIconTypes)type.MapIconType.Value).ToString();
						//}

						if (!String.IsNullOrEmpty(call.GeoLocationData) && call.GeoLocationData.Length > 1)
						{
							try
							{
								info.Latitude = double.Parse(call.GeoLocationData.Split(char.Parse(","))[0]);
								info.Longitude = double.Parse(call.GeoLocationData.Split(char.Parse(","))[1]);

								result.Data.MapMakerInfos.Add(info);
							}
							catch { }
						}
						else if (!String.IsNullOrEmpty(call.Address))
						{
							string coordinates = await _geoLocationProvider.GetLatLonFromAddress(call.Address);
							if (!String.IsNullOrEmpty(coordinates))
							{
								info.Latitude = double.Parse(coordinates.Split(char.Parse(","))[0]);
								info.Longitude = double.Parse(coordinates.Split(char.Parse(","))[1]);
							}

							result.Data.MapMakerInfos.Add(info);
						}
					}
					catch { }
				}
			}

			if (units != null && units.Any())
			{
				foreach (var unit in units)
				{
					if (!await _authorizationService.CanUserViewUnitLocationViaMatrixAsync(unit.UnitId, UserId, DepartmentId))
						continue;

					var latestLocation = unitLocations.FirstOrDefault(x => x.UnitId == unit.UnitId);
					var state = unitStates.FirstOrDefault(x => x.UnitId == unit.UnitId);

					MapMakerInfoData info = new MapMakerInfoData();
					info.ImagePath = "Engine_Responding";
					info.Id = $"u{unit.UnitId}";
					info.Title = unit.Name;
					info.InfoWindowContent = "";
					info.Type = 1;

					try
					{
						// Department has a TTL setup for units
						if (unitLocationTTL > 0 && latestLocation != null)
						{
							// Unit location TTL has expired the latest unit location we have.
							if (DateTime.UtcNow.AddMinutes(-unitLocationTTL) > latestLocation.Timestamp)
								latestLocation = null;
						}

						if (unitTypes != null && unitTypes.Count > 0 && !String.IsNullOrWhiteSpace(unit.Type))
						{
							var type = unitTypes.FirstOrDefault(x => x.Type == unit.Type);

							if (type != null && type.MapIconType.HasValue)
								info.ImagePath = ((MapIconTypes)type.MapIconType.Value).ToString();
						}

						if (latestLocation != null && state != null)
						{
							if (latestLocation.Timestamp > state.Timestamp)
							{
								info.Latitude = double.Parse(latestLocation.Latitude.ToString());
								info.Longitude = double.Parse(latestLocation.Longitude.ToString());

								result.Data.MapMakerInfos.Add(info);
							}
							else if (state.HasLocation())
							{
								info.Latitude = double.Parse(state.Latitude.Value.ToString());
								info.Longitude = double.Parse(state.Longitude.Value.ToString());

								result.Data.MapMakerInfos.Add(info);
							}
							else if (!unitAllowStatusWithNoLocationToOverwrite) // State was newer then location ping but did not have a valid location
							{
								info.Latitude = double.Parse(latestLocation.Latitude.ToString());
								info.Longitude = double.Parse(latestLocation.Longitude.ToString());

								result.Data.MapMakerInfos.Add(info);
							}
						}
						else if (latestLocation != null)
						{
							info.Latitude = double.Parse(latestLocation.Latitude.ToString());
							info.Longitude = double.Parse(latestLocation.Longitude.ToString());

							result.Data.MapMakerInfos.Add(info);
						}
						else if (state != null && state.HasLocation())
						{
							info.Latitude = double.Parse(state.Latitude.Value.ToString());
							info.Longitude = double.Parse(state.Longitude.Value.ToString());

							result.Data.MapMakerInfos.Add(info);
						}
					}
					catch { }
				}
			}

			if (personnelNames != null && personnelNames.Any())
			{
				foreach (var person in personnelNames)
				{
					if (!await _authorizationService.CanUserViewPersonLocationViaMatrixAsync(person.UserId, UserId, DepartmentId))
						continue;

					var latestLocation = personnelLocations.FirstOrDefault(x => x.UserId == person.UserId);
					var state = personnelStates.FirstOrDefault(x => x.UserId == person.UserId);

					MapMakerInfoData info = new MapMakerInfoData();

					info.ImagePath = "Person_RespondingCall";
					info.Id = $"p{person.UserId}";
					info.Title = person.Name;
					info.InfoWindowContent = "";
					info.Type = 3;

					try
					{
						// Department has a TTL setup for personnel
						if (personnelLocationTTL > 0 && latestLocation != null)
						{
							// Person location TTL has expired the latest personnel location we have.
							if (DateTime.UtcNow.AddMinutes(-personnelLocationTTL) > latestLocation.Timestamp)
								latestLocation = null;
						}

						if (latestLocation != null && state != null)
						{
							if (latestLocation.Timestamp > state.Timestamp) // Location ping newer then state
							{
								info.Latitude = double.Parse(latestLocation.Latitude.ToString());
								info.Longitude = double.Parse(latestLocation.Longitude.ToString());

								result.Data.MapMakerInfos.Add(info);
							}
							else if (state.HasLocation()) // State is newer then location ping and has a valid location
							{
								var location = state.GetCoordinates();
								info.Latitude = double.Parse(location.Latitude.Value.ToString());
								info.Longitude = double.Parse(location.Longitude.Value.ToString());

								result.Data.MapMakerInfos.Add(info);
							}
							else if (!personnelAllowStatusWithNoLocationToOverwrite) // State was newer then location ping but did not have a valid location
							{
								info.Latitude = double.Parse(latestLocation.Latitude.ToString());
								info.Longitude = double.Parse(latestLocation.Longitude.ToString());

								result.Data.MapMakerInfos.Add(info);
							}
						}
						else if (latestLocation != null)
						{
							info.Latitude = (double)latestLocation.Latitude;
							info.Longitude = (double)latestLocation.Longitude;

							result.Data.MapMakerInfos.Add(info);
						}
						else if (state != null)
						{
							if (state.HasLocation())
							{
								var location = state.GetCoordinates();

								info.Latitude = location.Latitude.Value;
								info.Longitude = location.Longitude.Value;

								result.Data.MapMakerInfos.Add(info);
							}
						}
					}
					catch { }
				}
			}

			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Gets the user created map layers in the system.
		/// </summary>
		/// <returns>GetMapLayersResult object</returns>
		[HttpGet("GetMayLayers")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<GetMapLayersResult>> GetMayLayers(int type)
		{
			var result = new GetMapLayersResult();

			var layers = await _mappingService.GetMapLayersForTypeDepartmentAsync(DepartmentId, (MapLayerTypes)type);

			if (layers != null && layers.Count > 0)
			{
				foreach (var layer in layers)
				{
					result.Data.Layers.Add(ConvertMapLayerData(layer));
				}
			}

			result.Data.LayerJson = JsonConvert.SerializeObject(result.Data.Layers.Select(x => x.Data.Features).ToList());

			result.PageSize = result.Data.Layers.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		public static GetMapLayersData ConvertMapLayerData(MapLayer layer)
		{
			var result = new GetMapLayersData();

			result.Id = layer.Id.ToString();
			result.DepartmentId = layer.DepartmentId;
			result.Name = layer.Name;
			result.Type = layer.Type;
			result.Color = layer.Color;
			result.IsSearchable = layer.IsSearchable;
			result.IsOnByDefault = layer.IsOnByDefault;
			result.AddedById = layer.AddedById;
			result.AddedOn = layer.AddedOn;
			result.UpdatedById = layer.UpdatedById;
			result.UpdatedOn = layer.UpdatedOn;

			result.Data = new GetMapLayersDataInfo();
			result.Data.Type = ((GeoJSONObjectType)layer.Type).ToString();
			result.Data.Features = layer.Data.Convert();

			return result;
		}
	}
}
