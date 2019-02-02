using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Cors;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.Version3.Models.BigBoard;
using Resgrid.Web.Services.Controllers.Version3.Models.BigBoard.BigBoardX;
using Resgrid.Web.Services.Controllers.Version3.Models.Calls;
using Resgrid.Web.Services.Controllers.Version3.Models.Links;
using Resgrid.Web.Services.Controllers.Version3.Models.Personnel;
using Resgrid.Web.Services.Controllers.Version3.Models.Units;

namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Operations to perform against the security sub-system
	/// </summary>
	[EnableCors(origins: "*", headers: "*", methods: "GET,POST,PUT,DELETE,OPTIONS")]
	public class LinksController : V3AuthenticatedApiControllerbase
	{
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentLinksService _departmentLinksService;
		private readonly ILimitsService _limitsService;
		private readonly ICallsService _callsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IGeoLocationProvider _geoLocationProvider;
		private readonly IUnitsService _unitsService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IUserStateService _userStateService;

		/// <summary>
		/// Operations to perform against the department links system. Department Links allow departments to 
		/// share data to other departments, for example calls or resource orders.
		/// </summary>
		public LinksController(IDepartmentsService departmentsService, IDepartmentLinksService departmentLinksService, ILimitsService limitsService, 
			ICallsService callsService, IUserProfileService userProfileService, IGeoLocationProvider geoLocationProvider, IUnitsService unitsService,
			IActionLogsService actionLogsService, IUserStateService userStateService)
		{
			_departmentsService = departmentsService;
			_departmentLinksService = departmentLinksService;
			_limitsService = limitsService;
			_callsService = callsService;
			_userProfileService = userProfileService;
			_geoLocationProvider = geoLocationProvider;
			_unitsService = unitsService;
			_actionLogsService = actionLogsService;
			_userStateService = userStateService;
		}

		/// <summary>
		/// Gets the current active department links for this department where data is bring shared to it
		/// </summary>
		/// <returns>List of DepartmentLinkResult objects with the information about the links</returns>
		public List<DepartmentLinkResult> GetActiveDepartmentLinks()
		{
			if (!_limitsService.CanDepartmentUseLinks(DepartmentId))
				return new List<DepartmentLinkResult>();

			var linkResults = new List<DepartmentLinkResult>();
			var links = _departmentLinksService.GetAllLinksForDepartment(DepartmentId);

			foreach (var link in links)
			{
				if (link.LinkedDepartmentId == DepartmentId && link.LinkEnabled)
				{
					var result = new DepartmentLinkResult();
					result.LinkId = link.DepartmentLinkId;
					result.DepartmentName = link.Department.Name;
					result.Color = link.DepartmentColor;
					result.ShareCalls = link.DepartmentShareCalls;
					result.ShareOrders = link.DepartmentShareOrders;
					result.SharePersonnel = link.DepartmentSharePersonnel;
					result.ShareUnits = link.DepartmentShareUnits;

					linkResults.Add(result);
				}
			}

			return linkResults;
		}

		/// <summary>
		/// Returns all the active calls for a specific department link
		/// </summary>
		/// <returns>Array of CallResult objects for each active call in the department</returns>
		[System.Web.Http.AcceptVerbs("GET")]
		public List<LinkedCallResult> GetActiveCallsForLink(int linkId)
		{
			var result = new List<LinkedCallResult>();

			var link = _departmentLinksService.GetLinkById(linkId);

			if (link.DepartmentId != DepartmentId && link.LinkedDepartmentId != DepartmentId)
				return new List<LinkedCallResult>();

			var calls = _callsService.GetActiveCallsByDepartment(link.DepartmentId).OrderByDescending(x => x.LoggedOn);
			var department = _departmentsService.GetDepartmentById(link.DepartmentId, false);

			foreach (var c in calls)
			{
				var call = new LinkedCallResult();

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
				call.Ste = c.State;
				call.Num = c.Number;

				call.Priority = c.ToCallPriorityDisplayText();
				call.PriorityCss = c.ToCallPriorityCss();
				call.State = c.ToCallStateDisplayText();
				call.StateCss = c.ToCallStateCss();

				result.Add(call);
			}

			return result;
		}

		/// <summary>
		/// Get's all the units for a department link and their current status information
		/// </summary>
		/// <returns>List of UnitStatusResult objects, with status information for each unit.</returns>
		[AcceptVerbs("GET")]
		public List<UnitStatusResult> GetUnitStatusesForLink(int linkId)
		{
			var results = new List<UnitStatusResult>();

			var link = _departmentLinksService.GetLinkById(linkId);
			if (link.DepartmentId != DepartmentId && link.LinkedDepartmentId != DepartmentId)
				return new List<UnitStatusResult>();

			var units = _unitsService.GetAllLatestStatusForUnitsByDepartmentId(link.DepartmentId);
			var department = _departmentsService.GetDepartmentById(link.DepartmentId, false);

			foreach (var u in units)
			{
				var unitStatus = new UnitStatusResult();
				unitStatus.Uid = u.UnitId;
				unitStatus.Typ = u.State;
				unitStatus.Tmp = u.Timestamp.TimeConverter(department);

				if (u.DestinationId.HasValue)
					unitStatus.Did = u.DestinationId.Value;

				results.Add(unitStatus);
			}

			return results;
		}

		/// <summary>
		/// Get's all the personnel in a department link and their current status and staffing information
		/// </summary>
		/// <returns>List of PersonnelStatusResult objects, with status and staffing information for each user.</returns>
		[AcceptVerbs("GET")]
		public List<PersonnelStatusResult> GetPersonnelStatusesForLink(int linkId)
		{
			var link = _departmentLinksService.GetLinkById(linkId);
			if (link.DepartmentId != DepartmentId && link.LinkedDepartmentId != DepartmentId)
				return new List<PersonnelStatusResult>();

			var results = new List<PersonnelStatusResult>();

			var actionLogs = _actionLogsService.GetActionLogsForDepartment(link.DepartmentId);
			var userStates = _userStateService.GetLatestStatesForDepartment(link.DepartmentId);
			var users = _departmentsService.GetAllUsersForDepartment(link.DepartmentId);
			Department department = _departmentsService.GetDepartmentById(link.DepartmentId, false);

			foreach (var u in users)
			{
				var log = (from l in actionLogs
						   where l.UserId == u.UserId
						   select l).FirstOrDefault();

				var state = (from l in userStates
							 where l.UserId == u.UserId
							 select l).FirstOrDefault();

				var s = new PersonnelStatusResult();
				s.Uid = u.UserId.ToString();

				if (log != null)
				{
					s.Atp = log.ActionTypeId;
					s.Atm = log.Timestamp.TimeConverter(department);

					if (log.DestinationId.HasValue)
					{
						if (log.ActionTypeId == (int)ActionTypes.RespondingToScene)
							s.Did = log.DestinationId.Value.ToString();
						else if (log.ActionTypeId == (int)ActionTypes.RespondingToStation)
							s.Did = log.DestinationId.Value.ToString();
						else if (log.ActionTypeId == (int)ActionTypes.AvailableStation)
							s.Did = log.DestinationId.Value.ToString();
					}
				}
				else
				{
					s.Atp = (int)ActionTypes.StandingBy;
					s.Atm = DateTime.UtcNow.TimeConverter(department);
				}

				if (state != null)
				{
					s.Ste = state.State;
					s.Stm = state.Timestamp.TimeConverter(department);
				}
				else
				{
					s.Ste = (int)UserStateTypes.Available;
					s.Stm = DateTime.UtcNow.TimeConverter(department);
				}
				results.Add(s);
			}


			return results;
		}

		/// <summary>
		/// Returns all the map markers for all active links
		/// </summary>
		/// <returns>Array of MapMakerInfo objects for each active call in all linked departments</returns>
		[AcceptVerbs("GET")]
		public List<MapMakerInfo> GetAllLinkedCallMapMarkers()
		{
			var result = new List<MapMakerInfo>();

			var links = _departmentLinksService.GetAllLinksForDepartment(DepartmentId);

			foreach (var link in links.Where(x => x.LinkEnabled && x.LinkedDepartmentId == DepartmentId))
			{
				var calls = _callsService.GetActiveCallsByDepartment(link.DepartmentId).OrderByDescending(x => x.LoggedOn);
				var department = _departmentsService.GetDepartmentById(link.DepartmentId, false);

				foreach (var call in calls)
				{
					MapMakerInfo info = new MapMakerInfo();
					info.ImagePath = "Call";
					info.Title = call.Name;
					info.InfoWindowContent = call.NatureOfCall;
					info.Color = link.DepartmentColor;

					if (!String.IsNullOrEmpty(call.GeoLocationData))
					{
						try
						{
							info.Latitude = double.Parse(call.GeoLocationData.Split(char.Parse(","))[0]);
							info.Longitude = double.Parse(call.GeoLocationData.Split(char.Parse(","))[1]);
						}
						catch { }
					}
					else if (!String.IsNullOrEmpty(call.Address))
					{
						string coordinates = _geoLocationProvider.GetLatLonFromAddress(call.Address);
						if (!String.IsNullOrEmpty(coordinates))
						{
							info.Latitude = double.Parse(coordinates.Split(char.Parse(","))[0]);
							info.Longitude = double.Parse(coordinates.Split(char.Parse(","))[1]);
						}
					}

					result.Add(info);
				}
			}

			return result;
		}

		/// <summary>
		/// Returns all the active calls for a specific department link
		/// </summary>
		/// <returns>Array of CallResult objects for each active call in the department</returns>
		[System.Web.Http.AcceptVerbs("GET")]
		public List<LinkedCallResult> GetAllActiveCallsForLinks()
		{
			var result = new List<LinkedCallResult>();

			var links = _departmentLinksService.GetAllLinksForDepartment(DepartmentId);

			foreach (var link in links)
			{
				var calls = _callsService.GetActiveCallsByDepartment(link.DepartmentId).OrderByDescending(x => x.LoggedOn);
				var department = _departmentsService.GetDepartmentById(link.DepartmentId, false);

				foreach (var c in calls)
				{
					var call = new LinkedCallResult();

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
						call.Aud = c.Attachments.Count(x => x.CallAttachmentType == (int) CallAttachmentTypes.DispatchAudio);
						call.Img = c.Attachments.Count(x => x.CallAttachmentType == (int) CallAttachmentTypes.Image);
						call.Fls = c.Attachments.Count(x => x.CallAttachmentType == (int) CallAttachmentTypes.File);
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
					call.Ste = c.State;
					call.Num = c.Number;

					call.Color = link.LinkedDepartmentColor;
					call.Priority = c.ToCallPriorityDisplayText();
					call.PriorityCss = c.ToCallPriorityCss();
					call.State = c.ToCallStateDisplayText();
					call.StateCss = c.ToCallStateCss();

					result.Add(call);
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