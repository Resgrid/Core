using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Twilio;
using Twilio.AspNet.Core;
using Twilio.TwiML;
using Twilio.TwiML.Voice;

namespace Resgrid.Web.Services.Controllers
{
	/// <summary>
	/// Twilio webhooks of the dispatcher desk voice flow (plan D8). An unknown caller to a department number is held in the item's
	/// own queue (<see cref="Hold"/>) until a dispatcher taps Answer; the desk rings the dispatcher's phone
	/// (<see cref="DispatcherConnect"/>), asks them to press 1, and bridges them to the caller with <c>&lt;Dial&gt;&lt;Queue&gt;</c>.
	/// A caller nobody can take leaves for the overflow handling (voicemail, which becomes a pending call); a caller who hangs up
	/// stays on the desk for Call back (<see cref="CallBack"/>). Paths: <see cref="DispatcherVoiceRoutes"/>.
	/// </summary>
	[ApiController]
	[Route(DispatcherVoiceRoutes.RoutePrefix)]
	[Produces("application/xml")]
	[ApiExplorerSettings(IgnoreApi = true)]
	public class DispatcherVoiceController : ControllerBase
	{
		/// <summary>Seconds of hold between re-checks of the held caller's item (Twilio re-requests the wait URL when it ends).</summary>
		public const int HoldPauseSeconds = 10;

		/// <summary>The longest a caller holds while an offer is out or a dispatcher is being rung, whatever the hold setting.</summary>
		public const int MaxHoldSeconds = 600;

		/// <summary>Voicemail length; Twilio transcribes recordings of up to two minutes.</summary>
		public const int VoicemailMaxSeconds = 120;

		/// <summary>Seconds the dispatcher has to press 1.</summary>
		public const int DispatcherGatherSeconds = 8;

		/// <summary>Seconds a call back rings the caller.</summary>
		public const int CallBackRingSeconds = 30;

		/// <summary>How often a transcription that arrived before its voicemail call was linked re-reads the item (within Twilio's 15 s).</summary>
		public const int TranscriptionLinkChecks = 4;

		private static readonly TimeSpan TtsPromptBudget = TimeSpan.FromSeconds(6);

		/// <summary>The pause between those re-reads (tests set zero).</summary>
		public TimeSpan TranscriptionLinkCheckInterval { get; set; } = TimeSpan.FromSeconds(1);

		private readonly IDispatcherQueueItemsRepository _dispatcherQueueItemsRepository;
		private readonly IDispatcherRoutingService _dispatcherRoutingService;
		private readonly IDispatcherVoiceService _dispatcherVoiceService;
		private readonly IDispatcherDeskConfigService _dispatcherDeskConfigService;
		private readonly IDispatcherDeskPendingCalls _pendingCalls;
		private readonly ICallsService _callsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly ITwilioVoiceResponseService _twilioVoiceResponseService;

		private Dictionary<string, string> _fields;
		private CancellationTokenSource _ttsBudgetCts;

		public DispatcherVoiceController(IDispatcherQueueItemsRepository dispatcherQueueItemsRepository, IDispatcherRoutingService dispatcherRoutingService,
			IDispatcherVoiceService dispatcherVoiceService, IDispatcherDeskConfigService dispatcherDeskConfigService, IDispatcherDeskPendingCalls pendingCalls,
			ICallsService callsService, IDepartmentsService departmentsService, IDepartmentSettingsService departmentSettingsService,
			ITwilioVoiceResponseService twilioVoiceResponseService)
		{
			_dispatcherQueueItemsRepository = dispatcherQueueItemsRepository;
			_dispatcherRoutingService = dispatcherRoutingService;
			_dispatcherVoiceService = dispatcherVoiceService;
			_dispatcherDeskConfigService = dispatcherDeskConfigService;
			_pendingCalls = pendingCalls;
			_callsService = callsService;
			_departmentsService = departmentsService;
			_departmentSettingsService = departmentSettingsService;
			_twilioVoiceResponseService = twilioVoiceResponseService;
		}

		#region The held caller

		/// <summary>
		/// The held caller's wait URL, re-requested by Twilio each time its TwiML ends: a hold prompt and a pause, or
		/// <c>&lt;Leave/&gt;</c> when the item overflowed, was closed, or has waited past the department's hold time with nobody
		/// taking it (then the item is marked overflowed). Leaving runs <see cref="EnqueueAction"/>, which plays the overflow.
		/// </summary>
		[HttpGet(DispatcherVoiceRoutes.Hold)]
		[HttpPost(DispatcherVoiceRoutes.Hold)]
		[ValidateRequest]
		public async Task<ActionResult> Hold([FromQuery] string itemId)
		{
			await LoadFieldsAsync();

			var response = new VoiceResponse();
			var item = await CallerItemAsync(itemId);

			if (item == null || await LeaveHoldAsync(item))
			{
				response.Leave();
				return Twiml(response);
			}

			var queueTime = ParseInt(Field("QueueTime")) ?? 0;
			await AppendPromptAsync(response, queueTime < HoldPauseSeconds ? DispatcherDeskVoicePrompts.HoldGreeting : DispatcherDeskVoicePrompts.HoldContinue,
				item.DepartmentId);
			response.Pause(HoldPauseSeconds);

			return Twiml(response);
		}

