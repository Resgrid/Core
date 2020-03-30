using System.Net;
using System.Net.Http;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using Resgrid.Services.CoreWeb;
using Resgrid.Web.Services.Controllers.Version3.Models.Status;
using System;
using System.Web.Http;
using System.Web.Http.Cors;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Providers.Bus;
using StatusResult = Resgrid.Web.Services.Controllers.Version3.Models.Status.StatusResult;
using Resgrid.Web.Services.Helpers;

namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Operations to perform against user statuses and their actions
	/// </summary>
	public class StatusController : V3AuthenticatedApiControllerbase
	{
		private readonly IUsersService _usersService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IUserStateService _userStateService;
		private readonly IAuthorizationService _authorizationService;
		private IWebEventPublisher _webEventPublisher;
		private readonly IOutboundEventProvider _outboundEventProvider;

		public StatusController(
			IUsersService usersService,
			IActionLogsService actionLogsService,
			IDepartmentsService departmentsService,
			IUserProfileService userProfileService,
			IDepartmentGroupsService departmentGroupsService,
			IUserStateService userStateService,
			IWebEventPublisher webEventPublisher,
			IAuthorizationService authorizationService,
			IOutboundEventProvider outboundEventProvider)
		{
			_usersService = usersService;
			_actionLogsService = actionLogsService;
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
			_webEventPublisher = webEventPublisher;
			_userStateService = userStateService;
			_departmentGroupsService = departmentGroupsService;
			_authorizationService = authorizationService;
			_outboundEventProvider = outboundEventProvider;
		}

		/// <summary>
		/// Gets the status/action for the current user. User credentials are supplied via the Auth header.
		/// </summary>
		/// <returns>StatusResult object with the users current status</returns>
		[AcceptVerbs("GET")]
		public StatusResult GetCurrentUserStatus()
		{
			var action = _actionLogsService.GetLastActionLogForUser(UserId, DepartmentId);
			var userState = _userStateService.GetLastUserStateByUserId(UserId);
			var department = _departmentsService.GetDepartmentById(DepartmentId, false);

			var statusResult = new StatusResult
			{
				Act = (int)ActionTypes.StandingBy,
				Uid = UserId.ToString(),
				Ste = userState.State,
				Sts = userState.Timestamp.TimeConverter(department)
			};

			if (action == null)
			{
				statusResult.Ats = DateTime.UtcNow.TimeConverter(department);
			}
			else
			{
				statusResult.Act = action.ActionTypeId;
				statusResult.Ats = action.Timestamp.TimeConverter(department);

				if (action.DestinationId.HasValue)
				{
					if (action.ActionTypeId == (int)ActionTypes.RespondingToScene)
						statusResult.Did = action.DestinationId.Value;
					else if (action.ActionTypeId == (int)ActionTypes.RespondingToStation)
						statusResult.Did = action.DestinationId.Value;
					else if (action.ActionTypeId == (int)ActionTypes.AvailableStation)
						statusResult.Did = action.DestinationId.Value;
				}
			}

			return statusResult;
		}

		[System.Web.Http.Description.ApiExplorerSettings(IgnoreApi = true)]
		[AcceptVerbs("GET")]
		public StatusResult GetCurrentStatus()
		{
			var action = _actionLogsService.GetLastActionLogForUser(UserId, DepartmentId);
			var userState = _userStateService.GetLastUserStateByUserId(UserId);
			var department = _departmentsService.GetDepartmentById(DepartmentId, false);

			var statusResult = new StatusResult
			{
				Act = (int)ActionTypes.StandingBy,
				Uid = UserId.ToString(),
				Ste = userState.State,
				Sts = userState.Timestamp.TimeConverter(department)
			};

			if (action == null)
			{
				statusResult.Ats = DateTime.UtcNow.TimeConverter(department);
			}
			else
			{
				statusResult.Act = action.ActionTypeId;
				statusResult.Ats = action.Timestamp.TimeConverter(department);

				if (action.DestinationId.HasValue)
				{
					if (action.ActionTypeId == (int)ActionTypes.RespondingToScene)
						statusResult.Did = action.DestinationId.Value;
					else if (action.ActionTypeId == (int)ActionTypes.RespondingToStation)
						statusResult.Did = action.DestinationId.Value;
					else if (action.ActionTypeId == (int)ActionTypes.AvailableStation)
						statusResult.Did = action.DestinationId.Value;
				}
			}

			return statusResult;
		}

		/// <summary>
		/// Sets the status/action for the current user.
		/// </summary>
		/// <param name="statusInput">StatusInput object with the Status/Action to set.</param>
		/// <returns>Returns HttpStatusCode Created if successful, BadRequest otherwise.</returns>
		[AcceptVerbs("POST")]
		public HttpResponseMessage SetCurrentStatus(StatusInput statusInput)
		{
			if (this.ModelState.IsValid)
			{
				try
				{
					ActionLog log = null;
					if (statusInput.Rto == 0)
						log = _actionLogsService.SetUserAction(UserId, DepartmentId, statusInput.Typ, statusInput.Geo, statusInput.Not);
					else if (statusInput.Dtp == 0)
						log = _actionLogsService.SetUserAction(UserId, DepartmentId, statusInput.Typ, statusInput.Geo, statusInput.Rto, statusInput.Not);
					else
						log = _actionLogsService.SetUserAction(UserId, DepartmentId, statusInput.Typ, statusInput.Geo, statusInput.Rto, statusInput.Dtp, statusInput.Not);

					OutboundEventProvider.PersonnelStatusChangedTopicHandler handler = new OutboundEventProvider.PersonnelStatusChangedTopicHandler();
					handler.Handle(new UserStatusEvent() { DepartmentId = DepartmentId, Status = log });

					return Request.CreateResponse(HttpStatusCode.Created);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
					throw HttpStatusCode.InternalServerError.AsException();
				}
			}

			throw HttpStatusCode.BadRequest.AsException();
		}

		/// <summary>
		/// Sets the status/action for the UserId passed into the data posted
		/// </summary>
		/// <param name="statusInput">StatusInput object with the Status/Action to set.</param>
		/// <returns>Returns HttpStatusCode Created if successful, BadRequest otherwise.</returns>
		//[System.Web.Http.AcceptVerbs(new string[] { "PUT", "POST"})]
		[HttpPut]
		public HttpResponseMessage SetStatusForUser(StatusInput statusInput)
		{
			if (this.ModelState.IsValid)
			{
				try
				{
					var userToSetStatusFor = _departmentsService.GetDepartmentMember(statusInput.Uid, DepartmentId);

					if (userToSetStatusFor == null)
						throw HttpStatusCode.NotFound.AsException();

					if (!_authorizationService.IsUserValidWithinLimits(statusInput.Uid, DepartmentId))
						throw HttpStatusCode.Unauthorized.AsException();

					if (!_authorizationService.IsUserValidWithinLimits(userToSetStatusFor.UserId, DepartmentId))
						throw HttpStatusCode.Unauthorized.AsException();

					if (DepartmentId != userToSetStatusFor.DepartmentId)
						throw HttpStatusCode.Unauthorized.AsException();

					// TODO: We need to check here if the user is a department admin, or the admin that the user is a part of

					ActionLog log = null;
					if (statusInput.Rto == 0)
						log = _actionLogsService.SetUserAction(statusInput.Uid, DepartmentId, statusInput.Typ, statusInput.Geo);
					else if (statusInput.Dtp == 0)
						log = _actionLogsService.SetUserAction(statusInput.Uid, DepartmentId, statusInput.Typ, statusInput.Geo, statusInput.Rto, statusInput.Not);
					else
						log = _actionLogsService.SetUserAction(statusInput.Uid, DepartmentId, statusInput.Typ, statusInput.Geo, statusInput.Rto);

					OutboundEventProvider.PersonnelStatusChangedTopicHandler handler = new OutboundEventProvider.PersonnelStatusChangedTopicHandler();
					handler.Handle(new UserStatusEvent() { DepartmentId = DepartmentId, Status = log });

					var response = Request.CreateResponse(HttpStatusCode.Created);
					response.Headers.Add("Access-Control-Allow-Origin", "*");
					response.Headers.Add("Access-Control-Allow-Methods", "GET,POST,PUT,DELETE,OPTIONS");
					return response;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
					throw HttpStatusCode.InternalServerError.AsException();
				}
			}

			throw HttpStatusCode.BadRequest.AsException();
		}

		/// <summary>
		/// Sets the status/action for the UserId passed into the data posted
		/// </summary>
		/// <param name="statusInput">StatusInput object with the Status/Action to set.</param>
		/// <returns>Returns HttpStatusCode Created if successful, BadRequest otherwise.</returns>
		//[System.Web.Http.AcceptVerbs(new string[] { "PUT", "POST"})]
		[HttpPost]
		public HttpResponseMessage PostStatusForUser(StatusInput statusInput)
		{
			if (this.ModelState.IsValid)
			{
				try
				{
					var userToSetStatusFor = _departmentsService.GetDepartmentMember(statusInput.Uid, DepartmentId);

					if (userToSetStatusFor == null)
						throw HttpStatusCode.NotFound.AsException();

					if (!_authorizationService.IsUserValidWithinLimits(statusInput.Uid, DepartmentId))
						throw HttpStatusCode.Unauthorized.AsException();

					if (!_authorizationService.IsUserValidWithinLimits(userToSetStatusFor.UserId, DepartmentId))
						throw HttpStatusCode.Unauthorized.AsException();

					if (DepartmentId != userToSetStatusFor.DepartmentId)
						throw HttpStatusCode.Unauthorized.AsException();

					// TODO: We need to check here if the user is a department admin, or the admin that the user is a part of

					ActionLog log = null;
					if (statusInput.Rto == 0)
						log = _actionLogsService.SetUserAction(statusInput.Uid, DepartmentId, statusInput.Typ, statusInput.Geo);
					else if (statusInput.Dtp == 0)
						log = _actionLogsService.SetUserAction(statusInput.Uid, DepartmentId, statusInput.Typ, statusInput.Geo, statusInput.Rto, statusInput.Not);
					else
						log = _actionLogsService.SetUserAction(statusInput.Uid, DepartmentId, statusInput.Typ, statusInput.Geo, statusInput.Rto);

					OutboundEventProvider.PersonnelStatusChangedTopicHandler handler = new OutboundEventProvider.PersonnelStatusChangedTopicHandler();
					handler.Handle(new UserStatusEvent() { DepartmentId = DepartmentId, Status = log });

					var response = Request.CreateResponse(HttpStatusCode.Created);
					response.Headers.Add("Access-Control-Allow-Origin", "*");
					response.Headers.Add("Access-Control-Allow-Methods", "GET,POST,PUT,DELETE,OPTIONS");
					return response;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
					throw HttpStatusCode.InternalServerError.AsException();
				}
			}

			throw HttpStatusCode.BadRequest.AsException();
		}

		public HttpResponseMessage Options()
		{
			var response = new HttpResponseMessage();
			response.Headers.Add("Access-Control-Allow-Origin", "*");
			response.Headers.Add("Access-Control-Request-Headers", "*");
			response.Headers.Add("Access-Control-Allow-Methods", "GET,POST,PUT,DELETE,OPTIONS");

			response.StatusCode = HttpStatusCode.OK;
			return response;
		}
	}
}
