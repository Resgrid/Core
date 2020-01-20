using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Reports.Units;
using Resgrid.Web.Areas.User.Models.Units;
using Resgrid.Web.Helpers;
using Microsoft.AspNetCore.Authorization;
using System.Text;

namespace Resgrid.Web.Areas.User.Controllers
{
	[Area("User")]
	public class UnitsController : SecureBaseController
	{
		#region Private Members and Constructors
		private readonly IDepartmentsService _departmentsService;
		private readonly IUsersService _usersService;
		private readonly IUnitsService _unitsService;
		private readonly Model.Services.IAuthorizationService _authorizationService;
		private readonly ILimitsService _limitsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly ICallsService _callsService;
		private readonly IEventAggregator _eventAggregator;
		private readonly ICustomStateService _customStateService;
		private readonly IGeoService _geoService;

		public UnitsController(IDepartmentsService departmentsService, IUsersService usersService, IUnitsService unitsService, Model.Services.IAuthorizationService authorizationService,
			ILimitsService limitsService, IDepartmentGroupsService departmentGroupsService, ICallsService callsService, IEventAggregator eventAggregator, ICustomStateService customStateService,
			IGeoService geoService)
		{
			_departmentsService = departmentsService;
			_usersService = usersService;
			_unitsService = unitsService;
			_authorizationService = authorizationService;
			_limitsService = limitsService;
			_departmentGroupsService = departmentGroupsService;
			_callsService = callsService;
			_eventAggregator = eventAggregator;
			_customStateService = customStateService;
			_geoService = geoService;
		}
		#endregion Private Members and Constructors

