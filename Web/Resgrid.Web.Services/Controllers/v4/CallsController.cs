using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Models.v4.Calls;
using System;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model.Helpers;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;
using Resgrid.Web.Services.Helpers;
using System.Net.Mime;
using System.Threading;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using Resgrid.Model.Events;
using Resgrid.Model.Queue;
using Resgrid.Web.Services.Models.v4.CallProtocols;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Calls, also referred to as Dispatches.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class CallsController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly ICallsService _callsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IGeoLocationProvider _geoLocationProvider;
		private readonly IAuthorizationService _authorizationService;
		private readonly IQueueService _queueService;
		private readonly IUsersService _usersService;
		private readonly IUnitsService _unitsService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IProtocolsService _protocolsService;
		private readonly IEventAggregator _eventAggregator;
		private readonly ICustomStateService _customStateService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IShiftsService _shiftsService;

		public CallsController(
			ICallsService callsService,
			IDepartmentsService departmentsService,
			IUserProfileService userProfileService,
			IGeoLocationProvider geoLocationProvider,
			IAuthorizationService authorizationService,
			IQueueService queueService,
			IUsersService usersService,
			IUnitsService unitsService,
			IActionLogsService actionLogsService,
			IDepartmentGroupsService departmentGroupsService,
			IPersonnelRolesService personnelRolesService,
			IProtocolsService protocolsService,
			IEventAggregator eventAggregator,
			ICustomStateService customStateService,
			IDepartmentSettingsService departmentSettingsService,
			IShiftsService shiftsService
			)
		{
			_callsService = callsService;
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
			_geoLocationProvider = geoLocationProvider;
			_authorizationService = authorizationService;
			_queueService = queueService;
			_usersService = usersService;
			_unitsService = unitsService;
			_actionLogsService = actionLogsService;
			_departmentGroupsService = departmentGroupsService;
			_personnelRolesService = personnelRolesService;
			_protocolsService = protocolsService;
			_eventAggregator = eventAggregator;
			_customStateService = customStateService;
			_departmentSettingsService = departmentSettingsService;
			_shiftsService = shiftsService;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Returns all the active calls for the department
		/// </summary>
		/// <returns>Array of CallResult objects for each active call in the department</returns>
		[HttpGet("GetActiveCalls")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<ActiveCallsResult>> GetActiveCalls()
		{
			var result = new ActiveCallsResult();

			var calls = (await _callsService.GetActiveCallsByDepartmentAsync(DepartmentId)).OrderByDescending(x => x.LoggedOn);

			if (calls != null && calls.Any())
			{
				foreach (var c in calls)
				{
					var callWithData = await _callsService.PopulateCallData(c, false, true, true, false, false, false, true, true, true);

					string address = "";
					if (String.IsNullOrWhiteSpace(c.Address) && c.HasValidGeolocationData())
					{
						var geo = c.GeoLocationData.Split(char.Parse(","));

						if (geo.Length == 2)
							address = await _geoLocationProvider.GetAddressFromLatLong(double.Parse(geo[0]), double.Parse(geo[1]));
					}
					else
						address = c.Address;

					result.Data.Add(ConvertCall(callWithData, null, address, TimeZone));
				}
				result.PageSize = result.Data.Count();
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Returns a specific call from the Resgrid System
		/// </summary>
		/// <param name="callId">Id of the call trying to be retrived</param>
		/// <returns>CallResult of the call in the Resgrid system</returns>
		[HttpGet("GetCall")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<GetCallResult>> GetCall(string callId)
		{
			if (String.IsNullOrWhiteSpace(callId))
				return BadRequest();

			var result = new CallResult();
			var c = await _callsService.GetCallByIdAsync(int.Parse(callId));

			if (c == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return Ok(result);
			}

			if (c.DepartmentId != DepartmentId)
				return Unauthorized();

			if (!await _authorizationService.CanUserViewCallAsync(UserId, int.Parse(callId)))
				return Unauthorized();

			c = await _callsService.PopulateCallData(c, false, true, true, false, false, false, true, true, true);

			string address = "";
			if (String.IsNullOrWhiteSpace(c.Address) && c.HasValidGeolocationData())
			{
				var geo = c.GeoLocationData.Split(char.Parse(","));

				if (geo.Length == 2)
					address = await _geoLocationProvider.GetAddressFromLatLong(double.Parse(geo[0]), double.Parse(geo[1]));
			}
			else
				address = c.Address;

			var protocols = new List<DispatchProtocol>();
			if (c.Protocols != null && c.Protocols.Any())
			{
				foreach (var callProtocol in c.Protocols)
				{
					var protocol = await _protocolsService.GetProtocolByIdAsync(callProtocol.CallProtocolId);
					if (protocol != null)
						protocols.Add(protocol);
				}
			}

			result.Data = ConvertCall(c, protocols, address, TimeZone);

			result.PageSize = 1;
			result.Status = ResponseHelper.Success;

			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Gets all the meta-data around a call, dispatched personnel, units, groups and responses
		/// </summary>
		/// <param name="callId">CallId to get data for</param>
		/// <returns></returns>
		[HttpGet("GetCallExtraData")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<CallExtraDataResult>> GetCallExtraData(int callId)
		{
			var result = new CallExtraDataResult();

			var call = await _callsService.GetCallByIdAsync(callId);

			if (call == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return Ok(result);
			}

			if (call.DepartmentId != DepartmentId)
				Unauthorized();

			if (!await _authorizationService.CanUserViewCallAsync(UserId, callId))
				return Unauthorized();

			call = await _callsService.PopulateCallData(call, true, true, true, true, true, true, true, true, true);

			result.Data.CallFormData = call.CallFormData;

			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);
			var units = await _unitsService.GetUnitsForDepartmentAsync(call.DepartmentId);
			var unitStates = (await _unitsService.GetUnitStatesForCallAsync(call.DepartmentId, callId)).OrderBy(x => x.UnitId).OrderBy(y => y.Timestamp).ToList();
			var actionLogs = (await _actionLogsService.GetActionLogsForCallAsync(call.DepartmentId, callId)).OrderBy(x => x.UserId).OrderBy(y => y.Timestamp).ToList();
			var names = await _usersService.GetUserGroupAndRolesByDepartmentIdAsync(DepartmentId, true, true, true);
			var priority = await _callsService.GetCallPrioritiesByIdAsync(call.DepartmentId, call.Priority, false);
			var roles = await _personnelRolesService.GetAllRolesForDepartmentAsync(call.DepartmentId);

			var customStates = await _customStateService.GetAllCustomStatesForDepartmentAsync(call.DepartmentId);
			var defaultUnitStatuses = _customStateService.GetDefaultUnitStatuses();
			var defaultUserStatuses = await _customStateService.GetCustomPersonnelStatusesOrDefaultsAsync(call.DepartmentId);

			if (priority != null)
			{
				result.Data.Priority = CallPrioritiesController.ConvertPriorityData(priority);
			}

			foreach (var actionLog in actionLogs)
			{
				var eventResult = new DispatchedEventResultData();
				eventResult.Id = actionLog.ActionLogId.ToString();
				eventResult.Timestamp = actionLog.Timestamp;
				eventResult.Type = "User";

				var name = names.FirstOrDefault(x => x.UserId == actionLog.UserId);
				if (name != null)
				{
					eventResult.Name = name.Name;

					if (name.DepartmentGroupId.HasValue)
					{
						eventResult.GroupId = name.DepartmentGroupId.Value.ToString();
						eventResult.Group = name.DepartmentGroupName;
					}
				}
				else
				{
					eventResult.Name = "Unknown User";
				}

				eventResult.StatusId = actionLog.ActionTypeId;
				eventResult.Location = actionLog.GeoLocationData;
				eventResult.Note = actionLog.Note;

				if (actionLog.ActionTypeId <= 25)
				{
					var state = defaultUserStatuses.FirstOrDefault(x => x.CustomStateDetailId == actionLog.ActionTypeId);

					if (state != null)
					{
						eventResult.StatusText = state.ButtonText;
						eventResult.StatusColor = state.ButtonColor;
					}
				}
				else
				{
					if (customStates != null && customStates.Count > 0)
					{
						var state = customStates.Select(state => state.Details.FirstOrDefault(x => x.CustomStateDetailId == actionLog.ActionTypeId)).FirstOrDefault(detail => detail != null);

						if (state != null)
						{
							eventResult.StatusText = state.ButtonText;
							eventResult.StatusColor = state.ButtonColor;
						}
					}
				}

				if (String.IsNullOrWhiteSpace(eventResult.StatusText))
					eventResult.StatusText = "Unknown";

				if (String.IsNullOrWhiteSpace(eventResult.StatusColor))
					eventResult.StatusColor = "#ffa500";

				result.Data.Activity.Add(eventResult);
			}

			foreach (var unitLog in unitStates)
			{
				var eventResult = new DispatchedEventResultData();
				eventResult.Id = unitLog.UnitStateId.ToString();
				eventResult.Timestamp = unitLog.Timestamp;
				eventResult.Type = "Unit";
				eventResult.Name = unitLog.Unit.Name;

				var group = groups.FirstOrDefault(x => x.DepartmentGroupId == unitLog.Unit.StationGroupId);
				if (group != null)
				{
					eventResult.GroupId = group.DepartmentGroupId.ToString();
					eventResult.Group = group.Name;
				}

				eventResult.StatusId = unitLog.State;
				eventResult.Location = unitLog.GeoLocationData;
				eventResult.Note = unitLog.Note;

				if (unitLog.UnitStateId <= 12)
				{
					var state = defaultUnitStatuses.FirstOrDefault(x => x.CustomStateDetailId == unitLog.UnitStateId);

					if (state != null)
					{
						eventResult.StatusText = state.ButtonText;
						eventResult.StatusColor = state.ButtonColor;
					}
				}
				else
				{
					if (customStates != null && customStates.Count > 0)
					{
						var state = customStates.Select(state => state.Details.FirstOrDefault(x => x.CustomStateDetailId == unitLog.State)).FirstOrDefault(detail => detail != null);

						if (state != null)
						{
							eventResult.StatusText = state.ButtonText;
							eventResult.StatusColor = state.ButtonColor;
						}
					}
				}

				if (String.IsNullOrWhiteSpace(eventResult.StatusText))
					eventResult.StatusText = "Unknown";

				if (String.IsNullOrWhiteSpace(eventResult.StatusColor))
					eventResult.StatusColor = "#ffa500";

				result.Data.Activity.Add(eventResult);
			}

			foreach (var dispatch in call.Dispatches)
			{
				var eventResult = new DispatchedEventResultData();
				eventResult.Id = dispatch.UserId;
				if (dispatch.LastDispatchedOn.HasValue)
				{
					eventResult.Timestamp = dispatch.LastDispatchedOn.Value;
				}
				eventResult.Type = "User";

				var name = names.FirstOrDefault(x => x.UserId == dispatch.UserId);
				if (name != null)
				{
					eventResult.Name = name.Name;

					if (name.DepartmentGroupId.HasValue)
					{
						eventResult.GroupId = name.DepartmentGroupId.Value.ToString();
						eventResult.Group = name.DepartmentGroupName;
					}
				}
				else
				{
					eventResult.Name = "Unknown User";
				}

				result.Data.Dispatches.Add(eventResult);
			}

			if (call.GroupDispatches != null && call.GroupDispatches.Any())
			{
				foreach (var groupDispatch in call.GroupDispatches)
				{
					var eventResult = new DispatchedEventResultData();
					eventResult.Id = groupDispatch.DepartmentGroupId.ToString();
					if (groupDispatch.LastDispatchedOn.HasValue)
					{
						eventResult.Timestamp = groupDispatch.LastDispatchedOn.Value;
					}
					eventResult.Type = "Group";

					var name = groups.FirstOrDefault(x => x.DepartmentGroupId == groupDispatch.DepartmentGroupId);
					if (name != null)
					{
						eventResult.Name = name.Name;
						eventResult.GroupId = name.DepartmentGroupId.ToString();
						eventResult.Group = name.Name;

					}
					else
					{
						eventResult.Name = "Unknown Group";
					}

					result.Data.Dispatches.Add(eventResult);
				}
			}

			if (call.UnitDispatches != null && call.UnitDispatches.Any())
			{
				foreach (var unitDispatch in call.UnitDispatches)
				{
					var eventResult = new DispatchedEventResultData();
					eventResult.Id = unitDispatch.UnitId.ToString();
					if (unitDispatch.LastDispatchedOn.HasValue)
					{
						eventResult.Timestamp = unitDispatch.LastDispatchedOn.Value;
					}
					eventResult.Type = "Unit";

					var unit = units.FirstOrDefault(x => x.UnitId == unitDispatch.UnitId);
					if (unit != null)
					{
						eventResult.Name = unit.Name;

						if (unit.StationGroupId.HasValue)
						{
							var group = groups.FirstOrDefault(x => x.DepartmentGroupId == unit.StationGroupId.GetValueOrDefault());
							if (group != null)
							{
								eventResult.GroupId = group.DepartmentGroupId.ToString();
								eventResult.Group = group.Name;

							}
						}

					}
					else
					{
						eventResult.Name = "Unknown Unit";
					}

					result.Data.Dispatches.Add(eventResult);
				}
			}

			if (call.RoleDispatches != null && call.RoleDispatches.Any())
			{
				foreach (var roleDispatch in call.RoleDispatches)
				{
					var eventResult = new DispatchedEventResultData();
					eventResult.Id = roleDispatch.RoleId.ToString();
					if (roleDispatch.LastDispatchedOn.HasValue)
					{
						eventResult.Timestamp = roleDispatch.LastDispatchedOn.Value;
					}
					eventResult.Type = "Role";

					var role = roles.FirstOrDefault(x => x.PersonnelRoleId == roleDispatch.RoleId);
					if (role != null)
					{
						eventResult.Name = role.Name;
					}
					else
					{
						eventResult.Name = "Unknown Role";
					}

					result.Data.Dispatches.Add(eventResult);
				}
			}

			if (call.Protocols != null && call.Protocols.Any())
			{
				foreach (var callProtocol in call.Protocols)
				{
					var protocol = await _protocolsService.GetProtocolByIdAsync(callProtocol.DispatchProtocolId);

					if (protocol != null)
						result.Data.Protocols.Add(CallProtocolsController.ConvertProtocolData(protocol));
				}
			}

			result.PageSize = 0;
			result.Status = ResponseHelper.Success;

			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Saves a call in the Resgrid system
		/// </summary>
		/// <param name="newCallInput"></param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns></returns>
		[HttpPost("SaveCall")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Call_Create)]
		public async Task<ActionResult<SaveCallResult>> SaveCall([FromBody] NewCallInput newCallInput, CancellationToken cancellationToken)
		{
			var result = new SaveCallResult();

			var canDoOperation = await _authorizationService.CanUserCreateCallAsync(UserId, DepartmentId);

			if (!canDoOperation)
				return Unauthorized();

			if (!ModelState.IsValid)
				return BadRequest();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var activeUsers = await _departmentsService.GetAllMembersForDepartmentAsync(DepartmentId);
			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);
			var roles = await _personnelRolesService.GetAllRolesForDepartmentAsync(DepartmentId);
			var units = await _unitsService.GetUnitsForDepartmentAsync(DepartmentId);

			var call = new Call
			{
				DepartmentId = DepartmentId,
				ReportingUserId = UserId,
				Priority = newCallInput.Priority,
				Name = newCallInput.Name,
				NatureOfCall = newCallInput.Nature
			};

			if (!string.IsNullOrWhiteSpace(newCallInput.ContactName))
				call.ContactName = newCallInput.ContactName;

			if (!string.IsNullOrWhiteSpace(newCallInput.ContactInfo))
				call.ContactNumber = newCallInput.ContactInfo;

			if (!string.IsNullOrWhiteSpace(newCallInput.ExternalId))
				call.ExternalIdentifier = newCallInput.ExternalId;

			if (!string.IsNullOrWhiteSpace(newCallInput.IncidentId))
				call.IncidentNumber = newCallInput.IncidentId;

			if (!string.IsNullOrWhiteSpace(newCallInput.ReferenceId))
				call.ReferenceNumber = newCallInput.ReferenceId;

			if (!string.IsNullOrWhiteSpace(newCallInput.Address))
				call.Address = newCallInput.Address;

			if (!string.IsNullOrWhiteSpace(newCallInput.What3Words))
				call.W3W = newCallInput.What3Words;

			if (!string.IsNullOrWhiteSpace(newCallInput.CallFormData))
				call.CallFormData = newCallInput.CallFormData;

			if (newCallInput.DispatchOn.HasValue)
			{
				call.DispatchOn = DateTimeHelpers.ConvertToUtc(newCallInput.DispatchOn.Value, department.TimeZone);
				call.HasBeenDispatched = false;
			}

			if (!string.IsNullOrWhiteSpace(newCallInput.Note))
				call.Notes = newCallInput.Note;

			if (!string.IsNullOrWhiteSpace(newCallInput.Geolocation))
				call.GeoLocationData = newCallInput.Geolocation;

			if (string.IsNullOrWhiteSpace(call.GeoLocationData) && !string.IsNullOrWhiteSpace(call.Address))
				call.GeoLocationData = await _geoLocationProvider.GetLatLonFromAddress(call.Address);

			if (string.IsNullOrWhiteSpace(call.GeoLocationData) && !string.IsNullOrWhiteSpace(call.W3W))
			{
				var coords = await _geoLocationProvider.GetCoordinatesFromW3WAsync(call.W3W);

				if (coords != null)
				{
					call.GeoLocationData = $"{coords.Latitude},{coords.Longitude}";
				}
			}

			call.LoggedOn = DateTime.UtcNow;

			if (!String.IsNullOrWhiteSpace(newCallInput.Type) && newCallInput.Type != "No Type")
			{
				var callTypes = await _callsService.GetCallTypesForDepartmentAsync(DepartmentId);
				var type = callTypes.FirstOrDefault(x => x.Type == newCallInput.Type);

				if (type != null)
				{
					call.Type = type.Type;
				}
			}
			var users = await _departmentsService.GetAllUsersForDepartmentAsync(DepartmentId);
			call.Dispatches = new Collection<CallDispatch>();
			call.GroupDispatches = new List<CallDispatchGroup>();
			call.RoleDispatches = new List<CallDispatchRole>();
			call.UnitDispatches = new List<CallDispatchUnit>();

			if (newCallInput.DispatchList == "0")
			{
				// Use case, existing clients and non-ionic2 app this will be null dispatch all users. Or we've specified everyone (0).
				foreach (var u in users)
				{
					var cd = new CallDispatch { UserId = u.UserId };

					call.Dispatches.Add(cd);
				}
			}
			else
			{
				var dispatch = newCallInput.DispatchList.Split(char.Parse("|"));

				try
				{
					var usersToDispatch = dispatch.Where(x => x.StartsWith("P:")).Select(y => y.Replace("P:", ""));
					foreach (var user in usersToDispatch)
					{
						if (activeUsers.Any(x => x.UserId == user && x.IsDeleted == false && x.IsDisabled == false))
						{
							var cd = new CallDispatch { UserId = user };
							call.Dispatches.Add(cd);
						}
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}

				try
				{
					var groupsToDispatch = dispatch.Where(x => x.StartsWith("G:")).Select(y => int.Parse(y.Replace("G:", "")));
					foreach (var group in groupsToDispatch)
					{
						if (groups.Any(x => x.DepartmentGroupId == group))
						{
							var cd = new CallDispatchGroup { DepartmentGroupId = group };
							call.GroupDispatches.Add(cd);
						}
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}

				try
				{
					var rolesToDispatch = dispatch.Where(x => x.StartsWith("R:")).Select(y => int.Parse(y.Replace("R:", "")));
					foreach (var role in rolesToDispatch)
					{
						if (roles.Any(x => x.PersonnelRoleId == role))
						{
							var cd = new CallDispatchRole { RoleId = role };
							call.RoleDispatches.Add(cd);
						}
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}

				try
				{
					var unitsToDispatch = dispatch.Where(x => x.StartsWith("U:")).Select(y => int.Parse(y.Replace("U:", "")));
					foreach (var unit in unitsToDispatch)
					{
						if (units.Any(x => x.UnitId == unit))
						{
							var cdu = new CallDispatchUnit { UnitId = unit };
							call.UnitDispatches.Add(cdu);
						}
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}

			if (call.UnitDispatches != null && call.UnitDispatches.Any())
			{
				foreach (var unitDispatch in call.UnitDispatches)
				{
					var unitRoleAssignments = await _unitsService.GetActiveRolesForUnitAsync(unitDispatch.UnitId);

					if (unitRoleAssignments != null && unitRoleAssignments.Any())
					{
						foreach (var unitRoleAssignment in unitRoleAssignments)
						{
							if (!call.Dispatches.Any(x => x.UserId == unitRoleAssignment.UserId))
							{
								CallDispatch cd = new CallDispatch();
								cd.UserId = unitRoleAssignment.UserId;

								call.Dispatches.Add(cd);
							}
						}
					}
				}
			}

			var dispatchShiftInsteadOfGroup = await _departmentSettingsService.GetDispatchShiftInsteadOfGroupAsync(DepartmentId);
			var autoSetStatusForShiftPersonnel = await _departmentSettingsService.GetAutoSetStatusForShiftDispatchPersonnelAsync(DepartmentId);
			var shiftDispatchStatus = await _departmentSettingsService.GetShiftCallDispatchPersonnelStatusToSetAsync(DepartmentId);
			//var shiftClearStatus = await _departmentSettingsService.GetShiftCallReleasePersonnelStatusToSetAsync(DepartmentId);

			List<string> shiftUserIds = new List<string>();
			if (dispatchShiftInsteadOfGroup)
			{
				if (call.GroupDispatches != null && call.GroupDispatches.Any())
				{
					var localizedDate = TimeConverterHelper.TimeConverter(DateTime.UtcNow, department);
					var shiftDate = new DateTime(localizedDate.Year, localizedDate.Month, localizedDate.Day);
					foreach (var group in call.GroupDispatches)
					{
						var signups = await _shiftsService.GetShiftSignupsByDepartmentGroupIdAndDayAsync(group.DepartmentGroupId, shiftDate);

						if (signups != null && signups.Any())
						{
							foreach (var signup in signups)
							{
								CallDispatch cd = new CallDispatch();
								cd.UserId = signup.UserId;

								call.Dispatches.Add(cd);
								shiftUserIds.Add(signup.UserId);
							}
						}
					}
				}
			}

			// Call is in the past or is now, were dispatching now (at the end of this func)
			if (call.DispatchOn.HasValue && call.DispatchOn.Value <= DateTime.UtcNow)
				call.HasBeenDispatched = true;

			var savedCall = await _callsService.SaveCallAsync(call, cancellationToken);

			//OutboundEventProvider handler = new OutboundEventProvider.CallAddedTopicHandler();
			//OutboundEventProvider..Handle(new CallAddedEvent() { DepartmentId = DepartmentId, Call = savedCall });
			_eventAggregator.SendMessage<CallAddedEvent>(new CallAddedEvent() { DepartmentId = DepartmentId, Call = savedCall });

			if (autoSetStatusForShiftPersonnel && shiftUserIds.Any() && call.HasBeenDispatched.GetValueOrDefault())
			{
				if (shiftDispatchStatus < 0)
					shiftDispatchStatus = (int)ActionTypes.RespondingToScene;

				foreach (var user in shiftUserIds)
				{
					await _actionLogsService.SetUserActionAsync(user, DepartmentId, shiftDispatchStatus, null, call.CallId, cancellationToken);
				}
			}

			var profiles = new List<string>();

			if (call.Dispatches != null && call.Dispatches.Any())
			{
				profiles.AddRange(call.Dispatches.Select(x => x.UserId).ToList());
			}

			if (call.GroupDispatches != null && call.GroupDispatches.Any())
			{
				foreach (var groupDispatch in call.GroupDispatches)
				{
					var group = await _departmentGroupsService.GetGroupByIdAsync(groupDispatch.DepartmentGroupId);

					if (group != null && group.Members != null)
					{
						profiles.AddRange(group.Members.Select(x => x.UserId));
					}
				}
			}

			if (call.RoleDispatches != null && call.RoleDispatches.Any())
			{
				foreach (var roleDispatch in call.RoleDispatches)
				{
					var members = await _personnelRolesService.GetAllMembersOfRoleAsync(roleDispatch.RoleId);

					if (members != null)
					{
						profiles.AddRange(members.Select(x => x.UserId).ToList());
					}
				}
			}

			var cqi = new CallQueueItem();
			cqi.Call = savedCall;

			if (profiles.Any())
				cqi.Profiles = await _userProfileService.GetSelectedUserProfilesAsync(profiles);

			if (!savedCall.DispatchOn.HasValue || savedCall.DispatchOn.Value <= DateTime.UtcNow)
				await _queueService.EnqueueCallBroadcastAsync(cqi, cancellationToken);

			result.Id = savedCall.CallId.ToString();
			result.PageSize = 0;
			result.Status = ResponseHelper.Created;

			ResponseHelper.PopulateV4ResponseData(result);

			return CreatedAtAction("GetCall", new { callId = result.Id }, result);
		}

		/// <summary>
		/// Updates an existing Active Call in the Resgrid system
		/// </summary>
		/// <param name="editCallInput">Data to updated the call</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>OK status code if successful</returns>
		[HttpPut("EditCall")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<ActionResult<EditCallResult>> EditCall([FromBody] EditCallInput editCallInput, CancellationToken cancellationToken)
		{
			var result = new EditCallResult();

			var canDoOperation = await _authorizationService.CanUserEditCallAsync(UserId, int.Parse(editCallInput.Id));

			if (!canDoOperation)
				return Unauthorized();

			var call = await _callsService.GetCallByIdAsync(int.Parse(editCallInput.Id));

			call = await _callsService.PopulateCallData(call, true, true, true, true, true, true, true, true, true);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			if (call == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return Ok(result);
			}

			if (!ModelState.IsValid)
				return BadRequest();

			if (call.DepartmentId != DepartmentId)
				return Unauthorized();

			if (call.State != (int)CallStates.Active)
				return BadRequest();

			var activeUsers = await _departmentsService.GetAllMembersForDepartmentAsync(DepartmentId);
			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);
			var roles = await _personnelRolesService.GetAllRolesForDepartmentAsync(DepartmentId);
			var units = await _unitsService.GetUnitsForDepartmentAsync(DepartmentId);

			call.Priority = editCallInput.Priority;
			call.Name = editCallInput.Name;
			call.NatureOfCall = editCallInput.Nature;

			if (!string.IsNullOrWhiteSpace(editCallInput.ContactName))
				call.ContactName = editCallInput.ContactName;

			if (!string.IsNullOrWhiteSpace(editCallInput.ContactInfo))
				call.ContactNumber = editCallInput.ContactInfo;

			if (!string.IsNullOrWhiteSpace(editCallInput.ExternalId))
				call.ExternalIdentifier = editCallInput.ExternalId;

			if (!string.IsNullOrWhiteSpace(editCallInput.IncidentId))
				call.IncidentNumber = editCallInput.IncidentId;

			if (!string.IsNullOrWhiteSpace(editCallInput.ReferenceId))
				call.ReferenceNumber = editCallInput.ReferenceId;

			if (!string.IsNullOrWhiteSpace(editCallInput.Address))
				call.Address = editCallInput.Address;

			if (!string.IsNullOrWhiteSpace(editCallInput.What3Words))
				call.W3W = editCallInput.What3Words;

			if (!string.IsNullOrWhiteSpace(editCallInput.CallFormData))
				call.CallFormData = editCallInput.CallFormData;

			if (editCallInput.DispatchOn.HasValue)
			{
				call.DispatchOn = DateTimeHelpers.ConvertToUtc(editCallInput.DispatchOn.Value, department.TimeZone);
				call.HasBeenDispatched = false;
			}

			if (!string.IsNullOrWhiteSpace(editCallInput.Note))
				call.Notes = editCallInput.Note;

			if (!string.IsNullOrWhiteSpace(editCallInput.Geolocation))
				call.GeoLocationData = editCallInput.Geolocation;

			if (string.IsNullOrWhiteSpace(call.GeoLocationData) && !string.IsNullOrWhiteSpace(call.Address))
				call.GeoLocationData = await _geoLocationProvider.GetLatLonFromAddress(call.Address);

			if (string.IsNullOrWhiteSpace(call.GeoLocationData) && !string.IsNullOrWhiteSpace(call.W3W))
			{
				var coords = await _geoLocationProvider.GetCoordinatesFromW3WAsync(call.W3W);

				if (coords != null)
				{
					call.GeoLocationData = $"{coords.Latitude},{coords.Longitude}";
				}
			}

			if (!String.IsNullOrWhiteSpace(editCallInput.Type) && editCallInput.Type != "No Type")
			{
				var callTypes = await _callsService.GetCallTypesForDepartmentAsync(DepartmentId);
				var type = callTypes.FirstOrDefault(x => x.Type == editCallInput.Type);

				if (type != null)
				{
					call.Type = type.Type;
				}
			}

			if (string.IsNullOrWhiteSpace(editCallInput.DispatchList) || editCallInput.DispatchList == "0")
			{
				if (call.Dispatches == null)
					call.Dispatches = new List<CallDispatch>();

				if (call.GroupDispatches == null)
					call.GroupDispatches = new List<CallDispatchGroup>();

				if (call.RoleDispatches == null)
					call.RoleDispatches = new List<CallDispatchRole>();

				if (call.UnitDispatches == null)
					call.UnitDispatches = new List<CallDispatchUnit>();

				var users = await _departmentsService.GetAllUsersForDepartmentAsync(DepartmentId);
				// Use case, existing clients and non-ionic2 app this will be null dispatch all users. Or we've specified everyone (0).
				foreach (var u in users)
				{
					var cd = new CallDispatch { UserId = u.UserId };

					call.Dispatches.Add(cd);
				}
			}
			else
			{
				var dispatch = editCallInput.DispatchList.Split(char.Parse("|"));
				var usersToDispatch = dispatch.Where(x => x.StartsWith("P:")).Select(y => y.Replace("P:", ""));
				var groupsToDispatch = dispatch.Where(x => x.StartsWith("G:")).Select(y => int.Parse(y.Replace("G:", "")));
				var rolesToDispatch = dispatch.Where(x => x.StartsWith("R:")).Select(y => int.Parse(y.Replace("R:", "")));
				var unitsToDispatch = dispatch.Where(x => x.StartsWith("U:")).Select(y => int.Parse(y.Replace("U:", "")));

				try
				{
					if (call.Dispatches == null)
						call.Dispatches = new List<CallDispatch>();

					var dispatchesToRemove = call.Dispatches.Select(x => x.UserId).Where(y => !usersToDispatch.Contains(y)).ToList();

					foreach (var userId in dispatchesToRemove)
					{
						var item = call.Dispatches.First(x => x.UserId == userId);
						call.Dispatches.Remove(item);
					}

					foreach (var user in usersToDispatch)
					{
						if (!call.Dispatches.Any(x => x.UserId == user))
						{
							if (activeUsers.Any(x => x.UserId == user && x.IsDeleted == false && x.IsDisabled == false))
							{
								var cd = new CallDispatch { CallId = call.CallId, UserId = user };
								call.Dispatches.Add(cd);
							}
						}
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}

				try
				{
					if (call.GroupDispatches == null)
						call.GroupDispatches = new List<CallDispatchGroup>();

					var dispatchesToRemove = call.GroupDispatches.Select(x => x.DepartmentGroupId).Where(y => !groupsToDispatch.Contains(y)).ToList();

					foreach (var id in dispatchesToRemove)
					{
						call.GroupDispatches.Remove(call.GroupDispatches.First(x => x.DepartmentGroupId == id));
					}

					foreach (var group in groupsToDispatch)
					{
						if (!call.GroupDispatches.Any(x => x.DepartmentGroupId == group))
						{
							if (groups.Any(x => x.DepartmentGroupId == group))
							{
								var cdg = new CallDispatchGroup { CallId = call.CallId, DepartmentGroupId = group };
								call.GroupDispatches.Add(cdg);
							}
						}
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}

				try
				{
					if (call.RoleDispatches == null)
						call.RoleDispatches = new List<CallDispatchRole>();

					var dispatchesToRemove = call.RoleDispatches.Select(x => x.RoleId).Where(y => !rolesToDispatch.Contains(y)).ToList();

					foreach (var id in dispatchesToRemove)
					{
						call.RoleDispatches.Remove(call.RoleDispatches.First(x => x.RoleId == id));
					}

					foreach (var role in rolesToDispatch)
					{
						if (!call.RoleDispatches.Any(x => x.RoleId == role))
						{
							if (roles.Any(x => x.PersonnelRoleId == role))
							{
								var cdr = new CallDispatchRole { CallId = call.CallId, RoleId = role };
								call.RoleDispatches.Add(cdr);
							}
						}
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}

				try
				{
					if (call.UnitDispatches == null)
						call.UnitDispatches = new List<CallDispatchUnit>();

					var dispatchesToRemove = call.UnitDispatches.Select(x => x.UnitId).Where(y => !unitsToDispatch.Contains(y)).ToList();

					foreach (var id in dispatchesToRemove)
					{
						call.UnitDispatches.Remove(call.UnitDispatches.First(x => x.UnitId == id));
					}

					foreach (var unit in unitsToDispatch)
					{
						if (!call.UnitDispatches.Any(x => x.UnitId == unit))
						{
							if (units.Any(x => x.UnitId == unit))
							{
								var cdu = new CallDispatchUnit { CallId = call.CallId, UnitId = unit };
								call.UnitDispatches.Add(cdu);
							}
						}
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}

			// Call is in the past or is now, were dispatching now (at the end of this func)
			if (call.DispatchOn.HasValue && call.DispatchOn.Value <= DateTime.UtcNow)
				call.HasBeenDispatched = true;

			await _callsService.SaveCallAsync(call, cancellationToken);

			if (editCallInput.RebroadcastCall)
			{
				var cqi = new CallQueueItem();
				cqi.Call = call;

				// If we have any group, unit or role dispatches just bet the farm and all all profiles for now.
				if (cqi.Call.GroupDispatches.Any() || cqi.Call.UnitDispatches.Any() || cqi.Call.RoleDispatches.Any())
					cqi.Profiles = (await _userProfileService.GetAllProfilesForDepartmentAsync(DepartmentId)).Select(x => x.Value).ToList();
				else if (cqi.Call.Dispatches != null && cqi.Call.Dispatches.Any())
					cqi.Profiles = await _userProfileService.GetSelectedUserProfilesAsync(cqi.Call.Dispatches.Select(x => x.UserId).ToList());
				else
					cqi.Profiles = new List<UserProfile>();


				if (cqi.Call.Dispatches.Any() || cqi.Call.GroupDispatches.Any() || cqi.Call.UnitDispatches.Any() || cqi.Call.RoleDispatches.Any())
					await _queueService.EnqueueCallBroadcastAsync(cqi, cancellationToken);
			}

			_eventAggregator.SendMessage<CallUpdatedEvent>(new CallUpdatedEvent() { DepartmentId = DepartmentId, Call = call });

			result.Id = call.CallId.ToString();
			result.PageSize = 0;
			result.Status = ResponseHelper.Updated;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Updates a call's scheduled dispatch time if it has not been dispatched
		/// </summary>
		/// <param name="input">Data to update</param>
		/// <returns></returns>
		[HttpPut("UpdateScheduledDispatchTime")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<ActionResult<UpdateScheduledDispatchTimeResult>> UpdateScheduledDispatchTime(UpdateDispatchTimeInput input)
		{
			var result = new UpdateScheduledDispatchTimeResult();
			var canDoOperation = await _authorizationService.CanUserEditCallAsync(UserId, int.Parse(input.Id));

			if (!canDoOperation)
				return Unauthorized();

			if (!ModelState.IsValid)
				return BadRequest();

			var call = await _callsService.GetCallByIdAsync(int.Parse(input.Id));
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			if (call == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return Ok(result);
			}

			if (call.DepartmentId != DepartmentId)
				return Unauthorized();

			if (call.HasBeenDispatched.HasValue && call.HasBeenDispatched.Value)
				return BadRequest();

			call.DispatchOn = DateTimeHelpers.ConvertToUtc(input.Date, department.TimeZone);
			call.HasBeenDispatched = false;

			var savedCall = await _callsService.SaveCallAsync(call);
			_eventAggregator.SendMessage<CallUpdatedEvent>(new CallUpdatedEvent() { DepartmentId = DepartmentId, Call = savedCall });

			result.Id = savedCall.CallId.ToString();
			result.PageSize = 0;
			result.Status = ResponseHelper.Updated;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Deletes a call
		/// </summary>
		/// <param name="callId">ID of the call</param>
		/// <returns></returns>
		[HttpDelete("DeleteCall")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Call_Delete)]
		public async Task<ActionResult<DeleteCallResult>> DeleteCall(string callId)
		{
			var result = new DeleteCallResult();

			if (String.IsNullOrWhiteSpace(callId))
				return BadRequest();

			var call = await _callsService.GetCallByIdAsync(int.Parse(callId));

			if (call == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return Ok(result);
			}

			var canDoOperation = await _authorizationService.CanUserCloseCallAsync(UserId, int.Parse(callId), DepartmentId);

			if (!canDoOperation)
				return Unauthorized();

			if (call.DepartmentId != DepartmentId)
				return Unauthorized();

			if (call.HasBeenDispatched.HasValue && call.HasBeenDispatched.Value)
				return BadRequest();

			call.IsDeleted = true;
			var savedCall = await _callsService.SaveCallAsync(call);

			_eventAggregator.SendMessage<CallUpdatedEvent>(new CallUpdatedEvent() { DepartmentId = DepartmentId, Call = savedCall });

			result.Id = savedCall.CallId.ToString();
			result.PageSize = 0;
			result.Status = ResponseHelper.Deleted;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Closes a Resgrid call
		/// </summary>
		/// <param name="closeCallInput">Data to close a call</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>OK status code if successful</returns>
		[HttpPut("CloseCall")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<ActionResult<CloseCallResult>> CloseCall([FromBody] CloseCallInput closeCallInput, CancellationToken cancellationToken)
		{
			var result = new CloseCallResult();

			if (!ModelState.IsValid)
				return BadRequest();

			var call = await _callsService.GetCallByIdAsync(int.Parse(closeCallInput.Id));

			if (call == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return Ok(result);
			}

			var canDoOperation = await _authorizationService.CanUserDeleteCallAsync(UserId, int.Parse(closeCallInput.Id), DepartmentId);

			if (!canDoOperation)
				return Unauthorized();

			if (call.DepartmentId != DepartmentId)
				return Unauthorized();

			call.ClosedByUserId = UserId;
			call.ClosedOn = DateTime.UtcNow;
			call.CompletedNotes = closeCallInput.Notes;
			call.State = closeCallInput.Type;

			var savedCall = await _callsService.SaveCallAsync(call, cancellationToken);

			_eventAggregator.SendMessage<CallClosedEvent>(new CallClosedEvent() { DepartmentId = DepartmentId, Call = savedCall });

			result.Id = savedCall.CallId.ToString();
			result.PageSize = 0;
			result.Status = ResponseHelper.Updated;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Returns all the non-dispatched (pending) scheduled calls for the department
		/// </summary>
		/// <returns>Array of CallResult objects for each active call in the department</returns>
		[HttpGet("GetAllPendingScheduledCalls")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<ScheduledCallsResult>> GetAllPendingScheduledCalls()
		{
			var result = new ScheduledCallsResult();

			var calls = (await _callsService.GetAllNonDispatchedScheduledCallsByDepartmentIdAsync(DepartmentId)).OrderBy(x => x.DispatchOn);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			if (calls != null && calls.Any())
			{
				foreach (var c in calls)
				{
					string address = "";
					if (String.IsNullOrWhiteSpace(c.Address) && c.HasValidGeolocationData())
					{
						var geo = c.GeoLocationData.Split(char.Parse(","));

						if (geo.Length == 2)
							address = await _geoLocationProvider.GetAddressFromLatLong(double.Parse(geo[0]), double.Parse(geo[1]));
					}
					else
						address = c.Address;

					result.Data.Add(ConvertCall(c, null, address, TimeZone));
				}
				result.PageSize = result.Data.Count();
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>
		/// Gets all the meta-data around a call, dispatched personnel, units, groups and responses
		/// </summary>
		/// <param name="callId">CallId to get data for</param>
		/// <returns></returns>
		[HttpGet("GetCallHistory")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<CallHistoryResult>> GetCallHistory(int callId)
		{
			var result = new CallHistoryResult();

			var call = await _callsService.GetCallByIdAsync(callId);

			if (call == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return Ok(result);
			}

			if (call.DepartmentId != DepartmentId)
				Unauthorized();

			call = await _callsService.PopulateCallData(call, true, true, true, true, true, true, true, true, true);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			result.Data.Add(new CallHistoryResultData()
			{
				Id = call.CallId.ToString(),
				TimestampUtc = call.LoggedOn,
				Timestamp = call.LoggedOn.TimeConverter(department),
				Type = 0,
				Info = $"Call created"
			});

			if (call.ClosedOn.HasValue)
			{
				result.Data.Add(new CallHistoryResultData()
				{
					Id = call.CallId.ToString(),
					TimestampUtc = call.ClosedOn.Value,
					Timestamp = call.ClosedOn.Value.TimeConverter(department),
					Type = 0,
					Info = $"Call closed"
				});
			}

			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);
			var units = await _unitsService.GetUnitsForDepartmentAsync(call.DepartmentId);
			var unitStates = (await _unitsService.GetUnitStatesForCallAsync(call.DepartmentId, callId)).OrderBy(x => x.UnitId).OrderBy(y => y.Timestamp).ToList();
			var actionLogs = (await _actionLogsService.GetActionLogsForCallAsync(call.DepartmentId, callId)).OrderBy(x => x.UserId).OrderBy(y => y.Timestamp).ToList();
			var names = await _usersService.GetUserGroupAndRolesByDepartmentIdAsync(DepartmentId, true, true, true);
			var priority = await _callsService.GetCallPrioritiesByIdAsync(call.DepartmentId, call.Priority, false);
			var roles = await _personnelRolesService.GetAllRolesForDepartmentAsync(call.DepartmentId);

			var customStates = await _customStateService.GetAllCustomStatesForDepartmentAsync(call.DepartmentId);
			var defaultUnitStatuses = _customStateService.GetDefaultUnitStatuses();
			var defaultUserStatuses = await _customStateService.GetCustomPersonnelStatusesOrDefaultsAsync(call.DepartmentId);


			foreach (var actionLog in actionLogs)
			{
				var nameInfo = names.FirstOrDefault(x => x.UserId == actionLog.UserId);
				CustomStateDetail state = null;
				if (actionLog.ActionTypeId <= 25)
				{
					state = defaultUserStatuses.FirstOrDefault(x => x.CustomStateDetailId == actionLog.ActionTypeId);
				}
				else
				{
					if (customStates != null && customStates.Count > 0)
					{
						state = customStates.Select(state => state.Details.FirstOrDefault(x => x.CustomStateDetailId == actionLog.ActionTypeId)).FirstOrDefault(detail => detail != null);
					}
				}

				if (nameInfo != null)
				{
					result.Data.Add(new CallHistoryResultData()
					{
						Id = actionLog.ActionLogId.ToString(),
						TimestampUtc = actionLog.Timestamp,
						Timestamp = actionLog.Timestamp.TimeConverter(department),
						Type = 2,
						Info = $"{nameInfo.LastName},{nameInfo.FirstName} set status to {state.ButtonText} at {actionLog.GeoLocationData}"
					});
				}
			}

			foreach (var unitLog in unitStates)
			{
				CustomStateDetail state = null;
				if (unitLog.UnitStateId <= 12)
				{
					state = defaultUnitStatuses.FirstOrDefault(x => x.CustomStateDetailId == unitLog.UnitStateId);
				}
				else
				{
					if (customStates != null && customStates.Count > 0)
					{
						state = customStates.Select(state => state.Details.FirstOrDefault(x => x.CustomStateDetailId == unitLog.State)).FirstOrDefault(detail => detail != null);
					}
				}

				result.Data.Add(new CallHistoryResultData()
				{
					Id = unitLog.UnitStateId.ToString(),
					TimestampUtc = unitLog.Timestamp,
					Timestamp = unitLog.Timestamp.TimeConverter(department),
					Type = 3,
					Info = $"{unitLog.Unit.Name} set status to {state.ButtonText} at {unitLog.GeoLocationData}"
				});
			}

			foreach (var dispatch in call.Dispatches)
			{
				var nameInfo = names.FirstOrDefault(x => x.UserId == dispatch.UserId);

				if (nameInfo != null)
				{
					result.Data.Add(new CallHistoryResultData()
					{
						TimestampUtc = call.LoggedOn.Add(TimeSpan.FromSeconds(30)),
						Timestamp = call.LoggedOn.Add(TimeSpan.FromSeconds(30)).TimeConverter(department),
						Type = 0,
						Info = $"{nameInfo.LastName}, {nameInfo.FirstName} was dispatched to the call"
					});
				}
			}

			if (call.GroupDispatches != null && call.GroupDispatches.Any())
			{
				foreach (var groupDispatch in call.GroupDispatches)
				{
					var name = groups.FirstOrDefault(x => x.DepartmentGroupId == groupDispatch.DepartmentGroupId);
					if (name != null)
					{
						result.Data.Add(new CallHistoryResultData()
						{
							TimestampUtc = call.LoggedOn.Add(TimeSpan.FromSeconds(30)),
							Timestamp = call.LoggedOn.Add(TimeSpan.FromSeconds(30)).TimeConverter(department),
							Type = 0,
							Info = $"Group {name.Name} was dispatched to the call"
						});

					}
				}
			}

			if (call.UnitDispatches != null && call.UnitDispatches.Any())
			{
				foreach (var unitDispatch in call.UnitDispatches)
				{
					var unit = units.FirstOrDefault(x => x.UnitId == unitDispatch.UnitId);
					if (unit != null)
					{
						result.Data.Add(new CallHistoryResultData()
						{
							TimestampUtc = call.LoggedOn.Add(TimeSpan.FromSeconds(30)),
							Timestamp = call.LoggedOn.Add(TimeSpan.FromSeconds(30)).TimeConverter(department),
							Type = 0,
							Info = $"Unit {unit.Name} was dispatched to the call"
						});
					}
				}
			}

			if (call.RoleDispatches != null && call.RoleDispatches.Any())
			{
				foreach (var roleDispatch in call.RoleDispatches)
				{
					var role = roles.FirstOrDefault(x => x.PersonnelRoleId == roleDispatch.RoleId);
					if (role != null)
					{
						result.Data.Add(new CallHistoryResultData()
						{
							TimestampUtc = call.LoggedOn.Add(TimeSpan.FromSeconds(30)),
							Timestamp = call.LoggedOn.Add(TimeSpan.FromSeconds(30)).TimeConverter(department),
							Type = 0,
							Info = $"Role {role.Name} was dispatched to the call"
						});
					}
				}
			}

			if (call.CallNotes != null && call.CallNotes.Any())
			{
				foreach (var note in call.CallNotes)
				{
					var nameInfo = names.FirstOrDefault(x => x.UserId == note.UserId);

					if (nameInfo != null)
					{
						result.Data.Add(new CallHistoryResultData()
						{
							TimestampUtc = note.Timestamp,
							Timestamp = note.Timestamp.TimeConverter(department),
							Type = 1,
							Info = $"{nameInfo.LastName}, {nameInfo.FirstName} added note '{note.Note}'"
						});
					}
				}
			}

			result.PageSize = 1;
			result.Status = ResponseHelper.Success;

			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Returns all the calls for the department inclusive in the date range
		/// </summary>
		/// <param name="startDate">Start date as UTC to get calls for</param>
		/// <param name="endDate">End date as UTC to get calls for</param>
		/// <returns>Array of CallResult objects for each call in the department within the range</returns>
		[HttpGet("GetCalls")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<ActiveCallsResult>> GetCalls(DateTime startDate, DateTime endDate)
		{
			var result = new ActiveCallsResult();

			var calls = (await _callsService.GetAllCallsByDepartmentDateRangeAsync(DepartmentId, startDate, endDate)).OrderByDescending(x => x.LoggedOn);

			if (calls != null && calls.Any())
			{
				foreach (var c in calls)
				{
					var callWithData = await _callsService.PopulateCallData(c, false, true, true, false, false, false, true, true, true);

					string address = "";
					if (String.IsNullOrWhiteSpace(c.Address) && c.HasValidGeolocationData())
					{
						var geo = c.GeoLocationData.Split(char.Parse(","));

						if (geo.Length == 2)
						{
							double lat, lng;
							if (double.TryParse(geo[0], out lat) && double.TryParse(geo[1], out lng))
							{
								address = await _geoLocationProvider.GetAddressFromLatLong(lat, lng);
							}
						}
					}
					else
						address = c.Address;

					result.Data.Add(ConvertCall(callWithData, null, address, TimeZone));
				}
				result.PageSize = result.Data.Count();
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		public static CallResultData ConvertCall(Call call, List<DispatchProtocol> protocol, string geoLocationAddress, string timeZone)
		{
			var callResult = new CallResultData();

			callResult.CallId = call.CallId.ToString();
			callResult.Priority = call.Priority;
			callResult.Name = StringHelpers.SanitizeHtmlInString(call.Name);

			if (!String.IsNullOrWhiteSpace(call.NatureOfCall))
				callResult.Nature = StringHelpers.SanitizeHtmlInString(call.NatureOfCall);

			if (!String.IsNullOrWhiteSpace(call.Notes))
				callResult.Note = StringHelpers.SanitizeHtmlInString(call.Notes);

			if (call.CallNotes != null)
				callResult.NotesCount = call.CallNotes.Count();
			else
				callResult.NotesCount = 0;

			if (call.Attachments != null)
			{
				callResult.AudioCount = call.Attachments.Count(x => x.CallAttachmentType == (int)CallAttachmentTypes.DispatchAudio);
				callResult.ImgagesCount = call.Attachments.Count(x => x.CallAttachmentType == (int)CallAttachmentTypes.Image);
				callResult.FileCount = call.Attachments.Count(x => x.CallAttachmentType == (int)CallAttachmentTypes.File);
			}
			else
			{
				callResult.AudioCount = 0;
				callResult.ImgagesCount = 0;
				callResult.FileCount = 0;
			}

			if (!String.IsNullOrWhiteSpace(geoLocationAddress))
				callResult.Address = geoLocationAddress;
			else
				callResult.Address = call.Address;

			callResult.Geolocation = call.GeoLocationData;
			callResult.LoggedOn = call.LoggedOn.TimeConverter(new Department() { TimeZone = timeZone });
			callResult.LoggedOnUtc = call.LoggedOn;
			callResult.State = call.State;
			callResult.Number = call.Number;

			if (call.DispatchOn.HasValue)
			{
				callResult.DispatchedOnUtc = call.DispatchOn.Value;
				callResult.DispatchedOn = call.DispatchOn.Value.TimeConverter(new Department() { TimeZone = timeZone });
			}

			callResult.What3Words = call.W3W;
			callResult.ContactName = call.ContactName;
			callResult.ContactInfo = call.ContactNumber;
			callResult.ReferenceId = call.ReferenceNumber;
			callResult.ExternalId = call.ExternalIdentifier;
			callResult.IncidentId = call.IncidentNumber;
			callResult.Type = call.Type;

			callResult.Protocols = new List<CallProtocolResultData>();
			if (protocol != null && protocol.Any())
			{
				foreach (var callProtocol in protocol)
				{
					callResult.Protocols.Add(CallProtocolsController.ConvertProtocolData(callProtocol));
				}
			}

			return callResult;
		}
	}
}
