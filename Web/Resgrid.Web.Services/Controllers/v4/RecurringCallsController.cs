using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.ServicesCore.Helpers;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.RecurringCalls;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Recurring calls (M0319, M0320): calls dispatched on a schedule ("interfacility transfers every Tuesday and Thursday at 08:00,
	/// 13:00 and 19:00"), the scheduled calls calendar, and the alerts dispatchers act on (a call that could not be created or was
	/// missed, nobody available to send, chosen resources unavailable, or a dispatcher-mode occurrence due). Anyone who can see
	/// calls sees the schedules, calendar and alerts (limited to their dispatch scope); creating a schedule, and sending, skipping
	/// or creating an occurrence, needs call creation; editing, pausing, resuming or deleting a schedule is for its creator or a
	/// department admin.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class RecurringCallsController : V4AuthenticatedApiControllerbase
	{
		/// <summary>The lead time choices the clients offer, in minutes.</summary>
		public static readonly int[] LeadTimeChoices = { 60, 120, 240, 480, 720, 1440, 2880, 4320, 10080 };

		/// <summary>Dispatcher mode: how long before an occurrence the dispatcher is alerted, in minutes.</summary>
		public static readonly int[] ManualAlertChoices = { 0, 5, 10, 15, 30, 60, 120, 240 };

		private static readonly Dictionary<string, string> ErrorMessages = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			[RecurringCallRecurrence.Errors.NameRequired] = "A name is required.",
			[RecurringCallRecurrence.Errors.TimesRequired] = "Add at least one time of day.",
			[RecurringCallRecurrence.Errors.TooManyTimes] = $"At most {RecurringCallRecurrence.MaxTimesPerDay} times a day.",
			[RecurringCallRecurrence.Errors.IntervalInvalid] = $"Repeat every 1 to {RecurringCallRecurrence.MaxInterval}.",
			[RecurringCallRecurrence.Errors.FrequencyInvalid] = "Choose daily, weekly or monthly.",
			[RecurringCallRecurrence.Errors.DaysRequired] = "Choose at least one day of the week.",
			[RecurringCallRecurrence.Errors.MonthlyDayInvalid] = "The day of the month must be 1 to 31.",
			[RecurringCallRecurrence.Errors.MonthlyWeekdayInvalid] = "Choose which weekday of the month.",
			[RecurringCallRecurrence.Errors.EndsBeforeStart] = "The end date is before the start date.",
			[RecurringCallRecurrence.Errors.LeadTimeInvalid] = "The lead time must be 1 hour to 14 days.",
			[RecurringCallRecurrence.Errors.CreationModeInvalid] = "CreationMode must be 0 (automatic) or 1 (dispatcher).",
			[RecurringCallRecurrence.Errors.ManualAlertInvalid] = "ManualAlertMinutes must be 0 to 1440.",
			[RecurringCallRecurrence.Errors.ContactEmailInvalid] = "The schedule contact email is not valid.",
			["ErrorStartsOnRequired"] = "StartsOn (yyyy-MM-dd) is required.",
			["ErrorEndsOnInvalid"] = "EndsOn must be yyyy-MM-dd.",
			["ErrorTimeInvalid"] = "Times must be HH:mm.",
			["ErrorCoordinatesInvalid"] = "GeoLocationData must be \"lat,lon\".",
			["ErrorDestinationPoiInvalid"] = "DestinationPoiId is not one of the department's destinations.",
			["ErrorFacilityPoiInvalid"] = "FacilityPoiId is not one of the department's points of interest.",
			["ErrorResumeOnInvalid"] = "ResumeOn must be yyyy-MM-dd."
		};

		private readonly IRecurringCallsService _recurringCallsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly ICallsService _callsService;
		private readonly IAuthorizationService _authorizationService;
		private readonly IFeatureToggleService _featureToggleService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly IMappingService _mappingService;
		private readonly IUserProfileService _userProfileService;

		public RecurringCallsController(IRecurringCallsService recurringCallsService, IDepartmentsService departmentsService, ICallsService callsService,
			IAuthorizationService authorizationService, IFeatureToggleService featureToggleService, IDepartmentSettingsService departmentSettingsService,
			IMappingService mappingService, IUserProfileService userProfileService)
		{
			_recurringCallsService = recurringCallsService;
			_departmentsService = departmentsService;
			_callsService = callsService;
			_authorizationService = authorizationService;
			_featureToggleService = featureToggleService;
			_departmentSettingsService = departmentSettingsService;
			_mappingService = mappingService;
			_userProfileService = userProfileService;
		}

		#region Schedules

		/// <summary>
		/// The department's recurring call schedules in the caller's dispatch scope (paused ones included), with each one's next
		/// dispatch.
		/// </summary>
		[HttpGet("GetRecurringCalls")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<RecurringCallsResult>> GetRecurringCalls(CancellationToken cancellationToken)
		{
			var result = new RecurringCallsResult();
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var upcoming = await UpcomingDispatchesAsync();
			var alerts = await _recurringCallsService.GetAlertsAsync(DepartmentId, UserId, null, cancellationToken);
			var alertCounts = alerts.GroupBy(x => x.Alert.RecurringCallId).ToDictionary(x => x.Key, x => x.Count());
			var profiles = await _userProfileService.GetAllProfilesForDepartmentAsync(DepartmentId) ?? new Dictionary<string, UserProfile>();
			var canCreate = await CanCreateAsync();

			foreach (var series in await _recurringCallsService.GetAllVisibleToUserAsync(DepartmentId, UserId))
				result.Data.Add(ToData(series, department, upcoming, alertCounts, profiles, canCreate));

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// One schedule in the caller's dispatch scope, its next dispatches and the calls it created that have not gone out (those
		/// in the caller's scope; a call can be changed on its own). 404 for a schedule outside the scope.
		/// </summary>
		[HttpGet("GetRecurringCall")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<RecurringCallResult>> GetRecurringCall(int recurringCallId, CancellationToken cancellationToken)
		{
			var series = await _recurringCallsService.GetByIdAsync(DepartmentId, recurringCallId);
			if (series == null || !await _recurringCallsService.IsVisibleToUserAsync(series, UserId))
				return NotFound();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var upcoming = await UpcomingDispatchesAsync();
			var alerts = await _recurringCallsService.GetAlertsAsync(DepartmentId, UserId, null, cancellationToken);
			var alertCounts = alerts.GroupBy(x => x.Alert.RecurringCallId).ToDictionary(x => x.Key, x => x.Count());
			var profiles = await _userProfileService.GetAllProfilesForDepartmentAsync(DepartmentId) ?? new Dictionary<string, UserProfile>();

			var detail = new RecurringCallDetailResultData
			{
				RecurringCall = ToData(series, department, upcoming, alertCounts, profiles, await CanCreateAsync())
			};

			if (series.IsActive && upcoming.TryGetValue(series.RecurringCallId, out var times))
				detail.NextDispatches = times.Take(6).Select(Iso).ToList();

			foreach (var call in await _recurringCallsService.GetUpcomingCallsAsync(DepartmentId, series.RecurringCallId, UserId))
			{
				detail.UpcomingCalls.Add(new RecurringCallUpcomingCallData
				{
					CallId = call.CallId,
					Number = call.Number,
					Name = ProtectedDataEnvelope.SafeDisplay(call.Name),
					DispatchOn = call.DispatchOn.HasValue ? Iso(call.DispatchOn.Value) : null,
					IsHeld = call.State == (int)CallStates.Pending
				});
			}

			var result = new RecurringCallResult { Data = detail, PageSize = 1, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>What a recurring call form needs: permissions, run cards, time zone, the lead time and alert choices and the limits.</summary>
		[HttpGet("GetRecurringCallOptions")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<RecurringCallOptionsResult>> GetRecurringCallOptions()
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			var result = new RecurringCallOptionsResult
			{
				Data = new RecurringCallOptionsResultData
				{
					CanCreate = await CanCreateAsync(),
					IsDepartmentAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin(),
					RunCardsEnabled = await _featureToggleService.IsEnabledAsync(FeatureFlagKeys.DispatchRunCards, DepartmentId),
					TimeZone = string.IsNullOrWhiteSpace(department?.TimeZone) ? "Pacific Standard Time" : department.TimeZone,
					Today = RecurringCallRecurrence.ToLocal(DateTime.UtcNow, department).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
					DefaultLeadTimeMinutes = RecurringCallRecurrence.DefaultLeadTimeMinutes,
					MinLeadTimeMinutes = RecurringCallRecurrence.MinLeadTimeMinutes,
					MaxLeadTimeMinutes = RecurringCallRecurrence.MaxLeadTimeMinutes,
					LeadTimeChoices = LeadTimeChoices.ToList(),
					DefaultManualAlertMinutes = RecurringCallRecurrence.DefaultManualAlertMinutes,
					MaxManualAlertMinutes = RecurringCallRecurrence.MaxManualAlertMinutes,
					ManualAlertChoices = ManualAlertChoices.ToList(),
					MaxTimesPerDay = RecurringCallRecurrence.MaxTimesPerDay,
					MaxInterval = RecurringCallRecurrence.MaxInterval
				},
				PageSize = 1,
				Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Creates (RecurringCallId 0) or updates a schedule, then creates the calls already due within its lead time. 400 with
		/// <see cref="SaveRecurringCallResult.Errors"/> when it cannot be saved.
		/// </summary>
		[HttpPost("SaveRecurringCall")]
		[Consumes("application/json")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Call_Create)]
		public async Task<ActionResult<SaveRecurringCallResult>> SaveRecurringCall([FromBody] RecurringCallInput input, CancellationToken cancellationToken)
		{
			if (input == null)
				return BadRequest();

			if (!await CanCreateAsync())
				return Unauthorized();

			RecurringCall series;
			if (input.RecurringCallId > 0)
			{
				series = await _recurringCallsService.GetByIdAsync(DepartmentId, input.RecurringCallId);
				if (series == null)
					return NotFound();

				if (!CanManage(series))
					return Unauthorized();
			}
			else
			{
				series = new RecurringCall { DepartmentId = DepartmentId };
			}

			var result = new SaveRecurringCallResult();
			void Fail(string key) => result.Errors.Add(new RecurringCallErrorData { Key = key, Message = ErrorMessages.TryGetValue(key, out var message) ? message : key });

			var startsOn = ParseDate(input.StartsOn);
			if (startsOn == null)
				Fail("ErrorStartsOnRequired");

			var endsOn = ParseDate(input.EndsOn);
			if (!string.IsNullOrWhiteSpace(input.EndsOn) && endsOn == null)
				Fail("ErrorEndsOnInvalid");

			if (input.Times != null && input.Times.Any(x => !string.IsNullOrWhiteSpace(x) && !RecurringCallRecurrence.TryParseTime(x, out _)))
				Fail("ErrorTimeInvalid");

			string geo = null;
			if (!string.IsNullOrWhiteSpace(input.GeoLocationData))
			{
				geo = ParseCoordinates(input.GeoLocationData);
				if (geo == null)
					Fail("ErrorCoordinatesInvalid");
			}

			Apply(input, series, startsOn, endsOn, geo);

			foreach (var error in RecurringCallRecurrence.Validate(series))
				Fail(error);

			if (series.DestinationPoiId.HasValue && await _mappingService.GetDestinationPOIByIdAsync(DepartmentId, series.DestinationPoiId.Value) == null)
				Fail("ErrorDestinationPoiInvalid");

			if (series.FacilityPoiId.HasValue && !(await _mappingService.GetPOIsForDepartmentAsync(DepartmentId) ?? new List<Poi>()).Any(x => x.PoiId == series.FacilityPoiId.Value))
				Fail("ErrorFacilityPoiInvalid");
			else
				// What the save itself does: a chosen pickup facility fills a blank address, so the fields below are checked as saved.
				await _recurringCallsService.ApplyFacilityDefaultsAsync(series);

			// The department's New Call Form Fields policy, as for a call entered through the API.
			var policy = await _departmentSettingsService.GetNewCallFieldPolicyAsync(DepartmentId) ?? new NewCallFieldPolicy();
			foreach (var violation in NewCallFieldPolicyValidator.Validate(policy, RecurringCallRecurrence.FieldValues(series)))
				result.Errors.Add(new RecurringCallErrorData { Key = $"NewCallField_{violation.Key}", Message = NewCallFieldPolicyValidator.DescribeViolations(new List<NewCallFieldViolation> { violation }) });

			if (result.Errors.Any())
			{
				result.Status = ResponseHelper.Failure;
				ResponseHelper.PopulateV4ResponseData(result);
				return BadRequest(result);
			}

			var saved = await _recurringCallsService.SaveAsync(series, UserId, series.RecurringCallId > 0 && input.ReplaceUpcomingCalls, Origin(), cancellationToken);

			result.Data = new SaveRecurringCallResultData
			{
				RecurringCallId = saved.Series.RecurringCallId,
				CreatedCalls = saved.CreatedCalls,
				ReplacedCalls = saved.ReplacedCalls
			};
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>Pauses a schedule without deleting it, with an optional reason and a date it resumes by itself.</summary>
		[HttpPost("PauseRecurringCall")]
		[Consumes("application/json")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_Create)]
		public async Task<ActionResult<RecurringCallActionResult>> PauseRecurringCall([FromBody] PauseRecurringCallInput input, CancellationToken cancellationToken)
		{
			var series = input == null ? null : await _recurringCallsService.GetByIdAsync(DepartmentId, input.RecurringCallId);
			if (series == null)
				return NotFound();

			if (!CanManage(series) || !await CanCreateAsync())
				return Unauthorized();

			var resumeOn = ParseDate(input.ResumeOn);
			if (!string.IsNullOrWhiteSpace(input.ResumeOn) && resumeOn == null)
				return BadRequest(ErrorMessages["ErrorResumeOnInvalid"]);

			var saved = await _recurringCallsService.PauseAsync(DepartmentId, series.RecurringCallId, UserId, input.Reason, resumeOn, input.DeleteUpcomingCalls, Origin(), cancellationToken);
			return Ok(await ActionResultAsync(saved != null, saved));
		}

		[HttpPost("ResumeRecurringCall")]
		[Consumes("application/json")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_Create)]
		public async Task<ActionResult<RecurringCallActionResult>> ResumeRecurringCall([FromBody] RecurringCallIdInput input, CancellationToken cancellationToken)
		{
			var series = input == null ? null : await _recurringCallsService.GetByIdAsync(DepartmentId, input.RecurringCallId);
			if (series == null)
				return NotFound();

			if (!CanManage(series) || !await CanCreateAsync())
				return Unauthorized();

			var saved = await _recurringCallsService.ResumeAsync(DepartmentId, series.RecurringCallId, UserId, Origin(), cancellationToken);
			return Ok(await ActionResultAsync(saved != null, saved));
		}

		/// <summary>Deletes a schedule; by default also the calls it created that have not gone out.</summary>
		[HttpPost("DeleteRecurringCall")]
		[Consumes("application/json")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_Create)]
		public async Task<ActionResult<RecurringCallActionResult>> DeleteRecurringCall([FromBody] DeleteRecurringCallInput input, CancellationToken cancellationToken)
		{
			var series = input == null ? null : await _recurringCallsService.GetByIdAsync(DepartmentId, input.RecurringCallId);
			if (series == null)
				return NotFound();

			if (!CanManage(series) || !await CanCreateAsync())
				return Unauthorized();

			var deleted = await _recurringCallsService.DeleteAsync(DepartmentId, series.RecurringCallId, UserId, input.DeleteUpcomingCalls, Origin(), cancellationToken);
			return Ok(await ActionResultAsync(deleted, null));
		}

		#endregion Schedules

		#region Calendar and occurrences

		/// <summary>
		/// The scheduled calls calendar between two instants (ISO-8601, years 1900 to 9000; at most 100 days): scheduled calls waiting
		/// to go out, the calls recurring schedules created, and their occurrences still to come, skipped, waiting for a dispatcher or
		/// missed.
		/// </summary>
		[HttpGet("GetScheduledCallsCalendar")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<ScheduledCallsCalendarResult>> GetScheduledCallsCalendar(string start, string end)
		{
			if (!DateTimeOffset.TryParse(start, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var from) ||
				!DateTimeOffset.TryParse(end, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var until) || until <= from ||
				!RecurringCallRecurrence.IsSupportedInstant(from.UtcDateTime) || !RecurringCallRecurrence.IsSupportedInstant(until.UtcDateTime))
				return BadRequest();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var schedules = (await _recurringCallsService.GetAllVisibleToUserAsync(DepartmentId, UserId)).ToDictionary(x => x.RecurringCallId);
			var entries = await _recurringCallsService.GetCalendarAsync(DepartmentId, UserId, from.UtcDateTime, until.UtcDateTime);
			var result = new ScheduledCallsCalendarResult();

			foreach (var entry in entries)
			{
				schedules.TryGetValue(entry.RecurringCallId ?? 0, out var series);

				result.Data.Add(new ScheduledCallCalendarEntryData
				{
					Kind = entry.Kind.ToString(),
					KindId = (int)entry.Kind,
					DispatchOn = Iso(entry.DispatchOnUtc),
					DispatchOnLocal = RecurringCallRecurrence.ToLocal(entry.DispatchOnUtc, department).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
					OccurrenceOn = entry.RecurringCallId.HasValue ? Iso(entry.OccurrenceOnUtc ?? entry.DispatchOnUtc) : null,
					Name = ProtectedDataEnvelope.SafeDisplay(entry.Name),
					Priority = entry.Priority,
					Address = ProtectedDataEnvelope.SafeDisplay(entry.Address),
					CallId = entry.CallId,
					CallNumber = entry.CallNumber,
					RecurringCallId = entry.RecurringCallId,
					ScheduleName = series != null ? ProtectedDataEnvelope.SafeDisplay(series.Name) : null
				});
			}

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>Skips an occurrence that has no call yet (still to come, waiting for a dispatcher, or missed).</summary>
		[HttpPost("SkipOccurrence")]
		[Consumes("application/json")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_Create)]
		public async Task<ActionResult<RecurringCallOccurrenceResult>> SkipOccurrence([FromBody] RecurringCallOccurrenceInput input, CancellationToken cancellationToken)
		{
			var (series, occurrence, error) = await OccurrenceAsync(input);
			if (error != null)
				return error;

			var done = await _recurringCallsService.SkipOccurrenceAsync(DepartmentId, series.RecurringCallId, occurrence, UserId, Origin(), cancellationToken);
			return Ok(OccurrenceResult(done, done ? "Skipped" : "NotSkipped", null));
		}

		[HttpPost("RestoreOccurrence")]
		[Consumes("application/json")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_Create)]
		public async Task<ActionResult<RecurringCallOccurrenceResult>> RestoreOccurrence([FromBody] RecurringCallOccurrenceInput input, CancellationToken cancellationToken)
		{
			var (series, occurrence, error) = await OccurrenceAsync(input);
			if (error != null)
				return error;

			var done = await _recurringCallsService.RestoreOccurrenceAsync(DepartmentId, series.RecurringCallId, occurrence, UserId, Origin(), cancellationToken);
			return Ok(OccurrenceResult(done, done ? "Restored" : "NotRestored", null));
		}

		/// <summary>
		/// Creates an occurrence's call without sending it, so it can be changed first: a future occurrence becomes a scheduled call,
		/// one already due a pending call (send it with Calls/DispatchCallNow). Returns the call already created when there is one.
		/// </summary>
		[HttpPost("CreateOccurrenceCall")]
		[Consumes("application/json")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_Create)]
		public async Task<ActionResult<RecurringCallOccurrenceResult>> CreateOccurrenceCall([FromBody] RecurringCallOccurrenceInput input, CancellationToken cancellationToken)
		{
			var (series, occurrence, error) = await OccurrenceAsync(input);
			if (error != null)
				return error;

			var call = await _recurringCallsService.CreateOccurrenceCallNowAsync(DepartmentId, series.RecurringCallId, occurrence, UserId, Origin(), cancellationToken);
			return Ok(OccurrenceResult(call != null, call != null ? "Created" : "NotCreated", call));
		}

		/// <summary>
		/// Sends an occurrence now (creating its call if it has none), with the schedule's run card and availability rules. Outcome
		/// Dispatched, Held when nobody could be sent (the call waits as pending and an alert is raised), or QueueFailed (not a
		/// success) when the broadcast could not be queued: nothing went out and the call is waiting again, a scheduled one to be
		/// retried by the scheduled-calls worker, a held one still pending for a dispatcher. NotWaiting: someone else is sending it,
		/// or it has gone out or been closed.
		/// </summary>
		[HttpPost("DispatchOccurrenceNow")]
		[Consumes("application/json")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_Create)]
		public async Task<ActionResult<RecurringCallOccurrenceResult>> DispatchOccurrenceNow([FromBody] RecurringCallOccurrenceInput input, CancellationToken cancellationToken)
		{
			var (series, occurrence, error) = await OccurrenceAsync(input);
			if (error != null)
				return error;

			var dispatched = await _recurringCallsService.DispatchOccurrenceNowAsync(DepartmentId, series.RecurringCallId, occurrence, UserId, Origin(), cancellationToken);
			var success = dispatched.Outcome == RecurringCallDispatchOutcomes.Dispatched || dispatched.Outcome == RecurringCallDispatchOutcomes.Held;

			var result = OccurrenceResult(success, dispatched.Outcome.ToString(), dispatched.Call);
			result.Data.UnavailableUnitIds = dispatched.UnavailableUnitIds ?? new List<int>();
			result.Data.UnavailableUserIds = dispatched.UnavailableUserIds ?? new List<string>();
			result.Data.RunCardApplied = dispatched.RunCardApplied;

			return Ok(result);
		}

		#endregion Calendar and occurrences

		#region Alerts

		/// <summary>
		/// The recurring call alerts the viewer can see: every open one, plus, with <paramref name="includeClosedHours"/>, those raised
		/// in that many past hours (at most 720). The Dispatch app shows them as a badge, banner and list; the hub event
		/// <c>recurringCallAlertsUpdated</c> (schedule id, { alertId, kind, raised }) says when to reload.
		/// </summary>
		[HttpGet("GetRecurringCallAlerts")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<RecurringCallAlertsResult>> GetRecurringCallAlerts(int? includeClosedHours, CancellationToken cancellationToken)
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var since = includeClosedHours.HasValue && includeClosedHours.Value > 0
				? DateTime.UtcNow.AddHours(-Math.Min(includeClosedHours.Value, 720))
				: (DateTime?)null;
			var canCreate = await CanCreateAsync();
			var alerts = await _recurringCallsService.GetAlertsAsync(DepartmentId, UserId, since, cancellationToken);
			var result = new RecurringCallAlertsResult();

			foreach (var info in alerts)
			{
				var alert = info.Alert;
				var kind = (RecurringCallAlertKinds)alert.Kind;
				var occurrence = DateTime.SpecifyKind(alert.OccurrenceOn, DateTimeKind.Utc);

				result.Data.Add(new RecurringCallAlertResultData
				{
					AlertId = alert.RecurringCallAlertId,
					RecurringCallId = alert.RecurringCallId,
					ScheduleName = ProtectedDataEnvelope.SafeDisplay(info.ScheduleName),
					DispatcherMode = info.DispatcherMode,
					ScheduleDeleted = info.ScheduleDeleted,
					Kind = kind.ToString(),
					KindId = alert.Kind,
					Reason = info.Reason,
					OccurrenceOn = Iso(occurrence),
					OccurrenceOnLocal = RecurringCallRecurrence.ToLocal(occurrence, department).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
					CallId = alert.CallId,
					CallNumber = info.CallNumber,
					CallState = info.CallState,
					CallDispatched = info.CallDispatched,
					UnavailableUnits = info.UnavailableUnits.Select(x => new RecurringCallAlertResourceData { Id = x.Key.ToString(CultureInfo.InvariantCulture), Name = x.Value }).ToList(),
					UnavailablePeople = info.UnavailablePeople.Select(x => new RecurringCallAlertResourceData { Id = x.Key, Name = x.Value }).ToList(),
					CreatedOn = Iso(alert.CreatedOn),
					AcknowledgedOn = alert.AcknowledgedOn.HasValue ? Iso(alert.AcknowledgedOn.Value) : null,
					AcknowledgedByUserId = alert.AcknowledgedByUserId,
					AcknowledgedByName = info.AcknowledgedByName,
					IsOpen = alert.IsOpen,
					ResolvedOn = alert.ResolvedOn.HasValue ? Iso(alert.ResolvedOn.Value) : null,
					Resolution = alert.Resolution.HasValue ? ((RecurringCallAlertResolutions)alert.Resolution.Value).ToString() : null,
					ResolvedByName = info.ResolvedByName,
					ResolutionNote = alert.ResolutionNote,
					// Send / create / skip: the occurrence has no call and the schedule still has it (not deleted, not moved since).
					CanSend = info.CanSend,
					// Acknowledge / dismiss: what those endpoints accept, a deleted schedule's alert included.
					CanAct = alert.IsOpen && canCreate
				});
			}

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		[HttpPost("AcknowledgeRecurringCallAlert")]
		[Consumes("application/json")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_Create)]
		public async Task<ActionResult<RecurringCallActionResult>> AcknowledgeRecurringCallAlert([FromBody] RecurringCallAlertInput input, CancellationToken cancellationToken)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.AlertId))
				return BadRequest();

			if (!await CanCreateAsync() || !await CanSeeAlertAsync(input.AlertId, cancellationToken))
				return Unauthorized();

			var done = await _recurringCallsService.AcknowledgeAlertAsync(DepartmentId, input.AlertId, UserId, Origin(), cancellationToken);
			return Ok(await ActionResultAsync(done, null));
		}

		[HttpPost("DismissRecurringCallAlert")]
		[Consumes("application/json")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_Create)]
		public async Task<ActionResult<RecurringCallActionResult>> DismissRecurringCallAlert([FromBody] RecurringCallAlertInput input, CancellationToken cancellationToken)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.AlertId))
				return BadRequest();

			if (!await CanCreateAsync() || !await CanSeeAlertAsync(input.AlertId, cancellationToken))
				return Unauthorized();

			var done = await _recurringCallsService.DismissAlertAsync(DepartmentId, input.AlertId, UserId, input.Note, Origin(), cancellationToken);
			return Ok(await ActionResultAsync(done, null));
		}

		#endregion Alerts

		#region Helpers

		private bool CanManage(RecurringCall series)
		{
			return series != null && (ClaimsAuthorizationHelper.IsUserDepartmentAdmin() ||
									  string.Equals(series.CreatedByUserId, UserId, StringComparison.OrdinalIgnoreCase));
		}

		private async Task<bool> CanCreateAsync()
		{
			return ClaimsAuthorizationHelper.CanCreateCall() && await _authorizationService.CanUserCreateCallAsync(UserId, DepartmentId);
		}

		private async Task<bool> CanSeeAlertAsync(string alertId, CancellationToken cancellationToken)
		{
			var alerts = await _recurringCallsService.GetAlertsAsync(DepartmentId, UserId, null, cancellationToken);
			return alerts.Any(x => string.Equals(x.Alert.RecurringCallAlertId, alertId, StringComparison.OrdinalIgnoreCase));
		}

		private async Task<(RecurringCall Series, DateTime Occurrence, ActionResult Error)> OccurrenceAsync(RecurringCallOccurrenceInput input)
		{
			if (input == null || !TryParseInstant(input.OccurrenceOn, out var occurrence) || !RecurringCallRecurrence.IsSupportedInstant(occurrence))
				return (null, default, BadRequest("OccurrenceOn must be an ISO-8601 instant (years 1900 to 9000)."));

			var series = await _recurringCallsService.GetByIdAsync(DepartmentId, input.RecurringCallId);
			if (series == null)
				return (null, default, NotFound());

			if (!await CanCreateAsync())
				return (null, default, Unauthorized());

			return (series, occurrence, null);
		}

		private static RecurringCallOccurrenceResult OccurrenceResult(bool success, string outcome, Call call)
		{
			var result = new RecurringCallOccurrenceResult
			{
				Success = success,
				Data = new RecurringCallOccurrenceResultData
				{
					Outcome = outcome,
					CallId = call?.CallId,
					CallNumber = call?.Number,
					CallState = call?.State
				},
				PageSize = 1,
				Status = success ? ResponseHelper.Success : ResponseHelper.Failure
			};
			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}

		private async Task<RecurringCallActionResult> ActionResultAsync(bool success, RecurringCall series)
		{
			var result = new RecurringCallActionResult { Success = success, Status = success ? ResponseHelper.Success : ResponseHelper.Failure };

			if (series != null)
			{
				var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
				var profiles = await _userProfileService.GetAllProfilesForDepartmentAsync(DepartmentId) ?? new Dictionary<string, UserProfile>();
				result.Data = ToData(series, department, await UpcomingDispatchesAsync(), new Dictionary<int, int>(), profiles, await CanCreateAsync());
				result.PageSize = 1;
			}

			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		private AuditOrigin Origin()
		{
			return new AuditOrigin
			{
				IpAddress = HttpContext?.Connection?.RemoteIpAddress?.ToString(),
				UserAgent = $"{Request?.Headers["User-Agent"]} {Request?.Headers["Accept-Language"]}"
			};
		}

		/// <summary>Per schedule, what will go out over the calendar window (waiting calls and occurrences still to come), in order.</summary>
		private async Task<Dictionary<int, List<DateTime>>> UpcomingDispatchesAsync()
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

		private RecurringCallResultData ToData(RecurringCall series, Department department, Dictionary<int, List<DateTime>> upcoming,
			Dictionary<int, int> alertCounts, Dictionary<string, UserProfile> profiles, bool canCreate)
		{
			var days = Enumerable.Range(0, 7).Where(x => (series.DaysOfWeek & RecurringCallRecurrence.DayBit((DayOfWeek)x)) != 0).ToList();
			var today = RecurringCallRecurrence.ToLocal(DateTime.UtcNow, department).Date;

			DateTime? next = null;
			if (series.IsActive)
			{
				if (upcoming.TryGetValue(series.RecurringCallId, out var times) && times.Any())
					next = times.First();
				else
					next = RecurringCallRecurrence.NextOccurrenceUtc(series, department, DateTime.UtcNow.AddDays(RecurringCallRecurrence.MaxRangeDays));
			}

			string Name(string userId) => !string.IsNullOrWhiteSpace(userId) && profiles.TryGetValue(userId, out var profile) ? profile?.FullName?.AsFirstNameLastName : null;

			return new RecurringCallResultData
			{
				RecurringCallId = series.RecurringCallId,
				Name = ProtectedDataEnvelope.SafeDisplay(series.Name),
				Description = series.Description,
				IsActive = series.IsActive,
				HasEnded = series.EndsOn.HasValue && series.EndsOn.Value.Date < today,
				Frequency = series.Frequency,
				RepeatInterval = series.RepeatInterval,
				Days = days,
				MonthlyDay = series.MonthlyDay,
				MonthlyWeek = series.MonthlyWeek,
				MonthlyWeekday = series.MonthlyWeekday,
				Times = RecurringCallRecurrence.ParseTimes(series.Times).Select(x => x.ToString(@"hh\:mm", CultureInfo.InvariantCulture)).ToList(),
				StartsOn = series.StartsOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
				EndsOn = series.EndsOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
				LeadTimeMinutes = series.LeadTimeMinutes,
				CreationMode = series.CreationMode,
				ManualAlertMinutes = series.ManualAlertMinutes,
				UseRunCard = series.UseRunCard,
				NatureOfCall = ProtectedDataEnvelope.SafeDisplay(series.NatureOfCall),
				Notes = ProtectedDataEnvelope.SafeDisplay(series.Notes),
				Address = ProtectedDataEnvelope.SafeDisplay(series.Address),
				GeoLocationData = series.GeoLocationData,
				Priority = series.Priority,
				Type = series.Type,
				ContactName = ProtectedDataEnvelope.SafeDisplay(series.ContactName),
				ContactNumber = ProtectedDataEnvelope.SafeDisplay(series.ContactNumber),
				ExternalIdentifier = series.ExternalIdentifier,
				ReferenceNumber = series.ReferenceNumber,
				IncidentNumber = series.IncidentNumber,
				MapPage = series.MapPage,
				DestinationPoiId = series.DestinationPoiId,
				CheckInTimersEnabled = series.CheckInTimersEnabled,
				ScheduleContactName = series.ScheduleContactName,
				ScheduleContactNumber = series.ScheduleContactNumber,
				ScheduleContactEmail = series.ScheduleContactEmail,
				FacilityName = series.FacilityName,
				FacilityDetails = series.FacilityDetails,
				FacilityPoiId = series.FacilityPoiId,
				DestinationName = series.DestinationName,
				DestinationDetails = series.DestinationDetails,
				DispatchUserIds = series.GetDispatchUserIds(),
				DispatchGroupIds = series.GetDispatchGroupIds(),
				DispatchUnitIds = series.GetDispatchUnitIds(),
				DispatchRoleIds = series.GetDispatchRoleIds(),
				AlertUserIds = series.GetAlertUserIds(),
				AlertGroupIds = series.GetAlertGroupIds(),
				AlertRoleIds = series.GetAlertRoleIds(),
				PausedOn = series.PausedOn.HasValue ? Iso(series.PausedOn.Value) : null,
				PausedByUserId = series.PausedByUserId,
				PausedByName = Name(series.PausedByUserId),
				PauseReason = series.PauseReason,
				ResumeOn = series.ResumeOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
				CreatedByUserId = series.CreatedByUserId,
				CreatedByName = Name(series.CreatedByUserId),
				CreatedOn = Iso(series.CreatedOn),
				UpdatedOn = series.UpdatedOn.HasValue ? Iso(series.UpdatedOn.Value) : null,
				NextDispatchOn = next.HasValue ? Iso(next.Value) : null,
				OpenAlertCount = alertCounts.TryGetValue(series.RecurringCallId, out var count) ? count : 0,
				CanManage = canCreate && CanManage(series)
			};
		}

		/// <summary>The posted schedule onto the entity. Lists left null are empty; a new schedule may be saved paused.</summary>
		private void Apply(RecurringCallInput input, RecurringCall series, DateTime? startsOn, DateTime? endsOn, string geo)
		{
			string Clean(string value, int max)
			{
				if (string.IsNullOrWhiteSpace(value))
					return null;

				var text = value.Trim();
				return text.Length > max ? text.Substring(0, max) : text;
			}

			var isNew = series.RecurringCallId == 0;

			series.Name = Clean(input.Name, RecurringCall.MaxNameLength);
			series.Description = Clean(input.Description, 4000);
			series.Frequency = input.Frequency;
			series.RepeatInterval = input.RepeatInterval;
			series.DaysOfWeek = (input.Days ?? new List<int>()).Where(x => x >= 0 && x <= 6).Aggregate(0, (bits, day) => bits | RecurringCallRecurrence.DayBit((DayOfWeek)day));
			series.MonthlyWeek = input.MonthlyWeek;
			series.MonthlyDay = input.MonthlyWeek > 0 ? 0 : input.MonthlyDay;
			series.MonthlyWeekday = input.MonthlyWeek > 0 ? input.MonthlyWeekday : 0;
			series.Times = RecurringCallRecurrence.FormatTimes((input.Times ?? new List<string>())
				.Select(x => RecurringCallRecurrence.TryParseTime(x, out var time) ? (TimeSpan?)time : null)
				.Where(x => x.HasValue).Select(x => x.Value));
			series.StartsOn = startsOn ?? series.StartsOn;
			series.EndsOn = endsOn;
			series.CreationMode = input.CreationMode;
			series.ManualAlertMinutes = input.ManualAlertMinutes ?? (isNew ? RecurringCallRecurrence.DefaultManualAlertMinutes : series.ManualAlertMinutes);
			series.LeadTimeMinutes = input.LeadTimeMinutes ?? (isNew ? RecurringCallRecurrence.DefaultLeadTimeMinutes : series.LeadTimeMinutes);
			if (series.IsDispatcherMode && (series.LeadTimeMinutes < RecurringCallRecurrence.MinLeadTimeMinutes || series.LeadTimeMinutes > RecurringCallRecurrence.MaxLeadTimeMinutes))
				series.LeadTimeMinutes = RecurringCallRecurrence.DefaultLeadTimeMinutes;
			series.UseRunCard = input.UseRunCard;

			series.NatureOfCall = Clean(input.NatureOfCall, int.MaxValue);
			series.Notes = Clean(input.Notes, int.MaxValue);
			series.Address = Clean(input.Address, int.MaxValue);
			series.GeoLocationData = geo;
			series.Priority = input.Priority;
			series.Type = Clean(input.Type, int.MaxValue);
			series.ContactName = Clean(input.ContactName, int.MaxValue);
			series.ContactNumber = Clean(input.ContactNumber, int.MaxValue);
			series.ExternalIdentifier = Clean(input.ExternalIdentifier, int.MaxValue);
			series.ReferenceNumber = Clean(input.ReferenceNumber, int.MaxValue);
			series.IncidentNumber = Clean(input.IncidentNumber, int.MaxValue);
			series.MapPage = Clean(input.MapPage, int.MaxValue);
			series.DestinationPoiId = input.DestinationPoiId.HasValue && input.DestinationPoiId.Value > 0 ? input.DestinationPoiId : null;
			series.CheckInTimersEnabled = input.CheckInTimersEnabled;

			series.ScheduleContactName = Clean(input.ScheduleContactName, RecurringCall.MaxShortFieldLength);
			series.ScheduleContactNumber = Clean(input.ScheduleContactNumber, 100);
			series.ScheduleContactEmail = Clean(input.ScheduleContactEmail, 256);
			series.FacilityName = Clean(input.FacilityName, RecurringCall.MaxShortFieldLength);
			series.FacilityDetails = Clean(input.FacilityDetails, 2000);
			series.FacilityPoiId = input.FacilityPoiId.HasValue && input.FacilityPoiId.Value > 0 ? input.FacilityPoiId : null;
			series.DestinationName = Clean(input.DestinationName, RecurringCall.MaxShortFieldLength);
			series.DestinationDetails = Clean(input.DestinationDetails, 2000);

			series.DispatchUserIds = RecurringCall.JoinIds(input.DispatchUserIds);
			series.DispatchGroupIds = RecurringCall.JoinIds(input.DispatchGroupIds);
			series.DispatchUnitIds = RecurringCall.JoinIds(input.DispatchUnitIds);
			series.DispatchRoleIds = RecurringCall.JoinIds(input.DispatchRoleIds);
			series.AlertUserIds = RecurringCall.JoinIds(input.AlertUserIds);
			series.AlertGroupIds = RecurringCall.JoinIds(input.AlertGroupIds);
			series.AlertRoleIds = RecurringCall.JoinIds(input.AlertRoleIds);

			if (isNew)
			{
				series.IsActive = input.IsActive ?? true;
				if (!series.IsActive)
				{
					series.PausedOn = DateTime.UtcNow;
					series.PausedByUserId = UserId;
				}
			}
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

		/// <summary>An ISO-8601 instant ("…Z", or with an offset); one without either is taken as UTC.</summary>
		public static bool TryParseInstant(string value, out DateTime utc)
		{
			if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
			{
				utc = DateTime.SpecifyKind(parsed.UtcDateTime, DateTimeKind.Utc);
				return true;
			}

			utc = default;
			return false;
		}

		public static string Iso(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

		#endregion Helpers
	}
}
