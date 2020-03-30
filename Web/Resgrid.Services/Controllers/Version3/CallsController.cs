using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using System.Net;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Cors;
using Resgrid.Framework;
using Resgrid.Model.Events;
using Resgrid.Model.Queue;
using Resgrid.Providers.Bus;
using Resgrid.Web.Services.Controllers.Version3.Models.Calls;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Controllers.Version3.Models.CallPriorities;

namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Operations to be performed against calls
	/// </summary>
	public class CallsController : V3AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private ICallsService _callsService;
		private IDepartmentsService _departmentsService;
		private IUserProfileService _userProfileService;
		private IGeoLocationProvider _geoLocationProvider;
		private readonly IAuthorizationService _authorizationService;
		private readonly IQueueService _queueService;
		private readonly IUsersService _usersService;
		private readonly IUnitsService _unitsService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IPersonnelRolesService _personnelRolesService;

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
			IPersonnelRolesService personnelRolesService
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
		}
		#endregion Members and Constructors

		/// <summary>
		/// Returns all the active calls for the department
		/// </summary>
		/// <returns>Array of CallResult objects for each active call in the department</returns>
		[System.Web.Http.AcceptVerbs("GET")]
		public List<CallResult> GetActiveCalls()
		{
			var result = new List<CallResult>();

			var calls = _callsService.GetActiveCallsByDepartment(DepartmentId).OrderByDescending(x => x.LoggedOn);
			var department = _departmentsService.GetDepartmentById(DepartmentId, false);

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

				if (String.IsNullOrWhiteSpace(c.Address) && !String.IsNullOrWhiteSpace(c.GeoLocationData))
				{
					var geo = c.GeoLocationData.Split(char.Parse(","));

					if (geo.Length == 2)
						call.Add = _geoLocationProvider.GetAddressFromLatLong(double.Parse(geo[0]), double.Parse(geo[1]));
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

			return result;
		}

		/// <summary>
		/// Returns all the active calls for the department (extended object result, more verbose then GetActiveCalls)
		/// </summary>
		/// <returns>Array of DepartmentCallResult objects for each active call in the department</returns>
		[System.Web.Http.AcceptVerbs("GET")]
		public List<DepartmentCallResult> GetActiveCallsForDepartment(int departmentId)
		{
			var result = new List<DepartmentCallResult>();

			if (departmentId != DepartmentId && !IsSystem)
				Unauthorized();

			var calls = _callsService.GetActiveCallsByDepartment(departmentId).OrderByDescending(x => x.LoggedOn);
			var department = _departmentsService.GetDepartmentById(departmentId, false);

			foreach (var c in calls)
			{
				var call = new DepartmentCallResult();

				call.Name = StringHelpers.SanitizeHtmlInString(c.Name);

				if (!String.IsNullOrWhiteSpace(c.NatureOfCall))
					call.NatureOfCall = StringHelpers.SanitizeHtmlInString(c.NatureOfCall);

				if (!String.IsNullOrWhiteSpace(c.Notes))
					call.Notes = StringHelpers.SanitizeHtmlInString(c.Notes);

				//if (c.CallNotes != null)
				//	call.Nts = c.CallNotes.Count();
				//else
				//	call.Nts = 0;

				//if (c.Attachments != null)
				//{
				//	call.Aud = c.Attachments.Count(x => x.CallAttachmentType == (int)CallAttachmentTypes.DispatchAudio);
				//	call.Img = c.Attachments.Count(x => x.CallAttachmentType == (int)CallAttachmentTypes.Image);
				//	call.Fls = c.Attachments.Count(x => x.CallAttachmentType == (int)CallAttachmentTypes.File);
				//}
				//else
				//{
				//	call.Aud = 0;
				//	call.Img = 0;
				//	call.Fls = 0;
				//}

				//if (String.IsNullOrWhiteSpace(c.Address) && !String.IsNullOrWhiteSpace(c.GeoLocationData))
				//{
				//	var geo = c.GeoLocationData.Split(char.Parse(","));

				//	if (geo.Length == 2)
				//		call.Add = _geoLocationProvider.GetAddressFromLatLong(double.Parse(geo[0]), double.Parse(geo[1]));
				//}
				//else
				//	call.Address = c.Address;

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

			return result;
		}

		/// <summary>
		/// Returns a specific call from the Resgrid System
		/// </summary>
		/// <returns>CallResult of the call in the Resgrid system</returns>
		[System.Web.Http.AcceptVerbs("GET")]
		public CallResult GetCall(int callId)
		{
			var c = _callsService.GetCallById(callId);
			var department = _departmentsService.GetDepartmentById(DepartmentId, false);
			//var types = _callsService.GetCallTypesForDepartment(DepartmentId);

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
					var audio = c.Attachments.FirstOrDefault(x => x.CallAttachmentType == (int) CallAttachmentTypes.DispatchAudio);

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

			if (String.IsNullOrWhiteSpace(c.Address) && !String.IsNullOrWhiteSpace(c.GeoLocationData))
			{
				var geo = c.GeoLocationData.Split(char.Parse(","));

				if (geo.Length == 2)
					call.Add = _geoLocationProvider.GetAddressFromLatLong(double.Parse(geo[0]), double.Parse(geo[1]));
			}
			else
				call.Add = c.Address;

			call.Geo = c.GeoLocationData;
			call.Lon = c.LoggedOn.TimeConverter(department);
			call.Utc = c.LoggedOn;
			call.Ste = c.State;
			call.Num = c.Number;

			if (!String.IsNullOrWhiteSpace(c.W3W))
				call.w3w = c.W3W;


			return call;
		}

		/// <summary>
		/// Gets all the meta-data around a call, dispatched personnel, units, groups and responses
		/// </summary>
		/// <param name="callId">CallId to get data for</param>
		/// <returns></returns>
		public CallDataResult GetCallExtraData(int callId)
		{
			var result = new CallDataResult();

			var call = _callsService.GetCallById(callId);
			var groups = _departmentGroupsService.GetAllGroupsForDepartment(DepartmentId);
			var units = _unitsService.GetUnitsForDepartment(call.DepartmentId);
			var unitStates = _unitsService.GetUnitStatesForCall(call.DepartmentId, callId).OrderBy(x => x.UnitId).OrderBy(y => y.Timestamp).ToList();
			var actionLogs = _actionLogsService.GetActionLogsForCall(call.DepartmentId, callId).OrderBy(x => x.UserId).OrderBy(y => y.Timestamp).ToList();
			var names = _usersService.GetUserGroupAndRolesByDepartmentId(DepartmentId, true, true, true);
			var priority = _callsService.GetCallPrioritesById(call.DepartmentId, call.Priority, false);

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

			return result;
		}

		/// <summary>
		/// Saves a call in the Resgrid system
		/// </summary>
		/// <param name="newCallInput"></param>
		/// <returns></returns>
		[System.Web.Http.AcceptVerbs("POST")]
		public HttpResponseMessage SaveCall([FromBody] NewCallInput newCallInput)
		{
			if (!ModelState.IsValid)
				return Request.CreateErrorResponse(HttpStatusCode.BadRequest, ModelState);

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

			if (!string.IsNullOrWhiteSpace(newCallInput.Cid))
				call.IncidentNumber = newCallInput.Cid;

			if (!string.IsNullOrWhiteSpace(newCallInput.Add))
				call.Address = newCallInput.Add;

			if (!string.IsNullOrWhiteSpace(newCallInput.W3W))
				call.W3W = newCallInput.W3W;

			//if (call.Address.Equals("Current Coordinates", StringComparison.InvariantCultureIgnoreCase))
			//	call.Address = "";

			if (!string.IsNullOrWhiteSpace(newCallInput.Not))
				call.Notes = newCallInput.Not;

			if (!string.IsNullOrWhiteSpace(newCallInput.Geo))
				call.GeoLocationData = newCallInput.Geo;

			if (string.IsNullOrWhiteSpace(call.GeoLocationData) && !string.IsNullOrWhiteSpace(call.Address))
				call.GeoLocationData = _geoLocationProvider.GetLatLonFromAddress(call.Address);

			if (string.IsNullOrWhiteSpace(call.GeoLocationData) && !string.IsNullOrWhiteSpace(call.W3W))
			{
				var coords = _geoLocationProvider.GetCoordinatesFromW3W(call.W3W);

				if (coords != null)
				{
					call.GeoLocationData = $"{coords.Latitude},{coords.Longitude}";
				}
			}

			call.LoggedOn = DateTime.UtcNow;

			if (!String.IsNullOrWhiteSpace(newCallInput.Typ) && newCallInput.Typ != "No Type")
			{
				var callTypes = _callsService.GetCallTypesForDepartment(DepartmentId);
				var type = callTypes.FirstOrDefault(x => x.Type == newCallInput.Typ);

				if (type != null)
				{
					call.Type = type.Type;
				}
			}
			var users = _departmentsService.GetAllUsersForDepartment(DepartmentId);
			call.Dispatches = new Collection<CallDispatch>();
			call.GroupDispatches = new List<CallDispatchGroup>();
			call.RoleDispatches = new List<CallDispatchRole>();

			if (string.IsNullOrWhiteSpace(newCallInput.Dis) || newCallInput.Dis == "0")
			{
				// Use case, existing clients and non-ionic2 app this will be null dispatch all users. Or we've specified everyone (0).
				foreach (var u in users)
				{
					var cd = new CallDispatch {UserId = u.UserId};

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
			}
			

			var savedCall = _callsService.SaveCall(call);

			OutboundEventProvider.CallAddedTopicHandler handler = new OutboundEventProvider.CallAddedTopicHandler();
			handler.Handle(new CallAddedEvent() { DepartmentId = DepartmentId, Call = savedCall });

			var profiles = new List<string>();

			if (call.Dispatches != null && call.Dispatches.Any())
			{
				profiles.AddRange(call.Dispatches.Select(x => x.UserId).ToList());
			}

			if (call.GroupDispatches != null && call.GroupDispatches.Any())
			{
				foreach (var groupDispatch in call.GroupDispatches)
				{
					var group = _departmentGroupsService.GetGroupById(groupDispatch.DepartmentGroupId);

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
					var members = _personnelRolesService.GetAllMembersOfRole(roleDispatch.RoleId);

					if (members != null)
					{
						profiles.AddRange(members.Select(x => x.UserId).ToList());
					}
				}
			}

			var cqi = new CallQueueItem();
			cqi.Call = savedCall;
			cqi.Profiles = _userProfileService.GetSelectedUserProfiles(profiles);

			_queueService.EnqueueCallBroadcast(cqi);

			return Request.CreateResponse(HttpStatusCode.Created);
		}

		/// <summary>
		/// Adds a new call into Resgrid and Dispatches the call
		/// </summary>
		/// <param name="callInput">Call data to add into the system</param>
		/// <returns></returns>
		public async Task<AddCallInput> AddCall([FromBody] AddCallInput callInput)
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

				if (string.IsNullOrWhiteSpace(call.GeoLocationData) && !string.IsNullOrWhiteSpace(call.Address))
					call.GeoLocationData = _geoLocationProvider.GetLatLonFromAddress(call.Address);

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
						var cd = new CallDispatch {UserId = u.UserId};

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
								var cd = new CallDispatchGroup {DepartmentGroupId = groupsToDispatch.DepartmentGroupId};
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

				var savedCall = _callsService.SaveCall(call);


				OutboundEventProvider.CallAddedTopicHandler handler = new OutboundEventProvider.CallAddedTopicHandler();
				handler.Handle(new CallAddedEvent() { DepartmentId = DepartmentId, Call = savedCall });

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

				_queueService.EnqueueCallBroadcast(cqi);

				callInput.CallId = savedCall.CallId;
				callInput.Number = savedCall.Number;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);

				throw ex;
			}

			return callInput;
		}

		/// <summary>
		/// Closes a Resgrid call
		/// </summary>
		/// <param name="closeCallInput">Data to close a call</param>
		/// <returns>OK status code if successful</returns>
		[System.Web.Http.AcceptVerbs("PUT")]
		public HttpResponseMessage CloseCall([FromBody] CloseCallInput closeCallInput)
		{
			if (!ModelState.IsValid)
				return Request.CreateErrorResponse(HttpStatusCode.BadRequest, ModelState);

			var call = _callsService.GetCallById(closeCallInput.Id);

			if (call == null)
				throw HttpStatusCode.NotFound.AsException();

			call.ClosedByUserId = UserId;
			call.ClosedOn = DateTime.UtcNow;
			call.CompletedNotes = closeCallInput.Msg;
			call.State = (int)closeCallInput.Typ;

			_callsService.SaveCall(call);

			OutboundEventProvider.CallAddedTopicHandler handler = new OutboundEventProvider.CallAddedTopicHandler();
			handler.Handle(new CallAddedEvent() { DepartmentId = DepartmentId, Call = call });

			return Request.CreateResponse(HttpStatusCode.OK);
		}

		/// <summary>
		/// Updates an existing Active Call in the Resgrid system
		/// </summary>
		/// <param name="editCallInput">Data to updated the call</param>
		/// <returns>OK status code if successful</returns>
		[System.Web.Http.AcceptVerbs("PUT")]
		public HttpResponseMessage EditCall([FromBody] EditCallInput editCallInput)
		{
			var call = _callsService.GetCallById(editCallInput.Cid);

			if (call == null)
				throw HttpStatusCode.NotFound.AsException();

			if (call.DepartmentId != DepartmentId)
				throw HttpStatusCode.Unauthorized.AsException();

			if (call.State != (int)CallStates.Active)
				throw HttpStatusCode.NotAcceptable.AsException();

			if (!String.IsNullOrWhiteSpace(editCallInput.Nme) && editCallInput.Nme != call.Name)
				call.Name = editCallInput.Nme;

			if (!String.IsNullOrWhiteSpace(editCallInput.Noc) && editCallInput.Noc != call.NatureOfCall)
				call.NatureOfCall = editCallInput.Noc;

			if (!String.IsNullOrWhiteSpace(editCallInput.Add) && editCallInput.Add != call.Address)
				call.Address = editCallInput.Add;

			_callsService.SaveCall(call);

			OutboundEventProvider.CallAddedTopicHandler handler = new OutboundEventProvider.CallAddedTopicHandler();
			handler.Handle(new CallAddedEvent() { DepartmentId = DepartmentId, Call = call });

			return Request.CreateResponse(HttpStatusCode.OK);
		}

		/// <summary>
		/// Get notes for a call
		/// </summary>
		/// <param name="callId">CallId of the call you want to get notes for</param>
		/// <returns></returns>
		public List<CallNoteResult> GetCallNotes(int callId)
		{
			var call = _callsService.GetCallById(callId);
			var department = _departmentsService.GetDepartmentById(DepartmentId);

			if (call == null)
				return null;

			if (call.DepartmentId != DepartmentId)
				return null;

			if (call.CallNotes == null || !call.CallNotes.Any())
				return null;

			var result = new List<CallNoteResult>();

			foreach (var note in call.CallNotes)
			{
				var noteResult = new CallNoteResult();
				noteResult.Cnd = note.CallNoteId;
				noteResult.Cid = note.CallId;
				noteResult.Src = note.Source;
				noteResult.Uid = note.UserId.ToString();
				noteResult.Tme = note.Timestamp.TimeConverter(call.Department).FormatForDepartment(department);
				noteResult.Tsp = note.Timestamp;
				noteResult.Not = note.Note;
				noteResult.Lat = note.Latitude;
				noteResult.Lng = note.Longitude;

				result.Add(noteResult);
			}

			return result;
		}

		/// <summary>
		/// Get the files for a call in the Resgrid System
		/// </summary>
		/// <param name="callId">CallId to get the files for</param>
		/// <param name="includeData">Include the data in the result</param>
		/// <param name="type">Type of file to get (Any = 0, Audio = 1, Images = 2, Files = 3, Videos = 4)</param>
		/// <returns></returns>
		[System.Web.Http.AcceptVerbs("GET")]
		public List<CallFileResult> GetFilesForCall(int callId, bool includeData, int type)
		{
			var result = new List<CallFileResult>();

			var call = _callsService.GetCallById(callId);

			if (call == null)
				return null;

			if (call.DepartmentId != DepartmentId)
				return null;

			var baseUrl = ConfigurationManager.AppSettings["ResgridApiUrl"];

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
					file.Tme = attachment.Timestamp.Value.TimeConverterToString(call.Department);
				else
					file.Tme = DateTime.UtcNow.TimeConverterToString(call.Department);

				if (!String.IsNullOrWhiteSpace(attachment.UserId))
					file.Uid = attachment.UserId;

				if (includeData)
					file.Data = Convert.ToBase64String(attachment.Data);

				if (type == 0)
					result.Add(file);
				else if (type == attachment.CallAttachmentType)
					result.Add(file);
			}

			return result;
		}

		/// <summary>
		/// Get a users avatar from the Resgrid system based on their ID
		/// </summary>
		/// <param name="id">ID of the user</param>
		/// <returns></returns>
		[System.Web.Http.AcceptVerbs("GET")]
		[AllowAnonymous]
		public HttpResponseMessage GetFile(int departmentId, int id)
		{
			var result = new HttpResponseMessage(HttpStatusCode.OK);

			var attachment = _callsService.GetCallAttachment(id);

			if (attachment == null)
				return Request.CreateResponse(HttpStatusCode.NotFound);

			// THIS IS A HACK! I've run into issues trying to load images on the mobile app
			// while using the Auth header (img src doesn't support headers)
			if (attachment.Call.DepartmentId != departmentId)
				return Request.CreateResponse(HttpStatusCode.Unauthorized);

			//if (attachment.Call.DepartmentId != DepartmentId)
			//	return Request.CreateResponse(HttpStatusCode.Unauthorized);

			result.Content = new ByteArrayContent(attachment.Data);
			result.Content.Headers.ContentType = new MediaTypeHeaderValue(FileHelper.GetContentTypeByExtension(Path.GetExtension(attachment.FileName)));

			return result;
		}

		/// <summary>
		/// Attaches a file to a call
		/// </summary>
		/// <param name="input">ID of the user</param>
		/// <returns></returns>
		[System.Web.Http.AcceptVerbs("POST")]
		public HttpResponseMessage UploadFile(CallFileInput input)
		{
			var result = new HttpResponseMessage(HttpStatusCode.OK);

			var call = _callsService.GetCallById(input.Cid);

			if (call == null)
				return Request.CreateResponse(HttpStatusCode.NotFound);

			if (call.DepartmentId != DepartmentId)
				return Request.CreateResponse(HttpStatusCode.Unauthorized);

			if (call.State != (int)CallStates.Active)
				return Request.CreateResponse(HttpStatusCode.NotAcceptable);

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

			if (!String.IsNullOrWhiteSpace(input.Lat)) {
				callAttachment.Latitude = decimal.Parse(input.Lat);
			}

			if (!String.IsNullOrWhiteSpace(input.Lon)) {
				callAttachment.Longitude = decimal.Parse(input.Lon);
			}

			_callsService.SaveCallAttachment(callAttachment);

			return result;
		}

		/// <summary>
		/// Get notes for a call
		/// </summary>
		/// <param name="input">CallId of the call you want to get notes for</param>
		/// <returns></returns>
		[System.Web.Http.AcceptVerbs("POST")]
		public HttpResponseMessage AddCallNote(AddCallNoteInput input)
		{
			var note = new CallNote();
			note.CallId = input.CallId;
			note.Timestamp = DateTime.UtcNow;
			note.Note = input.Note;
			note.UserId = input.UserId;
			note.Source = (int)CallNoteSources.Mobile;

			_callsService.SaveCallNote(note);

			return Request.CreateResponse(HttpStatusCode.OK);
		}

		[System.Web.Http.AcceptVerbs("GET")]
		[AllowAnonymous]
		public HttpResponseMessage GetCallAudio(string query)
		{
			if (String.IsNullOrWhiteSpace(query))
				return new HttpResponseMessage(HttpStatusCode.NotFound);

			string plainText = SymmetricEncryption.Decrypt(query, Config.SystemBehaviorConfig.ExternalAudioUrlParamPasshprase);

			if (String.IsNullOrWhiteSpace(plainText))
				return new HttpResponseMessage(HttpStatusCode.NotFound);

			var callAttachment = _callsService.GetCallAttachment(int.Parse(plainText));

			if (callAttachment != null && callAttachment.Data != null)
			{
				HttpResponseMessage result = new HttpResponseMessage(HttpStatusCode.OK);
				result.Content = new ByteArrayContent(callAttachment.Data);
				//result.Content.Headers.ContentDisposition.FileName = callAttachment.FileName;
				result.Content.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("attachment");
				result.Content.Headers.ContentDisposition.FileName = callAttachment.FileName;
				result.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
				result.Content.Headers.ContentLength = callAttachment.Data.Length;

				return result;
			}

			return new HttpResponseMessage(HttpStatusCode.NotFound);
		}

		/// <summary>
		/// Get notes for a call
		/// </summary>
		/// <param name="departmentId">CallId of the call you want to get notes for</param>
		/// <returns></returns>
		public List<CallPriorityResult> GetActiveCallPrioritiesForDepartment(int departmentId)
		{
			var result = new List<CallPriorityResult>();


			return result;
		}

		/// <summary>
		/// Get all the call types for a department
		/// </summary>
		/// <returns>An array of call types</returns>
		public List<CallTypeResult> GetCallTypes()
		{
			var result = new List<CallTypeResult>();

			var callTypes = _callsService.GetCallTypesForDepartment(DepartmentId);

			if (callTypes != null && callTypes.Any())
			{
				foreach(var callType in callTypes)
				{
					var type = new CallTypeResult();
					type.Id = callType.CallTypeId;
					type.Name = callType.Type;

					result.Add(type);
				}
			}

			return result;
		}

		public HttpResponseMessage Options()
		{
			var response = new HttpResponseMessage();
			response.StatusCode = HttpStatusCode.OK;
			response.Headers.Add("Access-Control-Allow-Origin", "*");
			response.Headers.Add("Access-Control-Request-Headers", "*");
			response.Headers.Add("Access-Control-Allow-Methods", "GET,POST,PUT,DELETE,OPTIONS");

			return response;
		}
	}
}
