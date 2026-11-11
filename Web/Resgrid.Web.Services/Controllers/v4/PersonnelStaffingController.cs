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
using System.Linq;
using Resgrid.Web.Services.Models.v4.PersonnelStaffing;
using Resgrid.Model.Helpers;
using Resgrid.Web.ServicesCore.Helpers;

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
		private readonly IProtectedReadService _protectedReadService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly Model.Services.IAuthorizationService _authorizationService;
		private readonly IInboundEmailReceiptsService _inboundEmailReceiptsService;

		/// <summary>
		/// The caller's Protected Data Grant, if they presented one. Absent means a staffing note
		/// resolves to the REDACTED placeholder rather than plaintext.
		/// </summary>
		private string ProtectedGrantToken => Request.Headers[DataProtectionController.GrantHeader].ToString();

		public PersonnelStaffingController(
			IUsersService usersService,
			IActionLogsService actionLogsService,
			IDepartmentsService departmentsService,
			IUserProfileService userProfileService,
			IUserStateService userStateService,
			IDepartmentGroupsService departmentGroupsService,
			IPersonnelRolesService personnelRolesService,
			IDepartmentSettingsService departmentSettingsService,
			Model.Services.IAuthorizationService authorizationService,
			IProtectedReadService protectedReadService,
			IInboundEmailReceiptsService inboundEmailReceiptsService
			)
		{
			_inboundEmailReceiptsService = inboundEmailReceiptsService;
			_protectedReadService = protectedReadService;
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
		/// Gets the current staffing for a user
		/// </summary>
		/// <param name="userId">UserId to get the status for</param>
		/// <returns></returns>
		[HttpGet("GetCurrentStatffing")]
		[HttpGet("GetCurrentStaffing")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Personnel_View)]
		public async Task<ActionResult<GetCurrentStaffingResult>> GetCurrentStatffing(string userId)
		{
			var result = new GetCurrentStaffingResult();

			if (string.IsNullOrEmpty(userId))
				userId = UserId;

			var member = await _departmentsService.GetDepartmentMemberAsync(userId, DepartmentId);

			if (member == null)
			{
				result.Data = null;
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;

				ResponseHelper.PopulateV4ResponseData(result);

				return result;
			}

			var userState = await _userStateService.GetLastUserStateByUserIdAsync(userId);

			// ADP (catalog v9): a staffing note is often why someone is unavailable. With a grant it
			// comes back as plaintext; without one as the REDACTED placeholder, never ciphertext.
			if (userState != null)
				await _protectedReadService.ResolveUserStatesForReadAsync(DepartmentId, new[] { userState },
					ProtectedGrantToken, UserId);

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			if (userState != null)
			{
				result.Data = ConvertPersonStaffing(userState, department, userId);
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
			var receipt = InboundEmailReceiptClaim.Untracked;

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

				if (input.UserId != UserId && !ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
					return Unauthorized();

				if (!int.TryParse(input.Type, out var staffingType))
					return BadRequest();

				// An app's offline outbox resends a staffing whose answer it lost with the same EventId and body
				// (StatusWriteReplayKeys); the replay gets the staffing the first attempt saved instead of a second one. Claimed
				// once the request is known to be valid. No EventId (or no client timestamp) is untracked, as before.
				receipt = await _inboundEmailReceiptsService.ClaimApiRequestAsync(InboundEmailKinds.ApiPersonStaffing, DepartmentId,
					StatusWriteReplayKeys.ForPersonStaffing(input), cancellationToken);

				if (receipt.Outcome == InboundEmailClaimOutcomes.InProgress)
					return StatusCode(StatusCodes.Status409Conflict);

				if (receipt.EntityId.HasValue &&
					(receipt.Outcome == InboundEmailClaimOutcomes.AlreadyProcessed || receipt.Outcome == InboundEmailClaimOutcomes.Resume))
				{
					await _inboundEmailReceiptsService.MarkCompletedAsync(receipt);
					return SavedPersonStaffingResult(result, receipt.EntityId.Value);
				}

				if (receipt.Outcome == InboundEmailClaimOutcomes.AlreadyProcessed)
					return StatusCode(StatusCodes.Status409Conflict);

				if (String.IsNullOrWhiteSpace(input.Note))
					savedState = await _userStateService.CreateUserState(userToSetStatusFor.UserId, DepartmentId, staffingType, cancellationToken);
				else
					savedState = await _userStateService.CreateUserState(userToSetStatusFor.UserId, DepartmentId, staffingType, input.Note, cancellationToken);

				await _inboundEmailReceiptsService.MarkSavedAsync(receipt, savedState.UserStateId, InboundEmailPendingSteps.None);
				await _inboundEmailReceiptsService.MarkCompletedAsync(receipt);

				return SavedPersonStaffingResult(result, savedState.UserStateId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				// Gives the claim back so the app's next replay is handled at once.
				await _inboundEmailReceiptsService.ReleaseAsync(receipt);
				return BadRequest();
			}
		}

		private ActionResult<SavePersonnelStaffingResult> SavedPersonStaffingResult(SavePersonnelStaffingResult result, int userStateId)
		{
			result.Id = userStateId.ToString();
			result.PageSize = 0;
			result.Status = ResponseHelper.Created;
			ResponseHelper.PopulateV4ResponseData(result);

			return Created($"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/v4/Statuses/GetAllStaffingsForPersonnel", result);
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

			// Validate and parse Type once
			if (!int.TryParse(input.Type, out var typeVal))
				return BadRequest("`Type` must be a valid integer.");

			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin() && input.UserIds.Any(x => x != UserId))
				return Unauthorized();

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
					savedState = await _userStateService.CreateUserState(userToSetStatusFor.UserId, DepartmentId, typeVal, cancellationToken);
				else
					savedState = await _userStateService.CreateUserState(userToSetStatusFor.UserId, DepartmentId, typeVal, input.Note, cancellationToken);

				logIds.Add(savedState.UserStateId.ToString());
			}

			result.Ids = logIds;
			result.PageSize = 0;
			result.Status = ResponseHelper.Created;
			ResponseHelper.PopulateV4ResponseData(result);

			return Created($"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/v4/Statuses/GetAllStaffingsForPersonnel", result);
		}

		public static GetCurrentStaffingResultData ConvertPersonStaffing(UserState userState, Department department, string userId)
		{
			var staffingResult = new GetCurrentStaffingResultData
			{
				StaffingType = (int)UserStateTypes.Available,
				UserId = userId,
				DepartmentId = department.DepartmentId.ToString()
			};

			if (userState == null)
			{
				staffingResult.TimestampUtc = DateTime.UtcNow;
				staffingResult.Timestamp = DateTime.UtcNow.TimeConverter(department);
			}
			else
			{
				staffingResult.StaffingType = userState.State;
				staffingResult.TimestampUtc = userState.Timestamp;
				staffingResult.Timestamp = userState.Timestamp.TimeConverter(department);
				staffingResult.Note = userState.Note;
			}

			return staffingResult;
		}
	}
}
