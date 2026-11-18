using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Localization;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Dispatch;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Calls → Recurring Calls (M0319): calls dispatched on a schedule ("interfacility transfers every Tuesday and Thursday at
	/// 08:00, 13:00 and 19:00"), and the calendar of scheduled calls. Anyone who can see calls sees the schedules and the calendar
	/// (limited to their dispatch scope); creating one needs call creation, and changing one is for its creator or a department
	/// admin, the same people who may change the scheduled calls it creates.
	/// </summary>
	[Area("User")]
	[Authorize]
	public class RecurringCallsController : SecureBaseController
	{
		/// <summary>The lead time choices on the form, in minutes.</summary>
		public static readonly int[] LeadTimeChoices = { 60, 120, 240, 480, 720, 1440, 2880, 4320, 10080 };

		/// <summary>Dispatcher mode: how long before an occurrence the dispatcher is alerted, in minutes.</summary>
		public static readonly int[] ManualAlertChoices = { 0, 5, 10, 15, 30, 60, 120, 240 };

		private readonly IRecurringCallsService _recurringCallsService;
		private readonly ICallsService _callsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentGroupsService _departmentGroupsService;
		private readonly IUnitsService _unitsService;
		private readonly IPersonnelRolesService _personnelRolesService;
		private readonly IUserProfileService _userProfileService;
		private readonly IMappingService _mappingService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly Resgrid.Model.Services.IAuthorizationService _authorizationService;
		private readonly IFeatureToggleService _featureToggleService;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Dispatch.RecurringCalls> _localizer;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Dispatch.Call> _callLocalizer;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Department.Department> _departmentLocalizer;

		public RecurringCallsController(IRecurringCallsService recurringCallsService, ICallsService callsService, IDepartmentsService departmentsService,
			IDepartmentGroupsService departmentGroupsService, IUnitsService unitsService, IPersonnelRolesService personnelRolesService,
			IUserProfileService userProfileService, IMappingService mappingService, IDepartmentSettingsService departmentSettingsService,
			Resgrid.Model.Services.IAuthorizationService authorizationService, IFeatureToggleService featureToggleService,
			IStringLocalizer<Resgrid.Localization.Areas.User.Dispatch.RecurringCalls> localizer,
			IStringLocalizer<Resgrid.Localization.Areas.User.Dispatch.Call> callLocalizer,
			IStringLocalizer<Resgrid.Localization.Areas.User.Department.Department> departmentLocalizer)
		{
			_recurringCallsService = recurringCallsService;
			_callsService = callsService;
			_departmentsService = departmentsService;
			_departmentGroupsService = departmentGroupsService;
			_unitsService = unitsService;
			_personnelRolesService = personnelRolesService;
			_userProfileService = userProfileService;
			_mappingService = mappingService;
			_departmentSettingsService = departmentSettingsService;
			_authorizationService = authorizationService;
			_featureToggleService = featureToggleService;
			_localizer = localizer;
			_callLocalizer = callLocalizer;
			_departmentLocalizer = departmentLocalizer;
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<IActionResult> Index()
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var priorities = await _callsService.GetActiveCallPrioritiesForDepartmentAsync(DepartmentId) ?? new List<DepartmentCallPriority>();
			var now = DateTime.UtcNow;
			var today = RecurringCallRecurrence.ToLocal(now, department).Date;

			var canCreate = await CanCreateAsync();
			var model = new RecurringCallsIndexView
			{
				CanCreate = canCreate,
				Message = TempData?["RecurringCallsMessage"] as string,
				ErrorMessage = TempData?["RecurringCallsError"] as string
			};

			var upcoming = await UpcomingDispatchesAsync(department);
			var alerts = await _recurringCallsService.GetAlertsAsync(DepartmentId, UserId);
			model.Alerts = alerts.Select(x => ToAlertRow(x, department, canCreate)).ToList();
			var alertCounts = alerts.GroupBy(x => x.Alert.RecurringCallId).ToDictionary(x => x.Key, x => x.Count());

			// The schedules in the member's dispatch scope, as the calendar and the alerts show them.
			foreach (var series in await _recurringCallsService.GetAllVisibleToUserAsync(DepartmentId, UserId))
			{
				var next = series.IsActive ? NextDispatches(series, upcoming, department, 1).FirstOrDefault() : (DateTime?)null;
				var priority = priorities.FirstOrDefault(x => x.DepartmentCallPriorityId == series.Priority);

				model.Rows.Add(new RecurringCallRow
				{
					RecurringCallId = series.RecurringCallId,
					Name = series.Name,
					Summary = Describe(series, _localizer),
					Dates = DescribeDates(series, _localizer),
					NextDispatch = next.HasValue ? next.Value.TimeConverterToString(department) : null,
					PriorityName = priority?.Name,
					PriorityColor = priority?.Color,
					IsActive = series.IsActive,
					HasEnded = series.EndsOn.HasValue && series.EndsOn.Value.Date < today,
					CanManage = CanManage(series),
					DispatcherMode = series.IsDispatcherMode,
					Facility = DescribeFacility(series),
					PauseReason = series.IsActive ? null : series.PauseReason,
					ResumeOn = !series.IsActive && series.ResumeOn.HasValue ? series.ResumeOn.Value.ToString("d", CultureInfo.CurrentUICulture) : null,
					OpenAlerts = alertCounts.TryGetValue(series.RecurringCallId, out var count) ? count : 0
				});
			}

			return View(model);
		}

		/// <summary>The form for a new schedule; with <paramref name="fromCallId"/>, pre-filled from that call (its details and who it dispatched).</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Call_Create)]
		public async Task<IActionResult> New(int? fromCallId)
		{
			if (!await CanCreateAsync())
				return Unauthorized();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var today = RecurringCallRecurrence.ToLocal(DateTime.UtcNow, department).Date;

			var model = new RecurringCallEditView
			{
				StartsOn = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
				Days = new List<int> { (int)today.DayOfWeek },
				MonthlyDay = today.Day,
				Times = new List<string> { "08:00" },
				CheckInTimersEnabled = await _departmentSettingsService.GetCheckInTimersAutoEnableForNewCallsAsync(DepartmentId)
			};

			if (fromCallId.HasValue && fromCallId.Value > 0 && await _authorizationService.CanUserViewCallAsync(UserId, fromCallId.Value))
			{
				var call = await _callsService.GetCallByIdAsync(fromCallId.Value);
				if (call != null && call.DepartmentId == DepartmentId && !call.IsDeleted)
				{
					call = await _callsService.PopulateCallData(call, true, false, false, true, true, true, false, false, false) ?? call;
					CopyFromCall(model, call, department);
				}
			}
			else
			{
				var priorities = await _callsService.GetActiveCallPrioritiesForDepartmentAsync(DepartmentId);
				model.Priority = priorities?.FirstOrDefault(x => x.IsDefault)?.DepartmentCallPriorityId ?? priorities?.FirstOrDefault()?.DepartmentCallPriorityId ?? 0;
			}

			await FillEditViewAsync(model, department);
			return View("Edit", model);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<IActionResult> Edit(int id)
		{
			var series = await _recurringCallsService.GetByIdAsync(DepartmentId, id);
			if (series == null)
				return NotFound();

			if (!CanManage(series) || !await CanCreateAsync())
				return Unauthorized();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var model = ToEditView(series);
			model.UpcomingCallCount = await ReplaceableCallCountAsync(id);

			await FillEditViewAsync(model, department, series);
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Call_Create)]
		public async Task<IActionResult> Save(RecurringCallEditView model, CancellationToken cancellationToken)
		{
			if (!await CanCreateAsync())
				return Unauthorized();

			model ??= new RecurringCallEditView();
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var policy = await _departmentSettingsService.GetNewCallFieldPolicyAsync(DepartmentId) ?? new NewCallFieldPolicy();
			model.FieldPolicy = policy;

			RecurringCall series;
			if (model.RecurringCallId > 0)
			{
				series = await _recurringCallsService.GetByIdAsync(DepartmentId, model.RecurringCallId);
				if (series == null)
					return NotFound();

				if (!CanManage(series))
					return Unauthorized();
			}
			else
			{
				series = new RecurringCall { DepartmentId = DepartmentId };
			}

			var previousAddress = series.Address;
			var previousGeoLocation = series.GeoLocationData;
			var previousUnitIds = series.GetDispatchUnitIds();
			var wasActive = series.RecurringCallId > 0 ? series.IsActive : (bool?)null;
			var isNew = series.RecurringCallId == 0;

			ApplySchedule(model, series);
			ApplyCallDetails(model, series, policy);
			ApplyScheduleDetails(model, series);

			// The unit picker offers only the units the member may see: stored units outside it are kept as they are, and only offered
			// units can be added (the same rule as the picker, so a crafted post cannot add others).
			series.DispatchUnitIds = RecurringCall.JoinIds(await MergeDispatchUnitsAsync(previousUnitIds, model.DispatchUnitIds));

			// Check-in timers hidden by the department: a new schedule takes the department's default (as a new call does), whatever
			// was posted; an edit keeps what it had.
			if (isNew && !model.IsVisible(NewCallFieldKeys.CheckInTimers))
				series.CheckInTimersEnabled = await _departmentSettingsService.GetCheckInTimersAutoEnableForNewCallsAsync(DepartmentId);

			// Pausing and resuming an existing schedule are their own actions (with a reason); the form only sets it for a new one.
			if (wasActive.HasValue)
				series.IsActive = wasActive.Value;
			else if (!series.IsActive)
			{
				series.PausedOn = DateTime.UtcNow;
				series.PausedByUserId = UserId;
			}

			// Coordinates: "lat,lon". Left as they were while the address changed, they belong to the old address: locate the new one.
			if (model.IsVisible(NewCallFieldKeys.Geolocation))
			{
				if (!string.IsNullOrWhiteSpace(model.Coordinates))
				{
					var point = ParseCoordinates(model.Coordinates);
					if (point == null)
						ModelState.AddModelError(nameof(model.Coordinates), _localizer["ErrorCoordinatesInvalid"].Value);
					else if (point == previousGeoLocation && !string.Equals((previousAddress ?? "").Trim(), (series.Address ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
						series.GeoLocationData = null;
					else
						series.GeoLocationData = point;
				}
				else
				{
					series.GeoLocationData = null;
				}
			}
			else if (!string.Equals((previousAddress ?? "").Trim(), (series.Address ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
			{
				// The pin is hidden but the address changed: the old pin belongs to the old address, so the new one is located instead.
				series.GeoLocationData = null;
			}

			if (string.IsNullOrWhiteSpace(model.StartsOn) || ParseDate(model.StartsOn) == null)
				ModelState.AddModelError(nameof(model.StartsOn), _localizer["ErrorStartsOnRequired"].Value);

			if (!string.IsNullOrWhiteSpace(model.EndsOn) && ParseDate(model.EndsOn) == null)
				ModelState.AddModelError(nameof(model.EndsOn), _localizer["ErrorEndsOnInvalid"].Value);

			if (model.Times != null && model.Times.Any(x => !string.IsNullOrWhiteSpace(x) && !RecurringCallRecurrence.TryParseTime(x, out _)))
				ModelState.AddModelError(nameof(model.Times), _localizer["ErrorTimeInvalid"].Value);

			foreach (var error in RecurringCallRecurrence.Validate(series))
				ModelState.AddModelError(string.Empty, string.Format(_localizer[error].Value, RecurringCallRecurrence.MaxTimesPerDay, RecurringCallRecurrence.MaxInterval));

			if (series.DestinationPoiId.HasValue && await _mappingService.GetDestinationPOIByIdAsync(DepartmentId, series.DestinationPoiId.Value) == null)
				ModelState.AddModelError(nameof(model.DestinationPoiId), _callLocalizer["InvalidDestinationPoi"].Value);

			if (series.FacilityPoiId.HasValue && !(await _mappingService.GetPOIsForDepartmentAsync(DepartmentId) ?? new List<Poi>()).Any(x => x.PoiId == series.FacilityPoiId.Value))
				ModelState.AddModelError(nameof(model.FacilityPoiId), _localizer["ErrorFacilityPoiInvalid"].Value);
			else
				// What the save itself does: a chosen pickup facility fills a blank address, so the fields below are checked as saved.
				await _recurringCallsService.ApplyFacilityDefaultsAsync(series);

			// The department's New Call Form Fields policy, as on the New Call page: what crews need on a call must be on every call
			// this creates. Fields this form cannot collect are not enforced.
			foreach (var violation in NewCallFieldPolicyValidator.Validate(policy, FieldValues(series)))
			{
				ModelState.AddModelError($"NewCallField_{violation.Key}",
					string.Format(_callLocalizer["NewCallFieldRequired"].Value, _departmentLocalizer[$"NewCallField_{violation.Key}"].Value));
			}

			if (!ModelState.IsValid)
			{
				if (series.RecurringCallId > 0)
					model.UpcomingCallCount = await ReplaceableCallCountAsync(series.RecurringCallId);

				await FillEditViewAsync(model, department, series.RecurringCallId > 0 ? series : null);
				return View("Edit", model);
			}

			var result = await _recurringCallsService.SaveAsync(series, UserId, series.RecurringCallId > 0 && model.ReplaceUpcomingCalls, Origin(), cancellationToken);

			var message = _localizer["SavedMessage"].Value;
			if (result.ReplacedCalls > 0)
				message += " " + string.Format(_localizer["SavedReplacedMessage"].Value, result.ReplacedCalls);
			if (result.CreatedCalls > 0)
				message += " " + string.Format(_localizer["SavedCreatedMessage"].Value, result.CreatedCalls);

			TempData["RecurringCallsMessage"] = message;
			return RedirectToAction("Index");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Call_Create)]
		public async Task<IActionResult> Pause(int id, string reason, string resumeOn, bool deleteUpcomingCalls, CancellationToken cancellationToken)
		{
			var series = await _recurringCallsService.GetByIdAsync(DepartmentId, id);
			if (series == null)
				return NotFound();

			if (!CanManage(series) || !await CanCreateAsync())
				return Unauthorized();

			var resumeDate = ParseDate(resumeOn);
			if (!string.IsNullOrWhiteSpace(resumeOn) && resumeDate == null)
			{
				TempData["RecurringCallsError"] = _localizer["ErrorResumeOnInvalid"].Value;
				return RedirectToAction("Index");
			}

			// Null when the schedule was deleted meanwhile: nothing was paused.
			if (await _recurringCallsService.PauseAsync(DepartmentId, id, UserId, reason, resumeDate, deleteUpcomingCalls, Origin(), cancellationToken) == null)
				TempData["RecurringCallsError"] = _localizer["ActionFailed"].Value;
			else
				TempData["RecurringCallsMessage"] = _localizer["PausedMessage"].Value;

			return RedirectToAction("Index");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Call_Create)]
		public async Task<IActionResult> Resume(int id, CancellationToken cancellationToken)
		{
			var series = await _recurringCallsService.GetByIdAsync(DepartmentId, id);
			if (series == null)
				return NotFound();

			if (!CanManage(series) || !await CanCreateAsync())
				return Unauthorized();

			if (await _recurringCallsService.ResumeAsync(DepartmentId, id, UserId, Origin(), cancellationToken) == null)
				TempData["RecurringCallsError"] = _localizer["ActionFailed"].Value;
			else
				TempData["RecurringCallsMessage"] = _localizer["ResumedMessage"].Value;

			return RedirectToAction("Index");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Call_Create)]
		public async Task<IActionResult> Delete(int id, bool deleteUpcomingCalls, CancellationToken cancellationToken)
		{
			var series = await _recurringCallsService.GetByIdAsync(DepartmentId, id);
			if (series == null)
				return NotFound();

			if (!CanManage(series) || !await CanCreateAsync())
				return Unauthorized();

			if (!await _recurringCallsService.DeleteAsync(DepartmentId, id, UserId, deleteUpcomingCalls, Origin(), cancellationToken))
				TempData["RecurringCallsError"] = _localizer["ActionFailed"].Value;
			else
				TempData["RecurringCallsMessage"] = _localizer["DeletedMessage"].Value;

			return RedirectToAction("Index");
		}

		/// <summary>
		/// The calendar of scheduled calls (FullCalendar event feed): waiting scheduled calls, calls the schedules created, and the
		/// occurrences still to come or skipped. Times are the department's local wall-clock times, like the department calendar.
		/// </summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<IActionResult> GetCalendarEvents(string start, string end)
		{
			if (!DateTimeOffset.TryParse(start, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var from) ||
				!DateTimeOffset.TryParse(end, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var until) || until <= from ||
				!RecurringCallRecurrence.IsSupportedInstant(from.UtcDateTime) || !RecurringCallRecurrence.IsSupportedInstant(until.UtcDateTime))
				return BadRequest();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			// The browser asks in its own zone but the events are placed in the department's: widen by a day either side.
			var fromUtc = from.UtcDateTime.AddDays(-1);
			var toUtc = until.UtcDateTime.AddDays(1);
			if ((toUtc - fromUtc).TotalDays > RecurringCallRecurrence.MaxRangeDays)
				toUtc = fromUtc.AddDays(RecurringCallRecurrence.MaxRangeDays);

			var entries = await _recurringCallsService.GetCalendarAsync(DepartmentId, UserId, fromUtc, toUtc);
			var schedules = (await _recurringCallsService.GetAllVisibleToUserAsync(DepartmentId, UserId)).ToDictionary(x => x.RecurringCallId);
			var canCreate = await CanCreateAsync();
			var colors = new Dictionary<int, string>();
			var baseUrl = GetBaseUrl();
			var events = new List<object>();

			foreach (var entry in entries)
			{
				if (!colors.TryGetValue(entry.Priority, out var color))
				{
					color = await _callsService.CallPriorityToColorAsync(entry.Priority, DepartmentId);
					colors[entry.Priority] = color;
				}

				schedules.TryGetValue(entry.RecurringCallId ?? 0, out var series);
				var occurrence = new DateTimeOffset(DateTime.SpecifyKind(entry.OccurrenceOnUtc ?? entry.DispatchOnUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
				var local = RecurringCallRecurrence.ToLocal(entry.DispatchOnUtc, department);
				var name = ProtectedDataEnvelope.SafeDisplay(entry.Name);
				var isCall = entry.CallId.HasValue;
				var kind = entry.Kind.ToString().ToLowerInvariant();

				string title;
				if (isCall)
					title = string.IsNullOrWhiteSpace(entry.CallNumber) ? name : $"{entry.CallNumber} {name}";
				else if (entry.Kind == ScheduledCallCalendarEntryKinds.Skipped)
					title = string.Format(_localizer["SkippedTitle"].Value, name);
				else if (entry.Kind == ScheduledCallCalendarEntryKinds.Due)
					title = string.Format(_localizer["DueTitle"].Value, name);
				else if (entry.Kind == ScheduledCallCalendarEntryKinds.Missed)
					title = string.Format(_localizer["MissedTitle"].Value, name);
				else
					title = name;

				if (entry.Kind == ScheduledCallCalendarEntryKinds.Held)
					title = string.Format(_localizer["HeldTitle"].Value, title);

				var background = entry.Kind switch
				{
					ScheduledCallCalendarEntryKinds.Cancelled => "#8a8a8a",
					ScheduledCallCalendarEntryKinds.Skipped => "#b8b8b8",
					ScheduledCallCalendarEntryKinds.Due => "#f8ac59",
					ScheduledCallCalendarEntryKinds.Held => "#ed5565",
					ScheduledCallCalendarEntryKinds.Missed => "#ed5565",
					_ => string.IsNullOrWhiteSpace(color) ? "#1e90ff" : color
				};

				events.Add(new
				{
					id = isCall ? $"call-{entry.CallId}" : $"occurrence-{entry.RecurringCallId}-{occurrence}",
					title,
					start = local.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
					allDay = false,
					backgroundColor = background,
					borderColor = background,
					classNames = new[] { "rg-scheduled-" + kind },
					// Calls open the call; occurrences open the scheduled calls calendar, which offers skip / create now / edit.
					url = isCall
						? $"{baseUrl}/User/Dispatch/ViewCall?callId={entry.CallId}"
						: $"{baseUrl}/User/Dispatch/ScheduledCalls?view=calendar",
					kind,
					callId = entry.CallId,
					recurringCallId = entry.RecurringCallId,
					occurrence,
					address = ProtectedDataEnvelope.SafeDisplay(entry.Address),
					when = local.ToString("f", CultureInfo.CurrentCulture),
					scheduleName = series != null ? ProtectedDataEnvelope.SafeDisplay(series.Name) : null,
					canManage = series != null && canCreate && CanManage(series),
					// Sending, skipping or creating one occurrence is a dispatcher's call, not only the schedule owner's.
					canAct = series != null && canCreate,
					editUrl = series != null ? $"{baseUrl}/User/RecurringCalls/Edit?id={series.RecurringCallId}" : null
				});
			}

			Response.Headers["Cache-Control"] = "no-store";
			return Json(events);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Call_Create)]
		public async Task<IActionResult> SkipOccurrence(int id, long occurrence, CancellationToken cancellationToken)
		{
			var series = await GetActionableAsync(id);
			if (series == null || !TryFromUnix(occurrence, out var occurrenceUtc))
				return Json(new { success = false });

			var done = await _recurringCallsService.SkipOccurrenceAsync(DepartmentId, id, occurrenceUtc, UserId, Origin(), cancellationToken);
			return Json(new { success = done });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Call_Create)]
		public async Task<IActionResult> RestoreOccurrence(int id, long occurrence, CancellationToken cancellationToken)
		{
			var series = await GetActionableAsync(id);
			if (series == null || !TryFromUnix(occurrence, out var occurrenceUtc))
				return Json(new { success = false });

			var done = await _recurringCallsService.RestoreOccurrenceAsync(DepartmentId, id, occurrenceUtc, UserId, Origin(), cancellationToken);
			return Json(new { success = done });
		}

		/// <summary>
		/// Creates an occurrence's call now without sending it, so it can be changed on its own; answers with the call's edit page (a
		/// future occurrence is a scheduled call) or the call itself (one already due waits as pending).
		/// </summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Call_Create)]
		public async Task<IActionResult> CreateOccurrenceNow(int id, long occurrence, CancellationToken cancellationToken)
		{
			var series = await GetActionableAsync(id);
			if (series == null || !TryFromUnix(occurrence, out var occurrenceUtc))
				return Json(new { success = false });

			var call = await _recurringCallsService.CreateOccurrenceCallNowAsync(DepartmentId, id, occurrenceUtc, UserId, Origin(), cancellationToken);
			if (call == null)
				return Json(new { success = false });

			var page = call.State == (int)CallStates.Pending ? "ViewCall" : "UpdateCall";
			return Json(new { success = true, callId = call.CallId, url = $"{GetBaseUrl()}/User/Dispatch/{page}?callId={call.CallId}" });
		}

		/// <summary>
		/// Sends an occurrence now (creating its call if it has none), with the schedule's run card and availability rules. Answers
		/// with the call: sent, or held as pending when nobody could be sent. Outcome QueueFailed (not a success): nothing went out
		/// and the call is waiting again (a scheduled one is retried by the worker, a held one stays pending).
		/// </summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Call_Create)]
		public async Task<IActionResult> DispatchOccurrenceNow(int id, long occurrence, CancellationToken cancellationToken)
		{
			var series = await GetActionableAsync(id);
			if (series == null || !TryFromUnix(occurrence, out var occurrenceUtc))
				return Json(new { success = false });

			var result = await _recurringCallsService.DispatchOccurrenceNowAsync(DepartmentId, id, occurrenceUtc, UserId, Origin(), cancellationToken);
			var success = result.Outcome == RecurringCallDispatchOutcomes.Dispatched || result.Outcome == RecurringCallDispatchOutcomes.Held;

			return Json(new
			{
				success,
				outcome = result.Outcome.ToString(),
				callId = result.Call?.CallId,
				url = result.Call != null ? $"{GetBaseUrl()}/User/Dispatch/ViewCall?callId={result.Call.CallId}" : null
			});
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Call_Create)]
		public async Task<IActionResult> AcknowledgeAlert(string alertId, CancellationToken cancellationToken)
		{
			if (!await CanCreateAsync() || !await CanSeeAlertAsync(alertId))
				return Json(new { success = false });

			return Json(new { success = await _recurringCallsService.AcknowledgeAlertAsync(DepartmentId, alertId, UserId, Origin(), cancellationToken) });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Call_Create)]
		public async Task<IActionResult> DismissAlert(string alertId, string note, CancellationToken cancellationToken)
		{
			if (!await CanCreateAsync() || !await CanSeeAlertAsync(alertId))
				return Json(new { success = false });

			return Json(new { success = await _recurringCallsService.DismissAlertAsync(DepartmentId, alertId, UserId, note, Origin(), cancellationToken) });
		}

		/// <summary>How many recurring call alerts are open for the member (the Scheduled Calls banner).</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<IActionResult> GetAlertCount(CancellationToken cancellationToken)
		{
			var alerts = await _recurringCallsService.GetAlertsAsync(DepartmentId, UserId, null, cancellationToken);

			Response.Headers["Cache-Control"] = "no-store";
			return Json(new { count = alerts.Count });
		}

		#region Helpers

		/// <summary>The creator of a schedule or a department admin may change it.</summary>
		private bool CanManage(RecurringCall series)
		{
			return series != null && (ClaimsAuthorizationHelper.IsUserDepartmentAdmin() ||
									  string.Equals(series.CreatedByUserId, UserId, StringComparison.OrdinalIgnoreCase));
		}

		private async Task<bool> CanCreateAsync()
		{
			return ClaimsAuthorizationHelper.CanCreateCall() && await _authorizationService.CanUserCreateCallAsync(UserId, DepartmentId);
		}

		/// <summary>A schedule whose occurrences the member may send, skip or create (anyone who can create calls).</summary>
		private async Task<RecurringCall> GetActionableAsync(int id)
		{
			var series = await _recurringCallsService.GetByIdAsync(DepartmentId, id);

			if (series == null || !await CanCreateAsync())
				return null;

			return series;
		}

		/// <summary>The alert is one the member sees (their dispatch scope).</summary>
		private async Task<bool> CanSeeAlertAsync(string alertId)
		{
			if (string.IsNullOrWhiteSpace(alertId))
				return false;

			var alerts = await _recurringCallsService.GetAlertsAsync(DepartmentId, UserId);
			return alerts.Any(x => string.Equals(x.Alert.RecurringCallAlertId, alertId, StringComparison.OrdinalIgnoreCase));
		}

		private AuditOrigin Origin()
		{
			return new AuditOrigin
			{
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}"
			};
		}

		private static RecurringCallAlertRow ToAlertRow(RecurringCallAlertInfo info, Department department, bool canCreate)
		{
			var occurrence = DateTime.SpecifyKind(info.Alert.OccurrenceOn, DateTimeKind.Utc);
			var unavailable = info.UnavailableUnits.Select(x => x.Value).Concat(info.UnavailablePeople.Select(x => x.Value)).ToList();

			return new RecurringCallAlertRow
			{
				AlertId = info.Alert.RecurringCallAlertId,
				RecurringCallId = info.Alert.RecurringCallId,
				ScheduleName = ProtectedDataEnvelope.SafeDisplay(info.ScheduleName),
				Kind = (RecurringCallAlertKinds)info.Alert.Kind,
				Occurrence = new DateTimeOffset(occurrence).ToUnixTimeSeconds(),
				When = RecurringCallRecurrence.ToLocal(occurrence, department).ToString("ddd, d MMM yyyy HH:mm", CultureInfo.CurrentUICulture),
				CallId = info.Alert.CallId,
				CallNumber = info.CallNumber,
				Reason = info.Reason,
				Unavailable = unavailable.Any() ? string.Join(", ", unavailable) : null,
				AcknowledgedBy = info.Alert.AcknowledgedOn.HasValue ? (info.AcknowledgedByName ?? "-") : null,
				// Acknowledge and dismiss are open to any call creator who sees the alert (as the actions check), the schedule deleted
				// or not; sending, creating or skipping only while the schedule still has that occurrence.
				CanAct = canCreate,
				CanSend = info.CanSend && canCreate
			};
		}

		/// <summary>Calls the edit's "replace" (or delete's "delete") would remove: scheduled and not sent. Held calls stay for the dispatcher.</summary>
		private async Task<int> ReplaceableCallCountAsync(int recurringCallId)
		{
			return (await _recurringCallsService.GetUpcomingCallsAsync(DepartmentId, recurringCallId)).Count(x => x.State != (int)CallStates.Pending);
		}

		/// <summary>
		/// The schedule's units after a save: those the member may see (the picker's rule) as ticked, and every stored unit outside
		/// that set kept as it was.
		/// </summary>
		private async Task<List<int>> MergeDispatchUnitsAsync(List<int> storedUnitIds, List<int> postedUnitIds)
		{
			var editable = new HashSet<int>();
			foreach (var unit in await _unitsService.GetUnitsForDepartmentAsync(DepartmentId) ?? new List<Unit>())
			{
				if (unit != null && !unit.IsDeleted && await _authorizationService.CanUserViewUnitViaMatrixAsync(unit.UnitId, UserId, DepartmentId))
					editable.Add(unit.UnitId);
			}

			return (storedUnitIds ?? new List<int>()).Where(x => !editable.Contains(x))
				.Concat((postedUnitIds ?? new List<int>()).Where(editable.Contains))
				.Distinct()
				.ToList();
		}

		/// <summary>"Mercy General Hospital → Riverside Dialysis", for the list.</summary>
		public static string DescribeFacility(RecurringCall series)
		{
			var from = string.IsNullOrWhiteSpace(series.FacilityName) ? null : ProtectedDataEnvelope.SafeDisplay(series.FacilityName);
			var to = string.IsNullOrWhiteSpace(series.DestinationName) ? null : ProtectedDataEnvelope.SafeDisplay(series.DestinationName);

			if (from != null && to != null)
				return $"{from} \u2192 {to}";

			return from ?? to;
		}

		/// <summary>An occurrence's Unix seconds as UTC; false for a value outside what the occurrence code can work with (it would overflow).</summary>
		public static bool TryFromUnix(long seconds, out DateTime utc)
		{
			utc = default;

			if (seconds < new DateTimeOffset(RecurringCallRecurrence.MinSupportedUtc).ToUnixTimeSeconds() ||
				seconds > new DateTimeOffset(RecurringCallRecurrence.MaxSupportedUtc).ToUnixTimeSeconds())
				return false;

			utc = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
			return true;
		}

		public static DateTime? ParseDate(string value)
		{
			if (DateTime.TryParseExact((value ?? "").Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
				return DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);

			return null;
		}

		/// <summary>"lat,lon" in range, as stored on calls; null when it is not one.</summary>
		public static string ParseCoordinates(string value)
		{
			var parts = (value ?? "").Split(',');
			if (parts.Length != 2 ||
				!double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) ||
				!double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lon) ||
				lat < -90 || lat > 90 || lon < -180 || lon > 180 || double.IsNaN(lat) || double.IsNaN(lon))
				return null;

			return $"{lat.ToString(CultureInfo.InvariantCulture)},{lon.ToString(CultureInfo.InvariantCulture)}";
		}

		/// <summary>The schedule part of the form.</summary>
		public static void ApplySchedule(RecurringCallEditView model, RecurringCall series)
		{
			series.Frequency = model.Frequency;
			series.RepeatInterval = model.RepeatInterval;
			series.DaysOfWeek = (model.Days ?? new List<int>()).Where(x => x >= 0 && x <= 6).Aggregate(0, (bits, day) => bits | RecurringCallRecurrence.DayBit((DayOfWeek)day));

			if (model.MonthlyMode == 1)
			{
				series.MonthlyDay = 0;
				series.MonthlyWeek = model.MonthlyWeek;
				series.MonthlyWeekday = model.MonthlyWeekday;
			}
			else
			{
				series.MonthlyDay = model.MonthlyDay;
				series.MonthlyWeek = 0;
				series.MonthlyWeekday = 0;
			}

			series.Times = RecurringCallRecurrence.FormatTimes((model.Times ?? new List<string>())
				.Select(x => RecurringCallRecurrence.TryParseTime(x, out var time) ? (TimeSpan?)time : null)
				.Where(x => x.HasValue).Select(x => x.Value));
			series.StartsOn = ParseDate(model.StartsOn) ?? series.StartsOn;
			series.EndsOn = ParseDate(model.EndsOn);
			series.LeadTimeMinutes = model.LeadTimeMinutes;
			series.IsActive = model.IsActive;
			series.CreationMode = model.CreationMode;
			series.ManualAlertMinutes = model.ManualAlertMinutes;
			series.UseRunCard = model.UseRunCard;

			// Dispatcher mode creates nothing ahead of time; a lead time still in range is kept for switching back.
			if (series.IsDispatcherMode && (series.LeadTimeMinutes < RecurringCallRecurrence.MinLeadTimeMinutes || series.LeadTimeMinutes > RecurringCallRecurrence.MaxLeadTimeMinutes))
				series.LeadTimeMinutes = RecurringCallRecurrence.DefaultLeadTimeMinutes;
		}

		/// <summary>About the schedule itself: description, its contact, the pickup facility and destination, and who to alert.</summary>
		public static void ApplyScheduleDetails(RecurringCallEditView model, RecurringCall series)
		{
			string Clean(string value, int max)
			{
				if (string.IsNullOrWhiteSpace(value))
					return null;

				var text = value.Trim();
				return text.Length > max ? text.Substring(0, max) : text;
			}

			series.Description = Clean(model.Description, 4000);
			series.ScheduleContactName = Clean(model.ScheduleContactName, RecurringCall.MaxShortFieldLength);
			series.ScheduleContactNumber = Clean(model.ScheduleContactNumber, 100);
			series.ScheduleContactEmail = Clean(model.ScheduleContactEmail, 256);
			series.FacilityName = Clean(model.FacilityName, RecurringCall.MaxShortFieldLength);
			series.FacilityDetails = Clean(model.FacilityDetails, 2000);
			series.FacilityPoiId = model.FacilityPoiId.HasValue && model.FacilityPoiId.Value > 0 ? model.FacilityPoiId : null;
			series.DestinationName = Clean(model.DestinationName, RecurringCall.MaxShortFieldLength);
			series.DestinationDetails = Clean(model.DestinationDetails, 2000);
			series.AlertUserIds = RecurringCall.JoinIds(model.AlertUserIds);
			series.AlertGroupIds = RecurringCall.JoinIds(model.AlertGroupIds);
			series.AlertRoleIds = RecurringCall.JoinIds(model.AlertRoleIds);
		}

		/// <summary>
		/// The call part of the form. A field the department hides is not on the form: a new schedule leaves it empty and an edit
		/// keeps what it had, as the call forms do.
		/// </summary>
		public static void ApplyCallDetails(RecurringCallEditView model, RecurringCall series, NewCallFieldPolicy policy)
		{
			var isNew = series.RecurringCallId == 0;
			bool Shows(string key) => policy == null || policy.IsVisible(key);
			string Clean(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

			series.Name = Clean(model.Name);
			if (series.Name != null && series.Name.Length > RecurringCall.MaxNameLength)
				series.Name = series.Name.Substring(0, RecurringCall.MaxNameLength);
			series.Priority = model.Priority;
			series.Type = Clean(model.Type);
			series.NatureOfCall = Clean(model.NatureOfCall);

			if (Shows(NewCallFieldKeys.Note) || isNew) series.Notes = Shows(NewCallFieldKeys.Note) ? Clean(model.Notes) : null;
			if (Shows(NewCallFieldKeys.Address) || isNew) series.Address = Shows(NewCallFieldKeys.Address) ? Clean(model.Address) : null;
			if (Shows(NewCallFieldKeys.ContactName) || isNew) series.ContactName = Shows(NewCallFieldKeys.ContactName) ? Clean(model.ContactName) : null;
			if (Shows(NewCallFieldKeys.ContactInfo) || isNew) series.ContactNumber = Shows(NewCallFieldKeys.ContactInfo) ? Clean(model.ContactNumber) : null;
			if (Shows(NewCallFieldKeys.ExternalId) || isNew) series.ExternalIdentifier = Shows(NewCallFieldKeys.ExternalId) ? Clean(model.ExternalIdentifier) : null;
			if (Shows(NewCallFieldKeys.ReferenceId) || isNew) series.ReferenceNumber = Shows(NewCallFieldKeys.ReferenceId) ? Clean(model.ReferenceNumber) : null;
			if (Shows(NewCallFieldKeys.IncidentId) || isNew) series.IncidentNumber = Shows(NewCallFieldKeys.IncidentId) ? Clean(model.IncidentNumber) : null;
			if (Shows(NewCallFieldKeys.MapPage) || isNew) series.MapPage = Shows(NewCallFieldKeys.MapPage) ? Clean(model.MapPage) : null;
			if (Shows(NewCallFieldKeys.DestinationPoi) || isNew)
				series.DestinationPoiId = Shows(NewCallFieldKeys.DestinationPoi) && model.DestinationPoiId.HasValue && model.DestinationPoiId.Value > 0 ? model.DestinationPoiId : null;
			if (Shows(NewCallFieldKeys.CheckInTimers))
				series.CheckInTimersEnabled = model.CheckInTimersEnabled;
			else if (isNew)
				series.CheckInTimersEnabled = model.CheckInTimersEnabled;

			// Recipients are always on this form: a schedule exists to dispatch someone, so it offers every list even where the
			// New Call page hides some.
			series.DispatchUserIds = RecurringCall.JoinIds(model.DispatchUserIds);
			series.DispatchGroupIds = RecurringCall.JoinIds(model.DispatchGroupIds);
			series.DispatchUnitIds = RecurringCall.JoinIds(model.DispatchUnitIds);
			series.DispatchRoleIds = RecurringCall.JoinIds(model.DispatchRoleIds);
		}

		/// <summary>What the New Call Form Fields policy checks, for a schedule. Fields this form has no input for are unsupported.</summary>
		public static NewCallFieldValues FieldValues(RecurringCall series) => RecurringCallRecurrence.FieldValues(series);

		private static RecurringCallEditView ToEditView(RecurringCall series)
		{
			var days = new List<int>();
			for (var day = 0; day < 7; day++)
			{
				if ((series.DaysOfWeek & RecurringCallRecurrence.DayBit((DayOfWeek)day)) != 0)
					days.Add(day);
			}

			return new RecurringCallEditView
			{
				RecurringCallId = series.RecurringCallId,
				Frequency = series.Frequency,
				RepeatInterval = series.RepeatInterval,
				Days = days,
				MonthlyMode = series.MonthlyWeek > 0 ? 1 : 0,
				MonthlyDay = series.MonthlyDay > 0 ? series.MonthlyDay : 1,
				MonthlyWeek = series.MonthlyWeek > 0 ? series.MonthlyWeek : 1,
				MonthlyWeekday = series.MonthlyWeek > 0 ? series.MonthlyWeekday : 1,
				Times = RecurringCallRecurrence.ParseTimes(series.Times).Select(x => x.ToString(@"hh\:mm", CultureInfo.InvariantCulture)).ToList(),
				StartsOn = series.StartsOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
				EndsOn = series.EndsOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
				LeadTimeMinutes = series.LeadTimeMinutes,
				IsActive = series.IsActive,
				CreationMode = series.CreationMode,
				ManualAlertMinutes = series.ManualAlertMinutes,
				UseRunCard = series.UseRunCard,
				Description = series.Description,
				ScheduleContactName = series.ScheduleContactName,
				ScheduleContactNumber = series.ScheduleContactNumber,
				ScheduleContactEmail = series.ScheduleContactEmail,
				FacilityName = series.FacilityName,
				FacilityDetails = series.FacilityDetails,
				FacilityPoiId = series.FacilityPoiId,
				DestinationName = series.DestinationName,
				DestinationDetails = series.DestinationDetails,
				AlertUserIds = series.GetAlertUserIds(),
				AlertGroupIds = series.GetAlertGroupIds(),
				AlertRoleIds = series.GetAlertRoleIds(),
				PauseReason = series.IsActive ? null : series.PauseReason,
				Name = series.Name,
				Priority = series.Priority,
				Type = series.Type,
				NatureOfCall = series.NatureOfCall,
				Notes = series.Notes,
				Address = series.Address,
				Coordinates = series.GeoLocationData,
				DestinationPoiId = series.DestinationPoiId,
				ContactName = series.ContactName,
				ContactNumber = series.ContactNumber,
				ExternalIdentifier = series.ExternalIdentifier,
				ReferenceNumber = series.ReferenceNumber,
				IncidentNumber = series.IncidentNumber,
				MapPage = series.MapPage,
				CheckInTimersEnabled = series.CheckInTimersEnabled,
				DispatchUserIds = series.GetDispatchUserIds(),
				DispatchGroupIds = series.GetDispatchGroupIds(),
				DispatchUnitIds = series.GetDispatchUnitIds(),
				DispatchRoleIds = series.GetDispatchRoleIds(),
				ReplaceUpcomingCalls = true
			};
		}

		/// <summary>"Repeat this call": its details and recipients; its dispatch time (if scheduled) becomes the first date and time.</summary>
		private static void CopyFromCall(RecurringCallEditView model, Call call, Department department)
		{
			model.FromCallNumber = call.Number;
			model.Name = ProtectedDataEnvelope.SafeDisplay(call.Name);
			model.Priority = call.Priority;
			model.Type = call.Type;
			model.NatureOfCall = ProtectedDataEnvelope.SafeDisplay(call.NatureOfCall);
			model.Notes = ProtectedDataEnvelope.SafeDisplay(call.Notes);
			model.Address = ProtectedDataEnvelope.SafeDisplay(call.Address);
			model.Coordinates = ParseCoordinates(call.GeoLocationData);
			model.DestinationPoiId = call.DestinationPoiId;
			model.ContactName = ProtectedDataEnvelope.SafeDisplay(call.ContactName);
			model.ContactNumber = ProtectedDataEnvelope.SafeDisplay(call.ContactNumber);
			model.ExternalIdentifier = call.ExternalIdentifier;
			model.ReferenceNumber = call.ReferenceNumber;
			model.IncidentNumber = call.IncidentNumber;
			model.MapPage = call.MapPage;
			model.CheckInTimersEnabled = call.CheckInTimersEnabled;
			model.DispatchUserIds = (call.Dispatches ?? new List<CallDispatch>()).Select(x => x.UserId).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
			model.DispatchGroupIds = (call.GroupDispatches ?? new List<CallDispatchGroup>()).Select(x => x.DepartmentGroupId).Distinct().ToList();
			model.DispatchUnitIds = (call.UnitDispatches ?? new List<CallDispatchUnit>()).Select(x => x.UnitId).Distinct().ToList();
			model.DispatchRoleIds = (call.RoleDispatches ?? new List<CallDispatchRole>()).Select(x => x.RoleId).Distinct().ToList();

			var when = RecurringCallRecurrence.ToLocal(call.DispatchOn ?? call.LoggedOn, department);
			if (call.DispatchOn.HasValue && call.DispatchOn.Value > DateTime.UtcNow)
				model.StartsOn = when.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

			model.Days = new List<int> { (int)when.DayOfWeek };
			model.MonthlyDay = when.Day;
			model.Times = new List<string> { when.ToString("HH:mm", CultureInfo.InvariantCulture) };
		}

		private async Task FillEditViewAsync(RecurringCallEditView model, Department department, RecurringCall series = null)
		{
			model.FieldPolicy ??= await _departmentSettingsService.GetNewCallFieldPolicyAsync(DepartmentId) ?? new NewCallFieldPolicy();
			model.TimeZoneName = string.IsNullOrWhiteSpace(department?.TimeZone) ? "Pacific Standard Time" : department.TimeZone;
			model.CanManage = series == null || CanManage(series);

			if (model.Times == null || !model.Times.Any())
				model.Times = new List<string> { "" };

			var priorities = await _callsService.GetActiveCallPrioritiesForDepartmentAsync(DepartmentId) ?? new List<DepartmentCallPriority>();
			model.Priorities = new SelectList(priorities, "DepartmentCallPriorityId", "Name", model.Priority);

			model.CallTypes = new List<SelectListItem> { new SelectListItem(_callLocalizer["NoType"].Value, "") };
			foreach (var type in (await _callsService.GetCallTypesForDepartmentAsync(DepartmentId) ?? new List<CallType>()).OrderBy(x => x.Type))
				model.CallTypes.Add(new SelectListItem(type.Type, type.Type, string.Equals(type.Type, model.Type, StringComparison.OrdinalIgnoreCase)));

			// A type the schedule names that no longer exists stays selectable, so saving does not silently drop it.
			if (!string.IsNullOrWhiteSpace(model.Type) && !model.CallTypes.Any(x => string.Equals(x.Value, model.Type, StringComparison.OrdinalIgnoreCase)))
				model.CallTypes.Add(new SelectListItem(model.Type, model.Type, true));

			model.DestinationPois = new List<SelectListItem> { new SelectListItem(_callLocalizer["NoDestination"].Value, "") };
			var destinations = await _mappingService.GetDestinationPOIsForDepartmentAsync(DepartmentId) ?? new List<Poi>();
			var groups = new Dictionary<string, SelectListGroup>(StringComparer.OrdinalIgnoreCase);
			foreach (var poi in destinations.OrderBy(x => x.Type?.Name).ThenBy(x => x.Name).ThenBy(x => x.Address))
			{
				var groupName = string.IsNullOrWhiteSpace(poi.Type?.Name) ? _callLocalizer["Pois"].Value : poi.Type.Name;
				if (!groups.TryGetValue(groupName, out var group))
					groups[groupName] = group = new SelectListGroup { Name = groupName };

				model.DestinationPois.Add(new SelectListItem(PoiDisplayHelper.GetSelectionLabel(poi, groupName), poi.PoiId.ToString(CultureInfo.InvariantCulture),
					model.DestinationPoiId == poi.PoiId) { Group = group });
			}

			model.FacilityPois = new List<SelectListItem> { new SelectListItem(_localizer["NoFacilityPoi"].Value, "") };
			var facilityGroups = new Dictionary<string, SelectListGroup>(StringComparer.OrdinalIgnoreCase);
			foreach (var poi in (await _mappingService.GetPOIsForDepartmentAsync(DepartmentId) ?? new List<Poi>()).OrderBy(x => x.Type?.Name).ThenBy(x => x.Name).ThenBy(x => x.Address))
			{
				var groupName = string.IsNullOrWhiteSpace(poi.Type?.Name) ? _callLocalizer["Pois"].Value : poi.Type.Name;
				if (!facilityGroups.TryGetValue(groupName, out var group))
					facilityGroups[groupName] = group = new SelectListGroup { Name = groupName };

				model.FacilityPois.Add(new SelectListItem(PoiDisplayHelper.GetSelectionLabel(poi, groupName), poi.PoiId.ToString(CultureInfo.InvariantCulture),
					model.FacilityPoiId == poi.PoiId) { Group = group });
			}

			model.RunCardsEnabled = await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.DispatchRunCards, DepartmentId);

			if (series != null)
			{
				model.PauseReason ??= series.IsActive ? null : series.PauseReason;
				model.ResumeOn = !series.IsActive && series.ResumeOn.HasValue ? series.ResumeOn.Value.ToString("d", CultureInfo.CurrentUICulture) : null;
			}

			var selectedUsers = new HashSet<string>(model.DispatchUserIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
			var members = await _departmentsService.GetAllMembersForDepartmentAsync(DepartmentId) ?? new List<DepartmentMember>();
			var profiles = await _userProfileService.GetAllProfilesForDepartmentAsync(DepartmentId) ?? new Dictionary<string, UserProfile>();
			model.Personnel = members
				.Where(x => x != null && !x.IsDeleted && !x.IsDisabled.GetValueOrDefault())
				.Select(x => new SelectListItem(profiles.TryGetValue(x.UserId, out var profile) ? profile.FullName.AsFirstNameLastName : x.UserId, x.UserId, selectedUsers.Contains(x.UserId)))
				.OrderBy(x => x.Text).ToList();

			var alertUsers = new HashSet<string>(model.AlertUserIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
			model.AlertPersonnel = model.Personnel.Select(x => new SelectListItem(x.Text, x.Value, alertUsers.Contains(x.Value))).ToList();

			var selectedGroups = new HashSet<int>(model.DispatchGroupIds ?? new List<int>());
			var departmentGroups = await _departmentGroupsService.GetAllGroupsForDepartmentAsync(DepartmentId) ?? new List<DepartmentGroup>();
			model.Groups = departmentGroups
				.Select(x => new SelectListItem(x.Name, x.DepartmentGroupId.ToString(CultureInfo.InvariantCulture), selectedGroups.Contains(x.DepartmentGroupId)))
				.OrderBy(x => x.Text).ToList();

			var alertGroups = new HashSet<int>(model.AlertGroupIds ?? new List<int>());
			model.AlertGroups = departmentGroups
				.Select(x => new SelectListItem(x.Name, x.DepartmentGroupId.ToString(CultureInfo.InvariantCulture), alertGroups.Contains(x.DepartmentGroupId)))
				.OrderBy(x => x.Text).ToList();

			var selectedUnits = new HashSet<int>(model.DispatchUnitIds ?? new List<int>());
			var units = new List<Unit>();
			foreach (var unit in await _unitsService.GetUnitsForDepartmentAsync(DepartmentId) ?? new List<Unit>())
			{
				if (!unit.IsDeleted && await _authorizationService.CanUserViewUnitViaMatrixAsync(unit.UnitId, UserId, DepartmentId))
					units.Add(unit);
			}
			model.Units = units
				.Select(x => new SelectListItem(x.Name, x.UnitId.ToString(CultureInfo.InvariantCulture), selectedUnits.Contains(x.UnitId)))
				.OrderBy(x => x.Text).ToList();

			var selectedRoles = new HashSet<int>(model.DispatchRoleIds ?? new List<int>());
			var departmentRoles = await _personnelRolesService.GetRolesForDepartmentAsync(DepartmentId) ?? new List<PersonnelRole>();
			model.Roles = departmentRoles
				.Select(x => new SelectListItem(x.Name, x.PersonnelRoleId.ToString(CultureInfo.InvariantCulture), selectedRoles.Contains(x.PersonnelRoleId)))
				.OrderBy(x => x.Text).ToList();

			var alertRoles = new HashSet<int>(model.AlertRoleIds ?? new List<int>());
			model.AlertRoles = departmentRoles
				.Select(x => new SelectListItem(x.Name, x.PersonnelRoleId.ToString(CultureInfo.InvariantCulture), alertRoles.Contains(x.PersonnelRoleId)))
				.OrderBy(x => x.Text).ToList();

			model.ManualAlertChoices = ManualAlertChoices
				.Concat(ManualAlertChoices.Contains(model.ManualAlertMinutes) || model.ManualAlertMinutes < 0 ? Enumerable.Empty<int>() : new[] { model.ManualAlertMinutes })
				.OrderBy(x => x)
				.Select(x => new SelectListItem(x == 0 ? _localizer["ManualAlertAtTime"].Value : DescribeLeadTime(x, _localizer),
					x.ToString(CultureInfo.InvariantCulture), x == model.ManualAlertMinutes))
				.ToList();

			model.LeadTimes = LeadTimeChoices
				.Concat(LeadTimeChoices.Contains(model.LeadTimeMinutes) || model.LeadTimeMinutes <= 0 ? Enumerable.Empty<int>() : new[] { model.LeadTimeMinutes })
				.OrderBy(x => x)
				.Select(x => new SelectListItem(DescribeLeadTime(x, _localizer), x.ToString(CultureInfo.InvariantCulture), x == model.LeadTimeMinutes))
				.ToList();

			var culture = CultureInfo.CurrentUICulture;
			model.WeekDays = Enumerable.Range(0, 7)
				.Select(x => new SelectListItem(culture.DateTimeFormat.GetDayName((DayOfWeek)x), x.ToString(CultureInfo.InvariantCulture)))
				.ToList();

			model.NextDispatches = new List<string>();
			if (series != null && series.IsActive && series.RecurringCallId > 0)
			{
				var stored = await _recurringCallsService.GetByIdAsync(DepartmentId, series.RecurringCallId) ?? series;
				foreach (var next in NextDispatches(stored, await UpcomingDispatchesAsync(department), department, 6))
					model.NextDispatches.Add(RecurringCallRecurrence.ToLocal(next.Value, department).ToString("ddd, d MMM yyyy HH:mm", CultureInfo.CurrentUICulture));
			}
		}

		/// <summary>
		/// What will actually go out, per schedule, over the calendar window: its waiting calls and the occurrences still to come.
		/// Skipped occurrences and calls deleted on their own are not dispatches, so they are left out.
		/// </summary>
		private async Task<Dictionary<int, List<DateTime>>> UpcomingDispatchesAsync(Department department)
		{
			var now = DateTime.UtcNow;
			var entries = await _recurringCallsService.GetCalendarAsync(DepartmentId, UserId, now, now.AddDays(RecurringCallRecurrence.MaxRangeDays));

			return entries
				.Where(x => x.RecurringCallId.HasValue && x.DispatchOnUtc >= now &&
							(x.Kind == ScheduledCallCalendarEntryKinds.Scheduled || x.Kind == ScheduledCallCalendarEntryKinds.Projected ||
							 x.Kind == ScheduledCallCalendarEntryKinds.Due))
				.GroupBy(x => x.RecurringCallId.Value)
				.ToDictionary(x => x.Key, x => x.Select(e => e.DispatchOnUtc).OrderBy(e => e).ToList());
		}

		/// <summary>A schedule's next dispatches; past the calendar window (a schedule that runs rarely), its next occurrence by pattern.</summary>
		private static List<DateTime?> NextDispatches(RecurringCall series, Dictionary<int, List<DateTime>> upcoming, Department department, int count)
		{
			if (upcoming.TryGetValue(series.RecurringCallId, out var times) && times.Any())
				return times.Take(count).Select(x => (DateTime?)x).ToList();

			var beyond = RecurringCallRecurrence.NextOccurrenceUtc(series, department, DateTime.UtcNow.AddDays(RecurringCallRecurrence.MaxRangeDays));
			return beyond.HasValue ? new List<DateTime?> { beyond } : new List<DateTime?>();
		}

		public static string DescribeLeadTime(int minutes, IStringLocalizer localizer)
		{
			if (minutes % 1440 == 0)
				return minutes == 1440 ? localizer["LeadTimeDay"].Value : string.Format(localizer["LeadTimeDays"].Value, minutes / 1440);

			if (minutes % 60 == 0)
				return minutes == 60 ? localizer["LeadTimeHour"].Value : string.Format(localizer["LeadTimeHours"].Value, minutes / 60);

			return string.Format(localizer["LeadTimeMinutes"].Value, minutes);
		}

		/// <summary>"Every week on Tuesday, Thursday at 08:00, 13:00, 19:00", in the member's language.</summary>
		public static string Describe(RecurringCall series, IStringLocalizer localizer)
		{
			var culture = CultureInfo.CurrentUICulture;
			var interval = Math.Max(1, series.RepeatInterval);
			string pattern;

			switch ((RecurringCallFrequencies)series.Frequency)
			{
				case RecurringCallFrequencies.Daily:
					pattern = interval == 1 ? localizer["SummaryDaily"].Value : string.Format(localizer["SummaryEveryNDays"].Value, interval);
					break;

				case RecurringCallFrequencies.Weekly:
				{
					var days = string.Join(", ", Enumerable.Range(0, 7)
						.Where(x => (series.DaysOfWeek & RecurringCallRecurrence.DayBit((DayOfWeek)x)) != 0)
						.Select(x => culture.DateTimeFormat.GetDayName((DayOfWeek)x)));
					pattern = interval == 1 ? string.Format(localizer["SummaryWeekly"].Value, days) : string.Format(localizer["SummaryEveryNWeeks"].Value, interval, days);
					break;
				}

				case RecurringCallFrequencies.Monthly:
					if (series.MonthlyWeek > 0)
					{
						var which = localizer[WeekKey(series.MonthlyWeek)].Value;
						var day = culture.DateTimeFormat.GetDayName((DayOfWeek)Math.Clamp(series.MonthlyWeekday, 0, 6));
						pattern = interval == 1
							? string.Format(localizer["SummaryMonthlyWeekday"].Value, which, day)
							: string.Format(localizer["SummaryEveryNMonthsWeekday"].Value, interval, which, day);
					}
					else
					{
						pattern = interval == 1
							? string.Format(localizer["SummaryMonthlyDay"].Value, series.MonthlyDay)
							: string.Format(localizer["SummaryEveryNMonthsDay"].Value, interval, series.MonthlyDay);
					}
					break;

				default:
					pattern = string.Empty;
					break;
			}

			var times = string.Join(", ", RecurringCallRecurrence.ParseTimes(series.Times).Select(x => x.ToString(@"hh\:mm", CultureInfo.InvariantCulture)));
			return string.Format(localizer["SummaryWithTimes"].Value, pattern, times);
		}

		public static string DescribeDates(RecurringCall series, IStringLocalizer localizer)
		{
			var culture = CultureInfo.CurrentUICulture;
			var text = string.Format(localizer["DatesFrom"].Value, series.StartsOn.ToString("d", culture));

			if (series.EndsOn.HasValue)
				text += " · " + string.Format(localizer["DatesUntil"].Value, series.EndsOn.Value.ToString("d", culture));

			return text;
		}

		public static string WeekKey(int week) => week switch
		{
			1 => "WeekFirst",
			2 => "WeekSecond",
			3 => "WeekThird",
			4 => "WeekFourth",
			_ => "WeekLast"
		};

		#endregion Helpers
	}
}
