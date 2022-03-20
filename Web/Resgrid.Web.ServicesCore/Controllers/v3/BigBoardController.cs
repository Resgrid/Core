using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.Version3.Models.BigBoard;
using Resgrid.Web.Services.Controllers.Version3.Models.BigBoard.BigBoardX;
using Resgrid.Web.Services.Controllers.Version3.Models.Groups;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Helpers;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Calls to get information specific for displaying data on a dashboard or big board application. Data returned
	/// is formatted and optimized for these scenarios to be outputted directly into a web application.
	/// </summary>
	[Route("api/v{version:ApiVersion}/[controller]")]
	[Produces("application/json")]
	[ApiVersion("3.0")]
	[ApiExplorerSettings(GroupName = "v3")]
	public class BigBoardController : V3AuthenticatedApiControllerbase
	{
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

		public BigBoardController(
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

		[HttpGet("GetPersonnelStatuses")]
		public async Task<ActionResult<List<PersonnelViewModel>>> GetPersonnelStatuses()
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var stations = await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(DepartmentId);
			var calls = await _callsService.GetActiveCallsByDepartmentAsync(DepartmentId);
			var allUsers = await _departmentsService.GetAllUsersForDepartmentAsync(DepartmentId);
			var hideUnavailable = await _departmentSettingsService.GetBigBoardHideUnavailableDepartmentAsync(DepartmentId);
			//var lastUserActionlogs = await _actionLogsService.GetAllActionLogsForDepartmentAsync(DepartmentId);
			var lastUserActionlogs = await _actionLogsService.GetLastActionLogsForDepartmentAsync(DepartmentId);
			var departmentGroups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);

			var lastUserStates = await _userStateService.GetLatestStatesForDepartmentAsync(DepartmentId);
			var personnelNames = await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId);

			var names = new Dictionary<string, string>();

			var userStates = new List<UserState>();

			foreach (var u in allUsers)
			{
				var state = lastUserStates.FirstOrDefault(x => x.UserId == u.UserId);

				if (state != null)
					userStates.Add(state);
				else
					userStates.Add(await _userStateService.GetLastUserStateByUserIdAsync(u.UserId));

				var name = personnelNames.FirstOrDefault(x => x.UserId == u.UserId);
				if (name != null)
					names.Add(u.UserId, name.Name);
			}

			var personnelViewModels = new List<PersonnelViewModel>();



			var sortedUngroupedUsers = from u in allUsers
										   // let mu = Membership.GetUser(u.UserId)
									   let userGroup = departmentGroups.FirstOrDefault(x => x.Members.Any(y => y.UserId == u.UserId))
									   let groupName = userGroup == null ? "" : userGroup.Name
									   //let roles = _personnelRolesService.GetRolesForUserAsync(u.UserId, DepartmentId).Result
									   //let name = (ProfileBase.Create(mu.UserName, true)).GetPropertyValue("Name").ToString()
									   let name = names.ContainsKey(u.UserId) ? names[u.UserId] : "Unknown User"
									   let weight = lastUserActionlogs.Where(x => x.UserId == u.UserId).FirstOrDefault().GetWeightForAction()
									   orderby groupName, weight, name ascending
									   select new
									   {
										   Name = name,
										   User = u,
										   Group = userGroup,
										   Roles = new List<PersonnelRole>()
									   };

			foreach (var u in sortedUngroupedUsers)
			{
				//var mu = Membership.GetUser(u.User.UserId);
				var al = lastUserActionlogs.Where(x => x.UserId == u.User.UserId).FirstOrDefault();
				var us = userStates.Where(x => x.UserId == u.User.UserId).FirstOrDefault();

				// if setting is such, ignore unavailable users.
				if (hideUnavailable.HasValue && hideUnavailable.Value && us.State != (int)UserStateTypes.Unavailable)
					continue;

				u.Roles.AddRange(await _personnelRolesService.GetRolesForUserAsync(u.User.UserId, DepartmentId));

				string callNumber = "";
				if (al != null && al.ActionTypeId == (int)ActionTypes.RespondingToScene ||
						(al != null && al.DestinationType.HasValue && al.DestinationType.Value == 2))
				{
					if (al.DestinationId.HasValue)
					{
						var call = calls.FirstOrDefault(x => x.CallId == al.DestinationId.Value);

						if (call != null)
							callNumber = call.Number;
					}
				}
				var respondingToDepartment =
					stations.Where(s => al != null && s.DepartmentGroupId == al.DestinationId).FirstOrDefault();
				var personnelViewModel = await PersonnelViewModel.Create(u.Name, al, us, department, respondingToDepartment, u.Group,
					u.Roles, callNumber);

				personnelViewModels.Add(personnelViewModel);
			}

			return personnelViewModels;
		}

		[HttpGet("GetCalls")]
		public async Task<ActionResult<List<CallViewModel>>> GetCalls()
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var calls = await _callsService.GetActiveCallsByDepartmentAsync(DepartmentId);
			var usersNames = await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId);

			var callViewModels = new List<CallViewModel>();

			foreach (var call in calls)
			{

				string name = "";
				var personName = usersNames.FirstOrDefault(x => x.UserId == call.ReportingUserId);

				if (personName != null)
				{
					name = personName.Name;
				}
				else
				{
					name = "Unknown";
				}


				var callViewModel = new CallViewModel
				{
					Id = call.Number,
					Name = call.Name,
					Timestamp = call.LoggedOn.TimeConverter(department),
					LoggingUser = name,
					Priority = call.ToCallPriorityDisplayText(),
					PriorityCss = call.ToCallPriorityCss(),
					State = call.ToCallStateDisplayText(),
					StateCss = call.ToCallStateCss(),
					Address = call.Address
				};

				callViewModels.Add(callViewModel);
			}

			return callViewModels;
		}

		[HttpGet("GetUnitStatuses")]
		public async Task<ActionResult<List<UnitViewModel>>> GetUnitStatuses()
		{
			var units = await _unitsService.GetUnitsForDepartmentAsync(DepartmentId);
			var unitStates = await _unitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync(DepartmentId);
			var activeCalls = await _callsService.GetActiveCallsByDepartmentAsync(DepartmentId);
			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);

			var unitViewModels = new List<UnitViewModel>();

			var sortedUnits = from u in units
							  let station = u.StationGroup
							  let stationName = station == null ? "" : station.Name
							  orderby stationName, u.Name ascending
							  select new
							  {
								  Unit = u,
								  Station = station,
								  StationName = stationName
							  };

			foreach (var unit in sortedUnits)
			{
				var stateFound = unitStates.FirstOrDefault(x => x.UnitId == unit.Unit.UnitId);
				var state = "Unknown";
				var stateCss = "";
				var stateStyle = "";
				int? destinationId = 0;
				decimal? latitude = 0;
				decimal? longitude = 0;
				var destinationName = "";
				DateTime? timestamp = null;

				if (stateFound != null)
				{
					var customState = await CustomStatesHelper.GetCustomUnitState(stateFound);
					if (customState != null)
					{
						state = customState.ButtonText;
						stateCss = customState.ButtonColor;
						stateStyle = customState.ButtonColor;

						if (customState.DetailType == (int)CustomStateDetailTypes.Calls)
						{
							var call = activeCalls.FirstOrDefault(x => x.CallId == stateFound.DestinationId);
							if (call != null)
							{
								destinationName = call.Number;
							}
						}
						else if (customState.DetailType == (int)CustomStateDetailTypes.Stations)
						{
							var station = groups.FirstOrDefault(x => x.DepartmentGroupId == stateFound.DestinationId);
							if (station != null)
							{
								destinationName = station.Name;
							}
						}
						else if (customState.DetailType == (int)CustomStateDetailTypes.CallsAndStations)
						{
							// First try and get the station, as a station can get a call (based on Id) but the inverse is hard
							var station = groups.FirstOrDefault(x => x.DepartmentGroupId == stateFound.DestinationId);
							if (station != null)
							{
								destinationName = station.Name;
							}
							else
							{
								var call = activeCalls.FirstOrDefault(x => x.CallId == stateFound.DestinationId);
								if (call != null)
								{
									destinationName = call.Number;
								}
							}
						}
					}
					else
					{
						state = stateFound.ToStateDisplayText();
						stateCss = stateFound.ToStateCss();
					}

					destinationId = stateFound.DestinationId;
					latitude = stateFound.Latitude;
					longitude = stateFound.Longitude;
					timestamp = stateFound.Timestamp;
				}

				int groupId = 0;
				if (unit.Station != null)
					groupId = unit.Station.DepartmentGroupId;

				var latestUnitLocation = await _unitsService.GetLatestUnitLocationAsync(unit.Unit.UnitId, timestamp);
				if (latestUnitLocation != null)
				{
					latitude = latestUnitLocation.Latitude;
					longitude = latestUnitLocation.Longitude;
				}

				var unitViewModel = new UnitViewModel
				{
					UnitId = unit.Unit.UnitId,
					Name = unit.Unit.Name,
					Type = unit.Unit.Type,
					State = state,
					StateCss = stateCss,
					StateStyle = stateStyle,
					Timestamp = timestamp,
					DestinationId = destinationId,
					Latitude = latitude,
					Longitude = longitude,
					GroupId = groupId,
					GroupName = unit.StationName,
					DestinationName = destinationName
				};

				unitViewModels.Add(unitViewModel);
			}

			return unitViewModels;
		}

		[HttpGet("GetMap")]
		public async Task<ActionResult<BigBoardMapModel>> GetMap()
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var stations = await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(DepartmentId);

			var address = await _departmentSettingsService.GetBigBoardCenterAddressDepartmentAsync(DepartmentId);
			var gpsCoordinates = await _departmentSettingsService.GetBigBoardCenterGpsCoordinatesDepartmentAsync(DepartmentId);
			var calls = await _callsService.GetActiveCallsByDepartmentAsync(DepartmentId);
			var units = await _unitsService.GetUnitsForDepartmentAsync(DepartmentId);
			var unitStates = await _unitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync(DepartmentId);

			var personnelViewModels = (await GetPersonnelStatuses()).Value;

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

			var mapModel = new BigBoardMapModel
			{
				CenterLat = centerLat.Value,
				CenterLon = centerLon.Value,
				ZoomLevel = zoomLevel.HasValue ? zoomLevel.Value : 9,
			};

			foreach (var station in stations)
			{
				MapMakerInfo info = new MapMakerInfo();
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

						mapModel.MapMakerInfos.Add(info);
					}
				}
				else if (!String.IsNullOrWhiteSpace(station.Latitude) && !String.IsNullOrWhiteSpace(station.Longitude))
				{
					info.Latitude = double.Parse(station.Latitude);
					info.Longitude = double.Parse(station.Longitude);

					mapModel.MapMakerInfos.Add(info);
				}
			}

			foreach (var call in calls)
			{
				MapMakerInfo info = new MapMakerInfo();
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

						mapModel.MapMakerInfos.Add(info);
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

					mapModel.MapMakerInfos.Add(info);
				}
			}

			foreach (var unit in unitStates)
			{
				if (unit.Latitude.HasValue && unit.Latitude.Value != 0 && unit.Longitude.HasValue &&
					unit.Longitude.Value != 0)
				{
					MapMakerInfo info = new MapMakerInfo();
					info.ImagePath = "Engine_Responding";
					info.Id = $"u{unit.UnitId}";
					info.Title = unit.Unit.Name;
					info.InfoWindowContent = "";
					info.Latitude = double.Parse(unit.Latitude.Value.ToString());
					info.Longitude = double.Parse(unit.Longitude.Value.ToString());

					mapModel.MapMakerInfos.Add(info);
				}
			}

			foreach (var person in personnelViewModels)
			{
				if (person.Latitude.HasValue && person.Latitude.Value != 0 && person.Longitude.HasValue &&
					person.Longitude.Value != 0)
				{
					MapMakerInfo info = new MapMakerInfo();

					if (person.StatusValue <= 25)
					{
						if (person.StatusValue == 5)
							info.ImagePath = "Person_RespondingStation";
						else if (person.StatusValue == 6)
							info.ImagePath = "Person_RespondingCall";
						else if (person.StatusValue == 3)
							info.ImagePath = "Person_OnScene";
						else
							info.ImagePath = "Person_RespondingCall";
					}
					else if (person.DestinationType > 0)
					{
						if (person.DestinationType == 1)
							info.ImagePath = "Person_RespondingStation";
						else if (person.DestinationType == 2)
							info.ImagePath = "Person_RespondingCall";
						else
							info.ImagePath = "Person_RespondingCall";
					}
					else
					{
						info.ImagePath = "Person_RespondingCall";
					}

					//info.Id = $"p{person.}";
					info.Title = person.Name;
					info.InfoWindowContent = "";
					info.Latitude = double.Parse(person.Latitude.Value.ToString());
					info.Longitude = double.Parse(person.Longitude.Value.ToString());

					mapModel.MapMakerInfos.Add(info);
				}
			}

			return Ok(mapModel);
		}

		[HttpGet("GetWeather")]
		public async Task<ActionResult<WeatherModel>> GetWeather()
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var address = await _departmentSettingsService.GetBigBoardCenterAddressDepartmentAsync(DepartmentId);
			var gpsCoordinates = await _departmentSettingsService.GetBigBoardCenterGpsCoordinatesDepartmentAsync(DepartmentId);


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

			var mapModel = new WeatherModel
			{
				Latitude = centerLat.Value,
				Longitude = centerLon.Value,
				WeatherUnit = weatherUnits,
			};



			return Ok(mapModel);
		}

		[HttpGet("GetGroups")]
		public async Task<ActionResult<GroupsModel>> GetGroups()
		{
			var result = new GroupsModel();
			result.Groups = new List<GroupInfoResult>();
			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);

			foreach (var group in groups)
			{
				var newGroup = new GroupInfoResult();
				newGroup.Gid = group.DepartmentGroupId;
				newGroup.Nme = group.Name;
				newGroup.Typ = group.Type.GetValueOrDefault();

				result.Groups.Add(newGroup);
			}

			return Ok(result);
		}
	}
}
