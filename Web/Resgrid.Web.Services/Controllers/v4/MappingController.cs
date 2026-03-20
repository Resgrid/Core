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
using Newtonsoft.Json.Linq;
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
		private readonly IIndoorMapService _indoorMapService;
		private readonly ICustomMapService _customMapService;

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
			Model.Services.IAuthorizationService authorizationService,
			IIndoorMapService indoorMapService,
			ICustomMapService customMapService
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
			_indoorMapService = indoorMapService;
			_customMapService = customMapService;
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
			//var personnelNames = await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId);
			var people = await _usersService.GetUserGroupAndRolesByDepartmentIdAsync(DepartmentId, false, false, false);
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

			if (people != null && people.Any())
			{
				foreach (var person in people)
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

		/// <summary>
		/// Gets all indoor maps for the department.
		/// </summary>
		/// <returns>GetIndoorMapsResult object with list of indoor maps</returns>
		[HttpGet("GetIndoorMaps")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<GetIndoorMapsResult>> GetIndoorMaps()
		{
			var result = new GetIndoorMapsResult();

			var maps = await _indoorMapService.GetIndoorMapsForDepartmentAsync(DepartmentId);

			if (maps != null && maps.Count > 0)
			{
				foreach (var m in maps)
				{
					result.Data.Add(new IndoorMapResultData
					{
						IndoorMapId = m.IndoorMapId,
						Name = m.Name,
						Description = m.Description,
						CenterLatitude = m.CenterLatitude,
						CenterLongitude = m.CenterLongitude,
						BoundsNELat = m.BoundsNELat,
						BoundsNELon = m.BoundsNELon,
						BoundsSWLat = m.BoundsSWLat,
						BoundsSWLon = m.BoundsSWLon,
						DefaultFloorId = m.DefaultFloorId
					});
				}
			}

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Gets a specific indoor map with its floors.
		/// </summary>
		/// <param name="id">Indoor map id</param>
		/// <returns>GetIndoorMapResult object with map and floor data</returns>
		[HttpGet("GetIndoorMap/{id}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<GetIndoorMapResult>> GetIndoorMap(string id)
		{
			var result = new GetIndoorMapResult();

			var map = await _indoorMapService.GetIndoorMapByIdAsync(id);

			if (map == null || map.DepartmentId != DepartmentId)
				return NotFound();

			result.Data.Map = new IndoorMapResultData
			{
				IndoorMapId = map.IndoorMapId,
				Name = map.Name,
				Description = map.Description,
				CenterLatitude = map.CenterLatitude,
				CenterLongitude = map.CenterLongitude,
				BoundsNELat = map.BoundsNELat,
				BoundsNELon = map.BoundsNELon,
				BoundsSWLat = map.BoundsSWLat,
				BoundsSWLon = map.BoundsSWLon,
				DefaultFloorId = map.DefaultFloorId
			};

			var floors = await _indoorMapService.GetFloorsForMapAsync(id);

			if (floors != null && floors.Count > 0)
			{
				foreach (var f in floors)
				{
					result.Data.Floors.Add(new IndoorMapFloorResultData
					{
						IndoorMapFloorId = f.IndoorMapFloorId,
						IndoorMapId = f.IndoorMapId,
						Name = f.Name,
						FloorOrder = f.FloorOrder,
						HasImage = f.ImageData != null && f.ImageData.Length > 0,
						BoundsNELat = f.BoundsNELat,
						BoundsNELon = f.BoundsNELon,
						BoundsSWLat = f.BoundsSWLat,
						BoundsSWLon = f.BoundsSWLon,
						Opacity = f.Opacity
					});
				}
			}

			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Gets a specific indoor map floor with its zones.
		/// </summary>
		/// <param name="floorId">Indoor map floor id</param>
		/// <returns>GetIndoorMapFloorResult object with floor and zone data</returns>
		[HttpGet("GetIndoorMapFloor/{floorId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<GetIndoorMapFloorResult>> GetIndoorMapFloor(string floorId)
		{
			var result = new GetIndoorMapFloorResult();

			var floor = await _indoorMapService.GetFloorByIdAsync(floorId);

			if (floor == null)
				return NotFound();

			var map = await _indoorMapService.GetIndoorMapByIdAsync(floor.IndoorMapId);

			if (map == null || map.DepartmentId != DepartmentId)
				return NotFound();

			result.Data = new IndoorMapFloorResultData
			{
				IndoorMapFloorId = floor.IndoorMapFloorId,
				IndoorMapId = floor.IndoorMapId,
				Name = floor.Name,
				FloorOrder = floor.FloorOrder,
				HasImage = floor.ImageData != null && floor.ImageData.Length > 0,
				BoundsNELat = floor.BoundsNELat,
				BoundsNELon = floor.BoundsNELon,
				BoundsSWLat = floor.BoundsSWLat,
				BoundsSWLon = floor.BoundsSWLon,
				Opacity = floor.Opacity,
				Zones = new List<IndoorMapZoneResultData>()
			};

			var zones = await _indoorMapService.GetZonesForFloorAsync(floorId);

			if (zones != null && zones.Count > 0)
			{
				foreach (var z in zones)
				{
					result.Data.Zones.Add(new IndoorMapZoneResultData
					{
						IndoorMapZoneId = z.IndoorMapZoneId,
						IndoorMapFloorId = z.IndoorMapFloorId,
						Name = z.Name,
						Description = z.Description,
						ZoneType = z.ZoneType,
						PixelGeometry = z.PixelGeometry,
						GeoGeometry = z.GeoGeometry,
						CenterPixelX = z.CenterPixelX,
						CenterPixelY = z.CenterPixelY,
						CenterLatitude = z.CenterLatitude,
						CenterLongitude = z.CenterLongitude,
						Color = z.Color,
						Metadata = z.Metadata,
						IsSearchable = z.IsSearchable
					});
				}
			}

			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Gets the image for a specific indoor map floor.
		/// </summary>
		/// <param name="floorId">Indoor map floor id</param>
		/// <returns>Floor image file</returns>
		[HttpGet("GetIndoorMapFloorImage/{floorId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<IActionResult> GetIndoorMapFloorImage(string floorId)
		{
			var floor = await _indoorMapService.GetFloorByIdAsync(floorId);

			if (floor == null)
				return NotFound();

			var map = await _indoorMapService.GetIndoorMapByIdAsync(floor.IndoorMapId);

			if (map == null || map.DepartmentId != DepartmentId)
				return NotFound();

			if (floor.ImageData == null || floor.ImageData.Length == 0)
				return NotFound();

			return File(floor.ImageData, floor.ImageContentType ?? "image/png");
		}

		/// <summary>
		/// Searches for indoor map zones matching the specified term.
		/// </summary>
		/// <param name="term">Search term</param>
		/// <returns>SearchIndoorLocationsResult object with matching zones</returns>
		[HttpGet("SearchIndoorLocations")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<SearchIndoorLocationsResult>> SearchIndoorLocations(string term, string mapId = null)
		{
			var result = new SearchIndoorLocationsResult();

			if (!string.IsNullOrWhiteSpace(term))
			{
				var zones = await _indoorMapService.SearchZonesAsync(DepartmentId, term);

				if (zones != null && zones.Count > 0)
				{
					HashSet<string> floorIdsForMap = null;
					if (!string.IsNullOrWhiteSpace(mapId))
					{
						var floors = await _indoorMapService.GetFloorsForMapAsync(mapId);
						floorIdsForMap = floors?.Select(f => f.IndoorMapFloorId).ToHashSet() ?? new HashSet<string>();
					}

					foreach (var z in zones)
					{
						if (floorIdsForMap != null && !floorIdsForMap.Contains(z.IndoorMapFloorId))
							continue;

						result.Data.Add(new IndoorMapZoneResultData
						{
							IndoorMapZoneId = z.IndoorMapZoneId,
							IndoorMapFloorId = z.IndoorMapFloorId,
							Name = z.Name,
							Description = z.Description,
							ZoneType = z.ZoneType,
							PixelGeometry = z.PixelGeometry,
							GeoGeometry = z.GeoGeometry,
							CenterPixelX = z.CenterPixelX,
							CenterPixelY = z.CenterPixelY,
							CenterLatitude = z.CenterLatitude,
							CenterLongitude = z.CenterLongitude,
							Color = z.Color,
							Metadata = z.Metadata,
							IsSearchable = z.IsSearchable
						});
					}
				}
			}

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Gets all custom maps for the department, with optional type filter.
		/// </summary>
		/// <param name="type">Optional map type filter (0=Indoor, 1=Outdoor, 2=Event, 3=Custom)</param>
		/// <returns>GetCustomMapsResult object</returns>
		[HttpGet("GetCustomMaps")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<GetCustomMapsResult>> GetCustomMaps(int? type)
		{
			var result = new GetCustomMapsResult();

			CustomMapType? filterType = type.HasValue ? (CustomMapType)type.Value : null;
			var maps = await _customMapService.GetCustomMapsForDepartmentAsync(DepartmentId, filterType);

			if (maps != null && maps.Count > 0)
			{
				foreach (var m in maps)
				{
					result.Data.Add(new CustomMapResultData
					{
						IndoorMapId = m.IndoorMapId,
						Name = m.Name,
						Description = m.Description,
						MapType = m.MapType,
						CenterLatitude = m.CenterLatitude,
						CenterLongitude = m.CenterLongitude,
						BoundsNELat = m.BoundsNELat,
						BoundsNELon = m.BoundsNELon,
						BoundsSWLat = m.BoundsSWLat,
						BoundsSWLon = m.BoundsSWLon,
						BoundsGeoJson = m.BoundsGeoJson,
						DefaultFloorId = m.DefaultFloorId
					});
				}
			}

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Gets a specific custom map with its layers.
		/// </summary>
		/// <param name="id">Custom map id</param>
		/// <returns>GetCustomMapResult object</returns>
		[HttpGet("GetCustomMap/{id}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<GetCustomMapResult>> GetCustomMap(string id)
		{
			var result = new GetCustomMapResult();

			var map = await _customMapService.GetCustomMapByIdAsync(id);
			if (map == null || map.DepartmentId != DepartmentId)
				return NotFound();

			result.Data.Map = new CustomMapResultData
			{
				IndoorMapId = map.IndoorMapId,
				Name = map.Name,
				Description = map.Description,
				MapType = map.MapType,
				CenterLatitude = map.CenterLatitude,
				CenterLongitude = map.CenterLongitude,
				BoundsNELat = map.BoundsNELat,
				BoundsNELon = map.BoundsNELon,
				BoundsSWLat = map.BoundsSWLat,
				BoundsSWLon = map.BoundsSWLon,
				BoundsGeoJson = map.BoundsGeoJson,
				DefaultFloorId = map.DefaultFloorId
			};

			var layers = await _customMapService.GetLayersForMapAsync(id);
			if (layers != null && layers.Count > 0)
			{
				foreach (var l in layers)
				{
					result.Data.Layers.Add(new CustomMapLayerResultData
					{
						IndoorMapFloorId = l.IndoorMapFloorId,
						IndoorMapId = l.IndoorMapId,
						Name = l.Name,
						FloorOrder = l.FloorOrder,
						LayerType = l.LayerType,
						HasImage = (l.ImageData != null && l.ImageData.Length > 0) || l.IsTiled,
						IsTiled = l.IsTiled,
						TileMinZoom = l.TileMinZoom,
						TileMaxZoom = l.TileMaxZoom,
						BoundsNELat = l.BoundsNELat,
						BoundsNELon = l.BoundsNELon,
						BoundsSWLat = l.BoundsSWLat,
						BoundsSWLon = l.BoundsSWLon,
						Opacity = l.Opacity
					});
				}
			}

			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Gets a specific custom map layer with its regions.
		/// </summary>
		/// <param name="layerId">Layer id</param>
		/// <returns>GetCustomMapLayerResult object</returns>
		[HttpGet("GetCustomMapLayer/{layerId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<GetCustomMapLayerResult>> GetCustomMapLayer(string layerId)
		{
			var result = new GetCustomMapLayerResult();

			var layer = await _customMapService.GetLayerByIdAsync(layerId);
			if (layer == null)
				return NotFound();

			var map = await _customMapService.GetCustomMapByIdAsync(layer.IndoorMapId);
			if (map == null || map.DepartmentId != DepartmentId)
				return NotFound();

			result.Data = new CustomMapLayerResultData
			{
				IndoorMapFloorId = layer.IndoorMapFloorId,
				IndoorMapId = layer.IndoorMapId,
				Name = layer.Name,
				FloorOrder = layer.FloorOrder,
				LayerType = layer.LayerType,
				HasImage = (layer.ImageData != null && layer.ImageData.Length > 0) || layer.IsTiled,
				IsTiled = layer.IsTiled,
				TileMinZoom = layer.TileMinZoom,
				TileMaxZoom = layer.TileMaxZoom,
				BoundsNELat = layer.BoundsNELat,
				BoundsNELon = layer.BoundsNELon,
				BoundsSWLat = layer.BoundsSWLat,
				BoundsSWLon = layer.BoundsSWLon,
				Opacity = layer.Opacity,
				Regions = new System.Collections.Generic.List<CustomMapRegionResultData>()
			};

			var regions = await _customMapService.GetRegionsForLayerAsync(layerId);
			if (regions != null && regions.Count > 0)
			{
				foreach (var r in regions)
				{
					result.Data.Regions.Add(new CustomMapRegionResultData
					{
						IndoorMapZoneId = r.IndoorMapZoneId,
						IndoorMapFloorId = r.IndoorMapFloorId,
						Name = r.Name,
						Description = r.Description,
						ZoneType = r.ZoneType,
						PixelGeometry = r.PixelGeometry,
						GeoGeometry = r.GeoGeometry,
						CenterPixelX = r.CenterPixelX,
						CenterPixelY = r.CenterPixelY,
						CenterLatitude = r.CenterLatitude,
						CenterLongitude = r.CenterLongitude,
						Color = r.Color,
						Metadata = r.Metadata,
						IsSearchable = r.IsSearchable,
						IsDispatchable = r.IsDispatchable
					});
				}
			}

			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Gets a tile image for a specific custom map layer.
		/// </summary>
		/// <param name="layerId">Layer id</param>
		/// <param name="z">Zoom level</param>
		/// <param name="x">Tile X</param>
		/// <param name="y">Tile Y</param>
		/// <returns>Tile image file</returns>
		[HttpGet("GetCustomMapTile/{layerId}/{z}/{x}/{y}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<IActionResult> GetCustomMapTile(string layerId, int z, int x, int y)
		{
			var tile = await _customMapService.GetTileAsync(layerId, z, x, y);

			if (tile == null)
				return NotFound();

			return File(tile.TileData, tile.TileContentType ?? "image/png");
		}

		/// <summary>
		/// Gets the full image for a non-tiled custom map layer.
		/// </summary>
		/// <param name="layerId">Layer id</param>
		/// <returns>Layer image file</returns>
		[HttpGet("GetCustomMapLayerImage/{layerId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<IActionResult> GetCustomMapLayerImage(string layerId)
		{
			var layer = await _customMapService.GetLayerByIdAsync(layerId);
			if (layer == null)
				return NotFound();

			var map = await _customMapService.GetCustomMapByIdAsync(layer.IndoorMapId);
			if (map == null || map.DepartmentId != DepartmentId)
				return NotFound();

			if (layer.ImageData == null || layer.ImageData.Length == 0)
				return NotFound();

			return File(layer.ImageData, layer.ImageContentType ?? "image/png");
		}

		/// <summary>
		/// Searches for custom map regions matching the specified term.
		/// </summary>
		/// <param name="term">Search term</param>
		/// <returns>SearchCustomMapRegionsResult object</returns>
		[HttpGet("SearchCustomMapRegions")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<SearchCustomMapRegionsResult>> SearchCustomMapRegions(string term, string layerId = null)
		{
			var result = new SearchCustomMapRegionsResult();

			if (!string.IsNullOrWhiteSpace(term))
			{
				var regions = await _customMapService.SearchRegionsAsync(DepartmentId, term);

				if (regions != null && regions.Count > 0)
				{
					foreach (var r in regions)
					{
						if (!string.IsNullOrWhiteSpace(layerId) && r.IndoorMapFloorId != layerId)
							continue;

						result.Data.Add(new CustomMapRegionResultData
						{
							IndoorMapZoneId = r.IndoorMapZoneId,
							IndoorMapFloorId = r.IndoorMapFloorId,
							Name = r.Name,
							Description = r.Description,
							ZoneType = r.ZoneType,
							PixelGeometry = r.PixelGeometry,
							GeoGeometry = r.GeoGeometry,
							CenterPixelX = r.CenterPixelX,
							CenterPixelY = r.CenterPixelY,
							CenterLatitude = r.CenterLatitude,
							CenterLongitude = r.CenterLongitude,
							Color = r.Color,
							Metadata = r.Metadata,
							IsSearchable = r.IsSearchable,
							IsDispatchable = r.IsDispatchable
						});
					}
				}
			}

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Returns raw GeoJSON FeatureCollection for a MapLayer (legacy vector layers).
		/// </summary>
		/// <param name="layerId">Map layer id</param>
		[HttpGet("GetMapLayerGeoJSON/{layerId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<IActionResult> GetMapLayerGeoJSON(string layerId)
		{
			var layer = await _mappingService.GetMapLayersByIdAsync(layerId);
			if (layer == null || layer.DepartmentId != DepartmentId)
				return NotFound();

			var fc = layer.Data?.Convert() ?? new FeatureCollection();
			var geoJson = JsonConvert.SerializeObject(fc);

			return Content(geoJson, "application/geo+json");
		}

		/// <summary>
		/// Returns a summary of all map layers that are on by default for the department.
		/// </summary>
		[HttpGet("GetAllActiveLayers")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<GetAllActiveLayersResult>> GetAllActiveLayers()
		{
			var result = new GetAllActiveLayersResult();

			var mapLayers = await _mappingService.GetMapLayersForTypeDepartmentAsync(DepartmentId, MapLayerTypes.TopLevel);
			if (mapLayers != null)
			{
				foreach (var layer in mapLayers.Where(l => !l.IsDeleted && l.IsOnByDefault))
				{
					result.Data.Add(new ActiveLayerResultData
					{
						Id = layer.GetId(),
						Name = layer.Name,
						LayerSource = "maplayer",
						Type = layer.Type,
						Color = layer.Color,
						IsSearchable = layer.IsSearchable,
						IsOnByDefault = layer.IsOnByDefault
					});
				}
			}

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Unified search across indoor zones and custom map regions.
		/// </summary>
		/// <param name="term">Search term</param>
		/// <param name="type">Filter: "all" (default), "indoor", or "custom"</param>
		[HttpGet("SearchAllMapFeatures")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<SearchAllMapFeaturesResult>> SearchAllMapFeatures(string term, string type = "all")
		{
			var result = new SearchAllMapFeaturesResult();

			if (string.IsNullOrWhiteSpace(term))
			{
				result.Status = ResponseHelper.Success;
				ResponseHelper.PopulateV4ResponseData(result);
				return Ok(result);
			}

			if (type == "all" || type == "indoor")
			{
				var zones = await _indoorMapService.SearchZonesAsync(DepartmentId, term);
				if (zones != null)
				{
					foreach (var z in zones)
					{
						var floor = await _indoorMapService.GetFloorByIdAsync(z.IndoorMapFloorId);
						var map = floor != null ? await _indoorMapService.GetIndoorMapByIdAsync(floor.IndoorMapId) : null;

						result.Data.Add(new MapFeatureResultData
						{
							FeatureType = "indoor_zone",
							Id = z.IndoorMapZoneId,
							Name = z.Name,
							Description = z.Description,
							MapId = map?.IndoorMapId,
							MapName = map?.Name,
							FloorOrLayerId = z.IndoorMapFloorId,
							FloorOrLayerName = floor?.Name,
							CenterLatitude = z.CenterLatitude,
							CenterLongitude = z.CenterLongitude,
							IsDispatchable = z.IsDispatchable
						});
					}
				}
			}

			if (type == "all" || type == "custom")
			{
				var regions = await _customMapService.SearchRegionsAsync(DepartmentId, term);
				if (regions != null)
				{
					foreach (var r in regions)
					{
						var layer = await _customMapService.GetLayerByIdAsync(r.IndoorMapFloorId);
						var map = layer != null ? await _customMapService.GetCustomMapByIdAsync(layer.IndoorMapId) : null;

						result.Data.Add(new MapFeatureResultData
						{
							FeatureType = "custom_region",
							Id = r.IndoorMapZoneId,
							Name = r.Name,
							Description = r.Description,
							MapId = map?.IndoorMapId,
							MapName = map?.Name,
							FloorOrLayerId = r.IndoorMapFloorId,
							FloorOrLayerName = layer?.Name,
							CenterLatitude = r.CenterLatitude,
							CenterLongitude = r.CenterLongitude,
							IsDispatchable = r.IsDispatchable
						});
					}
				}
			}

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Returns indoor maps whose bounding box is within radiusMeters of the given lat/lon.
		/// </summary>
		/// <param name="lat">Latitude</param>
		/// <param name="lon">Longitude</param>
		/// <param name="radiusMeters">Search radius in meters</param>
		[HttpGet("GetNearbyIndoorMaps")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<GetIndoorMapsResult>> GetNearbyIndoorMaps(double lat, double lon, double radiusMeters = 200)
		{
			var result = new GetIndoorMapsResult();

			var maps = await _indoorMapService.GetIndoorMapsForDepartmentAsync(DepartmentId);
			if (maps != null)
			{
				foreach (var m in maps)
				{
					if (IsPointNearBounds(lat, lon, radiusMeters, m.BoundsNELat, m.BoundsNELon, m.BoundsSWLat, m.BoundsSWLon))
					{
						result.Data.Add(new IndoorMapResultData
						{
							IndoorMapId = m.IndoorMapId,
							Name = m.Name,
							Description = m.Description,
							CenterLatitude = m.CenterLatitude,
							CenterLongitude = m.CenterLongitude,
							BoundsNELat = m.BoundsNELat,
							BoundsNELon = m.BoundsNELon,
							BoundsSWLat = m.BoundsSWLat,
							BoundsSWLon = m.BoundsSWLon,
							DefaultFloorId = m.DefaultFloorId
						});
					}
				}
			}

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Returns all zones for an indoor map floor as a GeoJSON FeatureCollection for direct rnmapbox consumption.
		/// </summary>
		/// <param name="floorId">Indoor map floor id</param>
		[HttpGet("GetIndoorMapZonesGeoJSON/{floorId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<IActionResult> GetIndoorMapZonesGeoJSON(string floorId)
		{
			var floor = await _indoorMapService.GetFloorByIdAsync(floorId);
			if (floor == null)
				return NotFound();

			var map = await _indoorMapService.GetIndoorMapByIdAsync(floor.IndoorMapId);
			if (map == null || map.DepartmentId != DepartmentId)
				return NotFound();

			var zones = await _indoorMapService.GetZonesForFloorAsync(floorId);
			var geoJson = BuildZonesGeoJson(zones ?? new List<Model.IndoorMapZone>());

			return Content(geoJson, "application/geo+json");
		}

		/// <summary>
		/// Returns all regions for a custom map layer as a GeoJSON FeatureCollection for direct rnmapbox consumption.
		/// </summary>
		/// <param name="layerId">Custom map layer id</param>
		[HttpGet("GetCustomMapRegionsGeoJSON/{layerId}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<IActionResult> GetCustomMapRegionsGeoJSON(string layerId)
		{
			var layer = await _customMapService.GetLayerByIdAsync(layerId);
			if (layer == null)
				return NotFound();

			var map = await _customMapService.GetCustomMapByIdAsync(layer.IndoorMapId);
			if (map == null || map.DepartmentId != DepartmentId)
				return NotFound();

			var regions = await _customMapService.GetRegionsForLayerAsync(layerId);
			var geoJson = BuildZonesGeoJson(regions ?? new List<Model.IndoorMapZone>());

			return Content(geoJson, "application/geo+json");
		}

		private static string BuildZonesGeoJson(List<Model.IndoorMapZone> zones)
		{
			var features = zones
				.Where(z => !z.IsDeleted && !string.IsNullOrWhiteSpace(z.GeoGeometry))
				.Select(z =>
				{
					JToken geometry;
					try { geometry = JToken.Parse(z.GeoGeometry); }
					catch { geometry = JValue.CreateNull(); }

					return new
					{
						type = "Feature",
						id = z.IndoorMapZoneId,
						geometry,
						properties = new
						{
							id = z.IndoorMapZoneId,
							name = z.Name,
							description = z.Description,
							zoneType = z.ZoneType,
							color = z.Color,
							isSearchable = z.IsSearchable,
							isDispatchable = z.IsDispatchable,
							metadata = z.Metadata
						}
					};
				})
				.ToList();

			return JsonConvert.SerializeObject(new { type = "FeatureCollection", features });
		}

		private static bool IsPointNearBounds(double lat, double lon, double radiusMeters,
			decimal boundsNELat, decimal boundsNELon, decimal boundsSWLat, decimal boundsSWLon)
		{
			// ~111,320 meters per degree of latitude
			double pad = radiusMeters / 111320.0;
			return lat <= (double)boundsNELat + pad && lat >= (double)boundsSWLat - pad
				&& lon <= (double)boundsNELon + pad && lon >= (double)boundsSWLon - pad;
		}
	}
}
