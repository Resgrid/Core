using Resgrid.Framework;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers.Version3.Models.Staffing;
using System;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Helpers;

namespace Resgrid.Web.Services.Controllers.Version3
{
	/// <summary>
	/// Operations to perform against user statuses and their actions
	/// </summary>
	[Route("api/v{version:ApiVersion}/[controller]")]
	[Produces("application/json")]
	[ApiVersion("3.0")]
	[ApiExplorerSettings(GroupName = "v3")]
	public class StaffingController : V3AuthenticatedApiControllerbase
	{
		private readonly IUsersService _usersService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUserStateService _userStateService;
		private readonly IUserProfileService _userProfileService;
		private readonly IAuthorizationService _authorizationService;

		public StaffingController(
			IUsersService usersService,
			IDepartmentsService departmentsService,
			IUserStateService userStateService,
			IUserProfileService userProfileService,
			IAuthorizationService authorizationService)
		{
			_usersService = usersService;
			_departmentsService = departmentsService;
			_userStateService = userStateService;
			_userProfileService = userProfileService;
			_authorizationService = authorizationService;
		}

		/// <summary>
		/// Gets the current staffing level (state) for the user
		/// </summary>
		/// <returns>StateResult object with the users current staffing level</returns>
		[HttpGet("GetCurrentStaffing")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<StaffingResult>> GetCurrentStaffing()
		{
			var profile = await _userProfileService.GetProfileByUserIdAsync(UserId);

			if (profile == null)
				return Unauthorized();

			var userState = await _userStateService.GetLastUserStateByUserIdAsync(UserId);

			var result = new StaffingResult
			{
				Uid = UserId.ToString(),
				Nme = profile.FullName.AsFirstNameLastName,
				Typ = userState.State,
				Tms = userState.Timestamp.TimeConverter(await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false)),
				Not = userState.Note
			};

			return Ok(result);
		}

		/// <summary>
		/// Sets the staffing level (state) for the current user.
		/// </summary>
		/// <param name="staffingInput">StateInput object with the State to set.</param>
		/// <returns>Returns HttpStatusCode Created if successful, BadRequest otherwise.</returns>
		[HttpPost("SetStaffing")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		public async Task<ActionResult> SetStaffing(StaffingInput staffingInput, CancellationToken cancellationToken)
		{
			if (this.ModelState.IsValid)
			{
				try
				{
					UserState savedState = null;

					if (String.IsNullOrWhiteSpace(staffingInput.Not))
						savedState = await _userStateService.CreateUserState(UserId, DepartmentId, staffingInput.Typ, cancellationToken);
					else
						savedState = await _userStateService.CreateUserState(UserId, DepartmentId, staffingInput.Typ, staffingInput.Not, cancellationToken);

					return CreatedAtAction(nameof(SetStaffing), new { id = savedState.UserStateId }, savedState);
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
		/// Sets the staffing level (state) for the UserId specificed in the input data.
		/// </summary>
		/// <param name="staffingInput">StateInput object with the State to set.</param>
		/// <returns>Returns HttpStatusCode Created if successful, BadRequest otherwise.</returns>
		[HttpPut("SetStaffingForUser")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		public async Task<ActionResult> SetStaffingForUser(StaffingInput staffingInput, CancellationToken cancellationToken)
		{
			if (this.ModelState.IsValid)
			{
				try
				{
					UserState savedState = null;
					var userToSetStatusFor = await _departmentsService.GetDepartmentMemberAsync(staffingInput.Uid, DepartmentId);

					if (userToSetStatusFor == null)
						return NotFound();

					if (!await _authorizationService.IsUserValidWithinLimitsAsync(staffingInput.Uid, DepartmentId))
						return Unauthorized();

					if (!await _authorizationService.IsUserValidWithinLimitsAsync(userToSetStatusFor.UserId, DepartmentId))
						return Unauthorized();

					// TODO: We need to check here if the user is a department admin, or the admin that the user is a part of

					if (String.IsNullOrWhiteSpace(staffingInput.Not))
						savedState = await _userStateService.CreateUserState(userToSetStatusFor.UserId, DepartmentId, staffingInput.Typ, cancellationToken);
					else
						savedState = await _userStateService.CreateUserState(userToSetStatusFor.UserId, DepartmentId, staffingInput.Typ, staffingInput.Not, cancellationToken);

					return CreatedAtAction(nameof(SetStaffingForUser), new { id = savedState.UserStateId }, savedState);
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