		[Authorize(Policy = ResgridResources.Unit_View)]
		public IActionResult Index()
		{
			var model = new UnitsIndexView();
			model.Department = _departmentsService.GetDepartmentById(DepartmentId);
			model.CanUserAddUnit = _limitsService.CanDepartentAddNewUnit(DepartmentId);
			model.Groups = _departmentGroupsService.GetAllGroupsForDepartment(DepartmentId);
			model.Units = _unitsService.GetUnitsForDepartment(DepartmentId);
			model.States = _unitsService.GetAllLatestStatusForUnitsByDepartmentId(DepartmentId);
			model.UnitStatuses = _customStateService.GetAllActiveUnitStatesForDepartment(DepartmentId);
			model.UnitCustomStates = new Dictionary<int, CustomState>();

			foreach (var unit in model.Units)
			{
				var type = _unitsService.GetUnitTypeByName(DepartmentId, unit.Type);
				if (type != null && type.CustomStatesId.HasValue)
				{
					var customStates = _customStateService.GetCustomSateById(type.CustomStatesId.Value);

					if (customStates != null)
					{
						model.UnitCustomStates.Add(unit.UnitId, customStates);
					}
				}
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_Create)]
		public IActionResult NewUnit()
		{
			var model = new NewUnitView();
			model.Unit = new Unit();
			model.Types = _unitsService.GetUnitTypesForDepartment(DepartmentId);

			var states = new List<CustomState>();
			states.Add(new CustomState
			{
				Name = "Standard Actions"
			});
			states.AddRange(_customStateService.GetAllActiveUnitStatesForDepartment(DepartmentId));
			model.States = states;

			var groups = new List<DepartmentGroup>();
			groups.Add(new DepartmentGroup
			{
				Name = "No Station"
			});
			groups.AddRange(_departmentGroupsService.GetAllStationGroupsForDepartment(DepartmentId));
			model.Stations = groups;

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Unit_Create)]
		public IActionResult NewUnit(NewUnitView model, IFormCollection form)
		{
			model.Types = _unitsService.GetUnitTypesForDepartment(DepartmentId);
			model.Stations = _departmentGroupsService.GetAllStationGroupsForDepartment(DepartmentId);

			if (_unitsService.GetUnitByNameDepartmentId(DepartmentId, model.Unit.Name) != null)
				ModelState.AddModelError("Name", "Unit with that name already exists.");

			var unitRoleNames = (from object key in form.Keys where key.ToString().StartsWith("unitRole_") select form[key.ToString()]).ToList();

			if (ModelState.IsValid)
			{
				model.Unit.DepartmentId = DepartmentId;

				if (model.Unit.StationGroupId.HasValue && model.Unit.StationGroupId.Value == 0)
					model.Unit.StationGroupId = null;

				model.Unit = _unitsService.SaveUnit(model.Unit);

				var roles = new List<UnitRole>();
				if (unitRoleNames.Count > 0)
				{
					foreach (var roleName in unitRoleNames)
					{
						var role = new UnitRole();
						role.Name = roleName;
						role.UnitId = model.Unit.UnitId;

						roles.Add(role);
					}
				}

				if (roles.Count > 0)
					_unitsService.SetRolesForUnit(model.Unit.UnitId, roles);

				var auditEvent = new AuditEvent();
				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.Type = AuditLogTypes.UnitAdded;
				auditEvent.After = model.Unit.CloneJson();
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				_eventAggregator.SendMessage<UnitAddedEvent>(new UnitAddedEvent() { DepartmentId = DepartmentId, Unit = model.Unit });

				return RedirectToAction("Index");
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_Update)]
		public IActionResult EditUnit(int unitId)
		{
			var model = new NewUnitView();
			model.Unit = _unitsService.GetUnitById(unitId);

			if (!_authorizationService.CanUserModifyUnit(UserId, unitId))
				Unauthorized();

			model.Types = _unitsService.GetUnitTypesForDepartment(DepartmentId);

			var groups = new List<DepartmentGroup>();
			groups.Add(new DepartmentGroup
			{
				Name = "No Station"
			});
			groups.AddRange(_departmentGroupsService.GetAllStationGroupsForDepartment(DepartmentId));
			model.Stations = groups;

			model.UnitRoles = _unitsService.GetRolesForUnit(unitId);

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Unit_Update)]
		public IActionResult EditUnit(NewUnitView model, IFormCollection form)
		{
			model.Types = _unitsService.GetUnitTypesForDepartment(DepartmentId);
			model.Stations = _departmentGroupsService.GetAllStationGroupsForDepartment(DepartmentId);

			if (!_authorizationService.CanUserModifyUnit(UserId, model.Unit.UnitId))
				Unauthorized();

			if (_unitsService.GetUnitByNameDepartmentId(DepartmentId, model.Unit.Name) != null && _unitsService.GetUnitByNameDepartmentId(DepartmentId, model.Unit.Name).UnitId != model.Unit.UnitId)
				ModelState.AddModelError("Name", "Unit with that name already exists.");

			var unitRoleNames = (from object key in form.Keys where key.ToString().StartsWith("unitRole_") select form[key.ToString()]).ToList();

			var unit = _unitsService.GetUnitById(model.Unit.UnitId);

			var auditEvent = new AuditEvent();
			auditEvent.DepartmentId = DepartmentId;
			auditEvent.UserId = UserId;
			auditEvent.Type = AuditLogTypes.UnitChanged;
			auditEvent.Before = unit.CloneJson();

			unit.Name = model.Unit.Name;
			unit.Type = model.Unit.Type;

			if (model.Unit.StationGroupId.HasValue && model.Unit.StationGroupId.Value != 0)
				unit.StationGroupId = model.Unit.StationGroupId;
			else
				unit.StationGroupId = null;

			if (ModelState.IsValid)
			{
				_unitsService.SaveUnit(unit);

				var roles = new List<UnitRole>();
				if (unitRoleNames.Count > 0)
				{
					foreach (var roleName in unitRoleNames)
					{
						var role = new UnitRole();
						role.Name = roleName;
						role.UnitId = unit.UnitId;

						roles.Add(role);
					}
				}

				if (roles.Count > 0)
					_unitsService.SetRolesForUnit(unit.UnitId, roles);
				else
					_unitsService.ClearRolesForUnit(unit.UnitId);

				auditEvent.After = unit.CloneJson();
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				return RedirectToAction("Index");
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public IActionResult SetUnitState(int unitId, int stateType)
		{
			if (!_authorizationService.CanUserViewUnit(UserId, unitId))
				Unauthorized();

			_unitsService.SetUnitState(unitId, stateType);

			return RedirectToAction("Index");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public IActionResult SetUnitStateWithDest(int unitId, int stateType, int type, int destination)
		{
			if (!_authorizationService.CanUserViewUnit(UserId, unitId))
				Unauthorized();

			var state = new UnitState();
			state.UnitId = unitId;
			state.LocalTimestamp = DateTime.UtcNow;
			state.State = stateType;
			state.Timestamp = DateTime.UtcNow;
			state.DestinationId = destination;

			try
			{
				var savedState = _unitsService.SetUnitState(state, DepartmentId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}

			return RedirectToAction("Index");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public IActionResult SetUnitStateForMultiple(string unitIds, int stateType)
		{
			if (!String.IsNullOrWhiteSpace(unitIds) && unitIds.Split(char.Parse("|")).Any())
			{
				foreach (var unitId in unitIds.Split(char.Parse("|")))
				{
					var unit = _unitsService.GetUnitById(int.Parse(unitId));

					if (!_authorizationService.CanUserViewUnit(UserId, unit.UnitId))
						Unauthorized();

					_unitsService.SetUnitState(unit.UnitId, stateType);
				}
			}

			return RedirectToAction("Index");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public IActionResult SetUnitStateWithDestForMultiple(string unitIds, int stateType, int type, int destination)
		{
			if (!String.IsNullOrWhiteSpace(unitIds) && unitIds.Split(char.Parse("|")).Any())
			{
				foreach (var unitId in unitIds.Split(char.Parse("|")))
				{
					var unit = _unitsService.GetUnitById(int.Parse(unitId));

					if (!_authorizationService.CanUserViewUnit(UserId, unit.UnitId))
						Unauthorized();

					var state = new UnitState();
					state.UnitId = unit.UnitId;
					state.LocalTimestamp = DateTime.UtcNow;
					state.State = stateType;
					state.Timestamp = DateTime.UtcNow;
					state.DestinationId = destination;

					try
					{
						var savedState = _unitsService.SetUnitState(state, DepartmentId);
					}
					catch (Exception ex)
					{
						Logging.LogException(ex);
					}
				}
			}

			return RedirectToAction("Index");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_Delete)]
		public IActionResult DeleteUnit(int unitId)
		{
			if (!_authorizationService.CanUserModifyUnit(UserId, unitId))
				Unauthorized();

			var unit = _unitsService.GetUnitById(unitId);

			var auditEvent = new AuditEvent();
			auditEvent.DepartmentId = DepartmentId;
			auditEvent.UserId = UserId;
			auditEvent.Type = AuditLogTypes.UnitRemoved;
			auditEvent.Before = unit.CloneJson();
			_eventAggregator.SendMessage<AuditEvent>(auditEvent);

			_unitsService.DeleteUnit(unitId);

			return RedirectToAction("Index");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.UnitLog_Create)]
		public IActionResult AddLog(int unitId)
		{
			var model = new AddLogView();
			var unit = _unitsService.GetUnitById(unitId);

			if (unit == null)
				Unauthorized();

			if (!_authorizationService.CanUserViewUnit(UserId, unitId))
				Unauthorized();

			model.Log = new UnitLog();
			model.Log.Timestamp = DateTime.UtcNow;
			model.Log.UnitId = unit.UnitId;
			model.UnitName = unit.Name;

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.UnitLog_Create)]
		public IActionResult AddLog(AddLogView model)
		{
			if (!_authorizationService.CanUserViewUnit(UserId, model.Log.UnitId))
				Unauthorized();

			if (ModelState.IsValid)
			{
				model.Log.Narrative = System.Net.WebUtility.HtmlDecode(model.Log.Narrative);
				_unitsService.SaveUnitLog(model.Log);

				return RedirectToAction("Index");
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.UnitLog_View)]
		public IActionResult ViewLogs(int unitId)
		{
			if (!_authorizationService.CanUserViewUnit(UserId, unitId))
				Unauthorized();

			var model = new ViewLogsView();
			model.Unit = _unitsService.GetUnitById(unitId);
			model.Department = _departmentsService.GetDepartmentById(DepartmentId, false);

			if (model.Unit == null)
				Unauthorized();

			model.Logs = _unitsService.GetLogsForUnit(model.Unit.UnitId);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public IActionResult ViewEvents(int unitId)
		{
			if (!_authorizationService.CanUserViewUnit(UserId, unitId))
				Unauthorized();

			var model = new ViewLogsView();
			model.Unit = _unitsService.GetUnitById(unitId);

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Unit_Update)]
		public IActionResult GenerateReport(IFormCollection form)
		{
			var eventIds = new List<int>();
			foreach (var key in form.Keys)
			{
				if (key.ToString().StartsWith("selectEvent_"))
				{
					var eventId = int.Parse(key.ToString().Replace("selectEvent_", ""));
					eventIds.Add(eventId);
				}
			}

			var model = new UnitEventsReportView();
			model.Rows = new List<UnitEventJson>();
			model.Department = _departmentsService.GetDepartmentById(DepartmentId, false);

			foreach (var eventId in eventIds)
			{
				var eventJson = new UnitEventJson();
				var eventRecord = _unitsService.GetUnitStateById(eventId);

				model.RunOn = DateTime.UtcNow.TimeConverter(model.Department);

				eventJson.UnitName = eventRecord.Unit.Name;
				eventJson.State = StringHelpers.GetDescription(((UnitStateTypes)eventRecord.State));
				eventJson.Timestamp = eventRecord.Timestamp.TimeConverterToString(model.Department).ToString();
				eventJson.Note = eventRecord.Note;

				if (((UnitStateTypes)eventRecord.State) == UnitStateTypes.Enroute)
				{
					if (eventRecord.DestinationId.HasValue)
					{
						var station = _departmentGroupsService.GetGroupById(eventRecord.DestinationId.Value, false);

						if (station != null)
							eventJson.DestinationName = station.Name;
						else
							eventJson.DestinationName = "Station";
					}
					else
					{
						eventJson.DestinationName = "Station";
					}
				}
				else if (((UnitStateTypes)eventRecord.State) == UnitStateTypes.Responding || ((UnitStateTypes)eventRecord.State) == UnitStateTypes.Committed
					|| ((UnitStateTypes)eventRecord.State) == UnitStateTypes.OnScene || ((UnitStateTypes)eventRecord.State) == UnitStateTypes.Staging
					 || ((UnitStateTypes)eventRecord.State) == UnitStateTypes.Released || ((UnitStateTypes)eventRecord.State) == UnitStateTypes.Cancelled)
				{
					if (eventRecord.DestinationId.HasValue)
					{
						var call = _callsService.GetCallById(eventRecord.DestinationId.Value, false);

						if (call != null)
							eventJson.DestinationName = call.Name;
						else
							eventJson.DestinationName = "Scene";
					}
				}

				if (eventRecord.LocalTimestamp.HasValue)
					eventJson.LocalTimestamp = eventRecord.LocalTimestamp.Value.ToString();

				if (eventRecord.Latitude.HasValue)
					eventJson.Latitude = eventRecord.Latitude.Value.ToString();

				if (eventRecord.Longitude.HasValue)
					eventJson.Longitude = eventRecord.Longitude.Value.ToString();

				model.Rows.Add(eventJson);
			}

			return View("~/Areas/User/Views/Reports/UnitEventsReport.cshtml", model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Unit_Update)]
		public IActionResult ClearAllUnitEvents(ViewLogsView model)
		{
			if (model.ConfirmClearAll)
				_unitsService.DeleteStatesForUnit(model.Unit.UnitId);

			return RedirectToAction("ViewEvents", new { unitId = model.Unit.UnitId });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public IActionResult GetUnitEvents(int unitId)
		{
			var unitEvents = new List<UnitEventJson>();

			if (!_authorizationService.CanUserViewUnit(UserId, unitId))
				Unauthorized();

			var department = _departmentsService.GetDepartmentById(DepartmentId, false);
			var events = _unitsService.GetAllStatesForUnit(unitId);

			foreach (var e in events)
			{
				var unitEvent = new UnitEventJson();
				unitEvent.EventId = e.UnitStateId;
				unitEvent.UnitId = e.UnitId;
				unitEvent.UnitName = e.Unit.Name;
				unitEvent.State = StringHelpers.GetDescription(((UnitStateTypes)e.State));
				unitEvent.Timestamp = e.Timestamp.TimeConverterToString(department);
				unitEvent.Note = e.Note;

				if (((UnitStateTypes)e.State) == UnitStateTypes.Enroute)
				{
					if (e.DestinationId.HasValue)
					{
						var station = _departmentGroupsService.GetGroupById(e.DestinationId.Value, false);

						if (station != null)
							unitEvent.DestinationName = station.Name;
						else
							unitEvent.DestinationName = "Station";
					}
					else
					{
						unitEvent.DestinationName = "Station";
					}
				}
				else if (((UnitStateTypes)e.State) == UnitStateTypes.Responding || ((UnitStateTypes)e.State) == UnitStateTypes.Committed
						|| ((UnitStateTypes)e.State) == UnitStateTypes.OnScene || ((UnitStateTypes)e.State) == UnitStateTypes.Staging
						 || ((UnitStateTypes)e.State) == UnitStateTypes.Released || ((UnitStateTypes)e.State) == UnitStateTypes.Cancelled)
				{
					if (e.DestinationId.HasValue)
					{
						var call = _callsService.GetCallById(e.DestinationId.Value, false);

						if (call != null)
							unitEvent.DestinationName = call.Name;
						else
							unitEvent.DestinationName = "Scene";
					}
				}

				if (e.LocalTimestamp.HasValue)
					unitEvent.LocalTimestamp = e.LocalTimestamp.Value.ToString();

				if (e.Latitude.HasValue)
					unitEvent.Latitude = e.Latitude.Value.ToString();

				if (e.Longitude.HasValue)
					unitEvent.Longitude = e.Longitude.Value.ToString();

				if (e.Accuracy.HasValue)
					unitEvent.Accuracy = e.Accuracy.Value.ToString();

				if (e.Altitude.HasValue)
					unitEvent.Altitude = e.Altitude.Value.ToString();

				if (e.AltitudeAccuracy.HasValue)
					unitEvent.AltitudeAccuracy = e.AltitudeAccuracy.Value.ToString();

				if (e.Speed.HasValue)
					unitEvent.Speed = e.Speed.Value.ToString();

				if (e.Heading.HasValue)
					unitEvent.Heading = e.Heading.Value.ToString();

				unitEvents.Add(unitEvent);
			}

			return Json(unitEvents);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public PartialViewResult SmallUnitsGrid()
		{
			return PartialView("_SmallUnitsGridPartial");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]

		public IActionResult GetUnits()
		{
			var units = new List<UnitJson>();
			var savedUnits = _unitsService.GetUnitsForDepartment(DepartmentId);

			foreach (var unit in savedUnits)
			{
				var unitJson = new UnitJson();
				unitJson.UnitId = unit.UnitId;
				unitJson.Name = unit.Name;
				unitJson.Type = unit.Type;

				if (unit.StationGroup != null)
					unitJson.Station = unit.StationGroup.Name;

				units.Add(unitJson);
			}

			return Json(units);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]

		public IActionResult GetUnitsForGroup(int groupId)
		{
			var units = new List<UnitJson>();
			var group = _departmentGroupsService.GetGroupById(groupId);

			if (group == null || group.DepartmentId != DepartmentId)
				Unauthorized();

			var savedUnits = _unitsService.GetAllUnitsForGroup(groupId);

			foreach (var unit in savedUnits)
			{
				var unitJson = new UnitJson();
				unitJson.UnitId = unit.UnitId;
				unitJson.Name = unit.Name;
				unitJson.Type = unit.Type;

				if (unit.StationGroup != null)
					unitJson.Station = unit.StationGroup.Name;

				units.Add(unitJson);
			}

			return Json(units);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public IActionResult GetUnitTypes()
		{
			var unitTypes = new List<UnitTypeJson>();
			var types = _unitsService.GetUnitTypesForDepartment(DepartmentId);

			foreach (var type in types)
			{
				var unitType = new UnitTypeJson();
				unitType.Id = type.UnitTypeId;
				unitType.Name = type.Type;

				unitTypes.Add(unitType);
			}

			return Json(unitTypes);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]

		public IActionResult GetUnitsList()
		{
			List<UnitForListJson> unitsJson = new List<UnitForListJson>();

			var units = _unitsService.GetUnitsForDepartment(DepartmentId);
			var states = _unitsService.GetAllLatestStatusForUnitsByDepartmentId(DepartmentId);
			var department = _departmentsService.GetDepartmentById(DepartmentId, false);

			foreach (var unit in units)
			{
				var unitJson = new UnitForListJson();
				unitJson.Name = unit.Name;
				unitJson.Type = unit.Type;
				unitJson.UnitId = unit.UnitId;

				if (unit.StationGroup != null)
					unitJson.Station = unit.StationGroup.Name;

				var state = states.FirstOrDefault(x => x.UnitId == unit.UnitId);

				if (state != null)
				{
					var customState = CustomStatesHelper.GetCustomUnitState(state);

					unitJson.StateId = state.State;
					unitJson.State = customState.ButtonText;
					unitJson.StateColor = customState.ButtonColor;
					unitJson.TextColor = customState.TextColor;
					unitJson.Timestamp = state.Timestamp.TimeConverterToString(department);
				}

				unitsJson.Add(unitJson);
			}

			return Json(unitsJson);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]

		public IActionResult GetUnitsForCallGrid(string callLat, string callLong)
		{
			List<UnitForListJson> unitsJson = new List<UnitForListJson>();

			var units = _unitsService.GetUnitsForDepartment(DepartmentId);
			var states = _unitsService.GetAllLatestStatusForUnitsByDepartmentId(DepartmentId);
			var department = _departmentsService.GetDepartmentById(DepartmentId, false);

			foreach (var unit in units)
			{
				var unitJson = new UnitForListJson();
				unitJson.Name = unit.Name;
				unitJson.Type = unit.Type;
				unitJson.UnitId = unit.UnitId;

				if (unit.StationGroup != null)
					unitJson.Station = unit.StationGroup.Name;

				var state = states.FirstOrDefault(x => x.UnitId == unit.UnitId);

				if (state != null)
				{
					var customState = CustomStatesHelper.GetCustomUnitState(state);

					unitJson.StateId = state.State;
					unitJson.State = customState.ButtonText;
					unitJson.StateColor = customState.ButtonColor;
					unitJson.TextColor = customState.TextColor;
					unitJson.Timestamp = state.Timestamp.TimeConverterToString(department);


					if (String.IsNullOrWhiteSpace(callLat) || String.IsNullOrWhiteSpace(callLong))
						unitJson.Eta = "N/A";
					else
					{
						var location = _unitsService.GetLatestUnitLocation(state.UnitId, state.Timestamp);

						if (location != null)
						{
							var eta = _geoService.GetEtaInSeconds($"{location.Latitude},{location.Longitude}", String.Format("{0},{1}", callLat, callLong));

							if (eta > 0)
								unitJson.Eta = $"{Math.Round(eta / 60, MidpointRounding.AwayFromZero)}m";
							else
								unitJson.Eta = "N/A";
						}
						else if (!String.IsNullOrWhiteSpace(state.GeoLocationData))
						{
							var eta = _geoService.GetEtaInSeconds(state.GeoLocationData, String.Format("{0},{1}", callLat, callLong));

							if (eta > 0)
								unitJson.Eta = $"{Math.Round(eta / 60, MidpointRounding.AwayFromZero)}m";
							else
								unitJson.Eta = "N/A";
						}
						else
							unitJson.Eta = "N/A";
					}
				}

				unitsJson.Add(unitJson);
			}

			return Json(unitsJson);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public IActionResult GetUnitOptionsDropdown(int unitId)
		{
			string buttonHtml = string.Empty;


			var unit = _unitsService.GetUnitById(unitId);
			var type = _unitsService.GetUnitTypeByName(DepartmentId, unit.Type);

			if (type != null && type.CustomStatesId.HasValue)
			{
				var customStates = _customStateService.GetCustomSateById(type.CustomStatesId.Value);
				var activeCalls = _callsService.GetActiveCallsByDepartment(DepartmentId);
				var stations = _departmentGroupsService.GetAllStationGroupsForDepartment(DepartmentId);

				StringBuilder sb = new StringBuilder();
				sb.Append($"<ul class='dropdown-menu multi-level unitStateList_{unitId}'>");

				var activeDetails = customStates.GetActiveDetails();

				foreach (var state in activeDetails.OrderBy(x => x.Order))
				{
					if (state.DetailType == (int)CustomStateDetailTypes.None)
					{
						sb.Append("<li><a style='color:" + state.ButtonColor + ";' href='/User/Units/SetUnitState?unitId=" + unitId + "&stateType=" + state.CustomStateDetailId + "'>" + state.ButtonText + "</a></li>");
					}
					else if (state.DetailType == (int)CustomStateDetailTypes.Calls)
					{
						sb.Append($"<li class='dropdown-submenu'><a style='color:{state.ButtonColor};' tabindex='-1' href='#'>{state.ButtonText}</a>");
						sb.Append($"<ul class='dropdown-menu unitStateList_{unitId}'>");
						sb.Append($"<li><a href='/User/Units/SetUnitState?unitId={unitId}&stateType={state.CustomStateDetailId}'>{state.ButtonText}</a></li>");
						sb.Append("<li class='divider'></li>");
						sb.Append("<li class='dropdown-header'>Calls</li>");

						foreach (var call in activeCalls)
						{
							sb.Append($"<li><a href='/User/Units/SetUnitStateWithDest?unitId={unitId}&stateType={state.CustomStateDetailId}&type=2&destination={call.CallId}'>{call.GetIdentifier()}:{call.Name}</a></li>");
						}

						sb.Append("</ul>");
					}
					else if (state.DetailType == (int)CustomStateDetailTypes.Stations)
					{
						sb.Append($"<li class='dropdown-submenu'><a style='color:{state.ButtonColor};' tabindex='-1' href='#'>{state.ButtonText}</a>");
						sb.Append($"<ul class='dropdown-menu unitStateList_{unitId}'>");
						sb.Append($"<li><a href='/User/Units/SetUnitState?unitId={unitId}&stateType={state.CustomStateDetailId}'>{state.ButtonText}</a></li>");
						sb.Append("<li class='divider'></li>");
						sb.Append("<li class='dropdown-header'>Stations</li>");

						foreach (var station in stations)
						{
							sb.Append("<li><a href='/User/Units/SetUnitStateWithDest?unitId=" + unitId + "&stateType=" + state.CustomStateDetailId + "&type=2&destination=" + station.DepartmentGroupId + "'>" + station.Name + "</a></li>");
						}

						sb.Append("</ul>");
					}
					else if (state.DetailType == (int)CustomStateDetailTypes.CallsAndStations)
					{
						sb.Append($"<li class='dropdown-submenu'><a style='color:{state.ButtonColor};' tabindex='-1' href='#'>{state.ButtonText}</a>");
						sb.Append($"<ul class='dropdown-menu unitStateList_{unitId}'>");
						sb.Append($"<li><a href='/User/Units/SetUnitState?unitId={unitId}&stateType={state.CustomStateDetailId}'>{state.ButtonText}</a></li>");
						sb.Append("<li class='divider'></li>");
						sb.Append("<li class='dropdown-header'>Calls</li>");

						foreach (var call in activeCalls)
						{
							sb.Append($"<li><a href='/User/Units/SetUnitStateWithDest?unitId={unitId}&stateType={state.CustomStateDetailId}&type=2&destination={call.CallId}'>{call.GetIdentifier()}:{call.Name}</a></li>");
						}

						sb.Append("<li class='dropdown-header'>Stations</li>");

						foreach (var station in stations)
						{
							sb.Append($"<li><a href='/User/Units/SetUnitStateWithDest?unitId={unitId}&stateType={state.CustomStateDetailId}&type=2&destination={station.DepartmentGroupId}'>{station.Name}</a></li>");
						}

						sb.Append("</ul>");
					}
				}
				sb.Append("</ul>");

				buttonHtml = sb.ToString();
			}

			if (String.IsNullOrWhiteSpace(buttonHtml))
			{
				StringBuilder sb = new StringBuilder();
				sb.Append($"<ul class='dropdown-menu unitStateList_{unitId}'>");
				sb.Append($"<li><a href='/User/Units/SetUnitState?unitId={unitId}&stateType=0'>Available</a></li>");
				sb.Append($"<li><a href='/User/Units/SetUnitState?unitId={unitId}&stateType=3'>Committed</a></li>");
				sb.Append($"<li><a href='/User/Units/SetUnitState?unitId={unitId}&stateType=1'>Delayed</a></li>");
				sb.Append($"<li><a href='/User/Units/SetUnitState?unitId={unitId}&stateType=4'>Out Of Service</a></li>");
				sb.Append($"<li><a href='/User/Units/SetUnitState?unitId={unitId}&stateType=2'>Unavailable</a></li>");
				sb.Append("</ul>");

				buttonHtml = sb.ToString();
			}

			return Content(buttonHtml);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public IActionResult GetUnitOptionsDropdownForStates(int stateId, string units)
		{
			string buttonHtml = string.Empty;

			if (stateId > 1)
			{
				var customStates = _customStateService.GetCustomSateById(stateId);
				var activeCalls = _callsService.GetActiveCallsByDepartment(DepartmentId);
				var stations = _departmentGroupsService.GetAllStationGroupsForDepartment(DepartmentId);

				StringBuilder sb = new StringBuilder();
				sb.Append($"<ul class='dropdown-menu multi-level unitStateList_{stateId}'>");
				var activeDetails = customStates.GetActiveDetails();

				if (activeDetails.Any())
				{
					foreach (var state in activeDetails.OrderBy(x => x.Order))
					{
						if (state.DetailType == (int)CustomStateDetailTypes.None)
						{
							sb.Append("<li><a style='color:" + state.ButtonColor + ";' href='/User/Units/SetUnitStateForMultiple?unitIds=" + units + "&stateType=" + state.CustomStateDetailId + "'>" + state.ButtonText + "</a></li>");
						}
						else if (state.DetailType == (int)CustomStateDetailTypes.Calls)
						{
							sb.Append($"<li class='dropdown-submenu'><a style='color:{state.ButtonColor};' tabindex='-1' href='#'>{state.ButtonText}</a>");
							sb.Append($"<ul class='dropdown-menu unitStateList_{stateId}'>");
							sb.Append($"<li><a href='/User/Units/SetUnitStateForMultiple?unitIds={units}&stateType={state.CustomStateDetailId}'>{state.ButtonText}</a></li>");
							sb.Append("<li class='divider'></li>");
							sb.Append("<li class='dropdown-header'>Calls</li>");

							foreach (var call in activeCalls)
							{
								sb.Append($"<li><a href='/User/Units/SetUnitStateWithDestForMultiple?unitIds={units}&stateType={state.CustomStateDetailId}&type=2&destination={call.CallId}'>{call.GetIdentifier()}:{call.Name}</a></li>");
							}

							sb.Append("</ul>");
						}
						else if (state.DetailType == (int)CustomStateDetailTypes.Stations)
						{
							sb.Append($"<li class='dropdown-submenu'><a style='color:{state.ButtonColor};' tabindex='-1' href='#'>{state.ButtonText}</a>");
							sb.Append($"<ul class='dropdown-menu unitStateList_{stateId}'>");
							sb.Append($"<li><a href='/User/Units/SetUnitStateForMultiple?unitIds={units}&stateType={state.CustomStateDetailId}'>{state.ButtonText}</a></li>");
							sb.Append("<li class='divider'></li>");
							sb.Append("<li class='dropdown-header'>Stations</li>");

							foreach (var station in stations)
							{
								sb.Append("<li><a href='/User/Units/SetUnitStateWithDestForMultiple?unitIds=" + units + "&stateType=" + state.CustomStateDetailId + "&type=2&destination=" + station.DepartmentGroupId + "'>" + station.Name + "</a></li>");
							}

							sb.Append("</ul>");
						}
						else if (state.DetailType == (int)CustomStateDetailTypes.CallsAndStations)
						{
							sb.Append($"<li class='dropdown-submenu'><a style='color:{state.ButtonColor};' tabindex='-1' href='#'>{state.ButtonText}</a>");
							sb.Append($"<ul class='dropdown-menu unitStateList_{stateId}'>");
							sb.Append($"<li><a href='/User/Units/SetUnitStateForMultiple?unitIds={units}&stateType={state.CustomStateDetailId}'>{state.ButtonText}</a></li>");
							sb.Append("<li class='divider'></li>");
							sb.Append("<li class='dropdown-header'>Calls</li>");

							foreach (var call in activeCalls)
							{
								sb.Append($"<li><a href='/User/Units/SetUnitStateWithDestForMultiple?unitIds={units}&stateType={state.CustomStateDetailId}&type=2&destination={call.CallId}'>{call.GetIdentifier()}:{call.Name}</a></li>");
							}

							sb.Append("<li class='dropdown-header'>Stations</li>");

							foreach (var station in stations)
							{
								sb.Append($"<li><a href='/User/Units/SetUnitStateWithDestForMultiple?unitIds={units}&stateType={state.CustomStateDetailId}&type=2&destination={station.DepartmentGroupId}'>{station.Name}</a></li>");
							}

							sb.Append("</ul>");
						}
					}
					sb.Append("</ul>");

					buttonHtml = sb.ToString();
				}
			}

			if (String.IsNullOrWhiteSpace(buttonHtml))
			{
				StringBuilder sb2 = new StringBuilder();
				sb2.Append($"<ul class='dropdown-menu unitStateList_{stateId}'>");
				sb2.Append($"<li><a href='/User/Units/SetUnitStateForMultiple?unitIds={units}&stateType=0'>Available</a></li>");
				sb2.Append($"<li><a href='/User/Units/SetUnitStateForMultiple?unitIds={units}&stateType=3'>Committed</a></li>");
				sb2.Append($"<li><a href='/User/Units/SetUnitStateForMultiple?unitIds={units}&stateType=1'>Delayed</a></li>");
				sb2.Append($"<li><a href='/User/Units/SetUnitStateForMultiple?unitIds={units}&stateType=4'>Out Of Service</a></li>");
				sb2.Append($"<li><a href='/User/Units/SetUnitStateForMultiple?unitIds={units}&stateType=2'>Unavailable</a></li>");
				sb2.Append("</ul>");

				buttonHtml = sb2.ToString();
			}

			return Content(buttonHtml);
		}
	}
}