		/// <summary>
		/// How the held caller left the queue (Twilio's QueueResult): hung up (the item stays open for Call back), bridged to a
		/// dispatcher and now done, or left / failed (the overflow handling plays).
		/// </summary>
		[HttpGet(DispatcherVoiceRoutes.EnqueueAction)]
		[HttpPost(DispatcherVoiceRoutes.EnqueueAction)]
		[ValidateRequest]
		public async Task<ActionResult> EnqueueAction([FromQuery] string itemId)
		{
			await LoadFieldsAsync();

			var response = new VoiceResponse();
			var item = await CallerItemAsync(itemId);
			var queueResult = (Field("QueueResult") ?? string.Empty).Trim().ToLowerInvariant();

			// Each held caller has a queue of their own, now empty: Twilio keeps queues until deleted.
			await _dispatcherVoiceService.ReleaseQueueAsync(Field("QueueSid"));

			switch (queueResult)
			{
				case "hangup":
					await RecordVoiceEventAsync(item, DispatcherQueueEventTypes.VoiceEnded, null, "Caller hung up while holding");
					return Twiml(response);

				case "bridged":
					await RecordVoiceEventAsync(item, DispatcherQueueEventTypes.VoiceEnded, null, "Call ended");
					response.Hangup();
					return Twiml(response);

				case "bridging-in-process":
				case "redirected":
					// The bridge or the redirect target owns the call now.
					return Twiml(response);

				default:
					// leave, queue-full, error, system-error: never drop a caller, take a message.
					if (item == null)
						return await NoDispatcherAvailableAsync(null);

					await MarkOverflowedAsync(item, queueResult == "leave" ? "No dispatcher could take the call: voicemail" : $"Hold failed ({queueResult}): voicemail");
					return await OverflowTwimlAsync(item);
			}
		}

		/// <summary>
		/// The overflow handling for a caller no dispatcher can take: the voicemail (Enhanced AI voice intake is not in this build,
		/// so that setting takes a message too). Reached by a redirect from the inbound call or the voice service.
		/// </summary>
		[HttpGet(DispatcherVoiceRoutes.Overflow)]
		[HttpPost(DispatcherVoiceRoutes.Overflow)]
		[ValidateRequest]
		public async Task<ActionResult> Overflow([FromQuery] string itemId)
		{
			await LoadFieldsAsync();

			var item = await CallerItemAsync(itemId);
			if (item == null)
				return await NoDispatcherAvailableAsync(null);

			await MarkOverflowedAsync(item, "No dispatcher free: voicemail");
			return await OverflowTwimlAsync(item);
		}

		/// <summary>
		/// The voicemail's record action: the message becomes a pending call (recording in its notes) and the overflowed voice item
		/// becomes that call, waiting in the queue again for a dispatcher (Call back works from it). A repeat does nothing more. The
		/// caller hears that the message was passed on only once it is held as a pending call; otherwise they are asked to call back,
		/// and the recording is kept in the item's history for the dispatchers.
		/// </summary>
		[HttpGet(DispatcherVoiceRoutes.Voicemail)]
		[HttpPost(DispatcherVoiceRoutes.Voicemail)]
		[ValidateRequest]
		public async Task<ActionResult> Voicemail([FromQuery] string itemId)
		{
			await LoadFieldsAsync();

			var response = new VoiceResponse();
			var item = await CallerItemAsync(itemId);
			var recordingUrl = Field("RecordingUrl");

			// An item that already is its voicemail call (a repeat of this request) has nothing more to save.
			var durable = item?.CallId != null;
			if (item != null && !item.CallId.HasValue && !string.IsNullOrWhiteSpace(recordingUrl))
			{
				try
				{
					// The caller is done with the line: the reopened item must not read as a caller still waiting.
					await RecordVoiceEventAsync(item, DispatcherQueueEventTypes.VoiceEnded, null, "Caller left a voicemail");
					var result = await _pendingCalls.FromVoicemailAsync(item, item.SourceAddress ?? DeskPhoneNumbers.ToE164(Field("From")), recordingUrl,
						ParseInt(Field("RecordingDuration")), Field("RecordingSid"));
					durable = result != null && result.Durable;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Dispatcher desk: the voicemail for item {itemId} could not be made a pending call.");
				}

				if (!durable)
					await RecordVoiceEventAsync(item, DispatcherQueueEventTypes.VoiceEnded, null, $"The voicemail could not be saved as a call. Recording: {recordingUrl}");
			}
			else if (item == null && !string.IsNullOrWhiteSpace(recordingUrl))
			{
				Logging.LogError($"[Dispatcher desk] Voicemail for unknown item {itemId} could not be kept: {recordingUrl}");
			}

			await AppendPromptAsync(response, durable ? DispatcherDeskVoicePrompts.VoicemailThanks : DispatcherDeskVoicePrompts.VoicemailNotSaved, item?.DepartmentId);
			response.Hangup();

			return Twiml(response);
		}

