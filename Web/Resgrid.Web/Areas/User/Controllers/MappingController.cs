using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GeoJSON.Net.Feature;
using GeoJSON.Net.Geometry;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
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
		private readonly ICustomMapsService _customMapsService;
		private readonly IFileService _fileService;
		private readonly ICustomMapImageService _customMapImageService;
		private readonly IWebHostEnvironment _webHostEnvironment;

		// Physical root directory for tiled map storage under wwwroot
		private string TileBasePath => Path.Combine(_webHostEnvironment.WebRootPath, "custommaps");

		public MappingController(IDepartmentSettingsService departmentSettingsService,
			IGeoLocationProvider geoLocationProvider, ICallsService callsService,
			IDepartmentsService departmentsService, IDepartmentGroupsService departmentGroupsService,
			IActionLogsService actionLogsService, IUnitsService unitsService, IMappingService mappingService,
			IKmlProvider kmlProvider, IPermissionsService permissionsService, IPersonnelRolesService personnelRolesService,
			ICustomMapsService customMapsService, IFileService fileService,
			ICustomMapImageService customMapImageService, IWebHostEnvironment webHostEnvironment)
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
			_customMapsService = customMapsService;
			_fileService = fileService;
			_customMapImageService = customMapImageService;
			_webHostEnvironment = webHostEnvironment;
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

			if (type == null)
				return NotFound();

			if (type.DepartmentId != DepartmentId)
				return Unauthorized();

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

			if (layer == null || layer.IsDeleted)
				return NotFound();

			if (layer.DepartmentId != DepartmentId)
				return Unauthorized();

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

			if (layer == null || layer.IsDeleted)
				return NotFound();

			if (layer.DepartmentId != DepartmentId)
				return Unauthorized();

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
					return Unauthorized();

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
					return Unauthorized();
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

			if (poiType == null)
				return NotFound();

			if (poiType.DepartmentId != DepartmentId)
				return Unauthorized();

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

			if (poiType == null)
				return NotFound();

			if (poiType.DepartmentId != DepartmentId)
				return Unauthorized();

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

			if (call == null)
				return NotFound();

			if (call.DepartmentId != DepartmentId)
				return Unauthorized();

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

			if (station == null)
				return NotFound();

			if (station.DepartmentId != DepartmentId)
				return Unauthorized();

			if (call == null)
				return NotFound();

			if (call.DepartmentId != DepartmentId)
				return Unauthorized();

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

		// ── Custom Maps ──────────────────────────────────────────────────────────

		[HttpGet]
		public async Task<IActionResult> CustomMaps()
		{
			var model = new CustomMapsView();
			model.CustomMaps = await _customMapsService.GetCustomMapsForDepartmentAsync(DepartmentId);

			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> NewCustomMap()
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var centerCoordinates = await _departmentSettingsService.GetMapCenterCoordinatesAsync(department);

			var model = new NewCustomMapView
			{
				CenterCoordinates = centerCoordinates,
				MapTypes = BuildMapTypeSelectList()
			};

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> NewCustomMap(NewCustomMapView model, CancellationToken cancellationToken)
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.CenterCoordinates = await _departmentSettingsService.GetMapCenterCoordinatesAsync(department);
			model.MapTypes = BuildMapTypeSelectList();

			if (ModelState.IsValid)
			{
				var customMap = new CustomMap
				{
					DepartmentId = DepartmentId,
					Name = model.Name,
					Description = model.Description,
					Type = model.Type,
					IsActive = model.IsActive,
					DefaultZoom = model.DefaultZoom,
					MinZoom = model.MinZoom,
					MaxZoom = model.MaxZoom,
					BoundsTopLeftLat = model.BoundsTopLeftLat,
					BoundsTopLeftLng = model.BoundsTopLeftLng,
					BoundsBottomRightLat = model.BoundsBottomRightLat,
					BoundsBottomRightLng = model.BoundsBottomRightLng,
					EventStartsOn = model.EventStartsOn,
					EventEndsOn = model.EventEndsOn,
					AddedById = UserId,
					AddedOn = DateTime.UtcNow
				};

				await _customMapsService.SaveCustomMapAsync(customMap, cancellationToken);
				return RedirectToAction("CustomMaps");
			}

			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> EditCustomMap(string customMapId)
		{
			if (string.IsNullOrWhiteSpace(customMapId))
				return RedirectToAction("CustomMaps");

			var customMap = await _customMapsService.GetCustomMapByIdAsync(customMapId);
			if (customMap == null)
				return NotFound();

			if (customMap.DepartmentId != DepartmentId)
				return Unauthorized();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var centerCoordinates = await _departmentSettingsService.GetMapCenterCoordinatesAsync(department);

			var model = new EditCustomMapView
			{
				CustomMapId = customMap.CustomMapId,
				Name = customMap.Name,
				Description = customMap.Description,
				Type = customMap.Type,
				IsActive = customMap.IsActive,
				DefaultZoom = customMap.DefaultZoom,
				MinZoom = customMap.MinZoom,
				MaxZoom = customMap.MaxZoom,
				BoundsTopLeftLat = customMap.BoundsTopLeftLat,
				BoundsTopLeftLng = customMap.BoundsTopLeftLng,
				BoundsBottomRightLat = customMap.BoundsBottomRightLat,
				BoundsBottomRightLng = customMap.BoundsBottomRightLng,
				EventStartsOn = customMap.EventStartsOn,
				EventEndsOn = customMap.EventEndsOn,
				CenterCoordinates = centerCoordinates,
				MapTypes = BuildMapTypeSelectList()
			};

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> EditCustomMap(EditCustomMapView model, CancellationToken cancellationToken)
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.CenterCoordinates = await _departmentSettingsService.GetMapCenterCoordinatesAsync(department);
			model.MapTypes = BuildMapTypeSelectList();

			if (ModelState.IsValid)
			{
				var customMap = await _customMapsService.GetCustomMapByIdAsync(model.CustomMapId);
				if (customMap == null)
					return NotFound();

				if (customMap.DepartmentId != DepartmentId)
					return Unauthorized();

				customMap.Name = model.Name;
				customMap.Description = model.Description;
				customMap.Type = model.Type;
				customMap.IsActive = model.IsActive;
				customMap.DefaultZoom = model.DefaultZoom;
				customMap.MinZoom = model.MinZoom;
				customMap.MaxZoom = model.MaxZoom;
				customMap.BoundsTopLeftLat = model.BoundsTopLeftLat;
				customMap.BoundsTopLeftLng = model.BoundsTopLeftLng;
				customMap.BoundsBottomRightLat = model.BoundsBottomRightLat;
				customMap.BoundsBottomRightLng = model.BoundsBottomRightLng;
				customMap.EventStartsOn = model.EventStartsOn;
				customMap.EventEndsOn = model.EventEndsOn;
				customMap.UpdatedOn = DateTime.UtcNow;
				customMap.UpdatedById = UserId;

				await _customMapsService.SaveCustomMapAsync(customMap, cancellationToken);
				return RedirectToAction("CustomMaps");
			}

			return View(model);
		}

		[HttpGet]
		public async Task<IActionResult> DeleteCustomMap(string customMapId, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(customMapId))
				return RedirectToAction("CustomMaps");

			var customMap = await _customMapsService.GetCustomMapByIdAsync(customMapId);
			if (customMap == null)
				return NotFound();

			if (customMap.DepartmentId != DepartmentId)
				return Unauthorized();

			await _customMapsService.DeleteCustomMapAsync(customMapId, cancellationToken);
			return RedirectToAction("CustomMaps");
		}

		// ── Floors ───────────────────────────────────────────────────────────────

		[HttpGet]
		public async Task<IActionResult> ManageFloors(string customMapId)
		{
			if (string.IsNullOrWhiteSpace(customMapId))
				return RedirectToAction("CustomMaps");

			var customMap = await _customMapsService.GetCustomMapByIdAsync(customMapId);
			if (customMap == null)
				return NotFound();

			if (customMap.DepartmentId != DepartmentId)
				return Unauthorized();

			var model = new ManageFloorsView
			{
				CustomMapId = customMapId,
				CustomMapName = customMap.Name,
				Floors = await _customMapsService.GetFloorsForMapAsync(customMapId)
			};

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> AddFloor(ManageFloorsView model, IFormFile floorImage, CancellationToken cancellationToken)
		{
			var customMap = await _customMapsService.GetCustomMapByIdAsync(model.CustomMapId);
			if (customMap == null)
				return NotFound();

			if (customMap.DepartmentId != DepartmentId)
				return Unauthorized();

			if (string.IsNullOrWhiteSpace(model.NewFloorName))
				ModelState.AddModelError("NewFloorName", "Floor name is required.");

			if (!ModelState.IsValid)
				return RedirectToAction("ManageFloors", new { customMapId = model.CustomMapId });

			var floor = new CustomMapFloor
			{
				CustomMapId = model.CustomMapId,
				Name = model.NewFloorName,
				FloorNumber = model.NewFloorNumber,
				SortOrder = model.NewSortOrder,
				IsDefault = model.NewIsDefault,
				Elevation = model.NewElevation,
				StorageType = (int)CustomMapFloorStorageType.None
			};

			await _customMapsService.SaveFloorAsync(floor, cancellationToken);

			// If an image was supplied, upload it immediately
			if (floorImage != null && floorImage.Length > 0)
			{
				var (valid, error) = ValidateImageUpload(floorImage);
				if (valid)
				{
					await _customMapsService.UploadFloorImageAsync(
						floor.CustomMapFloorId, floorImage, DepartmentId,
						TileBasePath,
						BuildTileUrlTemplate(floor.CustomMapFloorId),
						BuildImageUrlTemplate(),
						cancellationToken);
				}
			}

			return RedirectToAction("ManageFloors", new { customMapId = model.CustomMapId });
		}

		// ── Floor Image Management ───────────────────────────────────────────────

		/// <summary>GET: Upload/replace image for a floor (dedicated full-page form).</summary>
		[HttpGet]
		public async Task<IActionResult> UploadFloorImage(string customMapFloorId)
		{
			if (string.IsNullOrWhiteSpace(customMapFloorId))
				return RedirectToAction("CustomMaps");

			var floor = await _customMapsService.GetFloorByIdAsync(customMapFloorId);
			if (floor == null) return NotFound();

			var customMap = await _customMapsService.GetCustomMapByIdAsync(floor.CustomMapId);
			if (customMap == null || customMap.DepartmentId != DepartmentId) return Unauthorized();

			var model = new UploadFloorImageView
			{
				CustomMapFloorId = customMapFloorId,
				CustomMapId = floor.CustomMapId,
				CustomMapName = customMap.Name,
				FloorName = floor.Name,
				CurrentImageUrl = floor.ImageUrl,
				CurrentStorageType = (CustomMapFloorStorageType)floor.StorageType,
				ImageWidthPx = floor.ImageWidthPx,
				ImageHeightPx = floor.ImageHeightPx,
				TileZoomLevels = floor.TileZoomLevels,
				ThresholdMb = _customMapImageService.TiledThresholdBytes / (1024 * 1024)
			};

			return View(model);
		}

		/// <summary>POST: Process the uploaded image for a floor.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[RequestSizeLimit(524_288_000)] // 500 MB max
		public async Task<IActionResult> UploadFloorImage(UploadFloorImageView model, IFormFile floorImageFile, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(model.CustomMapFloorId))
				return RedirectToAction("CustomMaps");

			var floor = await _customMapsService.GetFloorByIdAsync(model.CustomMapFloorId);
			if (floor == null) return NotFound();

			var customMap = await _customMapsService.GetCustomMapByIdAsync(floor.CustomMapId);
			if (customMap == null || customMap.DepartmentId != DepartmentId) return Unauthorized();

			if (floorImageFile == null || floorImageFile.Length == 0)
			{
				ModelState.AddModelError("floorImageFile", "Please select an image file.");
				model.FloorName = floor.Name;
				model.CustomMapName = customMap.Name;
				model.ThresholdMb = _customMapImageService.TiledThresholdBytes / (1024 * 1024);
				return View(model);
			}

			var (valid, error) = ValidateImageUpload(floorImageFile);
			if (!valid)
			{
				ModelState.AddModelError("floorImageFile", error);
				model.FloorName = floor.Name;
				model.CustomMapName = customMap.Name;
				model.ThresholdMb = _customMapImageService.TiledThresholdBytes / (1024 * 1024);
				return View(model);
			}

			var updated = await _customMapsService.UploadFloorImageAsync(
				model.CustomMapFloorId, floorImageFile, DepartmentId,
				TileBasePath,
				BuildTileUrlTemplate(model.CustomMapFloorId),
				BuildImageUrlTemplate(),
				cancellationToken);

			if (updated == null)
			{
				ModelState.AddModelError("", "There was an error saving the image. Please try again.");
				model.FloorName = floor.Name;
				model.CustomMapName = customMap.Name;
				model.ThresholdMb = _customMapImageService.TiledThresholdBytes / (1024 * 1024);
				return View(model);
			}

			return RedirectToAction("ManageFloors", new { customMapId = floor.CustomMapId });
		}

		/// <summary>GET: Removes the stored image for a floor.</summary>
		[HttpGet]
		public async Task<IActionResult> DeleteFloorImage(string customMapFloorId, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(customMapFloorId))
				return RedirectToAction("CustomMaps");

			var floor = await _customMapsService.GetFloorByIdAsync(customMapFloorId);
			if (floor == null) return NotFound();

			var customMap = await _customMapsService.GetCustomMapByIdAsync(floor.CustomMapId);
			if (customMap == null || customMap.DepartmentId != DepartmentId) return Unauthorized();

			await _customMapsService.DeleteFloorImageAsync(customMapFloorId, TileBasePath, cancellationToken);

			return RedirectToAction("ManageFloors", new { customMapId = floor.CustomMapId });
		}

		/// <summary>GET: Serves the raw image bytes for a DatabaseBlob floor image.</summary>
		[HttpGet]
		public async Task<IActionResult> GetFloorImage(int fileId)
		{
			var file = await _fileService.GetFileByIdAsync(fileId);
			if (file == null || file.DepartmentId != DepartmentId)
				return NotFound();

			Response.Headers["Cache-Control"] = "public, max-age=86400";
			return File(file.Data, file.FileType ?? "image/png");
		}

		/// <summary>GET: Serves a single tile from a TiledPyramid floor.</summary>
		[HttpGet]
		[ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
		public async Task<IActionResult> GetFloorTile(string floorId, int z, int x, int y)
		{
			if (string.IsNullOrWhiteSpace(floorId))
				return NotFound();

			var floor = await _customMapsService.GetFloorByIdAsync(floorId);
			if (floor == null) return NotFound();

			var customMap = await _customMapsService.GetCustomMapByIdAsync(floor.CustomMapId);
			if (customMap == null || customMap.DepartmentId != DepartmentId) return Unauthorized();

			var tilePath = _customMapImageService.GetTilePath(TileBasePath, floorId, z, x, y);
			if (tilePath == null) return NotFound();

			return PhysicalFile(tilePath, "image/png");
		}

		[HttpGet]
		public async Task<IActionResult> DeleteFloor(string customMapFloorId, string customMapId, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(customMapFloorId))
				return RedirectToAction("CustomMaps");

			var floor = await _customMapsService.GetFloorByIdAsync(customMapFloorId);
			if (floor == null)
				return NotFound();

			var customMap = await _customMapsService.GetCustomMapByIdAsync(floor.CustomMapId);
			if (customMap == null || customMap.DepartmentId != DepartmentId)
				return Unauthorized();

			// Clean up stored image assets before deleting the floor
			if (floor.StorageType != (int)CustomMapFloorStorageType.None)
				await _customMapsService.DeleteFloorImageAsync(customMapFloorId, TileBasePath, cancellationToken);

			await _customMapsService.DeleteFloorAsync(customMapFloorId, cancellationToken);
			return RedirectToAction("ManageFloors", new { customMapId = floor.CustomMapId });
		}

		// ── Zones ────────────────────────────────────────────────────────────────

		[HttpGet]
		public async Task<IActionResult> ManageZones(string customMapFloorId)
		{
			if (string.IsNullOrWhiteSpace(customMapFloorId))
				return RedirectToAction("CustomMaps");

			var floor = await _customMapsService.GetFloorByIdAsync(customMapFloorId);
			if (floor == null)
				return NotFound();

			var customMap = await _customMapsService.GetCustomMapByIdAsync(floor.CustomMapId);
			if (customMap == null || customMap.DepartmentId != DepartmentId)
				return Unauthorized();

			var zones = await _customMapsService.GetZonesForFloorAsync(customMapFloorId);

			var featureCollection = BuildZoneFeatureCollection(zones);
			var zonesGeoJson = JsonConvert.SerializeObject(featureCollection);

			var model = new ManageZonesView
			{
				CustomMapFloorId = customMapFloorId,
				CustomMapId = customMap.CustomMapId,
				CustomMapName = customMap.Name,
				FloorName = floor.Name,
				ImageUrl = floor.ImageUrl,
				TileBaseUrl = floor.TileBaseUrl,
				StorageType = (CustomMapFloorStorageType)floor.StorageType,
				TileZoomLevels = floor.TileZoomLevels ?? 1,
				BoundsTopLeftLat = customMap.BoundsTopLeftLat,
				BoundsTopLeftLng = customMap.BoundsTopLeftLng,
				BoundsBottomRightLat = customMap.BoundsBottomRightLat,
				BoundsBottomRightLng = customMap.BoundsBottomRightLng,
				DefaultZoom = customMap.DefaultZoom,
				Zones = zones,
				ZonesGeoJson = zonesGeoJson,
				ZoneTypes = BuildZoneTypeSelectList()
			};

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SaveZones(string customMapFloorId, string zonesGeoJson, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(customMapFloorId))
				return RedirectToAction("CustomMaps");

			var floor = await _customMapsService.GetFloorByIdAsync(customMapFloorId);
			if (floor == null)
				return NotFound();

			var customMap = await _customMapsService.GetCustomMapByIdAsync(floor.CustomMapId);
			if (customMap == null || customMap.DepartmentId != DepartmentId)
				return Unauthorized();

			if (!string.IsNullOrWhiteSpace(zonesGeoJson))
			{
				try
				{
					var existingZones = await _customMapsService.GetZonesForFloorAsync(customMapFloorId);
					var featureCollection = JsonConvert.DeserializeObject<FeatureCollection>(zonesGeoJson);

					if (featureCollection?.Features != null)
					{
						var incomingIds = new HashSet<string>();

						foreach (var feature in featureCollection.Features)
						{
							var zoneId = feature.Properties.ContainsKey("zoneId") ? feature.Properties["zoneId"]?.ToString() : null;

							var existingZone = !string.IsNullOrWhiteSpace(zoneId)
								? existingZones.FirstOrDefault(z => z.CustomMapZoneId == zoneId)
								: null;

							// Only mark as "keep" when we actually found it in the DB — new zones
							// have no DB record yet so they don't need to be guarded from deletion.
							if (existingZone != null)
								incomingIds.Add(zoneId);

							// Reuse the existing zone entity for updates; create a bare new one (no
							// pre-assigned ID) for inserts so SaveOrUpdateAsync routes correctly.
							var zone = existingZone ?? new CustomMapZone { CustomMapFloorId = customMapFloorId };

							zone.Name = feature.Properties.ContainsKey("name") ? feature.Properties["name"]?.ToString() ?? "Zone" : "Zone";
							zone.ZoneType = feature.Properties.ContainsKey("zoneType") && int.TryParse(feature.Properties["zoneType"]?.ToString(), out var zt) ? zt : 0;
							zone.Color = feature.Properties.ContainsKey("color") ? feature.Properties["color"]?.ToString() ?? "#3388ff" : "#3388ff";
							zone.IsSearchable = feature.Properties.ContainsKey("isSearchable") && bool.TryParse(feature.Properties["isSearchable"]?.ToString(), out var isSrch) && isSrch;
							zone.IsActive = !feature.Properties.ContainsKey("isActive") || bool.TryParse(feature.Properties["isActive"]?.ToString(), out var isAct) && isAct;
							zone.Metadata = feature.Properties.ContainsKey("metadata") ? feature.Properties["metadata"]?.ToString() : null;
							zone.PolygonGeoJson = JsonConvert.SerializeObject(feature.Geometry);

							await _customMapsService.SaveZoneAsync(zone, cancellationToken);
						}

						foreach (var existing in existingZones.Where(z => !incomingIds.Contains(z.CustomMapZoneId)))
							await _customMapsService.DeleteZoneAsync(existing.CustomMapZoneId, cancellationToken);
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}

			return RedirectToAction("ManageZones", new { customMapFloorId });
		}

		[HttpGet]
		public async Task<IActionResult> DeleteZone(string customMapZoneId, string customMapFloorId, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(customMapZoneId))
				return RedirectToAction("CustomMaps");

			var zone = await _customMapsService.GetZoneByIdAsync(customMapZoneId);
			if (zone == null)
				return NotFound();

			var floor = await _customMapsService.GetFloorByIdAsync(zone.CustomMapFloorId);
			if (floor == null)
				return NotFound();

			var customMap = await _customMapsService.GetCustomMapByIdAsync(floor.CustomMapId);
			if (customMap == null || customMap.DepartmentId != DepartmentId)
				return Unauthorized();

			await _customMapsService.DeleteZoneAsync(customMapZoneId, cancellationToken);
			return RedirectToAction("ManageZones", new { customMapFloorId = zone.CustomMapFloorId });
		}

		// ── Helpers ──────────────────────────────────────────────────────────────

		/// <summary>
		/// Validates an uploaded image file (extension + size).
		/// Returns (true, null) on success or (false, errorMessage) on failure.
		/// Large files are allowed — the service chooses tiling automatically.
		/// </summary>
		private static (bool Valid, string Error) ValidateImageUpload(IFormFile file)
		{
			var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
			if (extension != ".jpg" && extension != ".jpeg" && extension != ".png" && extension != ".tif" && extension != ".tiff")
				return (false, "Only JPG, PNG, and TIFF images are accepted.");

			if (file.Length > 524_288_000) // 500 MB absolute ceiling
				return (false, "Image must be smaller than 500 MB.");

			return (true, null);
		}

		/// <summary>Returns the tile URL template for a given floor.</summary>
		private string BuildTileUrlTemplate(string floorId) =>
			Url.Action("GetFloorTile", "Mapping", new { area = "User", floorId, z = "{z}", x = "{x}", y = "{y}" });

		/// <summary>Returns the imageUrl template for a DatabaseBlob floor image (placeholder {fileId}).</summary>
		private string BuildImageUrlTemplate() =>
			Url.Action("GetFloorImage", "Mapping", new { area = "User", fileId = "__FILEID__" })
				?.Replace("__FILEID__", "{fileId}");

		private static List<SelectListItem> BuildMapTypeSelectList() =>
			Enum.GetValues<CustomMapType>()
				.Select(t => new SelectListItem(t.ToString(), ((int)t).ToString()))
				.ToList();

		private static List<SelectListItem> BuildZoneTypeSelectList() =>
			Enum.GetValues<CustomMapZoneType>()
				.Select(t => new SelectListItem(t.ToString(), ((int)t).ToString()))
				.ToList();

		private static object BuildZoneFeatureCollection(List<CustomMapZone> zones)
		{
			var features = zones.Select(z =>
			{
				object geometry = null;
				if (!string.IsNullOrWhiteSpace(z.PolygonGeoJson))
				{
					try { geometry = JsonConvert.DeserializeObject(z.PolygonGeoJson); }
					catch { geometry = null; }
				}

				return new
				{
					type = "Feature",
					geometry,
					properties = new
					{
						zoneId = z.CustomMapZoneId,
						name = z.Name,
						zoneType = z.ZoneType,
						color = z.Color,
						isSearchable = z.IsSearchable,
						isActive = z.IsActive,
						metadata = z.Metadata
					}
				};
			}).ToList();

			return new { type = "FeatureCollection", features };
		}
	}
}
