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
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Providers;
using Resgrid.WebCore.Areas.User.Models.Units;

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
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IGeoLocationProvider _geoLocationProvider;

		public UnitsController(IDepartmentsService departmentsService, IUsersService usersService, IUnitsService unitsService, Model.Services.IAuthorizationService authorizationService,
			ILimitsService limitsService, IDepartmentGroupsService departmentGroupsService, ICallsService callsService, IEventAggregator eventAggregator, ICustomStateService customStateService,
			IGeoService geoService, IDepartmentSettingsService departmentSettingsService, IGeoLocationProvider geoLocationProvider)
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
			_departmentSettingsService = departmentSettingsService;
			_geoLocationProvider = geoLocationProvider;
		}
		#endregion Private Members and Constructors

		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<IActionResult> Index()
		{
			var model = new UnitsIndexView();
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			model.CanUserAddUnit = await _limitsService.CanDepartmentAddNewUnit(DepartmentId);
			model.Groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);
			model.Units = await _unitsService.GetUnitsForDepartmentAsync(DepartmentId);
			model.States = await _unitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync(DepartmentId);
			model.UnitStatuses = await _customStateService.GetAllActiveUnitStatesForDepartmentAsync(DepartmentId);
			model.UnitCustomStates = new Dictionary<int, CustomState>();

			foreach (var unit in model.Units)
			{
				var type = await _unitsService.GetUnitTypeByNameAsync(DepartmentId, unit.Type);
				if (type != null && type.CustomStatesId.HasValue)
				{
					var customStates = await _customStateService.GetCustomSateByIdAsync(type.CustomStatesId.Value);

					if (customStates != null)
					{
						model.UnitCustomStates.Add(unit.UnitId, customStates);
					}
				}
			}

			if (model.Department.IsUserAnAdmin(UserId))
				model.IsUserAdminOrGroupAdmin = true;
			else
			{
				var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);

				foreach (var group in groups)
				{
					if (group.IsUserGroupAdmin(UserId))
					{
						model.IsUserAdminOrGroupAdmin = true;
						break;
					}
				}
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<IActionResult> UnitStaffing()
		{
			var model = new UnitStaffingView();
			model.Units = await _unitsService.GetUnitsForDepartmentAsync(DepartmentId);
			model.Users = _usersService.GetUserGroupAndRolesByDepartmentId(DepartmentId, false, false, false);
			model.ActiveRoles = await _unitsService.GetAllActiveRolesForUnitsByDepartmentIdAsync(DepartmentId);

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<IActionResult> UnitStaffing(UnitStaffingView model, IFormCollection form, CancellationToken cancellationToken)
		{
			model.Units = await _unitsService.GetUnitsForDepartmentAsync(DepartmentId);
			model.Users = _usersService.GetUserGroupAndRolesByDepartmentId(DepartmentId, false, false, false);
			model.ActiveRoles = await _unitsService.GetAllActiveRolesForUnitsByDepartmentIdAsync(DepartmentId);


			if (ModelState.IsValid)
			{
				List<int> unitRoles = (from object key in form.Keys
									   where key.ToString().StartsWith("Role_")
									   select int.Parse(key.ToString().Replace("Role_", ""))).ToList();

				foreach (var unit in model.Units)
				{
					await _unitsService.DeleteActiveRolesForUnitAsync(unit.UnitId, cancellationToken);
				}

				foreach (var unitRole in unitRoles)
				{
					var unitRoleStaffingUserId = form[$"Role_{unitRole}"];

					if (!String.IsNullOrWhiteSpace(unitRoleStaffingUserId))
					{
						var role = await _unitsService.GetRoleByIdAsync(unitRole);

						if (role != null)
						{
							UnitActiveRole activeRole = new UnitActiveRole();
							activeRole.UnitId = role.UnitId;
							activeRole.Role = role.Name;
							activeRole.UserId = unitRoleStaffingUserId;
							activeRole.DepartmentId = DepartmentId;
							activeRole.UpdatedBy = UserId;
							activeRole.UpdatedOn = DateTime.UtcNow;

							await _unitsService.SaveActiveRoleAsync(activeRole);
						}
					}
				}

				return RedirectToAction("Index");
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_Create)]
		public async Task<IActionResult> NewUnit()
		{
			var model = new NewUnitView();
			model.Unit = new Unit();
			model.Types = await _unitsService.GetUnitTypesForDepartmentAsync(DepartmentId);

			var states = new List<CustomState>();
			states.Add(new CustomState
			{
				Name = "Standard Actions"
			});
			states.AddRange(await _customStateService.GetAllActiveUnitStatesForDepartmentAsync(DepartmentId));
			model.States = states;

			var groups = new List<DepartmentGroup>();
			groups.Add(new DepartmentGroup
			{
				Name = "No Station"
			});
			groups.AddRange(await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(DepartmentId));
			model.Stations = groups;

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Unit_Create)]
		public async Task<IActionResult> NewUnit(NewUnitView model, IFormCollection form, CancellationToken cancellationToken)
		{
			model.Types = await _unitsService.GetUnitTypesForDepartmentAsync(DepartmentId);
			model.Stations = await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(DepartmentId);

			if ((await _unitsService.GetUnitByNameDepartmentIdAsync(DepartmentId, model.Unit.Name)) != null)
				ModelState.AddModelError("Name", "Unit with that name already exists.");

			var unitRoleNames = (from object key in form.Keys where key.ToString().StartsWith("unitRole_") select form[key.ToString()]).ToList();

			var query = unitRoleNames.GroupBy(x => x)
			  .Where(g => g.Count() > 1)
			  .Select(y => new { Element = y.Key, Counter = y.Count() })
			  .ToList();

			if (query.Any(x => x.Counter > 1))
				ModelState.AddModelError("", "Role Names need to be Unique, please ensure each name is not repeated.");

			//if (!model.Unit.StationGroupId.HasValue || model.Unit.StationGroupId.Value == 0)
			//	ModelState.AddModelError("", "You must select a Station Group to assign this unit to.");

			if (ModelState.IsValid)
			{
				model.Unit.DepartmentId = DepartmentId;

				if (model.Unit.StationGroupId.HasValue && model.Unit.StationGroupId.Value == 0)
					model.Unit.StationGroupId = null;

				model.Unit = await _unitsService.SaveUnitAsync(model.Unit, cancellationToken);

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
					await _unitsService.SetRolesForUnitAsync(model.Unit.UnitId, roles, cancellationToken);

				var auditEvent = new AuditEvent();
				auditEvent.DepartmentId = DepartmentId;
				auditEvent.UserId = UserId;
				auditEvent.Type = AuditLogTypes.UnitAdded;
				auditEvent.After = model.Unit.CloneJsonToString();
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				_eventAggregator.SendMessage<UnitAddedEvent>(new UnitAddedEvent() { DepartmentId = DepartmentId, Unit = model.Unit });

				return RedirectToAction("Index");
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_Update)]
		public async Task<IActionResult> EditUnit(int unitId)
		{
			var model = new NewUnitView();
			model.Unit = await _unitsService.GetUnitByIdAsync(unitId);

			if (!await _authorizationService.CanUserModifyUnitAsync(UserId, unitId))
				Unauthorized();

			model.Types = await _unitsService.GetUnitTypesForDepartmentAsync(DepartmentId);

			var groups = new List<DepartmentGroup>();
			groups.Add(new DepartmentGroup
			{
				Name = "No Station"
			});
			groups.AddRange(await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(DepartmentId));
			model.Stations = groups;

			model.UnitRoles = await _unitsService.GetRolesForUnitAsync(unitId);

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Unit_Update)]
		public async Task<IActionResult> EditUnit(NewUnitView model, IFormCollection form, CancellationToken cancellationToken)
		{
			model.Types = await _unitsService.GetUnitTypesForDepartmentAsync(DepartmentId);
			model.Stations = await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(DepartmentId);

			if (!await _authorizationService.CanUserModifyUnitAsync(UserId, model.Unit.UnitId))
				Unauthorized();

			var unitCheck = await _unitsService.GetUnitByNameDepartmentIdAsync(DepartmentId, model.Unit.Name);
			if (unitCheck != null && unitCheck.UnitId != model.Unit.UnitId)
				ModelState.AddModelError("Name", "Unit with that name already exists.");

			var unitRoleNames = (from object key in form.Keys where key.ToString().StartsWith("unitRole_") select form[key.ToString()]).ToList();

			var query = unitRoleNames.GroupBy(x => x)
									  .Where(g => g.Count() > 1)
									  .Select(y => new { Element = y.Key, Counter = y.Count() })
									  .ToList();

			if (query.Any(x => x.Counter > 1))
				ModelState.AddModelError("", "Role Names need to be Unique, please ensure each name is not repeated.");

			//if (!model.Unit.StationGroupId.HasValue || model.Unit.StationGroupId.Value == 0)
			//	ModelState.AddModelError("", "You must select a Station Group to assign this unit to.");

			var unit = await _unitsService.GetUnitByIdAsync(model.Unit.UnitId);

			var auditEvent = new AuditEvent();
			auditEvent.DepartmentId = DepartmentId;
			auditEvent.UserId = UserId;
			auditEvent.Type = AuditLogTypes.UnitChanged;
			auditEvent.Before = unit.CloneJsonToString();

			unit.Name = model.Unit.Name;
			unit.Type = model.Unit.Type;

			if (model.Unit.StationGroupId.HasValue && model.Unit.StationGroupId.Value != 0)
				unit.StationGroupId = model.Unit.StationGroupId;
			else
				unit.StationGroupId = null;

			if (ModelState.IsValid)
			{
				await _unitsService.SaveUnitAsync(unit, cancellationToken);

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
					await _unitsService.SetRolesForUnitAsync(unit.UnitId, roles, cancellationToken);
				else
					await _unitsService.ClearRolesForUnitAsync(unit.UnitId, cancellationToken);

				auditEvent.After = unit.CloneJsonToString();
				_eventAggregator.SendMessage<AuditEvent>(auditEvent);

				return RedirectToAction("Index");
			}

			model.Unit = await _unitsService.GetUnitByIdAsync(model.Unit.UnitId);

			model.Types = await _unitsService.GetUnitTypesForDepartmentAsync(DepartmentId);

			var groups = new List<DepartmentGroup>();
			groups.Add(new DepartmentGroup
			{
				Name = "No Station"
			});
			groups.AddRange(await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(DepartmentId));
			model.Stations = groups;

			model.UnitRoles = await _unitsService.GetRolesForUnitAsync(model.Unit.UnitId);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<IActionResult> SetUnitState(int unitId, int stateType, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserViewUnitAsync(UserId, unitId))
				Unauthorized();

			await _unitsService.SetUnitStateAsync(unitId, stateType, DepartmentId, cancellationToken);

			return RedirectToAction("Index");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<IActionResult> SetUnitStateWithDest(int unitId, int stateType, int type, int destination, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserViewUnitAsync(UserId, unitId))
				Unauthorized();

			var state = new UnitState();
			state.UnitId = unitId;
			state.LocalTimestamp = DateTime.UtcNow;
			state.State = stateType;
			state.Timestamp = DateTime.UtcNow;
			state.DestinationId = destination;

			try
			{
				var savedState = await _unitsService.SetUnitStateAsync(state, DepartmentId, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}

			return RedirectToAction("Index");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<IActionResult> SetUnitStateForMultiple(string unitIds, int stateType, CancellationToken cancellationToken)
		{
			if (!String.IsNullOrWhiteSpace(unitIds) && unitIds.Split(char.Parse("|")).Any())
			{
				foreach (var unitId in unitIds.Split(char.Parse("|")))
				{
					var unit = await _unitsService.GetUnitByIdAsync(int.Parse(unitId));

					if (await _authorizationService.CanUserViewUnitAsync(UserId, unit.UnitId))
						Unauthorized();

					await _unitsService.SetUnitStateAsync(unit.UnitId, stateType, DepartmentId, cancellationToken);
				}
			}

			return RedirectToAction("Index");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<IActionResult> SetUnitStateWithDestForMultiple(string unitIds, int stateType, int type, int destination, CancellationToken cancellationToken)
		{
			if (!String.IsNullOrWhiteSpace(unitIds) && unitIds.Split(char.Parse("|")).Any())
			{
				foreach (var unitId in unitIds.Split(char.Parse("|")))
				{
					var unit = await _unitsService.GetUnitByIdAsync(int.Parse(unitId));

					if (!await _authorizationService.CanUserViewUnitAsync(UserId, unit.UnitId))
						Unauthorized();

					var state = new UnitState();
					state.UnitId = unit.UnitId;
					state.LocalTimestamp = DateTime.UtcNow;
					state.State = stateType;
					state.Timestamp = DateTime.UtcNow;
					state.DestinationId = destination;

					try
					{
						var savedState = await _unitsService.SetUnitStateAsync(state, DepartmentId, cancellationToken);
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
		public async Task<IActionResult> DeleteUnit(int unitId, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserModifyUnitAsync(UserId, unitId))
				Unauthorized();

			var unit = await _unitsService.GetUnitByIdAsync(unitId);

			var auditEvent = new AuditEvent();
			auditEvent.DepartmentId = DepartmentId;
			auditEvent.UserId = UserId;
			auditEvent.Type = AuditLogTypes.UnitRemoved;
			auditEvent.Before = unit.CloneJsonToString();
			_eventAggregator.SendMessage<AuditEvent>(auditEvent);

			await _unitsService.DeleteUnitAsync(unitId, cancellationToken);

			return RedirectToAction("Index");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.UnitLog_Create)]
		public async Task<IActionResult> AddLog(int unitId)
		{
			var model = new AddLogView();
			var unit = await _unitsService.GetUnitByIdAsync(unitId);

			if (unit == null)
				Unauthorized();

			if (!await _authorizationService.CanUserViewUnitAsync(UserId, unitId))
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
		public async Task<IActionResult> AddLog(AddLogView model, CancellationToken cancellationToken)
		{
			if (!await _authorizationService.CanUserViewUnitAsync(UserId, model.Log.UnitId))
				Unauthorized();

			if (ModelState.IsValid)
			{
				model.Log.Narrative = System.Net.WebUtility.HtmlDecode(model.Log.Narrative);
				await _unitsService.SaveUnitLogAsync(model.Log, cancellationToken);

				return RedirectToAction("Index");
			}

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.UnitLog_View)]
		public async Task<IActionResult> ViewLogs(int unitId)
		{
			if (!await _authorizationService.CanUserViewUnitAsync(UserId, unitId))
				Unauthorized();

			var model = new ViewLogsView();
			model.Unit = await _unitsService.GetUnitByIdAsync(unitId);
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			if (model.Unit == null)
				Unauthorized();

			model.Logs = await _unitsService.GetLogsForUnitAsync(model.Unit.UnitId);

			return View(model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<IActionResult> ViewEvents(int unitId)
		{
			if (!await _authorizationService.CanUserViewUnitAsync(UserId, unitId))
				Unauthorized();

			var model = new ViewLogsView();
			model.Unit = await _unitsService.GetUnitByIdAsync(unitId);
			model.OSMKey = Config.MappingConfig.OSMKey;
			var address = await _departmentSettingsService.GetBigBoardCenterAddressDepartmentAsync(DepartmentId);
			var gpsCoordinates = await _departmentSettingsService.GetBigBoardCenterGpsCoordinatesDepartmentAsync(DepartmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			double? centerLat = null;
			double? centerLon = null;

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

			model.CenterLat = centerLat.Value;
			model.CenterLon = centerLon.Value;

			return View(model);
		}

		[HttpPost]
		[Authorize(Policy = ResgridResources.Unit_Update)]
		public async Task<IActionResult> GenerateReport(IFormCollection form)
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
			model.Department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			foreach (var eventId in eventIds)
			{
				var eventJson = new UnitEventJson();
				var eventRecord = await _unitsService.GetUnitStateByIdAsync(eventId);

				model.RunOn = DateTime.UtcNow.TimeConverter(model.Department);

				eventJson.UnitName = eventRecord.Unit.Name;
				eventJson.State = StringHelpers.GetDescription(((UnitStateTypes)eventRecord.State));
				eventJson.Timestamp = eventRecord.Timestamp.TimeConverterToString(model.Department).ToString();
				eventJson.Note = eventRecord.Note;

				if (((UnitStateTypes)eventRecord.State) == UnitStateTypes.Enroute)
				{
					if (eventRecord.DestinationId.HasValue)
					{
						var station = await _departmentGroupsService.GetGroupByIdAsync(eventRecord.DestinationId.Value, false);

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
						var call = await _callsService.GetCallByIdAsync(eventRecord.DestinationId.Value, false);

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
		public async Task<IActionResult> ClearAllUnitEvents(ViewLogsView model, CancellationToken cancellationToken)
		{
			if (model.ConfirmClearAll)
				await _unitsService.DeleteStatesForUnitAsync(model.Unit.UnitId, cancellationToken);

			return RedirectToAction("ViewEvents", new { unitId = model.Unit.UnitId });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<IActionResult> GetUnitEvents(int unitId)
		{
			var unitEvents = new List<UnitEventJson>();

			if (!await _authorizationService.CanUserViewUnitAsync(UserId, unitId))
				Unauthorized();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var events = await _unitsService.GetAllStatesForUnitAsync(unitId);

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
						var station = await _departmentGroupsService.GetGroupByIdAsync(e.DestinationId.Value, false);

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
						var call = await _callsService.GetCallByIdAsync(e.DestinationId.Value, false);

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

		public async Task<IActionResult> GetUnits()
		{
			var units = new List<UnitJson>();
			var savedUnits = await _unitsService.GetUnitsForDepartmentAsync(DepartmentId);

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

		public async Task<IActionResult> GetUnitsForGroup(int groupId)
		{
			var units = new List<UnitJson>();
			var group = await _departmentGroupsService.GetGroupByIdAsync(groupId);

			if (group == null || group.DepartmentId != DepartmentId)
				Unauthorized();

			var savedUnits = await _unitsService.GetAllUnitsForGroupAsync(groupId);

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

		public async Task<IActionResult> GetUnitsAndRolesForGroup(int groupId)
		{
			var units = new List<UnitJson>();
			var group = await _departmentGroupsService.GetGroupByIdAsync(groupId);

			if (group == null || group.DepartmentId != DepartmentId)
				Unauthorized();

			var savedUnits = await _unitsService.GetAllUnitsForGroupAsync(groupId);

			foreach (var unit in savedUnits)
			{
				var unitJson = new UnitJson();
				unitJson.UnitId = unit.UnitId;
				unitJson.Name = unit.Name;
				unitJson.Type = unit.Type;
				unitJson.GroupId = unit.StationGroupId.GetValueOrDefault();

				if (unit.StationGroup != null)
					unitJson.Station = unit.StationGroup.Name;

				var unitRoles = await _unitsService.GetRolesForUnitAsync(unit.UnitId);

				if (unitRoles != null && unitRoles.Any())
				{
					unitJson.Roles = new List<WebCore.Areas.User.Models.Units.UnitRoleJson>();

					foreach (var unitRole in unitRoles)
					{
						var role = new WebCore.Areas.User.Models.Units.UnitRoleJson();
						role.UnitId = unitRole.UnitId;
						role.UnitRoleId = unitRole.UnitRoleId;
						role.Name = unitRole.Name;

						unitJson.Roles.Add(role);
					}
				}

				units.Add(unitJson);
			}

			return Json(units);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<IActionResult> GetUnitTypes()
		{
			var unitTypes = new List<UnitTypeJson>();
			var types = await _unitsService.GetUnitTypesForDepartmentAsync(DepartmentId);

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

		public async Task<IActionResult> GetUnitsList()
		{
			List<UnitForListJson> unitsJson = new List<UnitForListJson>();

			var units = await _unitsService.GetUnitsForDepartmentAsync(DepartmentId);
			var states = await _unitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync(DepartmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);

			foreach (var unit in units)
			{
				var unitJson = new UnitForListJson();
				unitJson.Name = unit.Name;
				unitJson.Type = unit.Type;
				unitJson.UnitId = unit.UnitId;

				if (unit.StationGroupId.HasValue)
				{
					var group = groups.FirstOrDefault(x => x.DepartmentGroupId == unit.StationGroupId.Value);

					if (group != null)
						unitJson.Station = group.Name;
				}

				var state = states.FirstOrDefault(x => x.UnitId == unit.UnitId);

				if (state != null)
				{
					var customState = await CustomStatesHelper.GetCustomUnitState(state);

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

		public async Task<IActionResult> GetUnitsForCallGrid(string callLat, string callLong)
		{
			List<UnitForListJson> unitsJson = new List<UnitForListJson>();

			var units = await _unitsService.GetUnitsForDepartmentAsync(DepartmentId);
			var states = await _unitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync(DepartmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

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
					var customState = await CustomStatesHelper.GetCustomUnitState(state);

					unitJson.StateId = state.State;
					unitJson.State = customState.ButtonText;
					unitJson.StateColor = customState.ButtonColor;
					unitJson.TextColor = customState.TextColor;
					unitJson.Timestamp = state.Timestamp.TimeConverterToString(department);


					if (String.IsNullOrWhiteSpace(callLat) || String.IsNullOrWhiteSpace(callLong))
						unitJson.Eta = "N/A";
					else
					{
						var location = await _unitsService.GetLatestUnitLocationAsync(state.UnitId, state.Timestamp);

						if (location != null)
						{
							var eta = await _geoService.GetEtaInSecondsAsync($"{location.Latitude},{location.Longitude}", String.Format("{0},{1}", callLat, callLong));

							if (eta > 0)
								unitJson.Eta = $"{Math.Round(eta / 60, MidpointRounding.AwayFromZero)}m";
							else
								unitJson.Eta = "N/A";
						}
						else if (!String.IsNullOrWhiteSpace(state.GeoLocationData))
						{
							var eta = await _geoService.GetEtaInSecondsAsync(state.GeoLocationData, String.Format("{0},{1}", callLat, callLong));

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
		public async Task<IActionResult> GetUnitOptionsDropdown(int unitId)
		{
			string buttonHtml = string.Empty;


			var unit = await _unitsService.GetUnitByIdAsync(unitId);
			var type = await _unitsService.GetUnitTypeByNameAsync(DepartmentId, unit.Type);

			if (type != null && type.CustomStatesId.HasValue)
			{
				var customStates = await _customStateService.GetCustomSateByIdAsync(type.CustomStatesId.Value);
				var activeCalls = await _callsService.GetActiveCallsByDepartmentAsync(DepartmentId);
				var stations = await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(DepartmentId);

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
		public async Task<IActionResult> GetUnitOptionsDropdownForStates(int stateId, string units)
		{
			string buttonHtml = string.Empty;

			if (stateId > 1)
			{
				var customStates = await _customStateService.GetCustomSateByIdAsync(stateId);
				var activeCalls = await _callsService.GetActiveCallsByDepartmentAsync(DepartmentId);
				var stations = await _departmentGroupsService.GetAllStationGroupsForDepartmentAsync(DepartmentId);

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