		/// <summary>Twilio's transcription of the voicemail: added to the pending call as a note.</summary>
		[HttpGet(DispatcherVoiceRoutes.VoicemailTranscription)]
		[HttpPost(DispatcherVoiceRoutes.VoicemailTranscription)]
		[ValidateRequest]
		public async Task<ActionResult> VoicemailTranscription([FromQuery] string itemId)
		{
			await LoadFieldsAsync();

			var item = await CallerItemAsync(itemId);
			var text = Field("TranscriptionText");
			var status = Field("TranscriptionStatus");

			if (item == null || string.IsNullOrWhiteSpace(text) ||
				(!string.IsNullOrWhiteSpace(status) && !status.Equals("completed", StringComparison.OrdinalIgnoreCase)))
				return Twiml(new VoiceResponse());

			// Twilio does not order this callback after the record action: give the voicemail's save a moment to link its call.
			for (var wait = 0; !item.CallId.HasValue && wait < TranscriptionLinkChecks; wait++)
			{
				if (TranscriptionLinkCheckInterval > TimeSpan.Zero)
					await System.Threading.Tasks.Task.Delay(TranscriptionLinkCheckInterval);

				item = await AnyItemAsync(item.DispatcherQueueItemId) ?? item;
			}

			if (!item.CallId.HasValue)
			{
				// Still no call (its save failed, or it could not be linked): the transcript stays with the item's history rather than
				// being dropped.
				Logging.LogInfo($"[Dispatcher desk] Voicemail transcription for item {itemId} arrived before its pending call was linked; kept on the item.");
				await RecordVoiceEventAsync(item, DispatcherQueueEventTypes.VoiceEnded, null, $"Voicemail transcription (automatic): {text.Trim()}");
				return Twiml(new VoiceResponse());
			}

			try
			{
				var department = await _departmentsService.GetDepartmentByIdAsync(item.DepartmentId);
				await _callsService.SaveCallNoteAsync(new CallNote
				{
					CallId = item.CallId.Value,
					UserId = department?.ManagingUserId,
					Note = $"Voicemail transcription (automatic): {text.Trim()}",
					Timestamp = DateTime.UtcNow
				}, CancellationToken.None);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Dispatcher desk: the voicemail transcription for call {item.CallId} could not be saved.");
			}

			return Twiml(new VoiceResponse());
		}

		/// <summary>
		/// The <c>&lt;Queue url&gt;</c> of the dispatcher's dial, fetched on the held caller's leg the moment they are bridged: the
		/// connection is recorded and the caller hears that a dispatcher is on the line.
		/// </summary>
		[HttpGet(DispatcherVoiceRoutes.Bridge)]
		[HttpPost(DispatcherVoiceRoutes.Bridge)]
		[ValidateRequest]
		public async Task<ActionResult> Bridge([FromQuery] string itemId, [FromQuery] string userId)
		{
			await LoadFieldsAsync();

			var response = new VoiceResponse();
			var item = await VoiceItemAsync(itemId);

			if (item != null)
			{
				await RecordVoiceEventAsync(item, DispatcherQueueEventTypes.VoiceConnected, userId, "Dispatcher connected to the caller");
				await AppendPromptAsync(response, DispatcherDeskVoicePrompts.ConnectingCaller, item.DepartmentId);
			}

			return Twiml(response);
		}

		#endregion The held caller

		#region The dispatcher's leg

		/// <summary>
		/// The dispatcher's phone answered for Answer: "Resgrid dispatch call from …. Press 1 to connect." No key (their voicemail,
		/// or they hung up) goes to the action as a decline.
		/// </summary>
		[HttpGet(DispatcherVoiceRoutes.DispatcherConnect)]
		[HttpPost(DispatcherVoiceRoutes.DispatcherConnect)]
		[ValidateRequest]
		public async Task<ActionResult> DispatcherConnect([FromQuery] string itemId, [FromQuery] string userId)
		{
			await LoadFieldsAsync();

			var response = new VoiceResponse();
			var item = await VoiceItemAsync(itemId);

			if (item == null || !item.IsOpen() || string.IsNullOrWhiteSpace(userId) || !CanTake(item, userId))
				return await HangUpWithAsync(response, DispatcherDeskVoicePrompts.NotWaitingForYou, item?.DepartmentId);

			if (!await IsCallerWaitingAsync(item))
				return await HangUpWithAsync(response, DispatcherDeskVoicePrompts.CallerGone, item.DepartmentId);

			var gather = new Gather(numDigits: 1, action: new Uri(DispatcherVoiceRoutes.Url(DispatcherVoiceRoutes.DispatcherConnectAction, itemId, userId)),
				method: "POST", timeout: DispatcherGatherSeconds)
			{
				ActionOnEmptyResult = true
			};

			var prompt = DispatcherDeskVoicePrompts.DispatcherConnect(DispatcherDeskVoicePrompts.CallerFor(item.SourceName, item.SourceAddress));
			await AppendPromptAsync(gather, prompt, item.DepartmentId);
			gather.Pause(1);
			await AppendPromptAsync(gather, prompt, item.DepartmentId);
			response.Append(gather);

			// Only reached if Twilio falls through the gather: treated as no key.
			response.Redirect(new Uri(DispatcherVoiceRoutes.Url(DispatcherVoiceRoutes.DispatcherConnectAction, itemId, userId)), "POST");

			return Twiml(response);
		}

