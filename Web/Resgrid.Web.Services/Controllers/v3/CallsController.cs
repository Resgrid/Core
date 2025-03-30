using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.IO;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Resgrid.Framework;
using Resgrid.Model.Events;
using Resgrid.Model.Queue;
using Resgrid.Providers.Bus;
using Resgrid.Web.Services.Controllers.Version3.Models.Calls;
using Resgrid.Web.Services.Controllers.Version3.Models.CallPriorities;
using Resgrid.Web.Services.Controllers.Version3.Models.Protocols;
using Resgrid.Web.ServicesCore.Options;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;
using Resgrid.Web.Helpers;
using System.Text;

namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Operations to be performed against calls
	/// </summary>
	[Route("api/v{version:ApiVersion}/[controller]")]
	[Produces("application/json")]
	[ApiVersion("3.0")]
	[ApiExplorerSettings(GroupName = "v3")]
	public class CallsController : V3AuthenticatedApiControllerbase
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
		private readonly IOptions<AppOptions> _appSettingsAccessor;
		private readonly IEventAggregator _eventAggregator;

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
			IOptions<AppOptions> appSettingsAccessor,
			IEventAggregator eventAggregator
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
			_appSettingsAccessor = appSettingsAccessor;
			_eventAggregator = eventAggregator;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Returns all the active calls for the department
		/// </summary>
		/// <returns>Array of CallResult objects for each active call in the department</returns>
		[HttpGet("GetActiveCalls")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status500InternalServerError)]
		public async Task<ActionResult<List<CallResult>>> GetActiveCalls()
		{
			var result = new List<CallResult>();

			var calls = (await _callsService.GetActiveCallsByDepartmentAsync(DepartmentId)).OrderByDescending(x => x.LoggedOn);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			foreach (var c in calls)
			{
				var call = new CallResult();

				call.Cid = c.CallId;
				call.Pri = c.Priority;
				call.Ctl = c.IsCritical;
				call.Nme = StringHelpers.SanitizeHtmlInString(c.Name);

				if (!String.IsNullOrWhiteSpace(c.NatureOfCall))
					call.Noc = StringHelpers.SanitizeHtmlInString(c.NatureOfCall);

				call.Map = c.MapPage;

				if (!String.IsNullOrWhiteSpace(c.Notes))
					call.Not = StringHelpers.SanitizeHtmlInString(c.Notes);

				if (c.CallNotes != null)
					call.Nts = c.CallNotes.Count();
				else
					call.Nts = 0;

				if (c.Attachments != null)
				{
					call.Aud = c.Attachments.Count(x => x.CallAttachmentType == (int)CallAttachmentTypes.DispatchAudio);
					call.Img = c.Attachments.Count(x => x.CallAttachmentType == (int)CallAttachmentTypes.Image);
					call.Fls = c.Attachments.Count(x => x.CallAttachmentType == (int)CallAttachmentTypes.File);
				}
				else
				{
					call.Aud = 0;
					call.Img = 0;
					call.Fls = 0;
				}

				if (String.IsNullOrWhiteSpace(c.Address) && c.HasValidGeolocationData())
				{
					var geo = c.GeoLocationData.Split(char.Parse(","));

					if (geo.Length == 2)
						call.Add = await _geoLocationProvider.GetAddressFromLatLong(double.Parse(geo[0]), double.Parse(geo[1]));
				}
				else
					call.Add = c.Address;

				call.Geo = c.GeoLocationData;
				call.Lon = c.LoggedOn.TimeConverter(department);
				call.Utc = c.LoggedOn;
				call.Ste = c.State;
				call.Num = c.Number;

				result.Add(call);
			}

			return Ok(result);
		}

		/// <summary>
		/// Returns all the active calls for the department
		/// </summary>
		/// <returns>Array of CallResult objects for each active call in the department</returns>
		[HttpGet("GetActiveCallsEx")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status500InternalServerError)]
		public async Task<ActionResult<List<CallResultEx>>> GetActiveCallsEx()
		{
			var result = new List<CallResultEx>();

			var calls = (await _callsService.GetActiveCallsByDepartmentAsync(DepartmentId)).OrderByDescending(x => x.LoggedOn);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			foreach (var c in calls)
			{
				var call = new CallResultEx();
				call.Protocols = new List<CallProtocolResult>();

				call.Cid = c.CallId;
				call.Pri = c.Priority;
				call.Ctl = c.IsCritical;
				call.Nme = StringHelpers.SanitizeHtmlInString(c.Name);

				if (!String.IsNullOrWhiteSpace(c.NatureOfCall))
					call.Noc = StringHelpers.SanitizeHtmlInString(c.NatureOfCall);

				call.Map = c.MapPage;

				if (!String.IsNullOrWhiteSpace(c.Notes))
					call.Not = StringHelpers.SanitizeHtmlInString(c.Notes);

				if (c.CallNotes != null)
					call.Nts = c.CallNotes.Count();
				else
					call.Nts = 0;

				if (c.Attachments != null)
				{
					call.Aud = c.Attachments.Count(x => x.CallAttachmentType == (int)CallAttachmentTypes.DispatchAudio);
					call.Img = c.Attachments.Count(x => x.CallAttachmentType == (int)CallAttachmentTypes.Image);
					call.Fls = c.Attachments.Count(x => x.CallAttachmentType == (int)CallAttachmentTypes.File);
				}
				else
				{
					call.Aud = 0;
					call.Img = 0;
					call.Fls = 0;
				}

				if (String.IsNullOrWhiteSpace(c.Address) && c.HasValidGeolocationData())
				{
					var geo = c.GeoLocationData.Split(char.Parse(","));

					if (geo.Length == 2)
						call.Add = await _geoLocationProvider.GetAddressFromLatLong(double.Parse(geo[0]), double.Parse(geo[1]));
				}
				else
					call.Add = c.Address;

				call.Geo = c.GeoLocationData;
				call.Lon = c.LoggedOn.TimeConverter(department);
				call.Utc = c.LoggedOn;
				call.Ste = c.State;
				call.Num = c.Number;

				if (c.Protocols != null && c.Protocols.Any())
				{
					foreach (var protocol in c.Protocols)
					{
						var p = new CallProtocolResult();
						p.Id = protocol.DispatchProtocolId;
						p.Code = protocol.Protocol.Code;
						p.Name = protocol.Protocol.Name;

						call.Protocols.Add(p);
					}
				}

				result.Add(call);
			}

			return Ok(result);
		}

		/// <summary>
		/// Returns all the active calls for the department (extended object result, more verbose then GetActiveCalls)
		/// </summary>
		/// <returns>Array of DepartmentCallResult objects for each active call in the department</returns>
		[HttpGet("GetActiveCallsForDepartment")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status500InternalServerError)]
		public async Task<ActionResult<List<DepartmentCallResult>>> GetActiveCallsForDepartment(int departmentId)
		{
			var result = new List<DepartmentCallResult>();

			if (departmentId != DepartmentId && !IsSystem)
				Unauthorized();

			var calls = (await _callsService.GetActiveCallsByDepartmentAsync(departmentId)).OrderByDescending(x => x.LoggedOn);
			var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);

			foreach (var c in calls)
			{
				var call = new DepartmentCallResult();

				call.Name = StringHelpers.SanitizeHtmlInString(c.Name);

				if (!String.IsNullOrWhiteSpace(c.NatureOfCall))
					call.NatureOfCall = StringHelpers.SanitizeHtmlInString(c.NatureOfCall);

				if (!String.IsNullOrWhiteSpace(c.Notes))
					call.Notes = StringHelpers.SanitizeHtmlInString(c.Notes);

				call.LoggedOn = c.LoggedOn.TimeConverter(department);
				call.LoggedOnUtc = c.LoggedOn;
				call.CallId = c.CallId;
				call.Number = c.Number;
				call.DepartmentId = c.DepartmentId;
				call.ReportingUserId = c.ReportingUserId;
				call.Priority = c.Priority;
				call.IsCritical = c.IsCritical;
				call.Type = c.Type;
				call.IncidentNumber = c.IncidentNumber;
				call.MapPage = c.MapPage;
				call.CompletedNotes = c.CompletedNotes;
				call.Address = c.Address;
				call.GeoLocationData = c.GeoLocationData;
				call.ClosedByUserId = c.ClosedByUserId;
				call.ClosedOn = c.ClosedOn;
				call.State = c.State;
				call.IsDeleted = c.IsDeleted;
				call.CallSource = c.CallSource;
				call.DispatchCount = c.DispatchCount;
				call.LastDispatchedOn = c.LastDispatchedOn;
				call.SourceIdentifier = c.SourceIdentifier;
				call.ContactName = c.ContactName;
				call.ContactNumber = c.ContactNumber;
				call.Public = c.Public;
				call.ExternalIdentifier = c.ExternalIdentifier;
				call.ReferenceNumber = c.ReferenceNumber;

				if (c.Dispatches != null)
				{
					foreach (var dispatch in c.Dispatches)
					{
						var dispatchResult = new DepartmentCallDispatchResult();
						dispatchResult.CallDispatchId = dispatch.CallDispatchId;
						dispatchResult.CallId = dispatch.CallId;
						dispatchResult.UserId = dispatch.UserId;
						dispatchResult.GroupId = dispatch.GroupId;
						dispatchResult.DispatchCount = dispatch.DispatchCount;
						dispatchResult.LastDispatchedOn = dispatch.LastDispatchedOn;
						dispatchResult.ActionLogId = dispatch.ActionLogId;

						call.Dispatches.Add(dispatchResult);
					}
				}

				if (c.GroupDispatches != null)
				{
					foreach (var dispatch in c.GroupDispatches)
					{
						var dispatchResult = new DepartmentCallDispatchGroupResult();
						dispatchResult.CallDispatchGroupId = dispatch.CallDispatchGroupId;
						dispatchResult.CallId = dispatch.CallId;
						dispatchResult.DepartmentGroupId = dispatch.DepartmentGroupId;
						dispatchResult.DispatchCount = dispatch.DispatchCount;
						dispatchResult.LastDispatchedOn = dispatch.LastDispatchedOn;

						call.GroupDispatches.Add(dispatchResult);
					}
				}

				if (c.UnitDispatches != null)
				{
					foreach (var dispatch in c.UnitDispatches)
					{
						var dispatchResult = new DepartmentCallDispatchUnitResult();
						dispatchResult.CallDispatchUnitId = dispatch.CallDispatchUnitId;
						dispatchResult.CallId = dispatch.CallId;
						dispatchResult.UnitId = dispatch.UnitId;
						dispatchResult.DispatchCount = dispatch.DispatchCount;
						dispatchResult.LastDispatchedOn = dispatch.LastDispatchedOn;

						call.UnitDispatches.Add(dispatchResult);
					}
				}

				if (c.RoleDispatches != null)
				{
					foreach (var dispatch in c.RoleDispatches)
					{
						var dispatchResult = new DepartmentCallDispatchRoleResult();
						dispatchResult.CallDispatchRoleId = dispatch.CallDispatchRoleId;
						dispatchResult.CallId = dispatch.CallId;
						dispatchResult.RoleId = dispatch.RoleId;
						dispatchResult.DispatchCount = dispatch.DispatchCount;
						dispatchResult.LastDispatchedOn = dispatch.LastDispatchedOn;

						call.RoleDispatches.Add(dispatchResult);
					}
				}

				result.Add(call);
			}

			return Ok(result);
		}

		/// <summary>
		/// Returns a specific call from the Resgrid System
		/// </summary>
		/// <returns>CallResult of the call in the Resgrid system</returns>
		[HttpGet("GetCall")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status500InternalServerError)]
		public async Task<ActionResult<CallResult>> GetCall(int callId)
		{
			var c = await _callsService.GetCallByIdAsync(callId);
			c = await _callsService.PopulateCallData(c, false, true, true, false, false, false, false, true, true);

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			var call = new CallResult();

			call.Cid = c.CallId;
			call.Pri = c.Priority;
			call.Ctl = c.IsCritical;
			call.Nme = c.Name;
			call.Noc = c.NatureOfCall;
			call.Map = c.MapPage;
			call.Not = c.Notes;
			call.Eid = c.ExternalIdentifier;
			call.Rnm = c.ContactName;
			call.Rci = c.ContactNumber;
			call.Rid = c.ReferenceNumber;
			call.Typ = c.Type;

			if (c.CallNotes != null)
				call.Nts = c.CallNotes.Count();
			else
				call.Nts = 0;

			if (c.Attachments != null)
			{
				call.Aud = c.Attachments.Count(x => x.CallAttachmentType == (int)CallAttachmentTypes.DispatchAudio);
				call.Img = c.Attachments.Count(x => x.CallAttachmentType == (int)CallAttachmentTypes.Image);
				call.Fls = c.Attachments.Count(x => x.CallAttachmentType == (int)CallAttachmentTypes.File);

				if (call.Aud > 0)
				{
					var audio = c.Attachments.FirstOrDefault(x => x.CallAttachmentType == (int)CallAttachmentTypes.DispatchAudio);

					if (audio != null)
						call.Aid = SymmetricEncryption.Encrypt(audio.CallAttachmentId.ToString(), Config.SystemBehaviorConfig.ExternalAudioUrlParamPasshprase);
				}
			}
			else
			{
				call.Aud = 0;
				call.Img = 0;
				call.Fls = 0;
			}

			if (String.IsNullOrWhiteSpace(c.Address) && c.HasValidGeolocationData())
			{
				var geo = c.GeoLocationData.Split(char.Parse(","));

				if (geo.Length == 2)
				{
					call.Add = await _geoLocationProvider.GetAddressFromLatLong(double.Parse(geo[0]), double.Parse(geo[1]));
					call.Gla = geo[0].Trim();
					call.Glo = geo[1].Trim();
				}
			}
			else
				call.Add = c.Address;

			call.Geo = c.GeoLocationData;
			call.Lon = c.LoggedOn.TimeConverter(department);
			call.Utc = c.LoggedOn;
			call.Ste = c.State;
			call.Num = c.Number;

			if (c.DispatchOn.HasValue)
				call.Don = c.DispatchOn.Value.TimeConverter(department);

			if (!String.IsNullOrWhiteSpace(c.W3W))
				call.w3w = c.W3W;


			return Ok(call);
		}

		/// <summary>
		/// Gets all the meta-data around a call, dispatched personnel, units, groups and responses
		/// </summary>
		/// <param name="callId">CallId to get data for</param>
		/// <returns></returns>
		[HttpGet("GetCallExtraData")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status500InternalServerError)]
		public async Task<ActionResult<CallDataResult>> GetCallExtraData(int callId)
		{
			var result = new CallDataResult();

			var call = await _callsService.GetCallByIdAsync(callId);

			if (call.DepartmentId != DepartmentId)
				Unauthorized();

			call = await _callsService.PopulateCallData(call, true, true, true, true, true, true, true, true, true);

			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);
			var units = await _unitsService.GetUnitsForDepartmentAsync(call.DepartmentId);
			var unitStates = (await _unitsService.GetUnitStatesForCallAsync(call.DepartmentId, callId)).OrderBy(x => x.UnitId).OrderBy(y => y.Timestamp).ToList();
			var actionLogs = (await _actionLogsService.GetActionLogsForCallAsync(call.DepartmentId, callId)).OrderBy(x => x.UserId).OrderBy(y => y.Timestamp).ToList();
			var names = await _usersService.GetUserGroupAndRolesByDepartmentIdAsync(DepartmentId, true, true, true);
			var priority = await _callsService.GetCallPrioritiesByIdAsync(call.DepartmentId, call.Priority, false);
			var roles = await _personnelRolesService.GetAllRolesForDepartmentAsync(call.DepartmentId);

			if (priority != null)
			{
				result.Priority = new CallPriorityDataResult();
				result.Priority.Id = priority.DepartmentCallPriorityId;
				result.Priority.DepartmentId = priority.DepartmentId;
				result.Priority.Name = priority.Name;
				result.Priority.Color = priority.Color;
				result.Priority.Sort = priority.Sort;
				result.Priority.IsDeleted = priority.IsDeleted;
				result.Priority.IsDefault = priority.IsDefault;
				result.Priority.DispatchPersonnel = priority.DispatchPersonnel;
				result.Priority.DispatchUnits = priority.DispatchUnits;
				result.Priority.ForceNotifyAllPersonnel = priority.ForceNotifyAllPersonnel;
				result.Priority.Tone = priority.Tone;
				result.Priority.IsSystemPriority = priority.IsSystemPriority;
			}

			foreach (var actionLog in actionLogs)
			{
				var eventResult = new DispatchedEventResult();
				eventResult.Id = actionLog.ActionLogId.ToString();
				eventResult.Timestamp = actionLog.Timestamp;
				eventResult.Type = "User";

				var name = names.FirstOrDefault(x => x.UserId == actionLog.UserId);
				if (name != null)
				{
					eventResult.Name = name.Name;

					if (name.DepartmentGroupId.HasValue)
					{
						eventResult.GroupId = name.DepartmentGroupId.Value;
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

				result.Activity.Add(eventResult);
			}

			foreach (var unitLog in unitStates)
			{
				var eventResult = new DispatchedEventResult();
				eventResult.Id = unitLog.UnitStateId.ToString();
				eventResult.Timestamp = unitLog.Timestamp;
				eventResult.Type = "Unit";
				eventResult.Name = unitLog.Unit.Name;

				var group = groups.FirstOrDefault(x => x.DepartmentGroupId == unitLog.Unit.StationGroupId);
				if (group != null)
				{
					eventResult.GroupId = group.DepartmentGroupId;
					eventResult.Group = group.Name;
				}

				eventResult.StatusId = eventResult.StatusId;
				eventResult.Location = eventResult.Location;
				eventResult.Note = eventResult.Note;

				result.Activity.Add(eventResult);
			}

			foreach (var dispatch in call.Dispatches)
			{
				var eventResult = new DispatchedEventResult();
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
						eventResult.GroupId = name.DepartmentGroupId.Value;
						eventResult.Group = name.DepartmentGroupName;
					}
				}
				else
				{
					eventResult.Name = "Unknown User";
				}

				result.Dispatches.Add(eventResult);
			}

			if (call.GroupDispatches != null && call.GroupDispatches.Any())
			{
				foreach (var groupDispatch in call.GroupDispatches)
				{
					var eventResult = new DispatchedEventResult();
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
						eventResult.GroupId = name.DepartmentGroupId;
						eventResult.Group = name.Name;

					}
					else
					{
						eventResult.Name = "Unknown Group";
					}

					result.Dispatches.Add(eventResult);
				}
			}

			if (call.UnitDispatches != null && call.UnitDispatches.Any())
			{
				foreach (var unitDispatch in call.UnitDispatches)
				{
					var eventResult = new DispatchedEventResult();
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
								eventResult.GroupId = group.DepartmentGroupId;
								eventResult.Group = group.Name;

							}
						}

					}
					else
					{
						eventResult.Name = "Unknown Unit";
					}

					result.Dispatches.Add(eventResult);
				}
			}

			if (call.RoleDispatches != null && call.RoleDispatches.Any())
			{
				foreach (var roleDispatch in call.RoleDispatches)
				{
					var eventResult = new DispatchedEventResult();
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

					result.Dispatches.Add(eventResult);
				}
			}

			if (call.Protocols != null && call.Protocols.Any())
			{
				foreach (var callProtocol in call.Protocols)
				{
					var protocol = await _protocolsService.GetProtocolByIdAsync(callProtocol.CallProtocolId);
					var protocolResult = ProtocolResult.Convert(protocol);

					if (protocolResult != null)
						result.Protocols.Add(protocolResult);
				}
			}

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
		public async Task<ActionResult> SaveCall([FromBody] NewCallInput newCallInput, CancellationToken cancellationToken)
		{
			if (!ModelState.IsValid)
				return BadRequest();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			var call = new Call
			{
				DepartmentId = DepartmentId,
				ReportingUserId = UserId,
				Priority = (int)Enum.Parse(typeof(CallPriority), newCallInput.Pri),
				Name = newCallInput.Nme,
				NatureOfCall = newCallInput.Noc
			};

			if (!string.IsNullOrWhiteSpace(newCallInput.CNme))
				call.ContactName = newCallInput.CNme;

			if (!string.IsNullOrWhiteSpace(newCallInput.CNum))
				call.ContactName = newCallInput.CNum;

			if (!string.IsNullOrWhiteSpace(newCallInput.EId))
				call.ExternalIdentifier = newCallInput.EId;

			if (!string.IsNullOrWhiteSpace(newCallInput.InI))
				call.IncidentNumber = newCallInput.InI;

			if (!string.IsNullOrWhiteSpace(newCallInput.RId))
				call.ReferenceNumber = newCallInput.RId;

			if (!string.IsNullOrWhiteSpace(newCallInput.Add))
				call.Address = newCallInput.Add;

			if (!string.IsNullOrWhiteSpace(newCallInput.W3W))
				call.W3W = newCallInput.W3W;

			if (!string.IsNullOrWhiteSpace(newCallInput.Cfd))
				call.CallFormData = newCallInput.Cfd;

			if (newCallInput.Don.HasValue)
			{
				call.DispatchOn = newCallInput.Don.Value;

				call.DispatchOn = DateTimeHelpers.ConvertToUtc(newCallInput.Don.Value, department.TimeZone);
				call.HasBeenDispatched = false;
			}

			//if (call.Address.Equals("Current Coordinates", StringComparison.InvariantCultureIgnoreCase))
			//	call.Address = "";

			if (!string.IsNullOrWhiteSpace(newCallInput.Not))
				call.Notes = newCallInput.Not;

			if (!string.IsNullOrWhiteSpace(newCallInput.Geo))
				call.GeoLocationData = newCallInput.Geo;

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

			if (!String.IsNullOrWhiteSpace(newCallInput.Typ) && newCallInput.Typ != "No Type")
			{
				var callTypes = await _callsService.GetCallTypesForDepartmentAsync(DepartmentId);
				var type = callTypes.FirstOrDefault(x => x.Type == newCallInput.Typ);

				if (type != null)
				{
					call.Type = type.Type;
				}
			}
			var users = await _departmentsService.GetAllUsersForDepartmentAsync(DepartmentId);
			call.Dispatches = new Collection<CallDispatch>();
			call.GroupDispatches = new List<CallDispatchGroup>();
			call.RoleDispatches = new List<CallDispatchRole>();

			if (newCallInput.Dis == "0")
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
				var dispatch = newCallInput.Dis.Split(char.Parse("|"));

				try
				{
					var usersToDispatch = dispatch.Where(x => x.StartsWith("P:")).Select(y => y.Replace("P:", ""));
					foreach (var user in usersToDispatch)
					{
						var cd = new CallDispatch { UserId = user };
						call.Dispatches.Add(cd);
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
						var cd = new CallDispatchGroup { DepartmentGroupId = group };
						call.GroupDispatches.Add(cd);
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
						var cd = new CallDispatchRole { RoleId = role };
						call.RoleDispatches.Add(cd);
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
						var cdu = new CallDispatchUnit { UnitId = unit };
						call.UnitDispatches.Add(cdu);
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

			var savedCall = await _callsService.SaveCallAsync(call, cancellationToken);

			//OutboundEventProvider handler = new OutboundEventProvider.CallAddedTopicHandler();
			//OutboundEventProvider..Handle(new CallAddedEvent() { DepartmentId = DepartmentId, Call = savedCall });
			_eventAggregator.SendMessage<CallAddedEvent>(new CallAddedEvent() { DepartmentId = DepartmentId, Call = savedCall });

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

			return CreatedAtAction(nameof(SaveCall), new { id = savedCall.CallId }, savedCall);
		}

		/// <summary>
		/// Adds a new call into Resgrid and Dispatches the call
		/// </summary>
		/// <param name="callInput">Call data to add into the system</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>AddCallInput.</returns>
		[HttpPost("AddCall")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		[Obsolete]
		public async Task<ActionResult<AddCallInput>> AddCall([FromBody] AddCallInput callInput, CancellationToken cancellationToken)
		{
			try
			{

				var call = new Call
				{
					DepartmentId = DepartmentId,
					ReportingUserId = UserId,
					Priority = callInput.Priority,
					Name = callInput.Name,
					NatureOfCall = callInput.NatureOfCall,
					Number = callInput.Number,
					IsCritical = callInput.IsCritical,
					IncidentNumber = callInput.IncidentNumber,
					MapPage = callInput.MapPage,
					Notes = callInput.Notes,
					CompletedNotes = callInput.CompletedNotes,
					Address = callInput.Address,
					GeoLocationData = callInput.GeoLocationData,
					LoggedOn = callInput.LoggedOn,
					ClosedByUserId = callInput.ClosedByUserId,
					ClosedOn = callInput.ClosedOn,
					State = callInput.State,
					IsDeleted = callInput.IsDeleted,
					CallSource = callInput.CallSource,
					DispatchCount = callInput.DispatchCount,
					LastDispatchedOn = callInput.LastDispatchedOn,
					SourceIdentifier = callInput.SourceIdentifier,
					W3W = callInput.W3W,
					ContactName = callInput.ContactName,
					ContactNumber = callInput.ContactNumber,
					Public = callInput.Public,
					ExternalIdentifier = callInput.ExternalIdentifier,
					ReferenceNumber = callInput.ReferenceNumber
				};

				if (!string.IsNullOrWhiteSpace(call.GeoLocationData) && !string.IsNullOrWhiteSpace(call.Address))
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

				if (!String.IsNullOrWhiteSpace(callInput.Type) && callInput.Type != "No Type")
				{
					var callTypes = await _callsService.GetCallTypesForDepartmentAsync(DepartmentId);
					var type = callTypes.FirstOrDefault(x => x.Type == callInput.Type);

					if (type != null)
					{
						call.Type = type.Type;
					}
				}

				call.Dispatches = new List<CallDispatch>();
				call.GroupDispatches = new List<CallDispatchGroup>();
				call.RoleDispatches = new List<CallDispatchRole>();

				List<string> groupUserIds = new List<string>();

				var users = await _departmentsService.GetAllUsersForDepartmentAsync(DepartmentId);
				if (callInput.AllCall)
				{
					foreach (var u in users)
					{
						var cd = new CallDispatch { UserId = u.UserId };

						call.Dispatches.Add(cd);
					}
				}
				else
				{
					if (callInput.GroupCodesToDispatch != null && callInput.GroupCodesToDispatch.Count > 0)
					{
						var allGroups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId);

						foreach (var groupCode in callInput.GroupCodesToDispatch)
						{
							var groupsToDispatch = allGroups.FirstOrDefault(x => x.DispatchEmail == groupCode);

							if (groupsToDispatch != null)
							{
								var cd = new CallDispatchGroup { DepartmentGroupId = groupsToDispatch.DepartmentGroupId };
								call.GroupDispatches.Add(cd);

								if (groupsToDispatch.Members != null && groupsToDispatch.Members.Any())
								{
									foreach (var departmentGroupMember in groupsToDispatch.Members)
									{
										if (!groupUserIds.Contains(departmentGroupMember.UserId))
											groupUserIds.Add(departmentGroupMember.UserId);
									}
								}
							}
						}
					}
				}

				if (callInput.Attachments != null && callInput.Attachments.Any())
				{
					call.Attachments = new List<CallAttachment>();

					foreach (var attachment in callInput.Attachments)
					{
						var newAttachment = new CallAttachment();
						newAttachment.Data = attachment.Data;
						newAttachment.Timestamp = DateTime.UtcNow;
						newAttachment.FileName = attachment.FileName;
						newAttachment.Size = attachment.Size;
						newAttachment.CallAttachmentType = attachment.CallAttachmentType;

						call.Attachments.Add(newAttachment);
					}
				}

				var savedCall = await _callsService.SaveCallAsync(call, cancellationToken);


				//OutboundEventProvider.CallAddedTopicHandler handler = new OutboundEventProvider.CallAddedTopicHandler();
				//await handler.Handle(new CallAddedEvent() { DepartmentId = DepartmentId, Call = savedCall });
				_eventAggregator.SendMessage<CallAddedEvent>(new CallAddedEvent() { DepartmentId = DepartmentId, Call = savedCall });

				var cqi = new CallQueueItem();
				cqi.Call = savedCall;

				if (cqi.Call.Dispatches != null && cqi.Call.Dispatches.Any())
				{
					cqi.Profiles =
						await _userProfileService.GetSelectedUserProfilesAsync(cqi.Call.Dispatches.Select(x => x.UserId).ToList());
				}
				else
				{
					if (groupUserIds.Any())
						cqi.Profiles = await _userProfileService.GetSelectedUserProfilesAsync(groupUserIds);
				}

				await _queueService.EnqueueCallBroadcastAsync(cqi, cancellationToken);

				callInput.CallId = savedCall.CallId;
				callInput.Number = savedCall.Number;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				throw ex;
			}

			return Ok(callInput);
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
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult> CloseCall([FromBody] CloseCallInput closeCallInput, CancellationToken cancellationToken)
		{
			if (!ModelState.IsValid)
				return BadRequest();

			var call = await _callsService.GetCallByIdAsync(closeCallInput.Id);

			if (call == null)
				return NotFound();

			call.ClosedByUserId = UserId;
			call.ClosedOn = DateTime.UtcNow;
			call.CompletedNotes = closeCallInput.Msg;
			call.State = (int)closeCallInput.Typ;

			await _callsService.SaveCallAsync(call, cancellationToken);

			//OutboundEventProvider.CallAddedTopicHandler handler = new OutboundEventProvider.CallAddedTopicHandler();
			//await handler.Handle(new CallAddedEvent() { DepartmentId = DepartmentId, Call = call });
			_eventAggregator.SendMessage<CallAddedEvent>(new CallAddedEvent() { DepartmentId = DepartmentId, Call = call });

			return Ok();
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
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		public async Task<ActionResult> EditCall([FromBody] EditCallInput editCallInput, CancellationToken cancellationToken)
		{
			var call = await _callsService.GetCallByIdAsync(editCallInput.Cid);

			call = await _callsService.PopulateCallData(call, true, true, true, true, true, true, true, true, true);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			if (call == null)
				return NotFound();

			if (call.DepartmentId != DepartmentId)
				return Unauthorized();

			if (call.State != (int)CallStates.Active)
				return BadRequest();

			call.Priority = (int)Enum.Parse(typeof(CallPriority), editCallInput.Pri);
			call.Name = editCallInput.Nme;
			call.NatureOfCall = editCallInput.Noc;

			if (!string.IsNullOrWhiteSpace(editCallInput.CNme))
				call.ContactName = editCallInput.CNme;

			if (!string.IsNullOrWhiteSpace(editCallInput.CNum))
				call.ContactName = editCallInput.CNum;

			if (!string.IsNullOrWhiteSpace(editCallInput.EId))
				call.ExternalIdentifier = editCallInput.EId;

			if (!string.IsNullOrWhiteSpace(editCallInput.InI))
				call.IncidentNumber = editCallInput.InI;

			if (!string.IsNullOrWhiteSpace(editCallInput.RId))
				call.ReferenceNumber = editCallInput.RId;

			if (!string.IsNullOrWhiteSpace(editCallInput.Add))
				call.Address = editCallInput.Add;

			if (!string.IsNullOrWhiteSpace(editCallInput.W3W))
				call.W3W = editCallInput.W3W;

			if (!string.IsNullOrWhiteSpace(editCallInput.Cfd))
				call.CallFormData = editCallInput.Cfd;

			if (editCallInput.Don.HasValue)
			{
				call.DispatchOn = editCallInput.Don.Value;

				call.DispatchOn = DateTimeHelpers.ConvertToUtc(editCallInput.Don.Value, department.TimeZone);
				call.HasBeenDispatched = false;
			}

			if (!string.IsNullOrWhiteSpace(editCallInput.Not))
				call.Notes = editCallInput.Not;

			if (!string.IsNullOrWhiteSpace(editCallInput.Geo))
				call.GeoLocationData = editCallInput.Geo;

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

			if (!String.IsNullOrWhiteSpace(editCallInput.Typ) && editCallInput.Typ != "No Type")
			{
				var callTypes = await _callsService.GetCallTypesForDepartmentAsync(DepartmentId);
				var type = callTypes.FirstOrDefault(x => x.Type == editCallInput.Typ);

				if (type != null)
				{
					call.Type = type.Type;
				}
			}

			if (string.IsNullOrWhiteSpace(editCallInput.Dis) || editCallInput.Dis == "0")
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
				var dispatch = editCallInput.Dis.Split(char.Parse("|"));
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
							var cd = new CallDispatch { CallId = call.CallId, UserId = user };
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
							var cd = new CallDispatchGroup { CallId = call.CallId, DepartmentGroupId = group };
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
							var cd = new CallDispatchRole { CallId = call.CallId, RoleId = role };
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
							var cdu = new CallDispatchUnit { CallId = call.CallId, UnitId = unit };
							call.UnitDispatches.Add(cdu);
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

			_eventAggregator.SendMessage<CallAddedEvent>(new CallAddedEvent() { DepartmentId = DepartmentId, Call = call });

			return Ok();
		}

		/// <summary>
		/// Get notes for a call
		/// </summary>
		/// <param name="callId">CallId of the call you want to get notes for</param>
		/// <returns></returns>
		[HttpGet("GetCallNotes")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<List<CallNoteResult>>> GetCallNotes(int callId)
		{
			var call = await _callsService.GetCallByIdAsync(callId);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);

			if (call == null)
				return NotFound();

			if (call.DepartmentId != DepartmentId)
				return Unauthorized();

			call = await _callsService.PopulateCallData(call, false, false, true, false, false, false, false, false, false);

			if (call.CallNotes == null || !call.CallNotes.Any())
				return NotFound();

			var result = new List<CallNoteResult>();

			foreach (var note in call.CallNotes)
			{
				var noteResult = new CallNoteResult();
				noteResult.Cnd = note.CallNoteId;
				noteResult.Cid = note.CallId;
				noteResult.Src = note.Source;
				noteResult.Uid = note.UserId.ToString();
				noteResult.Tme = note.Timestamp.TimeConverter(department).FormatForDepartment(department);
				noteResult.Tsp = note.Timestamp;
				noteResult.Not = note.Note;
				noteResult.Lat = note.Latitude;
				noteResult.Lng = note.Longitude;
				noteResult.Fnm = await UserHelper.GetFullNameForUser(note.UserId);

				result.Add(noteResult);
			}

			return Ok(result);
		}

		/// <summary>
		/// Get the files for a call in the Resgrid System
		/// </summary>
		/// <param name="callId">CallId to get the files for</param>
		/// <param name="includeData">Include the data in the result</param>
		/// <param name="type">Type of file to get (Any = 0, Audio = 1, Images = 2, Files = 3, Videos = 4)</param>
		/// <returns></returns>
		[HttpGet("GetFilesForCall")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<List<CallFileResult>>> GetFilesForCall(int callId, bool includeData, int type)
		{
			var result = new List<CallFileResult>();

			var call = await _callsService.GetCallByIdAsync(callId);

			if (call == null)
				return NotFound();

			if (call.DepartmentId != DepartmentId)
				return Unauthorized();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			call = await _callsService.PopulateCallData(call, false, true, false, false, false, false, false, false, false);

			var baseUrl = Config.SystemBehaviorConfig.ResgridApiBaseUrl;

			foreach (var attachment in call.Attachments)
			{
				var file = new CallFileResult();
				file.Id = attachment.CallAttachmentId;
				file.Cid = attachment.CallId;
				file.Fln = attachment.FileName;
				file.Typ = attachment.CallAttachmentType;
				file.Url = baseUrl + "/api/v3/Calls/GetFile?departmentId=" + DepartmentId + "&id=" + attachment.CallAttachmentId;
				file.Nme = attachment.Name;
				file.Sze = attachment.Size.GetValueOrDefault();
				file.Mime = FileHelper.GetContentTypeByExtension(Path.GetExtension(attachment.FileName));

				if (attachment.Timestamp.HasValue)
					file.Tme = attachment.Timestamp.Value.TimeConverterToString(department);
				else
					file.Tme = DateTime.UtcNow.TimeConverterToString(department);

				if (!String.IsNullOrWhiteSpace(attachment.UserId))
					file.Uid = attachment.UserId;

				if (includeData)
					file.Data = Convert.ToBase64String(attachment.Data);

				if (type == 0)
					result.Add(file);
				else if (type == attachment.CallAttachmentType)
					result.Add(file);
			}

			return Ok(result);
		}

		/// <summary>
		/// Get a users avatar from the Resgrid system based on their ID
		/// </summary>
		/// <param name="id">ID of the user</param>
		/// <param name="departmentId">The department id of the file your requesting</param>
		/// <returns></returns>
		[HttpGet("GetFile")]
		[AllowAnonymous]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult> GetFile(int departmentId, int id)
		{
			var attachment = await _callsService.GetCallAttachmentAsync(id);

			if (attachment == null)
				return NotFound();

			var call = await _callsService.GetCallByIdAsync(attachment.CallId);
			if (call.DepartmentId != departmentId)
				return Unauthorized();

			//result.Content = new ByteArrayContent(attachment.Data);
			//result.Content.Headers.ContentType = new MediaTypeHeaderValue(FileHelper.GetContentTypeByExtension(Path.GetExtension(attachment.FileName)));

			return File(attachment.Data, FileHelper.GetContentTypeByExtension(Path.GetExtension(attachment.FileName)));
		}

		/// <summary>
		/// Attaches a file to a call
		/// </summary>
		/// <param name="input">ID of the user</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns></returns>
		[HttpPost("UploadFile")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		public async Task<ActionResult> UploadFile(CallFileInput input, CancellationToken cancellationToken)
		{
			var result = Ok();

			var call = await _callsService.GetCallByIdAsync(input.Cid);

			if (call == null)
				return NotFound();

			if (call.DepartmentId != DepartmentId)
				return Unauthorized();

			if (call.State != (int)CallStates.Active)
				return BadRequest();

			var callAttachment = new CallAttachment();
			callAttachment.CallId = input.Cid;
			callAttachment.CallAttachmentType = input.Typ;

			if (String.IsNullOrWhiteSpace(input.Nme))
				callAttachment.FileName = "cameraPhoneUpload.png";
			else
				callAttachment.FileName = input.Nme;

			callAttachment.UserId = input.Uid;
			callAttachment.Timestamp = DateTime.UtcNow;
			callAttachment.Data = Convert.FromBase64String(input.Data);

			if (!String.IsNullOrWhiteSpace(input.Lat))
			{
				callAttachment.Latitude = decimal.Parse(input.Lat);
			}

			if (!String.IsNullOrWhiteSpace(input.Lon))
			{
				callAttachment.Longitude = decimal.Parse(input.Lon);
			}

			var saved = await _callsService.SaveCallAttachmentAsync(callAttachment, cancellationToken);

			return CreatedAtAction(nameof(UploadFile), new { id = saved.CallAttachmentId }, saved);
		}

		/// <summary>
		/// Get notes for a call
		/// </summary>
		/// <param name="input">CallId of the call you want to get notes for</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>ActionResult.</returns>
		[HttpPost("AddCallNote")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		public async Task<ActionResult> AddCallNote(AddCallNoteInput input, CancellationToken cancellationToken)
		{
			var note = new CallNote();
			note.CallId = input.CallId;
			note.Timestamp = DateTime.UtcNow;
			note.Note = input.Note;
			note.UserId = input.UserId;
			note.Source = (int)CallNoteSources.Mobile;

			var saved = await _callsService.SaveCallNoteAsync(note, cancellationToken);

			return CreatedAtAction(nameof(AddCallNote), new { id = saved.CallNoteId }, saved);
		}


		/// <summary>
		/// Returns the call audio for a specific call (Allow Anonymous)
		/// </summary>
		/// <param name="query">Encrypted query string for the call</param>
		/// <returns>Http response type matching call audio format with data</returns>
		[HttpGet("GetCallAudio")]
		[AllowAnonymous]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult> GetCallAudio(string query)
		{
			if (String.IsNullOrWhiteSpace(query))
				return NotFound();

			var decodedQuery = Encoding.UTF8.GetString(Convert.FromBase64String(query));

			string plainText = SymmetricEncryption.Decrypt(decodedQuery, Config.SystemBehaviorConfig.ExternalAudioUrlParamPasshprase);

			if (String.IsNullOrWhiteSpace(plainText))
				return NotFound();

			var callAttachment = await _callsService.GetCallAttachmentAsync(int.Parse(plainText));

			if (callAttachment != null && callAttachment.Data != null)
			{
				//result.Content = new ByteArrayContent(callAttachment.Data);
				////result.Content.Headers.ContentDisposition.FileName = callAttachment.FileName;
				//result.Content.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("attachment");
				//result.Content.Headers.ContentDisposition.FileName = callAttachment.FileName;
				//result.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
				//result.Content.Headers.ContentLength = callAttachment.Data.Length;

				return File(new MemoryStream(callAttachment.Data), "audio/mpeg", callAttachment.FileName);
			}

			return NotFound();
		}

		/// <summary>
		/// Get notes for a call
		/// </summary>
		/// <param name="departmentId">CallId of the call you want to get notes for</param>
		/// <returns></returns>
		[HttpGet("GetActiveCallPrioritiesForDepartment")]
		public List<CallPriorityResult> GetActiveCallPrioritiesForDepartment(int departmentId)
		{
			var result = new List<CallPriorityResult>();


			return result;
		}

		/// <summary>
		/// Get all the call types for a department
		/// </summary>
		/// <returns>An array of call types</returns>
		[HttpGet("GetCallTypes")]
		[AllowAnonymous]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<List<CallTypeResult>>> GetCallTypes()
		{
			var result = new List<CallTypeResult>();

			var callTypes = await _callsService.GetCallTypesForDepartmentAsync(DepartmentId);

			if (callTypes != null && callTypes.Any())
			{
				foreach (var callType in callTypes)
				{
					var type = new CallTypeResult();
					type.Id = callType.CallTypeId;
					type.Name = callType.Type;

					result.Add(type);
				}
			}

			return result;
		}

		/// <summary>
		/// Returns all the non-dispatched (pending) scheduled calls for the department
		/// </summary>
		/// <returns>Array of CallResult objects for each active call in the department</returns>
		[HttpGet("GetAllPendingScheduledCalls")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status500InternalServerError)]
		public async Task<ActionResult<List<CallResult>>> GetAllPendingScheduledCalls()
		{
			var result = new List<CallResult>();

			var calls = (await _callsService.GetAllNonDispatchedScheduledCallsByDepartmentIdAsync(DepartmentId)).OrderBy(x => x.DispatchOn);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			foreach (var c in calls)
			{
				var call = new CallResult();

				call.Cid = c.CallId;
				call.Pri = c.Priority;
				call.Ctl = c.IsCritical;
				call.Nme = StringHelpers.SanitizeHtmlInString(c.Name);

				if (!String.IsNullOrWhiteSpace(c.NatureOfCall))
					call.Noc = StringHelpers.SanitizeHtmlInString(c.NatureOfCall);

				call.Map = c.MapPage;

				if (!String.IsNullOrWhiteSpace(c.Notes))
					call.Not = StringHelpers.SanitizeHtmlInString(c.Notes);

				if (c.CallNotes != null)
					call.Nts = c.CallNotes.Count();
				else
					call.Nts = 0;

				if (c.Attachments != null)
				{
					call.Aud = c.Attachments.Count(x => x.CallAttachmentType == (int)CallAttachmentTypes.DispatchAudio);
					call.Img = c.Attachments.Count(x => x.CallAttachmentType == (int)CallAttachmentTypes.Image);
					call.Fls = c.Attachments.Count(x => x.CallAttachmentType == (int)CallAttachmentTypes.File);
				}
				else
				{
					call.Aud = 0;
					call.Img = 0;
					call.Fls = 0;
				}

				if (String.IsNullOrWhiteSpace(c.Address) && c.HasValidGeolocationData())
				{
					var geo = c.GeoLocationData.Split(char.Parse(","));

					if (geo.Length == 2)
						call.Add = await _geoLocationProvider.GetAddressFromLatLong(double.Parse(geo[0]), double.Parse(geo[1]));
				}
				else
					call.Add = c.Address;

				call.Geo = c.GeoLocationData;
				call.Lon = c.LoggedOn.TimeConverter(department);
				call.Utc = c.LoggedOn;
				call.Ste = c.State;
				call.Num = c.Number;

				if (c.DispatchOn.HasValue)
					call.Don = c.DispatchOn.Value.TimeConverter(department);

				result.Add(call);
			}

			return Ok(result);
		}

		/// <summary>
		/// Updates a call's scheduled dispatch time if it has not been dispatched
		/// </summary>
		/// <param name="callId">ID of the call</param>
		/// <param name="date">UTC date to change the dispatch to</param>
		/// <returns></returns>
		[HttpGet("UpdateScheduledDispatchTime")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		public async Task<ActionResult> UpdateScheduledDispatchTime(int callId, DateTime date)
		{
			var call = await _callsService.GetCallByIdAsync(callId);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var canDoOperation = await _authorizationService.CanUserCreateCallAsync(UserId, DepartmentId);

			if (!canDoOperation)
				return Unauthorized();

			if (call == null)
				return NotFound();

			if (call.DepartmentId != DepartmentId)
				return Unauthorized();

			if (call.HasBeenDispatched.HasValue && call.HasBeenDispatched.Value)
				return BadRequest();

			call.DispatchOn = DateTimeHelpers.ConvertToUtc(date, department.TimeZone);
			call.HasBeenDispatched = false;

			var savedCall = await _callsService.SaveCallAsync(call);

			return Ok();
		}

		/// <summary>
		/// Deletes a call
		/// </summary>
		/// <param name="callId">ID of the call</param>
		/// <returns></returns>
		[HttpDelete("DeleteCall")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		public async Task<ActionResult> DeleteCall(int callId)
		{
			var call = await _callsService.GetCallByIdAsync(callId);
			var canDoOperation = await _authorizationService.CanUserCreateCallAsync(UserId, DepartmentId);

			if (!canDoOperation)
				return Unauthorized();

			if (call == null)
				return NotFound();

			if (call.DepartmentId != DepartmentId)
				return Unauthorized();

			if (call.HasBeenDispatched.HasValue && call.HasBeenDispatched.Value)
				return BadRequest();

			call.IsDeleted = true;

			var savedCall = await _callsService.SaveCallAsync(call);

			return Ok();
		}
	}
}
