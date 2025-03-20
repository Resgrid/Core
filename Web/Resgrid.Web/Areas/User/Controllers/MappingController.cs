using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GeoJSON.Net.Feature;
using GeoJSON.Net.Geometry;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Home;
using Resgrid.Web.Areas.User.Models.Mapping;
using Resgrid.Web.Helpers;
using Resgrid.WebCore.Areas.User.Models.Mapping;
using SharpKml.Dom;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class MappingController : SecureBaseController
	{
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IGeoLocationProvider _geoLocationProvider;
		private readonly ICallsService _callsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IUnitsService _unitsService;
		private readonly IMappingService _mappingService;
		private readonly IKmlProvider _kmlProvider;
		private readonly IPermissionsService _permissionsService;
		private readonly IPersonnelRolesService _personnelRolesService;

		public MappingController(IDepartmentSettingsService departmentSettingsService,
			IGeoLocationProvider geoLocationProvider, ICallsService callsService,
			IDepartmentsService departmentsService, IDepartmentGroupsService departmentGroupsService,
			IActionLogsService actionLogsService, IUnitsService unitsService, IMappingService mappingService,
			IKmlProvider kmlProvider, IPermissionsService permissionsService, IPersonnelRolesService personnelRolesService)
		{
			_departmentSettingsService = departmentSettingsService;
			_geoLocationProvider = geoLocationProvider;
			_callsService = callsService;
			_departmentsService = departmentsService;
			_departmentGroupsService = departmentGroupsService;
			_actionLogsService = actionLogsService;
			_unitsService = unitsService;
			_mappingService = mappingService;
			_kmlProvider = kmlProvider;
			_permissionsService = permissionsService;
			_personnelRolesService = personnelRolesService;
		}

		public async Task<IActionResult> Index()
		{
			var model = new MapIndexView();

			var address = await _departmentSettingsService.GetBigBoardCenterAddressDepartmentAsync(DepartmentId);
			var center = await _departmentSettingsService.GetBigBoardCenterGpsCoordinatesDepartmentAsync(DepartmentId);

			if (center != null)
			{
				var points = center.Split(char.Parse(","));

				if (points != null && points.Length == 2)
				{
					model.Latitude = points[0];
					model.Longitude = points[1];
				}
			}
			else if (address != null)
			{
				string coordinates =
					await _geoLocationProvider.GetLatLonFromAddress(string.Format("{0} {1} {2} {3} {4}", address.Address1,
						address.City, address.State, address.PostalCode, address.Country));

				if (!String.IsNullOrEmpty(coordinates))
				{
					double newLat;
					double newLon;
					var coordinatesArr = coordinates.Split(char.Parse(","));
					if (double.TryParse(coordinatesArr[0], out newLat) && double.TryParse(coordinatesArr[1], out newLon))
					{
						model.Latitude = newLat.ToString();
						model.Longitude = newLon.ToString();
					}
				}
			}

			return View(model);
		}

		public async Task<IActionResult> ViewType(int poiTypeId)
		{
			var model = new ViewTypeView();

			var type = await _mappingService.GetTypeByIdAsync(poiTypeId);

			if (type == null || type.DepartmentId != DepartmentId)
				Unauthorized();

			model.Type = type;
			var address = await _departmentSettingsService.GetBigBoardCenterAddressDepartmentAsync(DepartmentId);
			var center = await _departmentSettingsService.GetBigBoardCenterGpsCoordinatesDepartmentAsync(DepartmentId);

			if (center != null)
			{
				var points = center.Split(char.Parse(","));

				if (points != null && points.Length == 2)
				{
					model.Latitude = points[0];
					model.Longitude = points[1];
				}
			}
			else if (address != null)
			{
				string coordinates =
					await _geoLocationProvider.GetLatLonFromAddress(string.Format("{0} {1} {2} {3} {4}", address.Address1,
						address.City, address.State, address.PostalCode, address.Country));

				if (!String.IsNullOrEmpty(coordinates))
				{
					double newLat;
					double newLon;
					var coordinatesArr = coordinates.Split(char.Parse(","));
					if (double.TryParse(coordinatesArr[0], out newLat) && double.TryParse(coordinatesArr[1], out newLon))
					{
						model.Latitude = newLat.ToString();
						model.Longitude = newLon.ToString();
					}
				}
			}

			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> Layers()
		{
			var model = new LayersView();
			model.Layers = await _mappingService.GetMapLayersForTypeDepartmentAsync(DepartmentId, MapLayerTypes.TopLevel);
			
			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> NewLayer()
		{
			var model = new NewLayerView();
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.CenterCoordinates = await _departmentSettingsService.GetMapCenterCoordinatesAsync(model.Department);

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> NewLayer(NewLayerView model)
		{
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.CenterCoordinates = await _departmentSettingsService.GetMapCenterCoordinatesAsync(model.Department);

			if (ModelState.IsValid)
			{
				var newLayer = new MapLayer();
				newLayer.DepartmentId = DepartmentId;
				newLayer.Name = model.Name;
				newLayer.Color = model.Color;
				newLayer.IsOnByDefault = model.IsOnByDefault;
				newLayer.IsSearchable = model.IsSearchable;

				newLayer.Data = new MapLayerData();// JsonConvert.DeserializeObject<MapLayerData>(model.GeoJson);
				var feature = JsonConvert.DeserializeObject<FeatureCollection>(model.GeoJson);
				newLayer.Data.Hydrate(feature);

				newLayer.AddedById = UserId;
				newLayer.AddedOn = DateTime.UtcNow;

				var mapLayer = await _mappingService.SaveMapLayerAsync(newLayer);

				if (mapLayer.Id.Timestamp == 0)
				{
					ModelState.AddModelError("", "There was an error saving the layer. Please try again.");
					return View(model);
				}
				else
				{
					return RedirectToAction("Layers");
				}
			}

			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> EditLayer(string layerId)
		{
			if (String.IsNullOrWhiteSpace(layerId))
				return RedirectToAction("Layers");
			
			var model = new EditLayerView();
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.CenterCoordinates = await _departmentSettingsService.GetMapCenterCoordinatesAsync(model.Department);
			var layer = await _mappingService.GetMapLayersByIdAsync(layerId);

			if (layer == null || layer.DepartmentId != DepartmentId || layer.IsDeleted)
				Unauthorized();

			var feature = layer.Data.Convert();
			model.GeoJson = JsonConvert.SerializeObject(feature);

			model.Id = layer.Id.ToString();
			model.Name = layer.Name;
			model.Color = layer.Color;

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> EditLayer(EditLayerView model)
		{
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.CenterCoordinates = await _departmentSettingsService.GetMapCenterCoordinatesAsync(model.Department);

			if (ModelState.IsValid)
			{
				var newLayer = await _mappingService.GetMapLayersByIdAsync(model.Id);
				newLayer.DepartmentId = DepartmentId;
				newLayer.Name = model.Name;
				newLayer.Color = model.Color;
				newLayer.IsOnByDefault = model.IsOnByDefault;
				newLayer.IsSearchable = model.IsSearchable;

				newLayer.Data = new MapLayerData();// JsonConvert.DeserializeObject<MapLayerData>(model.GeoJson);
				var feature = JsonConvert.DeserializeObject<FeatureCollection>(model.GeoJson);
				newLayer.Data.Hydrate(feature);

				newLayer.UpdatedById = UserId;
				newLayer.UpdatedOn = DateTime.UtcNow;

				var mapLayer = await _mappingService.SaveMapLayerAsync(newLayer);

				return RedirectToAction("Layers");
			}

			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> DeleteLayer(string layerId)
		{
			if (String.IsNullOrWhiteSpace(layerId))
				return RedirectToAction("Layers");

			var layer = await _mappingService.GetMapLayersByIdAsync(layerId);

			if (layer == null || layer.DepartmentId != DepartmentId || layer.IsDeleted)
				Unauthorized();

			layer.IsDeleted = true;
			layer.UpdatedById = UserId;
			layer.UpdatedOn = DateTime.UtcNow;

			var mapLayer = await _mappingService.SaveMapLayerAsync(layer);

			return RedirectToAction("Layers");
		}

		public async Task<IActionResult> POIs()
		{
			var modal = new POIsView();
			modal.Types = await _mappingService.GetPOITypesForDepartmentAsync(DepartmentId);

			return View(modal);
		}

		[HttpGet]
		public async Task<IActionResult> AddPOIType()
		{
			var modal = new AddPOITypeView();
			modal.Type = new PoiType();
			modal.Type.Marker = "";
			modal.Type.Image = "";

			return View(modal);
		}

		[HttpGet]
		public async Task<IActionResult> ImportPOIs()
		{
			var model = new ImportPOIsView();

			return View(model);
		}

		[HttpPost]
		public async Task<IActionResult> ImportPOIs(ImportPOIsView modal, IFormFile fileToUpload, CancellationToken cancellationToken)
		{
			if (fileToUpload != null && fileToUpload.Length > 0)
			{
				//Path.GetExtension(file.FileName).ToLower() == "kmz"

				//var extenion = file.FileName.Substring(file.FileName.IndexOf(char.Parse(".")) + 1, file.FileName.Length - file.FileName.IndexOf(char.Parse(".")) - 1);
				var extenion = Path.GetExtension(fileToUpload.FileName).ToLower();

				if (!String.IsNullOrWhiteSpace(extenion))
					extenion = extenion.ToLower();

				if (extenion != ".kml" && extenion != ".kmz")
					ModelState.AddModelError("fileToUpload", string.Format("File type ({0}) is not a KMZ or KML extension to import POIs.", extenion));

				if (fileToUpload.Length > 10000000)
					ModelState.AddModelError("fileToUpload", "Document is too large, must be smaller then 10MB.");
			}

			if (ModelState.IsValid)
			{
				var coordinates = _kmlProvider.ImportFile(fileToUpload.OpenReadStream(), Path.GetExtension(fileToUpload.FileName).ToLower() == ".kmz");

				foreach (var coordinate in coordinates)
				{
					var poi = new Poi();
					poi.PoiTypeId = modal.TypeId;

					if (coordinate.Latitude.HasValue && coordinate.Longitude.HasValue)
					{
						poi.Latitude = coordinate.Latitude.Value;
						poi.Longitude = coordinate.Longitude.Value;

						await _mappingService.SavePOIAsync(poi, cancellationToken);
					}
				}

				return RedirectToAction("POIs");
			}

			return View(modal);
		}

		[HttpPost]
		public async Task<IActionResult> AddPOIType(AddPOITypeView modal, CancellationToken cancellationToken)
		{
			modal.Type.DepartmentId = DepartmentId;
			modal.Type.Marker = modal.MarkerType;

			if (ModelState.IsValid)
			{
				await _mappingService.SavePOITypeAsync(modal.Type, cancellationToken);

				return RedirectToAction("POIs");
			}

			return View(modal);
		}

		[HttpGet]
		public async Task<IActionResult> DeletePOIType(int poiTypeId, CancellationToken cancellationToken)
		{
			var type = await _mappingService.GetTypeByIdAsync(poiTypeId);

			if (type != null)
			{
				if (type.DepartmentId != DepartmentId)
					Unauthorized();

				await _mappingService.DeletePOITypeAsync(poiTypeId, cancellationToken);
			}

			return RedirectToAction("POIs");
		}

		[HttpGet]
		public async Task<IActionResult> AddPOI(int poiTypeId)
		{
			var modal = new AddPOIView();
			modal.TypeId = poiTypeId;
			modal.Poi = new Poi();


			return View(modal);
		}

		[HttpPost]
		public async Task<IActionResult> AddPOI(AddPOIView modal, CancellationToken cancellationToken)
		{
			var type = await _mappingService.GetTypeByIdAsync(modal.TypeId);

			if (type == null)
			{
				modal.Message = "Cannot add POI. Please go back and try adding from the type again.";
				ModelState.AddModelError("", "Cannot add POI. Please go back and try adding from the type again.");
			}
			else
			{
				if (type.DepartmentId != DepartmentId)
					Unauthorized();
			}

			if (ModelState.IsValid)
			{
				modal.Poi.PoiTypeId = modal.TypeId;
				await _mappingService.SavePOIAsync(modal.Poi, cancellationToken);

				return RedirectToAction("POIs");
			}

			return View(modal);
		}

		[HttpGet]
		public async Task<IActionResult> GetMapData(MapSettingsInput input)
		{
			MapDataJson dataJson = new MapDataJson();

			var calls = await _callsService.GetActiveCallsByDepartmentAsync(DepartmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var stations = await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(DepartmentId);
			var lastUserActionlogs = await _actionLogsService.GetLastActionLogsForDepartmentAsync(DepartmentId);
			var personnelNames = await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId);
			var unitStates = await _unitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync(DepartmentId);

			var userLocationPermission = await _permissionsService.GetPermissionByDepartmentTypeAsync(DepartmentId, PermissionTypes.CanSeePersonnelLocations);
			if (userLocationPermission != null)
			{
				var userGroup = await _departmentGroupsService.GetGroupForUserAsync(UserId, DepartmentId);
				int? groupId = null;

				if (userGroup != null)
					groupId = userGroup.DepartmentGroupId;

				var roles = await _personnelRolesService.GetRolesForUserAsync(UserId, DepartmentId);
				var allowedUsers = await _permissionsService.GetAllowedUsersAsync(userLocationPermission, DepartmentId, groupId, ClaimsAuthorizationHelper.IsUserDepartmentAdmin(), ClaimsAuthorizationHelper.IsUserDepartmentAdmin(), roles);

				lastUserActionlogs.RemoveAll(x => !allowedUsers.Contains(x.UserId));
			}

			if (input.ShowDistricts)
			{
				foreach (var station in stations)
				{
					if (!String.IsNullOrWhiteSpace(station.Geofence))
					{
						GeofenceJson geofence = new GeofenceJson();
						geofence.Name = station.Name;
						geofence.Color = station.GeofenceColor;
						geofence.Fence = station.Geofence;

						dataJson.Geofences.Add(geofence);
					}
				}
			}

			if (input.ShowStations)
			{

				foreach (var station in stations)
				{
					try
					{
						MapMakerInfo info = new MapMakerInfo();
						info.ImagePath = "Station";
						info.Title = station.Name;
						info.InfoWindowContent = station.Name;

						if (station.Address != null)
						{
							string coordinates =
								await _geoLocationProvider.GetLatLonFromAddress(string.Format("{0} {1} {2} {3}", station.Address.Address1,
									station.Address.City,
									station.Address.State,
									station.Address.PostalCode));

							if (!String.IsNullOrEmpty(coordinates))
							{
								info.Latitude = double.Parse(coordinates.Split(char.Parse(","))[0]);
								info.Longitude = double.Parse(coordinates.Split(char.Parse(","))[1]);

								dataJson.Markers.Add(info);
							}
						}
						else if (!String.IsNullOrWhiteSpace(station.Latitude) && !String.IsNullOrWhiteSpace(station.Longitude))
						{
							info.Latitude = double.Parse(station.Latitude);
							info.Longitude = double.Parse(station.Longitude);

							dataJson.Markers.Add(info);
						}
					}
					catch (Exception ex)
					{
						//Logging.LogException(ex);
					}
				}
			}

			if (input.ShowCalls)
			{
				foreach (var call in calls)
				{
					MapMakerInfo info = new MapMakerInfo();
					info.ImagePath = "Call";
					info.Title = call.Name;
					info.InfoWindowContent = call.NatureOfCall;

					if (!String.IsNullOrEmpty(call.GeoLocationData) && call.GeoLocationData.Length > 1)
					{
						try
						{
							info.Latitude = double.Parse(call.GeoLocationData.Split(char.Parse(","))[0]);
							info.Longitude = double.Parse(call.GeoLocationData.Split(char.Parse(","))[1]);

							dataJson.Markers.Add(info);
						}
						catch
						{
						}
					}
					else if (!String.IsNullOrEmpty(call.Address))
					{
						string coordinates = await _geoLocationProvider.GetLatLonFromAddress(call.Address);
						if (!String.IsNullOrEmpty(coordinates))
						{
							info.Latitude = double.Parse(coordinates.Split(char.Parse(","))[0]);
							info.Longitude = double.Parse(coordinates.Split(char.Parse(","))[1]);
						}

						dataJson.Markers.Add(info);
					}
				}
			}

			if (input.ShowUnits)
			{
				foreach (var unit in unitStates)
				{
					if (unit.Latitude.HasValue && unit.Latitude.Value != 0 && unit.Longitude.HasValue &&
							unit.Longitude.Value != 0)
					{
						MapMakerInfo info = new MapMakerInfo();
						info.ImagePath = "Engine_Responding";
						info.Title = unit.Unit.Name;
						info.InfoWindowContent = "";
						info.Latitude = double.Parse(unit.Latitude.Value.ToString());
						info.Longitude = double.Parse(unit.Longitude.Value.ToString());

						dataJson.Markers.Add(info);
					}
				}
			}

			if (input.ShowPersonnel)
			{
				foreach (var person in lastUserActionlogs)
				{
					if (!String.IsNullOrWhiteSpace(person.GeoLocationData))
					{
						MapMakerInfo info = new MapMakerInfo();
						info.ImagePath = "Person";

						var name = personnelNames.FirstOrDefault(x => x.UserId == person.UserId);
						if (name != null)
						{
							info.Title = name.Name;
							info.InfoWindowContent = "";
						}
						else
						{
							info.Title = "";
							info.InfoWindowContent = "";
						}

						var infos = person.GeoLocationData.Split(char.Parse(","));
						if (infos != null && infos.Length == 2)
						{
							info.Latitude = double.Parse(infos[0]);
							info.Longitude = double.Parse(infos[1]);

							dataJson.Markers.Add(info);
						}
					}
				}
			}

			if (input.ShowPOIs)
			{
				var poiTypes = await _mappingService.GetPOITypesForDepartmentAsync(DepartmentId);

				foreach (var poiType in poiTypes)
				{
					foreach (var poi in poiType.Pois)
					{
						MapMakerInfo info = new MapMakerInfo();
						info.ImagePath = poiType.Image;
						info.Marker = poiType.Marker;
						info.Title = poiType.Name;
						info.InfoWindowContent = "";
						info.Latitude = poi.Latitude;
						info.Longitude = poi.Longitude;
						info.Color = poiType.Color;

						dataJson.Pois.Add(info);
					}
				}
			}

			return Json(dataJson);
		}

		[HttpGet]
		public async Task<IActionResult> GetTypesMapData(int poiTypeId)
		{
			MapDataJson dataJson = new MapDataJson();

			var poiType = await _mappingService.GetTypeByIdAsync(poiTypeId);

			if (poiType == null || poiType.DepartmentId != DepartmentId)
				Unauthorized();

			foreach (var poi in poiType.Pois)
			{
				MapMakerInfo info = new MapMakerInfo();
				info.ImagePath = poiType.Image;
				info.Marker = poiType.Marker;
				info.Title = poiType.Name;
				info.InfoWindowContent = "";
				info.Latitude = poi.Latitude;
				info.Longitude = poi.Longitude;
				info.Color = poiType.Color;

				dataJson.Pois.Add(info);
			}

			return Json(dataJson);
		}

		[HttpGet]
		public async Task<IActionResult> GetPoisForType(int poiTypeId)
		{
			var poisJson = new List<PoiJson>();

			var poiType = await _mappingService.GetTypeByIdAsync(poiTypeId);

			if (poiType == null || poiType.DepartmentId != DepartmentId)
				Unauthorized();

			foreach (var poi in poiType.Pois)
			{
				var poiJson = new PoiJson();
				poiJson.PoiId = poi.PoiId;
				poiJson.PoiTypeId = poi.PoiTypeId;
				poiJson.Latitude = poi.Latitude;
				poiJson.Longitude = poi.Longitude;
				poiJson.Note = poi.Note;

				poisJson.Add(poiJson);
			}

			return Json(poisJson);
		}

		[HttpGet]
		public async Task<IActionResult> LiveRouting(int callId)
		{
			StationRoutingView model = new StationRoutingView();

			var call = await _callsService.GetCallByIdAsync(callId);

			if (call.DepartmentId != DepartmentId)
				Unauthorized();

			string endLat = "";
			string endLon = "";

			var callCocationParts = call.GeoLocationData.Split(char.Parse(","));
			endLat = callCocationParts[0];
			endLon = callCocationParts[1];

			model.EndLat = endLat;
			model.EndLon = endLon;

			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> StationRouting(int stationId, int callId)
		{
			StationRoutingView model = new StationRoutingView();

			var station = await _departmentGroupsService.GetGroupByIdAsync(stationId);
			var call = await _callsService.GetCallByIdAsync(callId);

			if (station.DepartmentId != DepartmentId)
				Unauthorized();

			if (call.DepartmentId != DepartmentId)
				Unauthorized();

			string startLat = "";
			string startLon = "";

			if (!String.IsNullOrWhiteSpace(station.Latitude) && !String.IsNullOrWhiteSpace(station.Longitude))
			{
				startLat = station.Latitude;
				startLon = station.Longitude;
			} else if (station.Address != null)
			{
				var location = await _geoLocationProvider.GetLatLonFromAddress(station.Address.FormatAddress());

				if (!String.IsNullOrWhiteSpace(location))
				{
					var locationParts = location.Split(char.Parse(","));
					startLat = locationParts[0];
					startLon = locationParts[1];
				}
			}

			string endLat = "";
			string endLon = "";

			var callCocationParts = call.GeoLocationData.Split(char.Parse(","));
			endLat = callCocationParts[0];
			endLon = callCocationParts[1];

			model.StartLat = startLat;
			model.StartLon = startLon;
			model.EndLat = endLat;
			model.EndLon = endLon;

			return View(model);
		}
	}
}