		/// <summary>
		/// The dispatcher's key: 1 takes the item (if it was only offered) and dials the caller's queue; anything else returns the
		/// item to the queue for another dispatcher.
		/// </summary>
		[HttpGet(DispatcherVoiceRoutes.DispatcherConnectAction)]
		[HttpPost(DispatcherVoiceRoutes.DispatcherConnectAction)]
		[ValidateRequest]
		public async Task<ActionResult> DispatcherConnectAction([FromQuery] string itemId, [FromQuery] string userId)
		{
			await LoadFieldsAsync();

			var response = new VoiceResponse();
			var item = await VoiceItemAsync(itemId);

			if (item == null || !item.IsOpen() || string.IsNullOrWhiteSpace(userId) || !CanTake(item, userId))
				return await HangUpWithAsync(response, DispatcherDeskVoicePrompts.NotWaitingForYou, item?.DepartmentId);

			if (!string.Equals((Field("Digits") ?? string.Empty).Trim(), "1", StringComparison.Ordinal))
			{
				await ReleaseAsync(item, userId, "Dispatcher did not press 1");
				return await HangUpWithAsync(response, DispatcherDeskVoicePrompts.ReturnedToQueue, item.DepartmentId);
			}

			if (!await IsCallerWaitingAsync(item))
				return await HangUpWithAsync(response, DispatcherDeskVoicePrompts.CallerGone, item.DepartmentId);

			if (item.State != (int)DispatcherQueueItemStates.Accepted)
			{
				// Answering an offer (or the shared queue) from the phone takes the item.
				var accepted = await _dispatcherRoutingService.AcceptAsync(item.DepartmentId, item.DispatcherQueueItemId, userId, CancellationToken.None);
				if (accepted == null || !accepted.Success)
					return await HangUpWithAsync(response, DispatcherDeskVoicePrompts.NotWaitingForYou, item.DepartmentId);

				item = accepted.Value ?? item;
			}

			var dial = new Dial();
			dial.Queue(_dispatcherVoiceService.QueueNameFor(item),
				url: new Uri(DispatcherVoiceRoutes.Url(DispatcherVoiceRoutes.Bridge, itemId, userId)), method: "POST");
			response.Append(dial);
			response.Hangup();

			return Twiml(response);
		}

		/// <summary>The dispatcher's phone answered for Call back: "Resgrid dispatch call back to …. Press 1 to call them now."</summary>
		[HttpGet(DispatcherVoiceRoutes.CallBack)]
		[HttpPost(DispatcherVoiceRoutes.CallBack)]
		[ValidateRequest]
		public async Task<ActionResult> CallBack([FromQuery] string itemId, [FromQuery] string userId)
		{
			await LoadFieldsAsync();

			var response = new VoiceResponse();
			var item = await AnyItemAsync(itemId);

			if (item == null || !item.IsOpen() || string.IsNullOrWhiteSpace(userId) || !CanTake(item, userId))
				return await HangUpWithAsync(response, DispatcherDeskVoicePrompts.NotWaitingForYou, item?.DepartmentId);

			if (string.IsNullOrWhiteSpace(DeskPhoneNumbers.DigitsOnly(item.SourceAddress)))
				return await HangUpWithAsync(response, DispatcherDeskVoicePrompts.NoCallBackNumber, item.DepartmentId);

			var gather = new Gather(numDigits: 1, action: new Uri(DispatcherVoiceRoutes.Url(DispatcherVoiceRoutes.CallBackAction, itemId, userId)),
				method: "POST", timeout: DispatcherGatherSeconds)
			{
				ActionOnEmptyResult = true
			};

			var prompt = DispatcherDeskVoicePrompts.CallBack(DispatcherDeskVoicePrompts.CallerFor(item.SourceName, item.SourceAddress));
			await AppendPromptAsync(gather, prompt, item.DepartmentId);
			gather.Pause(1);
			await AppendPromptAsync(gather, prompt, item.DepartmentId);
			response.Append(gather);
			response.Redirect(new Uri(DispatcherVoiceRoutes.Url(DispatcherVoiceRoutes.CallBackAction, itemId, userId)), "POST");

			return Twiml(response);
		}

