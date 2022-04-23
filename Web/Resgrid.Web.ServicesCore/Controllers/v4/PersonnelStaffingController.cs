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
using Resgrid.Framework;
using System.Collections.Generic;
using Resgrid.Web.Services.Models.v4.PersonnelStaffing;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Operations to perform against personnel staffing, i.e. Available, Delayed, in a department
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class PersonnelStaffingController : V4AuthenticatedApiControllerbase
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

		public PersonnelStaffingController(
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
		/// Saves a staffing for a person
		/// </summary>
		/// <param name="input">Staffing and related data</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>ActionResult.</returns>
		[HttpPost("SavePersonStaffing")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<ActionResult<SavePersonnelStaffingResult>> SavePersonStaffing(SavePersonnelStaffingInput input, CancellationToken cancellationToken)
		{
			if (!ModelState.IsValid)
				return BadRequest();

			var result = new SavePersonnelStaffingResult();

			try
			{
				UserState savedState = null;
				var userToSetStatusFor = await _departmentsService.GetDepartmentMemberAsync(input.UserId, DepartmentId);

				if (userToSetStatusFor == null)
					return NotFound();

				if (!await _authorizationService.IsUserValidWithinLimitsAsync(input.UserId, DepartmentId))
					return Unauthorized();

				if (!await _authorizationService.IsUserValidWithinLimitsAsync(userToSetStatusFor.UserId, DepartmentId))
					return Unauthorized();

				// TODO: We need to check here if the user is a department admin, or the admin that the user is a part of

				if (String.IsNullOrWhiteSpace(input.Note))
					savedState = await _userStateService.CreateUserState(userToSetStatusFor.UserId, DepartmentId, int.Parse(input.Type), cancellationToken);
				else
					savedState = await _userStateService.CreateUserState(userToSetStatusFor.UserId, DepartmentId, int.Parse(input.Type), input.Note, cancellationToken);

				result.Id = savedState.UserStateId.ToString();
				result.PageSize = 0;
				result.Status = ResponseHelper.Created;
				ResponseHelper.PopulateV4ResponseData(result);

				return Created($"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/v4/Statuses/GetAllStaffingsForPersonnel", result);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return BadRequest();
			}
		}

		/// <summary>
		/// Saves a staffing for multiple personnel
		/// </summary>
		/// <param name="input">Staffing and related data</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>ActionResult.</returns>
		[HttpPost("SavePersonsStaffings")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<ActionResult<SavePersonnelStaffingsResult>> SavePersonsStaffings(SavePersonnelStaffingsInput input, CancellationToken cancellationToken)
		{
			var result = new SavePersonnelStaffingsResult();

			if (!ModelState.IsValid)
				return BadRequest();

			List<string> logIds = new List<string>();
			foreach (var userId in input.UserIds)
			{
				UserState savedState = null;
				var userToSetStatusFor = await _departmentsService.GetDepartmentMemberAsync(userId, DepartmentId);

				if (userToSetStatusFor == null)
					continue;

				if (!await _authorizationService.IsUserValidWithinLimitsAsync(userId, DepartmentId))
					continue;

				if (!await _authorizationService.IsUserValidWithinLimitsAsync(userToSetStatusFor.UserId, DepartmentId))
					continue;

				// TODO: We need to check here if the user is a department admin, or the admin that the user is a part of

				if (String.IsNullOrWhiteSpace(input.Note))
					savedState = await _userStateService.CreateUserState(userToSetStatusFor.UserId, DepartmentId, int.Parse(input.Type), cancellationToken);
				else
					savedState = await _userStateService.CreateUserState(userToSetStatusFor.UserId, DepartmentId, int.Parse(input.Type), input.Note, cancellationToken);

				logIds.Add(savedState.UserStateId.ToString());
			}

			result.Ids = logIds;
			result.PageSize = 0;
			result.Status = ResponseHelper.Created;
			ResponseHelper.PopulateV4ResponseData(result);

			return Created($"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/v4/Statuses/GetAllStaffingsForPersonnel", result);
		}
	}
}
