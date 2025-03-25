using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using System.Threading.Tasks;
using Resgrid.Web.Services.Helpers;
using Resgrid.Model;
using System;
using System.Net.Mime;
using System.Threading;
using Resgrid.Web.Services.Models.v4.PersonnelStatuses;
using Resgrid.Framework;
using System.Collections.Generic;
using Resgrid.Model.Helpers;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Operations to perform against personnel in a department
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class PersonnelStatusesController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly IUsersService _usersService;
		private readonly IActionLogsService _actionLogsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IUserStateService _userStateService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly Model.Services.IAuthorizationService _authorizationService;

		public PersonnelStatusesController(
			IUsersService usersService,
			IActionLogsService actionLogsService,
			IDepartmentsService departmentsService,
			IUserProfileService userProfileService,
			IUserStateService userStateService,
			IDepartmentGroupsService departmentGroupsService,
			IPersonnelRolesService personnelRolesService,
			IDepartmentSettingsService departmentSettingsService,
			Model.Services.IAuthorizationService authorizationService
			)
		{
			_usersService = usersService;
			_actionLogsService = actionLogsService;
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
			_userStateService = userStateService;
			_departmentGroupsService = departmentGroupsService;
			_personnelRolesService = personnelRolesService;
			_departmentSettingsService = departmentSettingsService;
			_authorizationService = authorizationService;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Gets the current status for a user
		/// </summary>
		/// <param name="userId">UserId to get the status for</param>
		/// <returns></returns>
		[HttpGet("GetCurrentStatus")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Personnel_View)]
		public async Task<ActionResult<GetCurrentStatusResult>> GetCurrentStatus(string userId)
		{
			var result = new GetCurrentStatusResult();

			if (string.IsNullOrEmpty(userId))
				userId = UserId;

			var action = await _actionLogsService.GetLastActionLogForUserAsync(userId, DepartmentId);
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			if (action != null)
			{
				result.Data = ConvertPersonStatus(action, department, userId);
				result.PageSize = 1;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.Data = null;
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// Saves a status for a person
		/// </summary>
		/// <param name="input">Status and related data</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>ActionResult.</returns>
		[HttpPost("SavePersonStatus")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<ActionResult<SavePersonStatusResult>> SavePersonStatus(SavePersonStatusInput input, CancellationToken cancellationToken)
		{
			if (!ModelState.IsValid)
				return BadRequest();

			var result = new SavePersonStatusResult();

			try
			{
				var userToSetStatusFor = await _departmentsService.GetDepartmentMemberAsync(input.UserId, DepartmentId);

				if (userToSetStatusFor == null)
				{
					ResponseHelper.PopulateV4ResponseNotFound(result);
					return Ok(result);
				}

				if (!await _authorizationService.IsUserValidWithinLimitsAsync(UserId, DepartmentId))
					return Unauthorized();

				if (!await _authorizationService.IsUserValidWithinLimitsAsync(userToSetStatusFor.UserId, DepartmentId))
					return Unauthorized();

				if (DepartmentId != userToSetStatusFor.DepartmentId)
					return Unauthorized();

				// TODO: We need to check here if the user is a department admin, or the admin that the user is a part of

				string geolocation = null;
				if (!String.IsNullOrWhiteSpace(input.Latitude) && !String.IsNullOrWhiteSpace(input.Longitude))
					geolocation = $"{input.Latitude},{input.Longitude}";

				ActionLog log = null;
				if (String.IsNullOrWhiteSpace(input.RespondingTo) || input.RespondingTo == "0")
					log = await _actionLogsService.SetUserActionAsync(input.UserId, DepartmentId, int.Parse(input.Type), geolocation, cancellationToken);
				else
					log = await _actionLogsService.SetUserActionAsync(input.UserId, DepartmentId, int.Parse(input.Type), geolocation, int.Parse(input.RespondingTo), input.Note, cancellationToken);

				result.Id = log.ActionLogId.ToString();
				result.PageSize = 0;
				result.Status = ResponseHelper.Created;
				ResponseHelper.PopulateV4ResponseData(result);

				return Created($"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/v4/Statuses/GetAllStatusesForPersonnel", result);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return BadRequest();
			}
		}

		/// <summary>
		/// Saves a status for multiple personnel
		/// </summary>
		/// <param name="input">Status and related data</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>ActionResult.</returns>
		[HttpPost("SavePersonsStatuses")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<ActionResult<SavePersonsStatusesResult>> SavePersonsStatuses(SavePersonsStatusesInput input, CancellationToken cancellationToken)
		{
			var result = new SavePersonsStatusesResult();

			if (!ModelState.IsValid)
				return BadRequest();

			List<string> logIds = new List<string>();
			foreach (var userId in input.UserIds)
			{
				var userToSetStatusFor = await _departmentsService.GetDepartmentMemberAsync(userId, DepartmentId);

				if (userToSetStatusFor == null)
					continue;

				if (!await _authorizationService.IsUserValidWithinLimitsAsync(UserId, DepartmentId))
					continue;

				if (!await _authorizationService.IsUserValidWithinLimitsAsync(userToSetStatusFor.UserId, DepartmentId))
					continue;

				if (DepartmentId != userToSetStatusFor.DepartmentId)
					continue;

				string geolocation = null;
				if (!String.IsNullOrWhiteSpace(input.Latitude) && !String.IsNullOrWhiteSpace(input.Longitude))
					geolocation = $"{input.Latitude},{input.Longitude}";

				ActionLog log = null;
				if (String.IsNullOrWhiteSpace(input.RespondingTo) || input.RespondingTo == "0")
					log = await _actionLogsService.SetUserActionAsync(userId, DepartmentId, int.Parse(input.Type), geolocation, cancellationToken);
				else
					log = await _actionLogsService.SetUserActionAsync(userId, DepartmentId, int.Parse(input.Type), geolocation, int.Parse(input.RespondingTo), input.Note, cancellationToken);

				logIds.Add(log.ActionLogId.ToString());
			}

			result.Ids = logIds;
			result.PageSize = 0;
			result.Status = ResponseHelper.Created;
			ResponseHelper.PopulateV4ResponseData(result);

			return Created($"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/v4/Statuses/GetAllStatusesForPersonnel", result);
		}

		public static GetCurrentStatusResultData ConvertPersonStatus(ActionLog actionLog, Department department, string userId)
		{
			var statusResult = new GetCurrentStatusResultData
			{
				StatusType = (int)ActionTypes.StandingBy,
				UserId = userId,
				DepartmentId = department.DepartmentId.ToString()
			};

			if (actionLog == null)
			{
				statusResult.TimestampUtc = DateTime.UtcNow;
				statusResult.Timestamp = DateTime.UtcNow.TimeConverter(department);
			}
			else
			{
				statusResult.StatusType = actionLog.ActionTypeId;
				statusResult.TimestampUtc = actionLog.Timestamp;
				statusResult.Timestamp = actionLog.Timestamp.TimeConverter(department);
				statusResult.GeoLocationData = actionLog.GeoLocationData;
				statusResult.Note = actionLog.Note;

				if (actionLog.DestinationId.HasValue)
				{
					statusResult.DestinationId = actionLog.DestinationId.Value;

					if (actionLog.DestinationType.HasValue)
						statusResult.DestinationType = actionLog.DestinationType.Value;
					else
						statusResult.DestinationType = 2; // Call (1 = Group)
				}
			}

			return statusResult;
		}
	}
}