		/// <summary>
		/// The dispatcher's key for Call back: 1 dials the caller from the number they called (the department's number), anything
		/// else cancels. The item stays with the dispatcher either way.
		/// </summary>
		[HttpGet(DispatcherVoiceRoutes.CallBackAction)]
		[HttpPost(DispatcherVoiceRoutes.CallBackAction)]
		[ValidateRequest]
		public async Task<ActionResult> CallBackAction([FromQuery] string itemId, [FromQuery] string userId)
		{
			await LoadFieldsAsync();

			var response = new VoiceResponse();
			var item = await AnyItemAsync(itemId);

			if (item == null || !item.IsOpen() || string.IsNullOrWhiteSpace(userId) || !CanTake(item, userId))
				return await HangUpWithAsync(response, DispatcherDeskVoicePrompts.NotWaitingForYou, item?.DepartmentId);

			if (!string.Equals((Field("Digits") ?? string.Empty).Trim(), "1", StringComparison.Ordinal))
			{
				await RecordVoiceEventAsync(item, DispatcherQueueEventTypes.VoiceEnded, userId, "Call back cancelled");
				return await HangUpWithAsync(response, DispatcherDeskVoicePrompts.CallBackCancelled, item.DepartmentId);
			}

			var callee = DeskPhoneNumbers.ToE164(item.SourceAddress);
			if (string.IsNullOrWhiteSpace(DeskPhoneNumbers.DigitsOnly(callee)))
				return await HangUpWithAsync(response, DispatcherDeskVoicePrompts.NoCallBackNumber, item.DepartmentId);

			await RecordVoiceEventAsync(item, DispatcherQueueEventTypes.VoiceConnecting, userId, "Calling the caller back");

			var dial = new Dial(action: new Uri(DispatcherVoiceRoutes.Url(DispatcherVoiceRoutes.CallBackEnded, itemId, userId)), method: "POST",
				timeout: CallBackRingSeconds, callerId: await DepartmentNumberAsync(item));
			dial.Number(callee);
			response.Append(dial);

			return Twiml(response);
		}

		/// <summary>How the call back to the caller ended (Twilio's DialCallStatus).</summary>
		[HttpGet(DispatcherVoiceRoutes.CallBackEnded)]
		[HttpPost(DispatcherVoiceRoutes.CallBackEnded)]
		[ValidateRequest]
		public async Task<ActionResult> CallBackEnded([FromQuery] string itemId, [FromQuery] string userId)
		{
			await LoadFieldsAsync();

			var item = await AnyItemAsync(itemId);
			var status = (Field("DialCallStatus") ?? string.Empty).Trim().ToLowerInvariant();

			if (status == "completed" || status == "answered")
			{
				var seconds = ParseInt(Field("DialCallDuration"));
				await RecordVoiceEventAsync(item, DispatcherQueueEventTypes.VoiceConnected, userId,
					seconds.HasValue ? $"Call back answered ({seconds.Value} seconds)" : "Call back answered");
			}
			else
			{
				await RecordVoiceEventAsync(item, DispatcherQueueEventTypes.VoiceEnded, userId,
					$"Call back not answered ({(string.IsNullOrWhiteSpace(status) ? "unknown" : status)})");
			}

			var response = new VoiceResponse();
			response.Hangup();
			return Twiml(response);
		}

		/// <summary>
		/// Status callback of a leg the desk placed (<see cref="DispatcherVoiceRoutes.Legs"/>; the voice service sends
		/// <c>leg=dispatcher</c> for both). A dispatcher's phone that was busy, did not answer or failed, or that hung up before
		/// connecting, returns the item to the queue only while a held caller is still waiting for it (Answer); a call back leg
		/// (an abandoned caller, or a text item) only records what happened and the dispatcher keeps the item. <paramref name="attempt"/>
		/// (UTC ticks the voice service took before placing the leg) ties the callback to its ring: a late status of an earlier ring,
		/// once the dispatcher has been rung again, only records what happened, so it cannot return the item while the newer leg rings.
		/// </summary>
		[HttpGet(DispatcherVoiceRoutes.Status)]
		[HttpPost(DispatcherVoiceRoutes.Status)]
		[ValidateRequest]
		public async Task<ActionResult> Status([FromQuery] string itemId, [FromQuery] string userId, [FromQuery] string leg, [FromQuery] long? attempt = null)
		{
			await LoadFieldsAsync();

			var item = await AnyItemAsync(itemId);
			var status = (Field("CallStatus") ?? string.Empty).Trim().ToLowerInvariant();

			if (item == null || string.IsNullOrWhiteSpace(userId))
				return Twiml(new VoiceResponse());

			var unanswered = status == "busy" || status == "no-answer" || status == "failed" || status == "canceled";
			if (!unanswered && status != "completed")
				return Twiml(new VoiceResponse());

			// An Answer leg is one for a voice item whose held caller is still on the line; anything else is a call back.
			var events = item.Kind == (int)DispatcherQueueItemKinds.Voice && !string.IsNullOrWhiteSpace(item.ExternalReference) &&
						 !string.Equals(leg, DispatcherVoiceRoutes.Legs.CallBack, StringComparison.OrdinalIgnoreCase)
				? (await _dispatcherRoutingService.GetItemDetailAsync(item.DepartmentId, item.DispatcherQueueItemId))?.Events
				: null;
			var attemptStartedOn = AttemptStartedOn(attempt);
			var superseded = events != null && attemptStartedOn.HasValue && NewerAttemptStarted(events, userId, attemptStartedOn.Value);
			var answerLeg = events != null && CallerWaiting(events) && !superseded;

			if (unanswered)
			{
				await RecordVoiceEventAsync(item, DispatcherQueueEventTypes.VoiceEnded, userId,
					superseded ? $"Dispatcher's phone: {status} (an earlier ring)" : $"Dispatcher's phone: {status}");

				if (answerLeg)
					await ReleaseIfHeldAsync(item, userId, null);
			}
			else if (answerLeg && !ConnectedSinceLastAttempt(events, userId))
			{
				// Answered, then hung up without connecting (pressing 1 records the bridge; another key already released it).
				await ReleaseIfHeldAsync(item, userId, "Dispatcher hung up before connecting");
			}
			else
			{
				await RecordVoiceEventAsync(item, DispatcherQueueEventTypes.VoiceEnded, userId, "Dispatcher's phone call ended");
			}

			return Twiml(new VoiceResponse());
		}

