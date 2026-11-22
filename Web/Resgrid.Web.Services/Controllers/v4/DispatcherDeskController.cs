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
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.DispatcherDesk;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// The dispatcher desk ("Dispatchers on duty", M0324): signing in to the desk from the Dispatch app, the work routed to on-duty
	/// dispatchers (pending and scheduled calls, texts, phone calls, emails, chats), their breaks and the coverage timeline, and the
	/// dispatch rosters of shift days. Everything but the shift roster reads and changes (and taking the caller's own device off
	/// desk pushes) is for people allowed to work dispatch
	/// (the Dispatch app login permission, 403 otherwise); acting for another dispatcher (their breaks, work they hold) and the
	/// activity numbers are for desk supervisors, shift managers and department admins. Every POST answers a
	/// <see cref="DispatcherDeskActionResult"/>: a business-rule refusal is HTTP 200 with Success false and the DispatcherDeskErrors
	/// name. Authorization is the exception: a POST by a caller without the Dispatch app login permission gets 403 whose body is that
	/// result with the NotADispatcher error, and a GET the caller may not read answers 403 with no body.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class DispatcherDeskController : V4AuthenticatedApiControllerbase
	{
		/// <summary>The longest window the coverage timeline and the break list answer.</summary>
		public const int MaxWindowDays = 31;

		/// <summary>The longest window of the activity numbers.</summary>
		public const int MaxStatsDays = 92;

		private readonly IDispatchAccessService _dispatchAccessService;
		private readonly IDispatcherDeskConfigService _configService;
		private readonly IDispatcherPresenceService _presenceService;
		private readonly IDispatcherRoutingService _routingService;
		private readonly IDispatcherBreaksService _breaksService;
		private readonly IDispatcherConversationService _conversationService;
		private readonly IDispatcherVoiceService _voiceService;
		private readonly IShiftDispatchersService _shiftDispatchersService;
		private readonly IDispatcherDeskPushProvider _pushProvider;
		private readonly IShiftsService _shiftsService;
		private readonly IAuthorizationService _authorizationService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUserProfileService _userProfileService;
		private readonly ICallsService _callsService;

		private Dictionary<string, UserProfile> _profiles;
		private Department _department;

		public DispatcherDeskController(IDispatchAccessService dispatchAccessService, IDispatcherDeskConfigService configService,
			IDispatcherPresenceService presenceService, IDispatcherRoutingService routingService, IDispatcherBreaksService breaksService,
			IDispatcherConversationService conversationService, IDispatcherVoiceService voiceService, IShiftDispatchersService shiftDispatchersService,
			IDispatcherDeskPushProvider pushProvider, IShiftsService shiftsService, IAuthorizationService authorizationService,
			IDepartmentsService departmentsService, IUserProfileService userProfileService, ICallsService callsService)
		{
			_dispatchAccessService = dispatchAccessService;
			_configService = configService;
			_presenceService = presenceService;
			_routingService = routingService;
			_breaksService = breaksService;
			_conversationService = conversationService;
			_voiceService = voiceService;
			_shiftDispatchersService = shiftDispatchersService;
			_pushProvider = pushProvider;
			_shiftsService = shiftsService;
			_authorizationService = authorizationService;
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
			_callsService = callsService;
		}

		#region Reads

		/// <summary>
		/// The whole desk board: the department's desk settings, the caller's desk member, every on-duty or signed-in dispatcher,
		/// the open queue and the caller's breaks today. A caller who may not work dispatch gets only the settings and
		/// IsDispatcher false (the app then hides the desk).
		/// </summary>
		[HttpGet("GetDesk")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<DispatcherDeskResult>> GetDesk()
		{
			var now = DateTime.UtcNow;
			var config = await _configService.GetConfigAsync(DepartmentId);
			var data = new DispatcherDeskResultData
			{
				Enabled = config.Enabled,
				GeneratedOn = Iso(now),
				Config = ToConfigData(config),
				IsDispatcher = await IsDispatcherAsync()
			};

			if (data.IsDispatcher)
			{
				var snapshot = await _presenceService.GetDeskAsync(DepartmentId, now);
				var canSupervise = await _presenceService.CanSuperviseAsync(DepartmentId, UserId);

				data.GeneratedOn = Iso(snapshot.GeneratedOn == default ? now : snapshot.GeneratedOn);
				data.Members = new List<DispatcherDeskMemberData>();
				foreach (var member in snapshot.Members ?? new List<DispatcherDeskMember>())
					data.Members.Add(await ToMemberDataAsync(member, SameUser(member.UserId, UserId) && canSupervise));

				var me = (snapshot.Members ?? new List<DispatcherDeskMember>()).FirstOrDefault(x => SameUser(x.UserId, UserId)) ??
						 await _presenceService.GetMemberAsync(DepartmentId, UserId, now);
				data.Me = me != null ? await ToMemberDataAsync(me, canSupervise) : null;

				data.Queue = await ToItemsDataAsync(snapshot.Queue);

				foreach (var dispatcherBreak in await _breaksService.GetOpenBreaksForUserAsync(DepartmentId, UserId, now) ?? new List<DispatcherBreak>())
					data.MyBreaks.Add(await ToBreakDataAsync(dispatcherBreak));

				data.MinimumAvailable = snapshot.MinimumAvailable;
				data.AvailableCount = snapshot.AvailableCount;
				data.OnBreakCount = snapshot.OnBreakCount;
				data.OnDutyCount = snapshot.OnDutyCount;
				data.BelowMinimum = snapshot.BelowMinimum;
			}

			var result = new DispatcherDeskResult { Data = data, PageSize = data.Queue.Count, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>The open work on the desk (queued, offered and held), in the order dispatchers should take it.</summary>
		[HttpGet("GetQueue")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<DispatcherQueueResult>> GetQueue()
		{
			if (!await IsDispatcherAsync())
				return Forbidden();

			var result = new DispatcherQueueResult { Data = await ToItemsDataAsync(await _routingService.GetOpenItemsAsync(DepartmentId)) };
			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>One desk item (open or closed) with its history and conversation. 404 for an item of another department.</summary>
		[HttpGet("GetItem")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<DispatcherQueueItemDetailResult>> GetItem(string itemId)
		{
			if (!await IsDispatcherAsync())
				return Forbidden();

			var detail = string.IsNullOrWhiteSpace(itemId) ? null : await _routingService.GetItemDetailAsync(DepartmentId, itemId, UserId);
			if (detail?.Item == null || detail.Item.DepartmentId != DepartmentId)
				return NotFound();

			var item = detail.Item;
			var data = new DispatcherQueueItemDetailData
			{
				Item = await ToItemDataAsync(item, detail.Call, detail.Events, detail.Messages),
				CanReply = DispatcherRoutingPolicy.IntakeConversationIdOf(item) != null ? CanReplyOnIntakeThread(item, detail.IntakeConversation) : CanReply(item),
				CanConnectVoice = CanConnectVoice(item, detail.Events),
				IntakeConversation = await ToIntakeThreadDataAsync(detail.IntakeConversation)
			};

			foreach (var e in (detail.Events ?? new List<DispatcherQueueEvent>()).OrderBy(x => x.OccurredOn))
				data.Events.Add(new DispatcherQueueEventData
				{
					EventType = e.EventType,
					UserId = e.UserId,
					UserName = await NameForAsync(e.UserId),
					OccurredOn = Iso(e.OccurredOn),
					Detail = e.Detail
				});

			foreach (var message in (detail.Messages ?? new List<DispatcherConversationMessage>()).OrderBy(x => x.CreatedOn))
				data.Messages.Add(await ToMessageDataAsync(message));

			var result = new DispatcherQueueItemDetailResult { Data = data, PageSize = 1, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Duty windows, breaks and coverage slots for the team timeline. from/to are ISO UTC instants (default: today's
		/// department-local day, at most <see cref="MaxWindowDays"/> days); slotMinutes 1 to 60 (default 5).
		/// </summary>
		[HttpGet("GetCoverage")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<DispatcherCoverageResult>> GetCoverage(string from, string to, int? slotMinutes)
		{
			if (!await IsDispatcherAsync())
				return Forbidden();

			var window = await WindowAsync(from, to, MaxWindowDays);
			if (window == null || (slotMinutes.HasValue && (slotMinutes.Value < 1 || slotMinutes.Value > 60)))
				return BadRequest();

			var coverage = await _breaksService.GetCoverageAsync(DepartmentId, window.Value.FromUtc, window.Value.ToUtc, slotMinutes ?? 5);
			var data = new DispatcherCoverageResultData
			{
				FromUtc = Iso(coverage.FromUtc == default ? window.Value.FromUtc : coverage.FromUtc),
				ToUtc = Iso(coverage.ToUtc == default ? window.Value.ToUtc : coverage.ToUtc),
				SlotMinutes = coverage.SlotMinutes > 0 ? coverage.SlotMinutes : slotMinutes ?? 5,
				Duty = (coverage.Duty ?? new List<DispatcherDutyWindow>()).Select(ToDutyData).ToList(),
				Slots = (coverage.Slots ?? new List<DispatcherCoverageSlot>()).Select(x => new DispatcherCoverageSlotData
				{
					StartUtc = Iso(x.StartUtc),
					EndUtc = Iso(x.EndUtc),
					OnDuty = x.OnDuty,
					OnBreak = x.OnBreak,
					Available = x.Available,
					Minimum = x.Minimum,
					BelowMinimum = x.BelowMinimum,
					BreaksOverlap = x.BreaksOverlap
				}).ToList(),
				Warnings = (coverage.Warnings ?? new List<DispatcherDeskWarning>()).Select(ToWarningData).ToList()
			};

			foreach (var dispatcherBreak in coverage.Breaks ?? new List<DispatcherBreak>())
				data.Breaks.Add(await ToBreakDataAsync(dispatcherBreak));

			foreach (var userId in data.Duty.Select(x => x.UserId).Concat(data.Breaks.Select(x => x.UserId))
						 .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
				data.Names[userId] = await NameForAsync(userId) ?? userId;

			var result = new DispatcherCoverageResult { Data = data, PageSize = data.Slots.Count, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Every dispatcher's breaks planned or taken in the window (ISO UTC from/to; default today's department-local day, at most
		/// <see cref="MaxWindowDays"/> days): everyone sees the whole team's breaks to keep coverage and avoid overlaps.
		/// </summary>
		[HttpGet("GetBreaks")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<DispatcherBreaksResult>> GetBreaks(string from, string to)
		{
			if (!await IsDispatcherAsync())
				return Forbidden();

			var window = await WindowAsync(from, to, MaxWindowDays);
			if (window == null)
				return BadRequest();

			var result = new DispatcherBreaksResult();
			foreach (var dispatcherBreak in (await _breaksService.GetBreaksAsync(DepartmentId, window.Value.FromUtc, window.Value.ToUtc) ?? new List<DispatcherBreak>())
					 .OrderBy(x => x.StartedOn ?? x.ScheduledStartOn ?? x.CreatedOn))
				result.Data.Add(await ToBreakDataAsync(dispatcherBreak));

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// A shift's "Dispatchers on duty" settings and roster: the roster of the given shift day (approved and pending, with each
		/// dispatcher's window), else the shift's standing roster. Anyone who can see shifts can see it; CanSignup tells whether the
		/// caller can sign up for the day.
		/// </summary>
		[HttpGet("GetShiftDispatchers")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<ActionResult<ShiftDispatchersResult>> GetShiftDispatchers(int shiftId, int? shiftDayId)
		{
			var shift = await _shiftsService.GetShiftByIdAsync(shiftId);
			if (shift == null || shift.DepartmentId != DepartmentId)
				return NotFound();

			ShiftDay day = null;
			if (shiftDayId.HasValue && shiftDayId.Value > 0)
			{
				day = await _shiftsService.GetShiftDayByIdAsync(shiftDayId.Value);
				if (day == null || day.ShiftId != shift.ShiftId)
					return NotFound();
			}

			var settings = await _shiftDispatchersService.GetSettingsForShiftAsync(shift.ShiftId);
			var data = new ShiftDispatchersResultData
			{
				ShiftId = shift.ShiftId,
				IsEnabled = settings?.IsEnabled ?? false,
				IsDispatchOnly = settings?.IsDispatchOnly ?? false,
				RequiredDispatchers = settings?.RequiredDispatchers ?? 0,
				MinimumOnDesk = settings?.MinimumOnDesk ?? 0,
				AllowSelfScheduledBreaks = settings?.AllowSelfScheduledBreaks ?? false,
				BreakPlan = (settings?.GetBreakPlan() ?? new List<DispatcherBreakPlanEntry>()).Select(ToBreakPlanData).ToList()
			};

			if (day != null)
			{
				data.Dispatchers = (await _shiftDispatchersService.GetDayRosterAsync(DepartmentId, day.ShiftDayId) ?? new List<DispatcherDutyWindow>())
					.Select(ToDutyData).ToList();
				data.IsSignedUp = data.Dispatchers.Any(x => SameUser(x.UserId, UserId));
				data.CanSignup = data.IsEnabled && !data.IsSignedUp && shift.AssignmentType == (int)ShiftAssignmentTypes.Signup &&
								 !await IsShiftDayOverAsync(day, shift) && await _shiftDispatchersService.IsEligibleDispatcherAsync(DepartmentId, UserId);
			}
			else
			{
				data.Dispatchers = (await _shiftDispatchersService.GetRosterForShiftAsync(shift.ShiftId) ?? new List<ShiftDispatcher>())
					.Where(x => x != null && !x.ShiftDayId.HasValue && x.Status != (int)ShiftDispatcherStatuses.Denied)
					.Select(x => new DispatcherDutyData
					{
						UserId = x.UserId,
						ShiftId = x.ShiftId,
						ShiftName = shift.Name,
						ShiftColor = shift.Color,
						IsSupervisor = x.IsSupervisor,
						Source = x.Source,
						IsPending = x.Status == (int)ShiftDispatcherStatuses.PendingApproval,
						ShiftDispatcherId = x.ShiftDispatcherId
					}).ToList();
				data.IsSignedUp = data.Dispatchers.Any(x => SameUser(x.UserId, UserId));
			}

			foreach (var userId in data.Dispatchers.Select(x => x.UserId).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
				data.Names[userId] = await NameForAsync(userId) ?? userId;

			var result = new ShiftDispatchersResult { Data = data, PageSize = data.Dispatchers.Count, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Desk numbers per dispatcher and for the department over the window (ISO UTC from/to; default today's department-local
		/// day, at most <see cref="MaxStatsDays"/> days). Desk supervisors, shift managers and department admins only.
		/// </summary>
		[HttpGet("GetStats")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<DispatcherStatsResult>> GetStats(string from, string to)
		{
			if (!await IsDispatcherAsync() || !await _presenceService.CanSuperviseAsync(DepartmentId, UserId))
				return Forbidden();

			var window = await WindowAsync(from, to, MaxStatsDays);
			if (window == null)
				return BadRequest();

			var stats = await _routingService.GetStatsAsync(DepartmentId, window.Value.FromUtc, window.Value.ToUtc) ?? new DispatcherQueueStats();
			var data = new DispatcherQueueStatsData
			{
				Created = stats.Created,
				Completed = stats.Completed,
				Overflowed = stats.Overflowed,
				Cancelled = stats.Cancelled,
				Escalated = stats.Escalated,
				AverageSecondsToAnswer = stats.AverageSecondsToAnswer,
				CreatedByKind = (stats.CreatedByKind ?? new Dictionary<int, int>()).ToDictionary(x => x.Key.ToString(CultureInfo.InvariantCulture), x => x.Value)
			};

			foreach (var dispatcher in stats.Dispatchers ?? new List<DispatcherDeskStats>())
				data.Dispatchers.Add(new DispatcherDeskStatsData
				{
					UserId = dispatcher.UserId,
					Name = dispatcher.Name ?? await NameForAsync(dispatcher.UserId),
					Offered = dispatcher.Offered,
					Accepted = dispatcher.Accepted,
					Declined = dispatcher.Declined,
					OffersMissed = dispatcher.OffersMissed,
					Completed = dispatcher.Completed,
					Dispatched = dispatcher.Dispatched,
					AverageSecondsToAccept = dispatcher.AverageSecondsToAccept,
					AverageSecondsToComplete = dispatcher.AverageSecondsToComplete,
					Breaks = dispatcher.Breaks,
					BreakMinutes = dispatcher.BreakMinutes,
					BreaksOverdue = dispatcher.BreaksOverdue,
					OverdueMinutes = dispatcher.OverdueMinutes,
					BreaksMissed = dispatcher.BreaksMissed
				});

			var result = new DispatcherStatsResult { Data = data, PageSize = data.Dispatchers.Count, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>The department's desk settings the Dispatch app needs.</summary>
		[HttpGet("GetConfig")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<DispatcherDeskConfigResult>> GetConfig()
		{
			if (!await IsDispatcherAsync())
				return Forbidden();

			var result = new DispatcherDeskConfigResult { Data = ToConfigData(await _configService.GetConfigAsync(DepartmentId)), PageSize = 1, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		#endregion Reads

		#region Desk session

		/// <summary>Signs on to the desk (on the shift running now, or unscheduled where the department allows it).</summary>
		[HttpPost("SignIn")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<DispatcherDeskActionResult>> SignIn(DispatcherSignInInput input, CancellationToken cancellationToken)
		{
			if (!await IsDispatcherAsync())
				return NotADispatcher();

			input ??= new DispatcherSignInInput();
			DateTime? dutyEndsOn = null;
			if (!string.IsNullOrWhiteSpace(input.DutyEndsOn))
			{
				if (!TryParseInstant(input.DutyEndsOn, out var parsed))
					return await RefusedAsync(DispatcherDeskErrors.InvalidInput);

				dutyEndsOn = parsed;
			}

			var result = await _presenceService.SignInAsync(DepartmentId, UserId, new DispatcherSignInRequest
			{
				DeviceKind = input.DeviceKind,
				CallbackNumber = input.CallbackNumber,
				Unscheduled = input.Unscheduled,
				DutyEndsOn = dutyEndsOn,
				StartAway = input.StartAway
			}, cancellationToken);

			return Ok(await ActionResultAsync(result));
		}

		/// <summary>Signs off the desk: held and offered work goes back to the queue, an active break ends.</summary>
		[HttpPost("SignOff")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<DispatcherDeskActionResult>> SignOff(CancellationToken cancellationToken)
		{
			if (!await IsDispatcherAsync())
				return NotADispatcher();

			return Ok(await ActionResultAsync(await _presenceService.SignOffAsync(DepartmentId, UserId, UserId, cancellationToken)));
		}

		/// <summary>"Still here" from the app in the foreground.</summary>
		[HttpPost("Heartbeat")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<DispatcherDeskActionResult>> Heartbeat(CancellationToken cancellationToken)
		{
			if (!await IsDispatcherAsync())
				return NotADispatcher();

			await _presenceService.HeartbeatAsync(DepartmentId, UserId, cancellationToken);
			return Ok(await ActionResultAsync(DispatcherDeskResult<bool>.Ok(true)));
		}

		/// <summary>Back to taking routed work (from Away).</summary>
		[HttpPost("SetAvailable")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<DispatcherDeskActionResult>> SetAvailable(CancellationToken cancellationToken)
		{
			if (!await IsDispatcherAsync())
				return NotADispatcher();

			return Ok(await ActionResultAsync(await _presenceService.SetAvailableAsync(DepartmentId, UserId, UserId, cancellationToken)));
		}

		/// <summary>Signed in but not taking routed work for now (offers in flight go back to the queue).</summary>
		[HttpPost("SetAway")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<DispatcherDeskActionResult>> SetAway(DispatcherAwayInput input, CancellationToken cancellationToken)
		{
			if (!await IsDispatcherAsync())
				return NotADispatcher();

			return Ok(await ActionResultAsync(await _presenceService.SetAwayAsync(DepartmentId, UserId, Trim(input?.Reason, 250), UserId, cancellationToken)));
		}

		/// <summary>The phone the desk rings to connect the dispatcher to a caller (E.164; empty = the profile mobile).</summary>
		[HttpPost("SetCallbackNumber")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<DispatcherDeskActionResult>> SetCallbackNumber(DispatcherCallbackNumberInput input, CancellationToken cancellationToken)
		{
			if (!await IsDispatcherAsync())
				return NotADispatcher();

			return Ok(await ActionResultAsync(await _presenceService.SetCallbackNumberAsync(DepartmentId, UserId, input?.CallbackNumber, cancellationToken)));
		}

		/// <summary>
		/// Puts this device's push token on the caller's Dispatch-only desk subscriber (offers, queue alerts, break reminders). Only
		/// the Dispatch app calls it; its ordinary Devices/RegisterDevice registration stays as it is.
		/// </summary>
		[HttpPost("RegisterDevice")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<DispatcherDeskActionResult>> RegisterDevice(DispatcherDeviceInput input, CancellationToken cancellationToken)
		{
			if (!await IsDispatcherAsync())
				return NotADispatcher();

			if (!IsValidDevice(input))
				return await RefusedAsync(DispatcherDeskErrors.InvalidInput);

			var registered = await _pushProvider.RegisterDeviceAsync(DepartmentId, UserId, input.Platform, input.Token.Trim(), cancellationToken);
			return Ok(await ActionResultAsync(registered ? DispatcherDeskResult<bool>.Ok(true) : DispatcherDeskResult<bool>.Fail(DispatcherDeskErrors.ChannelUnavailable)));
		}

		/// <summary>
		/// Takes this device's push token off the desk subscriber (signing out of the Dispatch app). Open to a caller who may no
		/// longer dispatch too: it only ever removes the caller's own token, and a revoked dispatcher's device must be able to stop
		/// desk pushes.
		/// </summary>
		[HttpPost("UnregisterDevice")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<DispatcherDeskActionResult>> UnregisterDevice(DispatcherDeviceInput input, CancellationToken cancellationToken)
		{
			if (!IsValidDevice(input))
				return await RefusedAsync(DispatcherDeskErrors.InvalidInput);

			var removed = await _pushProvider.UnregisterDeviceAsync(DepartmentId, UserId, input.Platform, input.Token.Trim(), cancellationToken);
			return Ok(await ActionResultAsync(removed ? DispatcherDeskResult<bool>.Ok(true) : DispatcherDeskResult<bool>.Fail(DispatcherDeskErrors.ChannelUnavailable)));
		}

		#endregion Desk session

		#region Work

		/// <summary>Takes an item: one offered to the caller, or one waiting in the shared queue.</summary>
		[HttpPost("AcceptItem")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<DispatcherDeskActionResult>> AcceptItem(DispatcherItemInput input, CancellationToken cancellationToken)
		{
			if (!await IsDispatcherAsync())
				return NotADispatcher();

			if (string.IsNullOrWhiteSpace(input?.ItemId))
				return await RefusedAsync(DispatcherDeskErrors.InvalidInput);

			return Ok(await ItemActionResultAsync(await _routingService.AcceptAsync(DepartmentId, input.ItemId, UserId, cancellationToken)));
		}

		/// <summary>Passes on an item offered to the caller (it is offered to someone else and never to them again).</summary>
		[HttpPost("DeclineItem")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<DispatcherDeskActionResult>> DeclineItem(DispatcherDeclineInput input, CancellationToken cancellationToken)
		{
			if (!await IsDispatcherAsync())
				return NotADispatcher();

			if (string.IsNullOrWhiteSpace(input?.ItemId))
				return await RefusedAsync(DispatcherDeskErrors.InvalidInput);

			return Ok(await ItemActionResultAsync(await _routingService.DeclineAsync(DepartmentId, input.ItemId, UserId, Trim(input.Reason, 250), cancellationToken)));
		}

		/// <summary>Puts a held item back in the queue. Releasing work someone else holds needs supervisor rights.</summary>
		[HttpPost("ReleaseItem")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<DispatcherDeskActionResult>> ReleaseItem(DispatcherItemInput input, CancellationToken cancellationToken)
		{
			if (!await IsDispatcherAsync())
				return NotADispatcher();

			var refusal = await CheckHolderOrSupervisorAsync(input?.ItemId);
			if (refusal.HasValue)
				return await RefusedAsync(refusal.Value);

			return Ok(await ItemActionResultAsync(await _routingService.ReleaseAsync(DepartmentId, input.ItemId, UserId, cancellationToken)));
		}

		/// <summary>
		/// Hands an item to another on-duty dispatcher (as an offer they accept). Transferring work the caller does not hold needs
		/// supervisor rights.
		/// </summary>
		[HttpPost("TransferItem")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<DispatcherDeskActionResult>> TransferItem(DispatcherTransferInput input, CancellationToken cancellationToken)
		{
			if (!await IsDispatcherAsync())
				return NotADispatcher();

			if (string.IsNullOrWhiteSpace(input?.ToUserId))
				return await RefusedAsync(DispatcherDeskErrors.InvalidInput);

			var refusal = await CheckHolderOrSupervisorAsync(input.ItemId);
			if (refusal.HasValue)
				return await RefusedAsync(refusal.Value);

			return Ok(await ItemActionResultAsync(await _routingService.TransferAsync(DepartmentId, input.ItemId, UserId, input.ToUserId, Trim(input.Note, 500),
				cancellationToken)));
		}

		/// <summary>Finishes an item with an outcome (and the call it became, if any).</summary>
		[HttpPost("CompleteItem")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<DispatcherDeskActionResult>> CompleteItem(DispatcherCompleteInput input, CancellationToken cancellationToken)
		{
			if (!await IsDispatcherAsync())
				return NotADispatcher();

			if (string.IsNullOrWhiteSpace(input?.ItemId) || !Enum.IsDefined(typeof(DispatcherQueueItemOutcomes), input.Outcome) ||
				input.Outcome == (int)DispatcherQueueItemOutcomes.None)
				return await RefusedAsync(DispatcherDeskErrors.InvalidInput);

			if (input.CallId.HasValue && !await IsDepartmentCallAsync(input.CallId.Value))
				return await RefusedAsync(DispatcherDeskErrors.InvalidInput);

			return Ok(await ItemActionResultAsync(await _routingService.CompleteAsync(DepartmentId, input.ItemId, UserId,
				(DispatcherQueueItemOutcomes)input.Outcome, input.CallId, Trim(input.Note, 1000), input.NotifyParticipant ?? true, cancellationToken)));
		}

		/// <summary>Links the call made from a conversation to its item (the item stays held until completed).</summary>
		[HttpPost("LinkCall")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<DispatcherDeskActionResult>> LinkCall(DispatcherLinkCallInput input, CancellationToken cancellationToken)
		{
			if (!await IsDispatcherAsync())
				return NotADispatcher();

			if (string.IsNullOrWhiteSpace(input?.ItemId) || input.CallId <= 0 || !await IsDepartmentCallAsync(input.CallId))
				return await RefusedAsync(DispatcherDeskErrors.InvalidInput);

			return Ok(await ItemActionResultAsync(await _routingService.LinkCallAsync(DepartmentId, input.ItemId, UserId, input.CallId, cancellationToken)));
		}

		/// <summary>Replies on the item's channel (SMS from the department number, email, chat where supported).</summary>
		[HttpPost("SendMessage")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<DispatcherDeskActionResult>> SendMessage(DispatcherMessageInput input, CancellationToken cancellationToken)
		{
			if (!await IsDispatcherAsync())
				return NotADispatcher();

			if (string.IsNullOrWhiteSpace(input?.ItemId) || string.IsNullOrWhiteSpace(input.Body) || input.Body.Length > DispatcherConversationMessage.BodyMaxLength)
				return await RefusedAsync(DispatcherDeskErrors.InvalidInput);

			return Ok(await MessageActionResultAsync(input.ItemId,
				await _conversationService.SendReplyAsync(DepartmentId, input.ItemId, UserId, input.Body.Trim(), cancellationToken)));
		}

		/// <summary>A desk note on the item (never sent to the sender).</summary>
		[HttpPost("AddNote")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<DispatcherDeskActionResult>> AddNote(DispatcherMessageInput input, CancellationToken cancellationToken)
		{
			if (!await IsDispatcherAsync())
				return NotADispatcher();

			if (string.IsNullOrWhiteSpace(input?.ItemId) || string.IsNullOrWhiteSpace(input.Body) || input.Body.Length > DispatcherConversationMessage.BodyMaxLength)
				return await RefusedAsync(DispatcherDeskErrors.InvalidInput);

			return Ok(await MessageActionResultAsync(input.ItemId,
				await _conversationService.AddNoteAsync(DepartmentId, input.ItemId, UserId, input.Body.Trim(), cancellationToken)));
		}

		/// <summary>Answer: rings the dispatcher's phone and, when they press 1, bridges them to the caller held for the item.</summary>
		[HttpPost("ConnectVoice")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<DispatcherDeskActionResult>> ConnectVoice(DispatcherVoiceInput input, CancellationToken cancellationToken)
		{
			if (!await IsDispatcherAsync())
				return NotADispatcher();

			if (string.IsNullOrWhiteSpace(input?.ItemId))
				return await RefusedAsync(DispatcherDeskErrors.InvalidInput);

			return Ok(await ItemActionResultAsync(await _voiceService.ConnectAsync(DepartmentId, input.ItemId, UserId, input.CallbackNumber, cancellationToken)));
		}

		/// <summary>Call back: rings the dispatcher's phone, then calls the item's caller back from the department number.</summary>
		[HttpPost("CallBack")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<DispatcherDeskActionResult>> CallBack(DispatcherVoiceInput input, CancellationToken cancellationToken)
		{
			if (!await IsDispatcherAsync())
				return NotADispatcher();

			if (string.IsNullOrWhiteSpace(input?.ItemId))
				return await RefusedAsync(DispatcherDeskErrors.InvalidInput);

			return Ok(await ItemActionResultAsync(await _voiceService.CallBackAsync(DepartmentId, input.ItemId, UserId, input.CallbackNumber, cancellationToken)));
		}

		#endregion Work

		#region Breaks

		/// <summary>
		/// Plans (or, with an id, re-plans) a break. Supervisors plan anyone's; a dispatcher plans their own on a shift that allows
		/// self-scheduled breaks. Overlaps with other dispatchers and coverage below the minimum come back as warnings.
		/// </summary>
		[HttpPost("SaveBreak")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<DispatcherDeskActionResult>> SaveBreak(DispatcherSaveBreakInput input, CancellationToken cancellationToken)
		{
			if (!await IsDispatcherAsync())
				return NotADispatcher();

			if (input == null || input.DurationMinutes <= 0 || !TryParseInstant(input.ScheduledStartOn, out var startUtc))
				return await RefusedAsync(DispatcherDeskErrors.InvalidInput);

			DispatcherBreak entity;
			if (!string.IsNullOrWhiteSpace(input.DispatcherBreakId))
			{
				entity = await _breaksService.GetBreakAsync(DepartmentId, input.DispatcherBreakId);
				if (entity == null || entity.DepartmentId != DepartmentId)
					return await RefusedAsync(DispatcherDeskErrors.BreakNotFound);

				if (entity.State != (int)DispatcherBreakStates.Scheduled)
					return await RefusedAsync(DispatcherDeskErrors.BreakNotStartable);
			}
			else
			{
				var userId = string.IsNullOrWhiteSpace(input.UserId) ? UserId : input.UserId;
				var window = await FindDutyWindowAsync(userId, input.ShiftDayId, startUtc);

				entity = new DispatcherBreak
				{
					DepartmentId = DepartmentId,
					UserId = userId,
					ShiftId = window?.ShiftId,
					ShiftDayId = window?.ShiftDayId ?? (input.ShiftDayId > 0 ? input.ShiftDayId : null),
					State = (int)DispatcherBreakStates.Scheduled,
					CreatedByUserId = UserId,
					CreatedOn = DateTime.UtcNow
				};
			}

			if (!await CanPlanBreakAsync(entity.UserId, entity.ShiftId))
				return await RefusedAsync(DispatcherDeskErrors.NotPermitted);

			entity.BreakType = Enum.IsDefined(typeof(DispatcherBreakTypes), input.BreakType) ? input.BreakType : (int)DispatcherBreakTypes.Break;
			entity.DurationMinutes = input.DurationMinutes;
			entity.ScheduledStartOn = startUtc;
			entity.Note = Trim(input.Note, 500);
			entity.UpdatedByUserId = UserId;
			entity.UpdatedOn = DateTime.UtcNow;

			return Ok(await BreakActionResultAsync(await _breaksService.SaveBreakAsync(DepartmentId, entity, UserId, cancellationToken)));
		}

		/// <summary>Removes a planned break (same rights as planning it).</summary>
		[HttpPost("CancelBreak")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<DispatcherDeskActionResult>> CancelBreak(DispatcherBreakIdInput input, CancellationToken cancellationToken)
		{
			if (!await IsDispatcherAsync())
				return NotADispatcher();

			var entity = string.IsNullOrWhiteSpace(input?.DispatcherBreakId) ? null : await _breaksService.GetBreakAsync(DepartmentId, input.DispatcherBreakId);
			if (entity == null || entity.DepartmentId != DepartmentId)
				return await RefusedAsync(DispatcherDeskErrors.BreakNotFound);

			if (!await CanPlanBreakAsync(entity.UserId, entity.ShiftId))
				return await RefusedAsync(DispatcherDeskErrors.NotPermitted);

			var result = await _breaksService.CancelBreakAsync(DepartmentId, entity.DispatcherBreakId, UserId, cancellationToken);
			var action = await ActionResultAsync(result);
			if (result?.Success == true)
			{
				entity.State = (int)DispatcherBreakStates.Cancelled;
				action.Break = await CommittedBreakDataAsync(entity);
			}

			return Ok(action);
		}

		/// <summary>
		/// Starts a break: a planned one, or one taken now (type + minutes). The dispatcher is due back the break's length after
		/// now. Held work goes back to the queue only with ReturnWork (otherwise HoldingWork). Someone else's break: supervisors.
		/// </summary>
		[HttpPost("StartBreak")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<DispatcherDeskActionResult>> StartBreak(DispatcherStartBreakInput input, CancellationToken cancellationToken)
		{
			if (!await IsDispatcherAsync())
				return NotADispatcher();

			input ??= new DispatcherStartBreakInput();
			var userId = string.IsNullOrWhiteSpace(input.UserId) ? UserId : input.UserId;
			if (!SameUser(userId, UserId) && !await _presenceService.CanSuperviseAsync(DepartmentId, UserId))
				return await RefusedAsync(DispatcherDeskErrors.NotPermitted);

			if (string.IsNullOrWhiteSpace(input.DispatcherBreakId) && (!input.DurationMinutes.HasValue || input.DurationMinutes.Value <= 0))
				return await RefusedAsync(DispatcherDeskErrors.InvalidInput);

			return Ok(await BreakActionResultAsync(await _breaksService.StartBreakAsync(DepartmentId, userId,
				string.IsNullOrWhiteSpace(input.DispatcherBreakId) ? null : input.DispatcherBreakId, input.BreakType, input.DurationMinutes, input.ReturnWork,
				UserId, cancellationToken)));
		}

		/// <summary>Ends a break and makes the dispatcher available: the caller coming back, or a supervisor ending someone else's.</summary>
		[HttpPost("EndBreak")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<DispatcherDeskActionResult>> EndBreak(DispatcherEndBreakInput input, CancellationToken cancellationToken)
		{
			if (!await IsDispatcherAsync())
				return NotADispatcher();

			var userId = string.IsNullOrWhiteSpace(input?.UserId) ? UserId : input.UserId;
			var self = SameUser(userId, UserId);
			if (!self && !await _presenceService.CanSuperviseAsync(DepartmentId, UserId))
				return await RefusedAsync(DispatcherDeskErrors.NotPermitted);

			return Ok(await BreakActionResultAsync(await _breaksService.EndBreakAsync(DepartmentId, userId, UserId,
				self ? DispatcherBreakEndReasons.Returned : DispatcherBreakEndReasons.Supervisor, cancellationToken)));
		}

		/// <summary>Staggers the shift's break plan across the day's dispatchers without overlap (supervisors).</summary>
		[HttpPost("PlanBreaks")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[Authorize(Policy = ResgridResources.Call_View)]
		public async Task<ActionResult<DispatcherDeskActionResult>> PlanBreaks(DispatcherPlanBreaksInput input, CancellationToken cancellationToken)
		{
			if (!await IsDispatcherAsync())
				return NotADispatcher();

			var (day, _) = await DepartmentShiftDayAsync(input?.ShiftDayId ?? 0);
			if (day == null)
				return await RefusedAsync(DispatcherDeskErrors.ShiftNotFound);

			if (!await _presenceService.CanSuperviseAsync(DepartmentId, UserId))
				return await RefusedAsync(DispatcherDeskErrors.NotPermitted);

			return Ok(await ActionResultAsync(await _breaksService.PlanBreaksAsync(DepartmentId, day.ShiftDayId, input.Replace, UserId, cancellationToken)));
		}

		#endregion Breaks

		#region Shift day rosters

		/// <summary>The caller signs up to dispatch on one day of a Signup shift (pending when the shift requires approval).</summary>
		[HttpPost("SignupForDay")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<ActionResult<DispatcherDeskActionResult>> SignupForDay(ShiftDayDispatcherInput input, CancellationToken cancellationToken)
		{
			if (!await IsDispatcherAsync())
				return NotADispatcher();

			var (day, _) = await DepartmentShiftDayAsync(input?.ShiftDayId ?? 0);
			if (day == null)
				return await RefusedAsync(DispatcherDeskErrors.ShiftNotFound);

			return Ok(await ActionResultAsync(await _shiftDispatchersService.SignupForDayAsync(DepartmentId, day.ShiftDayId, UserId, cancellationToken)));
		}

		/// <summary>The caller withdraws their own signup for a day (a standing assignment is for a shift manager to change).</summary>
		[HttpPost("WithdrawFromDay")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<ActionResult<DispatcherDeskActionResult>> WithdrawFromDay(ShiftDayDispatcherInput input, CancellationToken cancellationToken)
		{
			if (!await IsDispatcherAsync())
				return NotADispatcher();

			var (day, _) = await DepartmentShiftDayAsync(input?.ShiftDayId ?? 0);
			if (day == null)
				return await RefusedAsync(DispatcherDeskErrors.ShiftNotFound);

			var roster = await _shiftDispatchersService.GetDayRosterAsync(DepartmentId, day.ShiftDayId) ?? new List<DispatcherDutyWindow>();
			var mine = roster.Where(x => x != null && SameUser(x.UserId, UserId)).ToList();
			if (!mine.Any())
				return await RefusedAsync(DispatcherDeskErrors.InvalidInput);

			if (mine.Any(x => x.Source != (int)ShiftDispatcherSources.Signup))
				return await RefusedAsync(DispatcherDeskErrors.NotPermitted);

			// The source is rechecked inside the remove so a row a manager re-assigned after the read above is not withdrawn.
			return Ok(await ActionResultAsync(await _shiftDispatchersService.RemoveFromDayAsync(DepartmentId, day.ShiftDayId, UserId, UserId, Trim(input.Note, 500),
				(int)ShiftDispatcherSources.Signup, cancellationToken)));
		}

		/// <summary>A shift manager puts a dispatcher on one day of the shift (either shift type).</summary>
		[HttpPost("AssignToDay")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<ActionResult<DispatcherDeskActionResult>> AssignToDay(ShiftDayDispatcherInput input, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(input?.UserId))
				return await RefusedAsync(DispatcherDeskErrors.InvalidInput);

			var (day, shift) = await DepartmentShiftDayAsync(input.ShiftDayId);
			if (day == null)
				return await RefusedAsync(DispatcherDeskErrors.ShiftNotFound);

			if (!await CanManageShiftRosterAsync(shift))
				return await RefusedAsync(DispatcherDeskErrors.NotPermitted);

			return Ok(await ActionResultAsync(await _shiftDispatchersService.AssignToDayAsync(DepartmentId, day.ShiftDayId, input.UserId, input.IsSupervisor, UserId,
				cancellationToken)));
		}

		/// <summary>A shift manager takes a dispatcher off one day (a standing dispatcher is kept off that day only).</summary>
		[HttpPost("RemoveFromDay")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<ActionResult<DispatcherDeskActionResult>> RemoveFromDay(ShiftDayDispatcherInput input, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(input?.UserId))
				return await RefusedAsync(DispatcherDeskErrors.InvalidInput);

			var (day, shift) = await DepartmentShiftDayAsync(input.ShiftDayId);
			if (day == null)
				return await RefusedAsync(DispatcherDeskErrors.ShiftNotFound);

			if (!await CanManageShiftRosterAsync(shift))
				return await RefusedAsync(DispatcherDeskErrors.NotPermitted);

			return Ok(await ActionResultAsync(await _shiftDispatchersService.RemoveFromDayAsync(DepartmentId, day.ShiftDayId, input.UserId, UserId,
				Trim(input.Note, 500), cancellationToken)));
		}

		/// <summary>A shift manager approves or denies a pending dispatcher signup for a day.</summary>
		[HttpPost("ReviewDaySignup")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Shift_View)]
		public async Task<ActionResult<DispatcherDeskActionResult>> ReviewDaySignup(ShiftDispatcherReviewInput input, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(input?.ShiftDispatcherId))
				return await RefusedAsync(DispatcherDeskErrors.InvalidInput);

			var (day, shift) = await DepartmentShiftDayAsync(input.ShiftDayId);
			if (day == null)
				return await RefusedAsync(DispatcherDeskErrors.ShiftNotFound);

			if (!await CanManageShiftRosterAsync(shift))
				return await RefusedAsync(DispatcherDeskErrors.NotPermitted);

			// The row must be one of this day's (a reviewer of one shift never reaches another shift's signups by id).
			var roster = await _shiftDispatchersService.GetDayRosterAsync(DepartmentId, day.ShiftDayId) ?? new List<DispatcherDutyWindow>();
			if (!roster.Any(x => x != null && string.Equals(x.ShiftDispatcherId, input.ShiftDispatcherId, StringComparison.OrdinalIgnoreCase)))
				return await RefusedAsync(DispatcherDeskErrors.InvalidInput);

			return Ok(await ActionResultAsync(await _shiftDispatchersService.ReviewSignupAsync(DepartmentId, input.ShiftDispatcherId, input.Approve, UserId,
				Trim(input.Note, 500), cancellationToken)));
		}

		#endregion Shift day rosters

		#region Mapping

		/// <summary>ISO-8601 UTC, as every v4 desk instant ("2026-11-19T16:00:00Z").</summary>
		public static string Iso(DateTime utc) =>
			DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

		/// <summary>ISO-8601 UTC, or null.</summary>
		public static string Iso(DateTime? utc) => utc.HasValue ? Iso(utc.Value) : null;

		/// <summary>
		/// An ISO-8601 date and time (zone-less = UTC) as UTC; false for anything else or outside years 1900 to 9000. A time alone
		/// ("09:00") is refused: it would take today's date from the server clock (the RecurringCalls rule).
		/// </summary>
		public static bool TryParseInstant(string value, out DateTime utc)
		{
			utc = default;
			if (string.IsNullOrWhiteSpace(value) || !StartsWithIsoDateAndTime(value.Trim()) ||
				!DateTimeOffset.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) ||
				parsed.Year < 1900 || parsed.Year > 9000)
				return false;

			utc = DateTime.SpecifyKind(parsed.UtcDateTime, DateTimeKind.Utc);
			return true;
		}

		/// <summary>"yyyy-MM-ddTHH:mm" at the start (seconds, fraction and zone may follow).</summary>
		private static bool StartsWithIsoDateAndTime(string value)
		{
			const string pattern = "dddd-dd-ddTdd:dd";
			if (value.Length < pattern.Length)
				return false;

			for (var i = 0; i < pattern.Length; i++)
			{
				var ok = pattern[i] == 'd' ? value[i] >= '0' && value[i] <= '9' : value[i] == pattern[i];
				if (!ok)
					return false;
			}

			return true;
		}

		/// <summary>Messages received since the holder last replied (desk notes never count).</summary>
		public static int UnreadCount(IEnumerable<DispatcherConversationMessage> messages)
		{
			var ordered = (messages ?? Enumerable.Empty<DispatcherConversationMessage>()).Where(x => x != null).OrderBy(x => x.CreatedOn).ToList();
			var lastReply = ordered.LastOrDefault(x => x.Direction == (int)DispatcherMessageDirections.Outbound);

			return ordered.Count(x => x.Direction == (int)DispatcherMessageDirections.Inbound && (lastReply == null || x.CreatedOn > lastReply.CreatedOn));
		}

		/// <summary>A held phone caller is still on the line: an open voice item with a live leg and no VoiceEnded on the caller's own leg.</summary>
		public static bool IsCallerWaiting(DispatcherQueueItem item, IEnumerable<DispatcherQueueEvent> events) =>
			item != null && item.Kind == (int)DispatcherQueueItemKinds.Voice && item.IsOpen() && !string.IsNullOrWhiteSpace(item.ExternalReference) &&
			DispatcherVoiceController.CallerWaiting(events);

		private static bool IsConversation(DispatcherQueueItem item) =>
			item.Kind == (int)DispatcherQueueItemKinds.Text || item.Kind == (int)DispatcherQueueItemKinds.Email ||
			item.Kind == (int)DispatcherQueueItemKinds.Chat || item.Kind == (int)DispatcherQueueItemKinds.Other;

		private static bool IsPhoneNumber(string value)
		{
			if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsLetter) || value.Contains('@'))
				return false;

			return value.Count(char.IsDigit) >= 7;
		}

		/// <summary>An intake text thread takes replies from the dispatcher holding the item while it is open and the texter has not opted out.</summary>
		private bool CanReplyOnIntakeThread(DispatcherQueueItem item, IntakeConversationView thread) =>
			item.IsOpen() && item.State == (int)DispatcherQueueItemStates.Accepted && SameUser(item.AssignedUserId, UserId) &&
			thread?.Conversation != null && thread.Conversation.IsOpen && !thread.Conversation.OptedOut;

		private async Task<DispatcherIntakeThreadData> ToIntakeThreadDataAsync(IntakeConversationView thread)
		{
			var conversation = thread?.Conversation;
			if (conversation == null || conversation.DepartmentId != DepartmentId)
				return null;

			var data = new DispatcherIntakeThreadData
			{
				IntakeConversationId = conversation.IntakeConversationId,
				State = conversation.State,
				Handler = conversation.Handler,
				AssignedUserId = conversation.AssignedUserId,
				AssignedUserName = await NameForAsync(conversation.AssignedUserId),
				OptedOut = conversation.OptedOut,
				ParticipantName = conversation.ParticipantName,
				ReplyFromAddress = conversation.ReplyFromAddress,
				StartedOn = Iso(conversation.StartedOn),
				ClosedOn = Iso(conversation.ClosedOn),
				CloseReason = conversation.CloseReason
			};

			foreach (var message in (thread.Messages ?? new List<IntakeConversationEntry>()).Where(x => x != null).OrderBy(x => x.CreatedOn))
				data.Messages.Add(new DispatcherIntakeMessageData
				{
					MessageId = message.MessageId,
					Direction = message.Direction,
					Author = message.Author,
					AuthorUserId = message.AuthorUserId,
					AuthorName = message.AuthorName,
					Text = message.Text,
					Status = message.Status,
					CreatedOn = Iso(message.CreatedOn)
				});

			return data;
		}

		private static bool CanReply(DispatcherQueueItem item)
		{
			if (!item.IsOpen() || string.IsNullOrWhiteSpace(item.SourceAddress))
				return false;

			switch ((DispatcherQueueItemKinds)item.Kind)
			{
				case DispatcherQueueItemKinds.Text:
					return IsPhoneNumber(item.SourceAddress);
				case DispatcherQueueItemKinds.Email:
					return item.SourceAddress.Contains('@');
				// A chat from an intake chat app (Slack, WhatsApp, ...) has no desk reply path: the conversation service refuses it, so the
				// composer is not offered (Omni and the web desk read this flag).
				case DispatcherQueueItemKinds.Chat:
					return false;
				case DispatcherQueueItemKinds.Other:
					return true;
				default:
					return false;
			}
		}

		/// <summary>Answer (a held caller is waiting) or Call back (a text or phone call with a phone number to ring back).</summary>
		private static bool CanConnectVoice(DispatcherQueueItem item, IEnumerable<DispatcherQueueEvent> events) =>
			item.IsOpen() && (IsCallerWaiting(item, events) ||
				DispatcherRoutingPolicy.CanCallBack((DispatcherQueueItemKinds)item.Kind) && IsPhoneNumber(item.SourceAddress));

		private async Task<List<DispatcherQueueItemData>> ToItemsDataAsync(IEnumerable<DispatcherQueueItem> items)
		{
			var list = new List<DispatcherQueueItemData>();
			var calls = new Dictionary<int, Call>();

			foreach (var item in items ?? Enumerable.Empty<DispatcherQueueItem>())
			{
				if (item == null)
					continue;

				List<DispatcherQueueEvent> events = null;
				List<DispatcherConversationMessage> messages = null;
				Call call = null;
				var snapshot = item;

				if (IsConversation(item) || item.Kind == (int)DispatcherQueueItemKinds.Voice)
				{
					// Conversation counts and the held caller's state come from the item's history. The row then takes everything
					// (its call link included) from that later read, so a call linked in between never shows beside a null CallId.
					var detail = await _routingService.GetItemDetailAsync(DepartmentId, item.DispatcherQueueItemId);
					snapshot = detail?.Item != null && detail.Item.DepartmentId == DepartmentId ? detail.Item : item;
					if (!snapshot.IsOpen())
						continue; // finished between the two reads: no longer open work

					events = detail?.Events;
					messages = detail?.Messages;
					call = detail?.Call;
					if (call != null)
						calls[call.CallId] = call;
				}

				if (call == null && snapshot.CallId.HasValue)
				{
					if (!calls.TryGetValue(snapshot.CallId.Value, out call))
					{
						call = await _callsService.GetCallByIdAsync(snapshot.CallId.Value);
						if (call != null && call.DepartmentId != DepartmentId)
							call = null;

						calls[snapshot.CallId.Value] = call;
					}
				}

				list.Add(await ToItemDataAsync(snapshot, call, events, messages));
			}

			return list;
		}

		private async Task<DispatcherQueueItemData> ToItemDataAsync(DispatcherQueueItem item, Call call, IEnumerable<DispatcherQueueEvent> events,
			IEnumerable<DispatcherConversationMessage> messages)
		{
			var conversation = (messages ?? Enumerable.Empty<DispatcherConversationMessage>()).Where(x => x != null).ToList();
			call = call != null && call.DepartmentId == DepartmentId ? call : null;

			return new DispatcherQueueItemData
			{
				DispatcherQueueItemId = item.DispatcherQueueItemId,
				Kind = item.Kind,
				State = item.State,
				Outcome = item.Outcome,
				CallId = item.CallId,
				CallNumber = call?.Number,
				CallName = call?.Name,
				CallPriority = call?.Priority,
				CallPriorityColor = call != null ? await _callsService.CallPriorityToColorAsync(call.Priority, DepartmentId) : null,
				Priority = item.Priority,
				SourceAddress = item.SourceAddress,
				SourceName = item.SourceName,
				TargetAddress = item.TargetAddress,
				Subject = item.Subject,
				Preview = item.Preview,
				AssignedUserId = item.AssignedUserId,
				AssignedUserName = await NameForAsync(item.AssignedUserId),
				OfferedOn = Iso(item.OfferedOn),
				OfferExpiresOn = Iso(item.OfferExpiresOn),
				OfferAttempts = item.OfferAttempts,
				AcceptedOn = Iso(item.AcceptedOn),
				CompletedOn = Iso(item.CompletedOn),
				DueOn = Iso(item.DueOn),
				CreatedOn = Iso(item.CreatedOn),
				LastActivityOn = Iso(item.LastActivityOn),
				OverflowedOn = Iso(item.OverflowedOn),
				OverflowNote = item.OverflowNote,
				EscalatedOn = Iso(item.EscalatedOn),
				Note = item.Note,
				CallerWaiting = IsCallerWaiting(item, events),
				IntakeConversationId = DispatcherRoutingPolicy.IntakeConversationIdOf(item),
				MessageCount = conversation.Count(x => x.Direction == (int)DispatcherMessageDirections.Inbound || x.Direction == (int)DispatcherMessageDirections.Outbound),
				UnreadCount = UnreadCount(conversation)
			};
		}

		private async Task<DispatcherConversationMessageData> ToMessageDataAsync(DispatcherConversationMessage message) =>
			ToMessageData(message, await NameForAsync(message.UserId));

		private static DispatcherConversationMessageData ToMessageData(DispatcherConversationMessage message, string userName)
		{
			return new DispatcherConversationMessageData
			{
				DispatcherConversationMessageId = message.DispatcherConversationMessageId,
				Direction = message.Direction,
				FromAddress = message.FromAddress,
				ToAddress = message.ToAddress,
				Body = message.Body,
				UserId = message.UserId,
				UserName = userName,
				CreatedOn = Iso(message.CreatedOn),
				DeliveryState = message.DeliveryState,
				Error = message.Error
			};
		}

		private async Task<DispatcherDeskMemberData> ToMemberDataAsync(DispatcherDeskMember member, bool canSupervise)
		{
			var presence = member.Presence;
			var self = SameUser(member.UserId, UserId);

			return new DispatcherDeskMemberData
			{
				UserId = member.UserId,
				Name = member.Name ?? await NameForAsync(member.UserId),
				DisplayState = (int)member.DisplayState,
				IsSignedIn = presence?.IsSignedIn() == true,
				IsOnDuty = member.IsOnDuty,
				IsSupervisor = member.IsSupervisor,
				DutyMode = presence?.DutyMode ?? (int)DispatcherDutyModes.Scheduled,
				Duty = member.Duty != null ? ToDutyData(member.Duty) : null,
				DutyEndsOn = Iso(presence != null && presence.IsSignedIn() ? presence.DutyEndsOn ?? member.Duty?.EndUtc : member.Duty?.EndUtc),
				SignedInOn = Iso(presence?.SignedInOn),
				StateChangedOn = Iso(presence?.StateChangedOn),
				LastSeenOn = Iso(presence?.LastSeenOn),
				AwayReason = presence?.AwayReason,
				// A dispatcher's own phone number is theirs: other members never see it.
				CallbackNumber = self ? presence?.CallbackNumber : null,
				DeviceKind = presence?.DeviceKind,
				ActiveBreak = member.ActiveBreak != null ? await ToBreakDataAsync(member.ActiveBreak) : null,
				NextBreak = member.NextBreak != null ? await ToBreakDataAsync(member.NextBreak) : null,
				ActiveItemCount = member.ActiveItemCount,
				OfferedItemCount = member.OfferedItemCount,
				Capacity = member.Capacity,
				OverdueMinutes = member.OverdueMinutes,
				CanSupervise = canSupervise
			};
		}

		private async Task<DispatcherBreakData> ToBreakDataAsync(DispatcherBreak dispatcherBreak) =>
			ToBreakData(dispatcherBreak, await NameForAsync(dispatcherBreak.UserId));

		private static DispatcherBreakData ToBreakData(DispatcherBreak dispatcherBreak, string userName)
		{
			return new DispatcherBreakData
			{
				DispatcherBreakId = dispatcherBreak.DispatcherBreakId,
				UserId = dispatcherBreak.UserId,
				UserName = userName,
				ShiftId = dispatcherBreak.ShiftId,
				ShiftDayId = dispatcherBreak.ShiftDayId,
				BreakType = dispatcherBreak.BreakType,
				State = dispatcherBreak.State,
				ScheduledStartOn = Iso(dispatcherBreak.ScheduledStartOn),
				ScheduledEndOn = Iso(dispatcherBreak.ScheduledEndOn()),
				DurationMinutes = dispatcherBreak.DurationMinutes,
				StartedOn = Iso(dispatcherBreak.StartedOn),
				ExpectedReturnOn = Iso(dispatcherBreak.ExpectedReturnOn),
				EndedOn = Iso(dispatcherBreak.EndedOn),
				EndReason = dispatcherBreak.EndReason,
				Note = dispatcherBreak.Note
			};
		}

		/// <summary>A duty window as the v4 model.</summary>
		public static DispatcherDutyData ToDutyData(DispatcherDutyWindow window)
		{
			return new DispatcherDutyData
			{
				UserId = window.UserId,
				ShiftId = window.ShiftId,
				ShiftDayId = window.ShiftDayId,
				ShiftName = window.ShiftName,
				ShiftColor = window.ShiftColor,
				Day = window.Day == default ? null : window.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
				StartUtc = Iso(window.StartUtc),
				EndUtc = Iso(window.EndUtc),
				IsSupervisor = window.IsSupervisor,
				Source = window.Source,
				IsPending = window.IsPending,
				ShiftDispatcherId = window.ShiftDispatcherId
			};
		}

		/// <summary>A desk warning as the v4 model (kind by name).</summary>
		public static DispatcherDeskWarningData ToWarningData(DispatcherDeskWarning warning)
		{
			return new DispatcherDeskWarningData
			{
				Kind = warning.Kind.ToString(),
				StartUtc = Iso(warning.StartUtc),
				EndUtc = Iso(warning.EndUtc),
				UserIds = (warning.UserIds ?? new List<string>()).ToList(),
				Available = warning.Available,
				Minimum = warning.Minimum,
				Minutes = warning.Minutes
			};
		}

		/// <summary>The part of the desk config the app needs, with the limits clamped as the desk applies them.</summary>
		public static DispatcherDeskConfigData ToConfigData(DispatcherDeskConfig config)
		{
			config ??= new DispatcherDeskConfig();

			return new DispatcherDeskConfigData
			{
				Enabled = config.Enabled,
				RequireShift = config.RequireShift,
				AllowUnscheduledDuty = config.AllowUnscheduledDuty,
				MaxActiveItemsPerDispatcher = config.EffectiveMaxActiveItems(),
				OfferTimeoutSeconds = config.EffectiveOfferTimeoutSeconds(),
				BreakReminderMinutes = config.EffectiveBreakReminderMinutes(),
				AutoReturnAfterMinutes = config.EffectiveAutoReturnAfterMinutes(),
				MinimumAvailableDispatchers = config.EffectiveMinimumAvailable(),
				PreventBreakBelowMinimum = config.PreventBreakBelowMinimum,
				SuppressAlertsOnBreak = config.SuppressAlertsOnBreak,
				RouteText = config.RouteText,
				RouteVoice = config.RouteVoice,
				RouteEmail = config.RouteEmail,
				RouteChat = config.RouteChat,
				RoutePendingCalls = config.RoutePendingCalls,
				RouteScheduledCalls = config.RouteScheduledCalls,
				EmailCallMode = (int)config.GetEmailCallMode(),
				DefaultBreakPlan = config.GetDefaultBreakPlan().Select(ToBreakPlanData).ToList()
			};
		}

		private static DispatcherBreakPlanEntryData ToBreakPlanData(DispatcherBreakPlanEntry entry) => new DispatcherBreakPlanEntryData
		{
			BreakType = entry.BreakType,
			DurationMinutes = entry.DurationMinutes,
			TargetOffsetMinutes = entry.TargetOffsetMinutes
		};

		#endregion Mapping

		#region Helpers

		private Task<bool> IsDispatcherAsync() => _dispatchAccessService.CanUseDispatchAsync(DepartmentId, UserId);

		private ActionResult Forbidden() => StatusCode(StatusCodes.Status403Forbidden);

		/// <summary>403 with a desk body, so the app reads it as the NotADispatcher refusal.</summary>
		private ActionResult NotADispatcher()
		{
			var result = new DispatcherDeskActionResult { Success = false, Error = DispatcherDeskErrors.NotADispatcher.ToString(), Status = ResponseHelper.Failure };
			ResponseHelper.PopulateV4ResponseData(result);

			return StatusCode(StatusCodes.Status403Forbidden, result);
		}

		private async Task<ActionResult> RefusedAsync(DispatcherDeskErrors error) =>
			Ok(await ActionResultAsync(DispatcherDeskResult<bool>.Fail(error)));

		/// <summary>
		/// The action's outcome with the caller's refreshed desk member. The outcome is already committed (a reply may be sent), so a
		/// failed refresh read leaves Me unset rather than failing the request: a client that saw an error would repeat the action.
		/// </summary>
		private async Task<DispatcherDeskActionResult> ActionResultAsync<T>(DispatcherDeskResult<T> result)
		{
			result ??= DispatcherDeskResult<T>.Fail(DispatcherDeskErrors.InvalidInput);

			var action = new DispatcherDeskActionResult
			{
				Success = result.Success,
				Error = result.Success ? string.Empty : result.Error.ToString(),
				Warnings = (result.Warnings ?? new List<DispatcherDeskWarning>()).Where(x => x != null).Select(ToWarningData).ToList()
			};

			try
			{
				var me = await _presenceService.GetMemberAsync(DepartmentId, UserId, DateTime.UtcNow);
				if (me != null)
					action.Me = await ToMemberDataAsync(me, await _presenceService.CanSuperviseAsync(DepartmentId, UserId));
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Dispatcher desk: refreshing the caller's desk member after an action");
			}

			action.Status = result.Success ? ResponseHelper.Success : ResponseHelper.Failure;
			ResponseHelper.PopulateV4ResponseData(action);

			return action;
		}

		/// <summary>The item as it is after the action, or as the action returned it when the refresh read fails (supplemental, like Me).</summary>
		private async Task<DispatcherQueueItemData> RefreshedItemDataAsync(DispatcherQueueItem fallback)
		{
			DispatcherQueueItemDetail detail = null;
			try
			{
				detail = await _routingService.GetItemDetailAsync(DepartmentId, fallback.DispatcherQueueItemId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Dispatcher desk: refreshing item {fallback.DispatcherQueueItemId} after an action");
			}

			try
			{
				return await ToItemDataAsync(detail?.Item ?? fallback, detail?.Call, detail?.Events, detail?.Messages);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Dispatcher desk: mapping item {fallback.DispatcherQueueItemId} after an action");
				return null;
			}
		}

		private async Task<DispatcherDeskActionResult> ItemActionResultAsync(DispatcherDeskResult<DispatcherQueueItem> result)
		{
			var action = await ActionResultAsync(result);
			if (result?.Value != null)
				action.Item = await RefreshedItemDataAsync(result.Value);

			return action;
		}

		private async Task<DispatcherDeskActionResult> BreakActionResultAsync(DispatcherDeskResult<DispatcherBreak> result)
		{
			var action = await ActionResultAsync(result);
			if (result?.Value != null)
				action.Break = await CommittedBreakDataAsync(result.Value);

			return action;
		}

		/// <summary>
		/// A break an action already stored, as the v4 model: a failed name lookup leaves UserName empty instead of turning the stored
		/// change into an error the app would repeat (supplemental, like Me).
		/// </summary>
		private async Task<DispatcherBreakData> CommittedBreakDataAsync(DispatcherBreak dispatcherBreak)
		{
			string userName = null;
			try
			{
				userName = await NameForAsync(dispatcherBreak.UserId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Dispatcher desk: naming the dispatcher of break {dispatcherBreak.DispatcherBreakId} after an action");
			}

			return ToBreakData(dispatcherBreak, userName);
		}

		private async Task<DispatcherDeskActionResult> MessageActionResultAsync(string itemId, DispatcherDeskResult<DispatcherConversationMessage> result)
		{
			var action = await ActionResultAsync(result);
			if (result?.Value != null)
			{
				try
				{
					action.Message = await ToMessageDataAsync(result.Value);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Dispatcher desk: naming the sender of message {result.Value.DispatcherConversationMessageId}");
					action.Message = ToMessageData(result.Value, null);
				}
			}

			if (result?.Success == true)
			{
				// The reply is already sent: a failed refresh read must not turn it into an error the app would retry.
				try
				{
					var detail = await _routingService.GetItemDetailAsync(DepartmentId, itemId);
					if (detail?.Item != null)
						action.Item = await ToItemDataAsync(detail.Item, detail.Call, detail.Events, detail.Messages);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Dispatcher desk: refreshing item {itemId} after a message");
				}
			}

			return action;
		}

		/// <summary>Null when the caller holds (or was offered) the item or may supervise; otherwise why it is refused.</summary>
		private async Task<DispatcherDeskErrors?> CheckHolderOrSupervisorAsync(string itemId)
		{
			if (string.IsNullOrWhiteSpace(itemId))
				return DispatcherDeskErrors.InvalidInput;

			var detail = await _routingService.GetItemDetailAsync(DepartmentId, itemId);
			if (detail?.Item == null || detail.Item.DepartmentId != DepartmentId)
				return DispatcherDeskErrors.ItemNotFound;

			if (SameUser(detail.Item.AssignedUserId, UserId) || await _presenceService.CanSuperviseAsync(DepartmentId, UserId))
				return null;

			return DispatcherDeskErrors.NotAssignedToYou;
		}

		/// <summary>Supervisors plan anyone's breaks; a dispatcher plans their own on a shift that allows self-scheduled breaks.</summary>
		private async Task<bool> CanPlanBreakAsync(string userId, int? shiftId)
		{
			if (await _presenceService.CanSuperviseAsync(DepartmentId, UserId))
				return true;

			if (!SameUser(userId, UserId) || !shiftId.HasValue)
				return false;

			var settings = await _shiftDispatchersService.GetSettingsForShiftAsync(shiftId.Value);
			return settings != null && settings.DepartmentId == DepartmentId && settings.IsEnabled && settings.AllowSelfScheduledBreaks;
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

		/// <summary>The shift day and its shift, when both are the department's.</summary>
		private async Task<(ShiftDay Day, Shift Shift)> DepartmentShiftDayAsync(int shiftDayId)
		{
			if (shiftDayId <= 0)
				return (null, null);

			var day = await _shiftsService.GetShiftDayByIdAsync(shiftDayId);
			var shift = day != null ? await _shiftsService.GetShiftByIdAsync(day.ShiftId) : null;

			return shift == null || shift.DepartmentId != DepartmentId ? (null, null) : (day, shift);
		}

		/// <summary>The shift day has ended in department-local time (the boundary ShiftDispatchersService.SignupForDayAsync refuses at).</summary>
		private async Task<bool> IsShiftDayOverAsync(ShiftDay day, Shift shift)
		{
			_department ??= await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var (_, endLocal) = ShiftTimeWindow.GetWindow(day.Day, shift.StartTime, shift.EndTime, shift.Hours);

			return DispatcherDutyCalculator.ToUtc(endLocal, _department) <= DateTime.UtcNow;
		}

		/// <summary>The dispatch roster is the whole shift's (no group), so it takes the whole shift's management scope.</summary>
		private async Task<bool> CanManageShiftRosterAsync(Shift shift)
		{
			var scope = await _authorizationService.GetShiftManagementScopeAsync(UserId, DepartmentId) ?? ShiftManagementScope.None();
			return scope.CanManageShiftGroup(shift, null);
		}

		private async Task<bool> IsDepartmentCallAsync(int callId)
		{
			var call = await _callsService.GetCallByIdAsync(callId);
			return call != null && call.DepartmentId == DepartmentId;
		}

		/// <summary>The requested window, or today's department-local day; null when it is not valid.</summary>
		private async Task<(DateTime FromUtc, DateTime ToUtc)?> WindowAsync(string from, string to, int maxDays)
		{
			DateTime fromUtc, toUtc;
			if (string.IsNullOrWhiteSpace(from) && string.IsNullOrWhiteSpace(to))
			{
				_department ??= await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
				var localToday = DateTime.SpecifyKind(DateTime.UtcNow.TimeConverter(_department).Date, DateTimeKind.Unspecified);
				fromUtc = DateTime.SpecifyKind(localToday.DepartmentLocalToUtc(_department), DateTimeKind.Utc);
				toUtc = DateTime.SpecifyKind(localToday.AddDays(1).DepartmentLocalToUtc(_department), DateTimeKind.Utc);
			}
			else if (!TryParseInstant(from, out fromUtc) || !TryParseInstant(to, out toUtc))
			{
				return null;
			}

			if (toUtc <= fromUtc || (toUtc - fromUtc).TotalDays > maxDays)
				return null;

			return (fromUtc, toUtc);
		}

		private async Task<string> NameForAsync(string userId)
		{
			if (string.IsNullOrWhiteSpace(userId))
				return null;

			_profiles ??= await _userProfileService.GetAllProfilesForDepartmentAsync(DepartmentId) ?? new Dictionary<string, UserProfile>();

			return _profiles.TryGetValue(userId, out var profile) ? profile?.FullName?.AsFirstNameLastName : null;
		}

		private static bool IsValidDevice(DispatcherDeviceInput input) =>
			input != null && input.Platform >= 1 && input.Platform <= 3 && !string.IsNullOrWhiteSpace(input.Token) && input.Token.Length <= 4096;

		private static bool SameUser(string a, string b) => !string.IsNullOrWhiteSpace(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

		private static string Trim(string value, int max)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;

			var trimmed = value.Trim();
			return trimmed.Length <= max ? trimmed : trimmed.Substring(0, max);
		}

		#endregion Helpers
	}
}
