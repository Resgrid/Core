using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Helpers;
using Resgrid.Web.Services.Controllers.Version3.Models.BigBoard.BigBoardX;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.UnitStatus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Units Status (State) information. For example is the unit Responding to a Call, or Available.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class UnitStatusController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly ICallsService _callsService;
		private readonly IUnitsService _unitsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IMappingService _mappingService;
		private readonly IIncidentCommandService _incidentCommandService;

		public UnitStatusController(
			ICallsService callsService,
			IUnitsService unitsService,
			IDepartmentGroupsService departmentGroupsService,
			IDepartmentSettingsService departmentSettingsService,
			IActionLogsService actionLogsService,
			IMappingService mappingService,
			IIncidentCommandService incidentCommandService
			)
		{
			_callsService = callsService;
			_unitsService = unitsService;
			_departmentGroupsService = departmentGroupsService;
			_departmentSettingsService = departmentSettingsService;
			_actionLogsService = actionLogsService;
			_mappingService = mappingService;
			_incidentCommandService = incidentCommandService;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Gets all the units in a departments current (latest) status (state) or a default
		/// </summary>
		/// <returns></returns>
		[HttpGet("GetAllUnitStatuses")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<ActionResult<UnitStautsesResult>> GetAllUnitStatuses()
		{
			var result = new UnitStautsesResult();

			var units = await _unitsService.GetUnitsForDepartmentAsync(DepartmentId);

			if (units != null && units.Any())
			{
				var unitStates = await _unitsService.GetAllLatestStatusForUnitsByDepartmentIdAsync(DepartmentId);
				var activeCalls = await _callsService.GetActiveCallsByDepartmentAsync(DepartmentId);
				var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);
				var pois = await _mappingService.GetPOIsForDepartmentAsync(DepartmentId);

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

				DateTime timestamp = DateTime.UtcNow;
				foreach (var unit in sortedUnits)
				{
					var stateFound = unitStates.FirstOrDefault(x => x.UnitId == unit.Unit.UnitId);

					if (stateFound != null)
					{
						timestamp = stateFound.Timestamp;
						var customState = await CustomStatesHelper.GetCustomUnitState(stateFound);
						var latestUnitLocation = await _unitsService.GetLatestUnitLocationAsync(unit.Unit.UnitId, timestamp);

						result.Data.Add(ConvertUnitStatusData(unit.Unit, stateFound, latestUnitLocation, customState, unit.Station, TimeZone, activeCalls, groups, pois));
					}
					else
					{
						var latestUnitLocation = await _unitsService.GetLatestUnitLocationAsync(unit.Unit.UnitId, timestamp);
						result.Data.Add(ConvertUnitStatusData(unit.Unit, stateFound, latestUnitLocation, null, unit.Station, TimeZone, activeCalls, groups, pois));
					}
				}

				result.PageSize = result.Data.Count;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// Gets the unit status for a specific unit id
		/// </summary>
		/// <returns></returns>
		[HttpGet("GetUnitStatus")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<ActionResult<UnitStatusResult>> GetUnitStatus(string unitId)
		{
			var result = new UnitStatusResult();

			if (String.IsNullOrWhiteSpace(unitId))
				return BadRequest();

			var unit = await _unitsService.GetUnitByIdAsync(int.Parse(unitId));

			if (unit == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return Ok(result);
			}

			if (unit.DepartmentId != DepartmentId)
				return Unauthorized();

			var activeCalls = await _callsService.GetActiveCallsByDepartmentAsync(DepartmentId);
			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);
			var pois = await _mappingService.GetPOIsForDepartmentAsync(DepartmentId);
			var status = await _unitsService.GetLastUnitStateByUnitIdAsync(int.Parse(unitId));

			DepartmentGroup group = null;
			if (unit.StationGroupId.HasValue)
				group = await _departmentGroupsService.GetGroupByIdAsync(unit.StationGroupId.Value);

			DateTime timestamp = DateTime.UtcNow;
			if (status != null)
			{
				timestamp = status.Timestamp;
				var customState = await CustomStatesHelper.GetCustomUnitState(status);
				var latestUnitLocation = await _unitsService.GetLatestUnitLocationAsync(status.UnitId, timestamp);

				result.Data = ConvertUnitStatusData(unit, status, latestUnitLocation, customState, group, TimeZone, activeCalls, groups, pois);
			}
			else
			{
				var latestUnitLocation = await _unitsService.GetLatestUnitLocationAsync(unit.UnitId, timestamp);
				result.Data = ConvertUnitStatusData(unit, null, latestUnitLocation, null, group, TimeZone, activeCalls, groups, pois);
			}

			result.PageSize = 1;
			result.Status = ResponseHelper.Success;

			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// Sets the status/action for the current user.
		/// </summary>
		/// <param name="statusInput">StatusInput object with the Status/Action to set.</param>
		/// <returns>Returns HttpStatusCode Created if successful, BadRequest otherwise.</returns>
		[HttpPost("SaveUnitStatus")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<ActionResult<SaveUnitStatusResult>> SaveUnitStatus(UnitStatusInput statusInput, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (!ModelState.IsValid)
				return BadRequest();

			return await ProcessSetUnitState(statusInput, cancellationToken);
		}

		private async Task<ActionResult<SaveUnitStatusResult>> ProcessSetUnitState(UnitStatusInput stateInput, CancellationToken cancellationToken = default(CancellationToken))
		{
			var result = new SaveUnitStatusResult();

			// Validate the unit id up front: this runs before the try/catch and ModelState check below, so a
			// non-numeric id would otherwise throw an unhandled FormatException (HTTP 500) instead of a 400.
			if (!int.TryParse(stateInput.Id, out var unitId))
				return BadRequest();

			var unit = await _unitsService.GetUnitByIdAsync(unitId);
			var setPersonnelStatus = await _departmentSettingsService.GetPersonnelOnUnitSetUnitStatusAsync(DepartmentId);

			if (unit == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return Ok(result);
			}

			if (unit.DepartmentId != DepartmentId)
				return Unauthorized();

			if (this.ModelState.IsValid)
			{
				try
				{
					var state = new UnitState();

					state.UnitId = unitId;
					state.LocalTimestamp = stateInput.Timestamp;

					// Parse device-supplied telemetry defensively: a malformed value (e.g. a "lat,lon" pair sent
					// in a single field) must be skipped, not throw FormatException and reject the whole status
					// update. Invalid values are simply omitted from the saved state.
					if (!String.IsNullOrWhiteSpace(stateInput.Latitude) && decimal.TryParse(stateInput.Latitude, out var latitude))
						state.Latitude = latitude;

					if (!String.IsNullOrWhiteSpace(stateInput.Longitude) && decimal.TryParse(stateInput.Longitude, out var longitude))
						state.Longitude = longitude;

					if (!String.IsNullOrWhiteSpace(stateInput.Accuracy) && decimal.TryParse(stateInput.Accuracy, out var accuracy))
						state.Accuracy = accuracy;

					if (!String.IsNullOrWhiteSpace(stateInput.Altitude) && decimal.TryParse(stateInput.Altitude, out var altitude))
						state.Altitude = altitude;

					if (!String.IsNullOrWhiteSpace(stateInput.AltitudeAccuracy) && decimal.TryParse(stateInput.AltitudeAccuracy, out var altitudeAccuracy))
						state.AltitudeAccuracy = altitudeAccuracy;

					if (!String.IsNullOrWhiteSpace(stateInput.Speed) && decimal.TryParse(stateInput.Speed, out var speed))
						state.Speed = speed;

					if (!String.IsNullOrWhiteSpace(stateInput.Heading) && decimal.TryParse(stateInput.Heading, out var heading))
						state.Heading = heading;

					if (!int.TryParse(stateInput.Type, out var stateType))
						return BadRequest();

					state.State = stateType;

					if (stateInput.Timestamp.HasValue)
						state.Timestamp = stateInput.Timestamp.Value;
					else
						state.Timestamp = DateTime.UtcNow;

					state.Note = stateInput.Note;

					if (state.Latitude.HasValue && state.Longitude.HasValue)
					{
						state.GeoLocationData = string.Format("{0},{1}", state.Latitude.Value, state.Longitude.Value);
					}

					if (!string.IsNullOrWhiteSpace(stateInput.RespondingTo) && int.TryParse(stateInput.RespondingTo, out var respondingToId) && respondingToId > 0)
					{
						var destinationType = stateInput.RespondingToType ?? (int)DestinationEntityTypes.Call;
						var destinationId = respondingToId;

						if (!await IsValidDestinationAsync(destinationId, destinationType))
							return BadRequest();

						state.DestinationId = destinationId;
						state.DestinationType = destinationType;
					}

					var savedState = await _unitsService.SetUnitStateAsync(state, DepartmentId);

					// Entity-need response: a unit that command requested answering the call lands on the
					// incident log. Best-effort — never blocks the status save.
					if (state.DestinationType == (int)DestinationEntityTypes.Call && state.DestinationId > 0)
					{
						try
						{
							var stateLabel = Enum.IsDefined(typeof(UnitStateTypes), state.State) ? ((UnitStateTypes)state.State).ToString() : $"status {state.State}";
							await _incidentCommandService.RecordNeedEntityStatusAsync(DepartmentId, state.DestinationId.Value, NeedEntityKind.Unit, state.UnitId.ToString(), stateLabel, UserId, cancellationToken);
						}
						catch (Exception ex)
						{
							Resgrid.Framework.Logging.LogException(ex);
						}
					}

					if (stateInput.Roles != null && stateInput.Roles.Count > 0)
					{
						var unitRoles = await _unitsService.GetRolesForUnitAsync(savedState.UnitId);
						var roles = new List<UnitStateRole>();
						foreach (var role in stateInput.Roles)
						{
							// Skip roles with a non-numeric RoleId rather than throwing and rejecting the whole
							// status update (TryParse also covers the null/empty check).
							if (!string.IsNullOrWhiteSpace(role.UserId) && int.TryParse(role.RoleId, out var unitStateRoleId))
							{
								var unitRole = new UnitStateRole();
								unitRole.UnitStateId = savedState.UnitStateId;
								unitRole.UserId = role.UserId;
								unitRole.UnitStateRoleId = unitStateRoleId;

								if (String.IsNullOrWhiteSpace(role.Name))
								{
									var savedRole = unitRoles.FirstOrDefault(x => x.UnitRoleId == unitRole.UnitStateRoleId);

									if (savedRole != null)
										unitRole.Role = savedRole.Name;
								}
								else
								{
									unitRole.Role = role.Name;
								}

								unitRole.IdValue = 0;
								unitRole.UnitStateRoleId = 0;

								roles.Add(unitRole);
								//_unitsService.AddUnitStateRoleForEvent(savedState.UnitStateId, role.Uid, role.Rid, savedState.Unit.Name, savedState.Timestamp);
							}
						}

						await _unitsService.AddAllUnitStateRolesAsync(roles);

						if (setPersonnelStatus)
						{
							if (roles.Count > 0)
							{
								foreach (var role in roles)
								{
									await _actionLogsService.CreateUnitLinkedStatus(role.UserId, DepartmentId, savedState.UnitStateId, unit.Name, cancellationToken);
								}
							}
						}
					}

					//OutboundEventProvider.UnitStatusTopicHandler handler = new OutboundEventProvider.UnitStatusTopicHandler();
					//handler.Handle(new UnitStatusEvent() { DepartmentId = DepartmentId, Status = savedState });
					// NOTE: UnitStatusEvent is now fired inside SetUnitStateAsync with PreviousStatus populated.

					if (savedState.UnitStateId > 0)
					{
						result.Id = savedState.UnitStateId.ToString();
						result.PageSize = 0;
						result.Status = ResponseHelper.Created;

						ResponseHelper.PopulateV4ResponseData(result);

						return CreatedAtAction("GetAllUnitStatuses", result);
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
					return BadRequest();
				}
			}

			return BadRequest();
		}


		public static UnitStatusResultData ConvertUnitStatusData(Unit unit, UnitState stateFound, UnitsLocation latestUnitLocation,
			CustomStateDetail customState, DepartmentGroup group, string timeZone, List<Call> activeCalls, List<DepartmentGroup> groups, List<Poi> pois)
		{
			var state = "Unknown";
			var stateCss = "";
			var stateStyle = "";
			int? destinationId = 0;
			int? destinationType = 0;
			decimal? latitude = 0;
			decimal? longitude = 0;
			var destinationName = "";
			var destinationAddress = "";
			var destinationTypeName = "";
			DateTime timestamp = DateTime.UtcNow;

			if (stateFound != null)
			{
				if (customState != null)
				{
					state = customState.ButtonText;
					stateCss = customState.ButtonColor;
					stateStyle = customState.ButtonColor;
				}
				else
				{
					state = stateFound.ToStateDisplayText();
					stateCss = stateFound.ToStateCss();
				}

				var resolvedDestination = DestinationResolutionHelper.Resolve(stateFound.DestinationId, stateFound.DestinationType, customState?.DetailType, activeCalls, groups, pois);
				destinationId = resolvedDestination.DestinationId;
				destinationType = resolvedDestination.DestinationType;
				destinationName = resolvedDestination.Name;
				destinationAddress = resolvedDestination.Address;
				destinationTypeName = resolvedDestination.TypeName;
				latitude = stateFound.Latitude;
				longitude = stateFound.Longitude;
				timestamp = stateFound.Timestamp;
			}

			string groupName = "";
			int groupId = 0;
			if (group != null)
			{
				groupId = group.DepartmentGroupId;
				groupName = group.Name;
			}

			if (latestUnitLocation != null)
			{
				latitude = latestUnitLocation.Latitude;
				longitude = latestUnitLocation.Longitude;
			}

			var unitViewModel = new UnitStatusResultData
			{
				UnitId = unit.UnitId.ToString(),
				Name = unit.Name,
				Type = unit.Type,
				State = state,
				StateCss = stateCss,
				StateStyle = stateStyle,
				TimestampUtc = timestamp,
				Timestamp = timestamp.TimeConverter(new Department() { TimeZone = timeZone }),
				DestinationId = destinationId,
				DestinationType = destinationType,
				Latitude = latitude,
				Longitude = longitude,
				GroupId = groupId,
				GroupName = groupName,
				DestinationName = destinationName,
				DestinationAddress = destinationAddress,
				DestinationTypeName = destinationTypeName
			};

			return unitViewModel;
		}

		private async Task<bool> IsValidDestinationAsync(int destinationId, int destinationType)
		{
			var entityType = (DestinationEntityTypes)destinationType;

			switch (entityType)
			{
				case DestinationEntityTypes.Station:
					var station = await _departmentGroupsService.GetGroupByIdAsync(destinationId);
					return station != null && station.DepartmentId == DepartmentId;
				case DestinationEntityTypes.Call:
					var call = await _callsService.GetCallByIdAsync(destinationId);
					return call != null && call.DepartmentId == DepartmentId;
				case DestinationEntityTypes.Poi:
					return await _mappingService.GetDestinationPOIByIdAsync(DepartmentId, destinationId) != null;
				default:
					return false;
			}
		}
	}
}
