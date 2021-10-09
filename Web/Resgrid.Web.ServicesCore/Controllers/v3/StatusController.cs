using System;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Bus;
using Resgrid.Web.Services.Controllers.Version3.Models.Status;
using Stripe;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;
using StatusResult = Resgrid.Web.Services.Controllers.Version3.Models.Status.StatusResult;


namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Operations to perform against user statuses and their actions
	/// </summary>
	[Route("api/v{version:ApiVersion}/[controller]")]
	[Produces("application/json")]
	[ApiVersion("3.0")]
	[ApiExplorerSettings(GroupName = "v3")]
	public class StatusController : V3AuthenticatedApiControllerbase
	{
		private readonly IUsersService _usersService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IUserStateService _userStateService;
		private readonly IAuthorizationService _authorizationService;
		private readonly IOutboundEventProvider _outboundEventProvider;
		private readonly IEventAggregator _eventAggregator;

		public StatusController(
			IUsersService usersService,
			IActionLogsService actionLogsService,
			IDepartmentsService departmentsService,
			IUserProfileService userProfileService,
			IDepartmentGroupsService departmentGroupsService,
			IUserStateService userStateService,
			IAuthorizationService authorizationService,
			IOutboundEventProvider outboundEventProvider,
			IEventAggregator eventAggregator)
		{
			_usersService = usersService;
			_actionLogsService = actionLogsService;
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
			_userStateService = userStateService;
			_departmentGroupsService = departmentGroupsService;
			_authorizationService = authorizationService;
			_outboundEventProvider = outboundEventProvider;
			_eventAggregator = eventAggregator;
		}

		/// <summary>
		/// Gets the status/action for the current user. User credentials are supplied via the Auth header.
		/// </summary>
		/// <returns>StatusResult object with the users current status</returns>
		[HttpGet("GetCurrentUserStatus")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<StatusResult>> GetCurrentUserStatus()
		{
			var action = await _actionLogsService.GetLastActionLogForUserAsync(UserId, DepartmentId);
			var userState = await _userStateService.GetLastUserStateByUserIdAsync(UserId);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

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

			return Ok(statusResult);
		}

		
		[HttpGet("GetCurrentStatus")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<StatusResult>> GetCurrentStatus()
		{
			var action = await _actionLogsService.GetLastActionLogForUserAsync(UserId, DepartmentId);
			var userState = await _userStateService.GetLastUserStateByUserIdAsync(UserId);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

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

			return Ok(statusResult);
		}

		/// <summary>
		/// <summary>
		/// Sets the status/action for the current user.
		/// </summary>
		/// <param name="statusInput">StatusInput object with the Status/Action to set.</param>
		/// <returns>Returns HttpStatusCode Created if successful, BadRequest otherwise.</returns>
		[HttpPost("SetCurrentStatus")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		public async Task<ActionResult> SetCurrentStatus(StatusInput statusInput, CancellationToken cancellationToken)
		{
			if (this.ModelState.IsValid)
			{
				try
				{
					ActionLog log = null;
					if (statusInput.Rto == 0)
						log = await _actionLogsService.SetUserActionAsync(UserId, DepartmentId, statusInput.Typ, statusInput.Geo, statusInput.Not, cancellationToken);
					else if (statusInput.Dtp == 0)
						log = await _actionLogsService.SetUserActionAsync(UserId, DepartmentId, statusInput.Typ, statusInput.Geo, statusInput.Rto, statusInput.Not, cancellationToken);
					else
						log = await _actionLogsService.SetUserActionAsync(UserId, DepartmentId, statusInput.Typ, statusInput.Geo, statusInput.Rto, statusInput.Dtp, statusInput.Not, cancellationToken);

					//OutboundEventProvider.PersonnelStatusChangedTopicHandler handler = new OutboundEventProvider.PersonnelStatusChangedTopicHandler();
					//await handler.Handle(new UserStatusEvent() { DepartmentId = DepartmentId, Status = log });
					_eventAggregator.SendMessage<UserStatusEvent>(new UserStatusEvent() { DepartmentId = DepartmentId, Status = log });

					return CreatedAtAction(nameof(SetCurrentStatus), new { id = log.ActionLogId }, log);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
					return BadRequest();
				}
			}

			return BadRequest();
		}

		/// <summary>
		/// Sets the status/action for the UserId passed into the data posted
		/// </summary>
		/// <param name="statusInput">StatusInput object with the Status/Action to set.</param>
		/// <returns>Returns HttpStatusCode Created if successful, BadRequest otherwise.</returns>
		//[System.Web.Http.AcceptVerbs(new string[] { "PUT", "POST"})]
		[HttpPut("SetStatusForUser")]
		[HttpPost("SetStatusForUser")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult> SetStatusForUser(StatusInput statusInput, CancellationToken cancellationToken)
		{
			if (this.ModelState.IsValid)
			{
				try
				{
					var userToSetStatusFor = await _departmentsService.GetDepartmentMemberAsync(statusInput.Uid, DepartmentId);

					if (userToSetStatusFor == null)
						return NotFound();

					if (!await _authorizationService.IsUserValidWithinLimitsAsync(statusInput.Uid, DepartmentId))
						return Unauthorized();

					if (!await _authorizationService.IsUserValidWithinLimitsAsync(userToSetStatusFor.UserId, DepartmentId))
						return Unauthorized();

					if (DepartmentId != userToSetStatusFor.DepartmentId)
						return Unauthorized();

					// TODO: We need to check here if the user is a department admin, or the admin that the user is a part of

					ActionLog log = null;
					if (statusInput.Rto == 0)
						log = await _actionLogsService.SetUserActionAsync(statusInput.Uid, DepartmentId, statusInput.Typ, statusInput.Geo, cancellationToken);
					else if (statusInput.Dtp == 0)
						log = await _actionLogsService.SetUserActionAsync(statusInput.Uid, DepartmentId, statusInput.Typ, statusInput.Geo, statusInput.Rto, statusInput.Not, cancellationToken);
					else
						log = await _actionLogsService.SetUserActionAsync(statusInput.Uid, DepartmentId, statusInput.Typ, statusInput.Geo, statusInput.Rto, cancellationToken);


					//OutboundEventProvider.PersonnelStatusChangedTopicHandler handler = new OutboundEventProvider.PersonnelStatusChangedTopicHandler();
					//await handler.Handle(new UserStatusEvent() { DepartmentId = DepartmentId, Status = log });
					_eventAggregator.SendMessage<UserStatusEvent>(new UserStatusEvent() { DepartmentId = DepartmentId, Status = log });

					return CreatedAtAction(nameof(SetStatusForUser), new { id = log.ActionLogId }, log);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
					return BadRequest();
				}
			}

			return BadRequest();
		}

		/// <summary>
		/// Sets the status/action for the UserId passed into the data posted
		/// </summary>
		/// <param name="statusInput">StatusInput object with the Status/Action to set.</param>
		/// <returns>Returns HttpStatusCode Created if successful, BadRequest otherwise.</returns>
		//[System.Web.Http.AcceptVerbs(new string[] { "PUT", "POST"})]
		[HttpPost("PostStatusForUser")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		public async Task<ActionResult> PostStatusForUser(StatusInput statusInput)
		{
			if (this.ModelState.IsValid)
			{
				try
				{
					var userToSetStatusFor = await _departmentsService.GetDepartmentMemberAsync(statusInput.Uid, DepartmentId);

					if (userToSetStatusFor == null)
						return NotFound();

					if (!await _authorizationService.IsUserValidWithinLimitsAsync(statusInput.Uid, DepartmentId))
						return Unauthorized();

					if (!await _authorizationService.IsUserValidWithinLimitsAsync(userToSetStatusFor.UserId, DepartmentId))
						return Unauthorized();

					if (DepartmentId != userToSetStatusFor.DepartmentId)
						return Unauthorized();

					// TODO: We need to check here if the user is a department admin, or the admin that the user is a part of

					ActionLog log = null;
					if (statusInput.Rto == 0)
						log = await _actionLogsService.SetUserActionAsync(statusInput.Uid, DepartmentId, statusInput.Typ, statusInput.Geo);
					else if (statusInput.Dtp == 0)
						log = await _actionLogsService.SetUserActionAsync(statusInput.Uid, DepartmentId, statusInput.Typ, statusInput.Geo, statusInput.Rto, statusInput.Not);
					else
						log = await _actionLogsService.SetUserActionAsync(statusInput.Uid, DepartmentId, statusInput.Typ, statusInput.Geo, statusInput.Rto);

					//OutboundEventProvider.PersonnelStatusChangedTopicHandler handler = new OutboundEventProvider.PersonnelStatusChangedTopicHandler();
					//await handler.Handle(new UserStatusEvent() { DepartmentId = DepartmentId, Status = log });
					_eventAggregator.SendMessage<UserStatusEvent>(new UserStatusEvent() { DepartmentId = DepartmentId, Status = log });

					return CreatedAtAction(nameof(SetStatusForUser), new { id = log.ActionLogId }, log);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
					return BadRequest();
				}
			}

			return BadRequest();
		}
	}
}
