using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.Home;
using Resgrid.Web.Areas.User.Models.Mapping;
using Resgrid.Web.Helpers;

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

		public IActionResult Index()
		{
			var model = new MapIndexView();

			var address = _departmentSettingsService.GetBigBoardCenterAddressDepartment(DepartmentId);
			var center = _departmentSettingsService.GetBigBoardCenterGpsCoordinatesDepartment(DepartmentId);

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
					_geoLocationProvider.GetLatLonFromAddress(string.Format("{0} {1} {2} {3} {4}", address.Address1,
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

		public IActionResult ViewType(int poiTypeId)
		{
			var model = new ViewTypeView();

			var type = _mappingService.GetTypeById(poiTypeId);

			if (type == null || type.DepartmentId != DepartmentId)
				Unauthorized();

			model.Type = type;
			var address = _departmentSettingsService.GetBigBoardCenterAddressDepartment(DepartmentId);
			var center = _departmentSettingsService.GetBigBoardCenterGpsCoordinatesDepartment(DepartmentId);

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
					_geoLocationProvider.GetLatLonFromAddress(string.Format("{0} {1} {2} {3} {4}", address.Address1,
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

		public IActionResult POIs()
		{
			var modal = new POIsView();
			modal.Types = _mappingService.GetPOITypesForDepartment(DepartmentId);

			return View(modal);
		}

		[HttpGet]
		public IActionResult AddPOIType()
		{
			var modal = new AddPOITypeView();
			modal.Type = new PoiType();
			modal.Type.Marker = "";

			return View(modal);
		}

		[HttpGet]
		public IActionResult ImportPOIs()
		{
			var model = new ImportPOIsView();

			return View(model);
		}

		[HttpPost]
		public IActionResult ImportPOIs(ImportPOIsView modal, IFormFile fileToUpload)
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

						_mappingService.SavePOI(poi);
					}
				}

				return RedirectToAction("POIs");
			}

			return View(modal);
		}

		[HttpPost]
		public IActionResult AddPOIType(AddPOITypeView modal)
		{
			modal.Type.DepartmentId = DepartmentId;
			modal.Type.Marker = modal.MarkerType;

			if (ModelState.IsValid)
			{
				_mappingService.SavePOIType(modal.Type);

				return RedirectToAction("POIs");
			}

			return View(modal);
		}

		[HttpGet]
		public IActionResult DeletePOIType(int poiTypeId)
		{
			var type = _mappingService.GetTypeById(poiTypeId);

			if (type != null)
			{
				if (type.DepartmentId != DepartmentId)
					Unauthorized();

				_mappingService.DeletePOIType(poiTypeId);
			}

			return RedirectToAction("POIs");
		}

		[HttpGet]
		public IActionResult AddPOI(int poiTypeId)
		{
			var modal = new AddPOIView();
			modal.TypeId = poiTypeId;
			modal.Poi = new Poi();


			return View(modal);
		}

		[HttpPost]
		public IActionResult AddPOI(AddPOIView modal)
		{
			var type = _mappingService.GetTypeById(modal.TypeId);

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
				_mappingService.SavePOI(modal.Poi);

				return RedirectToAction("POIs");
			}

			return View(modal);
		}

		[HttpGet]
		public IActionResult GetMapData(MapSettingsInput input)
		{
			MapDataJson dataJson = new MapDataJson();

			var calls = _callsService.GetActiveCallsByDepartment(DepartmentId);
			var department = _departmentsService.GetDepartmentById(DepartmentId, false);
			var stations = _departmentGroupsService.GetAllStationGroupsForDepartment(DepartmentId);
			var lastUserActionlogs = _actionLogsService.GetActionLogsForDepartment(DepartmentId);
			var personnelNames = _departmentsService.GetAllPersonnelNamesForDepartment(DepartmentId);
			var unitStates = _unitsService.GetAllLatestStatusForUnitsByDepartmentId(DepartmentId);

			var userLocationPermission = _permissionsService.GetPermisionByDepartmentType(DepartmentId, PermissionTypes.CanSeePersonnelLocations);
			if (userLocationPermission != null)
			{
				var userGroup = _departmentGroupsService.GetGroupForUser(UserId, DepartmentId);
				int? groupId = null;

				if (userGroup != null)
					groupId = userGroup.DepartmentGroupId;

				var roles = _personnelRolesService.GetRolesForUser(UserId, DepartmentId);
				var allowedUsers = _permissionsService.GetAllowedUsers(userLocationPermission, DepartmentId, groupId, ClaimsAuthorizationHelper.IsUserDepartmentAdmin(), ClaimsAuthorizationHelper.IsUserDepartmentAdmin(), roles);

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
					MapMakerInfo info = new MapMakerInfo();
					info.ImagePath = "Station";
					info.Title = station.Name;
					info.InfoWindowContent = station.Name;

					if (station.Address != null)
					{
						string coordinates =
							_geoLocationProvider.GetLatLonFromAddress(string.Format("{0} {1} {2} {3}", station.Address.Address1,
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
			}

			if (input.ShowCalls)
			{
				foreach (var call in calls)
				{
					MapMakerInfo info = new MapMakerInfo();
					info.ImagePath = "Call";
					info.Title = call.Name;
					info.InfoWindowContent = call.NatureOfCall;

					if (!String.IsNullOrEmpty(call.GeoLocationData))
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
						string coordinates = _geoLocationProvider.GetLatLonFromAddress(call.Address);
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
				var poiTypes = _mappingService.GetPOITypesForDepartment(DepartmentId);

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
		public IActionResult GetTypesMapData(int poiTypeId)
		{
			MapDataJson dataJson = new MapDataJson();

			var poiType = _mappingService.GetTypeById(poiTypeId);

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
		public IActionResult GetPoisForType(int poiTypeId)
		{
			var poisJson = new List<PoiJson>();

			var poiType = _mappingService.GetTypeById(poiTypeId);

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
	}
}