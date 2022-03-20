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
			IGeoLocationProvider geoLocationProvider
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
			//var units = await _unitsService.GetUnitsForDepartmentAsync(DepartmentId);
			var unitStates = await _unitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync(DepartmentId);

			//var personnelViewModels = (await GetPersonnelStatuses()).Value;

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


			foreach (var station in stations)
			{
				MapMakerInfoData info = new MapMakerInfoData();
				info.Id = $"s{station.DepartmentGroupId}";
				info.ImagePath = "Station";
				info.Title = station.Name;
				info.InfoWindowContent = station.Name;

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

			foreach (var call in calls)
			{
				MapMakerInfoData info = new MapMakerInfoData();
				info.ImagePath = "Call";
				info.Id = $"c{call.CallId}";
				info.Title = call.Name;
				info.InfoWindowContent = call.NatureOfCall;

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

			foreach (var unit in unitStates)
			{
				if (unit.Latitude.HasValue && unit.Latitude.Value != 0 && unit.Longitude.HasValue &&
					unit.Longitude.Value != 0)
				{
					MapMakerInfoData info = new MapMakerInfoData();
					info.ImagePath = "Engine_Responding";
					info.Id = $"u{unit.UnitId}";
					info.Title = unit.Unit.Name;
					info.InfoWindowContent = "";
					info.Latitude = double.Parse(unit.Latitude.Value.ToString());
					info.Longitude = double.Parse(unit.Longitude.Value.ToString());

					result.Data.MapMakerInfos.Add(info);
				}
			}

			//foreach (var person in personnelViewModels)
			//{
			//	if (person.Latitude.HasValue && person.Latitude.Value != 0 && person.Longitude.HasValue &&
			//		person.Longitude.Value != 0)
			//	{
			//		MapMakerInfoData info = new MapMakerInfoData();

			//		if (person.StatusValue <= 25)
			//		{
			//			if (person.StatusValue == 5)
			//				info.ImagePath = "Person_RespondingStation";
			//			else if (person.StatusValue == 6)
			//				info.ImagePath = "Person_RespondingCall";
			//			else if (person.StatusValue == 3)
			//				info.ImagePath = "Person_OnScene";
			//			else
			//				info.ImagePath = "Person_RespondingCall";
			//		}
			//		else if (person.DestinationType > 0)
			//		{
			//			if (person.DestinationType == 1)
			//				info.ImagePath = "Person_RespondingStation";
			//			else if (person.DestinationType == 2)
			//				info.ImagePath = "Person_RespondingCall";
			//			else
			//				info.ImagePath = "Person_RespondingCall";
			//		}
			//		else
			//		{
			//			info.ImagePath = "Person_RespondingCall";
			//		}

			//		//info.Id = $"p{person.}";
			//		info.Title = person.Name;
			//		info.InfoWindowContent = "";
			//		info.Latitude = double.Parse(person.Latitude.Value.ToString());
			//		info.Longitude = double.Parse(person.Longitude.Value.ToString());

			//		result.Data.MapMakerInfos.Add(info);
			//	}
			//}

			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}
	}
}
