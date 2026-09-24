using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Controllers.Version3.Models.Shifts;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Shifts;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Shifts: schedules, rosters, signups, trades and supervisor approvals
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class ShiftsController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private const int MaxRangeDays = 62;

		private readonly IShiftsService _shiftsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IAuthorizationService _authorizationService;

		public ShiftsController(
			IShiftsService shiftsService,
			IDepartmentsService departmentsService,
			IPersonnelRolesService personnelRolesService,
			IDepartmentGroupsService departmentGroupsService,
			IAuthorizationService authorizationService
		)
		{
			_shiftsService = shiftsService;
			_departmentsService = departmentsService;
			_personnelRolesService = personnelRolesService;
			_departmentGroupsService = departmentGroupsService;
			_authorizationService = authorizationService;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Gets all the shifts in a department
		/// </summary>
		/// <returns>List of ShiftResult objects.</returns>
		[HttpGet("GetShifts")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<ActionResult<ShiftsResult>> GetShifts()
		{
			var result = new ShiftsResult();
			var shifts = await _shiftsService.GetAllShiftsByDepartmentAsync(DepartmentId);
			var lookups = await GetLookupsAsync();

			foreach (var shift in shifts)
			{
				var shiftData = await _shiftsService.PopulateShiftData(shift, true, true, true, false, false);
				result.Data.Add(ConvertShift(shiftData, lookups));
			}

			result.PageSize = result.Data.Count;
			result.Status = result.Data.Any() ? ResponseHelper.Success : ResponseHelper.NotFound;

			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// Gets a single shift
		/// </summary>
		/// <param name="id">Shift id</param>
		/// <param name="shiftId">Legacy name for <paramref name="id"/> sent by older Responder builds</param>
		/// <returns>ShiftResult</returns>
		[HttpGet("GetShift")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<ActionResult<GetShiftResult>> GetShift(int id, [FromQuery] int? shiftId = null)
		{
			var result = new GetShiftResult();
			var shift = await _shiftsService.GetShiftByIdAsync(id > 0 ? id : shiftId.GetValueOrDefault());

			if (shift != null)
			{
				if (shift.DepartmentId != DepartmentId)
					return Unauthorized();

				var shiftData = await _shiftsService.PopulateShiftData(shift, true, true, true, false, false);

				result.Data = ConvertShift(shiftData, await GetLookupsAsync());
				result.PageSize = 1;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// Gets today's shift days (department local date), plus a night shift from yesterday that is still running
		/// </summary>
		/// <returns>List of ShiftDayResult objects.</returns>
		[HttpGet("GetTodaysShifts")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<ActionResult<ShiftDaysResult>> GetTodaysShifts()
		{
			var result = new ShiftDaysResult();
			var lookups = await GetLookupsAsync();
			var today = lookups.LocalNow.Date;

			var schedules = await _shiftsService.GetShiftDaySchedulesForDateRangeAsync(DepartmentId, today.AddDays(-1), today);

			foreach (var schedule in schedules.Where(x => x.Day.Day.Date == today || x.IsActive))
				result.Data.Add(ConvertSchedule(schedule, lookups, true));

			result.PageSize = result.Data.Count;
			result.Status = result.Data.Any() ? ResponseHelper.Success : ResponseHelper.NotFound;

			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// Gets a shift day with its roster, needs and the caller's status
		/// </summary>
		/// <param name="id">Shift day id</param>
		/// <param name="shiftDayId">Legacy name for <paramref name="id"/> sent by older Responder builds</param>
		[HttpGet("GetShiftDay")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<ActionResult<GetShiftDayResult>> GetShiftDay(int id, [FromQuery] int? shiftDayId = null)
		{
			var result = new GetShiftDayResult();
			var schedule = await _shiftsService.GetShiftDayScheduleAsync(id > 0 ? id : shiftDayId.GetValueOrDefault());

			if (schedule != null)
			{
				if (schedule.Shift.DepartmentId != DepartmentId)
					return Unauthorized();

				result.Data = ConvertSchedule(schedule, await GetLookupsAsync(), true);
				result.PageSize = 1;
				result.Status = ResponseHelper.Success;
			}
			else
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
			}

			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// Gets the department's shift days in a date range (department local dates, inclusive, at most 62 days)
		/// </summary>
		/// <param name="start">First day, yyyy-MM-dd</param>
		/// <param name="end">Last day, yyyy-MM-dd</param>
		/// <param name="shiftId">Only this shift</param>
		[HttpGet("GetShiftDaysForDateRange")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<ActionResult<ShiftDaysResult>> GetShiftDaysForDateRange(string start, string end, int? shiftId = null)
		{
			var result = new ShiftDaysResult();
			var lookups = await GetLookupsAsync();

			if (!TryGetRange(start, end, lookups.LocalNow.Date, 0, out var startDate, out var endDate))
			{
				result.Status = ResponseHelper.Failure;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			var schedules = await _shiftsService.GetShiftDaySchedulesForDateRangeAsync(DepartmentId, startDate, endDate, shiftId > 0 ? shiftId : null);

			foreach (var schedule in schedules)
				result.Data.Add(ConvertSchedule(schedule, lookups, false));

			result.PageSize = result.Data.Count;
			result.Status = result.Data.Any() ? ResponseHelper.Success : ResponseHelper.NotFound;

			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// Gets the shift days the caller is on (or waiting for approval on), default today to 30 days out
		/// </summary>
		/// <param name="start">First day, yyyy-MM-dd</param>
		/// <param name="end">Last day, yyyy-MM-dd</param>
		[HttpGet("GetMyShifts")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<ActionResult<ShiftDaysResult>> GetMyShifts(string start = null, string end = null)
		{
			var result = new ShiftDaysResult();
			var lookups = await GetLookupsAsync();

			if (!TryGetRange(start, end, lookups.LocalNow.Date, 30, out var startDate, out var endDate))
			{
				result.Status = ResponseHelper.Failure;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			var schedules = await _shiftsService.GetShiftDaySchedulesForDateRangeAsync(DepartmentId, startDate, endDate);

			foreach (var schedule in schedules.Where(x => x.Roster.Any(r => SameUser(r.UserId, UserId))))
				result.Data.Add(ConvertSchedule(schedule, lookups, false));

			result.PageSize = result.Data.Count;
			result.Status = result.Data.Any() ? ResponseHelper.Success : ResponseHelper.NotFound;

			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// Signs the caller up for a slot (group) on a shift day. On a shift that requires approval the signup waits
		/// for a supervisor.
		/// </summary>
		[HttpPost("SignupForShiftDay")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<ActionResult<SignupShiftDayResult>> SignupForShiftDay(ShiftDaySignupInput input, CancellationToken cancellationToken)
		{
			var result = new SignupShiftDayResult { Id = "" };

			if (input == null)
				return BadRequest();

			var shiftDay = await GetDepartmentShiftDayAsync(input.ShiftDayId);

			if (shiftDay == null)
				return NotFound();

			var signup = await _shiftsService.SignupUserForShiftDayAsync(shiftDay.ShiftDayId, input.GroupId, UserId, cancellationToken);

			if (signup.Success)
			{
				result.Id = signup.Item.ShiftSignupId.ToString();
				result.ApprovalPending = signup.Item.ApprovalPending;
				result.Status = ResponseHelper.Created;
			}
			else
			{
				result.ErrorCode = ToErrorCode(signup.Error);
				result.Status = ResponseHelper.Failure;
			}

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Withdraws a signup (the caller's own, or one in a group the caller supervises)
		/// </summary>
		[HttpPost("WithdrawFromShiftDay")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<ActionResult<ShiftOperationResult>> WithdrawFromShiftDay(WithdrawShiftSignupInput input, CancellationToken cancellationToken)
		{
			if (input == null)
				return BadRequest();

			var signup = await _shiftsService.GetShiftSignupByIdAsync(input.ShiftSignupId);
			var shift = signup != null ? await _shiftsService.GetShiftByIdAsync(signup.ShiftId) : null;

			if (shift == null || shift.DepartmentId != DepartmentId)
				return OperationResult(ShiftActionErrors.NotFound);

			if (!await _authorizationService.CanUserDeleteShiftSignupAsync(UserId, DepartmentId, signup.ShiftSignupId))
				return OperationResult(ShiftActionErrors.NotAllowed);

			// Withdrawing would silently take the day away from whoever it was traded to. Undo the trade first.
			if (signup.Trade != null && (signup.Trade.IsTradeComplete() || signup.Trade.ApprovalPending))
				return OperationResult(ShiftActionErrors.InvalidRequest);

			await _shiftsService.DeleteShiftSignupAsync(signup, cancellationToken);

			return OperationResult(ShiftActionErrors.None, signup.ShiftSignupId.ToString(), status: ResponseHelper.Deleted);
		}

		/// <summary>
		/// Gets the trades the caller started or was asked to take
		/// </summary>
		[HttpGet("GetShiftTrades")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<ActionResult<ShiftTradesResult>> GetShiftTrades()
		{
			var result = new ShiftTradesResult();
			var lookups = await GetLookupsAsync();
			var trades = await _shiftsService.GetTradesForUserAsync(DepartmentId, UserId);

			foreach (var trade in trades)
				result.Data.Add(await ConvertTradeAsync(trade, lookups));

			result.PageSize = result.Data.Count;
			result.Status = result.Data.Any() ? ResponseHelper.Success : ResponseHelper.NotFound;

			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// People the caller can ask to take their slot on a shift day: not on that day, and holding the roles the slot's
		/// group requires that the caller holds
		/// </summary>
		[HttpGet("GetTradeCandidates")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<ActionResult<ShiftPersonOptionsResult>> GetTradeCandidates(int shiftDayId)
		{
			var result = new ShiftPersonOptionsResult();
			var schedule = await _shiftsService.GetShiftDayScheduleAsync(shiftDayId);

			if (schedule == null || schedule.Shift.DepartmentId != DepartmentId)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			var lookups = await GetLookupsAsync();
			var myEntry = schedule.Roster.FirstOrDefault(x => SameUser(x.UserId, UserId) && x.IsOnDuty());

			if (myEntry == null)
			{
				result.Status = ResponseHelper.Failure;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			var requiredRoleIds = new List<int>();
			var myGroup = schedule.Shift.Groups?.FirstOrDefault(x => x.DepartmentGroupId == myEntry.DepartmentGroupId);

			if (myGroup?.Roles != null)
			{
				var myRoleIds = lookups.RoleIdsFor(UserId);
				requiredRoleIds = myGroup.Roles.Select(x => x.PersonnelRoleId).Where(myRoleIds.Contains).Distinct().ToList();
			}

			foreach (var option in await GetPersonnelOptionsAsync(schedule, lookups))
			{
				if (SameUser(option.UserId, UserId))
					continue;

				if (requiredRoleIds.All(option.RoleIds.Contains))
					result.Data.Add(option);
			}

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;

			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// Asks colleagues to take the caller's slot on a shift day
		/// </summary>
		[HttpPost("RequestShiftTrade")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<ActionResult<ShiftOperationResult>> RequestShiftTrade(RequestShiftTradeInput input, CancellationToken cancellationToken)
		{
			if (input == null)
				return BadRequest();

			if (await GetDepartmentShiftDayAsync(input.ShiftDayId) == null)
				return OperationResult(ShiftActionErrors.NotFound);

			var trade = await _shiftsService.RequestTradeAsync(input.ShiftDayId, UserId, input.UserIds, input.Note, cancellationToken);

			return trade.Success
				? OperationResult(ShiftActionErrors.None, trade.Item.ShiftSignupTradeId.ToString(), status: ResponseHelper.Created)
				: OperationResult(trade.Error);
		}

		/// <summary>
		/// Accepts (optionally offering days back) or declines a trade the caller was asked to take
		/// </summary>
		[HttpPost("RespondToShiftTrade")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<ActionResult<ShiftOperationResult>> RespondToShiftTrade(RespondToShiftTradeInput input, CancellationToken cancellationToken)
		{
			if (input == null)
				return BadRequest();

			if (!await IsDepartmentTradeAsync(input.ShiftSignupTradeId))
				return OperationResult(ShiftActionErrors.NotFound);

			var trade = await _shiftsService.RespondToTradeAsync(input.ShiftSignupTradeId, UserId, input.Accept, input.Note, input.OfferedShiftSignupIds, cancellationToken);

			return trade.Success
				? OperationResult(ShiftActionErrors.None, input.ShiftSignupTradeId.ToString(), status: ResponseHelper.Updated)
				: OperationResult(trade.Error);
		}

		/// <summary>
		/// The caller picks who takes their slot: a user taking it outright, or one of the offered swap-back days
		/// </summary>
		[HttpPost("FinishShiftTrade")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<ActionResult<ShiftOperationResult>> FinishShiftTrade(FinishShiftTradeInput input, CancellationToken cancellationToken)
		{
			if (input == null)
				return BadRequest();

			if (!await IsDepartmentTradeAsync(input.ShiftSignupTradeId))
				return OperationResult(ShiftActionErrors.NotFound);

			var trade = await _shiftsService.FinishTradeAsync(input.ShiftSignupTradeId, UserId, input.AcceptedUserId, input.TargetShiftSignupId, cancellationToken);

			return trade.Success
				? OperationResult(ShiftActionErrors.None, input.ShiftSignupTradeId.ToString(), trade.Item?.ApprovalPending ?? false, ResponseHelper.Updated)
				: OperationResult(trade.Error);
		}

		/// <summary>
		/// The caller withdraws a trade that has not taken effect
		/// </summary>
		[HttpPost("CancelShiftTrade")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<ActionResult<ShiftOperationResult>> CancelShiftTrade(CancelShiftTradeInput input, CancellationToken cancellationToken)
		{
			if (input == null)
				return BadRequest();

			if (!await IsDepartmentTradeAsync(input.ShiftSignupTradeId))
				return OperationResult(ShiftActionErrors.NotFound);

			var cancelled = await _shiftsService.CancelTradeAsync(input.ShiftSignupTradeId, UserId, cancellationToken);

			return cancelled.Success
				? OperationResult(ShiftActionErrors.None, input.ShiftSignupTradeId.ToString(), status: ResponseHelper.Deleted)
				: OperationResult(cancelled.Error);
		}

		/// <summary>
		/// Signups and trades waiting for approval in the groups the caller supervises
		/// </summary>
		[HttpGet("GetPendingApprovals")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<ActionResult<PendingShiftApprovalsResult>> GetPendingApprovals()
		{
			var result = new PendingShiftApprovalsResult();
			var lookups = await GetLookupsAsync();

			result.Data.IsSupervisor = lookups.Scope.IsSupervisor;

			if (lookups.Scope.IsSupervisor)
			{
				await GetShiftsAsync(lookups);

				foreach (var signup in await _shiftsService.GetPendingShiftSignupsAsync(DepartmentId))
				{
					if (!lookups.Scope.CanManageShiftGroup(signup.Shift, signup.DepartmentGroupId))
						continue;

					var day = FindDay(lookups, signup.ShiftId, signup.ShiftDay);

					result.Data.Signups.Add(new PendingShiftSignupResultData
					{
						ShiftSignupId = signup.ShiftSignupId.ToString(),
						UserId = signup.UserId,
						UserName = lookups.NameFor(signup.UserId),
						ShiftId = signup.ShiftId.ToString(),
						ShiftName = signup.Shift.Name,
						ShiftDayId = day?.ShiftDayId.ToString() ?? "",
						ShiftDay = signup.ShiftDay,
						Start = day?.Start ?? signup.ShiftDay,
						End = day?.End ?? signup.ShiftDay,
						GroupId = signup.DepartmentGroupId?.ToString() ?? "",
						GroupName = lookups.GroupNameFor(signup.DepartmentGroupId),
						SignupTimestamp = signup.SignupTimestamp,
						Roles = lookups.RoleNamesFor(signup.UserId)
					});
				}

				foreach (var trade in await _shiftsService.GetPendingTradesAsync(DepartmentId))
				{
					var converted = await ConvertTradeAsync(trade, lookups);

					if (converted.CanReview)
						result.Data.Trades.Add(converted);
				}
			}

			result.PageSize = result.Data.Signups.Count + result.Data.Trades.Count;
			result.Status = ResponseHelper.Success;

			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// A supervisor approves or denies a pending signup
		/// </summary>
		[HttpPost("ReviewShiftSignup")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<ActionResult<ShiftOperationResult>> ReviewShiftSignup(ReviewShiftSignupInput input, CancellationToken cancellationToken)
		{
			if (input == null)
				return BadRequest();

			var signup = await _shiftsService.GetShiftSignupByIdAsync(input.ShiftSignupId);
			var shift = signup != null ? await _shiftsService.GetShiftByIdAsync(signup.ShiftId) : null;

			if (shift == null || shift.DepartmentId != DepartmentId)
				return OperationResult(ShiftActionErrors.NotFound);

			var scope = await _authorizationService.GetShiftManagementScopeAsync(UserId, DepartmentId);

			if (!scope.CanManageShiftGroup(shift, signup.DepartmentGroupId))
				return OperationResult(ShiftActionErrors.NotAllowed);

			var reviewed = await _shiftsService.ReviewShiftSignupAsync(input.ShiftSignupId, input.Approve, UserId, input.Note, cancellationToken);

			return reviewed.Success
				? OperationResult(ShiftActionErrors.None, input.ShiftSignupId.ToString(), status: ResponseHelper.Updated)
				: OperationResult(reviewed.Error);
		}

		/// <summary>
		/// A supervisor approves or denies a trade waiting for approval
		/// </summary>
		[HttpPost("ReviewShiftTrade")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<ActionResult<ShiftOperationResult>> ReviewShiftTrade(ReviewShiftTradeInput input, CancellationToken cancellationToken)
		{
			if (input == null)
				return BadRequest();

			var trade = await _shiftsService.GetShiftTradeByIdAsync(input.ShiftSignupTradeId);
			var source = trade?.SourceShiftSignup;
			var shift = source != null ? await _shiftsService.GetShiftByIdAsync(source.ShiftId) : null;

			if (shift == null || shift.DepartmentId != DepartmentId)
				return OperationResult(ShiftActionErrors.NotFound);

			var scope = await _authorizationService.GetShiftManagementScopeAsync(UserId, DepartmentId);

			if (!scope.CanManageShiftGroup(shift, source.DepartmentGroupId))
				return OperationResult(ShiftActionErrors.NotAllowed);

			var reviewed = await _shiftsService.ReviewTradeAsync(input.ShiftSignupTradeId, input.Approve, UserId, input.Note, cancellationToken);

			return reviewed.Success
				? OperationResult(ShiftActionErrors.None, input.ShiftSignupTradeId.ToString(), status: ResponseHelper.Updated)
				: OperationResult(reviewed.Error);
		}

		/// <summary>
		/// Department personnel who are not on a shift day, for a supervisor to add
		/// </summary>
		[HttpGet("GetShiftDayPersonnelOptions")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<ActionResult<ShiftPersonOptionsResult>> GetShiftDayPersonnelOptions(int shiftDayId)
		{
			var result = new ShiftPersonOptionsResult();
			var schedule = await _shiftsService.GetShiftDayScheduleAsync(shiftDayId);

			if (schedule == null || schedule.Shift.DepartmentId != DepartmentId)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			var lookups = await GetLookupsAsync();

			if (!lookups.Scope.CanSuperviseShift(schedule.Shift))
			{
				result.Status = ResponseHelper.Failure;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = await GetPersonnelOptionsAsync(schedule, lookups);
			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;

			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// A supervisor adds a person to one shift day, in a group they supervise
		/// </summary>
		[HttpPost("AssignToShiftDay")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<ActionResult<ShiftOperationResult>> AssignToShiftDay(AssignToShiftDayInput input, CancellationToken cancellationToken)
		{
			if (input == null)
				return BadRequest();

			var shiftDay = await GetDepartmentShiftDayAsync(input.ShiftDayId);

			if (shiftDay == null)
				return OperationResult(ShiftActionErrors.NotFound);

			var shift = await _shiftsService.GetShiftByIdAsync(shiftDay.ShiftId);
			var scope = await _authorizationService.GetShiftManagementScopeAsync(UserId, DepartmentId);

			if (!scope.CanManageShiftGroup(shift, input.GroupId > 0 ? input.GroupId : (int?)null))
				return OperationResult(ShiftActionErrors.NotAllowed);

			var assigned = await _shiftsService.AssignUserToShiftDayAsync(input.ShiftDayId, input.UserId, input.GroupId, UserId, cancellationToken);

			return assigned.Success
				? OperationResult(ShiftActionErrors.None, assigned.Item.ShiftSignupId.ToString(), status: ResponseHelper.Created)
				: OperationResult(assigned.Error);
		}

		/// <summary>
		/// A supervisor takes a person off one shift day
		/// </summary>
		[HttpPost("RemoveFromShiftDay")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<ActionResult<ShiftOperationResult>> RemoveFromShiftDay(RemoveFromShiftDayInput input, CancellationToken cancellationToken)
		{
			if (input == null)
				return BadRequest();

			var schedule = await _shiftsService.GetShiftDayScheduleAsync(input.ShiftDayId);

			if (schedule == null || schedule.Shift.DepartmentId != DepartmentId)
				return OperationResult(ShiftActionErrors.NotFound);

			var entries = schedule.Roster.Where(x => SameUser(x.UserId, input.UserId)).ToList();

			if (!entries.Any())
				return OperationResult(ShiftActionErrors.NotOnShift);

			var scope = await _authorizationService.GetShiftManagementScopeAsync(UserId, DepartmentId);

			if (!entries.All(x => scope.CanManageShiftGroup(schedule.Shift, x.DepartmentGroupId)))
				return OperationResult(ShiftActionErrors.NotAllowed);

			var removed = await _shiftsService.RemoveUserFromShiftDayAsync(input.ShiftDayId, input.UserId, UserId, input.Note, cancellationToken);

			return removed.Success
				? OperationResult(ShiftActionErrors.None, input.ShiftDayId.ToString(), status: ResponseHelper.Updated)
				: OperationResult(removed.Error);
		}

		/// <summary>
		/// Everyone on duty right now: approved roster entries on every running shift day, trades applied
		/// </summary>
		[HttpGet("GetOnDutyPersonnel")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<ActionResult<OnDutyPersonnelResult>> GetOnDutyPersonnel()
		{
			var result = new OnDutyPersonnelResult();
			var lookups = await GetLookupsAsync();

			foreach (var schedule in await _shiftsService.GetActiveShiftDaySchedulesAsync(DepartmentId, DateTime.UtcNow))
			{
				foreach (var entry in schedule.Roster.Where(x => x.IsOnDuty()))
				{
					result.Data.Add(new OnDutyPersonResultData
					{
						UserId = entry.UserId,
						Name = lookups.NameFor(entry.UserId),
						ShiftId = schedule.Shift.ShiftId.ToString(),
						ShiftName = schedule.Shift.Name,
						ShiftDayId = schedule.Day.ShiftDayId.ToString(),
						Start = schedule.Day.Start,
						End = schedule.Day.End,
						GroupId = entry.DepartmentGroupId?.ToString() ?? "",
						GroupName = lookups.GroupNameFor(entry.DepartmentGroupId),
						Roles = lookups.RoleNamesFor(entry.UserId),
						Source = (int)entry.Source
					});
				}
			}

			result.Data = result.Data.OrderBy(x => x.GroupName).ThenBy(x => x.Name).ToList();
			result.PageSize = result.Data.Count;
			result.Status = result.Data.Any() ? ResponseHelper.Success : ResponseHelper.NotFound;

			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		#region Conversion

		private sealed class Lookups
		{
			public Department Department { get; set; }
			public DateTime LocalNow { get; set; }
			public string UserId { get; set; }
			public ShiftManagementScope Scope { get; set; }
			public Dictionary<string, string> Names { get; set; }
			public Dictionary<int, DepartmentGroup> Groups { get; set; }
			public Dictionary<int, PersonnelRole> Roles { get; set; }
			public Dictionary<string, List<PersonnelRole>> RolesByUser { get; set; }
			public List<Shift> Shifts { get; set; }

			public string NameFor(string userId)
			{
				return userId != null && Names.TryGetValue(userId, out var name) ? name : "";
			}

			public string GroupNameFor(int? groupId)
			{
				return groupId.HasValue && Groups.TryGetValue(groupId.Value, out var group) ? group.Name : "";
			}

			public List<int> RoleIdsFor(string userId)
			{
				return userId != null && RolesByUser.TryGetValue(userId, out var roles) ? roles.Select(x => x.PersonnelRoleId).Distinct().ToList() : new List<int>();
			}

			public List<string> RoleNamesFor(string userId)
			{
				return userId != null && RolesByUser.TryGetValue(userId, out var roles) ? roles.Select(x => x.Name).Distinct().ToList() : new List<string>();
			}
		}

		private async Task<Lookups> GetLookupsAsync()
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var names = await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId) ?? new List<PersonName>();
			var groups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId) ?? new List<DepartmentGroup>();
			var roles = await _personnelRolesService.GetRolesForDepartmentAsync(DepartmentId) ?? new List<PersonnelRole>();
			var rolesByUser = await _personnelRolesService.GetAllRolesForUsersInDepartmentAsync(DepartmentId) ?? new Dictionary<string, List<PersonnelRole>>();

			var lookups = new Lookups
			{
				Department = department,
				LocalNow = department != null ? DateTime.UtcNow.TimeConverter(department) : DateTime.UtcNow,
				UserId = UserId,
				Scope = await _authorizationService.GetShiftManagementScopeAsync(UserId, DepartmentId),
				Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
				Groups = groups.GroupBy(x => x.DepartmentGroupId).ToDictionary(x => x.Key, x => x.First()),
				Roles = roles.GroupBy(x => x.PersonnelRoleId).ToDictionary(x => x.Key, x => x.First()),
				RolesByUser = new Dictionary<string, List<PersonnelRole>>(StringComparer.OrdinalIgnoreCase)
			};

			foreach (var name in names.Where(x => x != null && !String.IsNullOrWhiteSpace(x.UserId)))
				lookups.Names[name.UserId] = name.Name;

			foreach (var pair in rolesByUser.Where(x => x.Key != null))
				lookups.RolesByUser[pair.Key] = pair.Value ?? new List<PersonnelRole>();

			return lookups;
		}

		private async Task<List<Shift>> GetShiftsAsync(Lookups lookups)
		{
			if (lookups.Shifts == null)
				lookups.Shifts = await _shiftsService.GetAllShiftsByDepartmentAsync(DepartmentId);

			return lookups.Shifts;
		}

		private static ShiftDay FindDay(Lookups lookups, int shiftId, DateTime date)
		{
			var shift = lookups.Shifts?.FirstOrDefault(x => x.ShiftId == shiftId);
			var day = shift?.Days?.FirstOrDefault(x => x.Day.Date == date.Date);

			if (day != null)
				day.Shift = shift;

			return day;
		}

		private ShiftsResultData ConvertShift(Shift shiftData, Lookups lookups)
		{
			var shift = new ShiftsResultData();
			shift.ShiftId = shiftData.ShiftId.ToString();
			shift.Name = shiftData.Name;
			shift.Code = shiftData.Code;
			shift.Color = shiftData.Color;
			shift.ScheduleType = shiftData.ScheduleType;
			shift.AssignmentType = shiftData.AssignmentType;
			shift.StartTime = shiftData.StartTime;
			shift.EndTime = shiftData.EndTime;
			shift.RequireApproval = shiftData.RequireApproval == true;
			shift.CanManage = lookups.Scope.CanSuperviseShift(shiftData);
			shift.PersonnelCount = shiftData.Personnel?.Count ?? 0;
			shift.GroupCount = shiftData.Groups?.Count ?? 0;
			shift.InShift = shiftData.Personnel != null && shiftData.Personnel.Any(x => SameUser(x.UserId, lookups.UserId));

			shift.Groups = (shiftData.Groups ?? new List<ShiftGroup>()).Where(x => x != null).Select(x => new ShiftGroupResultData
			{
				GroupId = x.DepartmentGroupId.ToString(),
				GroupName = lookups.GroupNameFor(x.DepartmentGroupId),
				Roles = (x.Roles ?? new List<ShiftGroupRole>()).Where(r => r != null).Select(r => new ShiftGroupRoleResultData
				{
					RoleId = r.PersonnelRoleId.ToString(),
					RoleName = lookups.Roles.TryGetValue(r.PersonnelRoleId, out var role) ? role.Name : "",
					Required = r.Required
				}).ToList()
			}).ToList();

			var days = (shiftData.Days ?? new List<ShiftDay>()).Where(x => x != null).OrderBy(x => x.Day).ToList();

			foreach (var day in days)
				day.Shift = shiftData;

			// The next day that has not finished, rather than only a day starting within minutes of now.
			var nextDay = days.FirstOrDefault(x => x.End > lookups.LocalNow);

			if (nextDay != null)
			{
				shift.NextDay = nextDay.Day.ToString("O");
				shift.NextDayId = nextDay.ShiftDayId.ToString();
			}

			shift.Days = days.Select(x => new ShiftDayResultData
			{
				ShiftDayId = x.ShiftDayId.ToString(),
				ShiftId = x.ShiftId.ToString(),
				ShiftName = shiftData.Name,
				ShiftDay = x.Day,
				Start = x.Start,
				End = x.End,
				ShiftType = shiftData.AssignmentType,
				Color = shiftData.Color,
				RequireApproval = shift.RequireApproval,
				SignedUp = shift.InShift,
				Signups = new List<ShiftDaySignupResultData>(),
				Needs = new List<ShiftDayGroupNeedsResultData>(),
				Roster = new List<ShiftDayRosterResultData>()
			}).ToList();

			return shift;
		}

		private ShiftDayResultData ConvertSchedule(ShiftDaySchedule schedule, Lookups lookups, bool full)
		{
			var shift = schedule.Shift;
			var day = schedule.Day;
			var myEntries = schedule.Roster.Where(x => SameUser(x.UserId, lookups.UserId)).ToList();
			var myActive = myEntries.FirstOrDefault(x => x.IsOnDuty());
			var myEntry = myActive ?? myEntries.FirstOrDefault();

			// The caller's own slot: a trade replacement entry is backed by someone else's signup.
			var mySignupId = myEntry != null && myEntry.Source != ShiftRosterSources.Trade ? myEntry.ShiftSignupId : null;
			var myTrade = mySignupId.HasValue
				? schedule.Trades.FirstOrDefault(x => x.SourceShiftSignupId == mySignupId.Value && !x.Denied && !x.IsTradeComplete())
				: null;

			var myStatus = 0;
			if (myActive != null)
				myStatus = 1;
			else if (myEntry != null)
				myStatus = 2;
			else if (schedule.Signups.Any(x => SameUser(x.UserId, lookups.UserId) && x.Denied))
				myStatus = 3;

			var isOver = lookups.LocalNow >= day.End;

			var data = new ShiftDayResultData
			{
				ShiftDayId = day.ShiftDayId.ToString(),
				ShiftId = shift.ShiftId.ToString(),
				ShiftName = shift.Name,
				ShiftDay = day.Day,
				Start = day.Start,
				End = day.End,
				ShiftType = shift.AssignmentType,
				Color = shift.Color,
				RequireApproval = shift.RequireApproval == true,
				SignedUp = myEntry != null,
				Filled = schedule.IsFilled(),
				OpenSlots = schedule.OpenSlots(),
				IsActive = schedule.IsActive,
				CanManage = lookups.Scope.CanSuperviseShift(shift),
				CanSignup = !isOver && myEntry == null &&
				            ((shift.Groups != null && shift.Groups.Any()) || shift.AssignmentType == (int)ShiftAssignmentTypes.Signup),
				MyStatus = myStatus,
				MySignupId = mySignupId?.ToString() ?? "",
				MyGroupId = myEntry?.DepartmentGroupId?.ToString() ?? "",
				MyTradeId = myTrade?.ShiftSignupTradeId.ToString() ?? "",
				Signups = new List<ShiftDaySignupResultData>(),
				Needs = new List<ShiftDayGroupNeedsResultData>(),
				Roster = new List<ShiftDayRosterResultData>()
			};

			if (!full)
				return data;

			foreach (var signup in schedule.Signups.Where(x => !x.Denied))
			{
				data.Signups.Add(new ShiftDaySignupResultData
				{
					UserId = signup.UserId,
					Name = lookups.NameFor(signup.UserId),
					Roles = lookups.RoleIdsFor(signup.UserId),
					GroupId = signup.DepartmentGroupId?.ToString() ?? "",
					ShiftSignupId = signup.ShiftSignupId.ToString(),
					ApprovalPending = signup.ApprovalPending
				});
			}

			foreach (var need in schedule.Needs)
			{
				data.Needs.Add(new ShiftDayGroupNeedsResultData
				{
					GroupId = need.Key.ToString(),
					GroupName = lookups.GroupNameFor(need.Key),
					CanManage = lookups.Scope.CanManageGroup(need.Key),
					GroupNeeds = need.Value.Select(x => new ShiftDayGroupRoleNeedsResultData
					{
						RoleId = x.Key.ToString(),
						RoleName = lookups.Roles.TryGetValue(x.Key, out var role) ? role.Name : "",
						Needed = Math.Max(x.Value, 0)
					}).ToList()
				});
			}

			foreach (var entry in schedule.Roster.OrderBy(x => lookups.GroupNameFor(x.DepartmentGroupId)).ThenBy(x => lookups.NameFor(x.UserId)))
			{
				data.Roster.Add(new ShiftDayRosterResultData
				{
					UserId = entry.UserId,
					Name = lookups.NameFor(entry.UserId),
					GroupId = entry.DepartmentGroupId?.ToString() ?? "",
					GroupName = lookups.GroupNameFor(entry.DepartmentGroupId),
					RoleIds = lookups.RoleIdsFor(entry.UserId),
					Roles = lookups.RoleNamesFor(entry.UserId),
					Source = (int)entry.Source,
					ShiftSignupId = entry.ShiftSignupId?.ToString() ?? "",
					ApprovalPending = entry.ApprovalPending,
					TradedFromUserId = entry.TradedFromUserId ?? "",
					TradedFromName = lookups.NameFor(entry.TradedFromUserId)
				});
			}

			return data;
		}

		private async Task<ShiftTradeResultData> ConvertTradeAsync(ShiftSignupTrade trade, Lookups lookups)
		{
			var source = trade.SourceShiftSignup;
			var shifts = await GetShiftsAsync(lookups);
			var shift = shifts.FirstOrDefault(x => x.ShiftId == source?.ShiftId) ?? source?.Shift;
			var day = source != null ? FindDay(lookups, source.ShiftId, source.ShiftDay) : null;
			var acceptedUserId = !String.IsNullOrWhiteSpace(trade.UserId) ? trade.UserId : trade.TargetShiftSignup?.UserId;

			var status = 0;
			if (trade.Denied)
				status = 3;
			else if (trade.ApprovalPending)
				status = 1;
			else if (trade.IsTradeComplete())
				status = 2;

			var data = new ShiftTradeResultData
			{
				ShiftSignupTradeId = trade.ShiftSignupTradeId.ToString(),
				Direction = SameUser(source?.UserId, lookups.UserId) ? 0 : 1,
				Status = status,
				MyState = (int)trade.GetState(lookups.UserId),
				SourceShiftSignupId = trade.SourceShiftSignupId.ToString(),
				SourceUserId = source?.UserId ?? "",
				SourceUserName = lookups.NameFor(source?.UserId),
				ShiftId = source?.ShiftId.ToString() ?? "",
				ShiftName = shift?.Name ?? "",
				ShiftDayId = day?.ShiftDayId.ToString() ?? "",
				ShiftDay = source?.ShiftDay ?? DateTime.MinValue,
				Start = day?.Start ?? source?.ShiftDay ?? DateTime.MinValue,
				End = day?.End ?? source?.ShiftDay ?? DateTime.MinValue,
				GroupId = source?.DepartmentGroupId?.ToString() ?? "",
				GroupName = lookups.GroupNameFor(source?.DepartmentGroupId),
				Note = trade.Note ?? "",
				AcceptedUserId = acceptedUserId ?? "",
				AcceptedUserName = lookups.NameFor(acceptedUserId),
				TargetShiftSignupId = trade.TargetShiftSignupId?.ToString() ?? "",
				TargetShiftDay = trade.TargetShiftSignup?.ShiftDay,
				ReviewNote = trade.ReviewNote ?? "",
				ReviewedByName = lookups.NameFor(trade.ReviewedByUserId),
				RequireApproval = shift?.RequireApproval == true,
				CanReview = trade.ApprovalPending && !trade.Denied && shift != null && lookups.Scope.CanManageShiftGroup(shift, source?.DepartmentGroupId)
			};

			foreach (var user in trade.Users ?? new List<ShiftSignupTradeUser>())
			{
				var userData = new ShiftTradeUserResultData
				{
					UserId = user.UserId,
					Name = lookups.NameFor(user.UserId),
					Declined = user.Declined,
					Offered = user.Offered,
					Reason = user.Reason ?? ""
				};

				foreach (var offer in (user.Shifts ?? new List<ShiftSignupTradeUserShift>()).Where(x => x.ShiftSignupId.HasValue))
				{
					var offered = await _shiftsService.GetShiftSignupByIdAsync(offer.ShiftSignupId.Value);

					if (offered == null)
						continue;

					userData.OfferedShifts.Add(new ShiftTradeOfferedShiftResultData
					{
						ShiftSignupId = offered.ShiftSignupId.ToString(),
						ShiftName = shifts.FirstOrDefault(x => x.ShiftId == offered.ShiftId)?.Name ?? "",
						ShiftDay = offered.ShiftDay
					});
				}

				data.Users.Add(userData);
			}

			return data;
		}

		private async Task<List<ShiftPersonOptionResultData>> GetPersonnelOptionsAsync(ShiftDaySchedule schedule, Lookups lookups)
		{
			var options = new List<ShiftPersonOptionResultData>();
			var members = await _departmentsService.GetAllMembersForDepartmentAsync(DepartmentId) ?? new List<DepartmentMember>();
			var onDay = new HashSet<string>(schedule.Roster.Select(x => x.UserId), StringComparer.OrdinalIgnoreCase);

			foreach (var member in members.Where(x => x != null && !x.IsDeleted && !String.IsNullOrWhiteSpace(x.UserId)))
			{
				if (onDay.Contains(member.UserId) || options.Any(x => SameUser(x.UserId, member.UserId)))
					continue;

				var group = lookups.Groups.Values.FirstOrDefault(g => g.Members != null && g.Members.Any(m => SameUser(m.UserId, member.UserId)));

				options.Add(new ShiftPersonOptionResultData
				{
					UserId = member.UserId,
					Name = lookups.NameFor(member.UserId),
					GroupId = group?.DepartmentGroupId.ToString() ?? "",
					GroupName = group?.Name ?? "",
					Roles = lookups.RoleNamesFor(member.UserId),
					RoleIds = lookups.RoleIdsFor(member.UserId)
				});
			}

			return options.Where(x => !String.IsNullOrWhiteSpace(x.Name)).OrderBy(x => x.Name).ToList();
		}

		#endregion Conversion

		#region Helpers

		private async Task<ShiftDay> GetDepartmentShiftDayAsync(int shiftDayId)
		{
			var shiftDay = await _shiftsService.GetShiftDayByIdAsync(shiftDayId);

			if (shiftDay == null)
				return null;

			// The day's multi-mapped Shift can come back null; never skip the department check because of that.
			var shift = shiftDay.Shift ?? await _shiftsService.GetShiftByIdAsync(shiftDay.ShiftId);

			return shift != null && shift.DepartmentId == DepartmentId ? shiftDay : null;
		}

		private async Task<bool> IsDepartmentTradeAsync(int shiftSignupTradeId)
		{
			var trade = await _shiftsService.GetShiftTradeByIdAsync(shiftSignupTradeId);

			return trade?.SourceShiftSignup?.Shift != null && trade.SourceShiftSignup.Shift.DepartmentId == DepartmentId;
		}

		private static bool TryGetRange(string start, string end, DateTime today, int defaultDays, out DateTime startDate, out DateTime endDate)
		{
			startDate = today;
			endDate = today.AddDays(defaultDays);

			if (!String.IsNullOrWhiteSpace(start) && !TryParseDate(start, out startDate))
				return false;

			if (!String.IsNullOrWhiteSpace(end) && !TryParseDate(end, out endDate))
				return false;
			else if (String.IsNullOrWhiteSpace(end) && !String.IsNullOrWhiteSpace(start))
				endDate = startDate.AddDays(defaultDays);

			if (endDate < startDate)
				return false;

			return (endDate - startDate).TotalDays <= MaxRangeDays;
		}

		private static bool TryParseDate(string value, out DateTime date)
		{
			if (DateTime.TryParseExact(value.Trim(), new[] { "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "O" }, CultureInfo.InvariantCulture,
				    DateTimeStyles.AllowWhiteSpaces, out date))
			{
				date = date.Date;
				return true;
			}

			return false;
		}

		private ActionResult<ShiftOperationResult> OperationResult(ShiftActionErrors error, string id = "", bool approvalPending = false, string status = null)
		{
			var result = new ShiftOperationResult
			{
				Id = id ?? "",
				ApprovalPending = approvalPending,
				ErrorCode = error == ShiftActionErrors.None ? "" : ToErrorCode(error),
				Status = error == ShiftActionErrors.None ? status ?? ResponseHelper.Success : ResponseHelper.Failure
			};

			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// ShiftActionErrors as the snake_case codes the mobile apps translate.
		/// </summary>
		public static string ToErrorCode(ShiftActionErrors error)
		{
			switch (error)
			{
				case ShiftActionErrors.None: return "";
				case ShiftActionErrors.NotFound: return "not_found";
				case ShiftActionErrors.NotAllowed: return "not_allowed";
				case ShiftActionErrors.AlreadySignedUp: return "already_signed_up";
				case ShiftActionErrors.InvalidGroup: return "invalid_group";
				case ShiftActionErrors.DayInPast: return "day_in_past";
				case ShiftActionErrors.NotOnShift: return "not_on_shift";
				case ShiftActionErrors.TradeExists: return "trade_exists";
				case ShiftActionErrors.NoUsers: return "no_users";
				case ShiftActionErrors.InvalidOffer: return "invalid_offer";
				case ShiftActionErrors.NotPending: return "not_pending";
				case ShiftActionErrors.AlreadyOnRoster: return "already_on_roster";
				default: return "invalid_request";
			}
		}

		private static bool SameUser(string a, string b)
		{
			return a != null && b != null && String.Equals(a, b, StringComparison.OrdinalIgnoreCase);
		}

		#endregion Helpers
	}
}
