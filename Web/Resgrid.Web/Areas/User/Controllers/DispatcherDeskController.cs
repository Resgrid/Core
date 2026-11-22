using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.DispatcherDesk;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Shifts → Dispatcher Desk (M0324, dispatchers-on-duty-plan.md): the live desk board, the break planner with the coverage
	/// timeline, the desk settings and the desk activity report. Dispatchers (Dispatch app login), shift managers and
	/// department admins see the board and the planner; supervising other dispatchers (ending their break, setting them away,
	/// moving their work, planning their breaks) is for <see cref="IDispatcherPresenceService.CanSuperviseAsync"/>; the settings
	/// are for department admins.
	/// </summary>
	[Area("User")]
	[Authorize]
	public class DispatcherDeskController : SecureBaseController
	{
		/// <summary>How often the board refetches without a realtime event.</summary>
		public const int BoardPollSeconds = 15;

		public const int BoardSlotMinutes = 15;
		public const int PlannerSlotMinutes = 5;
		public const int MaxActivityDays = 92;

		private const string MessageKey = "DispatcherDeskMessage";

		private readonly IDispatcherDeskConfigService _configService;
		private readonly IShiftDispatchersService _shiftDispatchersService;
		private readonly IDispatcherBreaksService _breaksService;
		private readonly IDispatcherPresenceService _presenceService;
		private readonly IDispatcherRoutingService _routingService;
		private readonly IDispatchAccessService _dispatchAccessService;
		private readonly IEnhancedAiAccessService _enhancedAiAccessService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IShiftsService _shiftsService;
		private readonly Resgrid.Model.Services.IAuthorizationService _authorizationService;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.DispatcherDesk.DispatcherDesk> _localizer;

		public DispatcherDeskController(IDispatcherDeskConfigService configService, IShiftDispatchersService shiftDispatchersService,
			IDispatcherBreaksService breaksService, IDispatcherPresenceService presenceService, IDispatcherRoutingService routingService,
			IDispatchAccessService dispatchAccessService, IEnhancedAiAccessService enhancedAiAccessService, IDepartmentsService departmentsService,
			IShiftsService shiftsService, Resgrid.Model.Services.IAuthorizationService authorizationService,
			IStringLocalizer<Resgrid.Localization.Areas.User.DispatcherDesk.DispatcherDesk> localizer)
		{
			_configService = configService;
			_shiftDispatchersService = shiftDispatchersService;
			_breaksService = breaksService;
			_presenceService = presenceService;
			_routingService = routingService;
			_dispatchAccessService = dispatchAccessService;
			_enhancedAiAccessService = enhancedAiAccessService;
			_departmentsService = departmentsService;
			_shiftsService = shiftsService;
			_authorizationService = authorizationService;
			_localizer = localizer;
		}

		#region Board

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> Index()
		{
			var access = await GetAccessAsync();

			if (!access.CanView)
				return Unauthorized();

			var config = await _configService.GetConfigAsync(DepartmentId) ?? new DispatcherDeskConfig();

			return View(new DispatcherDeskIndexView
			{
				DeskEnabled = config.Enabled,
				CanSupervise = access.CanSupervise,
				IsAdmin = access.IsAdmin,
				PollSeconds = BoardPollSeconds
			});
		}

		/// <summary>The board's data: every on-duty or signed-in dispatcher, the open queue and today's coverage strip.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> Board()
		{
			var access = await GetAccessAsync();

			if (!access.CanView)
				return StatusCode(StatusCodes.Status403Forbidden);

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var now = DateTime.UtcNow;
			var snapshot = await _presenceService.GetDeskAsync(DepartmentId, now) ?? new DispatcherDeskSnapshot();
			var names = await GetNamesAsync();
			var (dayStart, dayEnd) = LocalDayUtc(department, now.TimeConverter(department).Date);
			var coverage = await _breaksService.GetCoverageAsync(DepartmentId, dayStart, dayEnd, BoardSlotMinutes) ?? new DispatcherCoverage();

			var board = new DispatcherDeskBoardJson
			{
				Enabled = snapshot.Enabled,
				CanSupervise = access.CanSupervise,
				MyUserId = UserId,
				ServerNowMs = ToMs(now),
				GeneratedOn = FormatTime(now, department),
				MinimumAvailable = snapshot.MinimumAvailable,
				AvailableCount = snapshot.AvailableCount,
				OnBreakCount = snapshot.OnBreakCount,
				OnDutyCount = snapshot.OnDutyCount,
				BelowMinimum = snapshot.BelowMinimum
			};

			foreach (var member in (snapshot.Members ?? new List<DispatcherDeskMember>()).Where(x => x != null && !String.IsNullOrWhiteSpace(x.UserId)))
				board.Members.Add(ToMemberJson(member, names, department, now));

			board.Members = board.Members
				.OrderBy(x => StateOrder(x.State))
				.ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
				.ToList();

			foreach (var item in (snapshot.Queue ?? new List<DispatcherQueueItem>()).Where(x => x != null && x.IsOpen())
				         .OrderByDescending(x => x.Priority).ThenBy(x => x.CreatedOn))
				board.Queue.Add(ToQueueItemJson(item, names, department));

			board.Coverage = ToSlots(coverage, department, now);

			return Json(board);
		}

		/// <summary>Ends a dispatcher's break: their own (returned) or, for a supervisor, anyone's.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> EndBreak(string userId, CancellationToken cancellationToken)
		{
			var target = String.IsNullOrWhiteSpace(userId) ? UserId : userId;
			var self = SameUser(target, UserId);

			if (!await CanActForAsync(target))
				return Json(Refused(DispatcherDeskErrors.NotPermitted));

			var result = await _breaksService.EndBreakAsync(DepartmentId, target, UserId,
				self ? DispatcherBreakEndReasons.Returned : DispatcherBreakEndReasons.Supervisor, cancellationToken);

			return Json(await ToActionJsonAsync(result));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> SetAway(string userId, string reason, CancellationToken cancellationToken)
		{
			var target = String.IsNullOrWhiteSpace(userId) ? UserId : userId;

			if (!await CanActForAsync(target))
				return Json(Refused(DispatcherDeskErrors.NotPermitted));

			var result = await _presenceService.SetAwayAsync(DepartmentId, target, Trim(reason, 250), UserId, cancellationToken);

			return Json(await ToActionJsonAsync(result));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> SetAvailable(string userId, CancellationToken cancellationToken)
		{
			var target = String.IsNullOrWhiteSpace(userId) ? UserId : userId;

			if (!await CanActForAsync(target))
				return Json(Refused(DispatcherDeskErrors.NotPermitted));

			var result = await _presenceService.SetAvailableAsync(DepartmentId, target, UserId, cancellationToken);

			return Json(await ToActionJsonAsync(result));
		}

		/// <summary>Puts a held or offered item back in the queue: its holder, or a supervisor.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> ReleaseItem(string itemId, CancellationToken cancellationToken)
		{
			var item = await FindOpenItemAsync(itemId);

			if (item == null)
				return Json(Refused(DispatcherDeskErrors.ItemNotFound));

			if (!SameUser(item.AssignedUserId, UserId) && !await CanSuperviseAsync())
				return Json(Refused(DispatcherDeskErrors.NotPermitted));

			var result = await _routingService.ReleaseAsync(DepartmentId, item.DispatcherQueueItemId, UserId, cancellationToken);

			return Json(await ToActionJsonAsync(result));
		}

		/// <summary>Hands an item to another on-duty dispatcher (as an offer they accept): its holder, or a supervisor.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> TransferItem(string itemId, string toUserId, string note, CancellationToken cancellationToken)
		{
			if (String.IsNullOrWhiteSpace(toUserId))
				return Json(Refused(DispatcherDeskErrors.InvalidInput));

			var item = await FindOpenItemAsync(itemId);

			if (item == null)
				return Json(Refused(DispatcherDeskErrors.ItemNotFound));

			if (!SameUser(item.AssignedUserId, UserId) && !await CanSuperviseAsync())
				return Json(Refused(DispatcherDeskErrors.NotPermitted));

			var result = await _routingService.TransferAsync(DepartmentId, item.DispatcherQueueItemId, UserId, toUserId, Trim(note, 500), cancellationToken);

			return Json(await ToActionJsonAsync(result));
		}

		#endregion Board

		#region Breaks

		/// <summary>The coverage timeline and break planner for a department-local date, or for one shift day.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> Breaks(string date, int? shiftDayId)
		{
			var access = await GetAccessAsync();

			if (!access.CanView)
				return Unauthorized();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var config = await _configService.GetConfigAsync(DepartmentId) ?? new DispatcherDeskConfig();
			var model = new DispatcherDeskBreaksView { CanSupervise = access.CanSupervise, DeskEnabled = config.Enabled };

			if (shiftDayId.HasValue && shiftDayId.Value > 0)
			{
				var schedule = await _shiftsService.GetShiftDayScheduleAsync(shiftDayId.Value);

				if (schedule?.Shift == null || schedule.Day == null)
					return RedirectToAction("Breaks", new { date });

				if (schedule.Shift.DepartmentId != DepartmentId)
					return Unauthorized();

				model.ShiftDayId = schedule.Day.ShiftDayId;
				model.ShiftName = schedule.Shift.Name;
				model.Date = schedule.Day.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
			}
			else
			{
				model.Date = (TryParseDate(date, out var parsed) ? parsed : DateTime.UtcNow.TimeConverter(department).Date)
					.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
			}

			return View(model);
		}

		/// <summary>The planner's data: duty and break bars per dispatcher, the coverage lane and the warnings.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> BreaksData(string date, int? shiftDayId)
		{
			var access = await GetAccessAsync();

			if (!access.CanView)
				return StatusCode(StatusCodes.Status403Forbidden);

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var now = DateTime.UtcNow;
			DateTime fromUtc, toUtc;
			string title;
			int? dayId = null;

			if (shiftDayId.HasValue && shiftDayId.Value > 0)
			{
				var schedule = await _shiftsService.GetShiftDayScheduleAsync(shiftDayId.Value);

				if (schedule?.Shift == null || schedule.Day == null || schedule.Shift.DepartmentId != DepartmentId)
					return StatusCode(StatusCodes.Status404NotFound);

				dayId = schedule.Day.ShiftDayId;
				fromUtc = schedule.Day.Start.DepartmentLocalToUtc(department);
				toUtc = schedule.Day.End.DepartmentLocalToUtc(department);
				title = $"{schedule.Shift.Name}: {FormatDateTime(fromUtc, department)} – {FormatDateTime(toUtc, department)}";
			}
			else
			{
				var localDate = TryParseDate(date, out var parsed) ? parsed : now.TimeConverter(department).Date;
				(fromUtc, toUtc) = LocalDayUtc(department, localDate);
				title = localDate.ToString("D", CultureInfo.CurrentUICulture);
			}

			if (toUtc <= fromUtc)
				toUtc = fromUtc.AddDays(1);

			var coverage = await _breaksService.GetCoverageAsync(DepartmentId, fromUtc, toUtc, PlannerSlotMinutes) ?? new DispatcherCoverage();
			var names = await GetNamesAsync();
			var selfShifts = await GetSelfSchedulingShiftIdsAsync();

			var data = new DispatcherBreaksJson
			{
				CanSupervise = access.CanSupervise,
				MyUserId = UserId,
				FromMs = ToMs(fromUtc),
				ToMs = ToMs(toUtc),
				ServerNowMs = ToMs(now),
				SlotMinutes = coverage.SlotMinutes > 0 ? coverage.SlotMinutes : PlannerSlotMinutes,
				ShiftDayId = dayId,
				Title = title,
				Slots = ToSlots(coverage, department, now),
				Ticks = BuildTicks(fromUtc, toUtc, department)
			};

			var duty = (coverage.Duty ?? new List<DispatcherDutyWindow>()).Where(x => x != null && !String.IsNullOrWhiteSpace(x.UserId)).ToList();
			var breaks = (coverage.Breaks ?? new List<DispatcherBreak>()).Where(x => x != null && !String.IsNullOrWhiteSpace(x.UserId)).ToList();

			foreach (var userId in duty.Select(x => x.UserId).Concat(breaks.Select(x => x.UserId)).Distinct(StringComparer.OrdinalIgnoreCase))
			{
				var windows = duty.Where(x => SameUser(x.UserId, userId)).OrderBy(x => x.StartUtc).ToList();
				var isMe = SameUser(userId, UserId);
				var row = new DispatcherTimelineRowJson
				{
					UserId = windows.Select(x => x.UserId).FirstOrDefault() ?? userId,
					Name = NameFor(names, userId),
					IsMe = isMe,
					IsSupervisor = windows.Any(x => x.IsSupervisor),
					CanEdit = access.CanSupervise || (isMe && windows.Any(x => !x.IsPending && selfShifts.Contains(x.ShiftId)))
				};

				foreach (var window in windows)
				{
					row.Duty.Add(new DispatcherTimelineDutyJson
					{
						ShiftId = window.ShiftId,
						ShiftDayId = window.ShiftDayId,
						ShiftName = window.ShiftName,
						Color = SafeColor(window.ShiftColor),
						StartMs = ToMs(window.StartUtc),
						EndMs = ToMs(window.EndUtc),
						Label = $"{window.ShiftName} {FormatTime(window.StartUtc, department)} – {FormatTime(window.EndUtc, department)}",
						IsPending = window.IsPending,
						StartInput = ToInput(window.StartUtc, department)
					});
				}

				foreach (var dispatcherBreak in breaks.Where(x => SameUser(x.UserId, userId)).OrderBy(x => x.StartedOn ?? x.ScheduledStartOn))
				{
					var canEdit = dispatcherBreak.State == (int)DispatcherBreakStates.Scheduled &&
					              (access.CanSupervise || (isMe && dispatcherBreak.ShiftId.HasValue && selfShifts.Contains(dispatcherBreak.ShiftId.Value)));

					row.Breaks.Add(ToBreakJson(dispatcherBreak, department, now, canEdit));
				}

				data.Rows.Add(row);
			}

			data.Rows = data.Rows.OrderByDescending(x => x.IsMe).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
			data.CanPlan = access.CanSupervise && dayId.HasValue;
			data.Warnings = (coverage.Warnings ?? new List<DispatcherDeskWarning>()).Select(x => WarningText(x, names, department)).Where(x => x != null).Distinct().ToList();
			data.ShiftDays = duty
				.GroupBy(x => x.ShiftDayId)
				.Select(x => x.OrderBy(w => w.StartUtc).First())
				.OrderBy(x => x.StartUtc)
				.Select(x => new DispatcherShiftDayOptionJson
				{
					ShiftDayId = x.ShiftDayId,
					Label = $"{x.ShiftName} ({FormatTime(x.StartUtc, department)} – {FormatTime(duty.Where(w => w.ShiftDayId == x.ShiftDayId).Max(w => w.EndUtc), department)})"
				})
				.ToList();

			return Json(data);
		}

		/// <summary>
		/// Plans or re-plans a break. Supervisors plan anyone's; a dispatcher plans their own when the shift allows self-scheduled
		/// breaks. Warnings (overlaps, coverage below the minimum) come back with the saved break.
		/// </summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> SaveBreak(DispatcherBreakEditInput input, CancellationToken cancellationToken)
		{
			if (input == null)
				return Json(Refused(DispatcherDeskErrors.InvalidInput));

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			if (!TryParseLocal(input.Start, out var localStart))
				return Json(Refused(DispatcherDeskErrors.InvalidInput));

			if (input.DurationMinutes <= 0 || input.DurationMinutes > DispatcherDeskForms.MaxBreakMinutes)
				return Json(Refused(DispatcherDeskErrors.InvalidInput));

			var startUtc = localStart.DepartmentLocalToUtc(department);
			var now = DateTime.UtcNow;
			DispatcherBreak entity;

			if (!String.IsNullOrWhiteSpace(input.DispatcherBreakId))
			{
				entity = await _breaksService.GetBreakAsync(DepartmentId, input.DispatcherBreakId);

				if (entity == null || entity.DepartmentId != DepartmentId)
					return Json(Refused(DispatcherDeskErrors.BreakNotFound));

				if (entity.State != (int)DispatcherBreakStates.Scheduled)
					return Json(Refused(DispatcherDeskErrors.BreakNotStartable));
			}
			else
			{
				var userId = String.IsNullOrWhiteSpace(input.UserId) ? UserId : input.UserId;
				var window = await FindDutyWindowAsync(userId, input.ShiftDayId, startUtc);

				entity = new DispatcherBreak
				{
					DepartmentId = DepartmentId,
					UserId = window?.UserId ?? userId,
					ShiftId = window?.ShiftId,
					ShiftDayId = window?.ShiftDayId,
					State = (int)DispatcherBreakStates.Scheduled,
					CreatedByUserId = UserId,
					CreatedOn = now
				};
			}

			if (!await CanPlanBreakAsync(entity.UserId, entity.ShiftId))
				return Json(Refused(DispatcherDeskErrors.NotPermitted));

			entity.BreakType = Enum.IsDefined(typeof(DispatcherBreakTypes), input.BreakType) ? input.BreakType : (int)DispatcherBreakTypes.Break;
			entity.DurationMinutes = input.DurationMinutes;
			entity.ScheduledStartOn = startUtc;
			entity.Note = Trim(input.Note, 500);
			entity.UpdatedByUserId = UserId;
			entity.UpdatedOn = now;

			var result = await _breaksService.SaveBreakAsync(DepartmentId, entity, UserId, cancellationToken);

			return Json(await ToActionJsonAsync(result, department));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> CancelBreak(string dispatcherBreakId, CancellationToken cancellationToken)
		{
			var entity = String.IsNullOrWhiteSpace(dispatcherBreakId) ? null : await _breaksService.GetBreakAsync(DepartmentId, dispatcherBreakId);

			if (entity == null || entity.DepartmentId != DepartmentId)
				return Json(Refused(DispatcherDeskErrors.BreakNotFound));

			if (!await CanPlanBreakAsync(entity.UserId, entity.ShiftId))
				return Json(Refused(DispatcherDeskErrors.NotPermitted));

			var result = await _breaksService.CancelBreakAsync(DepartmentId, entity.DispatcherBreakId, UserId, cancellationToken);

			return Json(await ToActionJsonAsync(result));
		}

		/// <summary>Staggers the shift's break plan across the day's dispatchers (supervisors).</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> PlanBreaks(int shiftDayId, bool replace, CancellationToken cancellationToken)
		{
			var day = await _shiftsService.GetShiftDayByIdAsync(shiftDayId);
			var shift = day != null ? await _shiftsService.GetShiftByIdAsync(day.ShiftId) : null;

			if (day == null || shift == null || shift.DepartmentId != DepartmentId)
				return Json(Refused(DispatcherDeskErrors.ShiftNotFound));

			if (!await CanSuperviseAsync())
				return Json(Refused(DispatcherDeskErrors.NotPermitted));

			var result = await _breaksService.PlanBreaksAsync(DepartmentId, shiftDayId, replace, UserId, cancellationToken);
			var json = await ToActionJsonAsync(result);

			if (result != null && result.Success)
				json.Message = String.Format(_localizer["BreaksPlannedFormat"].Value, result.Value?.Count ?? 0);

			return Json(json);
		}

		#endregion Breaks

		#region Settings

		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> Settings()
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Unauthorized();

			var model = DispatcherDeskSettingsView.From(await _configService.GetConfigAsync(DepartmentId, true));
			await SetSettingsAvailabilityAsync(model);
			model.Message = TempData?[MessageKey] as string;

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> Settings(DispatcherDeskSettingsView model, IFormCollection form, CancellationToken cancellationToken)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin())
				return Unauthorized();

			if (model == null)
				return RedirectToAction("Settings");

			var current = await _configService.GetConfigAsync(DepartmentId, true) ?? new DispatcherDeskConfig();
			var plan = DispatcherDeskForms.ReadBreakPlan(form);
			var config = model.ToConfig(plan);

			await SetSettingsAvailabilityAsync(model);

			// The Enhanced AI choices need the add-on: a department without it keeps the value it already has, but cannot switch to it.
			if (!model.EnhancedAiAvailable)
			{
				if (config.GetEmailCallMode() == DispatcherEmailCallModes.EnhancedAi && current.GetEmailCallMode() != DispatcherEmailCallModes.EnhancedAi)
					ModelState.AddModelError(nameof(model.EmailCallMode), _localizer["EnhancedAiUnavailable"]);

				if (config.GetTextOverflowMode() == DispatcherTextOverflowModes.EnhancedAi && current.GetTextOverflowMode() != DispatcherTextOverflowModes.EnhancedAi)
					ModelState.AddModelError(nameof(model.TextOverflowMode), _localizer["EnhancedAiUnavailable"]);
			}

			if (!ModelState.IsValid)
			{
				model.DefaultBreakPlan = DispatcherDeskForms.ToRows(plan);
				return View(model);
			}

			await _configService.SaveConfigAsync(DepartmentId, config, UserId, cancellationToken);

			TempData[MessageKey] = _localizer["SettingsSaved"].Value;

			return RedirectToAction("Settings");
		}

		#endregion Settings

		#region Activity

		/// <summary>Desk activity for a department-local date range (supervisors and admins).</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<IActionResult> Activity(string from, string to)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin() && !await CanSuperviseAsync())
				return Unauthorized();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var today = DateTime.UtcNow.TimeConverter(department).Date;
			var toDate = TryParseDate(to, out var parsedTo) ? parsedTo : today;
			var fromDate = TryParseDate(from, out var parsedFrom) ? parsedFrom : toDate.AddDays(-6);

			if (fromDate > toDate)
				(fromDate, toDate) = (toDate, fromDate);

			if ((toDate - fromDate).TotalDays >= MaxActivityDays)
				fromDate = toDate.AddDays(-(MaxActivityDays - 1));

			var fromUtc = LocalDayUtc(department, fromDate).FromUtc;
			var toUtc = LocalDayUtc(department, toDate).ToUtc;
			var stats = await _routingService.GetStatsAsync(DepartmentId, fromUtc, toUtc) ?? new DispatcherQueueStats();
			var names = await GetNamesAsync();

			var model = new DispatcherDeskActivityView
			{
				From = fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
				To = toDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
				Created = stats.Created,
				Completed = stats.Completed,
				Overflowed = stats.Overflowed,
				Cancelled = stats.Cancelled,
				Escalated = stats.Escalated,
				AverageSecondsToAnswer = stats.AverageSecondsToAnswer,
				CreatedByKind = (stats.CreatedByKind ?? new Dictionary<int, int>()).OrderBy(x => x.Key).ToList(),
				Dispatchers = (stats.Dispatchers ?? new List<DispatcherDeskStats>()).Where(x => x != null).ToList()
			};

			foreach (var row in model.Dispatchers.Where(x => String.IsNullOrWhiteSpace(x.Name)))
				row.Name = NameFor(names, row.UserId);

			model.Dispatchers = model.Dispatchers.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

			return View(model);
		}

		#endregion Activity

		#region Access

		internal sealed class DeskAccess
		{
			public bool IsAdmin { get; set; }
			public bool IsDispatcher { get; set; }
			public bool IsShiftManager { get; set; }
			public bool CanSupervise { get; set; }

			/// <summary>Dispatchers, shift managers, desk supervisors and department admins see the board and the planner.</summary>
			public bool CanView => IsAdmin || IsDispatcher || IsShiftManager || CanSupervise;
		}

		private async Task<DeskAccess> GetAccessAsync()
		{
			var scope = await _authorizationService.GetShiftManagementScopeAsync(UserId, DepartmentId) ?? ShiftManagementScope.None();

			return new DeskAccess
			{
				IsAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin(),
				IsDispatcher = await _dispatchAccessService.CanUseDispatchAsync(DepartmentId, UserId),
				IsShiftManager = scope.IsSupervisor,
				CanSupervise = await CanSuperviseAsync()
			};
		}

		private Task<bool> CanSuperviseAsync() => _presenceService.CanSuperviseAsync(DepartmentId, UserId);

		/// <summary>A dispatcher acts for themselves; acting for someone else needs a supervisor.</summary>
		private async Task<bool> CanActForAsync(string userId)
		{
			if (SameUser(userId, UserId))
				return await _dispatchAccessService.CanUseDispatchAsync(DepartmentId, UserId) || await CanSuperviseAsync();

			return await CanSuperviseAsync();
		}

		/// <summary>Supervisors plan anyone's breaks; a dispatcher plans their own on a shift that allows self-scheduled breaks.</summary>
		private async Task<bool> CanPlanBreakAsync(string userId, int? shiftId)
		{
			if (await CanSuperviseAsync())
				return true;

			if (!SameUser(userId, UserId) || !shiftId.HasValue)
				return false;

			return (await GetSelfSchedulingShiftIdsAsync()).Contains(shiftId.Value);
		}

		private async Task<HashSet<int>> GetSelfSchedulingShiftIdsAsync()
		{
			return new HashSet<int>((await _shiftDispatchersService.GetSettingsForDepartmentAsync(DepartmentId) ?? new List<ShiftDispatchSetting>())
				.Where(x => x != null && x.IsEnabled && x.AllowSelfScheduledBreaks)
				.Select(x => x.ShiftId));
		}

		/// <summary>The dispatcher's duty window a new break belongs to: on the given shift day, else the one covering its start.</summary>
		private async Task<DispatcherDutyWindow> FindDutyWindowAsync(string userId, int? shiftDayId, DateTime startUtc)
		{
			if (shiftDayId.HasValue && shiftDayId.Value > 0)
			{
				var roster = await _shiftDispatchersService.GetDayRosterAsync(DepartmentId, shiftDayId.Value) ?? new List<DispatcherDutyWindow>();
				var window = roster.FirstOrDefault(x => x != null && SameUser(x.UserId, userId) && !x.IsPending);

				if (window != null)
					return window;
			}

			return await _shiftDispatchersService.GetDutyForUserAsync(DepartmentId, userId, startUtc);
		}

		private async Task<DispatcherQueueItem> FindOpenItemAsync(string itemId)
		{
			if (String.IsNullOrWhiteSpace(itemId))
				return null;

			return (await _routingService.GetOpenItemsAsync(DepartmentId) ?? new List<DispatcherQueueItem>())
				.FirstOrDefault(x => x != null && x.DepartmentId == DepartmentId && String.Equals(x.DispatcherQueueItemId, itemId, StringComparison.OrdinalIgnoreCase));
		}

		private async Task SetSettingsAvailabilityAsync(DispatcherDeskSettingsView model)
		{
			model.EnhancedAiAvailable = await CanUseAiAsync(FeatureFlagKeys.AiDispatchTemplate);
			model.VoiceAiAvailable = await CanUseAiAsync(FeatureFlagKeys.AiVoice);
		}

		private async Task<bool> CanUseAiAsync(string capabilityFlag)
		{
			try
			{
				return await _enhancedAiAccessService.CanUseAsync(DepartmentId, capabilityFlag);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return false;
			}
		}

		#endregion Access

		#region Mapping

		private DispatcherDeskMemberJson ToMemberJson(DispatcherDeskMember member, Dictionary<string, string> names, Department department, DateTime now)
		{
			var json = new DispatcherDeskMemberJson
			{
				UserId = member.UserId,
				Name = !String.IsNullOrWhiteSpace(member.Name) ? member.Name : NameFor(names, member.UserId),
				IsMe = SameUser(member.UserId, UserId),
				State = (int)member.DisplayState,
				IsOnDuty = member.IsOnDuty,
				IsSupervisor = member.IsSupervisor,
				IsSignedIn = member.Presence != null && member.Presence.IsSignedIn(),
				ShiftName = member.Duty?.ShiftName,
				DutyEnds = member.Duty != null ? FormatTime(member.Duty.EndUtc, department)
					: member.Presence?.DutyEndsOn != null ? FormatTime(member.Presence.DutyEndsOn.Value, department) : null,
				DeviceKind = member.Presence?.DeviceKind,
				AwayReason = member.DisplayState == DispatcherDisplayStates.Away ? member.Presence?.AwayReason : null,
				ActiveItems = member.ActiveItemCount,
				OfferedItems = member.OfferedItemCount,
				Capacity = member.Capacity,
				OverdueMinutes = member.OverdueMinutes
			};

			if (member.ActiveBreak != null && member.DisplayState == DispatcherDisplayStates.OnBreak)
			{
				var started = member.ActiveBreak.StartedOn;
				var dueBack = member.ActiveBreak.ExpectedReturnOn ?? started?.AddMinutes(member.ActiveBreak.DurationMinutes);

				json.BreakType = member.ActiveBreak.BreakType;
				json.BreakMinutes = member.ActiveBreak.DurationMinutes;
				json.BreakStarted = started.HasValue ? FormatTime(started.Value, department) : null;
				json.BreakDueBackMs = dueBack.HasValue ? ToMs(dueBack.Value) : (long?)null;
			}

			if (member.NextBreak?.ScheduledStartOn != null)
				json.NextBreak = String.Format(_localizer["NextBreakFormat"].Value, FormatTime(member.NextBreak.ScheduledStartOn.Value, department), member.NextBreak.DurationMinutes);

			return json;
		}

		private DispatcherDeskQueueItemJson ToQueueItemJson(DispatcherQueueItem item, Dictionary<string, string> names, Department department)
		{
			return new DispatcherDeskQueueItemJson
			{
				ItemId = item.DispatcherQueueItemId,
				Kind = item.Kind,
				State = item.State,
				Source = !String.IsNullOrWhiteSpace(item.SourceName) ? item.SourceName : item.SourceAddress,
				Subject = item.Subject,
				Preview = DispatcherQueueItem.TruncatePreview(StringHelpers.HtmlToPlainText(item.Preview ?? "")),
				CreatedMs = ToMs(item.CreatedOn),
				Created = FormatTime(item.CreatedOn, department),
				Due = item.DueOn.HasValue ? FormatDateTime(item.DueOn.Value, department) : null,
				AssignedUserId = item.AssignedUserId,
				AssignedName = !String.IsNullOrWhiteSpace(item.AssignedUserId) ? NameFor(names, item.AssignedUserId) : null,
				AssignedToMe = SameUser(item.AssignedUserId, UserId),
				CallId = item.CallId,
				Escalated = item.EscalatedOn.HasValue,
				Priority = item.Priority
			};
		}

		private DispatcherTimelineBreakJson ToBreakJson(DispatcherBreak dispatcherBreak, Department department, DateTime now, bool canEdit)
		{
			var plannedEnd = dispatcherBreak.ScheduledEndOn();
			var overdue = dispatcherBreak.State == (int)DispatcherBreakStates.Active && dispatcherBreak.ExpectedReturnOn.HasValue && dispatcherBreak.ExpectedReturnOn.Value < now;
			var shown = dispatcherBreak.StartedOn ?? dispatcherBreak.ScheduledStartOn;

			return new DispatcherTimelineBreakJson
			{
				BreakId = dispatcherBreak.DispatcherBreakId,
				ShiftDayId = dispatcherBreak.ShiftDayId,
				BreakType = dispatcherBreak.BreakType,
				State = dispatcherBreak.State,
				DurationMinutes = dispatcherBreak.DurationMinutes,
				PlannedStartMs = dispatcherBreak.ScheduledStartOn.HasValue ? ToMs(dispatcherBreak.ScheduledStartOn.Value) : (long?)null,
				PlannedEndMs = plannedEnd.HasValue ? ToMs(plannedEnd.Value) : (long?)null,
				StartedMs = dispatcherBreak.StartedOn.HasValue ? ToMs(dispatcherBreak.StartedOn.Value) : (long?)null,
				EndedMs = dispatcherBreak.EndedOn.HasValue ? ToMs(dispatcherBreak.EndedOn.Value) : (long?)null,
				ExpectedReturnMs = dispatcherBreak.ExpectedReturnOn.HasValue ? ToMs(dispatcherBreak.ExpectedReturnOn.Value) : (long?)null,
				Overdue = overdue,
				Label = shown.HasValue
					? $"{FormatTime(shown.Value, department)} · {String.Format(_localizer["MinutesShortFormat"].Value, dispatcherBreak.DurationMinutes)}"
					: String.Format(_localizer["MinutesShortFormat"].Value, dispatcherBreak.DurationMinutes),
				Note = dispatcherBreak.Note,
				StartInput = dispatcherBreak.ScheduledStartOn.HasValue ? ToInput(dispatcherBreak.ScheduledStartOn.Value, department) : null,
				CanEdit = canEdit
			};
		}

		private static List<DispatcherCoverageSlotJson> ToSlots(DispatcherCoverage coverage, Department department, DateTime now)
		{
			return (coverage?.Slots ?? new List<DispatcherCoverageSlot>()).Where(x => x != null).OrderBy(x => x.StartUtc).Select(x => new DispatcherCoverageSlotJson
			{
				StartMs = ToMs(x.StartUtc),
				EndMs = ToMs(x.EndUtc),
				Label = $"{FormatTime(x.StartUtc, department)} – {FormatTime(x.EndUtc, department)}",
				OnDuty = x.OnDuty,
				OnBreak = x.OnBreak,
				Available = x.Available,
				Minimum = x.Minimum,
				BelowMinimum = x.BelowMinimum,
				BreaksOverlap = x.BreaksOverlap,
				IsNow = now >= x.StartUtc && now < x.EndUtc
			}).ToList();
		}

		/// <summary>Hour ticks along the timeline (every 2 or 3 hours on a long window), labelled in department time.</summary>
		private static List<DispatcherTimelineTickJson> BuildTicks(DateTime fromUtc, DateTime toUtc, Department department)
		{
			var ticks = new List<DispatcherTimelineTickJson>();
			var hours = (toUtc - fromUtc).TotalHours;
			var step = hours > 36 ? 3 : hours > 18 ? 2 : 1;
			var tick = new DateTime(fromUtc.Year, fromUtc.Month, fromUtc.Day, fromUtc.Hour, 0, 0, DateTimeKind.Utc);

			if (tick < fromUtc)
				tick = tick.AddHours(1);

			for (var i = 0; tick <= toUtc && i < 200; tick = tick.AddHours(1), i++)
			{
				if (tick.TimeConverter(department).Hour % step != 0)
					continue;

				ticks.Add(new DispatcherTimelineTickJson { Ms = ToMs(tick), Label = FormatTime(tick, department) });
			}

			return ticks;
		}

		private async Task<DispatcherDeskActionJson> ToActionJsonAsync<T>(DispatcherDeskResult<T> result, Department department = null)
		{
			if (result == null)
				return Refused(DispatcherDeskErrors.InvalidInput);

			var json = result.Success
				? new DispatcherDeskActionJson { Success = true, Error = "", Message = _localizer["ActionDone"].Value }
				: Refused(result.Error);

			if (result.Warnings != null && result.Warnings.Any())
			{
				department ??= await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
				var names = await GetNamesAsync();
				json.Warnings = result.Warnings.Where(x => x != null).Select(x => WarningText(x, names, department)).Where(x => x != null).Distinct().ToList();
			}

			return json;
		}

		private DispatcherDeskActionJson Refused(DispatcherDeskErrors error)
		{
			if (error == DispatcherDeskErrors.None)
				error = DispatcherDeskErrors.InvalidInput;

			return new DispatcherDeskActionJson { Success = false, Error = error.ToString(), Message = _localizer["Error." + error].Value };
		}

		internal string WarningText(DispatcherDeskWarning warning, Dictionary<string, string> names, Department department)
		{
			if (warning == null)
				return null;

			var who = String.Join(", ", (warning.UserIds ?? new List<string>()).Where(x => !String.IsNullOrWhiteSpace(x)).Select(x => NameFor(names, x)).Distinct());
			var from = warning.StartUtc.HasValue ? FormatTime(warning.StartUtc.Value, department) : "";
			var to = warning.EndUtc.HasValue ? FormatTime(warning.EndUtc.Value, department) : "";

			switch (warning.Kind)
			{
				case DispatcherDeskWarningKinds.CoverageBelowMinimum:
					return String.Format(_localizer["Warning.CoverageBelowMinimum"].Value, warning.Available ?? 0, warning.Minimum ?? 0, from, to);
				case DispatcherDeskWarningKinds.BreaksOverlap:
					return String.Format(_localizer["Warning.BreaksOverlap"].Value, who, from, to);
				case DispatcherDeskWarningKinds.OutsideDuty:
					return String.Format(_localizer["Warning.OutsideDuty"].Value, who);
				case DispatcherDeskWarningKinds.NoCoverage:
					return String.Format(_localizer["Warning.NoCoverage"].Value, from, to);
				case DispatcherDeskWarningKinds.WorkReturnedToQueue:
					return String.Format(_localizer["Warning.WorkReturnedToQueue"].Value, who);
				case DispatcherDeskWarningKinds.StartedLate:
					return String.Format(_localizer["Warning.StartedLate"].Value, who, warning.Minutes ?? 0);
				case DispatcherDeskWarningKinds.StartedEarly:
					return String.Format(_localizer["Warning.StartedEarly"].Value, who, warning.Minutes ?? 0);
				default:
					return null;
			}
		}

		#endregion Mapping

		#region Helpers

		private async Task<Dictionary<string, string>> GetNamesAsync()
		{
			var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

			foreach (var name in await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId) ?? new List<PersonName>())
			{
				if (name != null && !String.IsNullOrWhiteSpace(name.UserId))
					names[name.UserId] = name.Name;
			}

			return names;
		}

		private string NameFor(Dictionary<string, string> names, string userId)
		{
			return userId != null && names.TryGetValue(userId, out var name) && !String.IsNullOrWhiteSpace(name) ? name : _localizer["UnknownPerson"].Value;
		}

		/// <summary>The department-local day [00:00, next 00:00) as UTC.</summary>
		internal static (DateTime FromUtc, DateTime ToUtc) LocalDayUtc(Department department, DateTime localDate)
		{
			var start = DateTime.SpecifyKind(localDate.Date, DateTimeKind.Unspecified);

			return (start.DepartmentLocalToUtc(department), start.AddDays(1).DepartmentLocalToUtc(department));
		}

		internal static string FormatTime(DateTime utc, Department department)
		{
			var local = utc.TimeConverter(department);

			return local.ToString(department?.Use24HourTime == true ? "HH:mm" : "h:mm tt", CultureInfo.InvariantCulture);
		}

		private static string FormatDateTime(DateTime utc, Department department)
		{
			var local = utc.TimeConverter(department);

			return local.ToString(department?.Use24HourTime == true ? "MM/dd HH:mm" : "MM/dd h:mm tt", CultureInfo.InvariantCulture);
		}

		private static string ToInput(DateTime utc, Department department)
		{
			return utc.TimeConverter(department).ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);
		}

		public static long ToMs(DateTime utc)
		{
			return new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
		}

		internal static bool TryParseDate(string value, out DateTime date)
		{
			return DateTime.TryParseExact((value ?? "").Trim(), new[] { "yyyy-MM-dd", "MM/dd/yyyy", "M/d/yyyy" }, CultureInfo.InvariantCulture,
				DateTimeStyles.None, out date);
		}

		/// <summary>A department-local date and time from a datetime-local input (yyyy-MM-ddTHH:mm), Kind unspecified.</summary>
		internal static bool TryParseLocal(string value, out DateTime local)
		{
			var parsed = DateTime.TryParseExact((value ?? "").Trim(), new[] { "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd HH:mm" },
				CultureInfo.InvariantCulture, DateTimeStyles.None, out local);
			local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

			return parsed;
		}

		/// <summary>Shift colours come from a picker as #rrggbb; anything else is not passed to the page.</summary>
		private static string SafeColor(string color)
		{
			return !String.IsNullOrWhiteSpace(color) && System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9a-fA-F]{3,8}$") ? color : null;
		}

		private static int StateOrder(int state)
		{
			switch ((DispatcherDisplayStates)state)
			{
				case DispatcherDisplayStates.Available: return 0;
				case DispatcherDisplayStates.Busy: return 1;
				case DispatcherDisplayStates.OnBreak: return 2;
				case DispatcherDisplayStates.Away: return 3;
				default: return 4;
			}
		}

		private static string Trim(string value, int max)
		{
			if (String.IsNullOrWhiteSpace(value))
				return null;

			var trimmed = value.Trim();
			return trimmed.Length <= max ? trimmed : trimmed.Substring(0, max);
		}

		private static bool SameUser(string a, string b)
		{
			return a != null && b != null && String.Equals(a, b, StringComparison.OrdinalIgnoreCase);
		}

		#endregion Helpers
	}
}
