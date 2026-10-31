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
		private readonly IShiftStaffingService _shiftStaffingService;
		private readonly IWorkShiftsService _workShiftsService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IUnitsService _unitsService;
		private readonly ICertificationService _certificationService;

		public ShiftsController(
			IShiftsService shiftsService,
			IDepartmentsService departmentsService,
			IPersonnelRolesService personnelRolesService,
			IDepartmentGroupsService departmentGroupsService,
			IAuthorizationService authorizationService,
			IShiftStaffingService shiftStaffingService,
			IWorkShiftsService workShiftsService,
			IDepartmentSettingsService departmentSettingsService,
			IUnitsService unitsService,
			ICertificationService certificationService
		)
		{
			_shiftsService = shiftsService;
			_departmentsService = departmentsService;
			_personnelRolesService = personnelRolesService;
			_departmentGroupsService = departmentGroupsService;
			_authorizationService = authorizationService;
			_shiftStaffingService = shiftStaffingService;
			_workShiftsService = workShiftsService;
			_departmentSettingsService = departmentSettingsService;
			_unitsService = unitsService;
			_certificationService = certificationService;
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
		/// Asks to trade out of the caller's slot on a shift day. With no UserIds the request is open: everyone eligible for
		/// the slot is told and may apply. With UserIds only those people are asked.
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
		/// Picks who takes the slot: a user taking it outright, or one of the offered swap-back days. The requester may pick
		/// when the department lets requesters decide; department admins, group admins (when allowed) and members of the
		/// department's approver roles may pick for anyone, and their pick needs no further approval.
		/// </summary>
		[HttpPost("FinishShiftTrade")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<ActionResult<ShiftOperationResult>> FinishShiftTrade(FinishShiftTradeInput input, CancellationToken cancellationToken)
		{
			if (input == null)
				return BadRequest();

			var trade = await GetDepartmentTradeAsync(input.ShiftSignupTradeId);

			if (trade == null)
				return OperationResult(ShiftActionErrors.NotFound);

			if (!trade.IsOpenForOffers())
				return OperationResult(ShiftActionErrors.InvalidRequest);

			var lookups = await GetLookupsAsync();
			var shift = await GetTradeShiftAsync(trade, lookups);

			if (!lookups.Authority.CanPick(trade, shift))
				return OperationResult(ShiftActionErrors.NotAllowed);

			var asApprover = lookups.Authority.IsApprover(shift, trade.GetDepartmentGroupId());
			var picked = await _shiftsService.PickTradeOfferAsync(input.ShiftSignupTradeId, UserId, asApprover, input.AcceptedUserId, input.TargetShiftSignupId, cancellationToken);

			return picked.Success
				? OperationResult(ShiftActionErrors.None, input.ShiftSignupTradeId.ToString(), picked.Item?.ApprovalPending ?? false, ResponseHelper.Updated)
				: OperationResult(picked.Error);
		}

		/// <summary>
		/// Withdraws a trade that has not taken effect: the requester's own, or one in a slot the caller supervises. The
		/// requester and everyone who applied are told.
		/// </summary>
		[HttpPost("CancelShiftTrade")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<ActionResult<ShiftOperationResult>> CancelShiftTrade(CancelShiftTradeInput input, CancellationToken cancellationToken)
		{
			if (input == null)
				return BadRequest();

			var trade = await GetDepartmentTradeAsync(input.ShiftSignupTradeId);

			if (trade == null)
				return OperationResult(ShiftActionErrors.NotFound);

			if (trade.IsTradeComplete())
				return OperationResult(ShiftActionErrors.InvalidRequest);

			var lookups = await GetLookupsAsync();
			var shift = await GetTradeShiftAsync(trade, lookups);

			if (!lookups.Authority.CanCancel(trade, shift))
				return OperationResult(ShiftActionErrors.NotAllowed);

			var cancelled = await _shiftsService.CancelTradeAsync(input.ShiftSignupTradeId, UserId, !lookups.Authority.IsRequester(trade), cancellationToken);

			return cancelled.Success
				? OperationResult(ShiftActionErrors.None, input.ShiftSignupTradeId.ToString(), status: ResponseHelper.Deleted)
				: OperationResult(cancelled.Error);
		}

		/// <summary>
		/// Open trade requests in the department that are still taking applicants, with whether the caller may apply
		/// </summary>
		[HttpGet("GetOpenShiftTrades")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<ActionResult<ShiftTradesResult>> GetOpenShiftTrades()
		{
			var result = new ShiftTradesResult();
			var lookups = await GetLookupsAsync();

			foreach (var trade in await _shiftsService.GetOpenTradesAsync(DepartmentId))
			{
				var eligibility = lookups.Authority.IsRequester(trade) ? null : await _shiftsService.GetTradeEligibilityAsync(trade);
				var converted = await ConvertTradeAsync(trade, lookups, eligibility);

				if (converted.IsOver)
					continue;

				if (!await MayDecideTradeAsync(trade, lookups))
					HideDecisionNotes(converted, trade, lookups.UserId);

				result.Data.Add(converted);
			}

			result.Data = result.Data.OrderBy(x => x.Start).ToList();
			result.PageSize = result.Data.Count;
			result.Status = result.Data.Any() ? ResponseHelper.Success : ResponseHelper.NotFound;

			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// One trade. The requester and the people who may decide it also get what they need to choose between applicants:
		/// each applicant's group, roles, current certifications, whether they still qualify, how much they are working in
		/// the week either side, and any shift they are already on that overlaps. Anyone else sees an open request, or one
		/// they were asked to take, without other applicants' notes.
		/// </summary>
		/// <param name="id">Shift signup trade id</param>
		[HttpGet("GetShiftTrade")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<ActionResult<ShiftTradeResult>> GetShiftTrade(int id)
		{
			var result = new ShiftTradeResult();
			var trade = await GetDepartmentTradeAsync(id);

			if (trade == null)
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			var lookups = await GetLookupsAsync();
			var mayDecide = await MayDecideTradeAsync(trade, lookups);

			// A request offered to particular people is only theirs to see (as on the web); an open one is on the board.
			if (!mayDecide && !trade.IsOpen && !IsTradeParticipant(trade, lookups.UserId))
			{
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			var eligibility = await _shiftsService.GetTradeEligibilityAsync(trade);
			var data = await ConvertTradeAsync(trade, lookups, lookups.Authority.IsRequester(trade) ? null : eligibility);

			if (!mayDecide)
				HideDecisionNotes(data, trade, lookups.UserId);
			else if (data.Users.Any())
				await AddApplicantDetailsAsync(data, trade, eligibility, lookups);

			result.Data = data;
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;

			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// Static shift (workshift) days in a date range (department local dates, inclusive, at most 62 days; default today
		/// to 30 days out), in the same shape as shift days with ShiftType 2. <paramref name="mine"/> keeps only the days the
		/// caller is staffing.
		/// </summary>
		/// <param name="start">First day, yyyy-MM-dd</param>
		/// <param name="end">Last day, yyyy-MM-dd</param>
		/// <param name="mine">Only days the caller works</param>
		[HttpGet("GetWorkshiftDays")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<ActionResult<ShiftDaysResult>> GetWorkshiftDays(string start = null, string end = null, bool mine = false)
		{
			var result = new ShiftDaysResult();
			var lookups = await GetLookupsAsync();

			if (!TryGetRange(start, end, lookups.LocalNow.Date, 30, out var startDate, out var endDate))
			{
				result.Status = ResponseHelper.Failure;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			var occurrences = await _shiftStaffingService.GetStaffingForRangeAsync(DepartmentId, startDate, endDate.AddDays(1), workshiftsOnly: true) ?? new List<ShiftDayStaffing>();
			var myTrades = await GetMyOpenWorkshiftTradesAsync();

			foreach (var occurrence in occurrences.Where(x => x.ShiftType == StaffedShiftTypes.Workshift))
			{
				if (mine && !occurrence.Personnel.Any(x => SameUser(x.UserId, UserId)))
					continue;

				result.Data.Add(ConvertWorkshiftDay(occurrence, lookups, myTrades));
			}

			result.PageSize = result.Data.Count;
			result.Status = result.Data.Any() ? ResponseHelper.Success : ResponseHelper.NotFound;

			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// One static shift (workshift) day with who is staffing it
		/// </summary>
		/// <param name="id">Workshift day id</param>
		[HttpGet("GetWorkshiftDay")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<ActionResult<GetShiftDayResult>> GetWorkshiftDay(string id)
		{
			var result = new GetShiftDayResult();
			var staffing = await _shiftStaffingService.GetWorkshiftDayStaffingAsync(id);
			var workshift = staffing != null ? await _workShiftsService.GetWorkshiftByIdAsync(staffing.WorkshiftId) : null;

			if (workshift == null || workshift.DepartmentId != DepartmentId)
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.NotFound;
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}

			result.Data = ConvertWorkshiftDay(staffing, await GetLookupsAsync(), await GetMyOpenWorkshiftTradesAsync());
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;

			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		/// <summary>
		/// Asks to trade out of a static shift (workshift) day the caller is staffing. The request is open: everyone eligible
		/// is told and may apply.
		/// </summary>
		[HttpPost("RequestWorkshiftTrade")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<ActionResult<ShiftOperationResult>> RequestWorkshiftTrade(RequestWorkshiftTradeInput input, CancellationToken cancellationToken)
		{
			if (input == null || String.IsNullOrWhiteSpace(input.WorkshiftDayId))
				return BadRequest();

			var trade = await _shiftsService.RequestWorkshiftTradeAsync(DepartmentId, input.WorkshiftDayId, UserId, input.Note, cancellationToken);

			return trade.Success
				? OperationResult(ShiftActionErrors.None, trade.Item.ShiftSignupTradeId.ToString(), status: ResponseHelper.Created)
				: OperationResult(trade.Error);
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

			result.Data.IsSupervisor = lookups.Authority.HasApprovals;

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
			}

			if (lookups.Authority.HasApprovals)
			{
				foreach (var trade in await _shiftsService.GetPendingTradesAsync(DepartmentId))
				{
					var converted = await ConvertTradeAsync(trade, lookups);

					if (converted.CanReview)
						result.Data.Trades.Add(converted);
				}

				// Open requests with applicants that the caller decides as an approver (requesters see their own under Trades).
				foreach (var trade in await _shiftsService.GetOpenTradesAsync(DepartmentId))
				{
					if (lookups.Authority.IsRequester(trade) || !(trade.Users ?? new List<ShiftSignupTradeUser>()).Any(x => x.Offered && !x.Declined))
						continue;

					var converted = await ConvertTradeAsync(trade, lookups);

					if (converted.CanPick && converted.PickIsApproval && !converted.IsOver && result.Data.Trades.All(x => x.ShiftSignupTradeId != converted.ShiftSignupTradeId))
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

			var trade = await GetDepartmentTradeAsync(input.ShiftSignupTradeId);

			if (trade == null)
				return OperationResult(ShiftActionErrors.NotFound);

			var lookups = await GetLookupsAsync();
			var shift = await GetTradeShiftAsync(trade, lookups);

			if (!lookups.Authority.IsApprover(shift, trade.GetDepartmentGroupId()))
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
			public ShiftTradeAuthority Authority { get; set; }
			public Dictionary<int, string> UnitNames { get; set; }
			public Dictionary<string, ShiftDayStaffing> WorkshiftDays { get; set; } = new Dictionary<string, ShiftDayStaffing>(StringComparer.OrdinalIgnoreCase);

			public string UnitNameFor(int? unitId)
			{
				return unitId.HasValue && UnitNames != null && UnitNames.TryGetValue(unitId.Value, out var name) ? name : "";
			}

			public DateTime ToUtc(DateTime local)
			{
				return Department != null ? local.DepartmentLocalToUtc(Department) : DateTime.SpecifyKind(local, DateTimeKind.Utc);
			}

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

			lookups.Authority = new ShiftTradeAuthority
			{
				UserId = UserId,
				Scope = lookups.Scope,
				Settings = await _departmentSettingsService.GetShiftTradeApproverSettingsAsync(DepartmentId) ?? ShiftTradeApproverSettings.Default(),
				RoleIds = lookups.RoleIdsFor(UserId)
			};

			var units = await _unitsService.GetUnitsForDepartmentIncludingDeletedAsync(DepartmentId) ?? new List<Unit>();
			lookups.UnitNames = units.Where(x => x != null).GroupBy(x => x.UnitId).ToDictionary(x => x.Key, x => x.First().Name);

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
				StartUtc = lookups.ToUtc(day.Start),
				EndUtc = lookups.ToUtc(day.End),
				CanTrade = !isOver && myActive != null && myActive.Source != ShiftRosterSources.Trade && myTrade == null,
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

		private async Task<ShiftTradeResultData> ConvertTradeAsync(ShiftSignupTrade trade, Lookups lookups, ShiftTradeEligibility eligibility = null)
		{
			var source = trade.SourceShiftSignup;
			var shifts = await GetShiftsAsync(lookups);
			var shift = await GetTradeShiftAsync(trade, lookups);
			var day = source != null ? FindDay(lookups, source.ShiftId, source.ShiftDay) : null;
			var workshiftDay = trade.IsWorkshiftTrade() ? await GetWorkshiftDayAsync(lookups, trade.WorkshiftDayId) : null;
			var acceptedUserId = trade.GetPickedUserId();
			var sourceUserId = trade.GetSourceUserId();
			var groupId = trade.GetDepartmentGroupId();
			var authority = lookups.Authority;

			var status = 0;
			if (trade.Denied)
				status = 3;
			else if (trade.ApprovalPending)
				status = 1;
			else if (trade.IsTradeComplete())
				status = 2;

			var start = day?.Start ?? workshiftDay?.Start ?? source?.ShiftDay ?? trade.SourceDay ?? DateTime.MinValue;
			var end = day?.End ?? workshiftDay?.End ?? source?.ShiftDay ?? trade.SourceDay ?? DateTime.MinValue;
			var isOver = workshiftDay != null ? workshiftDay.IsOver : (day != null && lookups.LocalNow >= day.End);

			var data = new ShiftTradeResultData
			{
				ShiftSignupTradeId = trade.ShiftSignupTradeId.ToString(),
				Direction = authority.IsRequester(trade) ? 0 : 1,
				Status = status,
				MyState = (int)trade.GetState(lookups.UserId),
				SourceShiftSignupId = trade.SourceShiftSignupId?.ToString() ?? "",
				SourceUserId = sourceUserId ?? "",
				SourceUserName = lookups.NameFor(sourceUserId),
				ShiftId = trade.IsWorkshiftTrade() ? trade.WorkshiftId ?? "" : source?.ShiftId.ToString() ?? "",
				ShiftName = shift?.Name ?? workshiftDay?.Name ?? "",
				ShiftDayId = trade.IsWorkshiftTrade() ? trade.WorkshiftDayId ?? "" : day?.ShiftDayId.ToString() ?? "",
				ShiftDay = source?.ShiftDay ?? workshiftDay?.Day ?? trade.SourceDay ?? DateTime.MinValue,
				Start = start,
				End = end,
				GroupId = groupId?.ToString() ?? "",
				GroupName = lookups.GroupNameFor(groupId),
				Note = trade.Note ?? "",
				AcceptedUserId = acceptedUserId ?? "",
				AcceptedUserName = lookups.NameFor(acceptedUserId),
				TargetShiftSignupId = trade.TargetShiftSignupId?.ToString() ?? "",
				TargetShiftDay = trade.TargetShiftSignup?.ShiftDay,
				ReviewNote = trade.ReviewNote ?? "",
				ReviewedByName = lookups.NameFor(trade.ReviewedByUserId),
				RequireApproval = shift?.RequireApproval == true,
				CanReview = authority.CanReview(trade, shift),
				IsOpen = trade.IsOpen,
				ShiftType = trade.IsWorkshiftTrade() ? (int)StaffedShiftTypes.Workshift : shift?.AssignmentType ?? 0,
				WorkshiftId = trade.WorkshiftId ?? "",
				WorkshiftDayId = trade.WorkshiftDayId ?? "",
				UnitId = trade.UnitId?.ToString() ?? "",
				UnitName = lookups.UnitNameFor(trade.UnitId),
				RequestedOn = trade.RequestedOn,
				ApplicantCount = (trade.Users ?? new List<ShiftSignupTradeUser>()).Count(x => x.Offered && !x.Declined),
				CanPick = !isOver && authority.CanPick(trade, shift),
				PickIsApproval = authority.IsApprover(shift, groupId),
				CanCancel = !isOver && authority.CanCancel(trade, shift),
				IsOver = isOver
			};

			var me = (trade.Users ?? new List<ShiftSignupTradeUser>()).FirstOrDefault(x => SameUser(x.UserId, lookups.UserId));

			if (eligibility != null && trade.IsOpen && !authority.IsRequester(trade))
			{
				var check = eligibility.Check(lookups.UserId);

				data.IsEligible = check == ShiftActionErrors.None;
				data.IneligibleReason = data.IsEligible ? "" : ToErrorCode(check);
				// Someone who withdrew may apply again; someone with a live offer has nothing more to apply for.
				data.CanApply = !isOver && trade.IsOpenForOffers() && data.IsEligible && (me == null || !me.Offered || me.Declined);
			}

			foreach (var user in trade.Users ?? new List<ShiftSignupTradeUser>())
			{
				var group = lookups.Groups.Values.FirstOrDefault(g => g.Members != null && g.Members.Any(m => SameUser(m.UserId, user.UserId)));
				var userData = new ShiftTradeUserResultData
				{
					UserId = user.UserId,
					Name = lookups.NameFor(user.UserId),
					Declined = user.Declined,
					Offered = user.Offered,
					Reason = user.Reason ?? "",
					RespondedOn = user.RespondedOn,
					GroupName = group?.Name ?? "",
					Roles = lookups.RoleNamesFor(user.UserId)
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

		/// <summary>
		/// What a decision between applicants needs: whether each still qualifies, their certifications, how much they work
		/// in the week either side of the traded day, and any overlapping shift they are already on.
		/// </summary>
		private async Task AddApplicantDetailsAsync(ShiftTradeResultData data, ShiftSignupTrade trade, ShiftTradeEligibility eligibility, Lookups lookups)
		{
			var userIds = data.Users.Select(x => x.UserId).Where(x => !String.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			var today = lookups.LocalNow.Date;
			var tradedKey = trade.IsWorkshiftTrade() ? $"w:{trade.WorkshiftDayId}" : $"s:{data.ShiftDayId}";

			var certifications = (await _certificationService.GetCertificationsForDepartmentAsync(DepartmentId, userIds) ?? new List<PersonnelCertification>())
				.Where(x => x != null && !x.IsDeleted && x.Status == (int)PersonnelCertificationStatuses.Active && (!x.ExpiresOn.HasValue || x.ExpiresOn.Value.Date >= today))
				.ToList();

			var occurrences = data.Start > DateTime.MinValue
				? await _shiftStaffingService.GetStaffingForRangeAsync(DepartmentId, data.Start.Date.AddDays(-7), data.End.Date.AddDays(8)) ?? new List<ShiftDayStaffing>()
				: new List<ShiftDayStaffing>();

			foreach (var user in data.Users)
			{
				var check = eligibility?.Check(user.UserId) ?? ShiftActionErrors.None;
				user.IsEligible = check == ShiftActionErrors.None;
				user.IneligibleReason = user.IsEligible ? "" : ToErrorCode(check);

				user.Certifications = certifications
					.Where(x => SameUser(x.UserId, user.UserId))
					.OrderBy(x => x.Name)
					.Select(x => x.ExpiresOn.HasValue ? $"{x.Name} (expires {x.ExpiresOn.Value:yyyy-MM-dd})" : x.Name)
					.Distinct()
					.ToList();

				var working = occurrences
					.Where(x => x.Key != tradedKey && x.Personnel.Any(p => SameUser(p.UserId, user.UserId)))
					.ToList();

				user.NearbyShiftCount = working.Count;
				user.NearbyShiftHours = Math.Round(working.Sum(x => x.Hours), 1);

				var conflict = working.FirstOrDefault(x => x.Start < data.End && x.End > data.Start);
				user.HasConflict = conflict != null;
				user.ConflictName = conflict?.Name ?? "";
			}
		}

		private ShiftDayResultData ConvertWorkshiftDay(ShiftDayStaffing staffing, Lookups lookups, List<ShiftSignupTrade> myTrades)
		{
			var mine = staffing.Personnel.FirstOrDefault(x => SameUser(x.UserId, lookups.UserId));
			var myTrade = (myTrades ?? new List<ShiftSignupTrade>()).FirstOrDefault(x => String.Equals(x.WorkshiftDayId, staffing.WorkshiftDayId, StringComparison.OrdinalIgnoreCase));

			var data = new ShiftDayResultData
			{
				ShiftId = staffing.WorkshiftId,
				ShiftDayId = staffing.WorkshiftDayId,
				WorkshiftId = staffing.WorkshiftId,
				WorkshiftDayId = staffing.WorkshiftDayId,
				ShiftName = staffing.Name,
				ShiftDay = staffing.Day,
				Start = staffing.Start,
				End = staffing.End,
				StartUtc = lookups.ToUtc(staffing.Start),
				EndUtc = lookups.ToUtc(staffing.End),
				ShiftType = (int)StaffedShiftTypes.Workshift,
				Color = staffing.Color,
				RequireApproval = false,
				SignedUp = mine != null,
				Filled = true,
				OpenSlots = 0,
				IsActive = staffing.IsActive,
				CanManage = lookups.Scope.AllGroups,
				CanSignup = false,
				MyStatus = mine != null ? 1 : 0,
				MySignupId = "",
				MyGroupId = mine?.DepartmentGroupId?.ToString() ?? "",
				MyTradeId = myTrade?.ShiftSignupTradeId.ToString() ?? "",
				MyUnitId = mine?.UnitId?.ToString() ?? "",
				MyUnitName = lookups.UnitNameFor(mine?.UnitId),
				CanTrade = mine != null && !staffing.IsOver && mine.RosterSource != ShiftRosterSources.Trade && myTrade == null,
				Signups = new List<ShiftDaySignupResultData>(),
				Needs = new List<ShiftDayGroupNeedsResultData>(),
				Roster = new List<ShiftDayRosterResultData>()
			};

			foreach (var person in staffing.Personnel.OrderBy(x => lookups.UnitNameFor(x.UnitId)).ThenBy(x => lookups.NameFor(x.UserId)))
			{
				data.Roster.Add(new ShiftDayRosterResultData
				{
					UserId = person.UserId,
					Name = lookups.NameFor(person.UserId),
					GroupId = person.DepartmentGroupId?.ToString() ?? "",
					GroupName = lookups.GroupNameFor(person.DepartmentGroupId),
					RoleIds = lookups.RoleIdsFor(person.UserId),
					Roles = lookups.RoleNamesFor(person.UserId),
					Source = person.RosterSource == ShiftRosterSources.Trade ? (int)ShiftRosterSources.Trade : (int)ShiftRosterSources.Assigned,
					ShiftSignupId = "",
					ApprovalPending = false,
					TradedFromUserId = person.TradedFromUserId ?? "",
					TradedFromName = lookups.NameFor(person.TradedFromUserId),
					UnitId = person.UnitId?.ToString() ?? "",
					UnitName = lookups.UnitNameFor(person.UnitId)
				});
			}

			return data;
		}

		/// <summary>The caller's own static shift day trades that are still open or waiting, to show on their days.</summary>
		private async Task<List<ShiftSignupTrade>> GetMyOpenWorkshiftTradesAsync()
		{
			var trades = await _shiftsService.GetTradesForUserAsync(DepartmentId, UserId) ?? new List<ShiftSignupTrade>();

			return trades.Where(x => x.IsWorkshiftTrade() && SameUser(x.GetSourceUserId(), UserId) && !x.Denied && !x.IsTradeComplete()).ToList();
		}

		private async Task<ShiftDayStaffing> GetWorkshiftDayAsync(Lookups lookups, string workshiftDayId)
		{
			if (String.IsNullOrWhiteSpace(workshiftDayId))
				return null;

			if (!lookups.WorkshiftDays.TryGetValue(workshiftDayId, out var staffing))
			{
				staffing = await _shiftStaffingService.GetWorkshiftDayStaffingAsync(workshiftDayId);
				lookups.WorkshiftDays[workshiftDayId] = staffing;
			}

			return staffing;
		}

		/// <summary>The shift a trade is on (with its groups, for supervision checks); null for a static shift day trade.</summary>
		private async Task<Shift> GetTradeShiftAsync(ShiftSignupTrade trade, Lookups lookups)
		{
			if (trade == null || trade.IsWorkshiftTrade())
				return null;

			var source = trade.SourceShiftSignup;
			var shifts = await GetShiftsAsync(lookups);

			return shifts.FirstOrDefault(x => x.ShiftId == source?.ShiftId) ?? source?.Shift;
		}

		/// <summary>The requester, or someone who supervises or approves the trade's slot.</summary>
		private async Task<bool> MayDecideTradeAsync(ShiftSignupTrade trade, Lookups lookups)
		{
			return lookups.Authority.IsRequester(trade) || lookups.Authority.IsSupervisor(trade, await GetTradeShiftAsync(trade, lookups));
		}

		/// <summary>Asked to take the trade, or applied for it.</summary>
		private static bool IsTradeParticipant(ShiftSignupTrade trade, string userId)
		{
			return (trade?.Users ?? new List<ShiftSignupTradeUser>()).Any(x => x != null && SameUser(x.UserId, userId));
		}

		/// <summary>
		/// What someone who can't decide a trade sees of it: their own note but nobody else's, and the approver's note only
		/// when they are part of the trade.
		/// </summary>
		private static void HideDecisionNotes(ShiftTradeResultData data, ShiftSignupTrade trade, string userId)
		{
			if (!IsTradeParticipant(trade, userId))
				data.ReviewNote = "";

			foreach (var user in data.Users.Where(x => !SameUser(x.UserId, userId)))
				user.Reason = "";
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
			return await GetDepartmentTradeAsync(shiftSignupTradeId) != null;
		}

		/// <summary>The trade, when it belongs to the caller's department (shift trades by their shift, static shift trades by their row).</summary>
		private async Task<ShiftSignupTrade> GetDepartmentTradeAsync(int shiftSignupTradeId)
		{
			var trade = await _shiftsService.GetShiftTradeByIdAsync(shiftSignupTradeId);

			if (trade == null)
				return null;

			if (trade.IsWorkshiftTrade())
				return trade.DepartmentId == DepartmentId ? trade : null;

			return trade.SourceShiftSignup?.Shift != null && trade.SourceShiftSignup.Shift.DepartmentId == DepartmentId ? trade : null;
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
				case ShiftActionErrors.NotEligible: return "not_eligible";
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
