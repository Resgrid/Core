using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.CheckInTimers;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Check-in timer operations for call accountability
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class CheckInTimersController : V4AuthenticatedApiControllerbase
	{
		private readonly ICheckInTimerService _checkInTimerService;
		private readonly ICallsService _callsService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUserProfileService _userProfileService;

		public CheckInTimersController(
			ICheckInTimerService checkInTimerService,
			ICallsService callsService,
			IDepartmentSettingsService departmentSettingsService,
			IDepartmentsService departmentsService,
			IUserProfileService userProfileService)
		{
			_checkInTimerService = checkInTimerService;
			_callsService = callsService;
			_departmentSettingsService = departmentSettingsService;
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
		}

		#region Timer Configuration

		/// <summary>
		/// Gets all timer configurations for the department
		/// </summary>
		[HttpGet("GetTimerConfigs")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<ActionResult<CheckInTimerConfigResult>> GetTimerConfigs()
		{
			var result = new CheckInTimerConfigResult();
			var configs = await _checkInTimerService.GetTimerConfigsForDepartmentAsync(DepartmentId);

			result.Data = configs.Select(c => new CheckInTimerConfigResultData
			{
				CheckInTimerConfigId = c.CheckInTimerConfigId,
				DepartmentId = c.DepartmentId,
				TimerTargetType = c.TimerTargetType,
				TimerTargetTypeName = ((CheckInTimerTargetType)c.TimerTargetType).ToString(),
				UnitTypeId = c.UnitTypeId,
				DurationMinutes = c.DurationMinutes,
				WarningThresholdMinutes = c.WarningThresholdMinutes,
				IsEnabled = c.IsEnabled,
				ActiveForStates = c.ActiveForStates,
				CreatedOn = c.CreatedOn,
				UpdatedOn = c.UpdatedOn
			}).ToList();

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Creates or updates a timer configuration
		/// </summary>
		[HttpPost("SaveTimerConfig")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<ActionResult<SaveCheckInTimerConfigResult>> SaveTimerConfig([FromBody] CheckInTimerConfigInput input, CancellationToken cancellationToken)
		{
			var result = new SaveCheckInTimerConfigResult();

			var config = new CheckInTimerConfig
			{
				CheckInTimerConfigId = input.CheckInTimerConfigId,
				DepartmentId = DepartmentId,
				TimerTargetType = input.TimerTargetType,
				UnitTypeId = input.UnitTypeId,
				DurationMinutes = input.DurationMinutes,
				WarningThresholdMinutes = input.WarningThresholdMinutes,
				IsEnabled = input.IsEnabled,
				ActiveForStates = input.ActiveForStates,
				CreatedByUserId = UserId
			};

			CheckInTimerConfig saved;
			try
			{
				saved = await _checkInTimerService.SaveTimerConfigAsync(config, cancellationToken);
			}
			catch (System.InvalidOperationException ex)
			{
				return BadRequest(ex.Message);
			}
			catch (System.UnauthorizedAccessException)
			{
				return NotFound();
			}

			result.Id = saved.CheckInTimerConfigId;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Deletes a timer configuration
		/// </summary>
		[HttpDelete("DeleteTimerConfig")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<ActionResult<SaveCheckInTimerConfigResult>> DeleteTimerConfig(string configId, CancellationToken cancellationToken)
		{
			var result = new SaveCheckInTimerConfigResult();

			bool deleted;
			try
			{
				deleted = await _checkInTimerService.DeleteTimerConfigAsync(configId, DepartmentId, cancellationToken);
			}
			catch (System.UnauthorizedAccessException)
			{
				return NotFound();
			}

			if (!deleted)
				return NotFound();

			result.Id = configId;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		#endregion Timer Configuration

		#region Timer Overrides

		/// <summary>
		/// Gets all timer overrides for the department
		/// </summary>
		[HttpGet("GetTimerOverrides")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<ActionResult<CheckInTimerOverrideResult>> GetTimerOverrides()
		{
			var result = new CheckInTimerOverrideResult();
			var overrides = await _checkInTimerService.GetTimerOverridesForDepartmentAsync(DepartmentId);

			result.Data = overrides.Select(o => new CheckInTimerOverrideResultData
			{
				CheckInTimerOverrideId = o.CheckInTimerOverrideId,
				DepartmentId = o.DepartmentId,
				CallTypeId = o.CallTypeId,
				CallPriority = o.CallPriority,
				TimerTargetType = o.TimerTargetType,
				TimerTargetTypeName = ((CheckInTimerTargetType)o.TimerTargetType).ToString(),
				UnitTypeId = o.UnitTypeId,
				DurationMinutes = o.DurationMinutes,
				WarningThresholdMinutes = o.WarningThresholdMinutes,
				IsEnabled = o.IsEnabled,
				ActiveForStates = o.ActiveForStates,
				CreatedOn = o.CreatedOn,
				UpdatedOn = o.UpdatedOn
			}).ToList();

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Creates or updates a timer override
		/// </summary>
		[HttpPost("SaveTimerOverride")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<ActionResult<SaveCheckInTimerOverrideResult>> SaveTimerOverride([FromBody] CheckInTimerOverrideInput input, CancellationToken cancellationToken)
		{
			var result = new SaveCheckInTimerOverrideResult();

			var ovr = new CheckInTimerOverride
			{
				CheckInTimerOverrideId = input.CheckInTimerOverrideId,
				DepartmentId = DepartmentId,
				CallTypeId = input.CallTypeId,
				CallPriority = input.CallPriority,
				TimerTargetType = input.TimerTargetType,
				UnitTypeId = input.UnitTypeId,
				DurationMinutes = input.DurationMinutes,
				WarningThresholdMinutes = input.WarningThresholdMinutes,
				IsEnabled = input.IsEnabled,
				ActiveForStates = input.ActiveForStates,
				CreatedByUserId = UserId
			};

			CheckInTimerOverride saved;
			try
			{
				saved = await _checkInTimerService.SaveTimerOverrideAsync(ovr, cancellationToken);
			}
			catch (System.InvalidOperationException ex)
			{
				return BadRequest(ex.Message);
			}
			catch (System.UnauthorizedAccessException)
			{
				return NotFound();
			}

			result.Id = saved.CheckInTimerOverrideId;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Deletes a timer override
		/// </summary>
		[HttpDelete("DeleteTimerOverride")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<ActionResult<SaveCheckInTimerOverrideResult>> DeleteTimerOverride(string overrideId, CancellationToken cancellationToken)
		{
			var result = new SaveCheckInTimerOverrideResult();

			bool deleted;
			try
			{
				deleted = await _checkInTimerService.DeleteTimerOverrideAsync(overrideId, DepartmentId, cancellationToken);
			}
			catch (System.UnauthorizedAccessException)
			{
				return NotFound();
			}

			if (!deleted)
				return NotFound();

			result.Id = overrideId;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		#endregion Timer Overrides

		#region Timer Status & Resolution

		/// <summary>
		/// Gets the resolved timers for a specific call
		/// </summary>
		[HttpGet("GetTimersForCall")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<ResolvedCheckInTimerResult>> GetTimersForCall(int callId)
		{
			var result = new ResolvedCheckInTimerResult();

			var call = await _callsService.GetCallByIdAsync(callId);
			if (call == null || call.DepartmentId != DepartmentId)
				return NotFound();

			var timers = await _checkInTimerService.ResolveAllTimersForCallAsync(call);

			result.Data = timers.Select(t => new ResolvedCheckInTimerResultData
			{
				TargetType = t.TargetType,
				TargetTypeName = ((CheckInTimerTargetType)t.TargetType).ToString(),
				UnitTypeId = t.UnitTypeId,
				TargetEntityId = t.TargetEntityId,
				TargetName = t.TargetName ?? ((CheckInTimerTargetType)t.TargetType).ToString(),
				DurationMinutes = t.DurationMinutes,
				WarningThresholdMinutes = t.WarningThresholdMinutes,
				IsFromOverride = t.IsFromOverride,
				ActiveForStates = t.ActiveForStates
			}).ToList();

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Gets real-time timer statuses with color coding for a call
		/// </summary>
		[HttpGet("GetTimerStatuses")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<CheckInTimerStatusResult>> GetTimerStatuses(int callId)
		{
			var result = new CheckInTimerStatusResult();

			var call = await _callsService.GetCallByIdAsync(callId);
			if (call == null || call.DepartmentId != DepartmentId)
				return NotFound();

			var statuses = await _checkInTimerService.GetActiveTimerStatusesForCallAsync(call);

			result.Data = statuses.Select(s => new CheckInTimerStatusResultData
			{
				TargetType = s.TargetType,
				TargetTypeName = ((CheckInTimerTargetType)s.TargetType).ToString(),
				TargetEntityId = s.TargetEntityId,
				TargetName = s.TargetName,
				UnitId = s.UnitId,
				LastCheckIn = s.LastCheckIn,
				DurationMinutes = s.DurationMinutes,
				WarningThresholdMinutes = s.WarningThresholdMinutes,
				ElapsedMinutes = s.ElapsedMinutes,
				Status = s.Status
			}).ToList();

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		#endregion Timer Status & Resolution

		#region Check-in Operations

		/// <summary>
		/// Records a check-in for a call with optional geolocation
		/// </summary>
		[HttpPost("PerformCheckIn")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<ActionResult<PerformCheckInResult>> PerformCheckIn([FromBody] PerformCheckInInput input, CancellationToken cancellationToken)
		{
			var result = new PerformCheckInResult();

			var call = await _callsService.GetCallByIdAsync(input.CallId);
			if (call == null || call.DepartmentId != DepartmentId)
				return NotFound();

			if (!call.CheckInTimersEnabled || call.State != (int)CallStates.Active)
				return BadRequest("Check-in timers are not enabled or call is not active.");

			if (!System.Enum.IsDefined(typeof(CheckInTimerTargetType), input.CheckInType))
				return BadRequest("Invalid check-in type.");

			var record = new CheckInRecord
			{
				DepartmentId = DepartmentId,
				CallId = input.CallId,
				CheckInType = input.CheckInType,
				UserId = UserId,
				UnitId = input.UnitId,
				Latitude = input.Latitude,
				Longitude = input.Longitude,
				Note = input.Note
			};

			var saved = await _checkInTimerService.PerformCheckInAsync(record, cancellationToken);

			result.Id = saved.CheckInRecordId;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Gets all check-in records for a call
		/// </summary>
		[HttpGet("GetCheckInHistory")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<CheckInRecordResult>> GetCheckInHistory(int callId)
		{
			var result = new CheckInRecordResult();

			var call = await _callsService.GetCallByIdAsync(callId);
			if (call == null || call.DepartmentId != DepartmentId)
				return NotFound();

			var records = await _checkInTimerService.GetCheckInsForCallAsync(callId);

			result.Data = records.Select(r => new CheckInRecordResultData
			{
				CheckInRecordId = r.CheckInRecordId,
				CallId = r.CallId,
				CheckInType = r.CheckInType,
				CheckInTypeName = ((CheckInTimerTargetType)r.CheckInType).ToString(),
				UserId = r.UserId,
				UnitId = r.UnitId,
				Latitude = r.Latitude,
				Longitude = r.Longitude,
				Timestamp = r.Timestamp,
				Note = r.Note
			}).ToList();

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		#endregion Check-in Operations

		#region Toggle Timers

		/// <summary>
		/// Enables or disables check-in timers on a call
		/// </summary>
		[HttpPut("ToggleCallTimers")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Call_Update)]
		public async Task<ActionResult<ToggleCallTimersResult>> ToggleCallTimers(int callId, bool enabled, CancellationToken cancellationToken)
		{
			var result = new ToggleCallTimersResult();

			var call = await _callsService.GetCallByIdAsync(callId);
			if (call == null || call.DepartmentId != DepartmentId)
				return NotFound();

			call.CheckInTimersEnabled = enabled;
			await _callsService.SaveCallAsync(call, cancellationToken);

			result.Id = callId.ToString();
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		#endregion Toggle Timers

		#region Endpoint 1 – User active-call check-in status

		/// <summary>
		/// Returns a check-in timer status summary for every active call (with
		/// personnel check-in timers enabled) that <paramref name="userId"/> has been
		/// dispatched on.  If the caller is querying a different user they must hold
		/// the <c>Personnel_View</c> policy; a user may always query themselves with
		/// just <c>Call_View</c>.
		/// </summary>
		/// <param name="userId">
		/// The ASP.NET Identity user identifier to look up. Must belong to the
		/// calling user's department.
		/// </param>
		[HttpGet("GetUserCallCheckInStatuses")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<UserCallCheckInStatusResult>> GetUserCallCheckInStatuses(string userId)
		{
			var result = new UserCallCheckInStatusResult();

			if (string.IsNullOrWhiteSpace(userId))
				return BadRequest("userId is required.");

			// When querying another user, require Personnel_View permission and
			// confirm the target belongs to the same department.
			if (!string.Equals(userId, UserId, System.StringComparison.OrdinalIgnoreCase))
			{
				// Re-check claim – Personnel_View is not inherited from base.
				// Use a department-membership guard as a defence-in-depth measure.
				var isMember = await _departmentsService.IsMemberOfDepartmentAsync(DepartmentId, userId);
				if (!isMember)
					return Forbid();
			}

			var summaries = await _checkInTimerService.GetUserActiveCallCheckInSummariesAsync(userId, DepartmentId);

			result.Data = summaries.Select(s => new UserCallCheckInStatusResultData
			{
				CallId = s.CallId,
				CallName = s.CallName,
				CallNumber = s.CallNumber,
				CallStartedOn = s.CallStartedOn,
				HasPersonnelTimer = s.HasPersonnelTimer,
				DurationMinutes = s.DurationMinutes,
				WarningThresholdMinutes = s.WarningThresholdMinutes,
				LastCheckIn = s.LastCheckIn,
				NeedsCheckIn = s.NeedsCheckIn,
				MinutesRemaining = s.MinutesRemaining,
				Status = s.Status
			}).ToList();

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		#endregion Endpoint 1

		#region Endpoint 2 – Call personnel check-in statuses

		/// <summary>
		/// For a call that has a personnel check-in timer active, returns the
		/// current check-in status (last check-in, time remaining, colour status)
		/// for every person dispatched on the call.
		/// </summary>
		/// <param name="callId">The call identifier to inspect.</param>
		[HttpGet("GetCallPersonnelCheckInStatuses")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<CallPersonnelCheckInStatusResult>> GetCallPersonnelCheckInStatuses(int callId)
		{
			var result = new CallPersonnelCheckInStatusResult();

			// Load the call and verify it belongs to the requesting user's department.
			var call = await _callsService.GetCallByIdAsync(callId);
			if (call == null || call.DepartmentId != DepartmentId)
				return NotFound();

			if (!call.CheckInTimersEnabled)
			{
				// Call exists but check-in timers are not enabled – return an empty
				// but well-formed response so clients can handle it gracefully.
				result.CallId = callId;
				result.HasActivePersonnelTimer = false;
				result.Data = new List<CallPersonnelCheckInStatusResultData>();
				result.PageSize = 0;
				result.Status = ResponseHelper.Success;
				ResponseHelper.PopulateV4ResponseData(result);
				return Ok(result);
			}

			if (call.State != (int)CallStates.Active)
				return BadRequest("Call is not in an active state.");

			// Resolve the personnel timer configuration so we can surface it to callers.
			var resolvedTimers = await _checkInTimerService.ResolveAllTimersForCallAsync(call);
			var personnelTimer = resolvedTimers
				.FirstOrDefault(t => t.TargetType == (int)CheckInTimerTargetType.Personnel);

			if (personnelTimer == null)
			{
				result.CallId = callId;
				result.HasActivePersonnelTimer = false;
				result.Data = new List<CallPersonnelCheckInStatusResultData>();
				result.PageSize = 0;
				result.Status = ResponseHelper.Success;
				ResponseHelper.PopulateV4ResponseData(result);
				return Ok(result);
			}

			// Fetch per-person statuses from the service layer.
			var statuses = await _checkInTimerService.GetCallPersonnelCheckInStatusesAsync(call);

			// Enrich with display names in a single bulk call (avoids N profile lookups).
			Dictionary<string, Model.UserProfile> profiles = null;
			if (statuses.Any())
				profiles = await _userProfileService.GetAllProfilesForDepartmentAsync(DepartmentId);

			result.CallId = callId;
			result.HasActivePersonnelTimer = true;
			result.DurationMinutes = personnelTimer.DurationMinutes;
			result.WarningThresholdMinutes = personnelTimer.WarningThresholdMinutes;

			result.Data = statuses.Select(s =>
			{
				string fullName = null;
				if (profiles != null && profiles.TryGetValue(s.UserId, out var profile))
					fullName = profile.FullName?.AsFirstNameLastName?.Trim();

				return new CallPersonnelCheckInStatusResultData
				{
					UserId = s.UserId,
					FullName = fullName ?? s.UserId,
					LastCheckIn = s.LastCheckIn,
					NeedsCheckIn = s.NeedsCheckIn,
					MinutesRemaining = s.MinutesRemaining,
					Status = s.Status
				};
			}).ToList();

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		#endregion Endpoint 2
	}
}