		#endregion The dispatcher's leg

		#region Helpers

		/// <summary>The decision of <see cref="Hold"/>: whether the held caller must leave the queue for the overflow handling now.</summary>
		private async Task<bool> LeaveHoldAsync(DispatcherQueueItem item, bool recheck = true)
		{
			if (item.State == (int)DispatcherQueueItemStates.Overflowed)
				return true;

			// Closed without the caller being connected: take a message rather than hang up on them.
			if (!item.IsOpen())
				return true;

			var heldSeconds = item.CreatedOn > DateTime.MinValue ? (DateTime.UtcNow - item.CreatedOn).TotalSeconds : ParseInt(Field("QueueTime")) ?? 0;

			int limit = MaxHoldSeconds;
			if (item.State == (int)DispatcherQueueItemStates.Queued)
			{
				// Nobody has it: the department's hold time (0 = overflow at once when nobody is free).
				var config = await _dispatcherDeskConfigService.GetConfigAsync(item.DepartmentId);
				limit = Math.Min(config?.EffectiveVoiceHoldSeconds() ?? 0, MaxHoldSeconds);
			}

			if (heldSeconds < limit)
				return false;

			// The limit belongs to the state read above. A dispatcher who took the caller (or was offered them) while the settings
			// loaded changes the item; its own limit decides then, so a caller being answered is not sent to voicemail.
			if (recheck)
			{
				var current = await _dispatcherQueueItemsRepository.GetItemAsync(item.DispatcherQueueItemId);
				if (current != null && (current.Version != item.Version || current.State != item.State))
					return await LeaveHoldAsync(current, recheck: false);
			}

			var note = item.State == (int)DispatcherQueueItemStates.Queued
				? $"No dispatcher free after {(int)heldSeconds} seconds on hold: voicemail"
				: $"Not connected after {(int)heldSeconds} seconds on hold: voicemail";

			// The overflow is written only for the version the limit was measured against. A dispatcher who takes the caller between
			// the re-read above and this write makes it fail; the caller then stays on hold and the next hold poll decides on the item
			// as it is by then (a closed item still leaves for the message).
			if (await TryMarkOverflowedAsync(item, note))
				return true;

			var latest = await _dispatcherQueueItemsRepository.GetItemAsync(item.DispatcherQueueItemId);
			return latest == null || !latest.IsOpen();
		}

		private async Task<ActionResult> OverflowTwimlAsync(DispatcherQueueItem item)
		{
			var response = new VoiceResponse();

			// DispatcherVoiceOverflowModes.EnhancedAiIntake falls back to the voicemail until voice intake is in this codebase.
			await AppendPromptAsync(response, DispatcherDeskVoicePrompts.VoicemailIntro, item.DepartmentId);
			response.Record(new Record(
				action: new Uri(DispatcherVoiceRoutes.Url(DispatcherVoiceRoutes.Voicemail, item.DispatcherQueueItemId)),
				method: "POST",
				timeout: 5,
				finishOnKey: "#",
				maxLength: VoicemailMaxSeconds,
				playBeep: true,
				transcribe: true,
				transcribeCallback: new Uri(DispatcherVoiceRoutes.Url(DispatcherVoiceRoutes.VoicemailTranscription, item.DispatcherQueueItemId))));

			// Twilio continues here when nothing was recorded.
			await AppendPromptAsync(response, DispatcherDeskVoicePrompts.VoicemailNotReceived, item.DepartmentId);
			response.Hangup();

			return Twiml(response);
		}

		private async Task<ActionResult> NoDispatcherAvailableAsync(int? departmentId)
		{
			return await HangUpWithAsync(new VoiceResponse(), DispatcherDeskVoicePrompts.NoDispatcherAvailable, departmentId);
		}

		private async Task<ActionResult> HangUpWithAsync(VoiceResponse response, string prompt, int? departmentId)
		{
			await AppendPromptAsync(response, prompt, departmentId);
			response.Hangup();
			return Twiml(response);
		}

		/// <summary>The held caller's voice item; null when it is not one, or the request is for another call.</summary>
		private async Task<DispatcherQueueItem> CallerItemAsync(string itemId)
		{
			var item = await VoiceItemAsync(itemId);
			if (item == null)
				return null;

			var callSid = Field("CallSid");
			if (!string.IsNullOrWhiteSpace(item.ExternalReference) && !string.IsNullOrWhiteSpace(callSid) &&
				!string.Equals(item.ExternalReference, callSid, StringComparison.OrdinalIgnoreCase))
			{
				Logging.LogInfo($"[Dispatcher desk] Voice webhook for item {itemId} came from another call; ignored.");
				return null;
			}

			return item;
		}

		private async Task<DispatcherQueueItem> VoiceItemAsync(string itemId)
		{
			var item = await AnyItemAsync(itemId);
			return item != null && item.Kind == (int)DispatcherQueueItemKinds.Voice ? item : null;
		}

		/// <summary>Any desk item: Call back works for every item with a phone number (a voice caller who hung up, a text).</summary>
		private async Task<DispatcherQueueItem> AnyItemAsync(string itemId)
		{
			if (string.IsNullOrWhiteSpace(itemId))
				return null;

			return await _dispatcherQueueItemsRepository.GetItemAsync(itemId);
		}

		/// <summary>The dispatcher holds the item, it is offered to them, or it waits in the shared queue for anyone.</summary>
		private static bool CanTake(DispatcherQueueItem item, string userId)
		{
			if (item.State == (int)DispatcherQueueItemStates.Queued)
				return true;

			return string.Equals(item.AssignedUserId, userId, StringComparison.OrdinalIgnoreCase);
		}

		private async Task<bool> IsCallerWaitingAsync(DispatcherQueueItem item)
		{
			if (string.IsNullOrWhiteSpace(item.ExternalReference))
				return false;

			var detail = await _dispatcherRoutingService.GetItemDetailAsync(item.DepartmentId, item.DispatcherQueueItemId);
			return CallerWaiting(detail?.Events);
		}

		/// <summary>No VoiceEnded on the caller's own leg (the convention: caller-leg events carry no user).</summary>
		public static bool CallerWaiting(IEnumerable<DispatcherQueueEvent> events)
		{
			return !(events ?? Enumerable.Empty<DispatcherQueueEvent>())
				.Any(x => x.EventType == (int)DispatcherQueueEventTypes.VoiceEnded && string.IsNullOrWhiteSpace(x.UserId));
		}

		/// <summary>The dispatcher was bridged to the caller after the desk last rang them.</summary>
		public static bool ConnectedSinceLastAttempt(IEnumerable<DispatcherQueueEvent> events, string userId)
		{
			var mine = (events ?? Enumerable.Empty<DispatcherQueueEvent>())
				.Where(x => string.Equals(x.UserId, userId, StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.OccurredOn).ToList();
			var lastAttempt = mine.LastOrDefault(x => x.EventType == (int)DispatcherQueueEventTypes.VoiceConnecting);

			return mine.Any(x => x.EventType == (int)DispatcherQueueEventTypes.VoiceConnected && (lastAttempt == null || x.OccurredOn >= lastAttempt.OccurredOn));
		}

		/// <summary>The ring attempt's start from the status callback's <c>attempt</c> (UTC ticks); null for a leg placed without one.</summary>
		public static DateTime? AttemptStartedOn(long? ticks) =>
			ticks.HasValue && ticks.Value > 0 && ticks.Value <= DateTime.MaxValue.Ticks ? new DateTime(ticks.Value, DateTimeKind.Utc) : (DateTime?)null;

		/// <summary>
		/// The dispatcher was rung again after the attempt that started at <paramref name="attemptStartedOn"/>: every placed leg records
		/// one VoiceConnecting for its dispatcher after it is placed, so a second one since then belongs to a later ring.
		/// </summary>
		public static bool NewerAttemptStarted(IEnumerable<DispatcherQueueEvent> events, string userId, DateTime attemptStartedOn)
		{
			return (events ?? Enumerable.Empty<DispatcherQueueEvent>())
				.Count(x => x.EventType == (int)DispatcherQueueEventTypes.VoiceConnecting && string.Equals(x.UserId, userId, StringComparison.OrdinalIgnoreCase) &&
							x.OccurredOn >= attemptStartedOn) > 1;
		}

		private async System.Threading.Tasks.Task ReleaseAsync(DispatcherQueueItem item, string userId, string detail)
		{
			try
			{
				if (item.State == (int)DispatcherQueueItemStates.Accepted && string.Equals(item.AssignedUserId, userId, StringComparison.OrdinalIgnoreCase))
					await _dispatcherRoutingService.ReleaseAsync(item.DepartmentId, item.DispatcherQueueItemId, userId, CancellationToken.None);
				else if (item.State == (int)DispatcherQueueItemStates.Offered && string.Equals(item.AssignedUserId, userId, StringComparison.OrdinalIgnoreCase))
					await _dispatcherRoutingService.DeclineAsync(item.DepartmentId, item.DispatcherQueueItemId, userId, detail, CancellationToken.None);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}

			if (!string.IsNullOrWhiteSpace(detail))
				await RecordVoiceEventAsync(item, DispatcherQueueEventTypes.VoiceEnded, userId, detail);
		}

		private async System.Threading.Tasks.Task ReleaseIfHeldAsync(DispatcherQueueItem item, string userId, string detail)
		{
			var fresh = await _dispatcherQueueItemsRepository.GetItemAsync(item.DispatcherQueueItemId) ?? item;
			if (fresh.IsOpen())
				await ReleaseAsync(fresh, userId, detail);
		}

		private async System.Threading.Tasks.Task MarkOverflowedAsync(DispatcherQueueItem item, string note)
		{
			if (item == null || !item.IsOpen())
				return;

			try
			{
				await _dispatcherRoutingService.MarkOverflowedAsync(item, note, CancellationToken.None);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
		}

		/// <summary>
		/// <see cref="MarkOverflowedAsync"/> only while the item still has the version and state the caller read: false when another
		/// write (a dispatcher accepting the caller) got there first. A failed write is logged and counts as overflowed, as before, so
		/// a caller is never left on hold because the desk store is unreachable.
		/// </summary>
		private async Task<bool> TryMarkOverflowedAsync(DispatcherQueueItem item, string note)
		{
			if (item == null || !item.IsOpen())
				return true;

			try
			{
				return await _dispatcherRoutingService.TryMarkOverflowedAsync(item, note, CancellationToken.None);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return true;
			}
		}

		private async System.Threading.Tasks.Task RecordVoiceEventAsync(DispatcherQueueItem item, DispatcherQueueEventTypes eventType, string userId, string detail)
		{
			if (item == null)
				return;

			try
			{
				await _dispatcherRoutingService.RecordVoiceEventAsync(item, eventType, string.IsNullOrWhiteSpace(userId) ? null : userId, detail,
					CancellationToken.None);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}
		}

		/// <summary>The caller id for a call back: the number the caller called, else the department's number, else the master number.</summary>
		private async Task<string> DepartmentNumberAsync(DispatcherQueueItem item)
		{
			if (!string.IsNullOrWhiteSpace(DeskPhoneNumbers.DigitsOnly(item.TargetAddress)))
				return DeskPhoneNumbers.ToE164(item.TargetAddress);

			var departmentNumber = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(item.DepartmentId);
			if (!string.IsNullOrWhiteSpace(DeskPhoneNumbers.DigitsOnly(departmentNumber)))
				return DeskPhoneNumbers.ToE164(departmentNumber);

			return Config.NumberProviderConfig.TwilioResgridNumber;
		}

		private async System.Threading.Tasks.Task AppendPromptAsync(VoiceResponse response, string text, int? departmentId)
		{
			var language = await TtsLanguageAsync(departmentId);
			try
			{
				await _twilioVoiceResponseService.AppendPromptAsync(response, text, TtsBudgetToken(), language);
			}
			catch (OperationCanceledException) when (TtsBudgetExpired)
			{
				_twilioVoiceResponseService.AppendSayFallback(response, text);
			}
		}

		private async System.Threading.Tasks.Task AppendPromptAsync(Gather gather, string text, int? departmentId)
		{
			var language = await TtsLanguageAsync(departmentId);
			try
			{
				await _twilioVoiceResponseService.AppendPromptAsync(gather, text, TtsBudgetToken(), language);
			}
			catch (OperationCanceledException) when (TtsBudgetExpired)
			{
				_twilioVoiceResponseService.AppendSayFallback(gather, text);
			}
		}

		private string _ttsLanguage;
		private int? _ttsLanguageDepartmentId;

		private async Task<string> TtsLanguageAsync(int? departmentId)
		{
			if (!departmentId.HasValue || departmentId.Value <= 0)
				return null;

			if (_ttsLanguageDepartmentId != departmentId)
			{
				_ttsLanguage = await _departmentSettingsService.GetTtsLanguageForDepartmentAsync(departmentId.Value);
				_ttsLanguageDepartmentId = departmentId;
			}

			return _ttsLanguage;
		}

		// One TTS time budget for every prompt in a request (as TwilioController): a hung TTS service must not outlast Twilio's
		// 15 second webhook limit; once it expires the remaining prompts fall back to <Say> (when that fallback is enabled).
		private CancellationToken TtsBudgetToken()
		{
			if (_ttsBudgetCts == null)
			{
				_ttsBudgetCts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext?.RequestAborted ?? CancellationToken.None);
				_ttsBudgetCts.CancelAfter(TtsPromptBudget);
				HttpContext?.Response.RegisterForDispose(_ttsBudgetCts);
			}

			return _ttsBudgetCts.Token;
		}

		private bool TtsBudgetExpired =>
			_ttsBudgetCts != null && _ttsBudgetCts.IsCancellationRequested && !(HttpContext?.RequestAborted.IsCancellationRequested ?? false);

		/// <summary>Twilio posts its parameters as a form (or, for GET webhooks, in the query string).</summary>
		private async System.Threading.Tasks.Task LoadFieldsAsync()
		{
			_fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			if (Request == null)
				return;

			foreach (var pair in Request.Query)
				_fields[pair.Key] = pair.Value.ToString();

			if (Request.HasFormContentType)
			{
				var form = await Request.ReadFormAsync();
				foreach (var pair in form)
					_fields[pair.Key] = pair.Value.ToString();
			}
		}

		private string Field(string name) => _fields != null && _fields.TryGetValue(name, out var value) ? value : null;

		private static int? ParseInt(string value) =>
			int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : (int?)null;

		private static ContentResult Twiml(VoiceResponse response)
		{
			return new ContentResult { Content = response.ToString(), ContentType = "application/xml", StatusCode = StatusCodes.Status200OK };
		}

		#endregion Helpers
	}
}
